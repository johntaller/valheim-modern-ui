using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class StatusEffectHud : IDisposable
{
    private const float Width = 174, Height = 33, Pitch = 37;
    private const int VisibleRows = 3;
    private readonly RectTransform root, rail, handle;
    private readonly Image surface;
    private readonly Canvas canvas;
    private readonly HiddenGraphics nativeEffects;
    private readonly List<StatusEffect> effects = new();
    private readonly List<StatusEffect> harmful = new(), other = new();
    private readonly Row[] rows = new Row[VisibleRows];
    private int lastInputFrame = -1, firstRow;
    private bool dragging;

    internal StatusEffectHud(Hud hud, RectTransform parent, Artwork art)
    {
        surface = ItemUi.Image(parent, "ActiveEffects");
        root = surface.rectTransform;
        ItemUi.TopLeft(root, 184, 7, Width + 18, Pitch * VisibleRows);
        canvas = parent.GetComponentInParent<Canvas>().rootCanvas;
        var shadows = ItemUi.Rect(root, "RowShadows");
        ItemUi.Stretch(shadows, Vector2.zero, Vector2.zero);
        for (int i = 0; i < rows.Length; i++) rows[i] = new Row(root, shadows, art, hud.m_healthText.font, i);
        var track = ItemUi.Image(root, "EffectsScrollbar", art["meter-fill"]);
        track.color = new Color32(43, 36, 27, 244);
        rail = track.rectTransform;
        ItemUi.TopLeft(rail, Width + 6, 5, 6, Pitch * VisibleRows - 10);
        var thumb = ItemUi.Image(rail, "Handle", art["meter-fill"]);
        thumb.color = ItemUi.Accent;
        handle = thumb.rectTransform;
        if (hud.m_statusEffectListRoot) nativeEffects = new HiddenGraphics(hud.m_statusEffectListRoot.gameObject);
    }

    internal void Refresh(Player player, bool inventory)
    {
        effects.Clear();
        player.GetSEMan().GetHUDStatusEffects(effects);
        harmful.Clear();
        other.Clear();
        foreach (var effect in effects)
        {
            int hash = effect.NameHash();
            if (effect.m_hidden || RestHud.HasDedicatedSlot(hash)) continue;
            (IsHarmful(effect, hash) ? harmful : other).Add(effect);
        }
        int count = harmful.Count + other.Count;
        root.gameObject.SetActive(count > 0);
        int lastRow = Mathf.Max(0, count - VisibleRows);
        bool canScroll = inventory && lastRow > 0;
        rail.gameObject.SetActive(canScroll);
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
            firstRow = 0;
            dragging = false;
        }
        for (int i = 0; i < rows.Length; i++)
        {
            int index = firstRow + i;
            bool negative = index < harmful.Count;
            if (index >= count) rows[i].Hide();
            else rows[i].Refresh(negative ? harmful[index] : other[index - harmful.Count], negative);
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
            if (point.x >= Width && ZInput.GetMouseButtonDown(0)) dragging = true;
        }
        if (!ZInput.GetMouseButton(0)) dragging = false;
        if (dragging)
            firstRow = Mathf.RoundToInt(Mathf.Clamp01((-point.y - 5 - thumbHeight * 0.5f)
                / (rail.rect.height - thumbHeight)) * lastRow);
    }

    private static bool IsHarmful(StatusEffect effect, int hash) =>
        effect.m_flashIcon || hash == SEMan.s_statusEffectWet || hash == SEMan.s_statusEffectCold
        || hash == SEMan.s_statusEffectFreezing || hash == SEMan.s_statusEffectEncumbered
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

        internal Row(RectTransform parent, RectTransform shadows, Artwork art, TMP_FontAsset font, int index)
        {
            rect = ItemUi.Rect(parent, "Effect");
            ItemUi.TopLeft(rect, 0, index * Pitch, Width, Height);
            shadow = HudStyle.Shadow(shadows, art, 0.77f);
            ItemUi.TopLeft(shadow.rectTransform, -78, index * Pitch - 20, Width + 156, Height + 40);
            state = ItemUi.Image(rect, "State");
            ItemUi.TopLeft(state.rectTransform, 4, 8, 2, 16);
            icon = ItemUi.Image(rect, "Icon");
            icon.preserveAspect = true;
            ItemUi.TopLeft(icon.rectTransform, 11, 5, 22, 22);
            title = ItemUi.Text(rect, "Name", font, 14, ItemUi.Ink);
            title.overflowMode = TextOverflowModes.Ellipsis;
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

        internal void Refresh(StatusEffect effect, bool harmful)
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
            float timerWidth = duration.enabled ? Mathf.Min(55, Mathf.Ceil(duration.GetPreferredValues(time).x)) : 0;
            ItemUi.TopLeft(duration.rectTransform, Width - timerWidth - 10, 0, timerWidth, Height);
            ItemUi.TopLeft(title.rectTransform, 40, 0, Width - 55 - timerWidth, Height);
        }
    }
}
