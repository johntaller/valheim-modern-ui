using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace ValheimNewUI;

internal sealed class EquipmentPanel
{
    internal const float Width = 244;
    internal const float Gap = 10;
    private const float Padding = 14, CellSize = 52, ColumnWidth = 104, ModifiersTop = 364;
    private const float ContentWidth = Width - Padding * 2;
    private const float ModifiersWidth = ContentWidth - 9;
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
    private readonly RectTransform modifiersPanel, viewport, content;
    private readonly Image modifiersSurface;
    private readonly ScrollRect scroll;
    private readonly Scrollbar modifiersScrollbar;
    private readonly TextMeshProUGUI total, modifiers;
    private readonly List<HitData.DamageModPair> damageLines = new();
    private float nextStats, lastArmor = float.NaN, backpackHeight;

    internal EquipmentPanel(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        bool russian = Localization.instance.GetSelectedLanguage() == "Russian";
        Rect = ItemUi.Image(parent, "NewUI_Equipment", art["panel"]).rectTransform;
        var title = Label(Rect, "Title", font, 18, ItemUi.Ink, russian ? "Экипировка" : "Equipment");
        title.fontStyle = FontStyles.Bold;
        ItemUi.TopLeft(title.rectTransform, Padding, 10, ContentWidth, 25);
        cells = ItemUi.Rect(Rect, "Cells");
        ItemUi.Stretch(cells, Vector2.zero, Vector2.zero);
        for (int i = 0; i < EquipmentInventory.SlotCount; i++)
        {
            Vector2 placement = CellPosition(i);
            var label = Label(Rect, "SlotLabel" + i, font, 13, ItemUi.Muted, (russian ? RussianLabels : Labels)[i]);
            label.alignment = TextAlignmentOptions.Center;
            ItemUi.TopLeft(label.rectTransform, placement.x - (ColumnWidth - CellSize) / 2,
                placement.y + CellSize, ColumnWidth, 17);
        }

        var rule = ItemUi.Image(Rect, "Divider");
        rule.color = new Color32(192, 157, 97, 110);
        ItemUi.TopLeft(rule.rectTransform, Padding, 316, ContentWidth, 1);
        var armor = Label(Rect, "TotalArmorLabel", font, 15, ItemUi.Muted,
            (russian ? "Всего брони" : "Total armor"));
        ItemUi.TopLeft(armor.rectTransform, Padding, 321, ContentWidth - 50, 23);
        total = Label(Rect, "TotalArmor", font, 19, ItemUi.Accent, "0");
        total.alignment = TextAlignmentOptions.MidlineRight;
        total.fontStyle = FontStyles.Bold;
        ItemUi.TopLeft(total.rectTransform, Width - Padding - 60, 321, 60, 23);
        modifiersPanel = ItemUi.Rect(Rect, "DamageModifiers");
        ItemUi.Stretch(modifiersPanel, Vector2.zero, Vector2.zero);
        var modifiersTitle = Label(modifiersPanel, "ModifiersLabel", font, 14, ItemUi.Ink,
            russian ? "Модификаторы урона" : "Damage modifiers");
        modifiersTitle.fontStyle = FontStyles.Bold;
        ItemUi.TopLeft(modifiersTitle.rectTransform, Padding, 345, ContentWidth, 18);

        modifiersSurface = ItemUi.Image(modifiersPanel, "ModifiersViewport");
        modifiersSurface.color = Color.clear;
        viewport = modifiersSurface.rectTransform;
        viewport.gameObject.AddComponent<RectMask2D>();
        content = ItemUi.Rect(viewport, "ModifiersContent");
        modifiers = Label(content, "Modifiers", font, 14, ItemUi.Muted, "");
        modifiers.alignment = TextAlignmentOptions.TopLeft;
        modifiers.textWrappingMode = TextWrappingModes.Normal;
        ItemUi.Stretch(modifiers.rectTransform, Vector2.zero, Vector2.zero);
        scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25;

        modifiersScrollbar = HudStyle.Scrollbar(modifiersPanel, art, "ModifiersScrollbar");
        scroll.verticalScrollbar = modifiersScrollbar;
        modifiersPanel.gameObject.SetActive(false);
        Rect.gameObject.SetActive(false);
    }

    internal void AttachCell(RectTransform cell, int index)
    {
        cell.SetParent(cells, false);
        Vector2 placement = index < EquipmentInventory.SlotCount ? CellPosition(index) : new Vector2(-10000, 0);
        cell.localScale = Vector3.one;
        ItemUi.TopLeft(cell, placement.x, placement.y, CellSize, CellSize);
    }

    private static Vector2 CellPosition(int index)
    {
        int column = index / 4;
        return new Vector2(Padding + (ColumnWidth - CellSize) / 2 + column * (ContentWidth - ColumnWidth),
            38 + 69 * (index % 4));
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, TMP_FontAsset font, int size, Color color, string text)
    {
        var label = ItemUi.Text(parent, name, font, size, color);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.text = text;
        return label;
    }

    internal void Align(RectTransform backpack, float height)
    {
        ItemUi.TopLeft(Rect, backpack.anchoredPosition.x + backpack.rect.width + Gap,
            -backpack.anchoredPosition.y, Width, Mathf.Max(height, 355));
        backpackHeight = height;
        ResizeModifiers(height);
    }

    private void ResizeModifiers(float backpackHeight)
    {
        bool show = modifiersPanel.gameObject.activeSelf;
        float panelHeight = Mathf.Max(backpackHeight, show ? 419 : 355);
        Rect.sizeDelta = new Vector2(Width, panelHeight);
        if (!show) return;
        float modifiersHeight = panelHeight - ModifiersTop - Padding;
        ItemUi.TopLeft(viewport, Padding, ModifiersTop, ModifiersWidth, modifiersHeight);
        ItemUi.TopLeft((RectTransform)modifiersScrollbar.transform, Width - Padding - 5, ModifiersTop, 5, modifiersHeight);
        SizeModifiers();
    }

    internal void Refresh(Player player, int row)
    {
        if (!Rect.gameObject.activeSelf)
        {
            Rect.gameObject.SetActive(true);
            nextStats = 0;
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
        var inventory = player.GetInventory();
        damageLines.Clear();
        // Match the order Valheim uses for armor when two items modify the same damage type.
        for (int i = 0; i < EquipmentInventory.SlotCount; i++)
        {
            int slot = i < 3 ? (i + 1) % 3 : i;
            var item = inventory.GetItemAt(slot, row);
            if (item?.m_equipped == true) damage.Apply(item.m_shared.m_damageModifiers);
        }
        foreach (var type in DamageTypes)
            damageLines.Add(new HitData.DamageModPair { m_type = type, m_modifier = damage.GetModifier(type) });
        string text = SE_Stats.GetDamageModifiersTooltipString(damageLines).Trim();
        bool show = text.Length > 0;
        if (modifiersPanel.gameObject.activeSelf != show)
        {
            modifiersPanel.gameObject.SetActive(show);
            ResizeModifiers(backpackHeight);
        }
        if (!show) return;
        text = Localization.instance.Localize(text.Replace("$inventory_dmgmod: ", "")
            .Replace(" VS ", " · ").Replace("color=orange", "color=#E1BB74"));
        if (modifiers.text == text) return;
        modifiers.text = text;
        SizeModifiers();
        scroll.verticalNormalizedPosition = 1;
    }

    private void SizeModifiers()
    {
        float height = modifiers.GetPreferredValues(modifiers.text, ModifiersWidth, Mathf.Infinity).y + 2;
        ItemUi.TopLeft(content, 0, 0, ModifiersWidth, Mathf.Max(height, viewport.rect.height));
        scroll.vertical = height > viewport.rect.height + 1;
        if (modifiersScrollbar.gameObject.activeSelf != scroll.vertical)
            modifiersScrollbar.gameObject.SetActive(scroll.vertical);
        modifiersSurface.raycastTarget = scroll.vertical;
    }
}
