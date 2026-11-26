using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Lays out the selected recipe like the item tooltip card: an icon well and a bold
// name above a gold rule, muted stats, then the requirements and the craft action.
// Valheim keeps filling every element, so only their placement and look change.
internal sealed class RecipeCard : IDisposable
{
    private const float Padding = 16, Well = 52, HeaderBottom = 80;
    private const float RequirementsTop = 166, UpgradeTop = 186;
    private static readonly Color32 Rule = new(192, 157, 97, 155);
    private static readonly Color32 Trough = new(8, 13, 15, 235);
    private static readonly Color32 ProgressFill = new(225, 187, 116, 150);
    private static RecipeCard current;

    private readonly InventoryGui gui;
    private readonly MenuStyle style;
    private readonly RectTransform panel, name, description;
    private readonly Image well, divider, requirementsRule;
    private readonly TextMeshProUGUI requirementsLabel;
    private readonly UITooltip repairTooltip;
    private readonly string repairText;
    private string lastNative, lastStyled;
    private bool variant, upgrade, laidOut;

    internal RecipeCard(InventoryGui gui, MenuStyle style, Artwork art, TMP_FontAsset font)
    {
        this.gui = gui;
        this.style = style;
        panel = (RectTransform)gui.m_recipeDecription.transform.parent;
        if (panel.GetComponent<Image>() is Image surface) Claim(surface, "item-card", Color.white);

        well = ItemUi.Image(panel, "NewUI_IconWell", art["slot"]);
        ItemUi.TopLeft(well.rectTransform, Padding, Padding, Well, Well);
        style.Claim(well);
        if (gui.m_recipeIcon)
        {
            var icon = gui.m_recipeIcon.rectTransform;
            Remember(icon);
            icon.SetSiblingIndex(well.transform.GetSiblingIndex() + 1);
            ItemUi.TopLeft(icon, Padding + 6, Padding + 6, Well - 12, Well - 12);
            Keep(gui.m_recipeIcon);
            gui.m_recipeIcon.preserveAspect = true;
        }

        name = gui.m_recipeName.rectTransform;
        Remember(name);
        Restyle(gui.m_recipeName, HudStyle.BrandFont(font), ItemUi.Ink, 22, 14, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        description = gui.m_recipeDecription.rectTransform;
        Remember(description);
        Restyle(gui.m_recipeDecription, font, ItemUi.Muted, 16, 12, FontStyles.Normal, TextAlignmentOptions.TopLeft);

        divider = ItemUi.Image(panel, "NewUI_HeaderRule");
        divider.color = Rule;
        ItemUi.TopLeft(divider.rectTransform, Padding, HeaderBottom, panel.rect.width - Padding * 2, 1);
        style.Claim(divider);

        requirementsRule = ItemUi.Image(panel, "NewUI_RequirementsRule");
        requirementsRule.color = Rule;
        Bottom(requirementsRule.rectTransform, RequirementsTop - 8, 1);
        style.Claim(requirementsRule);
        requirementsLabel = MenuStyle.Label(panel, "NewUI_RequirementsLabel", HudStyle.BrandFont(font),
            MenuStyle.Translate("Requires", "Требуется"), 15, ItemUi.Muted);
        Bottom(requirementsLabel.rectTransform, RequirementsTop - 12, 20);

        if (gui.m_variantButton)
        {
            var rect = (RectTransform)gui.m_variantButton.transform;
            Remember(rect);
            ItemUi.TopLeft(rect, Padding + Well + 12, Padding + 31, 104, 22);
            var label = gui.m_variantButton.GetComponentInChildren<TMP_Text>(true);
            if (label) Restyle(label, font, ItemUi.Ink, 13, 9, FontStyles.Normal, TextAlignmentOptions.Center);
        }

        if (gui.m_craftButton)
        {
            if (gui.m_craftButton.targetGraphic is Image face) Claim(face, "selected", Color.white);
            var label = gui.m_craftButton.GetComponentInChildren<TMP_Text>(true);
            if (label) Restyle(label, HudStyle.BrandFont(font), ItemUi.Accent, 22, 14, FontStyles.Normal, TextAlignmentOptions.Center);
        }
        StyleProgress(font);

        if (gui.m_repairButton && gui.m_repairButton.TryGetComponent(out repairTooltip))
        {
            repairText = repairTooltip.m_text;
            repairTooltip.m_text = Localization.instance.Localize(repairText) + "\n<color=#E1BB74>" +
                MenuStyle.Translate("Shift: repair everything", "Shift — отремонтировать всё") + "</color>";
        }
        current = this;
        Refresh();
    }

    private void StyleProgress(TMP_FontAsset font)
    {
        // The cancel button covers the bar and must stay invisible.
        if (gui.m_craftCancelButton && gui.m_craftCancelButton.targetGraphic) style.Claim(gui.m_craftCancelButton.targetGraphic);
        var bar = gui.m_craftProgressBar;
        if (!bar) return;
        var back = bar.transform.Find("bkg")?.GetComponent<Image>();
        if (back)
        {
            Claim(back, "slot", Trough);
            Remember(back.rectTransform);
            ItemUi.Stretch(back.rectTransform, new Vector2(-3, -3), new Vector2(3, 3));
        }
        if (bar.m_bar && bar.m_bar.GetComponent<Image>() is Image fill)
        {
            // A translucent fill keeps the white caption readable where it passes under it.
            Claim(fill, "meter-fill", ProgressFill);
            bar.SetColor(ProgressFill);
        }
        if (gui.m_craftProgressPanel)
            foreach (var label in gui.m_craftProgressPanel.GetComponentsInChildren<TMP_Text>(true))
                if (!gui.m_craftCancelButton || !label.transform.IsChildOf(gui.m_craftCancelButton.transform))
                    Restyle(label, font, ItemUi.Ink, 18, 12, FontStyles.Bold, TextAlignmentOptions.Center);
    }

    private void Claim(Image image, string role, Color tint)
    {
        style.Sprite(image, role, tint, force: true);
        if (role == "meter-fill") image.type = Image.Type.Simple;
    }

    private void Keep(Graphic graphic) => style.Claim(graphic);

    private void Remember(RectTransform rect) => style.Remember(new MenuRect(rect).Restore);

    private void Bottom(RectTransform rect, float top, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.right;
        rect.pivot = new Vector2(0.5f, 1);
        rect.offsetMin = new Vector2(Padding, top - height);
        rect.offsetMax = new Vector2(-Padding, top);
    }

    private void Restyle(TMP_Text text, TMP_FontAsset font, Color color, float size, float minimum, FontStyles weight,
        TextAlignmentOptions alignment)
    {
        var previous = (text.font, text.fontSize, text.enableAutoSizing, text.fontSizeMin, text.fontSizeMax, text.color,
            text.fontStyle, text.alignment, text.textWrappingMode, text.overflowMode);
        style.Remember(() =>
        {
            if (!text) return;
            text.font = previous.font;
            text.fontSize = previous.fontSize;
            text.enableAutoSizing = previous.enableAutoSizing;
            text.fontSizeMin = previous.fontSizeMin;
            text.fontSizeMax = previous.fontSizeMax;
            text.color = previous.color;
            text.fontStyle = previous.fontStyle;
            text.alignment = previous.alignment;
            text.textWrappingMode = previous.textWrappingMode;
            text.overflowMode = previous.overflowMode;
        });
        if (font) text.font = font;
        text.enableAutoSizing = true;
        text.fontSizeMin = minimum;
        text.fontSizeMax = size;
        text.fontSize = size;
        text.color = color;
        text.fontStyle = weight;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        style.Claim(text);
    }

    internal void Refresh()
    {
        bool hasVariant = gui.m_variantButton && gui.m_variantButton.gameObject.activeSelf;
        bool hasUpgrade = gui.m_itemCraftType && gui.m_itemCraftType.gameObject.activeSelf
            || gui.m_qualityPanel && gui.m_qualityPanel.gameObject.activeInHierarchy;
        if (!laidOut || hasVariant != variant || hasUpgrade != upgrade)
        {
            laidOut = true;
            variant = hasVariant;
            upgrade = hasUpgrade;
            float width = Mathf.Max(1, panel.rect.width - Padding * 3 - Well);
            ItemUi.TopLeft(name, Padding * 2 + Well - 4, Padding, width, variant ? 30 : Well);
            // An upgrade shows its caption above the requirements.
            float bottom = upgrade ? UpgradeTop : RequirementsTop;
            description.anchorMin = Vector2.zero;
            description.anchorMax = Vector2.one;
            description.pivot = new Vector2(0.5f, 1);
            description.offsetMin = new Vector2(Padding, bottom);
            description.offsetMax = new Vector2(-Padding, -HeaderBottom - 10);
        }
        bool selected = gui.m_recipeName.enabled;
        well.enabled = divider.enabled = selected;
        requirementsLabel.enabled = requirementsRule.enabled = selected && !upgrade;
    }

    // Use the tooltip's warmer stat colors in the native recipe text.
    private void Recolor()
    {
        var text = gui.m_recipeDecription;
        if (!text || text.text == lastStyled) return;
        string native = text.text;
        if (native != lastNative)
        {
            lastNative = native;
            lastStyled = native.Replace("<color=orange>", "<color=#E8BC77>").Replace("<color=yellow>", "<color=#F0D59B>");
        }
        if (lastStyled != native) text.text = lastStyled;
    }

    public void Dispose()
    {
        if (current == this) current = null;
        if (repairTooltip) repairTooltip.m_text = repairText;
        ItemUi.Destroy(well);
        ItemUi.Destroy(divider);
        ItemUi.Destroy(requirementsRule);
        ItemUi.Destroy(requirementsLabel);
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    private static class RecipeText
    {
        private static void Postfix(InventoryGui __instance)
        {
            var card = current;
            if (card?.gui != __instance) return;
            card.Recolor();
            card.Refresh();
        }
    }
}
