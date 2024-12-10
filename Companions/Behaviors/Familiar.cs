using System.Collections.Generic;
using HarmonyLib;

namespace Companions.Behaviors;

public class Familiar : Character
{
    public static readonly List<Familiar> m_instances = new();
    public Pet m_pet = null!;
    public FamiliarAI m_familiarAI = null!;
    public ItemDrop.ItemData? m_itemData;
    public override void Awake()
    {
        base.Awake();
        m_pet = GetComponent<Pet>();
        m_familiarAI = GetComponent<FamiliarAI>();
        m_instances.Add(this);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        m_instances.Remove(this);
    }
    
    public override bool InGodMode() => true;
    public override bool InGhostMode() => true;

    public override string GetHoverText() => m_pet.GetHoverText();

    public override string GetHoverName() => m_pet.GetText();

    [HarmonyPatch(typeof(Character), nameof(IsTamed), typeof(float))]
    private static class Character_IsTamed_Patch
    {
        private static bool Prefix(Character __instance, ref bool __result)
        {
            if (__instance is not Familiar) return true;
            __result = true;
            return false;
        }
    }
}