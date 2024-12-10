using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using Companions.Behaviors;
using HarmonyLib;
using ItemManager;
using UnityEngine;
using static ItemManager.Item;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Companions.Managers;

public static class CompanionManager
{
    public static GameObject Root = null!;
    private static readonly List<Companion> Companions = new();
    private static readonly Dictionary<string, Companion> CompanionItemMap = new();
    private static readonly List<Item> CompanionItems = new();

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.CreateItemTooltip))]
    private static class InventoryGrid_CreateItemTooltip_Patch
    {
	    private static void Prefix(ItemDrop.ItemData item) => SE_Pet.m_tempCompanionItem = item;
    }

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenTexts))]
    private static class InventoryGUI_OnOpenTexts_Patch
    {
	    private static void Prefix() => SE_Pet.m_tempCompanionItem = SE_Pet.m_currentCompanionItem;
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    private static class Humanoid_EquipItem_Patch
    {
	    private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
	    {
		    if (__instance is not Player player || !__result || !CompanionItemMap.TryGetValue(item.m_shared.m_name, out Companion companion)) return;
		    DespawnCompanions(player);
		    GameObject prefab = Object.Instantiate(companion.ClonedPrefab, FindSpawnPoint(player.transform.position, companion.SpawnRange, companion.IsFlying), Quaternion.identity);
		    companion.m_spawnEffects.Create(prefab.transform.position, prefab.transform.rotation);
		    if (prefab.TryGetComponent(out Familiar familiar))
		    {
			    familiar.m_familiarAI.m_owner = player;
			    familiar.m_familiarAI.m_timeSinceMovedToPlayer = 11f;
			    familiar.m_itemData = item;
			    if (item.m_customData.TryGetValue("CompanionName", out string tameName)) familiar.m_pet.SetText(tameName);
			    familiar.SetLevel(item.m_quality);
			    SE_Pet.m_currentCompanionItem = item;
			    Companion.CompanionItem.UpdateSE();
		    }
	    }

	    private static Vector3 FindSpawnPoint(Vector3 point, float distance, bool isFlying)
	    {
		    if (point.y > 3000f) return point;
		    Vector2 range = Random.insideUnitCircle * distance;
		    Vector3 spawnPoint = point + new Vector3(range.x, 0.0f, range.y);
		    if (ZoneSystem.instance.GetSolidHeight(spawnPoint, out float height))
		    {
			    spawnPoint.y = height;
		    }
		    if (isFlying) spawnPoint.y += 10f;
		    return spawnPoint;
	    }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    private static class Humanoid_UnequipItem_Patch
    {
	    private static void Postfix(Humanoid __instance, ItemDrop.ItemData? item)
	    {
		    if (__instance is not Player player || item == null || !CompanionItemMap.ContainsKey(item.m_shared.m_name)) return;
		    DespawnCompanions(player);
	    }
    }

    private static void DespawnCompanions(Player player)
    {
	    foreach (var instance in Familiar.m_instances)
	    {
		    if (instance.m_familiarAI.m_owner != player) continue;
		    instance.m_familiarAI.m_owner = null;
	    }
    }
    
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static class ObjectDB_Awake_Patch
    {
        private static void Postfix()
        {
            if (!ZNetScene.instance || !ObjectDB.instance) return;
            foreach(var companion in Companions) companion.Init();
            InitCompanionItems();
        }
	    private static void InitCompanionItems()
		{
			Assembly? bepinexConfigManager = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ConfigurationManager");

			Type? configManagerType = bepinexConfigManager?.GetType("ConfigurationManager.ConfigurationManager");
			if (DefaultConfigurability != Configurability.Disabled)
			{
				bool SaveOnConfigSet = plugin.Config.SaveOnConfigSet;
				plugin.Config.SaveOnConfigSet = false;

				foreach (Item item in CompanionItems.Where(i => i.configurability != Configurability.Disabled))
				{
					string nameKey = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
					string englishName = new Regex(@"[=\n\t\\""\'\[\]]*").Replace(english.Localize(nameKey), "").Trim();
					string localizedName = Localization.instance.Localize(nameKey).Trim();

					int order = 0;

					if ((item.configurability & Configurability.Recipe) != 0)
					{ 
						itemCraftConfigs[item] = new Dictionary<string, ItemConfig>();
						foreach (string configKey in item.Recipes.Keys.DefaultIfEmpty(""))
						{
							string configSuffix = configKey == "" ? "" : $" ({configKey})";

							if (item.Recipes.ContainsKey(configKey) && item.Recipes[configKey].Crafting.Stations.Count > 0)
							{
								ItemConfig cfg = itemCraftConfigs[item][configKey] = new ItemConfig();

								List<ConfigurationManagerAttributes> hideWhenNoneAttributes = new();

								cfg.table = config(englishName, "Crafting Station" + configSuffix, item.Recipes[configKey].Crafting.Stations.First().Table, new ConfigDescription($"Crafting station where {englishName} is available.", null, new ConfigurationManagerAttributes { Order = --order, Browsable = (item.configurationVisible & Configurability.Recipe) != 0, Category = localizedName }));
								bool CustomTableBrowsability() => cfg.table.Value == CraftingTable.Custom;
								ConfigurationManagerAttributes customTableAttributes = new() { Order = --order, browsability = CustomTableBrowsability, Browsable = CustomTableBrowsability() && (item.configurationVisible & Configurability.Recipe) != 0, Category = localizedName };
								cfg.customTable = config(englishName, "Custom Crafting Station" + configSuffix, item.Recipes[configKey].Crafting.Stations.First().custom ?? "", new ConfigDescription("", null, customTableAttributes));

								void TableConfigChanged(object o, EventArgs e)
								{
									item.UpdateItemTableConfig(configKey, cfg.table.Value, cfg.customTable.Value);
									customTableAttributes.Browsable = cfg.table.Value == CraftingTable.Custom;
									foreach (ConfigurationManagerAttributes attributes in hideWhenNoneAttributes)
									{
										attributes.Browsable = cfg.table.Value != CraftingTable.Disabled;
									}
									reloadConfigDisplay();
								}
								cfg.table.SettingChanged += TableConfigChanged;
								cfg.customTable.SettingChanged += TableConfigChanged;

								bool TableLevelBrowsability() => cfg.table.Value != CraftingTable.Disabled;
								ConfigurationManagerAttributes tableLevelAttributes = new() { Order = --order, browsability = TableLevelBrowsability, Browsable = TableLevelBrowsability() && (item.configurationVisible & Configurability.Recipe) != 0, Category = localizedName };
								hideWhenNoneAttributes.Add(tableLevelAttributes);
								cfg.tableLevel = config(englishName, "Crafting Station Level" + configSuffix, item.Recipes[configKey].Crafting.Stations.First().level, new ConfigDescription($"Required crafting station level to craft {englishName}.", null, tableLevelAttributes));
								cfg.tableLevel.SettingChanged += (_, _) =>
								{
									if (activeRecipes.ContainsKey(item) && activeRecipes[item].TryGetValue(configKey, out List<Recipe> recipes))
									{
										recipes.First().m_minStationLevel = cfg.tableLevel.Value;
									}
								};
								if (item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality > 1)
								{
									cfg.maximumTableLevel = config(englishName, "Maximum Crafting Station Level" + configSuffix, item.MaximumRequiredStationLevel == int.MaxValue ? item.Recipes[configKey].Crafting.Stations.First().level + item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality - 1 : item.MaximumRequiredStationLevel, new ConfigDescription($"Maximum crafting station level to upgrade and repair {englishName}.", null, tableLevelAttributes));
								}

								bool QualityResultBrowsability() => cfg.requireOneIngredient.Value == Toggle.On;
								cfg.requireOneIngredient = config(englishName, "Require only one resource" + configSuffix, item.Recipes[configKey].RequireOnlyOneIngredient ? Toggle.On : Toggle.Off, new ConfigDescription($"Whether only one of the ingredients is needed to craft {englishName}", null, new ConfigurationManagerAttributes { Order = --order, Category = localizedName }));
								ConfigurationManagerAttributes qualityResultAttributes = new() { Order = --order, browsability = QualityResultBrowsability, Browsable = QualityResultBrowsability() && (item.configurationVisible & Configurability.Recipe) != 0, Category = localizedName };
								cfg.requireOneIngredient.SettingChanged += (_, _) =>
								{
									if (activeRecipes.ContainsKey(item) && activeRecipes[item].TryGetValue(configKey, out List<Recipe> recipes))
									{
										foreach (Recipe recipe in recipes)
										{
											recipe.m_requireOnlyOneIngredient = cfg.requireOneIngredient.Value == Toggle.On;
										}
									}
									qualityResultAttributes.Browsable = QualityResultBrowsability();
									reloadConfigDisplay();
								};
								cfg.qualityResultAmountMultiplier = config(englishName, "Quality Multiplier" + configSuffix, item.Recipes[configKey].QualityResultAmountMultiplier, new ConfigDescription($"Multiplies the crafted amount based on the quality of the resources when crafting {englishName}. Only works, if Require Only One Resource is true.", null, qualityResultAttributes));
								cfg.qualityResultAmountMultiplier.SettingChanged += (_, _) =>
								{
									if (activeRecipes.ContainsKey(item) && activeRecipes[item].TryGetValue(configKey, out List<Recipe> recipes))
									{
										foreach (Recipe recipe in recipes)
										{
											recipe.m_qualityResultAmountMultiplier = cfg.qualityResultAmountMultiplier.Value;
										}
									}
								};

								ConfigEntry<string> itemConfig(string name, string value, string desc, bool isUpgrade)
								{
									bool ItemBrowsability() => cfg.table.Value != CraftingTable.Disabled;
									ConfigurationManagerAttributes attributes = new() { CustomDrawer = drawRequirementsConfigTable(item, isUpgrade), Order = --order, browsability = ItemBrowsability, Browsable = ItemBrowsability() && (item.configurationVisible & Configurability.Recipe) != 0, Category = localizedName };
									hideWhenNoneAttributes.Add(attributes);
									return config(englishName, name, value, new ConfigDescription(desc, null, attributes));
								}

								if ((!item.Recipes[configKey].RequiredItems.Free || item.Recipes[configKey].RequiredItems.Requirements.Count > 0) && item.Recipes[configKey].RequiredItems.Requirements.All(r => r.amountConfig is null))
								{
									cfg.craft = itemConfig("Crafting Costs" + configSuffix, new SerializedRequirements(item.Recipes[configKey].RequiredItems.Requirements).ToString(), $"Item costs to craft {englishName}", false);
								}
								if (item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality > 1 && (!item.Recipes[configKey].RequiredUpgradeItems.Free || item.Recipes[configKey].RequiredUpgradeItems.Requirements.Count > 0) && item.Recipes[configKey].RequiredUpgradeItems.Requirements.All(r => r.amountConfig is null))
								{
									cfg.upgrade = itemConfig("Upgrading Costs" + configSuffix, new SerializedRequirements(item.Recipes[configKey].RequiredUpgradeItems.Requirements).ToString(), $"Item costs per level to upgrade {englishName}", true);
								}

								void ConfigChanged(object o, EventArgs e)
								{
									item.UpdateCraftConfig(configKey, new SerializedRequirements(cfg.craft?.Value ?? ""), new SerializedRequirements(cfg.upgrade?.Value ?? ""));
								}

								if (cfg.craft != null)
								{
									cfg.craft.SettingChanged += ConfigChanged;
								}
								if (cfg.upgrade != null)
								{
									cfg.upgrade.SettingChanged += ConfigChanged;
								}
							}
						}

						if ((item.configurability & Configurability.Drop) != 0)
						{
							ConfigEntry<string> dropConfig = itemDropConfigs[item] = config(englishName, "Drops from", new SerializedDrop(item.DropsFrom.Drops).ToString(), new ConfigDescription($"{englishName} drops from this creature.", null, new ConfigurationManagerAttributes { CustomDrawer = drawDropsConfigTable, Category = localizedName, Browsable = (item.configurationVisible & Configurability.Drop) != 0 }));
							dropConfig.SettingChanged += (_, _) => item.UpdateCharacterDrop();
						}

						for (int i = 0; i < item.Conversions.Count; ++i)
						{
							string prefix = item.Conversions.Count > 1 ? $"{i + 1}. " : "";
							Conversion conversion = item.Conversions[i];
							conversion.config = new Conversion.ConversionConfig();
							int index = i;

							void UpdatePiece()
							{
								if (index >= item.conversions.Count || !ZNetScene.instance)
								{
									return;
								}
								string? newPieceName = conversion.config.piece.Value is not ConversionPiece.Disabled ? conversion.config.piece.Value == ConversionPiece.Custom ? conversion.config.customPiece.Value : getInternalName(conversion.config.piece.Value) : null;
								string? activePiece = conversion.config.activePiece;
								if (conversion.config.activePiece is not null)
								{
									Smelter smelter = ZNetScene.instance.GetPrefab(conversion.config.activePiece).GetComponent<Smelter>();
									int removeIndex = smelter.m_conversion.IndexOf(item.conversions[index]);
									if (removeIndex >= 0)
									{
										foreach (Smelter instantiatedSmelter in Resources.FindObjectsOfTypeAll<Smelter>())
										{
											if (Utils.GetPrefabName(instantiatedSmelter.gameObject) == activePiece)
											{
												instantiatedSmelter.m_conversion.RemoveAt(removeIndex);
											}
										}
									}
									conversion.config.activePiece = null;
								}
								if (item.conversions[index].m_from is not null && conversion.config.piece.Value is not ConversionPiece.Disabled)
								{
									if (ZNetScene.instance.GetPrefab(newPieceName)?.GetComponent<Smelter>() is not null)
									{
										conversion.config.activePiece = newPieceName;
										foreach (Smelter instantiatedSmelter in Resources.FindObjectsOfTypeAll<Smelter>())
										{
											if (Utils.GetPrefabName(instantiatedSmelter.gameObject) == newPieceName)
											{
												instantiatedSmelter.m_conversion.Add(item.conversions[index]);
											}
										}
									}
								}
							}

							conversion.config.input = config(englishName, $"{prefix}Conversion Input Item", conversion.Input, new ConfigDescription($"Input item to create {englishName}", null, new ConfigurationManagerAttributes { Category = localizedName, Browsable = (item.configurationVisible & Configurability.Recipe) != 0 }));
							conversion.config.input.SettingChanged += (_, _) =>
							{
								if (index < item.conversions.Count && ObjectDB.instance is { } objectDB)
								{
									ItemDrop? inputItem = SerializedRequirements.fetchByName(objectDB, conversion.config.input.Value);
									item.conversions[index].m_from = inputItem;
									UpdatePiece();
								}
							};
							conversion.config.piece = config(englishName, $"{prefix}Conversion Piece", conversion.Piece, new ConfigDescription($"Conversion piece used to create {englishName}", null, new ConfigurationManagerAttributes { Category = localizedName, Browsable = (item.configurationVisible & Configurability.Recipe) != 0 }));
							conversion.config.piece.SettingChanged += (_, _) => UpdatePiece();
							conversion.config.customPiece = config(englishName, $"{prefix}Conversion Custom Piece", conversion.customPiece ?? "", new ConfigDescription($"Custom conversion piece to create {englishName}", null, new ConfigurationManagerAttributes { Category = localizedName, Browsable = (item.configurationVisible & Configurability.Recipe) != 0 }));
							conversion.config.customPiece.SettingChanged += (_, _) => UpdatePiece();
						}
					}
					
					for (int i = 0; i < item.CookingConversions.Count; ++i)
	                {
	                    string prefix = item.CookingConversions.Count > 1 ? $"{i + 1}" : "";
	                    CookingConversion conversion = item.CookingConversions[i];
	                    conversion.config = new CookingConversion.ConversionConfig();
	                    int index = i;

	                    void UpdatePiece()
	                    {
	                        if (index >= item.cookingConversions.Count || !ZNetScene.instance) return;
	                        string? newPieceName = conversion.config.piece.Value is not CookingPiece.Disabled
	                            ? conversion.config.piece.Value == CookingPiece.Custom
	                                ? conversion.config.customPiece.Value
	                                : getInternalName(conversion.config.piece.Value)
	                            : null;
	                        string? activePiece = conversion.config.activePiece;
	                        if (conversion.config.activePiece is not null)
	                        {
	                            CookingStation station = ZNetScene.instance.GetPrefab(conversion.config.activePiece)
	                                .GetComponent<CookingStation>();
	                            int removeIndex = station.m_conversion.IndexOf(item.cookingConversions[index]);
	                            if (removeIndex >= 0)
	                            {
	                                foreach (CookingStation instantiatedStation in Resources
	                                             .FindObjectsOfTypeAll<CookingStation>())
	                                {
	                                    if (Utils.GetPrefabName(instantiatedStation.gameObject) == activePiece)
	                                    {
	                                        instantiatedStation.m_conversion.RemoveAt(removeIndex);
	                                    }
	                                }
	                            }

	                            conversion.config.activePiece = null;
	                        }

	                        if (item.cookingConversions[index].m_from is not null &&
	                            conversion.config.piece.Value is not CookingPiece.Disabled)
	                        {
	                            if (ZNetScene.instance.GetPrefab(newPieceName)
	                                    ?.GetComponent<CookingStation>() is not null)
	                            {
	                                conversion.config.activePiece = newPieceName;
	                                foreach (CookingStation instantiatedStation in Resources
	                                             .FindObjectsOfTypeAll<CookingStation>())
	                                {
	                                    if (Utils.GetPrefabName(instantiatedStation.gameObject) == newPieceName)
	                                    {
	                                        instantiatedStation.m_conversion.Add(item.cookingConversions[index]);
	                                    }
	                                }
	                            }
	                        }
	                    }

	                    conversion.config.cookTime = config(englishName, $"{prefix}Cook Time", conversion.CookTime,
	                        new ConfigDescription($"{prefix}Cook Time", null, new ConfigurationManagerAttributes()
	                        {
	                            Category = localizedName,
	                            Browsable = (item.configurationVisible & Configurability.Recipe) != 0
	                        }));
	                    conversion.config.cookTime.SettingChanged += (_, _) =>
	                    {
	                        item.cookingConversions[index].m_cookTime = conversion.config.cookTime.Value;
	                        UpdatePiece();
	                    };

	                    conversion.config.input = config(englishName, $"{prefix}Conversion Input Item",
	                        conversion.Input, new ConfigDescription($"Input item to create {englishName}", null,
	                            new ConfigurationManagerAttributes()
	                            {
	                                Category = localizedName,
	                                Browsable = (item.configurationVisible & Configurability.Recipe) != 0
	                            }));
	                    conversion.config.input.SettingChanged += (_, _) =>
	                    {
	                        if (index < item.cookingConversions.Count && ObjectDB.instance is { } objectDB)
	                        {
	                            ItemDrop? inputItem =
	                                SerializedRequirements.fetchByName(objectDB, conversion.config.input.Value);
	                            item.cookingConversions[index].m_from = inputItem;
	                            UpdatePiece();
	                        }
	                    };
	                    conversion.config.piece = config(englishName, $"{prefix}Conversion Piece", conversion.Piece,
	                        new ConfigDescription($"Conversion piece used to create {englishName}", null,
	                            new ConfigurationManagerAttributes()
	                            {
	                                Category = localizedName,
	                                Browsable = (item.configurationVisible & Configurability.Recipe) != 0
	                            }));
	                    conversion.config.piece.SettingChanged += (_, _) => UpdatePiece();
	                    conversion.config.customPiece = config(englishName, $"{prefix}Conversion Custom Piece",
	                        conversion.customPiece ?? "", new ConfigDescription(
	                            $"Custom conversion piece to create {englishName}", null,
	                            new ConfigurationManagerAttributes()
	                            {
	                                Category = localizedName,
	                                Browsable = (item.configurationVisible & Configurability.Recipe) != 0
	                            }));
	                    conversion.config.customPiece.SettingChanged += (_, _) => UpdatePiece();
	                }

					if ((item.configurability & Configurability.Stats) != 0)
					{
						item.statsConfigs.Clear();
						void statcfg<T>(string configName, string description, Func<ItemDrop.ItemData.SharedData, T> readDefault, Action<ItemDrop.ItemData.SharedData, T> setValue)
						{
							ItemDrop.ItemData.SharedData shared = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
							ConfigEntry<T> cfg = config(englishName, configName, readDefault(shared), new ConfigDescription(description, null, new ConfigurationManagerAttributes { Category = localizedName, Browsable = (item.configurationVisible & Configurability.Stats) != 0 }));
							if ((item.configurationVisible & Configurability.Stats) != 0)
							{
								setValue(shared, cfg.Value);
							}

							void ApplyConfig() => item.ApplyToAllInstances(item => setValue(item.m_shared, cfg.Value));

							item.statsConfigs.Add(cfg, ApplyConfig);

							cfg.SettingChanged += (_, _) =>
							{
								if ((item.configurationVisible & Configurability.Stats) != 0)
								{
									ApplyConfig();
								}
							};
						}

						ItemDrop.ItemData.SharedData shared = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
						ItemDrop.ItemData.ItemType itemType = shared.m_itemType;

						statcfg("Weight", $"Weight of {englishName}.", shared => shared.m_weight, (shared, value) => shared.m_weight = value);
						statcfg("Trader Value", $"Trader value of {englishName}.", shared => shared.m_value, (shared, value) => shared.m_value = value);

						if (itemType is ItemDrop.ItemData.ItemType.Bow or ItemDrop.ItemData.ItemType.Chest or ItemDrop.ItemData.ItemType.Hands or ItemDrop.ItemData.ItemType.Helmet or ItemDrop.ItemData.ItemType.Legs or ItemDrop.ItemData.ItemType.Shield or ItemDrop.ItemData.ItemType.Shoulder or ItemDrop.ItemData.ItemType.Tool or ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
						{
							statcfg("Durability", $"Durability of {englishName}.", shared => shared.m_maxDurability, (shared, value) => shared.m_maxDurability = value);
							statcfg("Durability per Level", $"Durability gain per level of {englishName}.", shared => shared.m_durabilityPerLevel, (shared, value) => shared.m_durabilityPerLevel = value);
							statcfg("Movement Speed Modifier", $"Movement speed modifier of {englishName}.", shared => shared.m_movementModifier, (shared, value) => shared.m_movementModifier = value);
						}

						if (itemType is ItemDrop.ItemData.ItemType.Bow or ItemDrop.ItemData.ItemType.Shield or ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
						{
							statcfg("Block Armor", $"Block armor of {englishName}.", shared => shared.m_blockPower, (shared, value) => shared.m_blockPower = value);
							statcfg("Block Armor per Level", $"Block armor per level for {englishName}.", shared => shared.m_blockPowerPerLevel, (shared, value) => shared.m_blockPowerPerLevel = value);
							statcfg("Block Force", $"Block force of {englishName}.", shared => shared.m_deflectionForce, (shared, value) => shared.m_deflectionForce = value);
							statcfg("Block Force per Level", $"Block force per level for {englishName}.", shared => shared.m_deflectionForcePerLevel, (shared, value) => shared.m_deflectionForcePerLevel = value);
							statcfg("Parry Bonus", $"Parry bonus of {englishName}.", shared => shared.m_timedBlockBonus, (shared, value) => shared.m_timedBlockBonus = value);
						}
						else if (itemType is ItemDrop.ItemData.ItemType.Chest or ItemDrop.ItemData.ItemType.Hands or ItemDrop.ItemData.ItemType.Helmet or ItemDrop.ItemData.ItemType.Legs or ItemDrop.ItemData.ItemType.Shoulder)
						{
							statcfg("Armor", $"Armor of {englishName}.", shared => shared.m_armor, (shared, value) => shared.m_armor = value);
							statcfg("Armor per Level", $"Armor per level for {englishName}.", shared => shared.m_armorPerLevel, (shared, value) => shared.m_armorPerLevel = value);
						}

						if (shared.m_skillType is Skills.SkillType.Axes or Skills.SkillType.Pickaxes)
						{
							statcfg("Tool tier", $"Tool tier of {englishName}.", shared => shared.m_toolTier, (shared, value) => shared.m_toolTier = value);
						}

						if (itemType is ItemDrop.ItemData.ItemType.Shield or ItemDrop.ItemData.ItemType.Chest or ItemDrop.ItemData.ItemType.Hands or ItemDrop.ItemData.ItemType.Helmet or ItemDrop.ItemData.ItemType.Legs or ItemDrop.ItemData.ItemType.Shoulder)
						{
							Dictionary<HitData.DamageType, DamageModifier> modifiers = shared.m_damageModifiers.ToDictionary(d => d.m_type, d => (DamageModifier)(int)d.m_modifier);
							foreach (HitData.DamageType damageType in ((HitData.DamageType[])Enum.GetValues(typeof(HitData.DamageType))).Except(new[] { HitData.DamageType.Chop, HitData.DamageType.Pickaxe, HitData.DamageType.Spirit, HitData.DamageType.Physical, HitData.DamageType.Elemental }))
							{
								statcfg($"{damageType.ToString()} Resistance", $"{damageType.ToString()} resistance of {englishName}.", _ => modifiers.TryGetValue(damageType, out DamageModifier modifier) ? modifier : DamageModifier.None, (shared, value) =>
								{
									HitData.DamageModPair modifier = new() { m_type = damageType, m_modifier = (HitData.DamageModifier)(int)value };
									for (int i = 0; i < shared.m_damageModifiers.Count; ++i)
									{
										if (shared.m_damageModifiers[i].m_type == damageType)
										{
											if (value == DamageModifier.None)
											{
												shared.m_damageModifiers.RemoveAt(i);
											}
											else
											{
												shared.m_damageModifiers[i] = modifier;
											}
											return;
										}
									}
									if (value != DamageModifier.None)
									{
										shared.m_damageModifiers.Add(modifier);
									}
								});
							}
						}

						if (itemType is ItemDrop.ItemData.ItemType.Consumable && shared.m_food > 0)
						{
							statcfg("Health", $"Health value of {englishName}.", shared => shared.m_food, (shared, value) => shared.m_food = value);
							statcfg("Stamina", $"Stamina value of {englishName}.", shared => shared.m_foodStamina, (shared, value) => shared.m_foodStamina = value);
							statcfg("Eitr", $"Eitr value of {englishName}.", shared => shared.m_foodEitr, (shared, value) => shared.m_foodEitr = value);
							statcfg("Duration", $"Duration of {englishName}.", shared => shared.m_foodBurnTime, (shared, value) => shared.m_foodBurnTime = value);
							statcfg("Health Regen", $"Health regen value of {englishName}.", shared => shared.m_foodRegen, (shared, value) => shared.m_foodRegen = value);
						}

						if (shared.m_skillType is Skills.SkillType.BloodMagic)
						{
							statcfg("Health Cost", $"Health cost of {englishName}.", shared => shared.m_attack.m_attackHealth, (shared, value) => shared.m_attack.m_attackHealth = value);
							statcfg("Health Cost Percentage", $"Health cost percentage of {englishName}.", shared => shared.m_attack.m_attackHealthPercentage, (shared, value) => shared.m_attack.m_attackHealthPercentage = value);
						}

						if (shared.m_skillType is Skills.SkillType.BloodMagic or Skills.SkillType.ElementalMagic)
						{
							statcfg("Eitr Cost", $"Eitr cost of {englishName}.", shared => shared.m_attack.m_attackEitr, (shared, value) => shared.m_attack.m_attackEitr = value);
						}

						if (itemType is ItemDrop.ItemData.ItemType.OneHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft or ItemDrop.ItemData.ItemType.Bow)
						{
							statcfg("Knockback", $"Knockback of {englishName}.", shared => shared.m_attackForce, (shared, value) => shared.m_attackForce = value);
							statcfg("Backstab Bonus", $"Backstab bonus of {englishName}.", shared => shared.m_backstabBonus, (shared, value) => shared.m_backstabBonus = value);
							statcfg("Attack Stamina", $"Attack stamina of {englishName}.", shared => shared.m_attack.m_attackStamina, (shared, value) => shared.m_attack.m_attackStamina = value);

							void SetDmg(string dmgType, Func<HitData.DamageTypes, float> readDmg, setDmgFunc setDmg)
							{
								statcfg($"{dmgType} Damage", $"{dmgType} damage dealt by {englishName}.", shared => readDmg(shared.m_damages), (shared, val) => setDmg(ref shared.m_damages, val));
								statcfg($"{dmgType} Damage Per Level", $"{dmgType} damage dealt increase per level for {englishName}.", shared => readDmg(shared.m_damagesPerLevel), (shared, val) => setDmg(ref shared.m_damagesPerLevel, val));
							}

							SetDmg("True", dmg => dmg.m_damage, (ref HitData.DamageTypes dmg, float val) => dmg.m_damage = val);
							SetDmg("Slash", dmg => dmg.m_slash, (ref HitData.DamageTypes dmg, float val) => dmg.m_slash = val);
							SetDmg("Pierce", dmg => dmg.m_pierce, (ref HitData.DamageTypes dmg, float val) => dmg.m_pierce = val);
							SetDmg("Blunt", dmg => dmg.m_blunt, (ref HitData.DamageTypes dmg, float val) => dmg.m_blunt = val);
							SetDmg("Chop", dmg => dmg.m_chop, (ref HitData.DamageTypes dmg, float val) => dmg.m_chop = val);
							SetDmg("Pickaxe", dmg => dmg.m_pickaxe, (ref HitData.DamageTypes dmg, float val) => dmg.m_pickaxe = val);
							SetDmg("Fire", dmg => dmg.m_fire, (ref HitData.DamageTypes dmg, float val) => dmg.m_fire = val);
							SetDmg("Poison", dmg => dmg.m_poison, (ref HitData.DamageTypes dmg, float val) => dmg.m_poison = val);
							SetDmg("Frost", dmg => dmg.m_frost, (ref HitData.DamageTypes dmg, float val) => dmg.m_frost = val);
							SetDmg("Lightning", dmg => dmg.m_lightning, (ref HitData.DamageTypes dmg, float val) => dmg.m_lightning = val);
							SetDmg("Spirit", dmg => dmg.m_spirit, (ref HitData.DamageTypes dmg, float val) => dmg.m_spirit = val);

							if (itemType is ItemDrop.ItemData.ItemType.Bow)
							{
								statcfg("Projectiles", $"Number of projectiles that {englishName} shoots at once.", shared => shared.m_attack.m_projectileBursts, (shared, value) => shared.m_attack.m_projectileBursts = value);
								statcfg("Burst Interval", $"Time between the projectiles {englishName} shoots at once.", shared => shared.m_attack.m_burstInterval, (shared, value) => shared.m_attack.m_burstInterval = value);
								statcfg("Minimum Accuracy", $"Minimum accuracy for {englishName}.", shared => shared.m_attack.m_projectileAccuracyMin, (shared, value) => shared.m_attack.m_projectileAccuracyMin = value);
								statcfg("Accuracy", $"Accuracy for {englishName}.", shared => shared.m_attack.m_projectileAccuracy, (shared, value) => shared.m_attack.m_projectileAccuracy = value);
								statcfg("Minimum Velocity", $"Minimum velocity for {englishName}.", shared => shared.m_attack.m_projectileVelMin, (shared, value) => shared.m_attack.m_projectileVelMin = value);
								statcfg("Velocity", $"Velocity for {englishName}.", shared => shared.m_attack.m_projectileVel, (shared, value) => shared.m_attack.m_projectileVel = value);
								statcfg("Maximum Draw Time", $"Time until {englishName} is fully drawn at skill level 0.", shared => shared.m_attack.m_drawDurationMin, (shared, value) => shared.m_attack.m_drawDurationMin = value);
								statcfg("Stamina Drain", $"Stamina drain per second while drawing {englishName}.", shared => shared.m_attack.m_drawStaminaDrain, (shared, value) => shared.m_attack.m_drawStaminaDrain = value);
							}
						}
					}

					if ((item.configurability & Configurability.Trader) != 0)
					{
						List<ConfigurationManagerAttributes> traderAttributes = new();
						bool TraderBrowsability() => item.traderConfig.trader.Value != 0;

						item.traderConfig = new TraderConfig
						{
							trader = config(englishName, "Trader Selling", item.Trade.Trader, new ConfigDescription($"Which traders sell {englishName}.", null, new ConfigurationManagerAttributes { Order = --order, Browsable = (item.configurationVisible & Configurability.Trader) != 0, Category = localizedName })),
						};
						item.traderConfig.trader.SettingChanged += (_, _) =>
						{
							item.ReloadTraderConfiguration();
							foreach (ConfigurationManagerAttributes attributes in traderAttributes)
							{
								attributes.Browsable = TraderBrowsability();
							}
							reloadConfigDisplay();
						};

						ConfigEntry<T> traderConfig<T>(string name, T value, string desc)
						{
							ConfigurationManagerAttributes attributes = new() { Order = --order, browsability = TraderBrowsability, Browsable = TraderBrowsability() && (item.configurationVisible & Configurability.Trader) != 0, Category = localizedName };
							traderAttributes.Add(attributes);
							ConfigEntry<T> cfg = config(englishName, name, value, new ConfigDescription(desc, null, attributes));
							cfg.SettingChanged += (_, _) => item.ReloadTraderConfiguration();
							return cfg;
						}

						item.traderConfig.price = traderConfig("Trader Price", item.Trade.Price, $"Price of {englishName} at the trader.");
						item.traderConfig.stack = traderConfig("Trader Stack", item.Trade.Stack, $"Stack size of {englishName} in the trader. Also known as the number of items sold by a trader in one transaction.");
						item.traderConfig.requiredGlobalKey = traderConfig("Trader Required Global Key", item.Trade.RequiredGlobalKey ?? "", $"Required global key to unlock {englishName} at the trader.");

						if (item.traderConfig.trader.Value != 0)
						{
							PrefabManager.AddItemToTrader(item.Prefab, item.traderConfig.trader.Value, item.traderConfig.price.Value, item.traderConfig.stack.Value, item.traderConfig.requiredGlobalKey.Value);
						}
					}
					else if (item.Trade.Trader != 0)
					{
						PrefabManager.AddItemToTrader(item.Prefab, item.Trade.Trader, item.Trade.Price, item.Trade.Stack, item.Trade.RequiredGlobalKey);
					}
				}

				if (SaveOnConfigSet)
				{
					plugin.Config.SaveOnConfigSet = true;
					plugin.Config.Save();
				}
			}
			configManager = configManagerType == null ? null : BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(configManagerType);

			foreach (Item item in CompanionItems)
			{
				foreach (KeyValuePair<string, ItemRecipe> kv in item.Recipes)
				{
					foreach (RequiredResourceList resourceList in new[] { kv.Value.RequiredItems, kv.Value.RequiredUpgradeItems })
					{
						for (int i = 0; i < resourceList.Requirements.Count; ++i)
						{
							if ((item.configurability & Configurability.Recipe) != 0 && resourceList.Requirements[i].amountConfig is { } amountCfg)
							{
								int resourceIndex = i;
								void ConfigChanged(object o, EventArgs e)
								{
									if (ObjectDB.instance && activeRecipes.ContainsKey(item) && activeRecipes[item].TryGetValue(kv.Key, out List<Recipe> recipes))
									{
										foreach (Recipe recipe in recipes)
										{
											recipe.m_resources[resourceIndex].m_amount = amountCfg.Value;
										}
									}
								}

								amountCfg.SettingChanged += ConfigChanged;
							}
						}
					}
				}

				item.InitializeNewRegisteredItem();
			}

			foreach (Item item in CompanionItems)
			{
				item.registerRecipesInObjectDB(ObjectDB.instance);
			}

			foreach (Item item in CompanionItems)
			{
				void RegisterStatusEffect(StatusEffect? statusEffect)
				{
					if (statusEffect is not null && !ObjectDB.instance.GetStatusEffect(statusEffect.name.GetStableHashCode()))
					{
						ObjectDB.instance.m_StatusEffects.Add(statusEffect);
					}
				}
				ItemDrop.ItemData.SharedData shared = item.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
				RegisterStatusEffect(shared.m_attackStatusEffect);
				RegisterStatusEffect(shared.m_consumeStatusEffect);
				RegisterStatusEffect(shared.m_equipStatusEffect);
				RegisterStatusEffect(shared.m_setStatusEffect);
			}
		}
    }

    public class Companion
    {
        private GameObject OriginalPrefab = null!;
        public GameObject ClonedPrefab = null!;
        private Familiar m_familiar = null!;
        private FamiliarAI m_familiarAI = null!;
        private Pet m_pet = null!;
        private readonly string originalPrefabName;
        private readonly string newPrefabName;
        public readonly CompanionItem Item = new CompanionItem();
        public float Health = 1f;
        public float RandomCircleInterval = 10f;
        public float MaxDistanceFromPlayer = 5f;
        public float MinDistanceFromPlayer = 1f;
        public float SpawnRange = 50f;
        public bool IsFlying = false;
        public float Scale = 1f;

        private readonly HitData.DamageModifiers DamageModifiers = new HitData.DamageModifiers()
        {
            m_blunt = HitData.DamageModifier.Ignore,
            m_slash = HitData.DamageModifier.Ignore,
            m_pierce = HitData.DamageModifier.Ignore,
            m_chop = HitData.DamageModifier.Ignore,
            m_pickaxe = HitData.DamageModifier.Ignore,
            m_fire = HitData.DamageModifier.Ignore,
            m_frost = HitData.DamageModifier.Ignore,
            m_lightning = HitData.DamageModifier.Ignore,
            m_poison = HitData.DamageModifier.Ignore,
            m_spirit = HitData.DamageModifier.Ignore
        };
        public readonly List<string> PetEffects = new();
        public EffectList m_spawnEffects = new();
        public readonly List<string> SpawnEffects = new();

        public Companion(string originalPrefab)
        {
            originalPrefabName = originalPrefab;
            newPrefabName = "RS_" + originalPrefab + "_Companion";
            Companions.Add(this);
        }

        public class CompanionItem
        {
            public string originalItemName = null!;
            public string newItemName = null!;
            public string DisplayName = "";
            public GameObject ClonedItem = null!;
            public ItemDrop m_itemDrop = null!;
            public readonly RequiredResourceList RequiredItems = new();
            public readonly SE_Pet EquipStatusEffect = ScriptableObject.CreateInstance<SE_Pet>();
            public void Set(string originalItem, string itemName)
            {
                originalItemName = originalItem;
                newItemName = "RS_" + itemName;
            }

            public void SetDisplayName(string displayName)
            {
	            DisplayName = displayName;
	            SetupSE();
            }

            public static void UpdateSE()
            {
	            if (!Player.m_localPlayer) return;
	            foreach (var se in Player.m_localPlayer.GetSEMan().GetStatusEffects())
	            {
		            if (se is SE_Pet sePet) sePet.SetConfigData();
	            }
            }

            private void SetupSE()
            {
	            EquipStatusEffect.name = "SE_" + newItemName;
	            EquipStatusEffect.config.ItemQualityMultiplier = config(DisplayName, "0. Item Quality Multiplier", 0.1f, new ConfigDescription("Set the item quality multiplier", new AcceptableValueRange<float>(0f, 1f)));
	            EquipStatusEffect.config.HealthRegen = config(DisplayName, "1. Health Regen", 1f, new ConfigDescription("Set health regen multiplier", new AcceptableValueRange<float>(1f, 10f)));
	            EquipStatusEffect.config.StaminaRegen = config(DisplayName, "2. Stamina Regen", 1f, new ConfigDescription("Set stamina regen multiplier", new AcceptableValueRange<float>(1f, 10f)));
	            EquipStatusEffect.config.EitrRegen = config(DisplayName, "3. Eitr Regen", 1f, new ConfigDescription("Set eitr regen multiplier", new AcceptableValueRange<float>(1f, 10f)));
	            EquipStatusEffect.config.CarryWeight = config(DisplayName, "4. Carry Weight", 0f, new ConfigDescription("Set carry weight bonus", new AcceptableValueRange<float>(0f, 500f)));
	            EquipStatusEffect.config.Speed = config(DisplayName, "5. Speed", 0f, new ConfigDescription("Set speed multiplier", new AcceptableValueRange<float>(0f, 2f)));
	            EquipStatusEffect.config.Jump = config(DisplayName, "6. Jump", 0f, new ConfigDescription("Set jump modifier", new AcceptableValueRange<float>(0f, 10f)));
	            EquipStatusEffect.config.MaxFallSpeed = config(DisplayName, "7. Max Fall Speed", 0f, new ConfigDescription("Set max fall speed", new AcceptableValueRange<float>(0f, 20f)));
	            void ConfigChanged(object sender, EventArgs args) => UpdateSE();
	            EquipStatusEffect.config.HealthRegen.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.StaminaRegen.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.EitrRegen.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.CarryWeight.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.Speed.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.ItemQualityMultiplier.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.Jump.SettingChanged += ConfigChanged;
	            EquipStatusEffect.config.MaxFallSpeed.SettingChanged += ConfigChanged;
            }
	    }

        public void Init()
        {
            if (ZNetScene.instance.GetPrefab(originalPrefabName) is not { } original) return;
            OriginalPrefab = original;
            ClonedPrefab = Object.Instantiate(original, Root.transform, false);
            ClonedPrefab.name = newPrefabName;
            ClonedPrefab.transform.localScale *= Scale;
            DestroyOldComponents();
            if (!InheritCharacter()) return;
            if (!InheritMonsterAI() && !InheritAnimalAI()) return;
            AddPetComponent();
            if (!CreateItem()) return;
            Helpers.UpdateEffects(SpawnEffects, ref m_spawnEffects);
            Register();
        }

        private bool CreateItem()
        {
            if (ObjectDB.instance.GetItemPrefab(Item.originalItemName) is { } originalItem && originalItem.GetComponent<ItemDrop>())
            {
                Item.ClonedItem = Object.Instantiate(originalItem, Root.transform, false);
                Item.ClonedItem.name = Item.newItemName;
                Item.m_itemDrop = Item.ClonedItem.GetComponent<ItemDrop>();
                ItemDrop.ItemData.SharedData SharedData = Item.m_itemDrop.m_itemData.m_shared;
                SharedData.m_name = $"$item_{Item.newItemName.ToLower()}";
                SharedData.m_description = $"$item_{Item.newItemName.ToLower()}_desc";
                SharedData.m_maxStackSize = 1;
                SharedData.m_useDurability = false;
                SharedData.m_teleportable = true;
                SharedData.m_maxQuality = 4;
                SharedData.m_itemType = ItemDrop.ItemData.ItemType.Utility;
                SharedData.m_attachOverride = ItemDrop.ItemData.ItemType.None;
                Item.EquipStatusEffect.m_icon = Item.m_itemDrop.m_itemData.GetIcon();
                Item.EquipStatusEffect.m_name = SharedData.m_name;
                SharedData.m_equipStatusEffect = Item.EquipStatusEffect;
                CompanionItemMap[Item.m_itemDrop.m_itemData.m_shared.m_name] = this;
                Item item = new Item(Item.ClonedItem);
                item.Name.English(Item.DisplayName);
                item.Description.English($"Summons a loyal {Localization.instance.Localize(m_familiar.m_name)} familiar to follow you, granting bonuses as long as it remains by your side.");
                item.RequiredItems.Requirements = Item.RequiredItems.Requirements;
                item.RequiredUpgradeItems.Requirements = item.RequiredItems.Requirements;
                item.RequiredUpgradeItems.Add("Coins", 100);
                item.Crafting.Add(CraftingTable.ArtisanTable, 1);
                CompanionItems.Add(item);
                return true;
            }

            return false;
        }

        private void DestroyOldComponents()
        {
            if (ClonedPrefab.TryGetComponent(out Humanoid humanoid)) Object.Destroy(humanoid);
            if (ClonedPrefab.TryGetComponent(out Character character)) Object.Destroy(character);
            if (ClonedPrefab.TryGetComponent(out Tameable tameable)) Object.Destroy(tameable);
            if (ClonedPrefab.TryGetComponent(out Growup growup)) Object.Destroy(growup);
            if (ClonedPrefab.TryGetComponent(out MonsterAI monsterAI)) Object.Destroy(monsterAI);
            if (ClonedPrefab.TryGetComponent(out AnimalAI animalAI)) Object.Destroy(animalAI);
            if (ClonedPrefab.TryGetComponent(out CharacterDrop characterDrop)) Object.Destroy(characterDrop);
            if (ClonedPrefab.TryGetComponent(out Procreation procreation)) Object.Destroy(procreation);
        }

        private bool InheritCharacter()
        {
            if (!OriginalPrefab.TryGetComponent(out Character component)) return false;
            m_familiar = ClonedPrefab.AddComponent<Familiar>();
            m_familiar.m_name = component.m_name;
            m_familiar.m_group = "Familiars";
            m_familiar.m_faction = Character.Faction.Boss;
            m_familiar.m_crouchSpeed = component.m_crouchSpeed;
            m_familiar.m_walkSpeed = component.m_walkSpeed;
            m_familiar.m_speed = component.m_speed;
            m_familiar.m_turnSpeed = component.m_turnSpeed;
            m_familiar.m_runSpeed = component.m_runSpeed;
            m_familiar.m_runTurnSpeed = component.m_runTurnSpeed;
            m_familiar.m_flySlowSpeed = component.m_flySlowSpeed;
            m_familiar.m_flyFastSpeed = component.m_flyFastSpeed;
            m_familiar.m_flyTurnSpeed = component.m_flyTurnSpeed;
            m_familiar.m_acceleration = component.m_acceleration;
            m_familiar.m_jumpForce = component.m_jumpForce;
            m_familiar.m_jumpForceForward = component.m_jumpForceForward;
            m_familiar.m_jumpForceTiredFactor = component.m_jumpForceTiredFactor;
            m_familiar.m_airControl = component.m_airControl;
            m_familiar.m_canSwim = component.m_canSwim;
            m_familiar.m_swimDepth = component.m_swimDepth;
            m_familiar.m_swimSpeed = component.m_swimSpeed;
            m_familiar.m_swimTurnSpeed = component.m_swimTurnSpeed;
            m_familiar.m_swimAcceleration = component.m_swimAcceleration;
            m_familiar.m_groundTilt = component.m_groundTilt;
            m_familiar.m_groundTiltSpeed = component.m_groundTiltSpeed;
            m_familiar.m_flying = component.m_flying;
            m_familiar.m_jumpStaminaUsage = component.m_jumpStaminaUsage;
            m_familiar.m_disableWhileSleeping = component.m_disableWhileSleeping;
            m_familiar.m_eye = ClonedPrefab.transform.Find("EyePos");
            m_familiar.m_hitEffects = component.m_hitEffects;
            m_familiar.m_critHitEffects = component.m_critHitEffects;
            m_familiar.m_backstabHitEffects = component.m_backstabHitEffects;
            m_familiar.m_deathEffects = component.m_deathEffects;
            m_familiar.m_waterEffects = component.m_waterEffects;
            m_familiar.m_tarEffects = component.m_tarEffects;
            m_familiar.m_slideEffects = component.m_slideEffects;
            m_familiar.m_jumpEffects = component.m_jumpEffects;
            m_familiar.m_flyingContinuousEffect = component.m_flyingContinuousEffect;
            m_familiar.m_pheromoneLoveEffect = component.m_pheromoneLoveEffect;
            m_familiar.m_tolerateWater = component.m_tolerateWater;
            m_familiar.m_tolerateFire = component.m_tolerateFire;
            m_familiar.m_tolerateSmoke = component.m_tolerateSmoke;
            m_familiar.m_tolerateTar = component.m_tolerateTar;
            m_familiar.m_health = Health;
            m_familiar.m_damageModifiers = DamageModifiers;
            m_familiar.m_staggerWhenBlocked = component.m_staggerWhenBlocked;
            m_familiar.m_staggerDamageFactor = component.m_staggerDamageFactor;
            m_familiar.m_lavaHeatEffects = component.m_lavaHeatEffects;
            return true;
        }

        private bool InheritMonsterAI()
        {
            if (!OriginalPrefab.TryGetComponent(out MonsterAI component)) return false;
            m_familiarAI = ClonedPrefab.AddComponent<FamiliarAI>();
            m_familiarAI.m_viewRange = component.m_viewRange;
            m_familiarAI.m_viewAngle = component.m_viewAngle;
            m_familiarAI.m_hearRange = component.m_hearRange;
            m_familiarAI.m_mistVision = component.m_mistVision;
            m_familiarAI.m_alertedEffects = component.m_alertedEffects;
            m_familiarAI.m_idleSound = component.m_idleSound;
            m_familiarAI.m_idleSoundInterval = component.m_idleSoundInterval;
            m_familiarAI.m_idleSoundChance = component.m_idleSoundChance;
            m_familiarAI.m_pathAgentType = component.m_pathAgentType;
            m_familiarAI.m_moveMinAngle = component.m_moveMinAngle;
            m_familiarAI.m_smoothMovement = component.m_smoothMovement;
            m_familiarAI.m_serpentMovement = component.m_serpentMovement;
            m_familiarAI.m_serpentTurnRadius = component.m_serpentTurnRadius;
            m_familiarAI.m_jumpInterval = component.m_jumpInterval;
            m_familiarAI.m_randomCircleInterval = RandomCircleInterval;
            m_familiarAI.m_randomMoveInterval = component.m_randomMoveInterval;
            m_familiarAI.m_randomMoveRange = component.m_randomMoveRange;
            m_familiarAI.m_randomFly = component.m_randomFly;
            m_familiarAI.m_chanceToTakeoff = component.m_chanceToTakeoff;
            m_familiarAI.m_chanceToLand = component.m_chanceToLand;
            m_familiarAI.m_groundDuration = component.m_groundDuration;
            m_familiarAI.m_airDuration = component.m_airDuration;
            m_familiarAI.m_maxLandAltitude = component.m_maxLandAltitude;
            m_familiarAI.m_takeoffTime = component.m_takeoffTime;
            m_familiarAI.m_flyAltitudeMin = component.m_flyAltitudeMin;
            m_familiarAI.m_flyAltitudeMax = component.m_flyAltitudeMax;
            m_familiarAI.m_flyAbsMinAltitude = component.m_flyAbsMinAltitude;
            m_familiarAI.m_avoidFire = component.m_avoidFire;
            m_familiarAI.m_afraidOfFire = component.m_afraidOfFire;
            m_familiarAI.m_avoidWater = component.m_avoidWater;
            m_familiarAI.m_avoidLava = component.m_avoidLava;
            m_familiarAI.m_skipLavaTargets = component.m_skipLavaTargets;
            m_familiarAI.m_avoidLavaFlee = component.m_avoidLavaFlee;
            m_familiarAI.m_passiveAggresive = component.m_passiveAggresive;
            m_familiarAI.m_fleeRange = component.m_fleeRange;
            m_familiarAI.m_fleeAngle = component.m_fleeAngle;
            m_familiarAI.m_fleeInterval = component.m_fleeInterval;
            m_familiarAI.m_maxDistanceFromPlayer = MaxDistanceFromPlayer;
            m_familiarAI.m_minDistanceFromPlayer = MinDistanceFromPlayer;
            
            return true;
        }
        
        private bool InheritAnimalAI()
        {
            if (!OriginalPrefab.TryGetComponent(out AnimalAI component)) return false;
            m_familiarAI = ClonedPrefab.AddComponent<FamiliarAI>();
            m_familiarAI.m_viewRange = component.m_viewRange;
            m_familiarAI.m_viewAngle = component.m_viewAngle;
            m_familiarAI.m_hearRange = component.m_hearRange;
            m_familiarAI.m_mistVision = component.m_mistVision;
            m_familiarAI.m_alertedEffects = component.m_alertedEffects;
            m_familiarAI.m_idleSound = component.m_idleSound;
            m_familiarAI.m_idleSoundInterval = component.m_idleSoundInterval;
            m_familiarAI.m_idleSoundChance = component.m_idleSoundChance;
            m_familiarAI.m_pathAgentType = component.m_pathAgentType;
            m_familiarAI.m_moveMinAngle = component.m_moveMinAngle;
            m_familiarAI.m_smoothMovement = component.m_smoothMovement;
            m_familiarAI.m_serpentMovement = component.m_serpentMovement;
            m_familiarAI.m_serpentTurnRadius = component.m_serpentTurnRadius;
            m_familiarAI.m_jumpInterval = component.m_jumpInterval;
            m_familiarAI.m_randomCircleInterval = RandomCircleInterval;
            m_familiarAI.m_randomMoveInterval = component.m_randomMoveInterval;
            m_familiarAI.m_randomMoveRange = component.m_randomMoveRange;
            m_familiarAI.m_randomFly = component.m_randomFly;
            m_familiarAI.m_chanceToTakeoff = component.m_chanceToTakeoff;
            m_familiarAI.m_chanceToLand = component.m_chanceToLand;
            m_familiarAI.m_groundDuration = component.m_groundDuration;
            m_familiarAI.m_airDuration = component.m_airDuration;
            m_familiarAI.m_maxLandAltitude = component.m_maxLandAltitude;
            m_familiarAI.m_takeoffTime = component.m_takeoffTime;
            m_familiarAI.m_flyAltitudeMin = component.m_flyAltitudeMin;
            m_familiarAI.m_flyAltitudeMax = component.m_flyAltitudeMax;
            m_familiarAI.m_flyAbsMinAltitude = component.m_flyAbsMinAltitude;
            m_familiarAI.m_avoidFire = component.m_avoidFire;
            m_familiarAI.m_afraidOfFire = component.m_afraidOfFire;
            m_familiarAI.m_avoidWater = component.m_avoidWater;
            m_familiarAI.m_avoidLava = component.m_avoidLava;
            m_familiarAI.m_skipLavaTargets = component.m_skipLavaTargets;
            m_familiarAI.m_avoidLavaFlee = component.m_avoidLavaFlee;
            m_familiarAI.m_passiveAggresive = component.m_passiveAggresive;
            m_familiarAI.m_fleeRange = component.m_fleeRange;
            m_familiarAI.m_fleeAngle = component.m_fleeAngle;
            m_familiarAI.m_fleeInterval = component.m_fleeInterval;
            m_familiarAI.m_maxDistanceFromPlayer = MaxDistanceFromPlayer;
            m_familiarAI.m_minDistanceFromPlayer = MinDistanceFromPlayer;

            return true;
        }

        private void AddPetComponent()
        {
            m_pet = ClonedPrefab.AddComponent<Pet>();
            Helpers.UpdateEffects(PetEffects, ref m_pet.m_petEffect);
        }

        private void Register()
        {
            if (!ZNetScene.instance.m_prefabs.Contains(ClonedPrefab)) ZNetScene.instance.m_prefabs.Add(ClonedPrefab);
            ZNetScene.instance.m_namedPrefabs[ClonedPrefab.name.GetStableHashCode()] = ClonedPrefab;
            if (!ZNetScene.instance.m_prefabs.Contains(Item.ClonedItem)) ZNetScene.instance.m_prefabs.Add(Item.ClonedItem);
            ZNetScene.instance.m_namedPrefabs[Item.ClonedItem.name.GetStableHashCode()] = Item.ClonedItem;
            if (!ObjectDB.instance.m_items.Contains(Item.ClonedItem)) ObjectDB.instance.m_items.Add(Item.ClonedItem);
            ObjectDB.instance.m_itemByHash[Item.ClonedItem.name.GetStableHashCode()] = Item.ClonedItem;
        }
    }
}