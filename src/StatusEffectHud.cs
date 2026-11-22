using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class StatusEffectHud : IDisposable
{
    private const float Width = 166, Height = 32;
    private readonly RectTransform root;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly HiddenGraphics nativeEffects;
    private readonly List<StatusEffect> effects = new();
    private readonly List<Row> rows = new();

    internal StatusEffectHud(Hud hud, RectTransform parent, Artwork art)
    {
        this.art = art;
        font = hud.m_healthText.font;
        root = ItemUi.Rect(parent, "ActiveEffects");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        root.sizeDelta = new Vector2(Width, 0);
        if (hud.m_statusEffectListRoot) nativeEffects = new HiddenGraphics(hud.m_statusEffectListRoot.gameObject);
    }

    internal void Refresh(Player player, float right)
    {
        root.anchoredPosition = new Vector2(-right, 0);
        effects.Clear();
        player.GetSEMan().GetHUDStatusEffects(effects);
        int shown = 0;
        foreach (var effect in effects)
        {
            if (effect.m_hidden) continue;
            if (shown == rows.Count) rows.Add(new Row(root, art, font));
            rows[shown].Refresh(effect, shown);
            shown++;
        }
        for (int i = shown; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        nativeEffects?.Restore();
        ItemUi.Destroy(root);
    }

    private sealed class Row
    {
        internal readonly RectTransform Rect;
        private readonly Image icon, state;
        private readonly TextMeshProUGUI title, duration;
        private StatusEffect previous;

        internal Row(RectTransform parent, Artwork art, TMP_FontAsset font)
        {
            Rect = ItemUi.Image(parent, "Effect", art["item-card"]).rectTransform;
            state = ItemUi.Image(Rect, "State");
            ItemUi.TopLeft(state.rectTransform, 4, 8, 2, 16);
            icon = ItemUi.Image(Rect, "Icon");
            icon.preserveAspect = true;
            ItemUi.TopLeft(icon.rectTransform, 11, 5, 22, 22);
            title = ItemUi.Text(Rect, "Name", font, 14, ItemUi.Ink);
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            duration = ItemUi.Text(Rect, "Remaining", font, 13, ItemUi.Accent);
            duration.alignment = TextAlignmentOptions.MidlineRight;
            duration.textWrappingMode = TextWrappingModes.NoWrap;
        }

        internal void Refresh(StatusEffect effect, int index)
        {
            if (!Rect.gameObject.activeSelf) Rect.gameObject.SetActive(true);
            ItemUi.TopLeft(Rect, 0, index * (Height + 4), Width, Height);
            if (previous != effect)
            {
                previous = effect;
                title.text = Localization.instance.Localize(effect.m_name);
            }
            if (icon.sprite != effect.m_icon) icon.sprite = effect.m_icon;
            icon.enabled = icon.sprite;
            bool warning = effect.m_flashIcon;
            icon.color = warning ? Color.Lerp(Color.white, ItemUi.Warning, 0.5f + 0.5f * Mathf.Sin(Time.time * 10)) : Color.white;
            state.color = warning ? ItemUi.Warning : effect.m_cooldownIcon ? ItemUi.Muted : ItemUi.Accent;
            string time = effect.GetIconText();
            if (duration.text != time) duration.text = time;
            duration.enabled = !string.IsNullOrEmpty(time);
            float timerWidth = duration.enabled ? Mathf.Min(55, Mathf.Ceil(duration.GetPreferredValues(time).x)) : 0;
            ItemUi.TopLeft(duration.rectTransform, Width - timerWidth - 10, 0, timerWidth, Height);
            ItemUi.TopLeft(title.rectTransform, 40, 0, Width - 55 - timerWidth, Height);
        }
    }
}
