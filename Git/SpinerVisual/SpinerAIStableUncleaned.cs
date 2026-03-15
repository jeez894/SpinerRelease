using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Spiner
{
    public class SpinerAI : EnemyAI, INoiseListener
    {
        // ─────────────────────────────────────────────
        //  Audio sources (for inspector wiring)
        // ─────────────────────────────────────────────
        [SerializeField] private AudioSource walkAudioSource = null!;   // looped footsteps
        [SerializeField] private AudioSource sfxAudioSource = null!;    // one-shot SFX

        // ─────────────────────────────────────────────
        //  Core SFX clips
        // ─────────────────────────────────────────────
        [SerializeField] private AudioClip kidnappingSound = null!;
        [SerializeField] private AudioClip moveSound = null!;
        [SerializeField] private AudioClip creepSound = null!;
        [SerializeField] private AudioClip transportSound = null!;
        [SerializeField] private AudioClip runawaySound = null!;
        [SerializeField] private AudioClip detectionSound = null!;

        //  Death variations
        [SerializeField] private AudioClip deathSound = null!;
        [SerializeField] private AudioClip deathSound2 = null!;
        [SerializeField] private AudioClip deathSound3 = null!;

        //  Roaming variations
        [SerializeField] private AudioClip roamingSound = null!;
        [SerializeField] private AudioClip roamingSound2 = null!;
        [SerializeField] private AudioClip roamingSound3 = null!;
        [SerializeField] private AudioClip roamingSound4 = null!;
        [SerializeField] private AudioClip roamingSound5 = null!;
        [SerializeField] private AudioClip roamingSound6 = null!;
        [SerializeField] private AudioClip roamingSound7 = null!;


        // ─────────────────────────────────────────────
        //  Animator (Mecanim)
        // ─────────────────────────────────────────────

        //  Animator state / trigger names
        public string animStalk = "Stalk";
        public string animWalk = "Walk";
        public string animAttack = "Attack";
        public string animGrab = "Grab";
        public string animHurt = "Hurt";
        public string animHurt2 = "Hurt2";
        public string animHurt3 = "Hurt3";
        public string animDeath = "Death";
        public string animDeath2 = "Death2";
        public string animSpin = "Spin";
        public string animSpinTest = "Spintest";


        private int spinerBehaviourStateIndex;
        [SerializeField] public Transform kidnapCarryPoint = null!;

        private System.Random randomGenerator = new();
        private bool isRunawayActive = false;
        private bool isPatrolActive = false;
        private bool isStalkingActive = false;
        private bool isKidnappingActive = false;
        private bool isTransportActive = false;
        private State previousState = State.Patrol;
        public float[] playerStealthMeters = new float[4];
        public PlayerControllerB chasingPlayer = null!;
        private int stalkingTargetId = -1;
        private int kidnappingTargetId = -1;
        private int transportingTargetId = -1;
        private Vector3 lastLoggedDestination = Vector3.zero;
        private bool overrideSpeed = false;
        private bool enableRpcLogging = true;

        private float transportReleaseTimer = 0f;
        private const float transportReleaseDelay = 5f; // ← délai en secondes à adapter
        private bool hasReceivedDamageRecently = false;
        private bool isAvoidingThreat = false;
        private Vector3 currentAvoidPoint;
        private int hurtAnimIndex = 0;

        // ======= CFG VALUE ========

        // Cache des valeurs reçues
        private int _cfgMaxHP;
        private float _cfgRoamVolume;
        private bool _cfgDarkMode;
        private float _cfgDarkReviveDelay;
        private float _cfgDarkKillTime;
        private bool _cfgApplied;

        // dark mode runtime
        private bool _feignDeathActive;
        private float _feignDeathTimer;
        private bool _phase2Lethal;           // après “résurrection”
        private bool _killTimerActive;
        private float _kidnapKillTimer;        // timer pendant la capture (phase 2)

        // =============== STALKING METER ===============
        private float stalkingMeter = 0f;
        private float stalkingMeterMax = 5f;
        private float stalkingMeterIncrement = 1f;
        private float stalkingMeterDecrement = 0.3f;
        // Sous-état "avoid"
        private bool isCreeping = false;
        private bool isHiding = false;
        private bool isFollowingBehind = false;


        // ─────────────────────────────────────────────
        //  FX system — one adaptive entry point
        // ─────────────────────────────────────────────
        public enum SpinerEvent { None, Kidnap, Death, Creep, Detection, Transport, Spin, Roam, Runaway }
        private AudioClip _lastClip = null!;
        private float _lastTime;
        private const float Phase2Pitch = 0.5f; // pitch plus grave en phase 2
        private const float Phase2VolMult = 1f;

        private enum State
        {
            Runaway,
            Patrol,
            Stalking,
            Kidnapping,
            Transport
        }


        // application du config au spawn
        // (voir aussi applylocalconfig et le applyruntimeconfig dans client rcp)
        // variables du config dans plugin
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _feignDeathActive = false;
            _killTimerActive = false;
            _phase2Lethal = false;
            _feignDeathTimer = 0f;
            _kidnapKillTimer = 0f;

            if (IsServer)
            {
                // 1) Broadcast des valeurs LIVE
                var json = SpinerPlugin.GetRuntimeConfigJson();
                ApplyRuntimeConfigClientRpc(json);

                // 2) Application locale host avec les valeurs LIVE
                _cfgMaxHP = SpinerPlugin.MaxHP.Value;
                _cfgRoamVolume = SpinerPlugin.RoamVolume.Value;
                _cfgDarkMode = SpinerPlugin.DarkMode.Value;
                _cfgDarkReviveDelay = SpinerPlugin.DarkReviveDelay.Value;
                _cfgDarkKillTime = SpinerPlugin.DarkKillTime.Value;
                _cfgApplied = true;
                ApplyLocalConfig();
            }
            else
            {
                _cfgApplied = false; // on attend ApplyRuntimeConfigClientRpc
            }
        }

        private void ApplyLocalConfig()
        {
            // si pas encore reçu, fallback BepInEx
            if (!_cfgApplied)
            {
                _cfgMaxHP = SpinerPlugin.MaxHP.Value;
                _cfgRoamVolume = SpinerPlugin.RoamVolume.Value;
                _cfgDarkMode = SpinerPlugin.DarkMode.Value;
                _cfgDarkReviveDelay = SpinerPlugin.DarkReviveDelay.Value;
                _cfgDarkKillTime = SpinerPlugin.DarkKillTime.Value;
                _cfgApplied = true;
            }

            enemyHP = _cfgMaxHP;
            // si tu veux aussi régler le volume roam ici, garde _cfgRoamVolume pour TriggerFx(Roam)
        }

        public override void Start()
        {
            base.Start();
            //SpinerPlugin.LogInfo("[Spiner] Starting AI for " + gameObject.name);

            if (!agent.isOnNavMesh)
                //SpinerPlugin.LogError("[Spiner] NavMeshAgent is not on a NavMesh!");

            LogCriticalReferences();

            if (creatureAnimator == null)
            {
                creatureAnimator = GetComponent<Animator>();
                if (creatureAnimator == null)
                {
                    SpinerPlugin.LogError($"[Init] ❌ Animator is NULL on object {gameObject.name} ({GetInstanceID()})");
                }
                else
                {
                    SpinerPlugin.LogWarning($"[Init] ⚠ Animator was missing but assigned via GetComponent");
                }
            }


            // Initialisation
            playerStealthMeters = new float[StartOfRound.Instance.allPlayerScripts.Length];
            randomGenerator = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            spinerBehaviourStateIndex = (int)State.Patrol;
            //SpinerPlugin.LogInfo("[Spiner] Initial state set to Patrol.");
        }

        public override void Update()
        {
            if (agent.destination != lastLoggedDestination)
                lastLoggedDestination = agent.destination;

            base.Update();
            TickFeignDeath();
            if (isEnemyDead) return;

            switch ((State)spinerBehaviourStateIndex)
            {
                case State.Runaway:
                    if (previousState != State.Runaway)
                        previousState = State.Runaway;
                    break;

                case State.Patrol:
                    if (previousState != State.Patrol)
                        previousState = State.Patrol;

                    var players = GetAllAlivePlayerObjects();
                    GameObject target = CheckLineOfSight(players, 45f, 50, 3f, this.eye);

                    if (target != null)
                    {
                        var p = target.GetComponent<PlayerControllerB>();
                        int index = Array.IndexOf(StartOfRound.Instance.allPlayerScripts, p);
                        if (index >= 0)
                        {
                            SpinerPlugin.LogInfo($"[Update] ▶ Target acquired during patrol: {target.name}");

                            // 👉 Côté serveur : on notifie les clients
                            if (IsServer)
                            {
                                BeginSpinerStalkClientRpc(index);
                            }
                            // 👉 Côté client : on demande au serveur de lancer le stalk
                            else
                            {
                                BeginSpinerStalkServerRpc(index);
                            }
                        }
                    }
                    else
                    {
                        //SpinerPlugin.LogInfo("[Update] ❌ No player detected during patrol");
                    }
                    break;

                case State.Stalking:
                    if (previousState != State.Stalking)
                        previousState = State.Stalking;
                    break;

                case State.Kidnapping:
                    if (previousState != State.Kidnapping)
                        previousState = State.Kidnapping;
                    break;

                case State.Transport:
                    if (previousState != State.Transport)
                        previousState = State.Transport;
                    // SpinerPlugin.LogInfo("[Update] ▶ Entering Transport update"); // évite le spam

                    var threats = new List<GameObject>();
                    threats.AddRange(GetAllAlivePlayerObjects(exclude: inSpecialAnimationWithPlayer));
                    threats.AddRange(GetAllAliveEnemyObjects());

                    if (!isAvoidingThreat)
                    {
                        GameObject threat = CheckLineOfSight(threats, 45f, 12, 2f, this.eye);
                        if (threat != null)
                        {
                            SpinerPlugin.LogInfo($"[Update] ⚠ Threat detected during transport: {threat.name}");
                            currentAvoidPoint = transform.position - (threat.transform.position - transform.position).normalized * 5f;
                            currentAvoidPoint.y = transform.position.y;

                            StopSearch(currentSearch, true);
                            SetDestinationToPosition(currentAvoidPoint, true);
                            isAvoidingThreat = true;
                        }
                    }
                    else
                    {
                        // Reprise normale si point atteint
                        if (Vector3.Distance(transform.position, currentAvoidPoint) < 2f)
                        {
                            SpinerPlugin.LogInfo("[Update] ✅ Avoid point reached → resuming search.");
                            StartSearch(transform.position);
                            isAvoidingThreat = false;
                        }
                    }
                    break;
            }
            // ---- GESTION MARCHE ----
            float speed = agent.velocity.magnitude;
            bool isMoving = speed > 0.5f;

            string currentState = creatureAnimator.GetCurrentAnimatorStateInfo(0).IsName("walk") ? "walk" : "other";
            bool inTransition = creatureAnimator.IsInTransition(0);

            // Appel unique à ta méthode centralisée
            SetAnimation(isMoving ? "Walk" : "Idle");

            //SpinerPlugin.LogInfo($"[Anim] speed: {speed:F2}, isMoving: {isMoving}, inTransition: {inTransition}, WalkBool: {creatureAnimator.GetBool(animWalk)}, currentState: {currentState}");

            // • Son pas : ON/OFF en suivant isMoving
            if (isMoving)
            {
                if (!walkAudioSource.isPlaying) walkAudioSource.Play();
            }
            else
            {
                if (walkAudioSource.isPlaying) walkAudioSource.Stop();
            }

        }


        private List<GameObject> GetAllAlivePlayerObjects(PlayerControllerB? exclude = null)
        {
            var list = new List<GameObject>();
            foreach (var p in StartOfRound.Instance.allPlayerScripts)
            {
                if (p != null && !p.isPlayerDead && p != exclude)
                    list.Add(p.gameObject);
            }
            return list;
        }

        private List<GameObject> GetAllAliveEnemyObjects()
        {
            var list = new List<GameObject>();
            foreach (var detect in GameObject.FindObjectsOfType<EnemyAICollisionDetect>())
            {
                if (detect != null && detect.mainScript != null && detect.mainScript != this && !detect.mainScript.isEnemyDead)
                    list.Add(detect.gameObject);
            }
            return list;
        }



        public override void DoAIInterval()
        {
            //SpinerPlugin.LogInfo($"[DoAIInterval] Current position: {transform.position} | Current destination: {agent.destination}");
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;

            if (IsServer && hasReceivedDamageRecently && spinerBehaviourStateIndex != (int)State.Runaway)
            {
                hasReceivedDamageRecently = false;
                BeginSpinerRunawayServerRpc();
            }


            base.DoAIInterval();

            switch ((State)spinerBehaviourStateIndex)
            {
                case State.Patrol:
                    if (!isPatrolActive) StartPatrol();
                    break;

                case State.Runaway:
                    if (!isRunawayActive) StartRunaway();
                    break;

                case State.Stalking:
                    if (!isStalkingActive)
                        if (stalkingTargetId != -1)
                            StartStalking(stalkingTargetId);
                        else
                            ChangeState(State.Patrol);
                    else
                    {
                        LookAtPlayer();
                        StalkingBehavior();
                    }
                    break;

                case State.Kidnapping:
                    if (!isKidnappingActive)
                        if (kidnappingTargetId != -1)
                            StartKidnapping(kidnappingTargetId);
                        else
                            ChangeState(State.Patrol);
                    else
                        PerformKidnapping();
                    break;

                case State.Transport:
                    if (!isTransportActive)
                        if (transportingTargetId != -1)
                            StartTransport(transportingTargetId);
                        else
                            ChangeState(State.Patrol);
                    else
                        PerformTransport();
                    break;
            }
        }

        private void ResetAllFlags()
        {
            SpinerPlugin.LogInfo("[ResetAllFlags] 🔄 Resetting all stalking-related flags.");

            // Désactive tous les sous-états
            isFollowingBehind = false;
            isCreeping = false;
            isHiding = false;

            // Désactive tous les états actifs
            isRunawayActive = false;
            isPatrolActive = false;
            isStalkingActive = false;
            isKidnappingActive = false;
            isTransportActive = false;

            overrideSpeed = false;
            agent.ResetPath();

            transportReleaseTimer = 0f;


            //SpinerPlugin.LogInfo("[ResetAllFlags] ✅ All flags reset.");
        }


        // ✅ Fonction centrale pour gérer la vitesse dynamiquement
        private void UpdateSpeed()
        {
            if (overrideSpeed)
            {
                SpinerPlugin.LogInfo($"[UpdateSpeed] ❌ Skipping speed update (override active). Current speed: {agent.speed}");
                return;
            }
            switch ((State)spinerBehaviourStateIndex)
            {
                case State.Patrol:
                    agent.speed = 1f;
                    break;
                case State.Runaway:
                    agent.speed = 6f;
                    break;
                case State.Stalking:
                    agent.speed = 2f;
                    break;
                case State.Kidnapping:
                    agent.speed = 4f;
                    break;
                case State.Transport:
                    agent.speed = 2f;
                    break;
            }

            SpinerPlugin.LogInfo($"[UpdateSpeed] Speed set to {agent.speed} for state {((State)spinerBehaviourStateIndex)}");
        }

        // ✅ Appel de `UpdateSpeed()` dans chaque changement d’état
        private void ChangeState(State newState, int playerId = -1)
        {
            if (spinerBehaviourStateIndex == (int)newState) return;

            SpinerPlugin.LogInfo($"[Spiner] State changed: {((State)spinerBehaviourStateIndex)} → {newState}");

            if (newState == State.Runaway && inSpecialAnimationWithPlayer != null)
            {
                SpinerPlugin.LogInfo("[Spiner] Force releasing player before switching to Runaway.");
                ReleaseTransportedPlayer(); // Nettoyage propre
            }

            // 1️⃣ ❌ Stopper l’état précédent proprement AVANT de reset les flags
            switch ((State)spinerBehaviourStateIndex)
            {
                case State.Runaway: StopRunaway(); break;
                case State.Patrol: StopPatrol(); break;
                case State.Stalking: StopStalking(); break;
                case State.Kidnapping: StopKidnapping(); break;
                case State.Transport: StopTransport(); break;
            }

            // 2️⃣ 🔄 Réinitialiser les flags APRÈS avoir stoppé l’ancien état
            ResetAllFlags();

            spinerBehaviourStateIndex = (int)newState;
            UpdateSpeed(); // ✅ Mettre à jour la vitesse immédiatement après le changement

            // 3️⃣ 🔄 Activer le nouvel état
            switch (newState)
            {
                case State.Patrol:
                    TriggerFx(SpinerEvent.Roam);
                    StartPatrol();
                    break;

                case State.Stalking:
                    TriggerFx(SpinerEvent.Creep);
                    StartStalking(playerId);
                    break;

                case State.Kidnapping:
                    TriggerFx(SpinerEvent.Kidnap);
                    StartKidnapping(playerId);
                    break;

                case State.Transport:
                    TriggerFx(SpinerEvent.Transport);
                    StartTransport(playerId);
                    break;

                case State.Runaway:
                    TriggerFx(SpinerEvent.Runaway);
                    StartRunaway();
                    break;
            }
        }
        private void LogRPC(string direction, string rpcName)
        {
            if (!enableRpcLogging) return;

            string info = $"[RPC] {direction} ▶ {rpcName} | " +
                          $"OwnerClientId={OwnerClientId} | " +
                          $"IsHost={IsHost}, IsServer={IsServer}, IsClient={IsClient}, IsOwner={IsOwner} | " +
                          $"CurrentState={(State)spinerBehaviourStateIndex}";

            SpinerPlugin.LogInfo(info);
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead) return;

            // Réduction des PV
            enemyHP -= force;
            SpinerPlugin.LogInfo($"[Combat] Enemy HP remaining: {enemyHP}");

            // Mort si PV <= 0
            if (enemyHP <= 0 && !isEnemyDead && IsOwner)
            {
                SpinerPlugin.LogInfo("[Combat] Enemy HP reached 0 → Killing enemy.");
                KillEnemyOnOwnerClient(false);
                return;
            }

            // Animation de blessure
            if (!isEnemyDead)
            {
                string[] hurtAnims = { "Hurt", "Hurt2", "Hurt3" };
                string anim = hurtAnims[hurtAnimIndex];
                SetAnimation(anim);
                SpinerPlugin.LogInfo($"[Combat] Playing hurt animation: {anim}");
                hurtAnimIndex = (hurtAnimIndex + 1) % hurtAnims.Length;
            }

            // Flag de comportement (serveur uniquement)
            if (!IsServer) return;

            SpinerPlugin.LogInfo("[Combat] Enemy took damage → flagging for Runaway.");
            hasReceivedDamageRecently = true;
        }


        public override void KillEnemy(bool destroy = false)
        {
            SpinerPlugin.LogInfo("enter kill enemy");
            ChangeState(State.Patrol);
            // si le mod est en DarkMode et qu’on n’a pas déjà feint la mort → feinte
            if (_cfgDarkMode && !_feignDeathActive && !_phase2Lethal && !destroy)
            {
                SpinerPlugin.LogInfo("killenemy with spin condition called");
                StartFeignDeath();
                return;
            }
            SpinerPlugin.LogInfo("enter death logic in killenemy");
            base.KillEnemy(false);
            SetAnimation("Death2"); // Ton anim personnalisée
        }

        private void StartFeignDeath()
        {
            SpinerPlugin.LogInfo("enter start feign death");
            isEnemyDead = true;             // bloque l’IA
            _feignDeathActive = true;
            _feignDeathTimer = _cfgDarkReviveDelay;

            // visuel/son
            SetAnimation("Death");
            TriggerFx(SpinerEvent.Death);

            // neutraliser le mob
            agent.enabled = false;
            CancelSpecialAnimationWithPlayer();
            // collisions inertes (optionnel, garde le collider mais en “Mort”)
            // foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false; // si besoin
        }

        private void TickFeignDeath()
        {
            if (!_feignDeathActive) return;
            _feignDeathTimer -= Time.deltaTime;
            if (_feignDeathTimer > 0f) return;

            // “Résurrection” : phase 2
            _feignDeathActive = false;
            _phase2Lethal = true;
            isEnemyDead = false;

            // redémarrer l’agent + FSM
            if (agent != null)
            {
                agent.enabled = true;
                if (!agent.isOnNavMesh) agent.Warp(transform.position); // évite ResetPath error
            }
            enemyHP = _cfgMaxHP;  // ← restaure la vie
            SpinerPlugin.LogInfo($"[DarkMode] Revived → HP set to {enemyHP}");
            SetAnimation("Resurrect");

            ChangeState(State.Runaway);
            // Option : rendre le modèle plus sombre (si shader supporte _Color)
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r.material.HasProperty("_Color"))
                    r.material.color = Color.Lerp(r.material.color, Color.black, 0.7f); // 0.7 = taux d’assombrissement
                if (r.material.HasProperty("_EmissionColor"))
                    r.material.SetColor("_EmissionColor", r.material.GetColor("_EmissionColor") * 0.5f);
            }
        }






        // =============== ÉTATS ===============
        private void StartRunaway()
        {
            if (isRunawayActive) return;
            stalkingTargetId = -1;
            kidnappingTargetId = -1;
            transportingTargetId = -1;

            SpinerPlugin.LogInfo("[Runaway] 🏃 Start Runaway!");

            isRunawayActive = true;
            UpdateSpeed();

            // ✅ Trouver le point le plus éloigné de SA PROPRE POSITION
            Transform farthestNode = ChooseFarthestNodeFromPosition(transform.position);

            if (farthestNode != null)
            {
                SpinerPlugin.LogInfo($"[Runaway] 📍 Escaping to {farthestNode.position}");
                base.SetDestinationToPosition(farthestNode.position, true);
            }
            else
            {
                SpinerPlugin.LogWarning("[Runaway] ❌ No valid escape node found! Using fallback.");
                Vector3 fallbackPos = transform.position + (UnityEngine.Random.insideUnitSphere * 10f);
                fallbackPos.y = transform.position.y; // On garde la même hauteur
                base.SetDestinationToPosition(fallbackPos, true);
            }

            // ✅ Lancer un chrono pour revenir en Patrol
            StartCoroutine(ExitRunawayAfterDelay());
        }

        private void StopRunaway()
        {
            if (!isRunawayActive) return;
            SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Runaway");
            isRunawayActive = false;
        }

        public void StartPatrol()
        {
            if (isPatrolActive) return;
            stalkingTargetId = -1;
            kidnappingTargetId = -1;
            transportingTargetId = -1;
            SpinerPlugin.LogInfo("[Spiner] 🚶 Start Patrol");
            isPatrolActive = true;
            UpdateSpeed();
            StartSearch(transform.position);
        }
        public void StopPatrol()
        {
            if (!isPatrolActive) return;
            SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Patrol");
            StopSearch(currentSearch, true);
            isPatrolActive = false;
        }

        private void StartStalking(int playerId)
        {
            stalkingTargetId = playerId;

            if (isStalkingActive) return;
            SpinerPlugin.LogInfo($"[Spiner] 🕵️ Start Stalking on Player {playerId}");
            isStalkingActive = true;
            chasingPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            LookAtPlayer();
            UpdateSpeed();
            StalkingBehavior();
        }
        private void StopStalking()
        {
            if (!isStalkingActive) return;
            SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Stalking");
            isStalkingActive = false;
            ResetRotation();
        }
        private void StartKidnapping(int playerId)
        {
            kidnappingTargetId = playerId;

            if (isKidnappingActive) return;
            SpinerPlugin.LogInfo("[Kidnapping] 🚨 Entering Kidnapping mode!");
            isKidnappingActive = true;
            UpdateSpeed();

            // ✅ Appel à la méthode dédiée pour gérer le Kidnapping
            PerformKidnapping();
        }

        private void StopKidnapping()
        {
            if (!isKidnappingActive) return;
            SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Kidnapping");
            isKidnappingActive = false;
        }
        private void StartTransport(int playerId)
        {
            if (inSpecialAnimationWithPlayer == null && playerId >= 0 && playerId < StartOfRound.Instance.allPlayerScripts.Length)
                inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerId];

            transportingTargetId = playerId;

            if (isTransportActive) return;

            SpinerPlugin.LogInfo("[Spiner] 🚛 Start Transport");
            isTransportActive = true;

            kidnapCarryPoint = transform.Find("KidnapCarryPoint");
            SpinerPlugin.LogInfo("[StartTransport] 🧭 KidnapCarryPoint reassigned: " + (kidnapCarryPoint != null));

            if (inSpecialAnimationWithPlayer != null && kidnapCarryPoint != null)
            {
                // 🧷 Attache physique
                inSpecialAnimationWithPlayer.transform.SetParent(kidnapCarryPoint);
                inSpecialAnimationWithPlayer.transform.localPosition = Vector3.zero;
                inSpecialAnimationWithPlayer.transform.localRotation = Quaternion.identity;

                // 🔒 Verrouillage état
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
                inSpecialAnimationWithPlayer.snapToServerPosition = true;

                // 🚫 Blocage du saut – uniquement joueur local
                if (inSpecialAnimationWithPlayer == GameNetworkManager.Instance.localPlayerController)
                {
                    var jumpAction = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump", false);
                    if (jumpAction != null) jumpAction.Disable();
                }

                SpinerPlugin.LogInfo("[StartTransport] ✅ Player successfully attached and configured.");
            }
            else
            {
                SpinerPlugin.LogWarning("[StartTransport] ⚠️ Cannot attach player: missing reference or carry point.");
            }

            UpdateSpeed();
            StartSearch(transform.position);
        }


        private void StopTransport()
        {
            if (!isTransportActive) return;
            SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Transport");
            isTransportActive = false;
            StopSearch(currentSearch, true);
        }

        private IEnumerator ExitRunawayAfterDelay()
        {
            yield return new WaitForSeconds(5f); // ⏳ Attend 5 secondes avant de revenir en patrouille

            SpinerPlugin.LogInfo("[Runaway] ⏳ Timeout reached, returning to Patrol.");
            ChangeState(State.Patrol);
        }


        ////////////////////////////////////////////////////////////////////////////////////
        // SPINER STALKING LOGIC: offset behind player + hide behind obstacle if discovered
        ////////////////////////////////////////////////////////////////////////////////////

        // ===================== STALKING CORE ======================
        // Appelée dans StartStalking() et à chaque DoAIInterval pendant Stalking
        private void StalkingBehavior()
        {
            //SpinerPlugin.LogInfo("[StalkingBehavior] ENTER");

            // 1) L’IA voit-elle le chasingPlayer ?
            bool iSeeThePlayer = ISeeMyTarget();
            //SpinerPlugin.LogInfo($"[StalkingBehavior] iSeeThePlayer = {iSeeThePlayer}");

            // 2) Le chasingPlayer me voit-il ?
            bool playerSeesMe = MyTargetSeesMe();
            //SpinerPlugin.LogInfo($"[StalkingBehavior] playerSeesMe = {playerSeesMe}");

            // 3) On met à jour le stalkingMeter
            float oldMeter = stalkingMeter;
            UpdateStalkingMeter(iSeeThePlayer);
            //SpinerPlugin.LogInfo($"[StalkingBehavior] Updated meter: {oldMeter} => {stalkingMeter} (inLOS={iSeeThePlayer})");

            // 4) Logique
            if (iSeeThePlayer)
            {
                if (playerSeesMe)
                {
                    // => On se cache derrière un obstacle
                    //SpinerPlugin.LogInfo("[StalkingBehavior] Both see each other => HideBehindObstacle()");
                    HideBehindObstacle();
                }
                else
                {
                    // => J'approche en restant derrière le joueur
                    //SpinerPlugin.LogInfo("[StalkingBehavior] I see him but he doesn't see me => BeingACreep()");
                    BeingACreep();
                }
            }
            else
            {
                //SpinerPlugin.LogInfo("[StalkingBehavior] I can't see him => ApproachBehindTarget()");
                ApproachBehindTarget();
            }

            //SpinerPlugin.LogInfo("[StalkingBehavior] EXIT");
        }

        // ✅ Oriente immédiatement l'ennemi vers le joueur
        private void LookAtPlayer()
        {
            if (chasingPlayer == null) return;

            Vector3 direction = chasingPlayer.transform.position - transform.position;
            direction.y = 0; // On évite les rotations verticales
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = targetRotation;

            //SpinerPlugin.LogInfo($"[LookAtPlayer] 🔄 Looking at player {chasingPlayer.name}");
        }

        // ✅ Réinitialise la rotation en quittant Stalking
        private void ResetRotation()
        {
            //SpinerPlugin.LogInfo("[ResetRotation] 🔄 Resetting to default rotation.");
            transform.rotation = Quaternion.identity; // Réinitialise l’orientation
        }


        // ===================== MISE À JOUR DU STALKING METER ======================
        private const float stalkingMeterAbortThreshold = -3f;
        private float lastLoggedStalkingSecond = -1f;

        private void UpdateStalkingMeter(bool inLosThisFrame)
        {
            float old = stalkingMeter;

            if (inLosThisFrame)
            {
                stalkingMeter = Mathf.Min(
                    stalkingMeter + stalkingMeterIncrement * Time.deltaTime,
                    stalkingMeterMax
                );
            }
            else
            {
                stalkingMeter = Mathf.Max(
                    stalkingMeter - stalkingMeterDecrement * Time.deltaTime,
                    stalkingMeterAbortThreshold // Autorise la descente sous 0 jusqu’au seuil
                );
            }

            float currentSecond = Mathf.Floor(stalkingMeter);
            if (currentSecond != lastLoggedStalkingSecond)
            {
                lastLoggedStalkingSecond = currentSecond;
                SpinerPlugin.LogInfo($"[UpdateStalkingMeter] Meter: {Mathf.RoundToInt(stalkingMeter)}s {(inLosThisFrame ? "(LOS)" : "(no LOS)")}");
            }

            if (stalkingMeter <= 0f && old > 0f)
            {
                SpinerPlugin.LogInfo("[UpdateStalkingMeter] Meter reached 0.");
                lastLoggedStalkingSecond = -1f;
            }

            if (stalkingMeter >= stalkingMeterMax)
            {
                SpinerPlugin.LogInfo("[UpdateStalkingMeter] Meter full => switch to KIDNAPPING");
                stalkingMeter = 0f;
                lastLoggedStalkingSecond = -1f;

                if (IsServer)
                    BeginSpinerKidnapClientRpc(stalkingTargetId);
                else
                    BeginSpinerKidnapServerRpc(stalkingTargetId);
            }

            if (stalkingMeter <= stalkingMeterAbortThreshold)
            {
                SpinerPlugin.LogInfo("[UpdateStalkingMeter] Meter dropped too low => switch to PATROL");
                stalkingMeter = 0f;
                lastLoggedStalkingSecond = -1f;

                if (IsServer)
                    BeginSpinerPatrolClientRpc();
                else
                    BeginSpinerPatrolServerRpc();
            }
        }




        // ===================== VISION: IA => Joueur & Joueur => IA =====================
        private bool ISeeMyTarget()
        {
            if (chasingPlayer == null)
            {
                //SpinerPlugin.LogInfo("[ISeeMyTarget] ❌ No chasingPlayer assigned, returning false");
                return false;
            }

            Vector3 eyePos = this.eye.position;
            Vector3 targetPos = chasingPlayer.transform.position + Vector3.up * 1f;

            float dist = Vector3.Distance(eyePos, targetPos);
            if (dist > 50f)
            {
                //SpinerPlugin.LogInfo("[ISeeMyTarget] ❌ Out of range, returning false");
                return false;
            }

            Vector3 dirToTarget = (targetPos - eyePos).normalized;
            float angle = Vector3.Angle(this.eye.forward, dirToTarget);
            if (angle > 70f)
            {
                //SpinerPlugin.LogInfo("[ISeeMyTarget] ❌ Out of FOV, returning false");
                return false;
            }

            bool blocked = Physics.Linecast(
                eyePos,
                targetPos,
                StartOfRound.Instance.collidersAndRoomMaskAndDefault
            );
            bool result = !blocked;
            //SpinerPlugin.LogInfo($"[ISeeMyTarget] ✅ Result={result}, blocked={blocked}");
            return result;
        }

        private bool MyTargetSeesMe()
        {
            if (chasingPlayer == null)
            {
                //SpinerPlugin.LogInfo("[MyTargetSeesMe] ❌ No chasingPlayer assigned, returning false");
                return false;
            }

            Transform playerCam = chasingPlayer.gameplayCamera.transform;
            Vector3 stalkerEyePos = this.eye.position;
            Vector3 playerCamPos = playerCam.position;

            float dist = Vector3.Distance(playerCamPos, stalkerEyePos);
            if (dist > 50f)
            {
                //SpinerPlugin.LogInfo("[MyTargetSeesMe] ❌ Out of range, returning false");
                return false;
            }

            Vector3 dirToStalker = (stalkerEyePos - playerCamPos).normalized;
            float angle = Vector3.Angle(playerCam.forward, dirToStalker);
            if (angle > 70f)
            {
                //SpinerPlugin.LogInfo("[MyTargetSeesMe] ❌ Out of FOV, returning false");
                return false;
            }

            bool blocked = Physics.Linecast(
                playerCamPos,
                stalkerEyePos,
                StartOfRound.Instance.collidersAndRoomMaskAndDefault
            );
            bool result = !blocked;
            //SpinerPlugin.LogInfo($"[MyTargetSeesMe] ✅ Result={result}, blocked={blocked}");
            return result;
        }

        // ===================== APPROCHE DERRIÈRE LE JOUEUR (OFFSET) =====================
        private void ApproachBehindTarget()
        {
            if (chasingPlayer == null) return;

            isHiding = false;
            isCreeping = false;
            overrideSpeed = true;

            // Distance idéale à garder
            float maxDistance = 2f;   // Distance qu'on veut maintenir
            float lateralOffset = 1f; // Décalage latéral
            Transform pTransform = chasingPlayer.transform;

            // Calcul de la position derrière le joueur
            Vector3 behindPlayer = pTransform.position
                                 - pTransform.forward * maxDistance
                                 + pTransform.right * lateralOffset;

            float currentDist = Vector3.Distance(transform.position, behindPlayer);

            // Calcul de la vitesse : vitesse = distance / 2, clampée entre 0.1 et 4
            float speed = Mathf.Clamp(currentDist / 2f, 0.1f, 4f);
            agent.speed = speed;

            // Mise à jour permanente de la destination tant qu'on suit
            base.SetDestinationToPosition(behindPlayer, true);

            //SpinerPlugin.LogInfo($"[ApproachBehindTarget] Moving behind player => {behindPlayer} | Dist={currentDist} | Speed={agent.speed}");
        }


        // ===================== CACHER DERRIÈRE UN OBSTACLE =====================
        private void HideBehindObstacle()
        {
            if (isHiding)
            {
                //SpinerPlugin.LogInfo("[HideBehindObstacle] ❌ Already hiding, skipping...");
                return;
            }

            if (chasingPlayer == null) return;

            isHiding = true;
            isCreeping = false;
            overrideSpeed = true;

            Vector3 enemyPos = transform.position;
            Vector3 playerPos = chasingPlayer.transform.position;

            // ✅ Nouvelle direction de scan : orientée sur l’axe IA → Joueur
            Vector3 dirToPlayer = (playerPos - enemyPos).normalized;

            float searchRadius = 6f;  // Distance max où chercher un node
            float searchAngle = 120f; // Zone de recherche derrière l’ennemi
            int numSamples = 15;      // Nombre de points qu'on va tester dans l'arc

            //SpinerPlugin.LogInfo($"[HideBehindObstacle] 🔎 Scanning for valid hiding nodes...");

            List<Vector3> possibleNodes = new List<Vector3>();

            // 🔥 Générer une liste complète de nodes derrière l'IA
            for (int i = 0; i < numSamples; i++)
            {
                float angle = (-searchAngle / 2) + ((searchAngle / (numSamples - 1)) * i);
                Vector3 dir = Quaternion.Euler(0, angle, 0) * -dirToPlayer; // Derrière l'IA
                Vector3 candidatePos = enemyPos + (dir * searchRadius);

                // Vérifier si ce point est sur le NavMesh
                NavMeshHit navHit;
                bool isValidNode = NavMesh.SamplePosition(candidatePos, out navHit, 2f, NavMesh.AllAreas);

                if (isValidNode)
                {
                    possibleNodes.Add(navHit.position);
                }

                //SpinerPlugin.LogInfo($"[HideBehindObstacle] 🧐 Node {i}: RequestedPos={candidatePos} | NavMeshPos={navHit.position} | Valid={isValidNode}");
            }

            //SpinerPlugin.LogInfo($"[HideBehindObstacle] 📊 {possibleNodes.Count} valid nodes found behind enemy.");

            // 🔍 Tester chaque node trouvé
            foreach (Vector3 node in possibleNodes)
            {
                float distToPlayer = Vector3.Distance(node, playerPos);
                bool isHidden = Physics.Raycast(node, (playerPos - node).normalized, distToPlayer, StartOfRound.Instance.collidersRoomDefaultAndFoliage);

                //SpinerPlugin.LogInfo($"[HideBehindObstacle] 🔬 Testing node at {node} | DistToPlayer={distToPlayer} | ObstacleBetween={isHidden}");

                if (isHidden)
                {
                    this.movingTowardsTargetPlayer = false;
                    // ✅ On tente de définir la nouvelle destination
                    bool hideSuccess = base.SetDestinationToPosition(node, true);
                    Vector3 newDestination = agent.destination; // Récupérer la destination juste après

                    //SpinerPlugin.LogInfo($"[HideBehindObstacle] 🛑 Node at {node} -> SetDestination success={hideSuccess} | New Destination={newDestination}");

                    if (hideSuccess)
                    {
                        // 🛠️ Vérifier si la destination a vraiment changé
                        if (Vector3.Distance(newDestination, node) > 0.5f)
                        {
                            //SpinerPlugin.LogWarning($"[HideBehindObstacle] ⚠️ Destination mismatch! Expected={node}, Got={newDestination}");
                        }

                        // 🔥 Forcer le déplacement
                        agent.ResetPath(); // Pour éviter les conflits avec un path en cours
                        this.moveTowardsDestination = true;
                        this.movingTowardsTargetPlayer = false;

                        //SpinerPlugin.LogInfo($"[HideBehindObstacle] ✅ Successfully hiding at {node}, forcing movement.");
                        agent.speed = 4f;
                        return; // Arrêter après avoir trouvé un spot valide
                    }
                }
            }
            // ❌ Aucun node trouvé, fallback amélioré
            //SpinerPlugin.LogInfo("[HideBehindObstacle] ❌ No valid hiding spot found! Searching for alternative escape...");

            // ✅ Trouver le node le plus éloigné DANS LA DIRECTION OPPOSÉE AU JOUEUR
            Vector3 escapeDir = -dirToPlayer; // On inverse la direction vers le joueur
            Vector3 escapePos = enemyPos + escapeDir * 6f; // Essai de fuite plus loin

            NavMeshHit escapeHit;
            bool foundEscape = NavMesh.SamplePosition(escapePos, out escapeHit, 3f, NavMesh.AllAreas);

            if (foundEscape)
            {
                //SpinerPlugin.LogInfo($"[HideBehindObstacle] 🔄 Escaping to {escapeHit.position}");
                base.SetDestinationToPosition(escapeHit.position, true);
                agent.speed = 5f;
            }
            else
            {
                // Si aucun point de fuite n'est trouvé, on tente de reculer normalement
                Vector3 fallbackPos = transform.position - (dirToPlayer * 5f);
                base.SetDestinationToPosition(fallbackPos, true);
                //SpinerPlugin.LogInfo($"[HideBehindObstacle] 🚨 Fallback to {fallbackPos}");
            }

            // 🔥 Lancer une coroutine pour surveiller si on est encore vu
            StartCoroutine(StopFleeingAfterTimeout());
        }

        // ✅ Arrêter la fuite après 3s si le joueur ne nous voit plus
        private IEnumerator StopFleeingAfterTimeout()
        {
            yield return new WaitForSeconds(3f);

            if (!MyTargetSeesMe()) // Si le joueur ne nous voit plus, on arrête
            {
                isHiding = false;
                isFollowingBehind = false;
                isCreeping = false;
                agent.ResetPath();
                //SpinerPlugin.LogInfo("[HideBehindObstacle] 🛑 Fleeing stopped, returning to stalking.");
            }
        }


        private IEnumerator ResetHidingState()
        {
            yield return new WaitForSeconds(2f);
            //SpinerPlugin.LogInfo("[ResetHidingState] Hiding completed, resetting state.");
            isHiding = false;
        }

        // ===================== OBSERVATION (STALK MODE) =====================
        // ✅ Active le mode creep (immobile + surveille si le joueur tourne la tête)
        private void BeingACreep()
        {
            if (isCreeping)
            {
                //SpinerPlugin.LogInfo("[BeingACreep] ❌ Already in creep mode, skipping...");
                return;
            }

            //SpinerPlugin.LogInfo("[BeingACreep] 👀 Entering creep mode...");

            // 🛑 Stopper le déplacement proprement
            agent.SetDestination(agent.transform.position);
            agent.velocity = Vector3.zero;

            isCreeping = true;
            isFollowingBehind = false;
            isHiding = false;
            overrideSpeed = true;

            // 🔄 On surveille si le joueur tourne la tête ou si on le perd de vue
            StartCoroutine(CheckForDetection());

            //SpinerPlugin.LogInfo("[BeingACreep] 🕵️ Standing still, watching the player...");
        }

        // ✅ Vérifie si le joueur nous voit OU si nous le perdons de vue
        private IEnumerator CheckForDetection()
        {
            while (isCreeping)
            {
                // 🚨 Si l'état change, on arrête
                if (spinerBehaviourStateIndex != (int)State.Stalking)
                {
                    //SpinerPlugin.LogInfo("[CheckForDetection] ❌ State changed, stopping detection.");
                    yield break;
                }

                bool playerSeesMe = MyTargetSeesMe();
                bool iSeeThePlayer = ISeeMyTarget();

                if (playerSeesMe)
                {
                    // 🚨 Si le joueur nous détecte → On se cache
                    //SpinerPlugin.LogInfo("[CheckForDetection] 🚨 Player detected me! Switching to hide mode.");
                    isCreeping = false;
                    HideBehindObstacle();
                    yield break;
                }
                else if (!iSeeThePlayer)
                {
                    // ❌ Si l’ennemi perd de vue le joueur → Repasser en approach derrière lui
                    //SpinerPlugin.LogInfo("[CheckForDetection] ❌ Lost sight of player, returning to ApproachBehindTarget.");
                    isCreeping = false;
                    ApproachBehindTarget();
                    yield break;
                }

                yield return new WaitForSeconds(0.2f);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////
        // SPINER KIDNAPPING LOGIC: Capture, transport & escape
        ////////////////////////////////////////////////////////////////////////////////////

        // ===================== KIDNAPPING CORE ======================
        // Appelée dans StartKidnapping() et gère la transition vers le transport
        private float kidnappingTimeout = 5f;
        private float kidnappingTimer = 0f;


        private void PerformKidnapping()
        {
            if (chasingPlayer == null)
            {
                SpinerPlugin.LogWarning("[Kidnapping] ❌ No chasingPlayer assigned. Switching to Runaway.");
                ChangeState(State.Runaway);
                return;
            }

            // Vérification de distance ou accessibilité
            float dist = Vector3.Distance(transform.position, chasingPlayer.transform.position);
            if (dist > 50f || !chasingPlayer.isPlayerControlled || chasingPlayer.isPlayerDead)
            {
                kidnappingTimer += Time.deltaTime;

                if (kidnappingTimer >= kidnappingTimeout)
                {
                    SpinerPlugin.LogWarning("[Kidnapping] ❌ Kidnapping timeout reached. Switching to Runaway.");
                    kidnappingTimer = 0f;
                    ChangeState(State.Runaway);
                    return;
                }
            }
            else
            {
                // Reset timer si la cible est à nouveau valide
                kidnappingTimer = 0f;
            }

            // Déplacement vers la cible
            base.SetDestinationToPosition(chasingPlayer.transform.position, true);
        }


        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            if (!IsServer)
            {
                SpinerPlugin.LogInfo("[Kidnapping] Ignored collision → not server.");
                return;
            }

            if (isEnemyDead)
            {
                SpinerPlugin.LogInfo("[Kidnapping] Ignored collision → enemy dead.");
                return;
            }

            if ((State)spinerBehaviourStateIndex != State.Kidnapping)
                return;

            if (isTransportActive || inSpecialAnimationWithPlayer != null)
                return;

            if ((State)spinerBehaviourStateIndex == State.Transport)
            {
                SpinerPlugin.LogInfo("[Kidnapping] Already in Transport state, skipping.");
                return;
            }

            PlayerControllerB player = base.MeetsStandardPlayerCollisionConditions(other, false, false);
            if (player == null)
            {
                SpinerPlugin.LogInfo("[Kidnapping] Collision detected, but no valid Player found.");
                return;
            }

            SpinerPlugin.LogInfo($"[Kidnapping] ✅ Valid collision with Player {player.playerUsername}.");
            inSpecialAnimationWithPlayer = player;
            kidnappingTargetId = Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player); // sécurité

            SpinerPlugin.LogInfo("[Kidnapping] Player control disabled. Transitioning to Transport state.");
            SpinerPlugin.LogInfo($"[Kidnapping] Player bind state just before transport: inAnimationWithEnemy={(player.inAnimationWithEnemy == this)}, inSpecialAnimationWithPlayer={inSpecialAnimationWithPlayer != null}");

            SetAnimation("Grab");

            // ---- Dark Mode: arme le timer létal pendant le transport ----
            if (_phase2Lethal && !_killTimerActive)
            {
                _killTimerActive = true;
                _kidnapKillTimer = _cfgDarkKillTime; // durée définie dans le config
                SpinerPlugin.LogInfo($"[DarkMode] Lethal timer armed: {_kidnapKillTimer:F1}s");
            }


            if (IsServer) BeginSpinerTransportClientRpc(kidnappingTargetId);
            else BeginSpinerTransportServerRpc(kidnappingTargetId);
        }



        ////////////////////////////////////////////////////////////////////////////////////
        // SPINER TRANSPORT LOGIC: Carrying kidnapped player to destination
        ////////////////////////////////////////////////////////////////////////////////////

        // ===================== TRANSPORT CORE ======================
        // Appelée dans StartTransport(), déplace le joueur kidnappé vers une zone cible

        private void PerformTransport()
        {
            SpinerPlugin.LogInfo("[PerformTransport] ✅ Called.");
            SpinerPlugin.LogInfo($"[PerformTransport] → Check state: null={inSpecialAnimationWithPlayer == null}, dead={inSpecialAnimationWithPlayer?.isPlayerDead}, active={inSpecialAnimationWithPlayer?.gameObject.activeInHierarchy}, binded={(inSpecialAnimationWithPlayer?.inAnimationWithEnemy == this)}, enemyDead={isEnemyDead}");

            // ───────── Pré-checks communs ─────────
            if (isEnemyDead)
            {
                SpinerPlugin.LogWarning("[Transport] ☠️ Enemy is dead during transport → releasing player.");
                ReleaseTransportedPlayer(); // StopLethalTimer() est appelé dedans
                return;
            }

            if (inSpecialAnimationWithPlayer == null ||
                inSpecialAnimationWithPlayer.isPlayerDead ||
                !inSpecialAnimationWithPlayer.gameObject.activeInHierarchy ||
                inSpecialAnimationWithPlayer.inAnimationWithEnemy != this)
            {
                SpinerPlugin.LogWarning("[Transport] ❌ Transported player invalid (null/dead/disconnected). Releasing.");
                ReleaseTransportedPlayer(); // StopLethalTimer() est appelé dedans
                ChangeState(State.Runaway);
                return;
            }

            // (Navigation/avoidance éventuelle ici si besoin)
            // ...

            // ───────── Branche DARK MODE (phase 2) ─────────
            if (_phase2Lethal)
            {
                // Pendant la capture en phase 2, on ne fait que le compte à rebours létal (serveur autoritaire)
                if (_killTimerActive && inSpecialAnimationWithPlayer != null)
                {
                    _kidnapKillTimer -= Time.deltaTime;

                    // Log discret éventuellement :
                    if ((_kidnapKillTimer % 1f) > (_kidnapKillTimer - Time.deltaTime) % 1f)
                        SpinerPlugin.LogInfo($"[DarkMode] Kill in {_kidnapKillTimer:F1}s");

                    if (_kidnapKillTimer <= 0f)
                    {
                        _killTimerActive = false;
                        SpinerPlugin.LogInfo("[DarkMode] Lethal timer reached 0 → killing captured player");

                        SetAnimation("Spin");

                        var p = inSpecialAnimationWithPlayer; // PlayerControllerB
                        if (p != null && !p.isPlayerDead)
                        {
                            // 1) on libère pour casser le lien (évite AllowPlayerDeath=false)
                            ReleaseTransportedPlayer();

                            // 2) exécution du kill selon le contexte
                            if (IsServer)
                            {
                                // serveur → envoie le ClientRpc (broadcast + filtre par targetId OK)
                                KillCapturedClientRpc(p.playerClientId, (int)CauseOfDeath.Snipped);
                            }
                            else if (p.IsOwner)
                            {
                                // client owner → tue localement (équiv. Barber côté owner)
                                p.KillPlayer(Vector3.up * 14f, true, CauseOfDeath.Snipped, 7, Vector3.zero);
                            }

                            ChangeState(State.Patrol);
                        }
                        return;
                    }
                }

                // En Dark phase 2, on **n’utilise pas** le releaseTimer ni le check distance pour “forcer” la libération.
                // On garde juste un garde-fou si le joueur se détache par bug :
                float distDark = Vector3.Distance(inSpecialAnimationWithPlayer!.transform.position, kidnapCarryPoint!.position);
                if (distDark > 4.0f) // marge plus large en phase 2
                {
                    SpinerPlugin.LogWarning($"[DarkMode] Player drifted too far in phase2 ({distDark:F2}m) → Forcing release.");
                    ReleaseTransportedPlayer();
                    ChangeState(State.Runaway);
                }

                return; // fin de la branche dark
            }

            // ───────── Branche NORMALE (ton code actuel) ─────────

            // ✅ Libération si un seul joueur en vie
            int aliveOthers = StartOfRound.Instance.allPlayerScripts.Count(p =>
                p != null && p.isPlayerControlled && !p.isPlayerDead && p != inSpecialAnimationWithPlayer);

            SpinerPlugin.LogInfo($"[Transport] Players alive (excluding carried): {aliveOthers}");

            if (aliveOthers <= 0)
            {
                transportReleaseTimer += Time.deltaTime;
                SpinerPlugin.LogInfo($"[Transport] ✅ No other players alive → Releasing in {transportReleaseDelay - transportReleaseTimer:0.0}s");

                if (transportReleaseTimer >= transportReleaseDelay)
                {
                    SpinerPlugin.LogInfo("[Transport] ⏱ Timer expired → releasing player.");
                    ReleaseTransportedPlayer(); // StopLethalTimer() au cas où
                    ChangeState(State.Runaway);

                    return;
                }
            }
            else
            {
                if (transportReleaseTimer > 0f)
                {
                    SpinerPlugin.LogInfo("[Transport] 🔄 Release timer reset.");
                    transportReleaseTimer = 0f;
                }
            }

            // ⛔ Libération si trop éloigné (branche normale)
            float dist = Vector3.Distance(inSpecialAnimationWithPlayer.transform.position, kidnapCarryPoint.position);
            if (dist > 2.5f)
            {
                SpinerPlugin.LogWarning($"[Transport] ❌ Player drifted too far from carry point ({dist:F2}m) → Forcing release.");
                ReleaseTransportedPlayer(); // StopLethalTimer() au cas où
                ChangeState(State.Runaway);
                return;
            }
        }




        private void ReleaseTransportedPlayer()
        {
            transportReleaseTimer = 0f;
            StopLethalTimer();
            if (inSpecialAnimationWithPlayer != null)
            {
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
                inSpecialAnimationWithPlayer.snapToServerPosition = false;

                // ✅ Réactivation du saut uniquement pour le joueur local
                if (inSpecialAnimationWithPlayer == GameNetworkManager.Instance.localPlayerController)
                {
                    var jumpAction = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump", false);
                    if (jumpAction != null)
                    {
                        jumpAction.Enable();
                        SpinerPlugin.LogInfo("[Transport] 🔓 Jump re-enabled after release.");
                    }
                }

                inSpecialAnimationWithPlayer.transform.SetParent(null);
                SpinerPlugin.LogInfo("[Transport] Player released from carry point.");
                inSpecialAnimationWithPlayer = null;
            }
        }

        private void StopLethalTimer()
        {
            _killTimerActive = false;
            _kidnapKillTimer = 0f;
        }


        ////////////////////////////////////////////////////////////////////////////////////
        // SPINER NETWORK RPCs: Synchronization & Multiplayer Communication
        ////////////////////////////////////////////////////////////////////////////////////

        // ===================== CFG SYNC =====================
        [ClientRpc]
        private void ApplyRuntimeConfigClientRpc(string json)
        {
            var cfg = JsonUtility.FromJson<SpinerPlugin.SpinerRuntimeConfig>(json);
            _cfgMaxHP = cfg.maxHP;
            _cfgRoamVolume = cfg.roamVolume;
            _cfgDarkMode = cfg.darkMode;
            _cfgDarkReviveDelay = cfg.darkReviveDelay;
            _cfgDarkKillTime = cfg.darkKillTime;
            _cfgApplied = true;
            ApplyLocalConfig();
        }

        // ===================== KILL SYNC =====================
        [ClientRpc]
        private void KillCapturedClientRpc(ulong targetId, int cause, ClientRpcParams rpcParams = default)
        {
            var local = StartOfRound.Instance.localPlayerController;
            if (local != null && local.playerClientId == targetId && !local.isPlayerDead)
            {
                local.KillPlayer(Vector3.up * 14f, true, (CauseOfDeath)cause, 7, Vector3.zero);
                // joue ici tes SFX/FX client si tu veux
            }
        }

        // ===================== PATROL =====================
        [ServerRpc(RequireOwnership = false)]
        private void BeginSpinerPatrolServerRpc()
        {
            LogRPC("Server", nameof(BeginSpinerPatrolServerRpc));
            ChangeState(State.Patrol);
            BeginSpinerPatrolClientRpc();
        }

        [ClientRpc]
        private void BeginSpinerPatrolClientRpc()
        {
            LogRPC("Client", nameof(BeginSpinerPatrolClientRpc));
            ChangeState(State.Patrol);
        }

        // ===================== STALKING =====================
        [ServerRpc(RequireOwnership = false)]
        private void BeginSpinerStalkServerRpc(int playerId)
        {
            LogRPC("Server", nameof(BeginSpinerStalkServerRpc));
            ChangeState(State.Stalking, playerId);
            BeginSpinerStalkClientRpc(playerId);
        }

        [ClientRpc]
        private void BeginSpinerStalkClientRpc(int playerId)
        {
            LogRPC("Client", nameof(BeginSpinerStalkClientRpc));
            ChangeState(State.Stalking, playerId);
        }

        // ===================== KIDNAPPING =====================
        [ServerRpc(RequireOwnership = false)]
        private void BeginSpinerKidnapServerRpc(int playerId)
        {
            LogRPC("Server", nameof(BeginSpinerKidnapServerRpc));
            ChangeState(State.Kidnapping, playerId);
            BeginSpinerKidnapClientRpc(playerId);
        }

        [ClientRpc]
        private void BeginSpinerKidnapClientRpc(int playerId)
        {
            LogRPC("Client", nameof(BeginSpinerKidnapClientRpc));
            ChangeState(State.Kidnapping, playerId);
        }

        // ===================== TRANSPORT =====================
        [ServerRpc(RequireOwnership = false)]
        private void BeginSpinerTransportServerRpc(int playerId)
        {
            LogRPC("Server", nameof(BeginSpinerTransportServerRpc));
            ChangeState(State.Transport, playerId);
            BeginSpinerTransportClientRpc(playerId);
        }

        [ClientRpc]
        private void BeginSpinerTransportClientRpc(int playerId)
        {
            LogRPC("Client", nameof(BeginSpinerTransportClientRpc));
            ChangeState(State.Transport, playerId);
        }

        // ===================== RUNAWAY =====================
        [ServerRpc(RequireOwnership = false)]
        private void BeginSpinerRunawayServerRpc()
        {
            LogRPC("Server", nameof(BeginSpinerRunawayServerRpc));
            ChangeState(State.Runaway);
            BeginSpinerRunawayClientRpc();
        }

        [ClientRpc]
        private void BeginSpinerRunawayClientRpc()
        {
            LogRPC("Client", nameof(BeginSpinerRunawayClientRpc));
            ChangeState(State.Runaway);
        }



        private void LogCriticalReferences()
        {
            //SpinerPlugin.LogInfo("[Spiner] Verifying critical references...");
            //SpinerPlugin.LogInfo("- NavMeshAgent: " + (agent != null ? "Present" : "Missing"));
            //SpinerPlugin.LogInfo("- Animator: " + (creatureAnimator != null ? "Present" : "Missing"));
            //SpinerPlugin.LogInfo("- NetworkObject: " + (thisNetworkObject != null ? "Present" : "Missing"));
        }


        // gestionaire animation
        private string _currentAnimState = "";

        public void SetAnimation(string animType)
        {
            if (creatureAnimator == null || string.IsNullOrEmpty(animType)) return;

            // Log global
            var speed = agent != null ? agent.velocity.magnitude : -1f;
            var stateInfo = creatureAnimator.GetCurrentAnimatorStateInfo(0);
            //SpinerPlugin.LogInfo($"[Anim ▶] Switching to: {animType} | Speed: {speed:F2} | CurrentState: {stateInfo.shortNameHash} | IsInTransition: {creatureAnimator.IsInTransition(0)}");

            // Marche : géré par bool
            if (animType == "Walk")
            {
                if (_currentAnimState == "Walk") return;
                creatureAnimator.SetBool(animWalk, true);
                _currentAnimState = "Walk";
            }
            else if (animType == "Idle")
            {
                if (_currentAnimState == "Idle") return;
                creatureAnimator.SetBool(animWalk, false);
                _currentAnimState = "Idle";
            }
            else if (animType == "Death")
            {
                if (_currentAnimState == "Death") return;
                creatureAnimator.SetBool(animWalk, false);   // coupe la marche
                creatureAnimator.SetBool(animDeath, true);   // active la mort
                _currentAnimState = "Death";
            }
            else if (animType == "Resurrect")
            {
                if (_currentAnimState == "Resurrect") return;
                creatureAnimator.SetBool(animWalk, false);   // coupe la marche
                creatureAnimator.SetBool(animDeath, false);   // active la mort
                _currentAnimState = "Resurrect";
            }
            else if (animType == "Death2")
            {
                if (_currentAnimState == "Death2") return;
                creatureAnimator.SetBool(animWalk, false);
                creatureAnimator.SetBool(animDeath2, true);  // variante mort 2
                _currentAnimState = "Death2";
            }
            else
            {
                // Reset la bool Walk si on part sur un trigger
                creatureAnimator.SetBool(animWalk, false);
                _currentAnimState = animType; // utile si tu veux tracer

                string? triggerName = animType switch
                {
                    "Stalk" => animStalk,
                    "Attack" => animAttack,
                    "Grab" => animGrab,
                    "Hurt" => animHurt,
                    "Hurt2" => animHurt2,
                    "Hurt3" => animHurt3,
                    "Spin" => animSpin,
                    "SpinTest" => animSpinTest,
                    _ => null
                };

                if (!string.IsNullOrEmpty(triggerName))
                {
                    creatureAnimator.ResetTrigger(triggerName); // en cas de spam
                    creatureAnimator.SetTrigger(triggerName);
                }
            }
        }




        /// Appel unique pour lancer anim + SFX pour un événement donné.
        /// Entièrement data‑driven via fxTable.
        /// </summary>
        // à mettre en champ privé
        private bool _phase2AudioTintApplied = false;

        private void TriggerFx(SpinerEvent fx)
        {
            AudioClip? clip = null;
            AudioSource src = sfxAudioSource; // SFX uniquement (pas les pas)

            switch (fx)
            {
                case SpinerEvent.Kidnap: clip = kidnappingSound; break;
                case SpinerEvent.Death: clip = deathSound; break;
                case SpinerEvent.Creep: clip = creepSound; break;
                case SpinerEvent.Transport: clip = transportSound; break;
                case SpinerEvent.Detection: clip = detectionSound; break;
                case SpinerEvent.Runaway: clip = runawaySound; break;
                case SpinerEvent.Roam:
                    {
                        AudioClip[] roamClips = { roamingSound, roamingSound2, roamingSound3 };
                        int index = Mathf.Abs((GetInstanceID() + Time.frameCount) % roamClips.Length);
                        clip = roamClips[index];
                        break;
                    }
                default: return;
            }

            if (clip == null || src == null)
            {
                SpinerPlugin.LogWarning(
                    $"[FX] {fx}: clip/src null (clip={clip?.name ?? "null"}, src={src?.name ?? "null"})"
                );
                return;
            }

            // applique le pitch UNE FOIS quand on entre en phase 2 (pas de restore)
            if (_phase2Lethal && !_phase2AudioTintApplied)
            {
                src.pitch = Phase2Pitch;               // ex: 0.10f pour test bien audible
                _phase2AudioTintApplied = true;
            }

            // anti-spam simple (même clip dans <0.05s)
            if (clip == _lastClip && Time.time - _lastTime < 0.05f) return;

            float vol = _phase2Lethal ? Phase2VolMult : 1f;
            SpinerPlugin.LogInfo($"[FX] {fx} | phase2={_phase2Lethal} | src={src.name} | clip={clip.name} | pitch={src.pitch:0.00}");
            src.PlayOneShot(clip, vol);

            _lastClip = clip;
            _lastTime = Time.time;
        }

    }
}
