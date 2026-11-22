using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class SelectionMenus : IDisposable
{
    private readonly Artwork art;
    private readonly MenuStyle style;
    private readonly float worldStep;
    private readonly HashSet<RectTransform> worldRows = new();
    private RectTransform characterCard, worldFrame, worldBody;
    private ScrollRect worldScroll;
    private Button worldBack;
    private MenuRect worldBackOriginal;
    private bool characterReady, worldReady;
    private float nextScan;

    internal FejdStartup Owner { get; }

    internal SelectionMenus(FejdStartup owner, Artwork art)
    {
        Owner = owner;
        this.art = art;
        style = new MenuStyle(art);
        worldStep = owner.m_worldListElementStep;
        owner.m_worldListElementStep = 41;
    }

    internal void Refresh()
    {
        if (!Owner) return;
        bool characters = Owner.m_selectCharacterPanel && Owner.m_selectCharacterPanel.activeInHierarchy;
        bool worlds = Owner.m_worldListPanel && Owner.m_worldListPanel.activeInHierarchy;
        if (characters && !characterReady) MakeCharacterCard();
        if (worlds && !worldReady) MakeWorldScreen();
        if (worldReady && worldBack)
        {
            if (worlds && worldBack.transform.parent != worldFrame) PlaceFooter(worldBack, 0.02f, 0.16f);
            else if (!worlds && worldBack.transform.parent == worldFrame) worldBackOriginal.Restore();
        }
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.16f;

        if (characters) style.Scan(Owner.m_selectCharacterPanel.transform);
        if (Owner.m_newCharacterPanel && Owner.m_newCharacterPanel.activeInHierarchy)
            style.Scan(Owner.m_newCharacterPanel.transform);
        if (Owner.m_createWorldPanel && Owner.m_createWorldPanel.activeInHierarchy)
            style.Scan(Owner.m_createWorldPanel.transform);
        if (worlds)
        {
            RefreshWorldRows();
            style.Scan(Owner.m_startGamePanel.transform);
        }
    }

    private void MakeCharacterCard()
    {
        var panel = (RectTransform)Owner.m_selectCharacterPanel.transform;
        var font = Owner.m_csName.font;
        characterCard = ItemUi.Image(panel, "NewUI_CharacterSelection", art["panel"]).rectTransform;
        CenterBottom(characterCard, 0, 96, 506, 198);
        characterCard.GetComponent<Image>().raycastTarget = true;
        characterCard.SetAsFirstSibling();
        HideLargeChrome(panel, characterCard);

        var characterTitle = MenuStyle.Label(characterCard, "ScreenTitle", font,
            MenuStyle.Translate("Select character", "Выбор персонажа"), 17, ItemUi.Accent, TextAlignmentOptions.Center);
        CraftingMenu.Put(characterTitle.rectTransform, 50, 12, 50, 22);
        var divider = ItemUi.Image(characterCard, "CharacterDivider");
        divider.color = MenuStyle.Edge;
        CraftingMenu.Put(divider.rectTransform, 15, 122, 15, 1);

        var name = Owner.m_csName;
        var rect = Move(name.rectTransform, characterCard);
        CraftingMenu.Put(rect, 73, 39, 73, 38);
        StyleTitle(name, 31);
        var source = Owner.m_csFileSource;
        if (source)
        {
            Move(source.rectTransform, characterCard);
            CraftingMenu.Put(source.rectTransform, 190, 82, 66, 26);
            style.Text(source, ItemUi.Muted, 15);
        }

        MoveCharacterButton(Owner.m_csLeftButton, 17, 47, 46, 45);
        MoveCharacterButton(Owner.m_csRightButton, 443, 47, 46, 45);
        MoveCharacterButton(Owner.m_csRemoveButton, 17, 140, 111, 46);
        MoveCharacterButton(Owner.m_csNewButton, 137, 140, 111, 46);
        MoveCharacterButton(Owner.m_csStartButton, 259, 140, 229, 46, primary: true);
        MoveCharacterButton(Owner.m_csNewBigButton, 127, 58, 252, 54, primary: true);

        foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!label.transform.IsChildOf(characterCard) && IsHeading(label, "Select character", "$menu_selectcharacter"))
                style.Hide(label);
        }
        characterReady = true;
    }

    private void MoveCharacterButton(Button button, float x, float y, float width, float height, bool primary = false)
    {
        if (!button) return;
        ItemUi.TopLeft(Move((RectTransform)button.transform, characterCard), x, y, width, height);
        StyleButton(button, primary);
    }

    private void MakeWorldScreen()
    {
        var panel = (RectTransform)Owner.m_worldListPanel.transform;
        worldScroll = Owner.m_worldListRoot.GetComponentInParent<ScrollRect>(true);
        if (!worldScroll) return;
        var font = Owner.m_worldStart.GetComponentInChildren<TMP_Text>(true).font;
        var newWorld = FindButton(panel, "OnWorldNew");
        var manage = FindButton(panel, "OnManageSaves");
        worldBack = FindButton(Owner.m_startGamePanel.transform, "OnStartGameBack");
        if (worldBack)
        {
            worldBackOriginal = new MenuRect((RectTransform)worldBack.transform);
            style.Remember(worldBackOriginal.Restore);
            StyleButton(worldBack);
        }

        worldFrame = ItemUi.Image(panel, "NewUI_WorldSelection", art["panel"]).rectTransform;
        ItemUi.Stretch(worldFrame, Vector2.zero, Vector2.zero);
        worldFrame.GetComponent<Image>().raycastTarget = true;
        worldFrame.SetAsFirstSibling();
        HideLargeChrome(panel, worldFrame);

        const float railWidth = 192;
        var rail = ItemUi.Image(worldFrame, "WorldOptions", art["slot"]).rectTransform;
        rail.anchorMin = Vector2.right;
        rail.anchorMax = Vector2.one;
        rail.offsetMin = new Vector2(-railWidth - 13, 65);
        rail.offsetMax = new Vector2(-13, -13);

        worldBody = ItemUi.Image(worldFrame, "WorldList", art["slot"]).rectTransform;
        ItemUi.Stretch(worldBody, new Vector2(13, 65), new Vector2(-railWidth - 25, -13));
        var title = MenuStyle.Label(worldBody, "ScreenTitle", font,
            MenuStyle.Translate("Select world", "Выбор мира"), 23, ItemUi.Accent);
        title.fontStyle = FontStyles.Bold;
        CraftingMenu.Put(title.rectTransform, 12, 8, 12, 33);
        var optionsTitle = MenuStyle.Label(rail, "OptionsTitle", font,
            MenuStyle.Translate("World options", "Настройки мира"), 18, ItemUi.Accent);
        CraftingMenu.Put(optionsTitle.rectTransform, 11, 9, 11, 29);

        var header = ItemUi.Rect(worldBody, "ColumnHeadings");
        CraftingMenu.Put(header, 18, 47, 16, 20);
        Column(header, "World", "Мир", 0, 0.44f);
        Column(header, "Seed", "Сид", 0.45f, 0.67f);
        Column(header, "Modifiers", "Модификаторы", 0.68f, 1);
        var line = ItemUi.Image(worldBody, "TableDivider");
        line.color = MenuStyle.Edge;
        CraftingMenu.Put(line.rectTransform, 11, 68, 11, 1);

        var scrollRect = Move((RectTransform)worldScroll.transform, worldBody);
        ItemUi.Stretch(scrollRect, new Vector2(9, 10), new Vector2(-19, -74));
        var viewport = worldScroll.viewport;
        if (viewport && viewport != scrollRect)
        {
            style.Remember(new MenuRect(viewport).Restore);
            ItemUi.Stretch(viewport, Vector2.zero, Vector2.zero);
        }
        style.Remember(new MenuRect(Owner.m_worldListRoot).Restore);
        StretchHorizontally(Owner.m_worldListRoot);
        var scrollbar = worldScroll.verticalScrollbar;
        if (!scrollbar)
        {
            foreach (var candidate in panel.GetComponentsInChildren<Scrollbar>(true))
            {
                if (candidate.direction is Scrollbar.Direction.BottomToTop or Scrollbar.Direction.TopToBottom)
                {
                    scrollbar = candidate;
                    break;
                }
            }
        }
        if (scrollbar)
        {
            var bar = Move((RectTransform)scrollbar.transform, worldBody);
            bar.anchorMin = Vector2.right;
            bar.anchorMax = Vector2.one;
            bar.offsetMin = new Vector2(-15, 12);
            bar.offsetMax = new Vector2(-7, -74);
            if (scrollbar.handleRect && scrollbar.handleRect.TryGetComponent<Image>(out var handle))
                style.Sprite(handle, "meter-fill", MenuStyle.MutedScrollbar, force: true);
        }

        PositionOption(Owner.m_openServerToggle, 47);
        PositionOption(Owner.m_publicServerToggle, 87);
        PositionOption(Owner.m_crossplayServerToggle, 127);
        PositionOption(Owner.m_samePlatformOnlyToggleWorldPanel, 167);
        var password = Owner.m_serverPassword ? Owner.m_serverPassword.transform as RectTransform : null;
        if (password && password.IsChildOf(panel))
        {
            foreach (var label in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.transform.IsChildOf(password) || !IsHeading(label, "Server Password", "$menu_password")) continue;
                CraftingMenu.Put(Move(label.rectTransform, rail), 12, 204, 12, 23);
                style.Text(label, ItemUi.Muted, 14);
            }
            CraftingMenu.Put(Move(password, rail), 12, 231, 13, 35);
            if (password.TryGetComponent<Image>(out var inputImage)) style.Sprite(inputImage, "slot", Color.white);
        }
        if (Owner.m_passwordError && Owner.m_passwordError.transform.IsChildOf(panel))
            CraftingMenu.Put(Move(Owner.m_passwordError.rectTransform, rail), 12, 272, 12, 54);

        if (Owner.m_serverOptionsButton && Owner.m_serverOptionsButton.transform.IsChildOf(panel))
        {
            var modifiers = Move((RectTransform)Owner.m_serverOptionsButton.transform, rail);
            modifiers.anchorMin = new Vector2(0, 0);
            modifiers.anchorMax = new Vector2(1, 0);
            modifiers.offsetMin = new Vector2(12, 12);
            modifiers.offsetMax = new Vector2(-12, 51);
            StyleButton(Owner.m_serverOptionsButton);
        }

        SetupFooter(newWorld, 0.17f, 0.30f);
        SetupFooter(Owner.m_worldRemove, 0.31f, 0.46f);
        SetupFooter(manage, 0.47f, 0.70f);
        SetupFooter(Owner.m_worldStart, 0.72f, 0.98f, true);
        foreach (var label in Owner.m_startGamePanel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!label.transform.IsChildOf(worldFrame) && IsHeading(label, "Select world", "$menu_selectworld"))
                style.Hide(label);
        }
        if (Owner.m_worldSourceInfoPanel && Owner.m_worldSourceInfoPanel.transform.IsChildOf(panel))
            style.Scan(Owner.m_worldSourceInfoPanel.transform);
        worldReady = true;
        RefreshWorldRows();

        void Column(Transform parent, string english, string russian, float left, float right)
        {
            var label = MenuStyle.Label(parent, english, font, MenuStyle.Translate(english, russian), 13, ItemUi.Muted);
            SetColumn(label.rectTransform, left, right);
        }

        void PositionOption(Toggle control, float top)
        {
            if (!control || !control.transform.IsChildOf(panel)) return;
            var rect = Move((RectTransform)control.transform, rail);
            CraftingMenu.Put(rect, 12, top, 12, 34);
            style.Control(control);
            var label = control.GetComponentInChildren<TMP_Text>(true);
            if (!label) return;
            style.Remember(new MenuRect(label.rectTransform).Restore);
            var autosizing = label.enableAutoSizing;
            var min = label.fontSizeMin;
            var max = label.fontSizeMax;
            var overflow = label.overflowMode;
            style.Remember(() =>
            {
                if (!label) return;
                label.enableAutoSizing = autosizing;
                label.fontSizeMin = min;
                label.fontSizeMax = max;
                label.overflowMode = overflow;
            });
            ItemUi.Stretch(label.rectTransform, new Vector2(36, 0), new Vector2(-2, 0));
            label.fontSizeMin = 11;
            label.fontSizeMax = 15;
            label.enableAutoSizing = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            style.Text(label, ItemUi.Ink, 15);
        }

        void SetupFooter(Button button, float left, float right, bool primary = false)
        {
            if (!button || !button.transform.IsChildOf(panel)) return;
            style.Remember(new MenuRect((RectTransform)button.transform).Restore);
            PlaceFooter(button, left, right);
            StyleButton(button, primary);
        }
    }

    private void PlaceFooter(Button button, float left, float right)
    {
        var rect = (RectTransform)button.transform;
        rect.SetParent(worldFrame, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchorMin = new Vector2(left, 0);
        rect.anchorMax = new Vector2(right, 0);
        rect.pivot = Vector2.zero;
        rect.offsetMin = new Vector2(0, 11);
        rect.offsetMax = new Vector2(0, 54);
    }

    private void RefreshWorldRows()
    {
        if (!worldScroll) return;
        int index = 0;
        worldRows.RemoveWhere(row => !row);
        foreach (RectTransform row in Owner.m_worldListRoot)
        {
            if (!row.gameObject.activeSelf || !row.TryGetComponent<Button>(out var button)) continue;
            if (worldRows.Add(row))
            {
                style.Remember(new MenuRect(row).Restore);
                StretchHorizontally(row);
                row.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 37);
                StyleButton(button);
                var selected = row.Find("selected") as RectTransform;
                if (selected)
                {
                    style.Remember(new MenuRect(selected).Restore);
                    ItemUi.Stretch(selected, new Vector2(1, 0), new Vector2(-1, 0));
                    style.Sprite(selected.GetComponent<Image>(), "selected", Color.white, force: true);
                }
                FormatCell(row, "name", 0.00f, 0.44f, 16, ItemUi.Ink);
                FormatCell(row, "seed", 0.45f, 0.67f, 14, ItemUi.Muted);
                FormatCell(row, "modifiers", 0.68f, 0.94f, 13, ItemUi.Muted);
                foreach (string source in new[] { "source_cloud", "source_local", "source_legacy" })
                {
                    if (row.Find(source) is not RectTransform icon) continue;
                    style.Remember(new MenuRect(icon).Restore);
                    icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(1, 0.5f);
                    icon.sizeDelta = new Vector2(13, 13);
                    icon.anchoredPosition = new Vector2(-6, 0);
                }
            }
            var target = new Vector2(0, -index++ * Owner.m_worldListElementStep);
            if (target != row.anchoredPosition) row.anchoredPosition = target;
        }
        Owner.m_worldListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Max(worldScroll.viewport ? worldScroll.viewport.rect.height : 0, index * Owner.m_worldListElementStep));
    }

    private void FormatCell(Transform row, string name, float left, float right, float size, Color tone)
    {
        var label = row.Find(name)?.GetComponent<TMP_Text>();
        if (!label) return;
        style.Remember(new MenuRect(label.rectTransform).Restore);
        SetColumn(label.rectTransform, left, right);
        style.Text(label, tone, size);
        var oldAlignment = label.alignment;
        var oldWrapping = label.textWrappingMode;
        var oldOverflow = label.overflowMode;
        style.Remember(() =>
        {
            if (!label) return;
            label.alignment = oldAlignment;
            label.textWrappingMode = oldWrapping;
            label.overflowMode = oldOverflow;
        });
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void HideLargeChrome(RectTransform panel, Transform replacement)
    {
        var plate = panel.GetComponent<Image>();
        if (plate) style.Hide(plate);
        foreach (var image in panel.GetComponentsInChildren<Image>(true))
        {
            if (image == plate || image.transform.IsChildOf(replacement) || !image.sprite
                || !image.sprite.name.StartsWith("woodpanel", StringComparison.Ordinal)) continue;
            var rect = image.rectTransform.rect;
            if (rect.width > panel.rect.width * 0.60f && rect.height > panel.rect.height * 0.60f)
                style.Hide(image);
        }
    }

    private RectTransform Move(RectTransform rect, RectTransform destination)
    {
        style.Remember(new MenuRect(rect).Restore);
        rect.SetParent(destination, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return rect;
    }

    private void StyleButton(Button button, bool primary = false)
    {
        style.Control(button);
        if (primary && button.targetGraphic is Image image) image.sprite = art["selected"];
        style.Text(button.GetComponentInChildren<TMP_Text>(true), primary ? ItemUi.Accent : ItemUi.Ink);
    }

    private void StyleTitle(TMP_Text label, float size)
    {
        style.Text(label, ItemUi.Ink, size);
        var alignment = label.alignment;
        var wrap = label.textWrappingMode;
        var overflow = label.overflowMode;
        style.Remember(() =>
        {
            if (!label) return;
            label.alignment = alignment;
            label.textWrappingMode = wrap;
            label.overflowMode = overflow;
        });
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static bool IsHeading(TMP_Text label, string english, string key) => string.Equals(label.text, english,
        StringComparison.OrdinalIgnoreCase) || label.text == key || label.text == Localization.instance.Localize(key);

    private static Button FindButton(Transform root, string method)
    {
        foreach (var button in root.GetComponentsInChildren<Button>(true))
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                if (button.onClick.GetPersistentMethodName(i) == method) return button;
        return null;
    }

    private static void SetColumn(RectTransform rect, float left, float right)
    {
        rect.anchorMin = new Vector2(left, 0);
        rect.anchorMax = new Vector2(right, 1);
        rect.offsetMin = new Vector2(6, 0);
        rect.offsetMax = new Vector2(-4, 0);
    }

    private static void StretchHorizontally(RectTransform rect)
    {
        rect.anchorMin = new Vector2(0, rect.anchorMin.y);
        rect.anchorMax = new Vector2(1, rect.anchorMax.y);
        rect.pivot = new Vector2(0, rect.pivot.y);
        rect.offsetMin = new Vector2(0, rect.offsetMin.y);
        rect.offsetMax = new Vector2(0, rect.offsetMax.y);
    }

    private static void CenterBottom(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    public void Dispose()
    {
        if (Owner) Owner.m_worldListElementStep = worldStep;
        style.Dispose();
        ItemUi.Destroy(characterCard);
        ItemUi.Destroy(worldFrame);
    }
}
