using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace Companions.Behaviors;

public class SE_Pet : SE_Stats
{
    public static ItemDrop.ItemData? m_currentCompanionItem;
    public static ItemDrop.ItemData? m_tempCompanionItem;
    public readonly Config config = new();
    public class Config
    {
        public ConfigEntry<float> ItemQualityMultiplier = null!;
        public ConfigEntry<float> HealthRegen = null!;
        public ConfigEntry<float> StaminaRegen = null!;
        public ConfigEntry<float> EitrRegen = null!;
        public ConfigEntry<float> CarryWeight = null!;
        public ConfigEntry<float> Speed = null!;
        public ConfigEntry<float> Jump = null!;
        public ConfigEntry<float> MaxFallSpeed = null!;

        // public ConfigEntry<string> Resistances = null!;
        // public readonly SerializedResistances Resists = new(new List<HitData.DamageModPair>());

        public class SerializedResistances
        {
            public readonly List<HitData.DamageModPair> Resistances;
            public SerializedResistances(List<HitData.DamageModPair> resists) => Resistances = resists;
            
            public SerializedResistances(string resists)
            {
                List<HitData.DamageModPair> output = new();
                foreach (var part in resists.Split(','))
                {
                    var pair = part.Split(':');
                    if (!Enum.TryParse(pair[0], true, out HitData.DamageType type)) continue;
                    if (!Enum.TryParse(pair[1], true, out HitData.DamageModifier mod)) continue;
                    output.Add(new HitData.DamageModPair()
                    {
                        m_type = type,
                        m_modifier = mod
                    });
                }
            
                Resistances = output;
            }
        
            public void Add(HitData.DamageType type, HitData.DamageModifier mod) => Resistances.Add(new HitData.DamageModPair() { m_type = type, m_modifier = mod });
        
            public override string ToString() => string.Join(",", Resistances.Select(r => $"{r.m_type}:{r.m_modifier}"));
        }
    }

    public void SetConfigData() => SetConfigData(GetQualityModifier());
    
    public void SetConfigData(float qualityModifier)
    {
        m_healthRegenMultiplier = config.HealthRegen.Value + qualityModifier * (config.HealthRegen.Value - 1f);
        m_staminaRegenMultiplier = config.StaminaRegen.Value + qualityModifier * (config.StaminaRegen.Value - 1f);
        m_eitrRegenMultiplier = config.EitrRegen.Value + qualityModifier * (config.EitrRegen.Value - 1f);
        m_addMaxCarryWeight = config.CarryWeight.Value * qualityModifier;
        m_speedModifier = config.Speed.Value * qualityModifier;
        m_jumpModifier = new Vector3(0f, config.Jump.Value * qualityModifier, 0f);
        m_maxMaxFallSpeed = config.MaxFallSpeed.Value / qualityModifier;
    }

    private float GetTempQualityModifier() => m_tempCompanionItem == null ? 1f : config.ItemQualityMultiplier.Value * (m_tempCompanionItem.m_quality - 1) + 1f;
    
    private float GetQualityModifier() => m_currentCompanionItem == null ? 1f : config.ItemQualityMultiplier.Value * (m_currentCompanionItem.m_quality - 1) + 1f;

    public override string GetTooltipString()
    {
        SetConfigData(GetTempQualityModifier());
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append(base.GetTooltipString());
        return stringBuilder.ToString();
    }
}