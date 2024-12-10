using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Companions.Managers;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;
using static Companions.Managers.CompanionManager;

namespace Companions
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class CompanionsPlugin : BaseUnityPlugin
    {
        internal const string ModName = "Companions";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "RustyMods";
        private const string ModGUID = Author + "." + ModName;
        private static readonly string ConfigFileName = ModGUID + ".cfg";
        private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource CompanionsLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle { On = 1, Off = 0 }
        
        private static ConfigEntry<Toggle> _serverConfigLocked = null!;

        private void InitConfigs()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
        }

        public void Awake()
        {
            InitConfigs();
            
            Root = new GameObject("root");
            DontDestroyOnLoad(Root);
            Root.SetActive(false);
            
            Customs.Read();

            Companion Piggy = new Companion("Boar_piggy");
            Piggy.PetEffects.Add("fx_boar_pet");
            Piggy.Item.Set("TrophyBoar", "CompanionPiggyItem");
            Piggy.Item.SetDisplayName("Piggy Companion");
            Piggy.Item.RequiredItems.Add("TrophyBoar", 10); 
            Piggy.SpawnEffects.Add("vfx_spawn");
            Piggy.SpawnEffects.Add("sfx_spawn");
            Piggy.Item.EquipStatusEffect.config.CarryWeight.Value = 100f;
            Companion Chicken = new Companion("Chicken");
            Chicken.PetEffects.Add("vfx_boar_love");
            Chicken.Item.Set("ChickenEgg", "CompanionChickenItem");
            Chicken.Item.SetDisplayName("Chicken Companion");
            Chicken.Item.RequiredItems.Add("ChickenEgg", 10);
            Chicken.SpawnEffects.Add("vfx_spawn");
            Chicken.SpawnEffects.Add("sfx_spawn");
            Chicken.Item.EquipStatusEffect.config.StaminaRegen.Value = 1.05f;
            Companion Hatchling = new Companion("Hatchling");
            Hatchling.PetEffects.Add("vfx_boar_love");
            Hatchling.Item.Set("TrophyHatchling", "CompanionHatchlingItem");
            Hatchling.Item.SetDisplayName("Drake Companion");
            Hatchling.Item.RequiredItems.Add("TrophyHatchling", 10);
            Hatchling.Scale = 0.5f;
            Hatchling.SpawnEffects.Add("vfx_spawn");
            Hatchling.SpawnEffects.Add("sfx_spawn");
            Hatchling.IsFlying = true;
            Hatchling.Item.EquipStatusEffect.config.MaxFallSpeed.Value = 10f;
            Companion LoxCalf = new Companion("Lox_Calf");
            LoxCalf.PetEffects.Add("fx_lox_pet");
            LoxCalf.Item.Set("TrophyLox", "CompanionLoxItem");
            LoxCalf.Item.SetDisplayName("Lox Companion");
            LoxCalf.Item.RequiredItems.Add("TrophyLox", 10);
            LoxCalf.SpawnEffects.Add("vfx_spawn");
            LoxCalf.SpawnEffects.Add("sfx_spawn");
            LoxCalf.Item.EquipStatusEffect.config.CarryWeight.Value = 300f;
            Companion Greyling = new Companion("Greyling");
            Greyling.PetEffects.Add("vfx_boar_love");
            Greyling.Item.Set("TrophyGreydwarf", "CompanionGreylingItem");
            Greyling.Item.SetDisplayName("Greyling Companion");
            Greyling.Item.RequiredItems.Add("TrophyGreydwarf", 5);
            Greyling.Item.RequiredItems.Add("TrophyGreydwarfShaman", 3);
            Greyling.Item.RequiredItems.Add("TrophyGreydwarfBrute", 2);
            Greyling.SpawnEffects.Add("vfx_spawn");
            Greyling.SpawnEffects.Add("sfx_spawn");
            Greyling.Item.EquipStatusEffect.config.HealthRegen.Value = 1.05f;
            Companion Neck = new Companion("Neck");
            Neck.PetEffects.Add("vfx_boar_love");
            Neck.Item.Set("TrophyNeck", "CompanionNeckItem");
            Neck.Item.SetDisplayName("Neck Companion");
            Neck.Item.RequiredItems.Add("TrophyNeck", 10);
            Neck.SpawnEffects.Add("vfx_spawn");
            Neck.SpawnEffects.Add("sfx_spawn");
            Neck.Item.EquipStatusEffect.config.Jump.Value = 1f;
            Companion WolfCub = new Companion("Wolf_cub");
            WolfCub.PetEffects.Add("vfx_boar_love");
            WolfCub.Item.Set("TrophyWolf", "CompanionWolfCubItem");
            WolfCub.Item.SetDisplayName("Wolf Cub Companion");
            WolfCub.Item.RequiredItems.Add("TrophyWolf", 10);
            WolfCub.SpawnEffects.Add("vfx_spawn");
            WolfCub.SpawnEffects.Add("sfx_spawn");
            WolfCub.Item.EquipStatusEffect.config.StaminaRegen.Value = 1.05f;
            Companion Blob = new Companion("Blob");
            Blob.PetEffects.Add("vfx_boar_love");
            Blob.Item.Set("TrophyBlob", "CompanionBlobItem");
            Blob.Item.SetDisplayName("Blob Companion");
            Blob.Item.RequiredItems.Add("TrophyBlob", 10);
            Blob.SpawnEffects.Add("vfx_spawn");
            Blob.SpawnEffects.Add("sfx_spawn");
            Blob.Item.EquipStatusEffect.config.Jump.Value = 0.5f;
            Companion Gjall = new Companion("Gjall");
            Gjall.PetEffects.Add("vfx_boar_love");
            Gjall.Item.Set("TrophyGjall", "CompanionGjallItem");
            Gjall.Item.SetDisplayName("Gjall Companion");
            Gjall.Item.RequiredItems.Add("TrophyGjall", 10);
            Gjall.SpawnEffects.Add("vfx_spawn");
            Gjall.SpawnEffects.Add("sfx_spawn");
            Gjall.Item.EquipStatusEffect.config.EitrRegen.Value = 1.1f;
            Gjall.Scale = 0.25f;
            Companion Hare = new Companion("Hare");
            Hare.PetEffects.Add("vfx_boar_love");
            Hare.Item.Set("TrophyHare", "CompanionHareItem");
            Hare.Item.SetDisplayName("Hare Companion");
            Hare.Item.RequiredItems.Add("TrophyHare", 10);
            Hare.SpawnEffects.Add("vfx_spawn");
            Hare.SpawnEffects.Add("sfx_spawn");
            Hare.Item.EquipStatusEffect.config.Speed.Value = 0.1f;
            
            
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy() => Config.Save();
        
        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                CompanionsLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                CompanionsLogger.LogError($"There was an issue loading your {ConfigFileName}");
                CompanionsLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions
        
        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order;
            [UsedImplicitly] public bool? Browsable;
            [UsedImplicitly] public string? Category;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
        }

        #endregion
    }

    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) &&
                   shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) &&
                   shortcut.Modifiers.All(Input.GetKey);
        }
    }
}