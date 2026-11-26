using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class SettingsMenu : IDisposable
{
    private static readonly AccessTools.FieldRef<Settings, GameObject> NativePanel =
        AccessTools.FieldRefAccess<Settings, GameObject>("m_settingsPanel");
    private static readonly AccessTools.FieldRef<Settings, Button> NativeBack =
        AccessTools.FieldRefAccess<Settings, Button>("m_backButton");
    private static readonly AccessTools.FieldRef<Settings, Button> NativeApply =
        AccessTools.FieldRefAccess<Settings, Button>("m_okButton");
    private static readonly AccessTools.FieldRef<Settings, TabHandler> NativeTabs =
        AccessTools.FieldRefAccess<Settings, TabHandler>("m_tabHandler");

    private readonly MenuStyle style;
    private readonly Artwork art;
    private readonly TabHandler handler;
    private readonly RectTransform frame, headingBar;
    private readonly SettingsPageView[] pages;
    private readonly string[] names;
    private readonly Image[] markers;
    private readonly TextMeshProUGUI heading;
    private int selected = -1, railSelected = -1;
    private float nextScan;
    private readonly System.Collections.Generic.HashSet<Button> placedBacks = new();

    internal Settings Owner { get; }
    private readonly TMP_FontAsset brand, brandLight;

    internal SettingsMenu(Settings settings, Artwork art)
    {
        Owner = settings;
        this.art = art;
        style = new MenuStyle(art);
        handler = NativeTabs(settings);
        var native = NativePanel(settings);
        var parent = (RectTransform)(native ? native.transform : handler.transform);
        var firstTab = handler.m_tabs.Find(tab => tab.m_button)?.m_button;
        var baseFont = firstTab ? firstTab.GetComponentInChildren<TMP_Text>(true)?.font : TMP_Settings.defaultFontAsset;
        var sans = FindSans(settings.transform) ?? baseFont;
        // Titles, tabs and buttons use the game's carved face; the controls keep the plain one.
        brand = HudStyle.BrandFont(baseFont);
        brandLight = HudStyle.BrandLight(baseFont);

        frame = ItemUi.Image(parent, "NewUI_Settings", art["panel"]).rectTransform;
        ItemUi.Stretch(frame, new Vector2(3, 6), new Vector2(-3, -6));
        frame.GetComponent<Image>().raycastTarget = true;
        frame.SetAsLastSibling();
        HideNativeChrome(parent, frame);

        var title = MenuStyle.Label(frame, "SettingsTitle", brand, Localization.instance.Localize("$menu_settings"),
            29, ItemUi.Accent);
        title.fontStyle = FontStyles.Normal;
        CraftingMenu.Put(title.rectTransform, 23, 15, 23, 42);

        var rail = ItemUi.Image(frame, "CategoryRail", art["slot"]).rectTransform;
        rail.anchorMin = Vector2.zero;
        rail.anchorMax = Vector2.up;
        rail.offsetMin = new Vector2(19, 76);
        rail.offsetMax = new Vector2(217, -75);
        var bodyPanel = ItemUi.Image(frame, "SettingsBody", art["slot"]).rectTransform;
        ItemUi.Stretch(bodyPanel, new Vector2(231, 76), new Vector2(-19, -75));

        headingBar = ItemUi.Rect(bodyPanel, "Heading");
        CraftingMenu.Put(headingBar, 19, 8, 16, 38);
        heading = MenuStyle.Label(headingBar, "CurrentCategory", brand, "", 21, ItemUi.Accent);
        heading.fontStyle = FontStyles.Normal;
        ItemUi.Stretch(heading.rectTransform, Vector2.zero, new Vector2(-130, 0));
        var separator = ItemUi.Image(bodyPanel, "Divider");
        separator.color = MenuStyle.Edge;
        CraftingMenu.Put(separator.rectTransform, 19, 48, 19, 1);
        var body = ItemUi.Rect(bodyPanel, "SettingsControls");
        ItemUi.Stretch(body, new Vector2(8, 10), new Vector2(-8, -54));

        pages = new SettingsPageView[handler.m_tabs.Count];
        names = new string[pages.Length];
        markers = new Image[pages.Length];
        int visibleTab = 0;
        for (int i = 0; i < pages.Length; i++)
        {
            var tab = handler.m_tabs[i];
            if (tab.m_page) pages[i] = new SettingsPageView(tab.m_page, body, style, art, sans);
            if (!tab.m_button) continue;
            var button = tab.m_button;
            var rect = (RectTransform)button.transform;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            names[i] = label ? label.text : "";
            style.Remember(new MenuRect(rect).Restore);
            Move(rect, rail);
            CraftingMenu.Put(rect, 9, 10 + visibleTab++ * 47, 9, 41);
            if (button.targetGraphic is Image image)
            {
                style.Control(button);
                button.transition = Selectable.Transition.ColorTint;
                var colors = MenuStyle.Palette;
                colors.disabledColor = Color.white;
                button.colors = colors;
                image.sprite = art["button"];
            }
            var selectedObj = button.transform.Find("Selected");
            if (selectedObj) style.Hide(selectedObj.gameObject);
            if (label)
            {
                style.Font(label, brandLight);
                style.Text(label, ItemUi.Muted, 16);
                Align(label, TextAlignmentOptions.MidlineLeft, new Vector4(18, 0, 6, 0));
            }
            markers[i] = ItemUi.Image(rect, "NewUI_Marker", art["pill"]);
            markers[i].color = ItemUi.Accent;
            markers[i].rectTransform.anchorMin = new Vector2(0, 0.5f);
            markers[i].rectTransform.anchorMax = new Vector2(0, 0.5f);
            markers[i].rectTransform.pivot = new Vector2(0, 0.5f);
            markers[i].rectTransform.sizeDelta = new Vector2(4, 22);
            markers[i].rectTransform.anchoredPosition = new Vector2(7, 0);
            style.Claim(markers[i]);
            var marker = markers[i].gameObject;
            style.Remember(() => { if (marker) UnityEngine.Object.Destroy(marker); });
        }

        FooterButton(NativeBack(settings), 160, primary: false);
        FooterButton(NativeApply(settings), 20, primary: true);
        Refresh();
    }

    private static TMP_FontAsset FindSans(Transform root)
    {
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            if (text.font && text.font.name.IndexOf("Sans", StringComparison.OrdinalIgnoreCase) >= 0) return text.font;
        return null;
    }

    private void Align(TMP_Text label, TextAlignmentOptions alignment, Vector4 margin)
    {
        var previous = label.alignment;
        var spacing = label.margin;
        style.Remember(() =>
        {
            if (!label) return;
            label.alignment = previous;
            label.margin = spacing;
        });
        label.alignment = alignment;
        label.margin = margin;
    }

    private void HideNativeChrome(RectTransform parent, RectTransform replacement)
    {
        var plate = parent.GetComponent<Image>();
        if (plate) style.Hide(plate);
        foreach (var image in parent.GetComponentsInChildren<Image>(false))
        {
            if (image.transform.IsChildOf(replacement) || image == plate || image.GetComponentInParent<Selectable>()) continue;
            bool tabPage = handler.m_tabs.Exists(tab => tab.m_page && image.transform.IsChildOf(tab.m_page));
            Rect bounds = image.rectTransform.rect;
            if (!tabPage && bounds.width >= parent.rect.width * 0.65f &&
                bounds.height >= parent.rect.height * 0.55f) style.Hide(image);
        }
        string settingsTitle = Localization.instance.Localize("$menu_settings");
        foreach (var label in parent.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!label.transform.IsChildOf(replacement) && (label.text == "$menu_settings" || label.text == settingsTitle))
                style.Hide(label);
        }
    }

    private void FooterButton(Button button, float right, bool primary)
    {
        if (!button) return;
        var rect = (RectTransform)button.transform;
        style.Remember(new MenuRect(rect).Restore);
        Move(rect, frame);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0);
        rect.sizeDelta = new Vector2(128, 43);
        rect.anchoredPosition = new Vector2(-right, 17);
        style.Control(button);
        if (primary && button.targetGraphic is Image image) image.sprite = art["selected"];
        var caption = button.GetComponentInChildren<TMP_Text>(true);
        style.Font(caption, brand);
        style.Text(caption, primary ? ItemUi.Accent : ItemUi.Ink, 17);
    }

    private static void Move(RectTransform rect, Transform parent)
    {
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    internal void Refresh()
    {
        if (!handler || !frame) return;
        int tab = ActiveTab();
        for (int i = 0; i < pages.Length; i++) pages[i]?.Refresh(i == tab);
        if (tab >= 0 && tab < pages.Length && tab != selected)
        {
            selected = tab;
            // A tab without a rail button, such as the radial menu options, is a subpage of the last rail tab.
            if (handler.m_tabs[tab].m_button) railSelected = tab;
            heading.text = Localization.instance.Localize(names[tab] ?? "");
            if (string.IsNullOrEmpty(heading.text)) heading.text = SubpageTitle(tab);
            for (int i = 0; i < pages.Length; i++)
            {
                var button = handler.m_tabs[i].m_button;
                bool current = i == railSelected;
                if (button && button.targetGraphic is Image graphic) graphic.sprite = art[current ? "selected" : "button"];
                var label = button ? button.GetComponentInChildren<TMP_Text>(true) : null;
                if (label) label.color = current ? ItemUi.Ink : ItemUi.Muted;
                if (markers[i]) markers[i].enabled = current;
            }
            nextScan = 0;
        }
        for (int i = 0; i < pages.Length; i++)
        {
            var back = pages[i]?.Back;
            if (!back) continue;
            PlaceSubpageBack(back);
            if (back.gameObject.activeSelf != (i == tab)) back.gameObject.SetActive(i == tab);
        }
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.2f;
        // Tabs can create additional rows and popups as input devices and settings change.
        style.Scan(frame);
    }

    // Opening a subpage such as the radial menu options shows its page without changing the handler's tab.
    private int ActiveTab()
    {
        int tab = handler.GetActiveTab();
        if (tab >= 0 && tab < pages.Length && handler.m_tabs[tab].m_page && handler.m_tabs[tab].m_page.gameObject.activeSelf)
            return tab;
        for (int i = 0; i < handler.m_tabs.Count; i++)
            if (handler.m_tabs[i].m_page && handler.m_tabs[i].m_page.gameObject.activeSelf) return i;
        return tab;
    }

    // The heading of a subpage repeats the caption of the button that opens it.
    private string SubpageTitle(int tab)
    {
        var page = handler.m_tabs[tab].m_page;
        if (!page) return "";
        string key = page.name.Replace("Tab", "").ToLowerInvariant();
        foreach (var button in frame.GetComponentsInChildren<Button>(true))
        {
            if (button.name.Replace(" ", "").ToLowerInvariant().IndexOf(key, StringComparison.Ordinal) < 0) continue;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label && !string.IsNullOrWhiteSpace(label.text)) return label.text;
        }
        return "";
    }

    // A subpage's own back button moves into the heading, where it stays visible above the list.
    private void PlaceSubpageBack(Button back)
    {
        if (!placedBacks.Add(back)) return;
        var rect = (RectTransform)back.transform;
        bool wasActive = back.gameObject.activeSelf;
        style.Remember(() => { if (back) back.gameObject.SetActive(wasActive); });
        style.Remember(new MenuRect(rect).Restore);
        Move(rect, headingBar);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 0.5f);
        rect.sizeDelta = new Vector2(120, 34);
        rect.anchoredPosition = Vector2.zero;
        style.Control(back);
        var caption = back.GetComponentInChildren<TMP_Text>(true);
        style.Font(caption, brand);
        style.Text(caption, ItemUi.Ink, 15);
    }

    public void Dispose()
    {
        style.Dispose();
        foreach (var page in pages) page?.Dispose();
        ItemUi.Destroy(frame);
    }
}
