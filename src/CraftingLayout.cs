using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Lays the game's crafting panel out like the other inventory pages: the same plate, a title on
// the left in the carved face above a thin rule, the Craft and Upgrade tabs as buttons on the
// right of that header, the recipe list below on the left and the recipe card beside it.
// Valheim keeps driving every element; only their placement and look change.
internal sealed class CraftingLayout
{
    private const float Side = 16, Header = 16, Rule = 54, BodyTop = 66, ListWidth = 196, Gap = 12;
    private const float TabWidth = 116, TabHeight = 30, Icon = 34;

    private readonly InventoryGui gui;
    private readonly Artwork art;
    private readonly RectTransform panel, title, stationIcon, level;
    private readonly List<(Button Button, TMP_Text Label)> tabs = new();
    private bool upgradeShown, iconShown, placed;

    internal CraftingLayout(InventoryGui gui, MenuStyle style, Artwork art, TMP_FontAsset font)
    {
        this.gui = gui;
        this.art = art;
        panel = gui.m_crafting;
        var brand = HudStyle.BrandFont(font);
        var light = HudStyle.BrandLight(font);

        // The plate fills the page exactly, like the plates of the other pages.
        foreach (Transform child in panel)
        {
            var image = child.GetComponent<Image>();
            if (child.name == "Bkg" && image)
            {
                Remember(style, (RectTransform)child);
                ItemUi.Stretch((RectTransform)child, Vector2.zero, Vector2.zero);
            }
            else if (child.name == "Darken" && image) style.Hide(image);
        }

        var rule = ItemUi.Image(panel, "NewUI_HeaderRule", art["meter-fill"]);
        rule.color = MenuStyle.Edge;
        CraftingMenu.Put(rule.rectTransform, 20, Rule, 20, 2);
        style.Claim(rule);
        style.Remember(() => ItemUi.Destroy(rule));

        var topic = gui.m_craftingStationName ? gui.m_craftingStationName : FindText(panel, "topic");
        if (topic)
        {
            title = topic.rectTransform;
            Remember(style, title);
            title.localScale = Vector3.one;
            style.Font(topic, brand);
            style.Text(topic, ItemUi.Accent, 24);
            Align(style, topic, TextAlignmentOptions.MidlineLeft);
            topic.enableAutoSizing = false;
            topic.overflowMode = TextOverflowModes.Ellipsis;
            style.Claim(topic);
        }

        if (gui.m_craftingStationIcon)
        {
            stationIcon = gui.m_craftingStationIcon.rectTransform;
            Remember(style, stationIcon);
            gui.m_craftingStationIcon.preserveAspect = true;
            style.Claim(gui.m_craftingStationIcon);
        }
        if (gui.m_craftingStationLevelRoot)
        {
            level = gui.m_craftingStationLevelRoot;
            Remember(style, level);
            if (gui.m_craftingStationLevel)
            {
                style.Font(gui.m_craftingStationLevel, brand);
                style.Text(gui.m_craftingStationLevel, ItemUi.Ink, 17);
            }
        }

        foreach (var button in new[] { gui.m_tabCraft, gui.m_tabUpgrade })
        {
            if (!button) continue;
            var rect = (RectTransform)button.transform;
            Remember(style, rect);
            style.Control(button);
            var colors = MenuStyle.Palette;
            colors.disabledColor = Color.white;
            var transition = button.transition;
            var previous = button.colors;
            style.Remember(() =>
            {
                if (!button) return;
                button.transition = transition;
                button.colors = previous;
            });
            button.transition = Selectable.Transition.ColorTint;
            button.colors = colors;
            var selected = button.transform.Find("Selected");
            if (selected) style.Hide(selected.gameObject);
            var label = button.transform.Find("Text")?.GetComponent<TMP_Text>();
            if (label)
            {
                style.Font(label, light);
                style.Text(label, ItemUi.Ink, 16);
                label.enableAutoSizing = false;
                style.Claim(label);
            }
            if (button.targetGraphic is Image face) style.Claim(face);
            tabs.Add((button, label));
        }
        // The game's own rule under the tabs gives way to the header rule.
        var border = panel.Find("TabsButtons/TabBorder")?.GetComponent<Image>();
        if (border) style.Hide(border);

        var list = gui.m_recipeListRoot ? FindAncestor(gui.m_recipeListRoot, "RecipeList") : null;
        if (list)
        {
            Remember(style, list);
            list.anchorMin = list.anchorMax = list.pivot = Vector2.up;
            list.anchoredPosition = new Vector2(Side, -BodyTop);
            list.sizeDelta = new Vector2(ListWidth, panel.rect.height - BodyTop - Side);
        }
        var card = gui.m_recipeDecription ? (RectTransform)gui.m_recipeDecription.transform.parent : null;
        if (card)
        {
            Remember(style, card);
            card.anchorMin = card.anchorMax = card.pivot = Vector2.one;
            card.anchoredPosition = new Vector2(-Side, -BodyTop);
            card.sizeDelta = new Vector2(panel.rect.width - Side * 2 - ListWidth - Gap, panel.rect.height - BodyTop - Side);
        }
        Refresh();
    }

    // The header follows what the station offers: its icon and level, and the Upgrade tab.
    internal void Refresh()
    {
        bool upgrade = gui.m_tabUpgrade && gui.m_tabUpgrade.gameObject.activeSelf;
        bool icon = stationIcon && stationIcon.gameObject.activeSelf;
        if (!placed || upgrade != upgradeShown || icon != iconShown)
        {
            placed = true;
            upgradeShown = upgrade;
            iconShown = icon;
            Place();
        }
        foreach (var (button, label) in tabs)
        {
            if (!button || !button.gameObject.activeSelf) continue;
            // The open tab is the one the game made unclickable.
            bool current = !button.interactable;
            if (button.targetGraphic is Image face)
            {
                var sprite = art[current ? "selected" : "button"];
                if (face.sprite != sprite) face.sprite = sprite;
            }
            if (label) label.color = current ? ItemUi.Accent : ItemUi.Ink;
        }
    }

    private void Place()
    {
        float right = 20;
        // Right to left: the Upgrade tab, the Craft tab, then the station level.
        if (upgradeShown && gui.m_tabUpgrade) right = PlaceRight((RectTransform)gui.m_tabUpgrade.transform, right, TabWidth, TabHeight);
        if (gui.m_tabCraft) right = PlaceRight((RectTransform)gui.m_tabCraft.transform, right, TabWidth, TabHeight);
        if (level && level.gameObject.activeSelf) right = PlaceRight(level, right, Icon, Icon);
        float left = 20;
        if (iconShown)
        {
            ItemUi.TopLeft(stationIcon, 18, Header - 4, Icon + 4, Icon + 4);
            left = 18 + Icon + 12;
        }
        if (title) CraftingMenu.Put(title, left, Header, right + 8, 30);
    }

    private float PlaceRight(RectTransform rect, float right, float width, float height)
    {
        // A tab sits inside the game's own tab group, which is placed elsewhere; anchor it to the
        // panel's corner through that group's offset.
        var parent = (RectTransform)rect.parent;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(width, height);
        Vector2 corner = new(panel.rect.xMax - right, panel.rect.yMax - Header + 2 - (TabHeight - height) * 0.5f);
        Vector2 local = parent.InverseTransformPoint(panel.TransformPoint(corner));
        Vector2 parentCorner = new(parent.rect.xMax, parent.rect.yMax);
        rect.anchoredPosition = local - parentCorner;
        return right + width + 6;
    }

    private static void Remember(MenuStyle style, RectTransform rect) => style.Remember(new MenuRect(rect).Restore);

    private static void Align(MenuStyle style, TMP_Text text, TextAlignmentOptions alignment)
    {
        var previous = (text.alignment, text.enableAutoSizing, text.overflowMode);
        style.Remember(() =>
        {
            if (!text) return;
            text.alignment = previous.alignment;
            text.enableAutoSizing = previous.enableAutoSizing;
            text.overflowMode = previous.overflowMode;
        });
        text.alignment = alignment;
    }

    private static TMP_Text FindText(Transform root, string name) => root.Find(name)?.GetComponent<TMP_Text>();

    private static RectTransform FindAncestor(Transform child, string name)
    {
        for (var node = child; node; node = node.parent)
            if (node.name == name) return node as RectTransform;
        return null;
    }
}
