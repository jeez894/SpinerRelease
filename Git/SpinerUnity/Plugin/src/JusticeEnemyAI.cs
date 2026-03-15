using UnityEngine;
using GameNetcodeStuff;

namespace JusticeEnemy
{
    public class JusticeEnemyAI : EnemyAI
    {
        private new UnityEngine.AI.NavMeshAgent agent; // Utilisation intentionnelle du masquage
        public Transform player;
        private new int currentBehaviourStateIndex;

        private float alertDistance = 10f;
        private float attackRange = 2f;

        private Vector3[] patrolPoints;
        private int currentPatrolIndex = 0;

        public AudioClip attackSound;
        public AudioClip moveSound;

        private bool isPlayingMoveSound = false;

        public override void Start()
        {
            base.Start();
            currentBehaviourStateIndex = 0; // Initialise en état "Patrouille".
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();

            // Points de patrouille par défaut.
            patrolPoints = new Vector3[]
            {
                new Vector3(0, 0, 0),
                new Vector3(5, 0, 5),
                new Vector3(-5, 0, -5)
            };

            player = GameObject.FindWithTag("Player").transform;
        }

        public override void Update()
        {
            base.Update();
            HandleMovementSound();
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();

            switch (currentBehaviourStateIndex)
            {
                case 0: // Patrouille
                    Patrol();
                    break;

                case 1: // Alerte
                    Alert();
                    break;

                case 2: // Attaque
                    Attack();
                    break;

                default:
                    Debug.LogError("Invalid state index!");
                    break;
            }
        }

        private void Patrol()
        {
            if (patrolPoints.Length == 0) return;

            agent.speed = 3f; // Vitesse de patrouille
            agent.SetDestination(patrolPoints[currentPatrolIndex]);

            if (Vector3.Distance(transform.position, patrolPoints[currentPatrolIndex]) < 1f)
            {
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
            }

            // Passe en alerte si un joueur est détecté en ligne de vue et à portée
            if (FoundClosestPlayerInRange(alertDistance, 5f))
            {
                SwitchState(1); // Passe en mode alerte
            }
        }

        private void Alert()
        {
            if (player == null) return;

            agent.speed = 0f; // L'ennemi s'arrête pour observer

            // Si le joueur reste dans le champ de vision
            if (CheckLineOfSightForPlayer(45f, Mathf.FloorToInt(alertDistance))) // Conversion explicite en int
            {
                Vector3 directionToPlayer = (player.position - transform.position).normalized;
                Quaternion lookRotation = Quaternion.LookRotation(new Vector3(directionToPlayer.x, 0, directionToPlayer.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 5f);

                // Si l'ennemi fait face au joueur, passe en attaque
                if (Vector3.Angle(transform.forward, directionToPlayer) < 10f)
                {
                    SwitchState(2); // Passe en mode attaque
                }
            }
            else
            {
                // Retourne en patrouille si le joueur est hors de vue
                SwitchState(0);
            }
        }

        private void Attack()
        {
            if (player == null) return;

            agent.speed = 5f; // Vitesse accrue pour l'attaque.
            agent.SetDestination(player.position);

            if (Vector3.Distance(transform.position, player.position) > alertDistance)
            {
                SwitchState(1); // Retour en alerte si le joueur s’éloigne.
            }

            // Ralentit le joueur.
            PlayerControllerB playerController = player.GetComponent<PlayerControllerB>();
            if (playerController != null)
            {
                playerController.movementSpeed *= 0.5f;
            }
        }

        private bool FoundClosestPlayerInRange(float range, float senseRange)
        {
            bool playerInSight = base.TargetClosestPlayer(1.5f, true, 70f);

            if (!playerInSight)
            {
                base.TargetClosestPlayer(1.5f, false, 70f);
                range = senseRange;
            }

            return targetPlayer != null && Vector3.Distance(transform.position, targetPlayer.transform.position) < range;
        }

        private void SwitchState(int newStateIndex)
        {
            if (currentBehaviourStateIndex == 2 && newStateIndex != 2)
            {
                PlayerControllerB playerController = player.GetComponent<PlayerControllerB>();
                if (playerController != null)
                {
                    playerController.movementSpeed /= 0.5f;
                }
            }

            currentBehaviourStateIndex = newStateIndex;
        }

        private void HandleMovementSound()
        {
            bool isMoving = agent.velocity.magnitude > 0.1f;
            bool isRotating = Quaternion.Angle(transform.rotation, agent.transform.rotation) > 1f;

            if ((isMoving || isRotating) && !isPlayingMoveSound)
            {
                if (moveSound != null && creatureSFX != null)
                {
                    creatureSFX.clip = moveSound;
                    creatureSFX.loop = true;
                    creatureSFX.Play();
                    isPlayingMoveSound = true;
                }
            }
            else if (!isMoving && !isRotating && isPlayingMoveSound)
            {
                if (creatureSFX != null)
                {
                    creatureSFX.Stop();
                    isPlayingMoveSound = false;
                }
            }
        }

        public override void OnCollideWithPlayer(Collider other)
        {
            base.OnCollideWithPlayer(other);

            if (other.CompareTag("Player"))
            {
                SwitchState(2);
            }
        }
    }
}
