using System;
using System.Collections.Generic;
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
    private readonly RectTransform frame;
    private readonly InlinePage[] pages;
    private readonly string[] names;
    private readonly TextMeshProUGUI heading;
    private readonly HashSet<TMP_Text> labels = new();
    private int selected = -1;
    private float nextScan;

    internal Settings Owner { get; }

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

        frame = ItemUi.Image(parent, "NewUI_Settings", art["panel"]).rectTransform;
        ItemUi.Stretch(frame, new Vector2(3, 6), new Vector2(-3, -6));
        frame.GetComponent<Image>().raycastTarget = true;
        frame.SetAsFirstSibling();
        HideNativeChrome(parent, frame);

        var title = MenuStyle.Label(frame, "SettingsTitle", baseFont, Localization.instance.Localize("$menu_settings"),
            29, ItemUi.Accent);
        title.fontStyle = FontStyles.Bold;
        CraftingMenu.Put(title.rectTransform, 23, 15, 23, 42);

        var rail = ItemUi.Image(frame, "CategoryRail", art["slot"]).rectTransform;
        rail.anchorMin = Vector2.zero;
        rail.anchorMax = Vector2.up;
        rail.offsetMin = new Vector2(19, 76);
        rail.offsetMax = new Vector2(217, -75);
        var bodyPanel = ItemUi.Image(frame, "SettingsBody", art["slot"]).rectTransform;
        ItemUi.Stretch(bodyPanel, new Vector2(231, 76), new Vector2(-19, -75));
        heading = MenuStyle.Label(bodyPanel, "CurrentCategory", baseFont, "", 21, ItemUi.Accent);
        heading.fontStyle = FontStyles.Bold;
        CraftingMenu.Put(heading.rectTransform, 19, 10, 18, 32);
        var separator = ItemUi.Image(bodyPanel, "Divider");
        separator.color = MenuStyle.Edge;
        CraftingMenu.Put(separator.rectTransform, 19, 46, 19, 1);
        var body = ItemUi.Rect(bodyPanel, "SettingsControls");
        ItemUi.Stretch(body, new Vector2(14, 12), new Vector2(-14, -52));

        pages = new InlinePage[handler.m_tabs.Count];
        names = new string[pages.Length];
        int visibleTab = 0;
        for (int i = 0; i < pages.Length; i++)
        {
            var tab = handler.m_tabs[i];
            if (tab.m_page) pages[i] = new InlinePage(tab.m_page, body, style);
            if (!tab.m_button) continue;
            var button = tab.m_button;
            var rect = (RectTransform)button.transform;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            names[i] = label ? label.text : "";
            style.Remember(new MenuRect(rect).Restore);
            Move(rect, rail);
            CraftingMenu.Put(rect, 9, 10 + visibleTab++ * 49, 9, 42);
            if (button.targetGraphic is Image image)
            {
                style.Control(button);
                button.transition = Selectable.Transition.ColorTint;
                button.colors = MenuStyle.Palette;
                image.sprite = art["button"];
            }
            var selectedObj = button.transform.Find("Selected");
            if (selectedObj) style.Hide(selectedObj.gameObject);
            if (label) style.Text(label, ItemUi.Ink, 16);
        }

        var back = NativeBack(settings);
        var apply = NativeApply(settings);
        FooterButton(back, 160, primary: false);
        FooterButton(apply, 20, primary: true);
        Refresh();
    }

    private void HideNativeChrome(RectTransform parent, RectTransform replacement)
    {
        var plate = parent.GetComponent<Image>();
        if (plate) style.Hide(plate);
        foreach (var image in parent.GetComponentsInChildren<Image>(true))
        {
            if (image.transform.IsChildOf(replacement) || image == plate || !image.sprite) continue;
            string sprite = image.sprite.name;
            bool tabPage = handler.m_tabs.Exists(tab => tab.m_page && image.transform.IsChildOf(tab.m_page));
            if (!tabPage && sprite.StartsWith("woodpanel", StringComparison.Ordinal) &&
                image.rectTransform.rect.width >= parent.rect.width * 0.55f) style.Hide(image);
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
        style.Text(button.GetComponentInChildren<TMP_Text>(true), primary ? ItemUi.Accent : ItemUi.Ink, 17);
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
        int tab = handler.GetActiveTab();
        if (tab >= 0 && tab < pages.Length)
        {
            pages[tab]?.Fit();
            if (tab != selected)
            {
                selected = tab;
                heading.text = Localization.instance.Localize(names[tab] ?? "");
                for (int i = 0; i < pages.Length; i++)
                {
                    var button = handler.m_tabs[i].m_button;
                    if (button && button.targetGraphic is Image graphic) graphic.sprite = art[i == tab ? "selected" : "button"];
                }
                nextScan = 0;
            }
        }
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 0.2f;
        // Tabs can create additional rows and popups as input devices and settings change.
        style.Scan(frame);
        if (tab < 0 || tab >= pages.Length || !handler.m_tabs[tab].m_page) return;
        foreach (var label in handler.m_tabs[tab].m_page.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!labels.Add(label)) continue;
            var color = label.color;
            if (color.r > 0.75f && color.g < color.r * 0.85f && color.b < 0.6f) style.Text(label, ItemUi.Ink);
        }
    }

    public void Dispose()
    {
        style.Dispose();
        ItemUi.Destroy(frame);
    }
}
