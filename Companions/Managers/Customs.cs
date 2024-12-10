using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using YamlDotNet.Serialization;

namespace Companions.Managers;

public static class Customs
{
    private static readonly string FileName = "RustyMods.Companions.Customs.yml";
    private static readonly string FilePath = Paths.ConfigPath + Path.DirectorySeparatorChar + FileName;

    public static void Read()
    {
        if (!File.Exists(FilePath))
        {
            var serializer = new SerializerBuilder().Build();
            File.WriteAllText(FilePath, serializer.Serialize(new List<Data>()
            {
                new Data(), new Data()
            }));
            return;
        }
        var deserializer = new DeserializerBuilder().Build();
        try
        {
            var data = deserializer.Deserialize<List<Data>>(File.ReadAllText(FilePath));
            foreach (var custom in data)
            {
                if (custom.Prefab.IsNullOrWhiteSpace() || custom.CloneItem.IsNullOrWhiteSpace()) continue;
                CompanionManager.Companion companion = new CompanionManager.Companion(custom.Prefab);
                companion.PetEffects.Add("vfx_boar_love");
                companion.Item.Set(custom.CloneItem, custom.NewItemName);
                companion.Item.SetDisplayName(custom.ItemDisplayName);
                companion.SpawnEffects.Add("vfx_spawn");
                companion.SpawnEffects.Add("sfx_spawn");
            }
        }
        catch
        {
            CompanionsPlugin.CompanionsLogger.LogWarning("Failed to parse: " + FileName);
        }
    }

    [Serializable]
    public class Data
    {
        public string Prefab = "";
        public string CloneItem = "";
        public string NewItemName = "";
        public string ItemDisplayName = "";
        public float Scale = 1f;
    }
    
}