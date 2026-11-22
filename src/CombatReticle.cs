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
    private readonly BraceGraphic brace;
    private readonly HudSparks sparks;
    private readonly HiddenGraphics vanillaAdrenaline;
    private readonly List<HiddenGraphics> stealthGraphics = new();
    private Color nativeColor;

    internal CombatReticle(Hud hud, RectTransform parent, Artwork art)
    {
        this.hud = hud;
        root = ItemUi.Rect(parent, "CombatReticle");
        HudStyle.Center(root, 0, 0, 1, 1);
        if (hud.m_adrenalineBarRoot) vanillaAdrenaline = Hide(hud.m_adrenalineBarRoot.gameObject);
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

        var bar = ItemUi.Rect(root, "AdrenalineBrace");
        HudStyle.Center(bar, 0, -39, 130, 28);
        brace = bar.gameObject.AddComponent<BraceGraphic>();
        brace.raycastTarget = false;
        sparks = HudSparks.Create(root, "AdrenalineEmbers", 136, 64, 22);
        sparks.rectTransform.anchoredPosition = new Vector2(0, -34);
        nativeColor = hud.m_adrenalineBarFast ? hud.m_adrenalineBarFast.GetColor() : ItemUi.Accent;
        if (nativeColor.a <= 0) nativeColor = ItemUi.Accent;
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

        float fill = 0, remaining = 0;
        float max = player.GetMaxAdrenaline();
        if (max > 0) fill = player.GetAdrenaline() / max;
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (!item.m_equipped || !item.m_shared.m_fullAdrenalineSE) continue;
            var effect = player.GetSEMan().GetStatusEffect(item.m_shared.m_fullAdrenalineSE.NameHash());
            if (!effect || effect.m_ttl <= 0) continue;
            remaining = Mathf.Max(remaining, Mathf.Clamp01(1 - effect.GetDuration() / effect.m_ttl));
        }
        bool active = remaining > 0;
        brace.gameObject.SetActive(active || fill > 0);
        sparks.gameObject.SetActive(active);
        if (!brace.gameObject.activeSelf) return;
        Color ember = Color.Lerp(new Color32(255, 124, 47, 255), new Color32(255, 224, 143, 255),
            0.55f + 0.3f * Mathf.Sin(now * 9));
        brace.Set(active ? remaining : fill, active ? ember : nativeColor, active);
        if (active)
        {
            sparks.rectTransform.anchoredPosition = new Vector2(-64 * (1 - remaining), -34);
            sparks.Animate(now, ember, 61 * remaining, 14);
        }
    }

    public void Dispose()
    {
        vanillaAdrenaline?.Restore();
        foreach (var hidden in stealthGraphics) hidden.Restore();
        ItemUi.Destroy(root);
    }
}

internal sealed class BraceGraphic : MaskableGraphic
{
    private float fill;
    private bool active;

    internal void Set(float value, Color tint, bool burning)
    {
        color = tint;
        if (fill == value && burning == active) return;
        fill = value;
        active = burning;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Stroke(mesh, 1, 6.5f, new Color32(17, 13, 13, 230));
        Stroke(mesh, 1, 3, new Color32(92, 68, 60, 210));
        if (active) Stroke(mesh, fill, 7, new Color(1, 0.28f, 0.08f, 0.42f));
        Stroke(mesh, fill, 3.2f, color);
    }

    private static void Stroke(VertexHelper mesh, float fraction, float thickness, Color tint)
    {
        int steps = Mathf.CeilToInt(64 * Mathf.Clamp01(fraction));
        if (steps == 0) return;
        Vector2 previous = Point(-1);
        for (int i = 1; i <= steps; i++)
        {
            Vector2 current = Point(-1 + 2 * Mathf.Clamp01(fraction) * i / steps);
            Vector2 delta = current - previous;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
            int v = mesh.currentVertCount;
            mesh.AddVert(previous + normal, tint, Vector2.zero);
            mesh.AddVert(previous - normal, tint, Vector2.zero);
            mesh.AddVert(current - normal, tint, Vector2.zero);
            mesh.AddVert(current + normal, tint, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
            mesh.AddTriangle(v, v + 2, v + 3);
            previous = current;
        }
    }

    private static Vector2 Point(float x)
    {
        float distance = Mathf.Abs(x);
        float join = -9 * Mathf.Pow(1 - Mathf.Clamp01(distance * 4.4f), 2);
        float curl = 8 * Mathf.Pow(Mathf.InverseLerp(0.78f, 1, distance), 2.2f);
        return new Vector2(x * 64, join + curl);
    }
}
