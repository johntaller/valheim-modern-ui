using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class CombatReticle : IDisposable
{
    private readonly Hud hud;
    private readonly RectTransform root, burden;
    private readonly Image weight, stealthMark;
    private readonly TextMeshProUGUI burdenLabel;
    private readonly List<HiddenGraphics> stealthGraphics = new();

    internal CombatReticle(Hud hud, RectTransform parent, Artwork art)
    {
        this.hud = hud;
        root = ItemUi.Rect(parent, "CombatReticle");
        HudStyle.Center(root, 0, 0, 1, 1);
        foreach (var state in new[] { hud.m_hidden, hud.m_targeted, hud.m_targetedAlert,
            hud.m_stealthBar ? hud.m_stealthBar.gameObject : null })
            if (state) stealthGraphics.Add(Hide(state));

        stealthMark = ItemUi.Image(root, "StealthDiamond", art["pickup-ring"]);
        HudStyle.Center(stealthMark.rectTransform, 0, 0, 22, 22);
        burden = ItemUi.Rect(root, "Encumbered");
        HudStyle.Center(burden, 0, 60, 180, 81);
        HudStyle.Center(HudStyle.Shadow(burden, art, 0.94f).rectTransform, 0, 0, 194, 102);
        weight = ItemUi.Image(burden, "Weight", art["weight"]);
        HudStyle.Center(weight.rectTransform, 0, 13, 29, 29);
        burdenLabel = ItemUi.Text(burden, "Warning", HudStyle.DisplayFont(hud.m_healthText.font), 16, ItemUi.Ink);
        burdenLabel.alignment = TextAlignmentOptions.Center;
        burdenLabel.text = MenuStyle.Translate("Encumbered", "Перевес");
        HudStyle.Center(burdenLabel.rectTransform, 0, -15, 180, 24);
    }

    private static HiddenGraphics Hide(GameObject target)
    {
        var hidden = new HiddenGraphics(target);
        hidden.SetRaycasts(false);
        return hidden;
    }

    internal void Refresh(Player player, bool visible)
    {
        root.gameObject.SetActive(visible);
        if (!visible) return;
        float now = Time.unscaledTime;
        bool stealth = hud.m_stealthBar && hud.m_stealthBar.gameObject.activeInHierarchy;
        bool alert = hud.m_targetedAlert && hud.m_targetedAlert.activeSelf;
        bool spotted = hud.m_targeted && hud.m_targeted.activeSelf;
        stealthMark.enabled = stealth;
        stealthMark.color = alert ? (Color)ItemUi.Warning : spotted ? ItemUi.Accent
            : Color.Lerp(new Color(1, 1, 1, 0.43f), ItemUi.Ink, player.GetStealthFactor());
        stealthMark.rectTransform.localScale = Vector3.one * (alert ? 1 + Mathf.Sin(now * 11) * 0.13f : 1);

        bool overloaded = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectEncumbered);
        burden.gameObject.SetActive(overloaded);
        weight.color = Color.Lerp(ItemUi.Warning, ItemUi.Ink, Mathf.Pow(0.5f + Mathf.Sin(now * 7) * 0.5f, 2));
    }

    public void Dispose()
    {
        foreach (var hidden in stealthGraphics) hidden.Restore();
        ItemUi.Destroy(root);
    }
}
