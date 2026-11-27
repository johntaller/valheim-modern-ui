using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Input = TMPro.TMP_InputField;
using Type = ItemDrop.ItemData.ItemType;

namespace ValheimNewUI;

internal sealed class CraftingMenu : IDisposable
{
    private enum Page { Craft, Character, Skills, Lore, Trophies, Achievements, Players }
    private enum Category { All, Weapons, Armor, Tools, Food, Materials, Other }
    private static readonly Action<InventoryGui, bool> RebuildRecipes =
        AccessTools.MethodDelegate<Action<InventoryGui, bool>>(AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));
    private static readonly AccessTools.FieldRef<InventoryGui, float> RecipeBaseHeight =
        AccessTools.FieldRefAccess<InventoryGui, float>("m_recipeListBaseSize");
    private static CraftingMenu current;

    internal readonly InventoryGui Gui;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly MenuStyle style;
    private readonly RectTransform tabs, content, filters;
    private readonly CanvasGroup tabsGroup;
    private readonly Input search;
    private readonly RectTransform choices;
    private readonly ScrollRect recipeScroll;
    private readonly CanvasGroup scrollbarGroup;
    private readonly RectTransform scrollViewport;
    private readonly TextMeshProUGUI categoryLabel, empty, title, identity;
    private readonly Button categoryButton;
    private readonly CharacterPage sheet;
    private readonly PlayersPage playersPage;
    private readonly InlinePage[] pages;
    private readonly RecipeCard card;
    private readonly CraftingLayout layout;
    private readonly Button[] buttons;
    private readonly List<RectTransform> recipeRows = new();
    private readonly float spacing;
    private readonly float recipeBaseHeight;
    private readonly GameObject originalDefault;
    private Page selected;
    private Category category;
    private string query = "";
    private int count = -1;
    private bool wasOpen, pendingFilter;
    private float nextStyleScan;

    internal CraftingMenu(InventoryGui gui, Artwork art, TMP_FontAsset font, OnlinePlayers players, RecipePins pins)
    {
        Gui = gui;
        this.art = art;
        this.font = font;
        style = new MenuStyle(art);
        spacing = gui.m_recipeListSpace;
        recipeBaseHeight = RecipeBaseHeight(gui);
        gui.m_recipeListSpace = Mathf.Max(36, spacing);
        recipeScroll = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>(true);
        layout = new CraftingLayout(gui, style, art, font);
        if (gui.m_recipeDecription && gui.m_recipeName) card = new RecipeCard(gui, style, art, font, pins);

        var craft = gui.m_crafting;
        content = ItemUi.Rect(craft.parent, "NewUI_InventoryPage");
        new MenuRect(craft).ApplyTo(content);
        content.SetSiblingIndex(craft.GetSiblingIndex() + 1);
        var backdrop = ItemUi.Image(content, "Background", art["panel"]);
        backdrop.raycastTarget = true;
        ItemUi.Stretch(backdrop.rectTransform, Vector2.zero, Vector2.zero);
        title = MenuStyle.Label(content, "Title", HudStyle.BrandFont(font), "", 24, ItemUi.Accent);
        title.richText = false;
        Put(title.rectTransform, 20, 16, 20, 30);
        var line = ItemUi.Image(content, "HeaderDivider", art["meter-fill"]);
        line.color = MenuStyle.Edge;
        Put(line.rectTransform, 20, 54, 20, 2);

        var body = ItemUi.Rect(content, "Body");
        ItemUi.Stretch(body, new Vector2(16, 14), new Vector2(-16, -65));
        sheet = new CharacterPage(body, art, font);
        playersPage = new PlayersPage(body, players, art, font);
        pages = new InlinePage[7];
        pages[(int)Page.Skills] = new InlinePage(gui.m_skillsDialog.transform, body, style, PageTitle(Page.Skills));
        pages[(int)Page.Lore] = new InlinePage(gui.m_textsDialog.transform, body, style, PageTitle(Page.Lore),
            MenuStyle.Translate("Valheim compendium", "Компендиум Valheim"), MenuStyle.Translate("Texts", "Тексты"));
        pages[(int)Page.Trophies] = new InlinePage(gui.m_trophiesPanel.transform, body, style, PageTitle(Page.Trophies));
        if (gui.m_achievementsPanel)
            pages[(int)Page.Achievements] = new InlinePage(gui.m_achievementsPanel.transform, body, style,
                PageTitle(Page.Achievements));

        // Preserve the real PvP toggle and its native restrictions.
        if (gui.m_pvp) sheet.AttachPvp(gui.m_pvp, style, art, font);

        tabs = ItemUi.Rect(gui.m_info, "NewUI_InventoryTabs");
        tabsGroup = tabs.gameObject.AddComponent<CanvasGroup>();
        tabsGroup.ignoreParentGroups = true;
        ItemUi.Stretch(tabs, Vector2.zero, Vector2.zero);
        foreach (Transform child in gui.m_info)
            if (child != tabs) style.Hide(child.gameObject);
        var tabBg = ItemUi.Image(tabs, "Background", art["panel"]);
        ItemUi.Stretch(tabBg.rectTransform, Vector2.zero, Vector2.zero);
        identity = MenuStyle.Label(tabs, "PlayerName", HudStyle.BrandFont(font), "", 21, ItemUi.Accent);
        identity.richText = false;
        Put(identity.rectTransform, 17, 9, 17, 27);

        buttons = new Button[7];
        var nav = ItemUi.Rect(tabs, "Tabs");
        ItemUi.Stretch(nav, new Vector2(15, 13), new Vector2(-15, -43));
        nav.gameObject.AddComponent<VerticalLayoutGroup>().spacing = 6;
        var rows = new RectTransform[2];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = ItemUi.Rect(nav, "TabRow");
            var layout = rows[i].gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
        }
        for (int i = 0; i < buttons.Length; i++)
        {
            var page = (Page)i;
            buttons[i] = MenuStyle.Button(rows[i / 4], art, font, PageTitle(page), () => Open(page), out _);
            if (page == Page.Achievements && !gui.m_achievementsPanel) buttons[i].interactable = false;
        }
        originalDefault = gui.m_uiGroups.Length > 2 && gui.m_uiGroups[2] ? gui.m_uiGroups[2].m_defaultElement : null;
        if (gui.m_uiGroups.Length > 2 && gui.m_uiGroups[2]) gui.m_uiGroups[2].m_defaultElement = buttons[0].gameObject;

        content.gameObject.SetActive(false);

        scrollViewport = recipeScroll ? (RectTransform)recipeScroll.transform : (RectTransform)gui.m_recipeListRoot.parent;
        filters = ItemUi.Rect(scrollViewport.parent, "NewUI_RecipeFilters");
        new MenuRect(scrollViewport).ApplyTo(filters);
        style.Remember(new MenuRect(scrollViewport).Restore);
        scrollViewport.offsetMax -= new Vector2(0, 77);
        if (gui.m_recipeListScroll)
        {
            var bar = gui.m_recipeListScroll;
            scrollbarGroup = bar.GetComponent<CanvasGroup>();
            if (scrollbarGroup) style.Remember(new ScrollbarState(scrollbarGroup).Restore);
            else
            {
                scrollbarGroup = bar.gameObject.AddComponent<CanvasGroup>();
                style.Remember(() => { if (scrollbarGroup) UnityEngine.Object.Destroy(scrollbarGroup); });
            }
            if (bar.targetGraphic is Image grip) style.Sprite(grip, "meter-fill", ItemUi.Muted, force: true);
            if (bar.GetComponent<Image>() is Image track && track != bar.targetGraphic)
                style.Sprite(track, "meter-fill", MenuStyle.Surface, force: true);
            var barRect = (RectTransform)bar.transform;
            style.Remember(new MenuRect(barRect).Restore);
            barRect.offsetMin += new Vector2(barRect.rect.width * 0.26f, 0);
            barRect.offsetMax -= new Vector2(barRect.rect.width * 0.26f, 77);
        }
        search = MakeSearch(filters);
        categoryButton = MenuStyle.Button(filters, art, font, CategoryTitle(Category.All), ShowCategories, out categoryLabel);
        Put((RectTransform)categoryButton.transform, 0, 37, 0, 29);
        categoryLabel.alignment = TextAlignmentOptions.MidlineLeft;
        ItemUi.Stretch(categoryLabel.rectTransform, new Vector2(10, 0), new Vector2(-21, 0));
        var chevron = MenuStyle.Label(categoryButton.transform, "Chevron", font, "▼", 11, ItemUi.Accent, TextAlignmentOptions.MidlineRight);
        ItemUi.Stretch(chevron.rectTransform, Vector2.zero, new Vector2(-9, 0));
        empty = MenuStyle.Label(filters, "EmptyRecipes", font, "", 14, ItemUi.Muted, TextAlignmentOptions.Center);
        empty.textWrappingMode = TextWrappingModes.Normal;
        Put(empty.rectTransform, 9, 100, 9, 52);
        choices = MakeCategories();
        current = this;
    }

    private Input MakeSearch(RectTransform parent)
    {
        var image = ItemUi.Image(parent, "RecipeSearch", art["slot"]);
        image.raycastTarget = true;
        Put(image.rectTransform, 0, 1, 0, 31);
        var input = image.gameObject.AddComponent<Input>();
        input.targetGraphic = image;
        input.colors = MenuStyle.Palette;
        input.lineType = Input.LineType.SingleLine;
        input.characterLimit = 80;
        var textArea = ItemUi.Rect(image.transform, "TextArea");
        ItemUi.Stretch(textArea, new Vector2(10, 1), new Vector2(-10, -1));
        textArea.gameObject.AddComponent<RectMask2D>();
        var text = MenuStyle.Label(textArea, "Text", font, "", 15, ItemUi.Ink);
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        var hint = MenuStyle.Label(textArea, "Placeholder", font,
            MenuStyle.Translate("Search recipes", "Поиск рецептов"), 14, ItemUi.Muted);
        ItemUi.Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        ItemUi.Stretch(hint.rectTransform, Vector2.zero, Vector2.zero);
        input.textViewport = textArea;
        input.textComponent = text;
        input.placeholder = hint;
        input.onSelect.AddListener(_ => choices.gameObject.SetActive(false));
        input.onValueChanged.AddListener(value =>
        {
            query = value.Trim();
            pendingFilter = true;
        });
        input.onSubmit.AddListener(_ => categoryButton.Select());
        return input;
    }

    private RectTransform MakeCategories()
    {
        var background = ItemUi.Image(filters, "RecipeCategories", art["panel"]);
        background.raycastTarget = true;
        var panel = background.rectTransform;
        const float height = 26;
        var categories = (Category[])Enum.GetValues(typeof(Category));
        Put(panel, 0, 70, 0, categories.Length * (height + 3) + 9);
        for (int i = 0; i < categories.Length; i++)
        {
            Category value = categories[i];
            var button = MenuStyle.Button(panel, art, font, CategoryTitle(value), () =>
            {
                category = value;
                categoryLabel.text = CategoryTitle(value);
                panel.gameObject.SetActive(false);
                categoryButton.Select();
                pendingFilter = true;
            }, out var label, 14);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            ItemUi.Stretch(label.rectTransform, new Vector2(9, 0), new Vector2(-5, 0));
            Put((RectTransform)button.transform, 5, i * (height + 3) + 5, 5, height);
        }
        panel.gameObject.SetActive(false);
        return panel;
    }

    private void ShowCategories()
    {
        choices.gameObject.SetActive(!choices.gameObject.activeSelf);
        if (choices.gameObject.activeSelf && ZInput.IsGamepadActive())
            choices.GetChild((int)category).GetComponent<Button>().Select();
    }

    private void Open(Page page)
    {
        if (selected == page) return;
        search.DeactivateInputField();
        choices.gameObject.SetActive(false);
        CloseNativePage();
        selected = page;
        ApplyPage();
    }

    private void ApplyPage()
    {
        bool crafting = selected == Page.Craft;
        var player = Player.m_localPlayer;
        Gui.m_crafting.gameObject.SetActive(crafting);
        content.gameObject.SetActive(!crafting);
        sheet.Root.gameObject.SetActive(selected == Page.Character);
        playersPage.Root.gameObject.SetActive(selected == Page.Players);
        title.text = PageTitle(selected);
        for (int i = 0; i < buttons.Length; i++)
            ((Image)buttons[i].targetGraphic).sprite = art[(Page)i == selected ? "selected" : "button"];
        if (crafting || !player) return;

        switch (selected)
        {
            case Page.Skills: Gui.OnOpenSkills(); break;
            case Page.Lore: Gui.OnOpenTexts(); break;
            case Page.Trophies: Gui.OnOpenTrophies(); break;
            case Page.Achievements: Gui.OnOpenAchievements(); break;
        }
        pages[(int)selected]?.Fit();
    }

    private void CloseNativePage()
    {
        switch (selected)
        {
            case Page.Skills: Gui.m_skillsDialog.gameObject.SetActive(false); break;
            case Page.Lore: Gui.m_textsDialog.gameObject.SetActive(false); break;
            case Page.Trophies: Gui.OnCloseTrophies(); break;
            case Page.Achievements: Gui.OnCloseAchievements(); break;
        }
    }

    internal void Refresh(Player player)
    {
        bool open = InventoryGui.IsVisible() && player && !player.IsDead() &&
            Gui.GetComponent<Animator>().GetBool("visible");
        if (tabs.gameObject.activeSelf != open) tabs.gameObject.SetActive(open);
        if (!open)
        {
            if (wasOpen) Open(Page.Craft);
            wasOpen = false;
            return;
        }
        tabsGroup.interactable = tabsGroup.blocksRaycasts = !Menu.IsVisible() && !Console.IsVisible() &&
            !Gui.m_variantDialog.gameObject.activeSelf && !Gui.m_splitDialog.IsActive;
        if (!wasOpen)
        {
            wasOpen = true;
            pendingFilter = true;
            ApplyPage();
        }
        identity.text = player.GetPlayerName();
        var page = pages[(int)selected];
        if (page != null && !page.Active) Open(Page.Craft);
        else page?.Fit();
        sheet.Refresh(player);
        playersPage.Refresh();

        if (selected == Page.Craft) layout.Refresh();
        if (pendingFilter && selected == Page.Craft)
        {
            pendingFilter = false;
            RecipeBaseHeight(Gui) = Mathf.Max(1, scrollViewport.rect.height);
            RebuildRecipes(Gui, false);
            if (recipeScroll) recipeScroll.verticalNormalizedPosition = 1;
        }
        if (scrollbarGroup)
        {
            bool overflow = count * Gui.m_recipeListSpace > scrollViewport.rect.height + 1;
            scrollbarGroup.alpha = overflow ? 1 : 0;
            scrollbarGroup.blocksRaycasts = overflow;
            scrollbarGroup.interactable = overflow;
        }
        if (Time.unscaledTime >= nextStyleScan)
        {
            nextStyleScan = Time.unscaledTime + 0.15f;
            style.Scan(content);
            style.Scan(Gui.m_crafting, Gui.m_recipeListRoot);
            if (selected == Page.Craft) StyleRecipeRows();
        }
        bool noRecipes = count == 0;
        if (empty.gameObject.activeSelf != noRecipes) empty.gameObject.SetActive(noRecipes);
        if (noRecipes)
            empty.text = query.Length > 0 || category != Category.All
                ? MenuStyle.Translate("No matching recipes", "Нет подходящих рецептов")
                : MenuStyle.Translate("No recipes available", "Нет доступных рецептов");
    }

    private void StyleRecipeRows()
    {
        recipeRows.RemoveAll(row => !row);
        foreach (RectTransform row in Gui.m_recipeListRoot)
        {
            if (!row.name.StartsWith(Gui.m_recipeElementPrefab.name, StringComparison.Ordinal) || recipeRows.Contains(row)) continue;
            recipeRows.Add(row);
            row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Gui.m_recipeListSpace - 2);
            foreach (string face in new[] { "bkg", "selected" })
            {
                var image = row.Find(face)?.GetComponent<Image>();
                if (!image) continue;
                image.sprite = art[face == "selected" ? "selected" : "slot"];
                image.type = Image.Type.Sliced;
            }
            if (row.GetComponent<Button>() is Button button)
            {
                button.transition = Selectable.Transition.ColorTint;
                button.colors = MenuStyle.Palette;
            }
            var item = row.Find("icon")?.GetComponent<Image>();
            if (item)
            {
                item.preserveAspect = true;
                if (item.color.a == 0) item.color = new Color32(200, 200, 192, 145);
            }
            var label = row.Find("name")?.GetComponent<TMP_Text>();
            if (label)
            {
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }

    private bool Allows(Recipe recipe)
    {
        var item = recipe.m_item.m_itemData.m_shared;
        if (category != Category.All && GetCategory(item) != category) return false;
        if (query.Length == 0) return true;
        string name = Localization.instance.Localize(item.m_name);
        return CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
    }

    private static Category GetCategory(ItemDrop.ItemData.SharedData item)
    {
        if (item.m_buildPieces || item.m_itemType is Type.Tool or Type.Torch
            || item.m_skillType is Skills.SkillType.WoodCutting or Skills.SkillType.Pickaxes) return Category.Tools;
        return item.m_itemType switch
        {
            Type.OneHandedWeapon or Type.TwoHandedWeapon or Type.TwoHandedWeaponLeft or Type.Bow or Type.Attach_Atgeir
                or Type.Ammo or Type.AmmoNonEquipable => Category.Weapons,
            Type.Helmet or Type.Chest or Type.Legs or Type.Shoulder or Type.Hands or Type.Shield
                or Type.Utility or Type.Trinket => Category.Armor,
            Type.Consumable or Type.Fish => Category.Food,
            Type.Material => Category.Materials,
            _ => Category.Other
        };
    }

    private static string PageTitle(Page page) => page switch
    {
        Page.Players => MenuStyle.Translate("Players", "Игроки"),
        Page.Craft => MenuStyle.Translate("Crafting", "Крафт"),
        Page.Character => MenuStyle.Translate("Character", "Персонаж"),
        Page.Skills => MenuStyle.Translate("Skills", "Навыки"),
        Page.Lore => MenuStyle.Translate("Lore", "Знания"),
        Page.Trophies => MenuStyle.Translate("Trophies", "Трофеи"),
        _ => MenuStyle.Translate("Achievements", "Достижения")
    };

    private static string CategoryTitle(Category value) => value switch
    {
        Category.All => MenuStyle.Translate("All categories", "Все категории"),
        Category.Weapons => MenuStyle.Translate("Weapons & ammo", "Оружие и боеприпасы"),
        Category.Armor => MenuStyle.Translate("Armor & accessories", "Броня и аксессуары"),
        Category.Tools => MenuStyle.Translate("Tools", "Инструменты"),
        Category.Food => MenuStyle.Translate("Food & potions", "Еда и зелья"),
        Category.Materials => MenuStyle.Translate("Materials", "Материалы"),
        _ => MenuStyle.Translate("Other", "Прочее")
    };

    internal static void Put(RectTransform rect, float left, float top, float right, float height)
    {
        rect.anchorMin = Vector2.up;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.up;
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public void Dispose()
    {
        if (current == this) current = null;
        if (Gui)
        {
            CloseNativePage();
            Gui.m_crafting.gameObject.SetActive(true);
            Gui.m_recipeListSpace = spacing;
            RecipeBaseHeight(Gui) = recipeBaseHeight;
            if (Gui.m_uiGroups.Length > 2 && Gui.m_uiGroups[2]) Gui.m_uiGroups[2].m_defaultElement = originalDefault;
        }
        card?.Dispose();
        style.Dispose();
        ItemUi.Destroy(tabs);
        ItemUi.Destroy(content);
        ItemUi.Destroy(filters);
        if (Gui && Player.m_localPlayer && InventoryGui.IsVisible()) RebuildRecipes(Gui, false);
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    private static class RecipeFilter
    {
        private static void Prefix(InventoryGui __instance, ref List<Recipe> recipes)
        {
            var menu = current;
            if (menu?.Gui != __instance || menu.category == Category.All && menu.query.Length == 0) return;
            recipes = recipes.FindAll(menu.Allows);
        }

        private static void Postfix(InventoryGui __instance, System.Collections.ICollection ___m_availableRecipes)
        {
            var menu = current;
            if (menu?.Gui != __instance) return;
            menu.count = ___m_availableRecipes.Count;
            menu.StyleRecipeRows();
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "Update")]
    private static class Typing
    {
        private static void Prefix(InventoryGui __instance)
        {
            var menu = current;
            if (menu?.Gui != __instance || !menu.search.isFocused) return;
            ZInput.ResetButtonStatus("Inventory");
            ZInput.ResetButtonStatus("Use");
        }
    }

    private readonly struct ScrollbarState
    {
        private readonly CanvasGroup group;
        private readonly float alpha;
        private readonly bool interactable, raycasts;

        internal ScrollbarState(CanvasGroup group)
        {
            this.group = group;
            alpha = group.alpha;
            interactable = group.interactable;
            raycasts = group.blocksRaycasts;
        }

        internal void Restore()
        {
            if (!group) return;
            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = raycasts;
        }
    }
}
