using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class StatusEffectHud : IDisposable
{
    private const float Height = 33, Pitch = 37;
    private const int VisibleRows = 3;
    private readonly RectTransform root, rail, handle;
    private readonly Image surface;
    private readonly Canvas canvas;
    private readonly HiddenGraphics nativeEffects;
    private readonly List<StatusEffect> effects = new();
    private readonly List<StatusEffect> ordered = new(), harmful = new(), other = new();
    private readonly TextMeshProUGUI overflow;
    private readonly Image overflowShadow;
    private StatusEffect newest;
    private bool followNewest;
    private readonly Row[] rows = new Row[VisibleRows];
    private int lastInputFrame = -1, firstRow;
    private bool dragging;
    private float width;

    internal StatusEffectHud(Hud hud, RectTransform parent, Artwork art)
    {
        surface = ItemUi.Image(parent, "ActiveEffects");
        root = surface.rectTransform;
        ItemUi.TopLeft(root, 184, 7, 18, Pitch * VisibleRows);
        canvas = parent.GetComponentInParent<Canvas>().rootCanvas;
        var shadows = ItemUi.Rect(root, "RowShadows");
        ItemUi.Stretch(shadows, Vector2.zero, Vector2.zero);
        for (int i = 0; i < rows.Length; i++) rows[i] = new Row(root, shadows, art, hud.m_healthText.font);
        overflowShadow = HudStyle.Shadow(root, art, 0.66f);
        overflow = ItemUi.Text(root, "HiddenEffects", hud.m_healthText.font, 13, ItemUi.Muted);
        var track = ItemUi.Image(root, "EffectsScrollbar", art["meter-fill"]);
        track.color = new Color32(43, 36, 27, 244);
        rail = track.rectTransform;
        var thumb = ItemUi.Image(rail, "Handle", art["meter-fill"]);
        thumb.color = ItemUi.Accent;
        handle = thumb.rectTransform;
        if (hud.m_statusEffectListRoot) nativeEffects = new HiddenGraphics(hud.m_statusEffectListRoot.gameObject);
    }

    internal void Refresh(Player player, bool inventory)
    {
        effects.Clear();
        player.GetSEMan().GetHUDStatusEffects(effects);
        effects.RemoveAll(effect => effect.m_hidden || effect.NameHash() == SEMan.s_statusEffectEncumbered
            || RestHud.HasDedicatedSlot(effect.NameHash()));
        ordered.RemoveAll(effect => !effects.Contains(effect));
        foreach (var effect in effects)
        {
            if (ordered.Contains(effect)) continue;
            ordered.Add(effect);
            newest = effect;
            followNewest = true;
        }
        harmful.Clear();
        other.Clear();
        foreach (var effect in ordered)
            (IsHarmful(effect, effect.NameHash()) ? harmful : other).Add(effect);
        float nextWidth = 0;
        foreach (var effect in harmful) nextWidth = Mathf.Max(nextWidth, rows[0].Measure(effect));
        foreach (var effect in other) nextWidth = Mathf.Max(nextWidth, rows[0].Measure(effect));
        if (width != nextWidth)
        {
            width = nextWidth;
            root.sizeDelta = new Vector2(width + 18, Pitch * VisibleRows);
            ItemUi.TopLeft(rail, width + 6, 5, 6, Pitch * VisibleRows - 10);
        }
        int count = harmful.Count + other.Count;
        root.gameObject.SetActive(count > 0);
        int lastRow = Mathf.Max(0, count - VisibleRows);
        bool canScroll = inventory && lastRow > 0;
        overflow.enabled = overflowShadow.enabled = !inventory && lastRow > 0;
        if (overflow.enabled)
        {
            overflow.SetText("+{0}", lastRow);
            ItemUi.TopLeft(overflow.rectTransform, 11, Pitch * VisibleRows, 50, 18);
            ItemUi.TopLeft(overflowShadow.rectTransform, -46, Pitch * VisibleRows - 14, 141, 46);
        }
        if (inventory && followNewest)
        {
            int latestIndex = harmful.IndexOf(newest);
            int otherIndex = other.IndexOf(newest);
            if (otherIndex >= 0) latestIndex = harmful.Count + otherIndex;
            if (latestIndex >= 0) firstRow = Mathf.Max(0, latestIndex - VisibleRows + 1);
            followNewest = false;
        }
        rail.gameObject.SetActive(canScroll);
        root.anchoredPosition = new Vector2(184, inventory ? 28 : -7);
        surface.color = inventory ? new Color32(20, 18, 16, 210) : Color.clear;
        if (canScroll)
        {
            float thumbHeight = rail.rect.height * VisibleRows / count;
            ReadScroll(lastRow, thumbHeight);
            firstRow = Mathf.Clamp(firstRow, 0, lastRow);
            ItemUi.TopLeft(handle, 0, (rail.rect.height - thumbHeight) * firstRow / lastRow, rail.rect.width, thumbHeight);
        }
        else
        {
            if (inventory) firstRow = 0;
            dragging = false;
        }
        for (int i = 0; i < rows.Length; i++)
        {
            if (i >= count)
            {
                rows[i].Hide();
                continue;
            }
            int index = firstRow + i;
            if (!inventory)
            {
                // Keep harmful effects above the newest entries from each group.
                int harmfulRows = Mathf.Min(harmful.Count, VisibleRows);
                index = i < harmfulRows ? harmful.Count - harmfulRows + i
                    : Mathf.Max(harmful.Count, lastRow + harmfulRows) + i - harmfulRows;
            }
            bool negative = index < harmful.Count;
            rows[i].Refresh(negative ? harmful[index] : other[index - harmful.Count], negative, width, i);
        }
    }

    private void ReadScroll(int lastRow, float thumbHeight)
    {
        if (lastInputFrame == Time.frameCount) return;
        lastInputFrame = Time.frameCount;
        // Valheim reserves inventory-wide pointer events on its own canvas.
        // Read its pointer and wheel only within the status panel.
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, ZInput.pointerPosition, camera, out var point)) return;
        if (root.rect.Contains(point))
        {
            float wheel = ZInput.GetMouseScrollWheel();
            if (wheel != 0) firstRow -= (int)Mathf.Sign(wheel) * Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(wheel)), 1, 3);
            if (point.x >= width && ZInput.GetMouseButtonDown(0)) dragging = true;
        }
        if (!ZInput.GetMouseButton(0)) dragging = false;
        if (dragging)
            firstRow = Mathf.RoundToInt(Mathf.Clamp01((-point.y - 5 - thumbHeight * 0.5f)
                / (rail.rect.height - thumbHeight)) * lastRow);
    }

    private static bool IsHarmful(StatusEffect effect, int hash) =>
        effect.m_flashIcon || hash == SEMan.s_statusEffectWet || hash == SEMan.s_statusEffectCold
        || hash == SEMan.s_statusEffectFreezing
        || hash == SEMan.s_statusEffectBurning || hash == SEMan.s_statusEffectFrost
        || hash == SEMan.s_statusEffectPoison || hash == SEMan.s_statusEffectSmoked
        || hash == SEMan.s_statusEffectTared || hash == SEMan.s_statusEffectSpirit
        || hash == SEMan.s_statusEffectLightning;

    public void Dispose()
    {
        nativeEffects?.Restore();
        ItemUi.Destroy(root);
    }

    private sealed class Row
    {
        private readonly RectTransform rect;
        private readonly Image icon, state, shadow;
        private readonly TextMeshProUGUI title, duration;
        private StatusEffect previous;

        internal Row(RectTransform parent, RectTransform shadows, Artwork art, TMP_FontAsset font)
        {
            rect = ItemUi.Rect(parent, "Effect");
            shadow = HudStyle.Shadow(shadows, art, 0.77f);
            state = ItemUi.Image(rect, "State");
            ItemUi.TopLeft(state.rectTransform, 4, 8, 2, 16);
            icon = ItemUi.Image(rect, "Icon");
            icon.preserveAspect = true;
            ItemUi.TopLeft(icon.rectTransform, 11, 5, 22, 22);
            title = ItemUi.Text(rect, "Name", font, 14, ItemUi.Ink);
            title.overflowMode = TextOverflowModes.Overflow;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            duration = ItemUi.Text(rect, "Remaining", font, 13, ItemUi.Accent);
            duration.alignment = TextAlignmentOptions.MidlineRight;
            duration.textWrappingMode = TextWrappingModes.NoWrap;
        }

        internal void Hide()
        {
            rect.gameObject.SetActive(false);
            shadow.enabled = false;
        }

        internal float Measure(StatusEffect effect)
        {
            string name = Localization.instance.Localize(effect.m_name);
            string time = effect.GetIconText();
            return Mathf.Ceil(title.GetPreferredValues(name).x + duration.GetPreferredValues(time).x) + 59;
        }

        internal void Refresh(StatusEffect effect, bool harmful, float width, int index)
        {
            if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
            shadow.enabled = true;
            if (previous != effect)
            {
                previous = effect;
                title.text = Localization.instance.Localize(effect.m_name);
            }
            if (icon.sprite != effect.m_icon) icon.sprite = effect.m_icon;
            icon.enabled = icon.sprite;
            bool warning = effect.m_flashIcon;
            icon.color = warning ? Color.Lerp(Color.white, ItemUi.Warning, 0.5f + 0.5f * Mathf.Sin(Time.time * 10)) : Color.white;
            state.color = harmful ? ItemUi.Warning : effect.m_cooldownIcon ? ItemUi.Muted : ItemUi.Accent;
            string time = effect.GetIconText();
            if (duration.text != time) duration.text = time;
            duration.enabled = !string.IsNullOrEmpty(time);
            float timerWidth = duration.enabled ? Mathf.Ceil(duration.GetPreferredValues(time).x) : 0;
            ItemUi.TopLeft(rect, 0, index * Pitch, width, Height);
            ItemUi.TopLeft(shadow.rectTransform, -78, index * Pitch - 20, width + 156, Height + 40);
            ItemUi.TopLeft(duration.rectTransform, width - timerWidth - 10, 0, timerWidth, Height);
            ItemUi.TopLeft(title.rectTransform, 40, 0, width - 55 - timerWidth, Height);
        }
    }
}
