using System.Text;
using BepInEx;
using UnityEngine;
namespace Companions.Behaviors;

public class Pet : MonoBehaviour, Interactable, TextReceiver
{
    public ZNetView m_nview = null!;
    public EffectList m_petEffect = new EffectList();
    public Familiar m_familiar = null!;
    public void Awake()
    {
        m_nview = GetComponent<ZNetView>();
        m_familiar = GetComponent<Familiar>();
        if (m_nview.IsValid())
        {
            m_nview.Register<string, string>(nameof(RPC_SetName), RPC_SetName);
        }
    }

    public void RPC_SetName(long sender, string name, string authorID)
    {
        if (!m_nview.IsValid() || !m_nview.IsOwner()) return;
        m_nview.GetZDO().Set(ZDOVars.s_tamedName, name);
        m_nview.GetZDO().Set(ZDOVars.s_tamedNameAuthor, authorID);
        if (m_familiar.m_itemData is not null) m_familiar.m_itemData.m_customData["CompanionName"] = name;
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (!m_nview.IsValid() || hold) return false;
        if (alt) TextInput.instance.RequestText(this, "$hud_rename", 10);
        else
        {
            m_petEffect.Create(transform.position, transform.rotation);
        }
        return true;
    }

    public string GetHoverText()
    {
        if (!m_nview.IsValid()) return "";
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append("\n[<color=yellow><b>$KEY_Use</b></color>] $hud_pet");
        stringBuilder.Append("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename");
        return Localization.instance.Localize(stringBuilder.ToString());
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

    public string GetText()
    {
        if (!m_nview.IsValid()) return m_familiar.m_name;
        var tamedName = m_nview.GetZDO().GetString(ZDOVars.s_tamedName);
        return tamedName.IsNullOrWhiteSpace() ? m_familiar.m_name : tamedName;
    }

    public void SetText(string text)
    {
        if (!m_nview.IsValid()) return;
        m_nview.InvokeRPC(nameof(RPC_SetName), text, PrivilegeManager.GetNetworkUserId());
    }
}