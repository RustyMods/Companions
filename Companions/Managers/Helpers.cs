using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Companions.Managers;

public static class Helpers
{
    public static string GetNormalizedName(string name) => Regex.Replace(name, @"\s*\(.*?\)", "").Trim();
    public static void UpdateEffects(List<string> effects, ref EffectList effectList)
    {
        if (effects.Count <= 0) return;
        List<EffectList.EffectData> data = effectList.m_effectPrefabs.ToList();
        foreach (string effect in effects)
        {
            GameObject? prefab = ZNetScene.instance.GetPrefab(effect);
            if (!prefab) continue;
            data.Add(new EffectList.EffectData() { m_prefab = prefab });
        }

        effectList.m_effectPrefabs = data.ToArray();
    }
}