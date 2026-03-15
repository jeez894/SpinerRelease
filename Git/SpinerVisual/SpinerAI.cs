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
        [SerializeField] private AudioSource walkAudioSource = null!;
        [SerializeField] private AudioSource sfxAudioSource = null!;

        // ─────────────────────────────────────────────
        //  Core SFX clips
        // ─────────────────────────────────────────────
        [SerializeField] private AudioClip kidnappingSound = null!;
        [SerializeField] private AudioClip moveSound = null!;
        [SerializeField] private AudioClip creepSound = null!;
        [SerializeField] private AudioClip transportSound = null!;
        [SerializeField] private AudioClip runawaySound = null!;
        [SerializeField] private AudioClip detectionSound = null!;
        [SerializeField] private AudioClip deathSound = null!;
        [SerializeField] private AudioClip roamingSound = null!;
        [SerializeField] private AudioClip roamingSound2 = null!;
        [SerializeField] private AudioClip roamingSound3 = null!;

        [SerializeField] private AudioClip quackKidnappingSound = null!;
        [SerializeField] private AudioClip quackMoveSound = null!;
        [SerializeField] private AudioClip quackCreepSound = null!;
        [SerializeField] private AudioClip quackTransportSound = null!;
        [SerializeField] private AudioClip quackRunawaySound = null!;
        [SerializeField] private AudioClip quackDetectionSound = null!;
        [SerializeField] private AudioClip quackDeathSound = null!;
        [SerializeField] private AudioClip quackRoamingSound = null!;
        [SerializeField] private AudioClip quackRoamingSound2 = null!;
        [SerializeField] private AudioClip quackRoamingSound3 = null!;

        private AudioClip currentKidnappingSound = null!;
        private AudioClip currentMoveSound = null!;
        private AudioClip currentCreepSound = null!;
        private AudioClip currentTransportSound = null!;
        private AudioClip currentRunawaySound = null!;
        private AudioClip currentDetectionSound = null!;
        private AudioClip currentDeathSound = null!;
        private AudioClip currentRoamingSound = null!;
        private AudioClip currentRoamingSound2 = null!;
        private AudioClip currentRoamingSound3 = null!;


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
        private const float transportReleaseDelay = 15f;
        private bool hasReceivedDamageRecently = false;
        private bool isAvoidingThreat = false;
        private Vector3 currentAvoidPoint;
        private int hurtAnimIndex = 0;

        private Transform transportTargetNode;
        private float getPathToTransportNodeInterval;

        // ======= CFG VALUE ========

        // Cache des valeurs reçues
        private int _cfgMaxHP;
        private float _cfgRoamVolume;
        private bool _cfgDarkMode;
        private float _cfgDarkReviveDelay;
        private float _cfgDarkKillTime;
        private bool _cfgApplied;
        private bool _cfgSoundStyle;
        // dark mode runtime
        private bool _feignDeathActive;
        private float _feignDeathTimer;
        private bool _phase2Lethal;
        private bool _killTimerActive;
        private float _kidnapKillTimer;

        // =============== STALKING METER ===============
        private float stalkingMeter = 0f;
        private float stalkingMeterMax = 15f;
        private float stalkingMeterIncrement = 1f;
        private float stalkingMeterDecrement = 0.5f;

        // --- Réglages LOS corridor ---
        private const float losCorridorOffset1 = 0.25f;  // gauche/droite
        private const float losCorridorOffset2 = 0.50f;  // gauche+/droite+
        private const float losLogInterval = 0.5f;
        private float _nextLosLogTime_ISee = 0f;



        // ─────────────────────────────────────────────
        //  FX system — one adaptive entry point
        // ─────────────────────────────────────────────
        public enum SpinerEvent { None, Kidnap, Death, Creep, Detection, Transport, Spin, Roam, Runaway }
        private AudioClip _lastClip = null!;
        private float _lastTime;
        private const float Phase2Pitch = 0.5f;
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
                _cfgSoundStyle = SpinerPlugin.SoundStyle.Value;
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
                _cfgSoundStyle = SpinerPlugin.SoundStyle.Value;
                _cfgApplied = true;
            }

            enemyHP = _cfgMaxHP;
            ApplySoundStyle();
        }

        public override void Start()
        {
            base.Start();

            if (!agent.isOnNavMesh)

            LogCriticalReferences();

            if (creatureAnimator == null)
            {
                creatureAnimator = GetComponent<Animator>();
                if (creatureAnimator == null)
                {
                    //SpinerPlugin.LogError($"[Init] ❌ Animator is NULL on object {gameObject.name} ({GetInstanceID()})");
                }
                else
                {
                    //SpinerPlugin.LogWarning($"[Init] ⚠ Animator was missing but assigned via GetComponent");
                }
            }


            // Initialisation
            playerStealthMeters = new float[StartOfRound.Instance.allPlayerScripts.Length];
            randomGenerator = new System.Random(StartOfRound.Instance.randomMapSeed + thisEnemyIndex);
            spinerBehaviourStateIndex = (int)State.Patrol;
        }

        public override void Update()
        {
            if (stunNormalizedTimer > 0f)
                return;
            if (agent.destination != lastLoggedDestination)
                lastLoggedDestination = agent.destination;

            base.Update();
            StuckSafeguardTick();
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
                            //SpinerPlugin.LogInfo($"[Update] ▶ Target acquired during patrol: {target.name}");

                            // Côté serveur : on notifie les clients
                            if (IsServer)
                            {
                                BeginSpinerStalkClientRpc(index);
                            }
                            // Côté client : on demande au serveur de lancer le stalk
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
                if (!walkAudioSource.isPlaying)
                {
                    walkAudioSource.volume = 0.75f;
                    walkAudioSource.Play();
                }
            }
            else
            {
                if (walkAudioSource.isPlaying)
                    walkAudioSource.Stop();
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

        private void ApplySoundStyle()
        {
            if (_cfgSoundStyle)
            {
                currentKidnappingSound = quackKidnappingSound;
                currentMoveSound = quackMoveSound;
                currentCreepSound = quackCreepSound;
                currentTransportSound = quackTransportSound;
                currentRunawaySound = quackRunawaySound;
                currentDetectionSound = quackDetectionSound;
                currentDeathSound = quackDeathSound;
                currentRoamingSound = quackRoamingSound;
                currentRoamingSound2 = quackRoamingSound2;
                currentRoamingSound3 = quackRoamingSound3;
            }
            else
            {
                currentKidnappingSound = kidnappingSound;
                currentMoveSound = moveSound;
                currentCreepSound = creepSound;
                currentTransportSound = transportSound;
                currentRunawaySound = runawaySound;
                currentDetectionSound = detectionSound;
                currentDeathSound = deathSound;
                currentRoamingSound = roamingSound;
                currentRoamingSound2 = roamingSound2;
                currentRoamingSound3 = roamingSound3;
            }
        }


        public override void DoAIInterval()
        {
            if (stunNormalizedTimer > 0f)
                return;
            //SpinerPlugin.LogInfo($"[DoAIInterval] Current position: {transform.position} | Current destination: {agent.destination}");
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead) return;

            if (IsServer && StartOfRound.Instance.shipIsLeaving && (State)spinerBehaviourStateIndex != State.Runaway)
            {
                //SpinerPlugin.LogWarning("[EndOfDay] Ship is leaving → forcing Runaway (release if needed).");
                BeginSpinerRunawayServerRpc(); // ou ChangeState(State.Runaway) si tu veux sans RPC
                return;
            }

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
                    {
                        if (transportingTargetId != -1)
                        {
                            StartTransport(transportingTargetId);
                        }
                        else
                        {
                            ChangeState(State.Patrol);
                        }
                    }
                    else
                    {
                        PerformTransport();

                        if ((State)spinerBehaviourStateIndex != State.Transport || !isTransportActive)
                            break;

                        var threats = new List<GameObject>();
                        threats.AddRange(GetAllAlivePlayerObjects(exclude: inSpecialAnimationWithPlayer));
                        threats.AddRange(GetAllAliveEnemyObjects());

                        if (!isAvoidingThreat)
                        {
                            GameObject visibleThreat = CheckLineOfSight(
                                threats,
                                45f,
                                12,
                                2f,
                                this.eye
                            );

                            if (visibleThreat != null)
                            {
                                Vector3 origin = transform.position;
                                Vector3 awayDir = (origin - visibleThreat.transform.position).normalized;
                                awayDir.y = 0f;

                                if (awayDir.sqrMagnitude > 0.01f)
                                {
                                    bool foundAvoidPoint = false;
                                    Vector3 validAvoidPoint = Vector3.zero;

                                    float[] distances = { 5f, 7f, 9f, 11f, 13f };
                                    float[] angleOffsets = { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 90f, -90f };

                                    foreach (float distance in distances)
                                    {
                                        foreach (float angle in angleOffsets)
                                        {
                                            Vector3 testDir = Quaternion.Euler(0f, angle, 0f) * awayDir;
                                            Vector3 rawCandidate = origin + testDir * distance;

                                            if (NavMesh.SamplePosition(rawCandidate, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                                            {
                                                NavMeshPath path = new NavMeshPath();
                                                if (NavMesh.CalculatePath(origin, hit.position, NavMesh.AllAreas, path) &&
                                                    path.status == NavMeshPathStatus.PathComplete)
                                                {
                                                    validAvoidPoint = hit.position;
                                                    foundAvoidPoint = true;
                                                    break;
                                                }
                                            }
                                        }

                                        if (foundAvoidPoint)
                                            break;
                                    }

                                    if (foundAvoidPoint)
                                    {
                                        currentAvoidPoint = validAvoidPoint;
                                        SetDestinationToPosition(currentAvoidPoint, true);
                                        isAvoidingThreat = true;
                                        break;
                                    }
                                }
                            }

                            // Pas de menace vue : choisir / conserver un vrai point de transport lointain
                            if (transportTargetNode == null ||
                                Vector3.Distance(transform.position, transportTargetNode.position) < 2f)
                            {
                                transportTargetNode = base.ChooseFarthestNodeFromPosition(
                                    transform.position,
                                    false,
                                    0,
                                    false,
                                    50,
                                    false
                                );
                            }

                            if (transportTargetNode != null &&
                                Time.realtimeSinceStartup - getPathToTransportNodeInterval > 1f)
                            {
                                targetNode = transportTargetNode;
                                SetDestinationToPosition(transportTargetNode.position, true);
                                getPathToTransportNodeInterval = Time.realtimeSinceStartup;
                            }
                        }
                        else
                        {
                            if (Vector3.Distance(transform.position, currentAvoidPoint) < 2f)
                            {
                                isAvoidingThreat = false;
                                currentAvoidPoint = Vector3.zero;

                                if (transportTargetNode == null ||
                                    Vector3.Distance(transform.position, transportTargetNode.position) < 2f)
                                {
                                    transportTargetNode = base.ChooseFarthestNodeFromPosition(
                                        transform.position,
                                        false,
                                        0,
                                        false,
                                        50,
                                        false
                                    );
                                }

                                if (transportTargetNode != null)
                                {
                                    targetNode = transportTargetNode;
                                    SetDestinationToPosition(transportTargetNode.position, true);
                                    getPathToTransportNodeInterval = Time.realtimeSinceStartup;
                                }
                            }
                        }
                    }
                    break;
            }
        }

        private void ResetAllFlags()
        {
            //SpinerPlugin.LogInfo("[ResetAllFlags] Resetting all AI flags");

            // --- états globaux ---
            isRunawayActive = false;
            isPatrolActive = false;
            isStalkingActive = false;
            isKidnappingActive = false;
            isTransportActive = false;

            // --- stalking movement system ---
            stalkingMovementActive = false;
            stalkingMoveTarget = Vector3.zero;
            stalkingMoveMode = 0;
            _stalkMoveStartTime = 0f;

            // --- nav / movement ---
            overrideSpeed = false;

            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = false;

            moveTowardsDestination = false;
            movingTowardsTargetPlayer = false;

            // --- transport ---
            transportReleaseTimer = 0f;

            //SpinerPlugin.LogInfo("[ResetAllFlags] ✅ All flags reset cleanly");
        }



        // ✅ Fonction centrale pour gérer la vitesse dynamiquement
        private void UpdateSpeed()
        {
            if (overrideSpeed)
            {
                //SpinerPlugin.LogInfo($"[UpdateSpeed] Skipping speed update (override active). Current speed: {agent.speed}");
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
                    agent.speed = 6f;
                    break;
                case State.Transport:
                    agent.speed = 4f;
                    break;
            }

            //SpinerPlugin.LogInfo($"[UpdateSpeed] Speed set to {agent.speed} for state {((State)spinerBehaviourStateIndex)}");
        }

        // Appel de `UpdateSpeed()` dans chaque changement d’état
        private void ChangeState(State newState, int playerId = -1)
        {
            if (spinerBehaviourStateIndex == (int)newState) return;

            //SpinerPlugin.LogInfo($"[Spiner] State changed: {((State)spinerBehaviourStateIndex)} → {newState}");

            if (newState == State.Runaway && inSpecialAnimationWithPlayer != null)
            {
                //SpinerPlugin.LogInfo("[Spiner] Force releasing player before switching to Runaway.");
                RequestReleaseTransportServer(); // Nettoyage propre
            }

            // 1️ Stopper l’état précédent proprement AVANT de reset les flags
            switch ((State)spinerBehaviourStateIndex)
            {
                case State.Runaway: StopRunaway(); break;
                case State.Patrol: StopPatrol(); break;
                case State.Stalking: StopStalking(); break;
                case State.Kidnapping: StopKidnapping(); break;
                case State.Transport: StopTransport(); break;
            }

            // 2️ Réinitialiser les flags APRÈS avoir stoppé l’ancien état
            ResetAllFlags();

            spinerBehaviourStateIndex = (int)newState;
            UpdateSpeed(); // Mettre à jour la vitesse immédiatement après le changement

            // 3️⃣ Activer le nouvel état
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

            enemyHP -= force;
            //SpinerPlugin.LogInfo($"[Combat] Enemy HP remaining: {enemyHP}");

            if (enemyHP <= 0 && !isEnemyDead && IsOwner)
            {
                SpinerPlugin.LogInfo("[Combat] Enemy HP reached 0 → Killing enemy.");
                KillEnemyOnOwnerClient(false);
                return;
            }

            if (!isEnemyDead)
            {
                string[] hurtAnims = { "Hurt", "Hurt2", "Hurt3" };
                string anim = hurtAnims[hurtAnimIndex];
                SetAnimation(anim);
                //SpinerPlugin.LogInfo($"[Combat] Playing hurt animation: {anim}");
                hurtAnimIndex = (hurtAnimIndex + 1) % hurtAnims.Length;
            }

            if (!IsServer) return;

            //SpinerPlugin.LogInfo("[Combat] Enemy took damage → flagging for Runaway.");
            hasReceivedDamageRecently = true;
        }


        public override void KillEnemy(bool destroy = false)
        {
            //SpinerPlugin.LogInfo("enter kill enemy");
            ChangeState(State.Patrol);
            if (_cfgDarkMode && !_feignDeathActive && !_phase2Lethal && !destroy)
            {
                //SpinerPlugin.LogInfo("killenemy with spin condition called");
                StartFeignDeath();
                return;
            }
            //SpinerPlugin.LogInfo("enter death logic in killenemy");
            RequestReleaseTransportServer();
            CancelSpecialAnimationWithPlayer();
            base.KillEnemy(false);
            SetAnimation("Death");
        }

        private void StartFeignDeath()
        {
            //SpinerPlugin.LogInfo("enter start feign death");
            isEnemyDead = true;
            _feignDeathActive = true;
            _feignDeathTimer = _cfgDarkReviveDelay;

            SetAnimation("Death");
            TriggerFx(SpinerEvent.Death);

            // neutraliser le mob
            agent.enabled = false;
            RequestReleaseTransportServer();
            CancelSpecialAnimationWithPlayer();
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
                if (!agent.isOnNavMesh) agent.Warp(transform.position);
            }
            enemyHP = _cfgMaxHP;
            //SpinerPlugin.LogInfo($"[DarkMode] Revived → HP set to {enemyHP}");
            SetAnimation("Resurrect");

            ChangeState(State.Runaway);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r.material.HasProperty("_Color"))
                    r.material.color = Color.Lerp(r.material.color, Color.black, 0.7f); // 0.7 = taux d’assombrissement
                if (r.material.HasProperty("_EmissionColor"))
                    r.material.SetColor("_EmissionColor", r.material.GetColor("_EmissionColor") * 0.5f);
            }
        }

        // stuck safe guard //

        private Vector3 _stuckLastPos;
        private float _stuckTimer;
        private float _stuckSampleT;
        private bool _stuckInit;

        [SerializeField] private float stuckSampleInterval = 0.25f;
        [SerializeField] private float stuckMinMoveDist = 0.5f;    // distance mini par sample (0.25s)
        [SerializeField] private float stuckTimeToTrigger = 5.0f;  // 5s

        private void StuckSafeguardTick()
        {
            if (!IsServer) return;

            if (agent == null || !agent.enabled) return;
            if (isEnemyDead || _feignDeathActive) return;
            if (stunNormalizedTimer > 0f) return;

            // Si tu veux exclure certains états :
            var st = (State)spinerBehaviourStateIndex;
            if (st == State.Transport || st == State.Kidnapping) return;

            // Init (évite _stuckLastPos=(0,0,0) au départ)
            if (!_stuckInit)
            {
                _stuckInit = true;
                _stuckLastPos = transform.position;
                _stuckTimer = 0f;
                _stuckSampleT = 0f;
                return;
            }

            // Sample timing
            _stuckSampleT += Time.deltaTime;
            if (_stuckSampleT < stuckSampleInterval) return;
            _stuckSampleT = 0f;

            // Garde-fous : ne pas considérer "stuck" si c'est normal de ne pas bouger
            if (agent.isStopped) return;
            if (agent.pathPending) return;
            if (!agent.hasPath) return;
            if (agent.remainingDistance <= agent.stoppingDistance + 0.1f) return;

            float moved = Vector3.Distance(transform.position, _stuckLastPos);
            _stuckLastPos = transform.position;

            // Si ça bouge => reset compteur
            if (moved >= stuckMinMoveDist)
            {
                _stuckTimer = 0f;
                return;
            }

            // Sinon ça bouge pas => on incrémente
            _stuckTimer += stuckSampleInterval;
            if (_stuckTimer < stuckTimeToTrigger) return;

            // Déclenchement : bloqué >= Xs
            _stuckTimer = 0f;

            // --- RECOVERY : replacer sur point NavMesh safe proche ---
            Vector3 origin = transform.position;

            bool found = false;
            Vector3 safePos = origin;

            if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit1, 6f, UnityEngine.AI.NavMesh.AllAreas))
            { safePos = hit1.position; found = true; }
            else if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit2, 12f, UnityEngine.AI.NavMesh.AllAreas))
            { safePos = hit2.position; found = true; }
            else if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit3, 20f, UnityEngine.AI.NavMesh.AllAreas))
            { safePos = hit3.position; found = true; }

            if (found)
            {
                agent.ResetPath();
                agent.Warp(safePos);

                // éviter un faux stuck juste après warp
                _stuckLastPos = transform.position;

                // relancer une destination pour ne pas rester sans path
                StartSearch(transform.position);
            }
            else
            {
                // Pas de point NavMesh trouvé => on ne fait rien (ou fallback plus tard)
            }
        }

        private void RequestReleaseTransportServer()
        {
            SpinerPlugin.LogWarning(
                $"[RELEASE SERVER] RequestReleaseTransportServer called | " +
                $"IsServer={IsServer} | playerId={transportingTargetId} | " +
                $"hasPlayer={(inSpecialAnimationWithPlayer != null)}"
            );

            if (!IsServer) return;
            if (inSpecialAnimationWithPlayer == null || transportingTargetId < 0) return;

            var p = inSpecialAnimationWithPlayer;

            // ---- Calcul safe position ----
            Vector3 origin = (kidnapCarryPoint != null) ? kidnapCarryPoint.position : p.transform.position;
            Vector3 safePos = origin;

            if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit1, 4f, UnityEngine.AI.NavMesh.AllAreas)) safePos = hit1.position;
            else if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit2, 8f, UnityEngine.AI.NavMesh.AllAreas)) safePos = hit2.position;
            else if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit3, 16f, UnityEngine.AI.NavMesh.AllAreas)) safePos = hit3.position;
            else if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out var hit4, 16f, UnityEngine.AI.NavMesh.AllAreas)) safePos = hit4.position;

            safePos += Vector3.up * 0.1f;

            SpinerPlugin.LogWarning($"[RELEASE SERVER] Safe position computed: {safePos}");

            int pid = transportingTargetId;

            // Applique local serveur
            ApplyReleaseTransportLocal(pid, safePos);

            SpinerPlugin.LogWarning($"[RELEASE SERVER] Broadcasting release to clients for player {pid}");

            // Broadcast clients
            ForceReleaseTransportClientRpc(pid, safePos);
        }
        private void ApplyReleaseTransportLocal(int playerId, Vector3 safePos)
        {
            SpinerPlugin.LogWarning(
                $"[RELEASE APPLY] Applying local release | " +
                $"Instance={(IsServer ? "SERVER" : "CLIENT")} | playerId={playerId}"
            );

            transportReleaseTimer = 0f;
            StopLethalTimer();

            isTransportActive = false;
            transportingTargetId = -1;

            if (playerId < 0 || playerId >= StartOfRound.Instance.allPlayerScripts.Length)
            {
                inSpecialAnimationWithPlayer = null;
                kidnapCarryPoint = null;
                return;
            }

            var p = StartOfRound.Instance.allPlayerScripts[playerId];
            if (p == null)
            {
                inSpecialAnimationWithPlayer = null;
                kidnapCarryPoint = null;
                return;
            }

            if (p.transform.parent != null)
                p.transform.SetParent(null, true);

            p.inSpecialInteractAnimation = false;
            p.inAnimationWithEnemy = null;
            p.snapToServerPosition = false;

            p.transform.position = safePos;

            if (p == GameNetworkManager.Instance.localPlayerController)
            {
                var jumpAction = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump", false);
                if (jumpAction != null) jumpAction.Enable();
            }

            if (inSpecialAnimationWithPlayer == p)
                inSpecialAnimationWithPlayer = null;

            kidnapCarryPoint = null;
        }



        // =============== ÉTATS ===============
        private void StartRunaway()
        {
            if (isRunawayActive) return;
            stalkingTargetId = -1;
            kidnappingTargetId = -1;
            transportingTargetId = -1;
            chasingPlayer = null;

            //SpinerPlugin.LogInfo("[Runaway] 🏃 Start Runaway!");

            isRunawayActive = true;
            UpdateSpeed();

            // Trouver le point le plus éloigné de SA PROPRE POSITION
            Transform farthestNode = ChooseFarthestNodeFromPosition(transform.position);

            if (farthestNode != null)
            {
                //SpinerPlugin.LogInfo($"[Runaway] 📍 Escaping to {farthestNode.position}");
                base.SetDestinationToPosition(farthestNode.position, true);
            }
            else
            {
                //SpinerPlugin.LogWarning("[Runaway] ❌ No valid escape node found! Using fallback.");
                Vector3 fallbackPos = transform.position + (UnityEngine.Random.insideUnitSphere * 10f);
                fallbackPos.y = transform.position.y;
                base.SetDestinationToPosition(fallbackPos, true);
            }

            // Lancer un chrono pour revenir en Patrol
            StartCoroutine(ExitRunawayAfterDelay());
        }

        private void StopRunaway()
        {
            if (!isRunawayActive) return;
            //SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Runaway");
            isRunawayActive = false;
        }

        public void StartPatrol()
        {
            if (isPatrolActive) return;
            stalkingTargetId = -1;
            kidnappingTargetId = -1;
            transportingTargetId = -1;
            chasingPlayer = null;
            //SpinerPlugin.LogInfo("[Spiner] 🚶 Start Patrol");
            isPatrolActive = true;
            UpdateSpeed();
            StartSearch(transform.position);
        }
        public void StopPatrol()
        {
            if (!isPatrolActive) return;
            //SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Patrol");
            StopSearch(currentSearch, true);
            isPatrolActive = false;
        }

        private void StartStalking(int playerId)
        {
            stalkingTargetId = playerId;

            if (isStalkingActive) return;
            //SpinerPlugin.LogInfo($"[Spiner] 🕵️ Start Stalking on Player {playerId}");
            isStalkingActive = true;
            chasingPlayer = StartOfRound.Instance.allPlayerScripts[playerId];
            LookAtPlayer();
            UpdateSpeed();
            StalkingBehavior();
        }
        private void StopStalking()
        {
            if (!isStalkingActive) return;

            isStalkingActive = false;

            // Reset cible (RPC-stable car StopStalking est appelé via ChangeState côté client aussi)
            stalkingTargetId = -1;

            // Reset movement stalking
            stalkingMoveMode = 0;
            stalkingMovementActive = false;
            stalkingMoveTarget = Vector3.zero;

            ResetRotation();
        }

        private void StartKidnapping(int playerId)
        {
            kidnappingTargetId = playerId;

            if (isKidnappingActive) return;
            SpinerPlugin.LogInfo("[Kidnapping] 🚨 Entering Kidnapping mode!");
            isKidnappingActive = true;
            UpdateSpeed();

            // Appel à la méthode dédiée pour gérer le Kidnapping
            PerformKidnapping();
        }

        private void StopKidnapping()
        {
            if (!isKidnappingActive) return;
            //SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Kidnapping");
            isKidnappingActive = false;
        }
        private void StartTransport(int playerId)
        {
            if (inSpecialAnimationWithPlayer == null && playerId >= 0 && playerId < StartOfRound.Instance.allPlayerScripts.Length)
                inSpecialAnimationWithPlayer = StartOfRound.Instance.allPlayerScripts[playerId];

            transportingTargetId = playerId;
            transportTargetNode = null;
            getPathToTransportNodeInterval = 0f;

            if (isTransportActive) return;

            //SpinerPlugin.LogInfo("[Spiner] 🚛 Start Transport");
            isTransportActive = true;

            kidnapCarryPoint = transform.Find("KidnapCarryPoint");
            //SpinerPlugin.LogInfo("[StartTransport] 🧭 KidnapCarryPoint reassigned: " + (kidnapCarryPoint != null));

            if (inSpecialAnimationWithPlayer != null && kidnapCarryPoint != null)
            {
                // Attache physique
                inSpecialAnimationWithPlayer.transform.SetParent(kidnapCarryPoint);
                inSpecialAnimationWithPlayer.transform.localPosition = Vector3.zero;
                inSpecialAnimationWithPlayer.transform.localRotation = Quaternion.identity;

                // Verrouillage état
                inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
                inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;
                inSpecialAnimationWithPlayer.snapToServerPosition = true;

                // Blocage du saut – uniquement joueur local
                if (inSpecialAnimationWithPlayer == GameNetworkManager.Instance.localPlayerController)
                {
                    var jumpAction = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump", false);
                    if (jumpAction != null) jumpAction.Disable();
                }

                //SpinerPlugin.LogInfo("[StartTransport] ✅ Player successfully attached and configured.");
            }
            else
            {
                //SpinerPlugin.LogWarning("[StartTransport] ⚠️ Cannot attach player: missing reference or carry point.");
            }

            UpdateSpeed();
        }

        private void StopTransport()
        {
            if (!isTransportActive) return;
            //SpinerPlugin.LogInfo("[Spiner] 🛑 Stop Transport");

            isTransportActive = false;
            transportTargetNode = null;
            getPathToTransportNodeInterval = 0f;
            isAvoidingThreat = false;
            currentAvoidPoint = Vector3.zero;

            StopSearch(currentSearch, true);
        }

        private IEnumerator ExitRunawayAfterDelay()
        {
            yield return new WaitForSeconds(5f);

            //SpinerPlugin.LogInfo("[Runaway] ⏳ Timeout reached, returning to Patrol.");
            ChangeState(State.Patrol);
        }


        ////////////////////////////////////////////////////////////////////////////////////
        // SPINER STALKING LOGIC: offset behind player + hide behind obstacle if discovered
        ////////////////////////////////////////////////////////////////////////////////////

        // ===================== STALKING CORE ======================
        // Appelée dans StartStalking() et à chaque DoAIInterval pendant Stalking
        // ---- champs (minimum vital) ----
        private float nextLosAvgTime = 0f;
        private int losSum = 0;
        private int losCount = 0;
        private int losScore = 5;

        // movement gate
        private bool stalkingMovementActive = false;
        private Vector3 stalkingMoveTarget = Vector3.zero;

        // 0 = none, 1 = approach, 2 = hide
        private int stalkingMoveMode = 0;

        private void StalkingBehavior()
        {
            // Drop target si le joueur est outside
            // (serveur autoritaire ; si tu veux que ça s’applique aussi en solo client, enlève le IsServer)
            if (IsServer && isStalkingActive && stalkingTargetId >= 0 &&
                stalkingTargetId < StartOfRound.Instance.allPlayerScripts.Length)
            {
                var p = StartOfRound.Instance.allPlayerScripts[stalkingTargetId];
                if (p == null || p.isPlayerDead || !p.gameObject.activeInHierarchy || !p.isInsideFactory)
                {
                    BeginSpinerPatrolServerRpc();
                    return;
                }
            }
            int losRaw = ISeeMyTarget();      // 0..5 (à chaque frame)
            bool playerSeesMe = MyTargetSeesMe();

            // meter précis -> RAW
            bool iSeeThePlayer = (losRaw > 0);
            UpdateStalkingMeter(iSeeThePlayer);

            // moyenne -> accumulateur
            losSum += losRaw;
            losCount++;

            // toutes les X secondes : calc moyenne et arrondi
            if (Time.time >= nextLosAvgTime)
            {
                nextLosAvgTime = Time.time + 0.5f;

                float avg = (losCount > 0) ? ((float)losSum / losCount) : 0f;
                losScore = Mathf.Clamp(Mathf.RoundToInt(avg), 0, 5);

                losSum = 0;
                losCount = 0;
            }

            // --- bible ---

            // LOS = 0  => APPROACH
            if (losScore == 0)
            {
                // si on est déjà en HIDE en cours, on le laisse préempter
                if (stalkingMovementActive && stalkingMoveMode == 2)
                {
                    HandleStalkingMovement(true, stalkingMoveTarget);
                    return;
                }

                if (!stalkingMovementActive || stalkingMoveMode != 1)
                {
                    Vector3 p = GetApproachPos();

                    if (p == Vector3.zero)
                    {
                        // pas de target valide => on reste idle/creep (stop propre)
                        stalkingMoveMode = 0;
                        HandleStalkingMovement(false, Vector3.zero, stopNow: true);
                        return;
                    }

                    stalkingMoveTarget = p;
                    stalkingMoveMode = 1;
                }

                HandleStalkingMovement(true, stalkingMoveTarget);
            }


            // LOS = 1
            else if (losScore == 1)
            {
                if (playerSeesMe)
                {
                    // CREEP (stop) : on stoppe même si un move est actif
                    stalkingMoveMode = 0;
                    HandleStalkingMovement(false, Vector3.zero, stopNow: true);
                }
                else // playerSeesMe == false
                {
                    if (stalkingMovementActive && stalkingMoveMode == 2)
                    {
                        HandleStalkingMovement(true, stalkingMoveTarget);
                        return;
                    }

                    if (!stalkingMovementActive || stalkingMoveMode != 1)
                    {
                        Vector3 p = GetApproachPos();

                        if (p == Vector3.zero)
                        {
                            stalkingMoveMode = 0;
                            HandleStalkingMovement(false, Vector3.zero, stopNow: true);
                            return;
                        }

                        stalkingMoveTarget = p;
                        stalkingMoveMode = 1;
                    }

                    HandleStalkingMovement(true, stalkingMoveTarget);
                }
            }

            // LOS = 2 => CREEP (stop)
            else if (losScore == 2)
            {
                stalkingMoveMode = 0;
                HandleStalkingMovement(false, Vector3.zero, stopNow: true);
            }

            // LOS = 3..5
            else // losScore >= 3
            {
                if (playerSeesMe)
                {
                    if (!stalkingMovementActive || stalkingMoveMode != 2)
                    {
                        Vector3 p = GetHidePos();

                        if (p == Vector3.zero)
                        {
                            stalkingMoveMode = 0;
                            HandleStalkingMovement(false, Vector3.zero, stopNow: true);
                            return;
                        }

                        stalkingMoveTarget = p;
                        //SpinerPlugin.LogInfo($"[Stalking] HIDE target={stalkingMoveTarget}");
                        stalkingMoveMode = 2;
                    }

                    HandleStalkingMovement(true, stalkingMoveTarget);
                }
                else
                {
                    // CREEP (stop)
                    stalkingMoveMode = 0;
                    HandleStalkingMovement(false, Vector3.zero, stopNow: true);
                }
            }
        }

        private float _stalkMoveStartTime = 0f; // safeguard timer
        private float _nextStalkMoveDbgTime = 0f; // (réutilisé uniquement pour safeguard si tu veux plus tard)

        private void HandleStalkingMovement(bool wantMove, Vector3 wantedTarget, bool stopNow = false)
        {
            // STOP (idempotent)
            if (stopNow || !wantMove)
            {
                //SpinerPlugin.LogInfo($"[StalkMove] STOP req | active={stalkingMovementActive} | curTarget={stalkingMoveTarget}");

                agent.ResetPath();
                agent.velocity = Vector3.zero;
                agent.isStopped = false;

                moveTowardsDestination = false;
                movingTowardsTargetPlayer = false;

                stalkingMovementActive = false;
                stalkingMoveTarget = Vector3.zero;
                stalkingMoveMode = 0;
                _stalkMoveStartTime = 0f;
                return;
            }

            // wantMove == true
            if (wantedTarget == Vector3.zero)
            {
                //SpinerPlugin.LogInfo("[StalkMove] WANTMOVE but wantedTarget=ZERO -> ignore");
                return;
            }

            // START une fois
            if (!stalkingMovementActive)
            {
                agent.speed = 4f;

                // 🔧 sample start sur NavMesh (sinon CalculatePath peut mentir)
                NavMeshHit startHit;
                if (!NavMesh.SamplePosition(transform.position, out startHit, 2f, NavMesh.AllAreas))
                {
                    //SpinerPlugin.LogInfo("[StalkMove] START REFUSE (start off navmesh)");
                    stalkingMoveTarget = Vector3.zero; // évite retry spam
                    return;
                }
                Vector3 startPos = startHit.position;

                // 🔧 check path AVANT de lancer
                NavMeshPath path = new NavMeshPath();
                bool hasPath = NavMesh.CalculatePath(startPos, wantedTarget, NavMesh.AllAreas, path);
                if (!hasPath || path.status != NavMeshPathStatus.PathComplete)
                {
                    //SpinerPlugin.LogInfo($"[StalkMove] START REFUSE (path) | hasPath={hasPath} status={path.status} | start={startPos} | target={wantedTarget}");
                    stalkingMoveTarget = Vector3.zero; // évite retry spam
                    return;
                }

                stalkingMoveTarget = wantedTarget;

                bool ok = base.SetDestinationToPosition(stalkingMoveTarget, true);

                //SpinerPlugin.LogInfo($"[StalkMove] START req | ok={ok} | target={stalkingMoveTarget} | onNavMesh={agent.isOnNavMesh} dest={agent.destination}");

                if (!ok)
                {
                    //SpinerPlugin.LogInfo("[StalkMove] START FAIL -> reset flags");
                    stalkingMovementActive = false;
                    stalkingMoveTarget = Vector3.zero;
                    moveTowardsDestination = false;
                    movingTowardsTargetPlayer = false;
                    stalkingMoveMode = 0;
                    _stalkMoveStartTime = 0f;
                    return;
                }

                moveTowardsDestination = true;
                movingTowardsTargetPlayer = false;

                stalkingMovementActive = true;
                _stalkMoveStartTime = Time.time; // ✅ start safeguard
                return;
            }

            // TICK + arrivée + safeguard anti-blocage
            float arriveDist = 1.0f;
            float dist = Vector3.Distance(transform.position, stalkingMoveTarget);

            //SpinerPlugin.LogInfo($"[StalkMove] TICK | dist={dist:0.00} | target={stalkingMoveTarget} | hasPath={agent.hasPath} status={agent.pathStatus} vel={agent.velocity.magnitude:0.00}");

            // Arrivée normale
            if (dist <= arriveDist)
            {
                //SpinerPlugin.LogInfo($"[StalkMove] ARRIVED | target={stalkingMoveTarget}");

                agent.ResetPath();
                agent.velocity = Vector3.zero;

                moveTowardsDestination = false;
                movingTowardsTargetPlayer = false;

                stalkingMovementActive = false;
                stalkingMoveTarget = Vector3.zero;
                stalkingMoveMode = 0;
                _stalkMoveStartTime = 0f;
                return;
            }

            // Safeguard : si on reste trop longtemps sans “arriver”, on stoppe pour éviter un soft-lock
            float timeout = 5f; // 4 ou 5 comme tu veux
            if (_stalkMoveStartTime > 0f && (Time.time - _stalkMoveStartTime) >= timeout)
            {
                //SpinerPlugin.LogInfo($"[StalkMove] TIMEOUT -> force stop | dist={dist:0.00} | target={stalkingMoveTarget}");

                agent.ResetPath();
                agent.velocity = Vector3.zero;
                agent.isStopped = false;

                moveTowardsDestination = false;
                movingTowardsTargetPlayer = false;

                stalkingMovementActive = false;
                stalkingMoveTarget = Vector3.zero;
                stalkingMoveMode = 0;
                _stalkMoveStartTime = 0f;
                return;
            }
        }



        // Oriente immédiatement l'ennemi vers le joueur
        private void LookAtPlayer()
        {
            if (chasingPlayer == null) return;

            Vector3 direction = chasingPlayer.transform.position - transform.position;
            direction.y = 0;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = targetRotation;

            //SpinerPlugin.LogInfo($"[LookAtPlayer] Looking at player {chasingPlayer.name}");
        }

        // Réinitialise la rotation en quittant Stalking
        private void ResetRotation()
        {
            //SpinerPlugin.LogInfo("[ResetRotation] Resetting to default rotation.");
            transform.rotation = Quaternion.identity;
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
                    stalkingMeterAbortThreshold
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

                BeginSpinerKidnapServerRpc(stalkingTargetId);
            }

            if (stalkingMeter <= stalkingMeterAbortThreshold)
            {
                SpinerPlugin.LogInfo("[UpdateStalkingMeter] Meter dropped too low => switch to PATROL");
                stalkingMeter = 0f;
                lastLoggedStalkingSecond = -1f;

                BeginSpinerPatrolServerRpc();
            }
        }




        // ===================== VISION: IA => Joueur & Joueur => IA =====================
        private int ISeeMyTarget()
        {
            if (chasingPlayer == null)
            {
                //SpinerPlugin.LogInfo("[ISeeMyTarget] No chasingPlayer assigned, returning false");
                return 0;
            }

            Vector3 eyePos = this.eye.position;
            Vector3 targetPos = chasingPlayer.transform.position + Vector3.up * 1f;

            float dist = Vector3.Distance(eyePos, targetPos);
            if (dist > 50f)
            {
                //SpinerPlugin.LogInfo("[ISeeMyTarget] Out of range, returning false");
                return 0;
            }

            Vector3 dirToTarget = (targetPos - eyePos).normalized;
            float angle = Vector3.Angle(this.eye.forward, dirToTarget);
            if (angle > 70f)
            {
                //SpinerPlugin.LogInfo("[ISeeMyTarget] Out of FOV, returning false");
                return 0;
            }

            // ===== LOS "couloir" : 5 linecasts parallèles (centre, gauche, gauche+, droite, droite+) =====
            // On décale l'ORIGINE latéralement mais on garde la même direction (donc rayons parallèles).
            Vector3 right = Vector3.Cross(Vector3.up, dirToTarget).normalized;
            if (right.sqrMagnitude < 0.0001f) right = this.eye.right; // fallback rare

            Vector3 oC = eyePos;
            Vector3 oL1 = eyePos - right * losCorridorOffset1;
            Vector3 oL2 = eyePos - right * losCorridorOffset2;
            Vector3 oR1 = eyePos + right * losCorridorOffset1;
            Vector3 oR2 = eyePos + right * losCorridorOffset2;

            Vector3 endC = oC + dirToTarget * dist;
            Vector3 endL1 = oL1 + dirToTarget * dist;
            Vector3 endL2 = oL2 + dirToTarget * dist;
            Vector3 endR1 = oR1 + dirToTarget * dist;
            Vector3 endR2 = oR2 + dirToTarget * dist;

            int mask = StartOfRound.Instance.collidersAndRoomMaskAndDefault;

            bool clearC = !Physics.Linecast(oC, endC, mask);
            bool clearL1 = !Physics.Linecast(oL1, endL1, mask);
            bool clearL2 = !Physics.Linecast(oL2, endL2, mask);
            bool clearR1 = !Physics.Linecast(oR1, endR1, mask);
            bool clearR2 = !Physics.Linecast(oR2, endR2, mask);

            // ✅ score 0..5
            int score = 0;
            if (clearC) score++;
            if (clearL1) score++;
            if (clearL2) score++;
            if (clearR1) score++;
            if (clearR2) score++;

            // (optionnel) bool “comme avant” pour tes logs
            bool blocked = (score == 0);
            bool result = !blocked;

            //SpinerPlugin.LogInfo($"[ISeeMyTarget] Result={result}, blocked={blocked}");

            // Log throttlé (toutes les 0.5s max) : quels casts passent
            if (Time.time >= _nextLosLogTime_ISee)
            {
                _nextLosLogTime_ISee = Time.time + losLogInterval;
                string hits =
                    $"C:{(clearC ? 1 : 0)} " +
                    $"L:{(clearL1 ? 1 : 0)}{(clearL2 ? 1 : 0)} " +
                    $"R:{(clearR1 ? 1 : 0)}{(clearR2 ? 1 : 0)}";
                SpinerPlugin.LogInfo($"[ISeeMyTarget] LOS={result} | score={score}/5 | {hits}");
            }

            return score;
        }


        private bool MyTargetSeesMe()
        {
            if (chasingPlayer == null)
            {
                //SpinerPlugin.LogInfo("[MyTargetSeesMe] No chasingPlayer assigned, returning false");
                return false;
            }

            Transform playerCam = chasingPlayer.gameplayCamera.transform;
            Vector3 stalkerEyePos = this.eye.position;
            Vector3 playerCamPos = playerCam.position;

            float dist = Vector3.Distance(playerCamPos, stalkerEyePos);
            if (dist > 50f)
            {
                //SpinerPlugin.LogInfo("[MyTargetSeesMe] Out of range, returning false");
                return false;
            }

            Vector3 dirToStalker = (stalkerEyePos - playerCamPos).normalized;
            float angle = Vector3.Angle(playerCam.forward, dirToStalker);
            if (angle > 70f)
            {
                //SpinerPlugin.LogInfo("[MyTargetSeesMe] Out of FOV, returning false");
                return false;
            }

            bool blocked = Physics.Linecast(
                playerCamPos,
                stalkerEyePos,
                StartOfRound.Instance.collidersAndRoomMaskAndDefault
            );
            bool result = !blocked;
            //SpinerPlugin.LogInfo($"[MyTargetSeesMe] Result={result}, blocked={blocked}");
            return result;
        }

        // ===================== APPROCHE DERRIÈRE LE JOUEUR (OFFSET) =====================
        private Vector3 GetApproachPos()
        {
            if (chasingPlayer == null) return Vector3.zero;

            Vector3 enemyPos = transform.position;

            Transform p = chasingPlayer.transform;
            float backDist = 2f;
            float radius = 2.5f;        // rayon du cône autour du point "derrière"
            int samples = 15;
            float navSnap = 2.5f;

            // Point de base derrière le joueur
            Vector3 behindCenter = p.position - p.forward * backDist;

            // buffer path (évite alloc en boucle)
            NavMeshPath path = new NavMeshPath();

            // Petite fonction locale: tente de trouver un point atteignable dans un cône donné
            Vector3 TryCone(float angleDeg)
            {
                // on tire des directions autour de "back" (donc centré sur -forward du joueur)
                Vector3 backDir = (-p.forward).normalized;

                for (int i = 0; i < samples; i++)
                {
                    float t = (samples == 1) ? 0.5f : (float)i / (samples - 1);
                    float a = Mathf.Lerp(-angleDeg * 0.5f, angleDeg * 0.5f, t);

                    Vector3 dir = Quaternion.Euler(0f, a, 0f) * backDir;
                    Vector3 candidate = behindCenter + dir * radius;

                    // snap sur NavMesh
                    NavMeshHit hit;
                    if (!NavMesh.SamplePosition(candidate, out hit, navSnap, NavMesh.AllAreas))
                        continue;

                    // check path complet depuis l’ennemi
                    bool hasPath = NavMesh.CalculatePath(enemyPos, hit.position, NavMesh.AllAreas, path);
                    if (!hasPath || path.status != NavMeshPathStatus.PathComplete)
                        continue;

                    // ✅ trouvé
                    SpinerPlugin.LogInfo($"[GetApproachPos] PICK pos={hit.position} | cone={angleDeg} | pathComplete");
                    return hit.position;
                }

                return Vector3.zero;
            }

            // Pass 1: cône normal
            Vector3 pos = TryCone(120f);

            // Pass 2: élargi si rien
            if (pos == Vector3.zero)
                pos = TryCone(180f);

            if (pos == Vector3.zero)
                SpinerPlugin.LogInfo("[GetApproachPos] FAIL no pathComplete candidates (120->180)");

            return pos;
        }


        // ===================== CACHER DERRIÈRE UN OBSTACLE =====================
        // Renvoie une position de cachette SUR NAVMESH + atteignable (PathComplete), ou Vector3.zero si rien.
        private Vector3 GetHidePos()
        {
            if (chasingPlayer == null) return Vector3.zero;
            SpinerPlugin.LogInfo("[SubState] ENTER HIDE");
            Vector3 enemyPos = transform.position;
            Vector3 playerPos = chasingPlayer.transform.position;

            Vector3 dirToPlayer = (playerPos - enemyPos).normalized;

            List<Vector3> possibleNodes = new List<Vector3>();

            // ---- helper : ajoute des nodes sur une plage d’angles, sans doublons ----
            void AddNodes(float angleMin, float angleMax, float radius, int samples)
            {
                for (int i = 0; i < samples; i++)
                {
                    float t = (samples == 1) ? 0.5f : (float)i / (samples - 1);
                    float angle = Mathf.Lerp(angleMin, angleMax, t);

                    Vector3 dir = Quaternion.Euler(0f, angle, 0f) * -dirToPlayer;
                    Vector3 candidatePos = enemyPos + dir * radius;

                    NavMeshHit navHit;
                    bool isValidNode = NavMesh.SamplePosition(candidatePos, out navHit, 2f, NavMesh.AllAreas);
                    if (!isValidNode) continue;

                    // évite doublons (<= 0.5m)
                    bool tooClose = false;
                    for (int k = 0; k < possibleNodes.Count; k++)
                    {
                        if ((possibleNodes[k] - navHit.position).sqrMagnitude < 0.25f)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (!tooClose)
                        possibleNodes.Add(navHit.position);
                }
            }

            // ---- passes : on scanne seulement les "nouvelles" zones ----
            // Pass 1 : 120° -> [-60..+60]
            AddNodes(-60f, +60f, 6f, 15);

            // Pass 2 : extension vers 180° -> [-90..-60] et [60..90]
            AddNodes(-90f, -60f, 8f, 6);
            AddNodes(+60f, +90f, 8f, 6);

            // Pass 3 : extension vers 240° -> [-120..-90] et [90..120]
            AddNodes(-120f, -90f, 10f, 6);
            AddNodes(+90f, +120f, 10f, 6);

            SpinerPlugin.LogInfo($"[GetHidePos] {possibleNodes.Count} candidate nodes generated.");

            // Path buffer (évite alloc en boucle)
            NavMeshPath path = new NavMeshPath();

            foreach (Vector3 node in possibleNodes)
            {
                float distToPlayer = Vector3.Distance(node, playerPos);

                // 1) “caché” : obstacle entre node et joueur
                bool isHidden = Physics.Raycast(
                    node,
                    (playerPos - node).normalized,
                    distToPlayer,
                    StartOfRound.Instance.collidersRoomDefaultAndFoliage
                );
                if (!isHidden) continue;

                // 2) “atteignable” : PathComplete depuis la position actuelle
                bool hasPath = NavMesh.CalculatePath(enemyPos, node, NavMesh.AllAreas, path);
                bool pathOk = hasPath && path.status == NavMeshPathStatus.PathComplete;
                if (!pathOk) continue;

                SpinerPlugin.LogInfo($"[GetHidePos] PICK node={node} | pathComplete");
                return node;
            }

            // --- fallback fuite (si tu veux le garder) ---
            SpinerPlugin.LogInfo("[GetHidePos] No valid hiding spot found! Searching for alternative escape...");

            Vector3 escapeDir = -dirToPlayer;
            Vector3 escapePos = enemyPos + escapeDir * 6f;

            NavMeshHit escapeHit;
            bool foundEscape = NavMesh.SamplePosition(escapePos, out escapeHit, 3f, NavMesh.AllAreas);
            if (foundEscape)
            {
                bool hasEscapePath = NavMesh.CalculatePath(enemyPos, escapeHit.position, NavMesh.AllAreas, path);
                if (hasEscapePath && path.status == NavMeshPathStatus.PathComplete)
                    return escapeHit.position;
            }

            Vector3 fallbackPos = enemyPos - (dirToPlayer * 5f);

            NavMeshHit fallbackHit;
            bool foundFallback = NavMesh.SamplePosition(fallbackPos, out fallbackHit, 3f, NavMesh.AllAreas);
            if (foundFallback)
            {
                bool hasFallbackPath = NavMesh.CalculatePath(enemyPos, fallbackHit.position, NavMesh.AllAreas, path);
                if (hasFallbackPath && path.status == NavMeshPathStatus.PathComplete)
                    return fallbackHit.position;
            }

            return Vector3.zero;
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
                SpinerPlugin.LogWarning("[Kidnapping] No chasingPlayer assigned. Switching to Runaway.");
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
                    //SpinerPlugin.LogWarning("[Kidnapping] Kidnapping timeout reached. Switching to Runaway.");
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
                //SpinerPlugin.LogInfo("[Kidnapping] Ignored collision → not server.");
                return;
            }

            if (isEnemyDead)
            {
                //SpinerPlugin.LogInfo("[Kidnapping] Ignored collision → enemy dead.");
                return;
            }

            State currentState = (State)spinerBehaviourStateIndex;

            // Autorisé uniquement en Patrol / Stalking / Kidnapping
            if (currentState != State.Patrol &&
                currentState != State.Stalking &&
                currentState != State.Kidnapping)
                return;

            // Sécurité : jamais de nouvelle capture si déjà en transport
            if (currentState == State.Transport)
            {
                //SpinerPlugin.LogInfo("[Kidnapping] Ignored collision → already in Transport state.");
                return;
            }

            // Sécurité : déjà un captif / animation spéciale déjà en cours
            if (isTransportActive || inSpecialAnimationWithPlayer != null)
            {
                //SpinerPlugin.LogInfo("[Kidnapping] Ignored collision → already carrying a player.");
                return;
            }

            PlayerControllerB player = base.MeetsStandardPlayerCollisionConditions(other, false, false);
            if (player == null)
            {
                //SpinerPlugin.LogInfo("[Kidnapping] Collision detected, but no valid Player found.");
                return;
            }

            int collidedId = Array.IndexOf(StartOfRound.Instance.allPlayerScripts, player);
            if (collidedId < 0)
                return; // sécurité

            // Collision valide => on kidnap CE joueur
            inSpecialAnimationWithPlayer = player;

            kidnappingTargetId = collidedId;
            transportingTargetId = collidedId;
            chasingPlayer = player;
            stalkingTargetId = collidedId;

            //SpinerPlugin.LogInfo($"[Kidnapping] Valid collision with Player {player.playerUsername}. New targetId={kidnappingTargetId}");

            SetAnimation("Grab");

            // ---- Dark Mode: arme le timer létal pendant le transport ----
            if (_phase2Lethal && !_killTimerActive)
            {
                _killTimerActive = true;
                _kidnapKillTimer = _cfgDarkKillTime;
                //SpinerPlugin.LogInfo($"[DarkMode] Lethal timer armed: {_kidnapKillTimer:F1}s");
            }

            // Le code ne tourne que côté serveur ici, donc appel direct au ClientRpc
            BeginSpinerTransportClientRpc(kidnappingTargetId);
        }



        ////////////////////////////////////////////////////////////////////////////////////
        // SPINER TRANSPORT LOGIC: Carrying kidnapped player to destination
        ////////////////////////////////////////////////////////////////////////////////////

        // ===================== TRANSPORT CORE ======================
        // Appelée dans StartTransport(), déplace le joueur kidnappé vers une zone cible

        private void PerformTransport()
        {
            //SpinerPlugin.LogInfo("[PerformTransport] Called.");
            //SpinerPlugin.LogInfo($"[PerformTransport] → Check state: null={inSpecialAnimationWithPlayer == null}, dead={inSpecialAnimationWithPlayer?.isPlayerDead}, active={inSpecialAnimationWithPlayer?.gameObject.activeInHierarchy}, binded={(inSpecialAnimationWithPlayer?.inAnimationWithEnemy == this)}, enemyDead={isEnemyDead}");

            // ───────── Pré-checks communs ─────────
            if (isEnemyDead)
            {
                //SpinerPlugin.LogWarning("[Transport]  Enemy is dead during transport → releasing player.");
                RequestReleaseTransportServer();
                return;
            }

            if (inSpecialAnimationWithPlayer == null ||
                inSpecialAnimationWithPlayer.isPlayerDead ||
                !inSpecialAnimationWithPlayer.gameObject.activeInHierarchy ||
                inSpecialAnimationWithPlayer.inAnimationWithEnemy != this)
            {
                //SpinerPlugin.LogWarning("[Transport] Transported player invalid (null/dead/disconnected). Releasing.");
                RequestReleaseTransportServer();
                ChangeState(State.Runaway);
                return;
            }

            // ───────── Branche DARK MODE (phase 2) ─────────
            if (_phase2Lethal)
            {
                if (!IsServer)
                    return;
                // Pendant la capture en phase 2, on ne fait que le compte à rebours létal (serveur autoritaire)
                if (_killTimerActive && inSpecialAnimationWithPlayer != null)
                {
                    _kidnapKillTimer -= Time.deltaTime;

                    if ((_kidnapKillTimer % 1f) > (_kidnapKillTimer - Time.deltaTime) % 1f)
                        //SpinerPlugin.LogInfo($"[DarkMode] Kill in {_kidnapKillTimer:F1}s");

                    if (_kidnapKillTimer <= 0f)
                    {
                        _killTimerActive = false;
                        //SpinerPlugin.LogInfo("[DarkMode] Lethal timer reached 0 → killing captured player");

                        SetAnimation("Spin");

                        var p = inSpecialAnimationWithPlayer;
                        if (p != null && !p.isPlayerDead)
                        {
                            // 1) on libère pour casser le lien (évite AllowPlayerDeath=false)
                            RequestReleaseTransportServer();

                            // 2) exécution du kill selon le contexte
                            if (IsServer)
                            {
                                // serveur → envoie le ClientRpc (broadcast + filtre par targetId OK)
                                KillCapturedClientRpc(p.playerClientId, (int)CauseOfDeath.Snipped);
                            }
                            if (p.IsOwner)
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
                    //SpinerPlugin.LogWarning($"[DarkMode] Player drifted too far in phase2 ({distDark:F2}m) → Forcing release.");
                    RequestReleaseTransportServer();
                    ChangeState(State.Runaway);
                }

                return;
            }

            // ───────── Branche NORMALE (ton code actuel) ─────────

            // Libération si un seul joueur en vie
            int aliveOthers = StartOfRound.Instance.allPlayerScripts.Count(p =>
                p != null && p.isPlayerControlled && !p.isPlayerDead && p != inSpecialAnimationWithPlayer);

            //SpinerPlugin.LogInfo($"[Transport] Players alive (excluding carried): {aliveOthers}");

            if (aliveOthers <= 0)
            {
                transportReleaseTimer += Time.deltaTime;
                //SpinerPlugin.LogInfo($"[Transport] No other players alive → Releasing in {transportReleaseDelay - transportReleaseTimer:0.0}s");

                if (transportReleaseTimer >= transportReleaseDelay)
                {
                    //SpinerPlugin.LogInfo("[Transport] Timer expired → releasing player.");
                    RequestReleaseTransportServer();
                    ChangeState(State.Runaway);

                    return;
                }
            }
            else
            {
                if (transportReleaseTimer > 0f)
                {
                    //SpinerPlugin.LogInfo("[Transport] Release timer reset.");
                    transportReleaseTimer = 0f;
                }
            }

            // Libération si trop éloigné (branche normale)
            float dist = Vector3.Distance(inSpecialAnimationWithPlayer.transform.position, kidnapCarryPoint.position);
            if (dist > 2.5f)
            {
                //SpinerPlugin.LogWarning($"[Transport] Player drifted too far from carry point ({dist:F2}m) → Forcing release.");
                RequestReleaseTransportServer();
                ChangeState(State.Runaway);
                return;
            }
        }

        /*private void ReleaseTransportedPlayer(bool snapToSafePoint = true)
        {
            transportReleaseTimer = 0f;
            StopLethalTimer();

            // Reset logique transport (évite re-attach fantôme)
            isTransportActive = false;
            transportingTargetId = -1;

            if (inSpecialAnimationWithPlayer == null)
            {
                kidnapCarryPoint = null;
                return;
            }

            var p = inSpecialAnimationWithPlayer;

            // 1) Détache d'abord (sinon le parent peut réappliquer une position)
            if (p.transform.parent != null)
                p.transform.SetParent(null, true);

            // 2) Déverrouillage flags
            p.inSpecialInteractAnimation = false;
            p.inAnimationWithEnemy = null;
            p.snapToServerPosition = false;

            // 3) Snap sécurité sur NavMesh (tout interne, directement ici)
            //    Idéalement serveur-only pour éviter des divergences.
            if (snapToSafePoint && IsServer)
            {
                Vector3 origin = (kidnapCarryPoint != null) ? kidnapCarryPoint.position : p.transform.position;

                bool found = false;
                Vector3 safePos = origin;

                // Essais progressifs (rayons croissants)
                if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit1, 4f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    safePos = hit1.position; found = true;
                }
                else if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit2, 8f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    safePos = hit2.position; found = true;
                }
                else if (UnityEngine.AI.NavMesh.SamplePosition(origin, out var hit3, 16f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    safePos = hit3.position; found = true;
                }
                else
                {
                    // Fallback : tenter autour de la position du Spiner
                    Vector3 origin2 = transform.position;
                    if (UnityEngine.AI.NavMesh.SamplePosition(origin2, out var hit4, 16f, UnityEngine.AI.NavMesh.AllAreas))
                    {
                        safePos = hit4.position; found = true;
                    }
                }

                if (found)
                {
                    // petit offset vertical pour éviter clipping sol
                    p.transform.position = safePos + Vector3.up * 0.1f;
                }
                else
                {
                    // dernier recours : remonter au-dessus de la position actuelle
                    p.transform.position = p.transform.position + Vector3.up * 1.0f;
                }
            }

            // 4) Réactivation du saut uniquement pour le joueur local
            if (p == GameNetworkManager.Instance.localPlayerController)
            {
                var jumpAction = IngamePlayerSettings.Instance.playerInput.actions.FindAction("Jump", false);
                if (jumpAction != null)
                    jumpAction.Enable();
            }

            // 5) Nettoyage refs
            inSpecialAnimationWithPlayer = null;
            kidnapCarryPoint = null;
        }*/


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
            //SpinerPlugin.LogInfo($"[KillCapturedClientRpc] localId={local?.playerClientId}, targetId={targetId}, isDead={local?.isPlayerDead}");

            if (local != null && local.playerClientId == targetId && !local.isPlayerDead)
            {
                local.KillPlayer(Vector3.up * 14f, true, (CauseOfDeath)cause, 7, Vector3.zero);
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

        // ===================== RELEASE =====================

        [ClientRpc]
        private void ForceReleaseTransportClientRpc(int playerId, Vector3 safePos)
        {
            ApplyReleaseTransportLocal(playerId, safePos);
        }

        // ===================== ANIMATION =====================
        [ClientRpc]
        private void SyncAnimClientRpc(string animType)
        {
            // Important: pas de rebroadcast depuis un client
            SetAnimation(animType, fromRpc: true);
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

        public void SetAnimation(string animType, bool fromRpc = false)
        {
            if (creatureAnimator == null || string.IsNullOrEmpty(animType)) return;

            // On ne sync PAS Walk/Idle (spam Update)
            bool isWalkOrIdle = (animType == "Walk" || animType == "Idle");

            // 1) Si on est SERVEUR et que ça ne vient pas d'un RPC :
            //    on broadcast toutes les anims sauf Walk/Idle
            if (!fromRpc && IsServer && !isWalkOrIdle)
            {
                SyncAnimClientRpc(animType);
            }

            // 2) Si on est CLIENT et que ça ne vient pas d'un RPC :
            //    on n'applique PAS les anims "événement" localement (le serveur les enverra)
            if (!fromRpc && !IsServer && !isWalkOrIdle)
            {
                return;
            }

            // --- Applique localement (serveur + clients quand fromRpc=true) ---

            if (animType == "Walk")
            {
                if (_currentAnimState == "Walk") return;
                creatureAnimator.SetBool(animWalk, true);
                _currentAnimState = "Walk";
                return;
            }

            if (animType == "Idle")
            {
                if (_currentAnimState == "Idle") return;
                creatureAnimator.SetBool(animWalk, false);
                _currentAnimState = "Idle";
                return;
            }

            if (animType == "Death")
            {
                if (_currentAnimState == "Death") return;
                creatureAnimator.SetBool(animWalk, false);
                creatureAnimator.SetBool(animDeath, true);
                _currentAnimState = "Death";
                return;
            }

            if (animType == "Resurrect")
            {
                if (_currentAnimState == "Resurrect") return;
                creatureAnimator.SetBool(animWalk, false);
                creatureAnimator.SetBool(animDeath, false);
                _currentAnimState = "Resurrect";
                return;
            }

            if (animType == "Death2")
            {
                if (_currentAnimState == "Death2") return;
                creatureAnimator.SetBool(animWalk, false);
                creatureAnimator.SetBool(animDeath2, true);
                _currentAnimState = "Death2";
                return;
            }

            // Triggers
            creatureAnimator.SetBool(animWalk, false);
            _currentAnimState = animType;

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
                creatureAnimator.ResetTrigger(triggerName);
                creatureAnimator.SetTrigger(triggerName);
            }
        }

        private bool _phase2AudioTintApplied = false;

        private void TriggerFx(SpinerEvent fx)
        {
            AudioClip? clip = null;
            AudioSource src = sfxAudioSource;

            switch (fx)
            {
                case SpinerEvent.Kidnap: clip = currentKidnappingSound; break;
                case SpinerEvent.Death: clip = currentDeathSound; break;
                case SpinerEvent.Creep: clip = currentCreepSound; break;
                case SpinerEvent.Transport: clip = currentTransportSound; break;
                case SpinerEvent.Detection: clip = currentDetectionSound; break;
                case SpinerEvent.Runaway: clip = currentRunawaySound; break;
                case SpinerEvent.Roam:
                    {
                        AudioClip[] roamClips = { currentRoamingSound, currentRoamingSound2, currentRoamingSound3 };
                        int index = Mathf.Abs((GetInstanceID() + Time.frameCount) % roamClips.Length);
                        clip = roamClips[index];
                        break;
                    }
                default: return;
            }

            if (clip == null || src == null)
            {
                //SpinerPlugin.LogWarning($"[FX] {fx}: clip/src null (clip={clip?.name ?? "null"}, src={src?.name ?? "null"})");
                return;
            }

            // applique le pitch UNE FOIS quand on entre en phase 2 (pas de restore)
            if (_phase2Lethal && !_phase2AudioTintApplied)
            {
                src.pitch = Phase2Pitch;
                _phase2AudioTintApplied = true;
            }

            // anti-spam simple
            if (clip == _lastClip && Time.time - _lastTime < 0.05f) return;

            float vol = _phase2Lethal ? Phase2VolMult : 1f;
            //SpinerPlugin.LogInfo($"[FX] {fx} | phase2={_phase2Lethal} | src={src.name} | clip={clip.name} | pitch={src.pitch:0.00}");
            src.PlayOneShot(clip, vol);

            _lastClip = clip;
            _lastTime = Time.time;
        }
        public override void OnDestroy()
        {
            // Notre cleanup local avant de laisser EnemyAI faire le sien
            if (inSpecialAnimationWithPlayer != null)
            {
                //SpinerPlugin.LogWarning("[OnDestroy] Spiner destroyed while carrying → forcing local release.");
                RequestReleaseTransportServer();
            }

            base.OnDestroy();
        }
        private void OnDisable()
        {
            if (inSpecialAnimationWithPlayer != null)
            {
                //SpinerPlugin.LogWarning("[OnDisable] Spiner disabled while carrying → forcing local release.");
                RequestReleaseTransportServer();
            }
        }

    }
}
