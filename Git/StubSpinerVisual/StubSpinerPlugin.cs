using System.Reflection;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using System;
using LethalLib;
using LethalLib.Extras;
using LethalLib.Modules;

using static LethalLib.Modules.Levels;

namespace Spiner
{
    [BepInPlugin("Jeez.Spiner", "Spiner Mod", "1.0.0")]
    [BepInDependency("evaisa.lethallib")]
    public class SpinerPlugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = null!;
        public static AssetBundle? Assets;
        public static EnemyType? EnemyType;
        public static GameObject? enemyPrefab;

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
            InitializeNetworkBehaviours();

            LoadAssets();

            if (enemyPrefab == null || EnemyType == null)
            {
                LogError("[Spiner] Prefab or EnemyType is null. Registration aborted.");
                return;
            }

            LogInfo("[Spiner] Registering enemy prefab in the network...");
            NetworkPrefabs.RegisterNetworkPrefab(enemyPrefab);
            LogInfo("[Spiner] Enemy prefab registered successfully.");

            LogInfo("[Spiner] Registering enemy in the spawn system...");
            Enemies.RegisterEnemy(
                EnemyType,
                500,
                LevelTypes.All,
                Enemies.SpawnType.Default,
                (TerminalNode)null,
                null
                );

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
    }
}
