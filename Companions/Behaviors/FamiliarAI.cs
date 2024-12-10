using HarmonyLib;
using UnityEngine;

namespace Companions.Behaviors;

public class FamiliarAI : BaseAI
{
    public Player? m_owner;
    public float m_maxDistanceToForceMove = 25f;
    public float m_maxDistanceFromPlayer = 10f;
    public float m_minDistanceFromPlayer = 5f;
    public float m_timeSinceMovedToPlayer;

    public override bool UpdateAI(float dt)
    {
        if (!base.UpdateAI(dt)) return false;
        if (m_owner is not null)
        {
            m_timeSinceMovedToPlayer += dt;
            if (m_maxDistanceToForceMove < Vector3.Distance(m_owner.transform.position, transform.position))
            {
                return MoveTo(dt, m_owner.transform.position, m_minDistanceFromPlayer, true);
            }
            if (m_timeSinceMovedToPlayer > 10f)
            {
                if (m_maxDistanceFromPlayer < Vector3.Distance(m_owner.transform.position, transform.position))
                {
                    return MoveTo(dt, m_owner.transform.position, m_minDistanceFromPlayer, true);
                }
                m_timeSinceMovedToPlayer = 0f;
            }
            m_spawnPoint = m_owner.transform.position;
            IdleMovement(dt);
            return true;
        }
        MoveAwayAndDespawn(dt, true);
        return false;
    }

    [HarmonyPatch(typeof(BaseAI), nameof(IsEnemy), typeof(Character))]
    private static class BaseAI_IsEnemy_Patch
    {
        private static bool Prefix(Character other, ref bool __result)
        {
            if (other is not Familiar) return true;
            __result = false;
            return false;
        }
    }
    
}