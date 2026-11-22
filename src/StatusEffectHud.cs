using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class StatusEffectHud : IDisposable
{
    private const float Width = 174, Height = 33, Pitch = 37, VisibleRows = 3;
    private readonly RectTransform root, content, rail, handle;
    private readonly Image surface;
    private readonly Canvas canvas;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly HiddenGraphics nativeEffects;
    private readonly List<StatusEffect> effects = new();
    private readonly List<Row> rows = new();
    private int lastInputFrame = -1;
    private float offset;
    private bool dragging;

    internal StatusEffectHud(Hud hud, RectTransform parent, Artwork art)
    {
        this.art = art;
        font = hud.m_healthText.font;
        surface = ItemUi.Image(parent, "ActiveEffects");
        root = surface.rectTransform;
        ItemUi.TopLeft(root, 287, 10, Width + 18, Pitch * VisibleRows);
        canvas = parent.GetComponentInParent<Canvas>().rootCanvas;
        var viewport = ItemUi.Rect(root, "Viewport");
        ItemUi.Stretch(viewport, Vector2.zero, new Vector2(-8, 0));
        viewport.gameObject.AddComponent<RectMask2D>();
        content = ItemUi.Rect(viewport, "Effects");
        ItemUi.TopLeft(content, 0, 0, Width, Pitch * VisibleRows);
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
        int shown = 0;
        foreach (var effect in effects)
        {
            if (effect.m_hidden || RestHud.HasDedicatedSlot(effect.NameHash())) continue;
            if (shown == rows.Count) rows.Add(new Row(content, art, font));
            rows[shown].Refresh(effect, shown);
            shown++;
        }
        for (int i = shown; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(false);
        root.gameObject.SetActive(shown > 0);
        if (shown == 0) return;
        bool canScroll = inventory && shown > VisibleRows;
        rail.gameObject.SetActive(canScroll);
        surface.color = inventory ? new Color32(20, 18, 16, 210) : Color.clear;
        if (canScroll)
        {
            float travel = Pitch * (shown - VisibleRows);
            float thumbHeight = rail.rect.height * VisibleRows / shown;
            ReadScroll(travel, thumbHeight);
            offset = Mathf.Clamp(offset, 0, travel);
            ItemUi.TopLeft(handle, 0, (rail.rect.height - thumbHeight) * offset / travel, rail.rect.width, thumbHeight);
        }
        else
        {
            offset = 0;
            dragging = false;
        }
        content.anchoredPosition = new Vector2(0, offset);
        content.sizeDelta = new Vector2(Width, Pitch * Mathf.Max(VisibleRows, shown));
    }

    private void ReadScroll(float travel, float thumbHeight)
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
            if (wheel != 0) offset -= Mathf.Clamp(wheel, -3, 3) * Pitch;
            if (point.x >= Width && ZInput.GetMouseButtonDown(0)) dragging = true;
        }
        if (!ZInput.GetMouseButton(0)) dragging = false;
        if (dragging)
            offset = Mathf.Clamp01((-point.y - 5 - thumbHeight * 0.5f) / (rail.rect.height - thumbHeight)) * travel;
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
            Rect = ItemUi.Rect(parent, "Effect");
            var shadow = HudStyle.Shadow(Rect, art, 0.92f);
            HudStyle.Center(shadow.rectTransform, 0, 0, Width + 64, 63);
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
            ItemUi.TopLeft(Rect, 0, index * Pitch, Width, Height);
            if (previous != effect)
            {
                previous = effect;
                title.text = Localization.instance.Localize(effect.m_name);
            }
            if (icon.sprite != effect.m_icon) icon.sprite = effect.m_icon;
            icon.enabled = icon.sprite;
            bool warning = effect.m_flashIcon;
            icon.color = warning ? Color.Lerp(Color.white, ItemUi.Warning, 0.5f + 0.5f * Mathf.Sin(Time.time * 10)) : Color.white;
            int hash = effect.NameHash();
            bool harmful = warning || hash == SEMan.s_statusEffectWet || hash == SEMan.s_statusEffectCold
                || hash == SEMan.s_statusEffectFreezing || hash == SEMan.s_statusEffectEncumbered
                || hash == SEMan.s_statusEffectBurning || hash == SEMan.s_statusEffectFrost
                || hash == SEMan.s_statusEffectPoison || hash == SEMan.s_statusEffectSmoked
                || hash == SEMan.s_statusEffectTared || hash == SEMan.s_statusEffectSpirit
                || hash == SEMan.s_statusEffectLightning;
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
