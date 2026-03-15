using System.Reflection;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Bootstrap;
using UnityEngine;
using System;
using System.Linq;
using LethalLib;
using LethalLib.Extras;
using LethalLib.Modules;
using BepInEx.Configuration;

using static LethalLib.Modules.Levels;

namespace Spiner
{
    [BepInPlugin("Jeez.Spiner", "Spiner Mod", "2.0.0")]
    [BepInDependency("evaisa.lethallib")]
    [BepInDependency("ainavt.lc.lethalconfig", BepInDependency.DependencyFlags.SoftDependency)]

    public class SpinerPlugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        public static AssetBundle? Assets;
        public static EnemyType? EnemyType;
        public static GameObject? enemyPrefab;
        public static TerminalNode? EnemyTerminalNode;
        public static TerminalKeyword? EnemyTerminalKeyword;
        public static ConfigEntry<int> MaxHP = null!;
        public static ConfigEntry<float> RoamVolume = null!;
        public static ConfigEntry<bool> SoundStyle = null!;
        public static ConfigEntry<bool> DarkMode = null!;
        public static ConfigEntry<float> DarkReviveDelay = null!;
        public static ConfigEntry<float> DarkKillTime = null!;
        public static ConfigEntry<int> SpawnWeight = null!;



        // Profil runtime partagé
        [System.Serializable]
        public class SpinerRuntimeConfig
        {
            public int maxHP;
            public float roamVolume;
            public bool soundStyle;
            public bool darkMode;
            public float darkReviveDelay;
            public float darkKillTime;
        }

        public static SpinerRuntimeConfig RuntimeConfig = new SpinerRuntimeConfig();

        private void LoadAssets()
        {
            LogInfo("[Spiner] Starting to load AssetBundle...");
            try
            {
                string bundlePath = this.GetFilePath("Spiner");
                LogInfo($"[Spiner] Loading AssetBundle from: {bundlePath}");

                Assets = AssetBundle.LoadFromFile(bundlePath);
                if (Assets == null)
                {
                    LogError("[Spiner] Failed to load AssetBundle: Spiner");
                    return;
                }

                // Log the contents of the AssetBundle
                LogAssetBundleContents(Assets);

                LogInfo("[Spiner] AssetBundle loaded successfully.");

                // Load the prefab
                LogInfo("[Spiner] Loading enemy definition: SpinerObj.prefab");
                enemyPrefab = Assets.LoadAsset<GameObject>("assets/modassets/Spiner/SpinerObj.prefab");
                if (enemyPrefab == null)
                {
                    LogError("[Spiner] Failed to load SpinerObj.prefab from AssetBundle");
                    return;
                }
                LogInfo("[Spiner] Prefab loaded successfully.");

                // Load the EnemyType from the AssetBundle
                LogInfo("[Spiner] Loading EnemyType definition: Spiner.asset");
                EnemyType = Assets.LoadAsset<EnemyType>("assets/modassets/Spiner/Spiner.asset");
                if (EnemyType == null)
                {
                    LogError("[Spiner] Failed to load EnemyType from AssetBundle. Ensure it is properly configured.");
                    return;
                }
                LogInfo("[Spiner] EnemyType loaded successfully.");

                // Ensure the EnemyType prefab matches
                EnemyType.enemyPrefab = enemyPrefab;
                LogInfo("[Spiner] EnemyType prefab assigned successfully.");
            }
            catch (Exception ex)
            {
                LogError($"[Spiner] Failed to load assets: {ex.Message}");
            }
        }

        private void Awake()
        {
            Logger = base.Logger;
            LogInfo("[Spiner] Plugin Awake - Initializing...");

            // --- CONFIG BepInEx + LethalConfig ---
            MaxHP = Config.Bind("Stats", "MaxHP", 5, "Spiner max HP");
            RoamVolume = Config.Bind("Audio", "RoamVolume", 0.2f, "Roam sound volume (0..1)");
            SoundStyle = Config.Bind("Audio", "QuackSounds", false, "Use quack sound pack instead of normal sounds");
            DarkMode = Config.Bind("DarkMode", "Enabled", true, "Enable Dark Mode (feint death + lethal phase 2)");
            DarkReviveDelay = Config.Bind("DarkMode", "ReviveDelaySec", 25.0f, "Seconds before revival");
            DarkKillTime = Config.Bind("DarkMode", "KillTimeSec", 20.0f, "Seconds before executing the captive in phase 2");
            SpawnWeight = Config.Bind("Spawns", "SpawnWeight", 40, "Spawn weight (0 = disabled)");


            // (optionnel) sliders LethalConfig
            TryRegisterLethalConfigSliders();
            /*LethalConfigManager.AddConfigItem
                (new BoolCheckBoxConfigItem(DarkMode, true));
            LethalConfigManager.AddConfigItem
                (new FloatSliderConfigItem(DarkReviveDelay, new FloatSliderOptions { Min = 1, Max = 30, RequiresRestart = false }));
            LethalConfigManager.AddConfigItem
                (new FloatSliderConfigItem(DarkKillTime, new FloatSliderOptions { Min = 1, Max = 60, RequiresRestart = false }));
            LethalConfigManager.AddConfigItem(
                new IntSliderConfigItem(MaxHP, new IntSliderOptions { Min = 1, Max = 20, RequiresRestart = false }));
            LethalConfigManager.AddConfigItem(
                new FloatSliderConfigItem(RoamVolume, new FloatSliderOptions { Min = 0f, Max = 1f, RequiresRestart = false }));
*/

            InitializeNetworkBehaviours();

            LoadAssets();

            if (Assets == null)
            {
                LogError("[Spiner] AssetBundle is null after LoadAssets() -> cannot load terminal assets.");
                return;
            }

            if (enemyPrefab == null || EnemyType == null)
            {
                LogError("[Spiner] Prefab or EnemyType is null. Registration aborted.");
                return;
            }

            EnemyTerminalNode = Assets.LoadAsset<TerminalNode>(
                "assets/modassets/spiner/spiner tn.asset"
            );
            EnemyTerminalKeyword = Assets.LoadAsset<TerminalKeyword>(
                "assets/modassets/spiner/spiner tk.asset"
            );

            if (EnemyTerminalNode != null)
                LogInfo($"[TERM] node ok | name={EnemyTerminalNode.name} | textLen={(EnemyTerminalNode.displayText?.Length ?? 0)}");

            if (EnemyTerminalKeyword != null)
                LogInfo($"[TERM] keyword ok | name={EnemyTerminalKeyword.name} | word={EnemyTerminalKeyword.word} | isVerb={EnemyTerminalKeyword.isVerb}");

            if (EnemyTerminalNode == null || EnemyTerminalKeyword == null)
            {
                LogWarning("[Spiner] Terminal description not found (no bestiary entry).");
            }

            LogInfo("[Spiner] Registering enemy prefab in the network...");
            NetworkPrefabs.RegisterNetworkPrefab(enemyPrefab);
            LogInfo("[Spiner] Enemy prefab registered successfully.");

            LogInfo("[Spiner] Registering enemy in the spawn system...");

            int weight = Mathf.Max(0, SpawnWeight.Value);
            if (weight == 0)
            {
                LogInfo("[Spiner] SpawnWeight = 0 → Spiner désactivé.");
            }
            else
            {
                Enemies.RegisterEnemy(
                    EnemyType,
                    weight,
                    LevelTypes.All,
                    Enemies.SpawnType.Default,
                    EnemyTerminalNode!,
                    EnemyTerminalKeyword!
                );
                LogInfo($"[Spiner] Enemy registered. weight={weight}");
            }



            LogInfo("[Spiner] Enemy registered successfully in the spawn system.");

            LogInfo("[Spiner] Plugin initialized successfully.");
        }
        public static void LogInfo(string message)
        {
            if (Logger != null)
            {
                string timestamp = $"[Frame {Time.frameCount}] ";
                Logger.LogInfo(timestamp + message);
            }
        }

        public static void LogWarning(string message)
        {
            if (Logger != null)
            {
                string timestamp = $"[Frame {Time.frameCount}] ";
                Logger.LogWarning(timestamp + message);
            }
        }

        public static void LogError(string message)
        {
            if (Logger != null)
            {
                string timestamp = $"[Frame {Time.frameCount}] ";
                Logger.LogError(timestamp + message);
            }
        }

        public static void LogDebug(string message)
        {
            if (Logger != null)
            {
                string timestamp = $"[Frame {Time.frameCount}] ";
                Logger.LogDebug(timestamp + message);
            }
        }


        private string GetFilePath(string path)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(this.GetType().Assembly.Location)!, path);
        }

        private static void InitializeNetworkBehaviours()
        {
            // See https://github.com/EvaisaDev/UnityNetcodePatcher?tab=readme-ov-file#preparing-mods-for-patching
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }

        private void LogAssetBundleContents(AssetBundle assetBundle)
        {
            if (assetBundle == null)
            {
                LogError("[Spiner] AssetBundle is null. Cannot list contents.");
                return;
            }

            try
            {
                string[] assetNames = assetBundle.GetAllAssetNames();
                if (assetNames.Length == 0)
                {
                    Logger.LogWarning("[Spiner] AssetBundle contains no assets.");
                    return;
                }

                LogInfo("[Spiner] Listing all assets in the AssetBundle:");
                foreach (string assetName in assetNames)
                {
                    LogInfo($"[Spiner] Found asset: {assetName}");
                }
            }
            catch (Exception ex)
            {
                LogError($"[Spiner] Failed to list AssetBundle contents: {ex.Message}");
            }
        }
        public static string GetRuntimeConfigJson()
        {
            // Lire les valeurs ACTUELLES (même session, sans redémarrer)
            var cfg = new SpinerRuntimeConfig
            {
                maxHP = MaxHP.Value,
                roamVolume = RoamVolume.Value,
                soundStyle = SoundStyle.Value,
                darkMode = DarkMode.Value,
                darkReviveDelay = DarkReviveDelay.Value,
                darkKillTime = DarkKillTime.Value
            };
            return JsonUtility.ToJson(cfg);
        }

        private void TryRegisterLethalConfigSliders()
        {
            try
            {
                // 1) Vérifier si le plugin LethalConfig est chargé
                if (!Chainloader.PluginInfos.ContainsKey("ainavt.lc.lethalconfig"))
                {
                    LogInfo("[Spiner] LethalConfig non détecté, sliders ignorés.");
                    return;
                }

                // 2) Récupérer l'assembly LethalConfig
                var lethalConfigAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "LethalConfig");

                if (lethalConfigAsm == null)
                {
                    LogWarning("[Spiner] Assembly LethalConfig introuvable malgré le plugin. Pas de sliders.");
                    return;
                }

                // 3) Récupérer les types nécessaires
                var managerType = lethalConfigAsm.GetType("LethalConfig.LethalConfigManager");
                var boolItemType = lethalConfigAsm.GetType("LethalConfig.ConfigItems.BoolCheckBoxConfigItem");
                var floatSliderType = lethalConfigAsm.GetType("LethalConfig.ConfigItems.FloatSliderConfigItem");
                var intSliderType = lethalConfigAsm.GetType("LethalConfig.ConfigItems.IntSliderConfigItem");
                var floatOptionsType = lethalConfigAsm.GetType("LethalConfig.ConfigItems.Options.FloatSliderOptions");
                var intOptionsType = lethalConfigAsm.GetType("LethalConfig.ConfigItems.Options.IntSliderOptions");

                if (managerType == null || boolItemType == null ||
                    floatSliderType == null || intSliderType == null ||
                    floatOptionsType == null || intOptionsType == null)
                {
                    LogWarning("[Spiner] Types LethalConfig incomplets, pas de sliders.");
                    return;
                }

                // On récupère explicitement la surcharge avec 1 seul paramètre
                var addConfigItemMethod = managerType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                        m.Name == "AddConfigItem" &&
                        m.GetParameters().Length == 1
                    );

                if (addConfigItemMethod == null)
                {
                    LogWarning("[Spiner] Aucune surcharge de LethalConfigManager.AddConfigItem(param unique) trouvée.");
                    return;
                }


                // 4) Créer les options pour les sliders
                object MakeFloatOptions(float min, float max, bool restart)
                {
                    var opt = Activator.CreateInstance(floatOptionsType);
                    floatOptionsType.GetProperty("Min")!.SetValue(opt, min);
                    floatOptionsType.GetProperty("Max")!.SetValue(opt, max);
                    floatOptionsType.GetProperty("RequiresRestart")!.SetValue(opt, restart);
                    return opt!;
                }

                object MakeIntOptions(int min, int max, bool restart)
                {
                    var opt = Activator.CreateInstance(intOptionsType);
                    intOptionsType.GetProperty("Min")!.SetValue(opt, min);
                    intOptionsType.GetProperty("Max")!.SetValue(opt, max);
                    intOptionsType.GetProperty("RequiresRestart")!.SetValue(opt, restart);
                    return opt!;
                }

                // 5) Créer les items et les enregistrer

                // DarkMode checkbox
                var darkModeItem = Activator.CreateInstance(
                    boolItemType,
                    new object[] { DarkMode, true }
                );
                addConfigItemMethod.Invoke(null, new object[] { darkModeItem! });

                // DarkReviveDelay slider (1 - 30)
                var reviveOptions = MakeFloatOptions(1f, 30f, false);
                var reviveItem = Activator.CreateInstance(
                    floatSliderType,
                    new object[] { DarkReviveDelay, reviveOptions }
                );
                addConfigItemMethod.Invoke(null, new object[] { reviveItem! });

                // DarkKillTime slider (1 - 60)
                var killOptions = MakeFloatOptions(1f, 60f, false);
                var killItem = Activator.CreateInstance(
                    floatSliderType,
                    new object[] { DarkKillTime, killOptions }
                );
                addConfigItemMethod.Invoke(null, new object[] { killItem! });

                // MaxHP slider (1 - 20)
                var hpOptions = MakeIntOptions(1, 20, false);
                var hpItem = Activator.CreateInstance(
                    intSliderType,
                    new object[] { MaxHP, hpOptions }
                );
                addConfigItemMethod.Invoke(null, new object[] { hpItem! });

                // RoamVolume slider (0 - 1)
                var volumeOptions = MakeFloatOptions(0f, 1f, false);
                var volumeItem = Activator.CreateInstance(
                    floatSliderType,
                    new object[] { RoamVolume, volumeOptions }
                );
                addConfigItemMethod.Invoke(null, new object[] { volumeItem! });

                // SoundStyle checkbox (false = normal, true = quack)
                var soundStyleItem = Activator.CreateInstance(
                    boolItemType,
                    new object[] { SoundStyle, true }
                );
                addConfigItemMethod.Invoke(null, new object[] { soundStyleItem! });

                LogInfo("[Spiner] Sliders LethalConfig enregistrés via reflection.");
            }
            catch (Exception ex)
            {
                LogError($"[Spiner] Erreur lors de l'initialisation LethalConfig (reflection) : {ex}");
            }
        }



    }
}
