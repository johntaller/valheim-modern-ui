using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Item = ItemDrop.ItemData;
using Image = UnityEngine.UI.Image;

namespace ValheimNewUI;

internal sealed class EquipmentPanel
{
    internal const float Width = 244;
    internal const float Gap = 10;
    private const float Padding = 14, CellWidth = 66, CellHeight = 56, EffectsTop = 242;
    private const float ContentWidth = Width - Padding * 2;
    private const float EffectsWidth = ContentWidth - 9;
    private static readonly string[] Labels = { "Helmet", "Chest", "Legs", "Cape", "Accessory", "Trinket" };
    private static readonly string[] RussianLabels = { "Шлем", "Доспех", "Поножи", "Плащ", "Аксессуар", "Оберег" };
    private static readonly HitData.DamageType[] DamageTypes =
    {
        HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Chop,
        HitData.DamageType.Pickaxe, HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning,
        HitData.DamageType.Poison, HitData.DamageType.Spirit
    };

    internal readonly RectTransform Rect;
    private readonly RectTransform cells;
    private readonly RectTransform viewport, content;
    private readonly Image effectsSurface;
    private readonly ScrollRect scroll;
    private readonly Scrollbar effectsScrollbar;
    private readonly TextMeshProUGUI total, effects;
    private readonly TextMeshProUGUI[] ratings = new TextMeshProUGUI[EquipmentInventory.SlotCount];
    private readonly bool russian;
    private readonly HashSet<StatusEffect> displayedEffects = new();
    private readonly List<HitData.DamageModPair> damageLines = new();
    private readonly StringBuilder effectNames = new();
    private readonly StringBuilder summary = new();
    private float nextStats, lastArmor = float.NaN;

    internal EquipmentPanel(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        russian = Localization.instance.GetSelectedLanguage() == "Russian";
        Rect = ItemUi.Image(parent, "NewUI_Equipment", art["panel"]).rectTransform;
        var title = Label(Rect, "Title", font, 18, ItemUi.Ink, russian ? "Экипировка" : "Equipment");
        title.fontStyle = FontStyles.Bold;
        ItemUi.TopLeft(title.rectTransform, Padding, 10, ContentWidth, 25);
        cells = ItemUi.Rect(Rect, "Cells");
        ItemUi.Stretch(cells, Vector2.zero, Vector2.zero);
        for (int i = 0; i < ratings.Length; i++)
        {
            Vector2 placement = CellPosition(i);
            var label = Label(Rect, "SlotLabel" + i, font, 13, ItemUi.Muted, (russian ? RussianLabels : Labels)[i]);
            label.alignment = TextAlignmentOptions.Center;
            ItemUi.TopLeft(label.rectTransform, placement.x, placement.y + CellHeight + 1, CellWidth, 19);
            var rating = Label(Rect, "ArmorValue" + i, font, 13, ItemUi.Accent, "");
            rating.alignment = TextAlignmentOptions.Center;
            rating.fontStyle = FontStyles.Bold;
            var chip = ItemUi.Image(Rect, "ArmorBadge" + i, art["item-card"]);
            chip.color = new Color32(221, 209, 186, 255);
            ItemUi.TopLeft(chip.rectTransform, placement.x + CellWidth - 33, placement.y + 4, 29, 19);
            rating.rectTransform.SetParent(chip.rectTransform, false);
            ItemUi.Stretch(rating.rectTransform, Vector2.zero, Vector2.zero);
            ratings[i] = rating;
        }

        var rule = ItemUi.Image(Rect, "Divider");
        rule.color = new Color32(192, 157, 97, 110);
        ItemUi.TopLeft(rule.rectTransform, Padding, 194, ContentWidth, 1);
        var armor = Label(Rect, "TotalArmorLabel", font, 15, ItemUi.Muted,
            (russian ? "Всего брони" : "Total armor"));
        ItemUi.TopLeft(armor.rectTransform, Padding, 198, ContentWidth - 50, 23);
        total = Label(Rect, "TotalArmor", font, 19, ItemUi.Accent, "0");
        total.alignment = TextAlignmentOptions.MidlineRight;
        total.fontStyle = FontStyles.Bold;
        ItemUi.TopLeft(total.rectTransform, Width - Padding - 60, 198, 60, 23);
        var effectsTitle = Label(Rect, "EffectsLabel", font, 14, ItemUi.Ink,
            russian ? "Эффекты экипировки" : "Equipment effects");
        effectsTitle.fontStyle = FontStyles.Bold;
        ItemUi.TopLeft(effectsTitle.rectTransform, Padding, 224, ContentWidth, 18);

        effectsSurface = ItemUi.Image(Rect, "EffectsViewport");
        effectsSurface.color = Color.clear;
        viewport = effectsSurface.rectTransform;
        viewport.gameObject.AddComponent<RectMask2D>();
        content = ItemUi.Rect(viewport, "EffectsContent");
        effects = Label(content, "Effects", font, 14, ItemUi.Muted, "");
        effects.alignment = TextAlignmentOptions.TopLeft;
        effects.textWrappingMode = TextWrappingModes.Normal;
        ItemUi.Stretch(effects.rectTransform, Vector2.zero, Vector2.zero);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25;

        var rail = ItemUi.Image(Rect, "EffectsScrollbar", art["slot"]);
        rail.raycastTarget = true;
        var slider = ItemUi.Rect(rail.transform, "SlidingArea");
        ItemUi.Stretch(slider, Vector2.zero, Vector2.zero);
        var handle = ItemUi.Image(slider, "Handle", art["meter-fill"]);
        handle.color = ItemUi.Accent;
        handle.raycastTarget = true;
        ItemUi.Stretch(handle.rectTransform, Vector2.zero, Vector2.zero);
        effectsScrollbar = rail.gameObject.AddComponent<Scrollbar>();
        effectsScrollbar.handleRect = handle.rectTransform;
        effectsScrollbar.targetGraphic = handle;
        effectsScrollbar.direction = Scrollbar.Direction.BottomToTop;
        var navigation = effectsScrollbar.navigation;
        navigation.mode = Navigation.Mode.None;
        effectsScrollbar.navigation = navigation;
        scroll.verticalScrollbar = effectsScrollbar;
        Rect.gameObject.SetActive(false);
    }

    internal void AttachCell(RectTransform cell, int index)
    {
        cell.SetParent(cells, false);
        Vector2 placement = index < ratings.Length ? CellPosition(index) : new Vector2(-10000, 0);
        ItemUi.TopLeft(cell, placement.x, placement.y, CellWidth, CellHeight);
    }

    private static Vector2 CellPosition(int index) => new(Padding + (CellWidth + 9) * (index % 3), 39 + 77 * (index / 3));

    private static TextMeshProUGUI Label(RectTransform parent, string name, TMP_FontAsset font, int size, Color color, string text)
    {
        var label = ItemUi.Text(parent, name, font, size, color);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.text = text;
        return label;
    }

    internal void Align(RectTransform backpack, float height)
    {
        float panelHeight = Mathf.Max(height, 322);
        ItemUi.TopLeft(Rect, backpack.anchoredPosition.x + backpack.rect.width + Gap,
            -backpack.anchoredPosition.y, Width, panelHeight);
        float effectsHeight = panelHeight - EffectsTop - Padding;
        ItemUi.TopLeft(viewport, Padding, EffectsTop, EffectsWidth, effectsHeight);
        ItemUi.TopLeft((RectTransform)effectsScrollbar.transform, Width - Padding - 5, EffectsTop, 5, effectsHeight);
        SizeEffects();
    }

    internal void Refresh(Player player, int row)
    {
        if (!Rect.gameObject.activeSelf)
        {
            Rect.gameObject.SetActive(true);
            nextStats = 0;
        }

        var inventory = player.GetInventory();
        for (int i = 0; i < ratings.Length; i++)
        {
            var item = inventory.GetItemAt(i, row);
            var badge = ratings[i].transform.parent.gameObject;
            bool show = item?.m_equipped == true;
            if (badge.activeSelf != show) badge.SetActive(show);
            if (!show) continue;
            string armor = (i < 4 ? item.GetArmor() : 0).ToString("0.#");
            if (ratings[i].text != armor) ratings[i].text = armor;
        }

        float bodyArmor = player.GetBodyArmor();
        if (lastArmor != bodyArmor)
        {
            lastArmor = bodyArmor;
            total.text = bodyArmor.ToString("0.#");
        }
        if (Time.unscaledTime < nextStats) return;
        nextStats = Time.unscaledTime + 0.2f;

        var damage = new HitData.DamageModifiers();
        displayedEffects.Clear();
        effectNames.Clear();
        summary.Clear();
        damageLines.Clear();
        float speed = 0, eitr = 0;
        // Valheim applies chest and legs before helmet and cape.
        for (int i = 0; i < ratings.Length; i++)
        {
            int slot = i < 3 ? (i + 1) % 3 : i;
            var item = inventory.GetItemAt(slot, row);
            if (item?.m_equipped != true) continue;
            var data = item.m_shared;
            if (slot < 4) damage.Apply(data.m_damageModifiers);
            speed += data.m_movementModifier;
            eitr += data.m_eitrRegenModifier;
            AddEffect(player, data.m_equipStatusEffect);
            AddEffect(player, data.m_setStatusEffect);
            if (data.m_fullAdrenalineSE)
                AddLine(effectNames, "$item_fulladrenaline: " + data.m_fullAdrenalineSE.m_name);
        }

        foreach (var effect in displayedEffects)
            if (effect is SE_Stats stats) damage.Apply(stats.m_mods);
        foreach (var type in DamageTypes)
            damageLines.Add(new HitData.DamageModPair { m_type = type, m_modifier = damage.GetModifier(type) });
        string resistances = SE_Stats.GetDamageModifiersTooltipString(damageLines).Trim();
        if (resistances.Length > 0)
            AddLine(summary, resistances.Replace("$inventory_dmgmod: ", "").Replace(" VS ", " · ").Replace("color=orange", "color=#E1BB74"));
        if (effectNames.Length > 0) AddLine(summary, effectNames.ToString());
        if (speed != 0) AddLine(summary, $"$item_movement_modifier {speed:+0%;-0%}");
        if (eitr != 0) AddLine(summary, $"$item_eitrregen_modifier {eitr:+0%;-0%}");

        string text = summary.Length == 0 ? (russian ? "Нет активных эффектов" : "No active effects")
            : Localization.instance.Localize(summary.ToString());
        if (effects.text == text) return;
        effects.text = text;
        SizeEffects();
        scroll.verticalNormalizedPosition = 1;
    }

    private void AddEffect(Player player, StatusEffect effect)
    {
        if (!effect || !player.GetSEMan().GetStatusEffect(effect.NameHash()) || !displayedEffects.Add(effect)) return;
        AddLine(effectNames, effect.m_name);
    }

    private static void AddLine(StringBuilder text, string line)
    {
        if (text.Length > 0) text.AppendLine();
        text.Append(line);
    }

    private void SizeEffects()
    {
        float height = effects.GetPreferredValues(effects.text, EffectsWidth, Mathf.Infinity).y + 2;
        ItemUi.TopLeft(content, 0, 0, EffectsWidth, Mathf.Max(height, viewport.rect.height));
        scroll.vertical = height > viewport.rect.height + 1;
        if (effectsScrollbar.gameObject.activeSelf != scroll.vertical)
            effectsScrollbar.gameObject.SetActive(scroll.vertical);
        effectsSurface.raycastTarget = scroll.vertical;
    }
}
