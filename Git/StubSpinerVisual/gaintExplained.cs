using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering.HighDefinition;

// Cette classe hérite de EnemyAI et implémente IVisibleThreat.
// EnemyAI fournit une base pour l’IA d’un ennemi (déplacements, pathfinding, etc.).
// IVisibleThreat est une interface qui indique que cet ennemi peut être
// perçu comme une menace par d'autres systèmes (avec un type de menace, etc.).
public class ForestGiantAI : EnemyAI, IVisibleThreat
{
    // ================ IVisibleThreat ================
    // Indique quel type de menace représente ce ForestGiantAI.
    // ThreatType est un enum qui répertorie les différents types d’ennemis (Forests, Ghost, etc.).

    // (get) => C’est une propriété de type ThreatType retournant "ForestGiant".
    ThreatType IVisibleThreat.type
    {
        get
        {
            return ThreatType.ForestGiant;
        }
    }

    // Méthode de l’interface IVisibleThreat qui, dans ce cas, ne fait rien de spécial et renvoie 0.
    // "id" peut être utilisé pour déclencher un comportement particulier, mais ici non implémenté.
    int IVisibleThreat.SendSpecialBehaviour(int id)
    {
        return 0;
    }

    // Renvoie un "threat level" fixe de 18 pour ce géant.
    // Par exemple, un système plus large pourrait comparer les menaceLevels pour prioriser l'ennemi.
    int IVisibleThreat.GetThreatLevel(Vector3 seenByPosition)
    {
        return 18;
    }

    // Niveau d'intérêt du threat : retourne 0 => Ce ForestGiant n’a pas de niveau d’intérêt spécifique.
    int IVisibleThreat.GetInterestLevel()
    {
        return 0;
    }

    // Renvoie le transform de "l'œil" du threat (pour que d'autres systèmes puissent s'orienter vers).
    Transform IVisibleThreat.GetThreatLookTransform()
    {
        return this.eye;
    }

    // Renvoie tout simplement le transform principal de l’ennemi (ici, "base.transform").
    Transform IVisibleThreat.GetThreatTransform()
    {
        return base.transform;
    }

    // Renvoie la vélocité de l’ennemi si c’est l’owner (sûrement pour du netcode).
    // Sinon, Vector3.zero.
    Vector3 IVisibleThreat.GetThreatVelocity()
    {
        if (base.IsOwner)
        {
            return this.agent.velocity;
        }
        return Vector3.zero;
    }

    // Calcule la "visibilité" de cet ennemi. Ici, si l'ennemi est mort => 0.
    // Sinon, 1f quand il bouge, 0.75f quand il est statique.
    float IVisibleThreat.GetVisibility()
    {
        if (this.isEnemyDead)
        {
            return 0f;
        }
        if (this.agentLocalVelocity.sqrMagnitude > 0f)
        {
            return 1f;
        }
        return 0.75f;
    }

    // ==============================================================
    // ================ Méthodes héritées de EnemyAI =================
    // ==============================================================

    // Méthode override : Quand l'ennemi se fait frapper (HitEnemy).
    // - force = dégâts,
    // - playerWhoHit = joueur qui a frappé,
    // - playHitSFX = bool pour jouer un son de hit.
    // - hitID = ID custom.
    public override void HitEnemy(int force = 1, PlayerControllerB playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
    {
        // Appelle la logique parent. Possiblement gère du netcode, des events.
        base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);

        // Réduit la vie de l’ennemi.
        this.enemyHP -= force;

        // Si la vie est <= 0 et qu’on n’est pas déjà mort,
        // et si on est l’owner (serveur local, on suppose),
        // on tue l’ennemi.
        if ((float)this.enemyHP <= 0f && !this.isEnemyDead && base.IsOwner)
        {
            base.KillEnemyOnOwnerClient(false);
        }
    }

    // Méthode override : Quand l'ennemi meurt (KillEnemy).
    // "destroy" = si on supprime l’ennemi direct.
    public override void KillEnemy(bool destroy = false)
    {
        // Logique parent (marque l’ennemi comme mort, netcode, etc.).
        base.KillEnemy(destroy);

        // Coupe la vitesse (empêche l’IA de bouger).
        this.agent.speed = 0f;

        // Si l’ennemi était en train de manger un joueur, on arrête la coroutine.
        if (this.eatPlayerCoroutine != null)
        {
            base.StopCoroutine(this.eatPlayerCoroutine);
        }

        // Méthode locale qui relâche le joueur tenu, s’il y en a un.
        this.DropPlayerBody();

        // Joue un cri de mort (giantCry).
        this.creatureVoice.PlayOneShot(this.giantCry);

        // Désactive les particules de "burning".
        this.burningParticlesContainer.SetActive(false);
    }

    // Méthode override : AnimationEventA() est souvent appelée par un event d’animation 
    // (ex: frame précise dans l’anim) pour faire des dégâts de zone ou autre.
    public override void AnimationEventA()
    {
        base.AnimationEventA();

        // SphereCast pour détecter les joueurs proches (2.7f de rayon, 3.9f de distance).
        RaycastHit[] array = Physics.SphereCastAll(
            this.deathFallPosition.position,
            2.7f,
            this.deathFallPosition.forward,
            3.9f,
            StartOfRound.Instance.playersMask,
            QueryTriggerInteraction.Ignore
        );

        // Si un joueur local (GameNetworkManager.Instance.localPlayerController) est dans la zone,
        // on le tue (KillPlayer).
        for (int i = 0; i < array.Length; i++)
        {
            PlayerControllerB component = array[i].transform.GetComponent<PlayerControllerB>();
            if (component != null && component == GameNetworkManager.Instance.localPlayerController)
            {
                GameNetworkManager.Instance.localPlayerController.KillPlayer(
                    Vector3.zero,
                    true,
                    CauseOfDeath.Gravity,
                    0,
                    default(Vector3)
                );
                return;
            }
        }
    }

    // Méthode override : Appelée quand l’ennemi est touché par une explosion (HitFromExplosion).
    // "distance" = distance de l’explosion.
    public override void HitFromExplosion(float distance)
    {
        // Logique parent de prise de dégâts d’explosion.
        base.HitFromExplosion(distance);

        // Si l’ennemi est déjà mort, on ne fait rien.
        if (this.isEnemyDead)
        {
            return;
        }

        // currentBehaviourStateIndex == 2 semble être un état "burning" (voir plus loin),
        // donc si c’est déjà dans cet état, on ne fait rien.
        if (this.currentBehaviourStateIndex == 2)
        {
            return;
        }

        // On note le temps (timeAtStartOfBurning).
        this.timeAtStartOfBurning = Time.realtimeSinceStartup;

        // Si on est l’owner : on force le passage en état 2 (brûle).
        if (!base.IsOwner)
        {
            return;
        }
        base.SwitchToBehaviourState(2);
    }

    // Méthode override : Start() se lance quand l’ennemi est instancié en jeu.
    public override void Start()
    {
        // Appelle la logique parent (EnemyAI Start).
        base.Start();

        // Initialise les stealthMeters du joueur (sûrement pour calculer comment il repère les players).
        for (int i = 0; i < this.playerStealthMeters.Length; i++)
        {
            this.playerStealthMeters[i] = 0f;
        }

        // "lookTarget" est un transform que le géant utilise pour regarder ? 
        // On le détache du parent (this) => setParent(null).
        this.lookTarget.SetParent(null);
    }

    // Méthode override : Exécutée régulièrement (toutes les X secondes) pour la logique "moins urgente".
    // On voit un switch sur currentBehaviourStateIndex : 0 (patrol), 1 (chase), 2 (burning), etc.
    public override void DoAIInterval()
    {
        // Appel parent => effectue la logique par défaut (EnemyAI).
        base.DoAIInterval();

        // S’il n’y a plus de joueurs vivants, on arrête.
        if (StartOfRound.Instance.livingPlayers == 0)
        {
            return;
        }

        // Si l’ennemi est mort, on arrête.
        if (this.isEnemyDead)
        {
            return;
        }

        // Selon l’état (0, 1, 2), on fait différentes actions :
        switch (this.currentBehaviourStateIndex)
        {
            case 0:
                // => État 0 = "roaming / patrouille"
                if (this.searchForPlayers.inProgress)
                {
                    // Stop une recherche "searchForPlayers" si elle était en cours.
                    base.StopSearch(this.searchForPlayers, true);
                }

                // "investigating" => sous-état où le géant enquête sur un bruit ?
                if (this.investigating)
                {
                    // Si pas encore commencé l’investigation => on arrête la patrouille roamPlanet,
                    // on set la destination vers l’endroit à investiguer (investigatePosition).
                    if (!this.hasBegunInvestigating)
                    {
                        this.hasBegunInvestigating = true;
                        base.StopSearch(this.roamPlanet, false);
                        base.SetDestinationToPosition(this.investigatePosition, false);
                    }
                    // Si on est proche de l’investigatePosition, on arrête d’investiguer.
                    if (Vector3.Distance(base.transform.position, this.investigatePosition) < 5f)
                    {
                        this.investigating = false;
                        this.hasBegunInvestigating = false;
                    }
                    return;
                }

                // Si la patrouille (roamPlanet) n’est pas en cours,
                // on en démarre une depuis la position de l’ennemi.
                if (!this.roamPlanet.inProgress)
                {
                    Vector3 position = base.transform.position;

                    // S’il sort d’un chase (previousBehaviourStateIndex == 1) et qu’il est près de l’ascenseur,
                    // il choisit un node loin de l’ascenseur pour patrouiller.
                    if (this.previousBehaviourStateIndex == 1 &&
                        Vector3.Distance(base.transform.position, StartOfRound.Instance.elevatorTransform.position) < 30f)
                    {
                        position = base.ChooseFarthestNodeFromPosition(
                            StartOfRound.Instance.elevatorTransform.position,
                            false, 0, false, 50, false
                        ).position;
                    }

                    // Lancement de la patrouille
                    base.StartSearch(position, this.roamPlanet);
                    return;
                }
                break;

            case 1:
                // => État 1 = "chase"
                this.investigating = false;
                this.hasBegunInvestigating = false;

                // Stop la patrouille roamPlanet si c’était en cours.
                if (this.roamPlanet.inProgress)
                {
                    base.StopSearch(this.roamPlanet, false);
                }

                // "lostPlayerInChase" => variable qui indique qu’on a perdu la cible.
                if (!this.lostPlayerInChase)
                {
                    // Si on est toujours sur un searchForPlayers => on l’arrête
                    // et on se met à poursuivre le joueur en direct.
                    if (this.searchForPlayers.inProgress)
                    {
                        base.StopSearch(this.searchForPlayers, true);
                        Debug.Log("Found player during chase; stopping search coroutine and moving after target player");
                    }
                    // On se dirige vers le "chasingPlayer" (agent.SetDestination).
                    base.SetMovingTowardsTargetPlayer(this.chasingPlayer);
                    return;
                }
                // => Si on a perdu le joueur
                // on lance un searchForPlayers autour de lastSeenPlayerPositionInChase
                if (!this.searchForPlayers.inProgress)
                {
                    Debug.Log("Forest giant starting search for players routine");
                    this.searchForPlayers.searchWidth = 25f;
                    base.StartSearch(this.lastSeenPlayerPositionInChase, this.searchForPlayers);
                    Debug.Log("Lost player in chase; beginning search where the player was last seen");
                    return;
                }
                break;

            case 2:
                // => État 2 = "burning" (référence au HitFromExplosion)
                if (this.searchForPlayers.inProgress)
                {
                    base.StopSearch(this.searchForPlayers, true);
                }
                if (!this.roamPlanet.inProgress)
                {
                    // "searchPrecision" = paramètre interne pour la patrouille
                    // (distance entre nodes ?).
                    this.roamPlanet.searchPrecision = 18f;
                    // Choisit un node lointain et patrouille.
                    base.StartSearch(
                        base.ChooseFarthestNodeFromPosition(base.transform.position, false, 0, false, 50, false).position,
                        this.roamPlanet
                    );
                }
                break;

            default:
                return;
        }
    }

    // Méthode override : quand la searchRoutine est terminée (AI a fini de patrouiller).
    public override void FinishedCurrentSearchRoutine()
    {
        // Si ce n’est pas le owner => on arrête.
        if (!base.IsOwner)
        {
            return;
        }

        // S’il était en chase (currentBehaviourStateIndex == 1),
        // qu’il a perdu le joueur (lostPlayerInChase) et pas de LOS (chasingPlayerInLOS),
        // alors il repasse en roaming (état 0).
        if (this.currentBehaviourStateIndex == 1 && this.lostPlayerInChase && !this.chasingPlayerInLOS)
        {
            Debug.Log("Forest giant: Finished search; player not in line of sight, lost player, returning to roaming mode");
            base.SwitchToBehaviourState(0);
        }
    }

    // Méthode override : callback quand l’IA atteint un node de la search en cours (un waypoint).
    public override void ReachedNodeInSearch()
    {
        base.ReachedNodeInSearch();

        if (!base.IsOwner)
        {
            return;
        }

        // Si on est en état 0 (patrol)
        // on a un timer "stopAndLookInterval" qui, toutes les X s, force un "stopAndLook" 
        // => le géant se tourne vers un angle "targetYRot" choisi aléatoirement.
        if (this.currentBehaviourStateIndex != 0)
        {
            return;
        }

        if (this.stopAndLookInterval > 12f)
        {
            this.stopAndLookInterval = 0f;
            this.stopAndLookTimer = Random.Range(3f, 12f);
            this.targetYRot = RoundManager.Instance.YRotationThatFacesTheFarthestFromPosition(
                this.eye.position,
                10f,
                5
            );
        }
    }

    // =================================================================================
    // (Fin du bloc actuel)
    // =================================================================================

    // On ne voit pas la fin de la classe (probablement plus loin),
    // mais c’est déjà toute la logique principale (Update, DoAIInterval, etc.).
}
// Cette partie du code continue la classe ForestGiantAI.
// On retrouve la suite des méthodes relatives au comportement de l’ennemi.

// ========================= LATEUPDATE =========================
// Méthode appelée après toutes les Update() du frame.
// Souvent utilisée pour des ajustements de position/rotation
// après que tout le reste soit calculé.
private void LateUpdate()
{
    // inSpecialAnimationWithPlayer => s’il est en train de faire
    // une animation spéciale avec un joueur (porter, manger, etc.),
    // on positionne le joueur dans la main/point d’attache du géant.
    if (this.inSpecialAnimationWithPlayer != null)
    {
        this.inSpecialAnimationWithPlayer.transform.position = this.holdPlayerPoint.position;
        this.inSpecialAnimationWithPlayer.transform.rotation = this.holdPlayerPoint.rotation;
    }

    // Si "lookingAtTarget" est vrai, on appelle LookAtTarget() qui oriente la tête/rotation.
    if (this.lookingAtTarget)
    {
        this.LookAtTarget();
    }

    // "staring" = booléen pour l’Animator. Contrôle une animation de “regard” ?
    this.creatureAnimator.SetBool("staring", this.lookingAtTarget);

    // Check si la partie réseau est valide et si le joueur local existe.
    if (GameNetworkManager.Instance == null || GameNetworkManager.Instance.localPlayerController == null)
    {
        return;
    }

    // Ajuste le volume d’un son "farWideSFX" en fonction de la distance au joueur.
    // Plus le joueur est près, plus le volume est fort.
    this.farWideSFX.volume = Mathf.Clamp(
        Vector3.Distance(GameNetworkManager.Instance.localPlayerController.transform.position, base.transform.position)
        / (this.farWideSFX.maxDistance - 10f),
        0f, 1f
    );
}

// ======================= GIANTSEEPLAYEREFFECT =======================
// Cette méthode gère l’effet quand le géant voit (ou poursuit) le joueur.
private void GiantSeePlayerEffect()
{
    // Si le joueur local est mort ou à l’intérieur de la factory,
    // pas d’effet de peur.
    if (GameNetworkManager.Instance.localPlayerController.isPlayerDead
        || GameNetworkManager.Instance.localPlayerController.isInsideFactory)
    {
        return;
    }

    // Si l’ennemi est en état 1 (chase), qu’il poursuit le joueur local
    // et qu’il ne l’a pas "perdu" (lostPlayerInChase = false),
    // alors on augmente la peur du joueur.
    if (this.currentBehaviourStateIndex == 1
        && this.chasingPlayer == GameNetworkManager.Instance.localPlayerController
        && !this.lostPlayerInChase)
    {
        // IncreaseFearLevelOverTime(1.4f, 1f) => augmente la peur en continu.
        GameNetworkManager.Instance.localPlayerController.IncreaseFearLevelOverTime(1.4f, 1f);
        return;
    }

    // Sinon, on vérifie si le joueur est visible dans un angle (CheckLineOfSightForPosition)
    // 45f de FOV, 70 de distance, etc. "flag" => true si le joueur est visible.
    bool flag = !GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom
                && base.CheckLineOfSightForPosition(
                    GameNetworkManager.Instance.localPlayerController.gameplayCamera.transform.position,
                    45f,
                    70,
                    -1f,
                    null
                );

    if (flag)
    {
        // Si le joueur est très proche (<15m), jump la peur à 0.7.
        // Sinon, 0.4 => un effet plus modéré.
        if (Vector3.Distance(base.transform.position, GameNetworkManager.Instance.localPlayerController.transform.position) < 15f)
        {
            GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.7f, true);
            return;
        }
        GameNetworkManager.Instance.localPlayerController.JumpToFearLevel(0.4f, true);
    }
}

// ========================= UPDATE =========================
// Méthode appelée chaque frame. C’est le cœur de la logique temps-réel.
public override void Update()
{
    // Appel de la logique parent (EnemyAI).
    base.Update();

    // Vérifie si le joueur local est dispo (sinon, pas besoin de faire de la logique).
    if (GameNetworkManager.Instance.localPlayerController == null)
    {
        return;
    }

    // Si l’ennemi est mort, on diminue lentement le volume du burning audio.
    if (this.isEnemyDead)
    {
        this.giantBurningAudio.volume -= Time.deltaTime * 0.5f;
    }

    // Si on est stun (stunNormalizedTimer > 0f) et qu’on est en train de manger un joueur,
    // ou si on est mort ou en état 2 (burning),
    // on arrête l’animation de kill (StopKillAnimation).
    // Sinon, on appelle GiantSeePlayerEffect() pour gérer la peur du joueur.
    if ((this.stunNormalizedTimer > 0f && this.inEatingPlayerAnimation)
        || this.isEnemyDead
        || this.currentBehaviourStateIndex == 2)
    {
        this.StopKillAnimation();
    }
    else
    {
        this.GiantSeePlayerEffect();
    }

    // Si l’ennemi est mort, on ne fait plus rien.
    if (this.isEnemyDead)
    {
        return;
    }

    // SetBool "stunned" pour l’Animator si on est stun.
    this.creatureAnimator.SetBool("stunned", this.stunNormalizedTimer > 0f);

    // CalculateAnimationDirection(1f) => met à jour un paramètre d’anim en fonction de la direction ?
    this.CalculateAnimationDirection(1f);

    // Incrémente divers timers chaque frame :
    this.stopAndLookInterval += Time.deltaTime;
    this.timeSinceChangingTarget += Time.deltaTime;
    this.timeSinceDetectingVoice += Time.deltaTime;

    // switch sur currentBehaviourStateIndex (0=patrol, 1=chase, 2=burning…)
    switch (this.currentBehaviourStateIndex)
    {
        case 0:
            // État 0 : patrouille
            // reachForPlayerRig.weight => composant d’animation rigging ?
            // On le met à 0 progressif (Lerp) pour arrêter l’animation de saisir le joueur.
            this.reachForPlayerRig.weight = Mathf.Lerp(
                this.reachForPlayerRig.weight,
                0f,
                Time.deltaTime * 15f
            );

            // On réinitialise quelques variables :
            this.lostPlayerInChase = false;
            this.triggerChaseByTouchingDebounce = false;
            this.hasLostPlayerInChaseDebounce = false;
            this.lookingAtTarget = false;

            // Si on n’est pas l’owner (serveur ou autorité), on arrête là.
            if (!base.IsOwner)
            {
                return;
            }

            // stopAndLookTimer > 0 => c’est un délai pendant lequel le géant "regarde autour" sans bouger ?
            if (this.stopAndLookTimer > 0f)
            {
                this.stopAndLookTimer -= Time.deltaTime;
                // On tourne le géant vers "targetYRot".
                this.turnCompass.eulerAngles = new Vector3(
                    base.transform.eulerAngles.x,
                    this.targetYRot,
                    base.transform.eulerAngles.z
                );
                base.transform.rotation = Quaternion.Lerp(
                    base.transform.rotation,
                    this.turnCompass.rotation,
                    5f * Time.deltaTime
                );

                // On met la vitesse à 0 => il s’arrête totalement.
                this.agent.speed = 0f;
            }
            else
            {
                // S’il est stun (stunNormalizedTimer > 0) par un joueur
                // et que ce joueur n’est pas le "chasingPlayer",
                // on commence à chase ce nouveau joueur.
                if (this.stunNormalizedTimer > 0f
                    && this.stunnedByPlayer != null
                    && this.stunnedByPlayer != this.chasingPlayer)
                {
                    this.FindAndTargetNewPlayerOnLocalClient(this.stunnedByPlayer);
                    this.BeginChasingNewPlayerClientRpc((int)this.stunnedByPlayer.playerClientId);
                }

                // Sinon, la vitesse repasse à 5f (patrouille plus rapide ?).
                this.agent.speed = 5f;
            }

            // LookForPlayers() => check si un joueur est visible ?
            this.LookForPlayers();
            return;

        case 1:
            // État 1 : chase
            // Tente de saisir le joueur s’il est proche (ReachForPlayerIfClose).
            this.ReachForPlayerIfClose();

            // Si on n’est pas l’owner, on arrête (pas d’autorité sur la position).
            if (!base.IsOwner)
            {
                return;
            }

            // Si on est en pleine animation de "manger" le joueur, on ne bouge plus.
            if (this.inEatingPlayerAnimation)
            {
                this.agent.speed = 0f;
                return;
            }

            // Sinon, on "LookForPlayers()" => essaie de repérer/maintenir la cible en vue.
            this.LookForPlayers();

            // lostPlayerInChase => variable bool => a-t-on perdu la cible ?
            if (this.lostPlayerInChase)
            {
                if (!this.hasLostPlayerInChaseDebounce)
                {
                    // On indique qu’on ne regarde plus la cible,
                    // hasLostPlayerInChaseDebounce => évite de spammer.
                    this.lookingAtTarget = false;
                    this.hasLostPlayerInChaseDebounce = true;
                    this.HasLostPlayerInChaseClientRpc();
                }

                // On baisse le weight du rigging (reachForPlayer).
                this.reachForPlayerRig.weight = Mathf.Lerp(this.reachForPlayerRig.weight, 0f, Time.deltaTime * 15f);

                // On refait le même bloc "stopAndLookTimer" que dans l’état 0
                // => le géant s’arrête qq secondes et tourne sur lui-même.
                if (this.stopAndLookTimer > 0f)
                {
                    this.stopAndLookTimer -= Time.deltaTime;
                    this.turnCompass.eulerAngles = new Vector3(
                        base.transform.eulerAngles.x,
                        this.targetYRot,
                        base.transform.eulerAngles.z
                    );
                    base.transform.rotation = Quaternion.Lerp(
                        base.transform.rotation,
                        this.turnCompass.rotation,
                        5f * Time.deltaTime
                    );
                    this.agent.speed = 0f;
                }
                else if (this.stunNormalizedTimer > 0f)
                {
                    // S’il est stun, vitesse à 0.
                    this.agent.speed = 0f;
                }
                else
                {
                    // Sinon, on augmente progressivement la vitesse jusqu’à max 7f.
                    this.agent.speed = Mathf.Min(
                        Mathf.Max(this.agent.speed, 0.1f) * 1.3f,
                        7f
                    );
                    Debug.Log(string.Format("agent speed: {0}", this.agent.speed));
                }

                // Si on vient de retrouver la LOS (chasingPlayerInLOS = true),
                // on réinitialise lostPlayerInChase et on reprend la chase.
                if (this.chasingPlayerInLOS)
                {
                    this.noticePlayerTimer = 0f;
                    this.lostPlayerInChase = false;
                    return;
                }

                // Sinon, on incrémente noticePlayerTimer,
                // et si ça dure +9s => on repasse à l’état 0 (patrouille).
                this.noticePlayerTimer += Time.deltaTime;
                if (this.noticePlayerTimer > 9f)
                {
                    base.SwitchToBehaviourState(0);
                    return;
                }
            }
            else
            {
                // => Si on n’a pas "perdu" le joueur
                // on place "lookTarget" sur la position du joueur,
                // on active lookingAtTarget = true.
                this.lookTarget.position = this.chasingPlayer.transform.position;
                this.lookingAtTarget = true;

                // Si on est stun => vitesse 0, sinon on la monte graduellement vers 7f max.
                if (this.stunNormalizedTimer > 0f)
                {
                    this.agent.speed = 0f;
                }
                else
                {
                    this.agent.speed = Mathf.Min(
                        Mathf.Max(this.agent.speed, 0.1f) * 1.3f,
                        7f
                    );
                }

                // Si on avait mis "hasLostPlayerInChaseDebounce" à true,
                // on le repasse à false => on signale qu’on a retrouvé le joueur.
                if (this.hasLostPlayerInChaseDebounce)
                {
                    this.hasLostPlayerInChaseDebounce = false;
                    this.HasFoundPlayerInChaseClientRpc();
                }

                // Si le joueur est toujours dans la LOS, on actualise lastSeenPlayerPositionInChase.
                // Sinon, on incrémente noticePlayerTimer,
                // et si >3s => lostPlayerInChase = true => on repasse dans le bloc précédent.
                if (this.chasingPlayerInLOS)
                {
                    this.noticePlayerTimer = 0f;
                    this.lastSeenPlayerPositionInChase = this.chasingPlayer.transform.position;
                    return;
                }
                this.noticePlayerTimer += Time.deltaTime;
                if (this.noticePlayerTimer > 3f)
                {
                    this.lostPlayerInChase = true;
                    return;
                }
            }
            break;

        case 2:
            // État 2 : burning
            // On arrête de regarder la cible.
            this.lookingAtTarget = false;

            // Si on est mort, on arrête tout.
            if (this.isEnemyDead)
            {
                return;
            }

            // Active les particules de feu si pas déjà actif.
            if (!this.burningParticlesContainer.activeSelf)
            {
                this.burningParticlesContainer.SetActive(true);
            }

            // Lance le son giantBurningAudio s’il n’est pas déjà en train de jouer,
            // puis on augmente progressivement le volume.
            if (!this.giantBurningAudio.isPlaying)
            {
                this.giantBurningAudio.Play();
            }
            this.giantBurningAudio.volume = Mathf.Min(
                this.giantBurningAudio.volume + Time.deltaTime * 0.5f,
                1f
            );

            // Si on n’est pas l’owner, on arrête là.
            if (!base.IsOwner)
            {
                return;
            }

            // On augmente la vitesse petit à petit, jusqu’à 8f max.
            this.agent.speed = Mathf.Min(
                Mathf.Max(this.agent.speed, 0.1f) * 1.3f,
                8f
            );

            // Si plus de 10s se sont écoulées depuis le début du burning,
            // on tue l’ennemi.
            if (Time.realtimeSinceStartup - this.timeAtStartOfBurning > 10f)
            {
                base.KillEnemyOnOwnerClient(false);
            }
            break;

        default:
            return;
    }
}

// ===================== REACHFORPLAYERIFCLOSE =====================
// Méthode appelée en chase (état 1) pour animer le bras qui tente d’attraper le joueur.
private void ReachForPlayerIfClose()
{
    // Conditions : pas stun, pas perdu le joueur, pas déjà en anim spéciale avec un joueur,
    // pas d'obstacle (Linecast) entre l’oeil et la cible,
    // distance < 8f => L’ennemi peut “saisir” le joueur.
    if (this.stunNormalizedTimer <= 0f
        && !this.lostPlayerInChase
        && this.inSpecialAnimationWithPlayer == null
        && !Physics.Linecast(
            this.eye.position,
            this.chasingPlayer.transform.position,
            StartOfRound.Instance.collidersAndRoomMaskAndDefault
        )
        && Vector3.Distance(base.transform.position, this.chasingPlayer.transform.position) < 8f)
    {
        // Lerp le weight du rig vers 0.9 => plus élevé = plus forte animation de saisie.
        this.reachForPlayerRig.weight = Mathf.Lerp(
            this.reachForPlayerRig.weight,
            0.9f,
            Time.deltaTime * 6f
        );

        // On met "reachForPlayerTarget" un peu autour du joueur (random offset).
        // Cela permet un mouvement plus réaliste du bras.
        Vector3 vector = this.chasingPlayer.transform.position + Vector3.up * 0.5f;
        this.reachForPlayerTarget.position = new Vector3(
            vector.x + Random.Range(-0.2f, 0.2f),
            vector.y + Random.Range(-0.2f, 0.2f),
            vector.z + Random.Range(-0.2f, 0.2f)
        );
        return;
    }

    // Sinon, on baisse le weight => arrête l’anim de saisie.
    this.reachForPlayerRig.weight = Mathf.Lerp(
        this.reachForPlayerRig.weight,
        0f,
        Time.deltaTime * 15f
    );
}

// ========================= LOOKATTARGET =========================
// Oriente le géant vers "this.lookTarget", en tournant l’objet "turnCompass".
private void LookAtTarget()
{
    this.turnCompass.LookAt(this.lookTarget);
    base.transform.rotation = Quaternion.Lerp(
        base.transform.rotation,
        this.turnCompass.rotation,
        15f * Time.deltaTime
    );

    // Force l'axe X et Z à 0 => le géant ne penche pas la tête haut/bas,
    // il ne tourne que sur Y.
    base.transform.localEulerAngles = new Vector3(
        0f,
        base.transform.localEulerAngles.y,
        0f
    );
}
// Méthode pour repérer les joueurs dans un certain rayon / angle,
// et gérer le passage éventuel à l'état "chase" (currentBehaviourStateIndex == 1)
private void LookForPlayers()
{
    // GetAllPlayersInLineOfSight(...) est une méthode héritée de EnemyAI
    // qui retourne un tableau de PlayerControllerB détectés jusqu'à 50f de distance,
    // 70 de FOV, etc. On utilise 'this.eye' comme point de vue,
    // et collidersRoomDefaultAndFoliage comme layerMask.
    PlayerControllerB[] allPlayersInLineOfSight = base.GetAllPlayersInLineOfSight(
        50f, 70, this.eye, 3f,
        StartOfRound.Instance.collidersRoomDefaultAndFoliage
    );

    // Si on détecte au moins un joueur
    if (allPlayersInLineOfSight != null)
    {
        // playerControllerB: le joueur le plus proche ?
        PlayerControllerB playerControllerB = allPlayersInLineOfSight[0];
        int num = 0;               // index du joueur le plus proche
        float num2 = 1000f;        // distance min
        PlayerControllerB playerControllerB2 = allPlayersInLineOfSight[0];
        float num3 = 0f;           // stealthMeter le plus élevé
        float num4 = 1f;           // multiplicateur de "scrutiny" ?

        // On parcourt tous les joueurs du round,
        // et on met à jour un tableau "playerStealthMeters" pour mesurer
        // combien le géant soupçonne chaque joueur.
        for (int i = 0; i < StartOfRound.Instance.allPlayerScripts.Length; i++)
        {
            // Si le joueur fait partie de ceux en LOS:
            if (allPlayersInLineOfSight.Contains(StartOfRound.Instance.allPlayerScripts[i]))
            {
                float num5 = Vector3.Distance(
                    StartOfRound.Instance.allPlayerScripts[i].transform.position,
                    this.eye.position
                );

                // Si le joueur n’est pas accroupi => +1
                if (!StartOfRound.Instance.allPlayerScripts[i].isCrouching)
                {
                    num4 += 1f;
                }

                // timeSincePlayerMoving < 0.1f => le joueur bouge => +1
                if (StartOfRound.Instance.allPlayerScripts[i].timeSincePlayerMoving < 0.1f)
                {
                    num4 += 1f;
                }

                // On augmente le "stealthMeters[i]" en fonction de la distance,
                // du temps, et d’un facteur "scrutiny" et num4.
                this.playerStealthMeters[i] += Mathf.Clamp(
                    Time.deltaTime / (num5 * 0.21f) * this.scrutiny * num4,
                    0f,
                    1f
                );

                // Si ce joueur a maintenant le plus grand stealthMeter,
                // on le considère comme le plus "visible" (playerControllerB2).
                if (this.playerStealthMeters[i] > num3)
                {
                    num3 = this.playerStealthMeters[i];
                    playerControllerB2 = StartOfRound.Instance.allPlayerScripts[i];
                }

                // On vérifie s’il est plus proche que 'num2'
                if (num5 < num2)
                {
                    playerControllerB = StartOfRound.Instance.allPlayerScripts[i];
                    num2 = num5;
                    num = i;  // on stocke l'index de ce joueur
                }
            }
            else
            {
                // Si le joueur n’est pas dans le LOS,
                // on réduit son stealthMeter lentement.
                this.playerStealthMeters[i] -= Time.deltaTime * 0.33f;
            }
        }

        // Si on est en état "chase" (index 1)
        if (this.currentBehaviourStateIndex == 1)
        {
            // Si on a "perdu" le joueur, on met à jour chasingPlayerInLOS
            // => en gros, si le stealthMeter global > 0.15f, il est encore en vue.
            if (this.lostPlayerInChase)
            {
                this.chasingPlayerInLOS = (num3 > 0.15f);
                return;
            }

            // Sinon, on se base sur la présence de "this.chasingPlayer" dans allPlayersInLineOfSight
            // pour savoir s’il est encore visible.
            this.chasingPlayerInLOS = allPlayersInLineOfSight.Contains(this.chasingPlayer);

            // Si on est stun par un certain joueur, on le priorise.
            if (this.stunnedByPlayer != null)
            {
                playerControllerB = this.stunnedByPlayer;
            }

            // Si le joueur le plus proche n’est pas le "chasingPlayer",
            // et qu’il a un stealthMeter suffisant (>0.3) pour être ciblé,
            // et qu’on a passé >2s depuis le dernier changement de cible,
            // on switch de cible vers ce nouveau joueur.
            if (playerControllerB != this.chasingPlayer
                && this.playerStealthMeters[num] > 0.3f
                && this.timeSinceChangingTarget > 2f)
            {
                this.FindAndTargetNewPlayerOnLocalClient(playerControllerB);
                if (base.IsServer)
                {
                    this.BeginChasingNewPlayerServerRpc((int)playerControllerB.playerClientId);
                }
            }
            return;
        }
        else
        {
            // Si on est pas en chase (état 1)
            // On check si on est stun par un joueur, on priorise ce joueur dans playerControllerB2
            if (this.stunnedByPlayer != null)
            {
                playerControllerB2 = this.stunnedByPlayer;
            }

            // Si le stealthMeter le plus élevé est > 1f, ou si on est stun,
            // on déclenche la chase (BeginChasingNewPlayer).
            if (num3 > 1f || this.stunnedByPlayer)
            {
                this.BeginChasingNewPlayerClientRpc((int)playerControllerB2.playerClientId);
                this.chasingPlayerInLOS = true;
            }
            // Sinon, si > 0.35f, on "s’arrête et regarde" le joueur (stopAndLookTimer).
            else if (num3 > 0.35f)
            {
                if (this.stopAndLookTimer < 2f)
                {
                    this.stopAndLookTimer = 2f;
                }
                this.turnCompass.LookAt(playerControllerB2.transform);
                this.targetYRot = this.turnCompass.eulerAngles.y;
                this.timeSpentStaring += Time.deltaTime;
            }

            // Si on a effectivement switché en chase (==1), on arrête là.
            if (this.currentBehaviourStateIndex == 1)
            {
                return;
            }

            // Si on regarde un joueur depuis plus de 3s => on passe en "investigating"
            // => le géant va se rendre sur place (dans DoAIInterval, on gère investigating).
            if (this.timeSpentStaring > 3f && !this.investigating)
            {
                this.investigating = true;
                this.hasBegunInvestigating = false;
                this.investigatePosition = RoundManager.Instance.GetNavMeshPosition(
                    playerControllerB2.transform.position,
                    default(NavMeshHit),
                    5f,
                    -1
                );
                return;
            }
        }
    }
    else
    {
        // Sinon (pas de joueurs en LOS)
        if (this.currentBehaviourStateIndex == 1)
        {
            // Si on était en chase, on dit qu’on n’a plus le joueur en LOS.
            this.chasingPlayerInLOS = false;
        }
        // On réinitialise timeSpentStaring
        this.timeSpentStaring = 0f;
    }
}

// ==================== CHANGER DE JOUEUR CIBLE ====================
public void FindAndTargetNewPlayerOnLocalClient(PlayerControllerB newPlayer)
{
    // Met à jour "chasingPlayer" avec ce nouveau joueur
    this.chasingPlayer = newPlayer;
    // Remet à zéro le timer depuis le changement de cible
    this.timeSinceChangingTarget = 0f;
    // stopAndLookTimer remis à 0
    this.stopAndLookTimer = 0f;
}

// ================= SERVERRPC : BeginChasingNewPlayerServerRpc =================
// Appelé côté client pour demander au serveur de forcer la chase d’un nouveau joueur (playerId).
[ServerRpc]
private void BeginChasingNewPlayerServerRpc(int playerId)
{
    // Vérification du NetworkManager
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }

    // S’assurer qu’on est le owner, etc. (vérifications standard Netcode).
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
    {
        if (base.OwnerClientId != networkManager.LocalClientId)
        {
            if (networkManager.LogLevel <= LogLevel.Normal)
            {
                Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
            }
            return;
        }

        // Prépare l’envoi RPC au serveur (bytepacking).
        ServerRpcParams serverRpcParams;
        FastBufferWriter writer = base.__beginSendServerRpc(344062384U, serverRpcParams, RpcDelivery.Reliable);
        BytePacker.WriteValueBitPacked(writer, playerId);
        base.__endSendServerRpc(ref writer, 344062384U, serverRpcParams, RpcDelivery.Reliable);
    }

    // Si on est côté serveur, on appelle directement BeginChasingNewPlayerClientRpc
    if (this.__rpc_exec_stage == NetworkBehaviour.__RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
    {
        this.BeginChasingNewPlayerClientRpc(playerId);
    }
}

// ================= CLIENTRPC : BeginChasingNewPlayerClientRpc =================
// Appelé par le serveur sur tous les clients pour les synchroniser : on change de cible
[ClientRpc]
private void BeginChasingNewPlayerClientRpc(int playerId)
{
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }

    // Côté serveur => distribution du RPC (idem verif).
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
    {
        ClientRpcParams clientRpcParams;
        FastBufferWriter writer = base.__beginSendClientRpc(1296181132U, clientRpcParams, RpcDelivery.Reliable);
        BytePacker.WriteValueBitPacked(writer, playerId);
        base.__endSendClientRpc(ref writer, 1296181132U, clientRpcParams, RpcDelivery.Reliable);
    }

    // Si on est réellement un client, on exécute la logique :
    if (this.__rpc_exec_stage == NetworkBehaviour.__RpcExecStage.Client && (networkManager.IsClient || networkManager.IsHost))
    {
        // reset timers
        this.noticePlayerTimer = 0f;
        this.timeSinceChangingTarget = 0f;

        // on définit chasingPlayer d’après l’ID passé en param
        this.chasingPlayer = StartOfRound.Instance.allPlayerScripts[playerId];

        // on signale qu’on n’a pas “perdu le joueur”,
        // et qu’on le regarde
        this.hasLostPlayerInChaseDebounce = false;
        this.lostPlayerInChase = false;

        // si on change de cible après un certain temps,
        // on peut couper la vitesse.
        if (this.timeSinceChangingTarget > 1f)
        {
            this.agent.speed = 0f;
        }

        // On passe l’état local en 1 (chase).
        base.SwitchToBehaviourStateOnLocalClient(1);
    }
}

// ================= CLIENTRPC : HasLostPlayerInChaseClientRpc =================
// Appelé par le serveur pour indiquer à tous les clients que l’IA a “perdu” la cible.
[ClientRpc]
private void HasLostPlayerInChaseClientRpc()
{
    // Vérifs Netcode standard
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
    {
        ClientRpcParams clientRpcParams;
        FastBufferWriter fastBufferWriter = base.__beginSendClientRpc(3295708237U, clientRpcParams, RpcDelivery.Reliable);
        base.__endSendClientRpc(ref fastBufferWriter, 3295708237U, clientRpcParams, RpcDelivery.Reliable);
    }
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client || (!networkManager.IsClient && !networkManager.IsHost))
    {
        return;
    }

    // On marque lostPlayerInChase = true,
    // lookingAtTarget = false => on ne regarde plus le joueur.
    this.lostPlayerInChase = true;
    this.lookingAtTarget = false;
}

// ================= CLIENTRPC : HasFoundPlayerInChaseClientRpc =================
// Inverse de la précédente, signale qu’on a retrouvé le joueur.
[ClientRpc]
private void HasFoundPlayerInChaseClientRpc()
{
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
    {
        ClientRpcParams clientRpcParams;
        FastBufferWriter fastBufferWriter = base.__beginSendClientRpc(2685047264U, clientRpcParams, RpcDelivery.Reliable);
        base.__endSendClientRpc(ref fastBufferWriter, 2685047264U, clientRpcParams, RpcDelivery.Reliable);
    }
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client || (!networkManager.IsClient && !networkManager.IsHost))
    {
        return;
    }

    // On repasse lostPlayerInChase = false, lookingAtTarget = true.
    this.lostPlayerInChase = false;
    this.lookingAtTarget = true;
}

// ================= CALCULATEANIMATIONDIRECTION =================
// Gère les paramètres VelocityX / VelocityY pour l’Animator
// en fonction de la vitesse locale (agentLocalVelocity).
private void CalculateAnimationDirection(float maxSpeed = 1f)
{
    // agentLocalVelocity est calculée en comparant la position actuelle
    // à la previousPosition, transformée dans l’espace local (animationContainer).
    this.agentLocalVelocity = this.animationContainer.InverseTransformDirection(
        Vector3.ClampMagnitude(base.transform.position - this.previousPosition, 1f)
        / (Time.deltaTime * 4f)
    );

    // Interpolation (Lerp) de velX / velZ
    this.velX = Mathf.Lerp(this.velX, this.agentLocalVelocity.x, 5f * Time.deltaTime);
    this.creatureAnimator.SetFloat("VelocityX", Mathf.Clamp(this.velX, -maxSpeed, maxSpeed));

    this.velZ = Mathf.Lerp(this.velZ, this.agentLocalVelocity.z, 5f * Time.deltaTime);
    this.creatureAnimator.SetFloat("VelocityY", Mathf.Clamp(this.velZ, -maxSpeed, maxSpeed));

    // Met à jour la "previousPosition" pour le prochain calcul
    this.previousPosition = base.transform.position;
}

// ================= ONCOLLIDEWITHPLAYER =================
// Méthode appelée quand un player entre en collision avec l’ennemi.
public override void OnCollideWithPlayer(Collider other)
{
    // Logique parent de gestion de collision
    base.OnCollideWithPlayer(other);

    // Si on est déjà dans une animation spéciale (manger un joueur) => on ignore
    if (this.inSpecialAnimationWithPlayer != null || this.inEatingPlayerAnimation)
    {
        return;
    }

    // Si on est stun (stunNormalizedTimer >= 0f) => on ignore
    // (peut-être >= 0f signifie "stun actif"?)
    if (this.stunNormalizedTimer >= 0f)
    {
        return;
    }

    // Si on est en état 2 (burning), on ignore
    if (this.currentBehaviourStateIndex == 2)
    {
        return;
    }

    // "MeetsStandardPlayerCollisionConditions" => méthode parent
    // qui vérifie si 'other' est bien un PlayerControllerB valide.
    PlayerControllerB playerControllerB = base.MeetsStandardPlayerCollisionConditions(
        other,
        this.inEatingPlayerAnimation,
        false
    );

    // Si c’est un joueur local valide
    if (playerControllerB != null && playerControllerB == GameNetworkManager.Instance.localPlayerController)
    {
        // On vérifie s’il n’est pas dans un véhicule fermé, etc. => skip
        VehicleController vehicleController = Object.FindObjectOfType<VehicleController>();
        if (vehicleController != null
            && playerControllerB.physicsParent != null
            && playerControllerB.physicsParent == vehicleController.transform
            && !vehicleController.backDoorOpen)
        {
            return;
        }

        // On lance un linecast pour voir si c’est pas bloqué par un mur
        Vector3 a = Vector3.Normalize(
            (this.centerPosition.position - (GameNetworkManager.Instance.localPlayerController.transform.position + Vector3.up * 1.5f))
            * 1000f
        );
        RaycastHit raycastHit;
        if (Physics.Linecast(
            this.centerPosition.position + a * 1.7f,
            GameNetworkManager.Instance.localPlayerController.transform.position + Vector3.up * 1.5f,
            out raycastHit,
            StartOfRound.Instance.collidersAndRoomMaskAndDefault,
            QueryTriggerInteraction.Ignore
        ))
        {
            return;
        }

        // Si le vaisseau part ou n’a pas atterri, et que le joueur est dedans, skip
        if ((StartOfRound.Instance.shipIsLeaving || !StartOfRound.Instance.shipHasLanded)
            && GameNetworkManager.Instance.localPlayerController.isInHangarShipRoom)
        {
            return;
        }

        // Si le joueur est déjà dans une anim avec un autre ennemi, skip
        if (playerControllerB.inAnimationWithEnemy != null)
        {
            return;
        }

        // Si le joueur fait une animation spéciale (ex: crocheter un coffre ?),
        // on arrête cette animation (CancelAnimationExternally).
        if (playerControllerB.inSpecialInteractAnimation
            && playerControllerB.currentTriggerInAnimationWith != null)
        {
            playerControllerB.currentTriggerInAnimationWith.CancelAnimationExternally();
        }

        // Si on est en patrouille (état 0) et qu’on n’a pas déjà trigger la chase,
        // on lance la chase vers ce joueur (BeginChasingNewPlayerServerRpc).
        if (this.currentBehaviourStateIndex == 0 && !this.triggerChaseByTouchingDebounce)
        {
            this.triggerChaseByTouchingDebounce = true;
            this.BeginChasingNewPlayerServerRpc((int)playerControllerB.playerClientId);
            return;
        }

        // Sinon, on "Grab" le joueur => l’ennemi enclenche l’animation de saisie (GrabPlayerServerRpc).
        this.GrabPlayerServerRpc((int)playerControllerB.playerClientId);
    }
}
// ============== GrabPlayerServerRpc ==============
// Méthode appelée par un client (owner) pour demander au serveur
// de saisir (grab) un joueur "playerId".
[ServerRpc(RequireOwnership = false)]
public void GrabPlayerServerRpc(int playerId)
{
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }

    // Vérifie qu'on est bien en phase "Server"
    // ou qu'on a l'autorité pour appeler ce RPC.
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
    {
        ServerRpcParams serverRpcParams;
        FastBufferWriter writer = base.__beginSendServerRpc(2965927486U, serverRpcParams, RpcDelivery.Reliable);
        BytePacker.WriteValueBitPacked(writer, playerId);
        base.__endSendServerRpc(ref writer, 2965927486U, serverRpcParams, RpcDelivery.Reliable);
    }

    // S'il s'agit vraiment du serveur qui exécute
    // (et qu'on a l'autorité), on procède.
    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Server || (!networkManager.IsServer && !networkManager.IsHost))
    {
        return;
    }

    // inSpecialAnimationWithPlayer => si on est déjà en animation spéciale
    // (ex: en train de manger un autre joueur), on annule.
    if (this.inSpecialAnimationWithPlayer != null)
    {
        return;
    }

    // On calcule la rotation du géant (enemyYRot).
    // Si un Raycast devant le géant touche un obstacle à moins de 6m,
    // il se tournera vers l'angle "farthestFromPosition" (éviter les collisions ?).
    Vector3 position = base.transform.position;
    int enemyYRot = (int)base.transform.eulerAngles.y;
    RaycastHit raycastHit;
    if (Physics.Raycast(
        this.centerPosition.position,
        this.centerPosition.forward,
        out raycastHit,
        6f,
        StartOfRound.Instance.collidersAndRoomMaskAndDefault,
        QueryTriggerInteraction.Ignore
    ))
    {
        enemyYRot = (int)RoundManager.Instance.YRotationThatFacesTheFarthestFromPosition(position, 20f, 5);
    }

    // On envoie un ClientRpc pour que tous les clients lancent GrabPlayerClientRpc
    this.GrabPlayerClientRpc(playerId, position, enemyYRot);
}

// ============== GrabPlayerClientRpc ==============
// Reçu par tous les clients pour synchroniser le "grab".
// Lance le BeginEatPlayer(...) sur chaque client.
[ClientRpc]
public void GrabPlayerClientRpc(int playerId, Vector3 enemyPosition, int enemyYRot)
{
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }

    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client && (networkManager.IsServer || networkManager.IsHost))
    {
        ClientRpcParams clientRpcParams;
        FastBufferWriter writer = base.__beginSendClientRpc(3924255731U, clientRpcParams, RpcDelivery.Reliable);
        BytePacker.WriteValueBitPacked(writer, playerId);
        writer.WriteValueSafe(enemyPosition);
        BytePacker.WriteValueBitPacked(writer, enemyYRot);
        base.__endSendClientRpc(ref writer, 3924255731U, clientRpcParams, RpcDelivery.Reliable);
    }

    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Client || (!networkManager.IsClient && !networkManager.IsHost))
    {
        return;
    }

    // Si on est déjà en animation spéciale, on ne fait rien.
    if (this.inSpecialAnimationWithPlayer != null)
    {
        return;
    }

    // Lance l’animation "BeginEatPlayer" côté client.
    // playerId : index du joueur, enemyPosition & enemyYRot : position/rotation finale du géant.
    this.BeginEatPlayer(StartOfRound.Instance.allPlayerScripts[playerId], enemyPosition, enemyYRot);
}

// ============== BeginEatPlayer ==============
// Configure l’état pour démarrer la coroutine EatPlayerAnimation(...)
private void BeginEatPlayer(PlayerControllerB playerBeingEaten, Vector3 enemyPosition, int enemyYRot)
{
    this.inSpecialAnimationWithPlayer = playerBeingEaten;
    this.inSpecialAnimationWithPlayer.inSpecialInteractAnimation = true;
    this.inSpecialAnimationWithPlayer.inAnimationWithEnemy = this;

    if (this.eatPlayerCoroutine != null)
    {
        base.StopCoroutine(this.eatPlayerCoroutine);
    }

    // Lance la coroutine d’animation de "manger" le joueur.
    this.eatPlayerCoroutine = base.StartCoroutine(
        this.EatPlayerAnimation(playerBeingEaten, enemyPosition, enemyYRot)
    );
}

// ============== EatPlayerAnimation ==============
// Coroutine qui gère l’animation de saisie, la mise à mort du joueur, etc.
private IEnumerator EatPlayerAnimation(PlayerControllerB playerBeingEaten, Vector3 enemyPosition, int enemyYRot)
{
    // On désactive "lookingAtTarget"
    this.lookingAtTarget = false;

    // Déclenche le trigger "EatPlayer" dans l’Animator => animation de manger.
    this.creatureAnimator.SetTrigger("EatPlayer");
    this.inEatingPlayerAnimation = true;
    this.inSpecialAnimation = true;

    // Force le joueur à sortir de l’ascenseur/hangar, au cas où
    playerBeingEaten.isInElevator = false;
    playerBeingEaten.isInHangarShipRoom = false;

    // On stocke la position/rotation actuelles (startPosition / startRotation).
    Vector3 startPosition = base.transform.position;
    Quaternion startRotation = base.transform.rotation;

    // Petit lerp sur 10 itérations pour déplacer le géant vers "enemyPosition"
    // et ajuster sa rotation vers "enemyYRot".
    for (int i = 0; i < 10; i++)
    {
        base.transform.position = Vector3.Lerp(
            startPosition,
            enemyPosition,
            (float)i / 10f
        );
        base.transform.rotation = Quaternion.Lerp(
            startRotation,
            Quaternion.Euler(
                base.transform.eulerAngles.x,
                (float)enemyYRot,
                base.transform.eulerAngles.z
            ),
            (float)i / 10f
        );
        yield return new WaitForSeconds(0.01f);
    }

    // Fixe la position/rotation finale.
    base.transform.position = enemyPosition;
    base.transform.rotation = Quaternion.Euler(
        base.transform.eulerAngles.x,
        (float)enemyYRot,
        base.transform.eulerAngles.z
    );
    this.serverRotation = base.transform.eulerAngles;

    // Petite pause
    yield return new WaitForSeconds(0.2f);

    // On désactive "inSpecialAnimation" (le gros déplacement est fini).
    this.inSpecialAnimation = false;

    // On attend 4.4s => l’ennemi "mange" le joueur pendant ce laps de temps.
    yield return new WaitForSeconds(4.4f);

    // Si le joueur est toujours vivant et lié à cet ennemi...
    if (playerBeingEaten.inAnimationWithEnemy == this && !playerBeingEaten.isPlayerDead)
    {
        // On "tue" le joueur (KillPlayer).
        this.inSpecialAnimationWithPlayer = null;
        playerBeingEaten.KillPlayer(Vector3.zero, false, CauseOfDeath.Crushing, 0, default(Vector3));
        playerBeingEaten.inSpecialInteractAnimation = false;
        playerBeingEaten.inAnimationWithEnemy = null;

        // On affiche du sang sur le visage du géant (bloodOnFaceDecal).
        this.bloodOnFaceDecal.enabled = true;

        // Petite pause avant la fin.
        yield return new WaitForSeconds(3f);
    }
    else
    {
        // Sinon, on arrête simplement le son ?
        this.creatureVoice.Stop();
    }

    // On sort de l’animation de manger.
    this.inEatingPlayerAnimation = false;
    this.inSpecialAnimationWithPlayer = null;

    // Si on est Owner, on vérifie s’il y a encore un joueur en vue :
    // si non, on repasse en Patrol (état 0).
    if (!base.IsOwner)
    {
        yield break;
    }
    if (base.CheckLineOfSightForPlayer(50f, 15, -1) != null)
    {
        // ex: on continue le chase si on voit quelqu’un ?
        PlayerControllerB playerControllerB = this.chasingPlayer;
    }
    else
    {
        // On repasse en patrol
        base.SwitchToBehaviourState(0);
    }
}

// ============== DropPlayerBody ==============
// Interrompt l’animation spéciale (si le joueur est en train d’être mangé).
private void DropPlayerBody()
{
    if (this.inSpecialAnimationWithPlayer != null)
    {
        this.inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
        this.inSpecialAnimationWithPlayer.inSpecialInteractAnimation = false;
        // (Redondant, probablement une faute de copier-coller).
        this.inSpecialAnimationWithPlayer.inAnimationWithEnemy = null;
        this.inSpecialAnimationWithPlayer = null;
    }
}

// ============== StopKillAnimation ==============
// Arrête la coroutine "EatPlayerAnimation", libère le joueur, etc.
private void StopKillAnimation()
{
    if (this.eatPlayerCoroutine != null)
    {
        base.StopCoroutine(this.eatPlayerCoroutine);
    }
    this.inEatingPlayerAnimation = false;
    this.inSpecialAnimation = false;
    this.DropPlayerBody();
    this.creatureVoice.Stop();
}

// ============== ReactToNoise ==============
// Gère la réaction du géant quand il entend un bruit (distanceToNoise).
// Il peut regarder vers le bruit, s’arrêter, ou déclencher l’investigation.
private void ReactToNoise(float distanceToNoise, Vector3 noisePosition)
{
    // Si on est en chase (état 1)
    if (this.currentBehaviourStateIndex == 1)
    {
        // S’il voit déjà le joueur (chasingPlayerInLOS) et que ce bruit est
        // bien plus proche que le joueur, il se retourne un instant (stopAndLookTimer = 1f).
        if (this.chasingPlayerInLOS && distanceToNoise - Vector3.Distance(base.transform.position, this.chasingPlayer.transform.position) < -3f)
        {
            this.stopAndLookTimer = 1f;
            this.turnCompass.LookAt(noisePosition);
            this.targetYRot = this.turnCompass.eulerAngles.y;
            return;
        }
        // Si le bruit est < 15f et que noticePlayerTimer > 3f,
        // on s’arrête 2s et on se tourne vers le bruit.
        if (distanceToNoise < 15f && this.noticePlayerTimer > 3f)
        {
            this.stopAndLookTimer = 2f;
            this.turnCompass.LookAt(noisePosition);
            this.targetYRot = this.turnCompass.eulerAngles.y;
            return;
        }
    }
    else
    {
        // Si on est en état 0 (patrouille) ou autre,
        // on s’arrête 1.5s, on regarde le bruit,
        // on incrémente timeSpentStaring.
        this.stopAndLookTimer = 1.5f;
        this.turnCompass.LookAt(noisePosition);
        this.targetYRot = this.turnCompass.eulerAngles.y;
        this.timeSpentStaring += 0.3f;

        // Si on a passé 3s à regarder => passer en "investigating".
        if (this.timeSpentStaring > 3f)
        {
            this.investigating = true;
            this.hasBegunInvestigating = false;
            this.investigatePosition = RoundManager.Instance.GetNavMeshPosition(
                noisePosition,
                default(NavMeshHit),
                5f,
                -1
            );
        }
    }
}

// ============== DetectPlayerVoiceServerRpc ==============
// Appelé par un client quand un joueur fait du bruit (parler ?).
[ServerRpc]
public void DetectPlayerVoiceServerRpc(Vector3 noisePosition)
{
    NetworkManager networkManager = base.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }

    if (this.__rpc_exec_stage != NetworkBehaviour.__RpcExecStage.Server && (networkManager.IsClient || networkManager.IsHost))
    {
        if (base.OwnerClientId != networkManager.LocalClientId)
        {
            if (networkManager.LogLevel <= LogLevel.Normal)
            {
                Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
            }
            return;
        }
        ServerRpcParams serverRpcParams;
        FastBufferWriter fastBufferWriter = base.__beginSendServerRpc(1714423781U, serverRpcParams, RpcDelivery.Reliable);
        fastBufferWriter.WriteValueSafe(noisePosition);
        base.__endSendServerRpc(ref fastBufferWriter, 1714423781U, serverRpcParams, RpcDelivery.Reliable);
    }

    // Si on est vraiment sur le serveur, on calcule la distance et appelle ReactToNoise().
    if (this.__rpc_exec_stage == NetworkBehaviour.__RpcExecStage.Server && (networkManager.IsServer || networkManager.IsHost))
    {
        float distanceToNoise = Vector3.Distance(noisePosition, base.transform.position);
        this.ReactToNoise(distanceToNoise, noisePosition);
    }
}
// ==================================================================
// INITIALISATION ET HANDLERS RPC
// ==================================================================

// Méthode protégée override pour initialiser des variables (héritée de EnemyAI).
// Ici, elle appelle simplement la méthode parent.
protected override void __initializeVariables()
{
    base.__initializeVariables();
}

// Méthode statique marquée [RuntimeInitializeOnLoadMethod] :
// Appelée automatiquement quand la scène se charge.
// Elle enregistre les gestionnaires RPC dans "NetworkManager.__rpc_func_table".
[RuntimeInitializeOnLoadMethod]
internal static void InitializeRPCS_ForestGiantAI()
{
    // Chaque ligne associe un "uint" (ex: 344062384U) à un "RpcReceiveHandler".
    // Ce sont les identifiants de chaque RPC (BeginChasing, GrabPlayer, etc.).
    NetworkManager.__rpc_func_table.Add(344062384U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_344062384));
    NetworkManager.__rpc_func_table.Add(1296181132U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_1296181132));
    NetworkManager.__rpc_func_table.Add(3295708237U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_3295708237));
    NetworkManager.__rpc_func_table.Add(2685047264U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_2685047264));
    NetworkManager.__rpc_func_table.Add(2965927486U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_2965927486));
    NetworkManager.__rpc_func_table.Add(3924255731U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_3924255731));
    NetworkManager.__rpc_func_table.Add(1714423781U, new NetworkManager.RpcReceiveHandler(ForestGiantAI.__rpc_handler_1714423781));
}

// =================== HANDLERS DE RPC ===================
// Chaque handler est une méthode statique appelée quand l’ID RPC correspondant arrive.
// Ils reconstruisent les paramètres depuis le réseau (BitPacked, etc.),
// puis appellent la bonne méthode (BeginChasingNewPlayerServerRpc, etc.) côté serveur/client.

private static void __rpc_handler_344062384(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    // On récupère le NetworkManager lié au "target" (ForestGiantAI).
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }

    // Vérifie que celui qui appelle ce RPC est bien le "OwnerClientId" de l'objet (contrôle d'autorité).
    if (rpcParams.Server.Receive.SenderClientId != target.OwnerClientId)
    {
        if (networkManager.LogLevel <= LogLevel.Normal)
        {
            Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
        }
        return;
    }

    // On lit un paramètre "playerId" packé dans "reader".
    int playerId;
    ByteUnpacker.ReadValueBitPacked(reader, out playerId);

    // On indique qu’on exécute un RPC côté serveur, puis on appelle la vraie méthode.
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Server;
    ((ForestGiantAI)target).BeginChasingNewPlayerServerRpc(playerId);
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

private static void __rpc_handler_1296181132(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    int playerId;
    ByteUnpacker.ReadValueBitPacked(reader, out playerId);
    // RPC exécuté côté client => on appelle BeginChasingNewPlayerClientRpc
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Client;
    ((ForestGiantAI)target).BeginChasingNewPlayerClientRpc(playerId);
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

private static void __rpc_handler_3295708237(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    // Exécuté côté client => HasLostPlayerInChaseClientRpc
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Client;
    ((ForestGiantAI)target).HasLostPlayerInChaseClientRpc();
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

private static void __rpc_handler_2685047264(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    // Exécuté côté client => HasFoundPlayerInChaseClientRpc
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Client;
    ((ForestGiantAI)target).HasFoundPlayerInChaseClientRpc();
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

private static void __rpc_handler_2965927486(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    int playerId;
    ByteUnpacker.ReadValueBitPacked(reader, out playerId);
    // Exécuté côté serveur => GrabPlayerServerRpc
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Server;
    ((ForestGiantAI)target).GrabPlayerServerRpc(playerId);
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

private static void __rpc_handler_3924255731(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    int playerId;
    ByteUnpacker.ReadValueBitPacked(reader, out playerId);

    Vector3 enemyPosition;
    reader.ReadValueSafe(out enemyPosition);

    int enemyYRot;
    ByteUnpacker.ReadValueBitPacked(reader, out enemyYRot);

    // Exécuté côté client => GrabPlayerClientRpc
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Client;
    ((ForestGiantAI)target).GrabPlayerClientRpc(playerId, enemyPosition, enemyYRot);
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

private static void __rpc_handler_1714423781(NetworkBehaviour target, FastBufferReader reader, __RpcParams rpcParams)
{
    NetworkManager networkManager = target.NetworkManager;
    if (networkManager == null || !networkManager.IsListening)
    {
        return;
    }
    if (rpcParams.Server.Receive.SenderClientId != target.OwnerClientId)
    {
        if (networkManager.LogLevel <= LogLevel.Normal)
        {
            Debug.LogError("Only the owner can invoke a ServerRpc that requires ownership!");
        }
        return;
    }
    Vector3 noisePosition;
    reader.ReadValueSafe(out noisePosition);

    // Exécuté côté serveur => DetectPlayerVoiceServerRpc
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.Server;
    ((ForestGiantAI)target).DetectPlayerVoiceServerRpc(noisePosition);
    target.__rpc_exec_stage = NetworkBehaviour.__RpcExecStage.None;
}

// Redéfinit le typeName, retourne "ForestGiantAI"
protected internal override string __getTypeName()
{
    return "ForestGiantAI";
}

// ==================================================================
// VARIABLES MEMBRES (champs) DU ForestGiantAI
// ==================================================================

// Coroutines, booléens et transforms divers
private Coroutine eatPlayerCoroutine;
private bool inEatingPlayerAnimation;

// Point où le géant tient le joueur
public Transform holdPlayerPoint;

// AISearchRoutine => scripts (coroutines) pour patrouiller et rechercher
public AISearchRoutine roamPlanet;
public AISearchRoutine searchForPlayers;

// Vitesse locale pour l’animation (VelocityX / VelocityY)
private float velX;
private float velZ;
private Vector3 previousPosition;
private Vector3 agentLocalVelocity;

// Container Transform pour l’animation
public Transform animationContainer;

// IKConstraint pour saisir le joueur
public TwoBoneIKConstraint reachForPlayerRig;
public Transform reachForPlayerTarget;

// Timers et angles pour se retourner, "stopAndLook"
private float stopAndLookInterval;
private float stopAndLookTimer;
private float targetYRot;

// "scrutiny" => intensité de la détection
public float scrutiny = 1f;
// Array pour 4 joueurs max => "stealthMeters"
public float[] playerStealthMeters = new float[4];

// Variables de "staring"
public float timeSpentStaring;
public bool investigating;
private bool hasBegunInvestigating;
public Vector3 investigatePosition;

// Référence au joueur qu’on poursuit
public PlayerControllerB chasingPlayer;
private bool lostPlayerInChase;
private float noticePlayerTimer;
private bool lookingAtTarget;
public Transform turnCompass;
public Transform lookTarget;
private bool chasingPlayerInLOS;
private float timeSinceChangingTarget;
private bool hasLostPlayerInChaseDebounce;
private bool triggerChaseByTouchingDebounce;

// AudioSource pour un son d’ambiance lointain
public AudioSource farWideSFX;
// Décalcomanie de sang
public DecalProjector bloodOnFaceDecal;

// Position du dernier joueur vu pendant la chase
private Vector3 lastSeenPlayerPositionInChase;

// Timer depuis qu’on a détecté une voix ?
private float timeSinceDetectingVoice;

// Points de repère pour la position/centre du géant
public Transform centerPosition;
public Transform handBone;
public Transform deathFallPosition;

// Clips audio divers (cri, etc.)
public AudioClip giantFall;
public AudioClip giantCry;

// AudioSource + particules pour quand il brûle
public AudioSource giantBurningAudio;
public GameObject burningParticlesContainer;
private float timeAtStartOfBurning;
