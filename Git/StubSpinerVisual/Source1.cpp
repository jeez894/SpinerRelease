using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Unity.Netcode;
using GameNetcodeStuff;
using LethalLib.Modules;

namespace Spiner
{
    public class SpinerAI : EnemyAI, INoiseListener
    {
        public AudioClip ? attackSound; // Nullable
        public AudioClip ? moveSound; // Nullable
        private bool isPlayingMoveSound = false;
        private new int spinerBehaviourStateIndex;
        private Vector3 ? lastHeardNoisePosition = null;
        private bool inJugementAnimation = false;
        private float hearNoiseCooldown;
        private float alertDistance = 10f;
        private float attackRange = 2f;
        private int currentPatrolIndex = 0;
        private System.Random randomGenerator;
        private Vector3 lastPosition;
        private Coroutine alertCoroutine;
        private Coroutine attackCoroutine;
        private bool isAlertActive = false;
        private bool isAttacking = false;
        private bool isPatrolling = false;
        private State previousState = State.Patrol;
        private bool newNoiseDetected = false;
        private float lastNoiseTime = 0f;


        public override void Start()
        {
            base.Start();
            SpinerPlugin.Logger.LogInfo("[Spiner] Starting AI for " + gameObject.name);
            SpinerPlugin.Logger.LogInfo($"allAINodes count: {allAINodes?.Length ?? 0}");

            if (!agent.isOnNavMesh)
            {
                SpinerPlugin.Logger.LogError("[Spiner] NavMeshAgent is not on a NavMesh!");
            }
            else
            {
                SpinerPlugin.Logger.LogInfo("[Spiner] NavMeshAgent is on a valid NavMesh.");
            }



            // Vérification des références critiques
            LogCriticalReferences();

            // Initialize random generator for behaviors
            randomGenerator = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);

            // Set initial behavior state
            spinerBehaviourStateIndex = (int)State.Patrol;
            SpinerPlugin.Logger.LogInfo("[Spiner] Initial state set to Patrol.");
        }

        public override void Update()
        {
            base.Update();

            if (isEnemyDead)
                return;

            switch ((State)spinerBehaviourStateIndex)
            {
            case State.Patrol:
                if (previousState != State.Patrol) // ? Évite de lancer StartPatrol() en boucle
                {
                    previousState = State.Patrol;
                }
                break;

            case State.Alert:
                if (previousState != State.Alert) // ? Transition unique
                {
                    previousState = State.Alert;
                }
                break;

            case State.Attack:
                if (previousState != State.Attack) // ? Transition unique
                {
                    previousState = State.Attack;
                }
                break;
            }
        }

        public override void DoAIInterval()
        {
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
                return;

            // On vérifie l'état avant d'appeler base.DoAIInterval()
            if ((State)spinerBehaviourStateIndex == State.Attack)
            {
                // -- État ATTACK --
                // 1) Pas de base.DoAIInterval() pour empêcher le SetDestination automatique
                // 2) Mais on veut quand même la synchro réseau ? 
                //    => Appelle juste la méthode de synchro si tu y as accès
                this.SyncPositionToClients();
            }
            else
            {
                // -- État PATROL / ALERT --
                // On laisse la logique parente faire son SetDestination, etc.
                base.DoAIInterval();
            }

            // Maintenant, on gère la logique de ton IA
            switch ((State)spinerBehaviourStateIndex)
            {
            case State.Patrol:
                if (!isPatrolling)
                    StartPatrol();
                break;

            case State.Alert:
                if (!isAlertActive)
                    StartAlert(lastHeardNoisePosition ? ? transform.position);
                break;

            case State.Attack:
                if (!isAttacking)
                    StartAttack();
                break;
            }
        }


        // ? Lance la patrouille (équivalent StartSearch)
        public void StartPatrol()
        {
            StopPatrol(); // ?? Assurer qu'on ne superpose pas plusieurs patrouilles

            SpinerPlugin.Logger.LogInfo("[Spiner] ?? Start Patrol");
            isPatrolling = true;
            agent.speed = 1f;

            // ?? Démarrer la recherche avec `StartSearch()`
            StartSearch(transform.position);
        }

        // ? Arrête la patrouille proprement
        public void StopPatrol()
        {
            if (!isPatrolling) return;

            SpinerPlugin.Logger.LogInfo("[Spiner] ?? Stop Patrol");
            StopSearch(currentSearch, true);
            isPatrolling = false;
        }

        // ? Lancer une alerte proprement
        public void StartAlert(Vector3 noisePosition)
        {
            if (isAlertActive) return; // ?? Évite de relancer l'alerte en boucle

            SpinerPlugin.Logger.LogInfo("[Spiner] ?? Start Alert");
            isAlertActive = true;

            StopPatrol(); // ?? Stopper la patrouille
            alertCoroutine = StartCoroutine(AlertCoroutine());
        }

        // ? Arrêter proprement l’alerte
        public void StopAlert()
        {
            if (!isAlertActive) return;

            SpinerPlugin.Logger.LogInfo("[Spiner] ?? Stop Alert");
            if (alertCoroutine != null) StopCoroutine(alertCoroutine);

            isAlertActive = false;
        }

        // ? Lance l'attaque
        public void StartAttack()
        {
            if (isAttacking) return; // ?? Empêcher plusieurs attaques simultanées

            SpinerPlugin.Logger.LogInfo("[Spiner] ?? Start Attack");
            isAttacking = true;

            StopAlert(); // ?? Arrêter l’alerte si on passe en attaque


            // ?? Lancer la coroutine d’attaque
            attackCoroutine = StartCoroutine(AttackCoroutine());
        }

        // ? Arrête l’attaque proprement
        public void StopAttack()
        {
            if (!isAttacking) return;

            SpinerPlugin.Logger.LogInfo("[Spiner] ?? Stop Attack");
            if (attackCoroutine != null) StopCoroutine(attackCoroutine);

            isAttacking = false;
        }

        // ? Nouvelle gestion unifiée de l'alerte et de la détection de bruit
        private IEnumerator AlertCoroutine()
        {
            SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Alert started at {lastHeardNoisePosition}");

            float alertDuration = 5f;
            float startTime = Time.time;
            float rotationSpeed = 90f;
            float timeInSight = 0f;

            agent.speed = 0f; // ? L'ennemi s'arrête pour observer

            while (Time.time - startTime < alertDuration)
            {
                SpinerPlugin.Logger.LogInfo($"[Spiner] ? Alert ongoing... Time left: {alertDuration - (Time.time - startTime):F2}s");

                // ?? Tourne vers le dernier bruit entendu
                if (lastHeardNoisePosition.HasValue)
                {
                    Vector3 directionToNoise = (lastHeardNoisePosition.Value - transform.position).normalized;
                    Quaternion targetRotation = Quaternion.LookRotation(new Vector3(directionToNoise.x, 0, directionToNoise.z));

                    float angleDifference = Quaternion.Angle(transform.rotation, targetRotation);
                    if (angleDifference > 0.5f)
                    {
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                    }
                }

                // ?? Vérifie si un joueur est en vue
                PlayerControllerB detectedPlayer = CheckLineOfSightForClosestPlayer();
                if (detectedPlayer != null)
                {
                    timeInSight += Time.deltaTime;
                    if (timeInSight >= 1.5f)
                    {
                        SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Player {detectedPlayer.playerUsername} CONFIRMED. SWITCHING TO ATTACK.");
                        targetPlayer = detectedPlayer;
                        spinerBehaviourStateIndex = (int)State.Attack; // ?? On change l’état ici !
                        StopAlert();
                        yield break;
                    }
                }
                else
                {
                    timeInSight = Mathf.Max(0f, timeInSight - Time.deltaTime * 0.5f);
                }

                yield return null; // ?? Attente frame suivante
            }
            spinerBehaviourStateIndex = (int)State.Patrol; // ?? On change l’état ici !
            StopAlert();
            SpinerPlugin.Logger.LogInfo("[Spiner] ?? ALERT ENDED. Returning to Patrol.");
        }

        private IEnumerator AttackCoroutine()
        {
            SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Attack started!");

            if (targetPlayer == null)
            {
                StopAttack();
                SpinerPlugin.Logger.LogInfo("[Spiner] ? No player to attack.");
                spinerBehaviourStateIndex = (int)State.Patrol;
                yield break;
            }

            // ?? Capture **une seule fois** la position actuelle du joueur
            Vector3 finalAttackPosition = targetPlayer.transform.position;

            SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Charging towards {finalAttackPosition}");

            // ?? Désactiver tout mouvement en cours et lancer l’attaque
            agent.ResetPath();
            agent.speed = 5f;
            agent.SetDestination(finalAttackPosition);

            float attackDuration = 3f;
            float startTime = Time.time;

            while (Time.time - startTime < attackDuration)
            {
                if (spinerBehaviourStateIndex != (int)State.Attack)
                {
                    StopAttack();
                    SpinerPlugin.Logger.LogInfo("[Spiner] ?? Attack interrupted (State changed)");
                    yield break;
                }

                float distanceToTarget = Vector3.Distance(transform.position, finalAttackPosition);
                Vector3 currentNavDestination = agent.destination; // ?? Destination réelle de l’agent

                SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Distance to target: {distanceToTarget:F2}m - Remaining NavMesh distance: {agent.remainingDistance:F2}m - NavMesh Destination: {currentNavDestination}");

                if (distanceToTarget < 1f) // ?? Si l'ennemi atteint sa cible
                {
                    StopAttack();
                    SpinerPlugin.Logger.LogInfo("[Spiner] ?? Attack landed! Player hit!");
                    spinerBehaviourStateIndex = (int)State.Patrol;
                    yield break;
                }

                yield return null;
            }

            // ? Si l’attaque dure trop longtemps, elle échoue
            StopAttack();
            SpinerPlugin.Logger.LogInfo("[Spiner] ? Attack timed out.");
            spinerBehaviourStateIndex = (int)State.Patrol;
        }


        // ? Gestion améliorée des bruits (avec actualisation de l’alerte si en cours)
        public override void DetectNoise(Vector3 noisePosition, float noiseLoudness, int timesPlayedInOneSpot = 0, int noiseID = 0)
        {
            SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Noise detected at {noisePosition} with loudness {noiseLoudness}");

            // ?? 1?? Vérification : L'ennemi **n'écoute que lorsqu'il est en Patrouille** !
            if (spinerBehaviourStateIndex != (int)State.Patrol)
            {
                SpinerPlugin.Logger.LogInfo($"[Spiner] ? Ignoring noise: Enemy is in state {(State)spinerBehaviourStateIndex}.");
                return;
            }

            // ?? 2?? Vérification : **Cooldown du bruit**
            float timeSinceLastNoise = Time.time - lastNoiseTime;
            if (timeSinceLastNoise < 1f) // ?? Augmenté à 1s au lieu de 0.5s
            {
                SpinerPlugin.Logger.LogInfo($"[Spiner] ? Noise ignored due to cooldown. Time since last noise: {timeSinceLastNoise:F2}s");
                return;
            }

            // ? Met à jour le temps du dernier bruit traité **après les checks de cooldown**
            lastNoiseTime = Time.time;

            // ?? 3?? Vérification : **Éviter la détection de bruits similaires en boucle**
            if (lastHeardNoisePosition.HasValue && Vector3.Distance(lastHeardNoisePosition.Value, noisePosition) < 1f)
            {
                SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Ignoring duplicate noise (too close to last heard position).");
                return;
            }

            float effectiveRange = 18f * noiseLoudness;
            float distanceToNoise = Vector3.Distance(transform.position, noisePosition);

            // ? Si l'ennemi est **en patrouille** et que le bruit est significatif, il doit **passer en alerte**
            if (distanceToNoise < effectiveRange)
            {
                SpinerPlugin.Logger.LogInfo($"[Spiner] ?? Significant noise detected. Switching to Alert.");
                lastHeardNoisePosition = noisePosition;
                spinerBehaviourStateIndex = (int)State.Alert; // ?? On change l'état, et `DoAIInterval()` s'occupera de la transition !
            }
        }


        private void HandleMovementSound()
        {
            bool isMoving = agent.velocity.magnitude > 0.1f;
            if (isMoving && moveSound != null && creatureSFX != null && !creatureSFX.isPlaying)
            {
                creatureSFX.clip = moveSound;
                creatureSFX.loop = true;
                creatureSFX.Play();
            }
            else if (!isMoving && creatureSFX != null && creatureSFX.isPlaying)
            {
                creatureSFX.Stop();
            }
        }

        private void HandleDeath()
        {
            SpinerPlugin.Logger.LogInfo("[Spiner] Handling death for " + gameObject.name);
            agent.speed = 0f;
            creatureVoice ? .PlayOneShot(dieSFX);
        }

        private void LogCriticalReferences()
        {
            SpinerPlugin.Logger.LogInfo("[Spiner] Verifying critical references...");
            SpinerPlugin.Logger.LogInfo("- NavMeshAgent: " + (agent != null ? "Present" : "Missing"));
            SpinerPlugin.Logger.LogInfo("- Animator: " + (creatureAnimator != null ? "Present" : "Missing"));
            SpinerPlugin.Logger.LogInfo("- NetworkObject: " + (thisNetworkObject != null ? "Present" : "Missing"));
            SpinerPlugin.Logger.LogInfo("- AINodes: " + (allAINodes != null && allAINodes.Length > 0 ? $"{allAINodes.Length} nodes found" : "Missing or empty"));
            SpinerPlugin.Logger.LogInfo("- RoundManager.Instance: " + (RoundManager.Instance != null ? "Present" : "Missing"));
        }

        private enum State
        {
            Patrol = 0,
            Alert = 1,
            Attack = 2
        }
    }
}
