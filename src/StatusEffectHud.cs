using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Active status effects beside the rested and power slots. In play they are a compact grid of
// icons with their timers, filling rows from the bottom; with the inventory open they become a
// list with names, in columns, so nothing ever needs scrolling. Harmful effects always come first
// and wear a red plate; an effect about to run out blinks and its timer turns red.
internal sealed class StatusEffectHud : IDisposable
{
    private const float Block = 130;
    // Play: a plate with the icon and its timer below.
    private const float Plate = 28, CellStep = 38, RowStep = 44, TimerHeight = 13;
    private const int PerRow = 6, Rows = 3;
    // Inventory: lines with names, top to bottom, then the next column.
    private const float LineHeight = 26, LineWidth = 196, ColumnGap = 12;
    private const int PerColumn = 5;

    private static readonly Color32 HarmfulPlate = new(232, 118, 104, 255);
    private static readonly Color32 HarmfulName = new(255, 150, 136, 255);

    private readonly RectTransform root;
    private readonly HiddenGraphics nativeEffects;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly List<StatusEffect> effects = new();
    private readonly List<Entry> entries = new();
    private readonly Comparison<StatusEffect> order;
    private readonly TextMeshProUGUI more;
    private float left;

    internal StatusEffectHud(Hud hud, RectTransform parent, Artwork art, float x)
    {
        this.art = art;
        font = hud.m_healthText.font;
        root = ItemUi.Rect(parent, "ActiveEffects");
        left = x;
        ItemUi.TopLeft(root, x, 0, LineWidth, Block);
        more = ItemUi.Text(root, "More", font, 13, ItemUi.Muted);
        more.alignment = TextAlignmentOptions.Center;
        more.textWrappingMode = TextWrappingModes.NoWrap;
        more.enabled = false;
        if (hud.m_statusEffectListRoot) nativeEffects = new HiddenGraphics(hud.m_statusEffectListRoot.gameObject);
        order = Compare;
    }

    // The list follows the rested and power slots when the cards push them aside.
    internal void SetX(float x) => left = x;

    internal void Refresh(Player player, bool inventory)
    {
        effects.Clear();
        player.GetSEMan().GetHUDStatusEffects(effects);
        effects.RemoveAll(effect => !effect || effect.m_hidden || effect.NameHash() == SEMan.s_statusEffectEncumbered
            || RestHud.HasDedicatedSlot(effect.NameHash()));
        for (int i = 0; i < effects.Count; i++) indices[effects[i]] = i;
        effects.Sort(order);
        bool any = effects.Count > 0;
        if (root.gameObject.activeSelf != any) root.gameObject.SetActive(any);
        root.anchoredPosition = new Vector2(left, 0);
        if (!any)
        {
            indices.Clear();
            return;
        }

        int capacity = inventory ? int.MaxValue : PerRow * Rows;
        int shown = Mathf.Min(effects.Count, capacity);
        // The last cell of a full grid says how many more there are.
        if (shown < effects.Count) shown--;
        while (entries.Count < shown) entries.Add(new Entry(root, art, font));
        float now = Time.time;
        for (int i = 0; i < entries.Count; i++)
        {
            if (i >= shown)
            {
                entries[i].Hide();
                continue;
            }
            var effect = effects[i];
            if (inventory)
            {
                int column = i / PerColumn, line = i % PerColumn;
                entries[i].Line(effect, IsHarmful(effect), column * (LineWidth + ColumnGap), line * LineHeight, now);
            }
            else
            {
                int row = i / PerRow, cell = i % PerRow;
                entries[i].Cell(effect, IsHarmful(effect), cell * CellStep, Block - Plate - TimerHeight - row * RowStep, now);
            }
        }
        more.enabled = shown < effects.Count;
        if (more.enabled)
        {
            more.SetText("+{0}", effects.Count - shown);
            int cell = shown % PerRow, row = shown / PerRow;
            ItemUi.TopLeft(more.rectTransform, cell * CellStep, Block - Plate - TimerHeight - row * RowStep, Plate, Plate);
        }
        indices.Clear();
    }

    private readonly Dictionary<StatusEffect, int> indices = new();

    // The effect whose line or plate is under the pointer.
    internal StatusEffect At(Vector2 screen, Camera camera)
    {
        if (!root.gameObject.activeInHierarchy) return null;
        foreach (var entry in entries)
            if (entry.Shown && RectTransformUtility.RectangleContainsScreenPoint(entry.Rect, screen, camera)) return entry.Effect;
        return null;
    }

    // Harmful effects first, then the ones running out soonest; effects without a timer last.
    private int Compare(StatusEffect a, StatusEffect b)
    {
        bool harmA = IsHarmful(a), harmB = IsHarmful(b);
        if (harmA != harmB) return harmA ? -1 : 1;
        float leftA = a.m_ttl > 0 ? a.GetRemaningTime() : float.MaxValue;
        float leftB = b.m_ttl > 0 ? b.GetRemaningTime() : float.MaxValue;
        int byTime = leftA.CompareTo(leftB);
        if (byTime != 0) return byTime;
        indices.TryGetValue(a, out int indexA);
        indices.TryGetValue(b, out int indexB);
        return indexA.CompareTo(indexB);
    }

    private static bool IsHarmful(StatusEffect effect)
    {
        int hash = effect.NameHash();
        return effect.m_flashIcon || hash == SEMan.s_statusEffectWet || hash == SEMan.s_statusEffectCold
            || hash == SEMan.s_statusEffectFreezing
            || hash == SEMan.s_statusEffectBurning || hash == SEMan.s_statusEffectFrost
            || hash == SEMan.s_statusEffectPoison || hash == SEMan.s_statusEffectSmoked
            || hash == SEMan.s_statusEffectTared || hash == SEMan.s_statusEffectSpirit
            || hash == SEMan.s_statusEffectLightning;
    }

    // Running out: the last fifth of its time, at least five and at most thirty seconds.
    private static bool Expiring(StatusEffect effect) =>
        effect.m_ttl > 0 && effect.GetRemaningTime() <= Mathf.Clamp(effect.m_ttl * 0.2f, 5, 30);

    public void Dispose()
    {
        nativeEffects?.Restore();
        ItemUi.Destroy(root);
    }

    private sealed class Entry
    {
        private readonly RectTransform rect;
        internal RectTransform Rect => rect;
        internal StatusEffect Effect => previous;
        internal bool Shown => rect.gameObject.activeSelf && previous;
        private readonly Image back, plate, icon, track, fill;
        private readonly TextMeshProUGUI title, timer;
        private StatusEffect previous;

        internal Entry(RectTransform parent, Artwork art, TMP_FontAsset font)
        {
            rect = ItemUi.Rect(parent, "Effect");
            // With the inventory open each line sits on a dark plate, readable over snow and sky.
            back = ItemUi.Image(rect, "Line", art["slot"]);
            back.color = new Color(1, 1, 1, 0.92f);
            plate = ItemUi.Image(rect, "Plate", art["slot"]);
            icon = ItemUi.Image(plate.transform, "Icon");
            icon.preserveAspect = true;
            ItemUi.Stretch(icon.rectTransform, Vector2.one * 3, Vector2.one * -3);
            track = ItemUi.Image(rect, "Time", art["meter-fill"]);
            track.color = new Color32(12, 16, 18, 220);
            fill = ItemUi.Image(track.transform, "Left", art["meter-fill"]);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            ItemUi.Stretch(fill.rectTransform, Vector2.zero, Vector2.zero);
            title = ItemUi.Text(rect, "Name", font, 14, ItemUi.Ink);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            timer = ItemUi.Text(rect, "Remaining", font, 12, ItemUi.Ink);
            timer.textWrappingMode = TextWrappingModes.NoWrap;
            timer.overflowMode = TextOverflowModes.Overflow;
        }

        internal void Hide()
        {
            if (rect.gameObject.activeSelf) rect.gameObject.SetActive(false);
        }

        // Play: the plate with the timer centred below it.
        internal void Cell(StatusEffect effect, bool harmful, float x, float y, float now)
        {
            Common(effect, harmful, now, out bool expiring, out float share);
            ItemUi.TopLeft(rect, x, y, Plate, Plate + TimerHeight);
            back.enabled = false;
            ItemUi.TopLeft(plate.rectTransform, 0, 0, Plate, Plate);
            ItemUi.TopLeft(track.rectTransform, 4, Plate - 4, Plate - 8, 2);
            track.enabled = fill.enabled = share >= 0;
            title.enabled = false;
            timer.alignment = TextAlignmentOptions.Top;
            timer.fontSize = 12;
            ItemUi.TopLeft(timer.rectTransform, -6, Plate + 1, Plate + 12, TimerHeight);
            Ink(harmful, expiring, now);
        }

        // Inventory: a line with the plate, the name, the timer and a thin bar of time left.
        internal void Line(StatusEffect effect, bool harmful, float x, float y, float now)
        {
            Common(effect, harmful, now, out bool expiring, out float share);
            ItemUi.TopLeft(rect, x, y, LineWidth, LineHeight - 2);
            back.enabled = true;
            ItemUi.TopLeft(back.rectTransform, 0, 0, LineWidth + 4, LineHeight - 2);
            ItemUi.TopLeft(plate.rectTransform, 0, 0, LineHeight - 2, LineHeight - 2);
            title.enabled = true;
            title.color = harmful ? HarmfulName : ItemUi.Ink;
            timer.alignment = TextAlignmentOptions.MidlineRight;
            timer.fontSize = 13;
            float timerWidth = timer.text.Length > 0 ? Mathf.Ceil(timer.GetPreferredValues(timer.text).x) + 2 : 0;
            ItemUi.TopLeft(timer.rectTransform, LineWidth - timerWidth - 4, 0, timerWidth, LineHeight - 5);
            ItemUi.TopLeft(title.rectTransform, LineHeight + 4, 0, LineWidth - LineHeight - 10 - timerWidth, LineHeight - 5);
            ItemUi.TopLeft(track.rectTransform, LineHeight + 4, LineHeight - 6, LineWidth - LineHeight - 8, 2);
            track.enabled = fill.enabled = share >= 0;
            Ink(harmful, expiring, now);
        }

        private void Common(StatusEffect effect, bool harmful, float now, out bool expiring, out float share)
        {
            if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
            if (previous != effect)
            {
                previous = effect;
                title.text = Localization.instance.Localize(effect.m_name);
            }
            if (icon.sprite != effect.m_icon) icon.sprite = effect.m_icon;
            icon.enabled = icon.sprite;
            string time = effect.GetIconText() ?? "";
            if (timer.text != time) timer.text = time;
            timer.enabled = time.Length > 0;
            expiring = Expiring(effect);
            share = effect.m_ttl > 0 ? Mathf.Clamp01(effect.GetRemaningTime() / effect.m_ttl) : -1;
            fill.fillAmount = Mathf.Max(0, share);
            plate.color = harmful ? HarmfulPlate : Color.white;
        }

        private void Ink(bool harmful, bool expiring, float now)
        {
            // Running out: the icon blinks and the timer and its bar turn red.
            float blink = expiring ? 0.5f + 0.5f * Mathf.Cos(now * Mathf.PI * 2 * 1.6f) : 1;
            icon.color = new Color(1, 1, 1, Mathf.Lerp(0.35f, 1, blink));
            timer.color = expiring ? ItemUi.Warning : ItemUi.Ink;
            fill.color = expiring || harmful ? ItemUi.Warning : ItemUi.Accent;
        }
    }
}
