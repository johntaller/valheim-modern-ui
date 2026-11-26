using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Image = UnityEngine.UI.Image;

namespace ValheimNewUI;

internal sealed class ItemTooltip
{
    // Valheim shares a single popup between every UITooltip. Keep its hover delay,
    // focus and dismissal, then supply a card only when the owner is an item cell.
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly FieldInfo CurrentField = typeof(UITooltip).GetField("m_current", PrivateStatic);
    private static readonly FieldInfo PopupField = typeof(UITooltip).GetField("m_tooltip", PrivateStatic);

    private readonly Canvas canvas;
    private readonly RectTransform card;
    private readonly Image surface, header, icon, rule, banner, bannerEdge, footerRule;
    private readonly TextMeshProUGUI title, quality, details, warning, footer, stack, weight;
    private readonly Image qualityGlow;
    private readonly Image weightIcon;
    private readonly Image durabilityTrack, durabilityFill;
    private ItemDrop.ItemData lastItem;
    private Sprite lastSprite;
    private string lastText, lastTopic, lastFooter;
    private bool lastCheated;
    private int lastStack;
    private float lastWidth, lastMaxHeight;

    internal ItemTooltip(Canvas canvas, Artwork art, TMP_FontAsset font)
    {
        this.canvas = canvas;
        card = ItemUi.Rect(canvas.transform, "NewUI_ItemTooltip");
        surface = ItemUi.Image(card, "Card", art["item-card"]);
        header = ItemUi.Image(card, "IconWell", art["slot"]);
        qualityGlow = ItemUi.Image(header.transform, "QualityGlow", art["glow"]);
        ItemUi.Stretch(qualityGlow.rectTransform, Vector2.one * 3, -Vector2.one * 3);
        icon = ItemUi.Image(header.transform, "Icon");
        icon.preserveAspect = true;
        ItemUi.Stretch(icon.rectTransform, Vector2.one * 5, -Vector2.one * 5);
        title = ItemUi.Text(card, "ItemName", HudStyle.BrandFont(font), 22, ItemUi.Ink);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.textWrappingMode = TextWrappingModes.Normal;
        title.enableAutoSizing = true;
        title.fontSizeMin = 14;
        title.fontSizeMax = 22;
        quality = ItemUi.Text(card, "Quality", font, 14, ItemUi.Muted);
        quality.fontStyle = FontStyles.Bold;
        quality.alignment = TextAlignmentOptions.TopLeft;
        quality.textWrappingMode = TextWrappingModes.NoWrap;
        rule = ItemUi.Image(card, "Divider");
        rule.color = new Color32(192, 157, 97, 155);
        details = ItemUi.Text(card, "DescriptionAndStats", font, 16, ItemUi.Muted);
        details.alignment = TextAlignmentOptions.TopLeft;
        details.textWrappingMode = TextWrappingModes.Normal;
        details.overflowMode = TextOverflowModes.Ellipsis;
        banner = ItemUi.Image(card, "CheatWarning");
        banner.color = new Color32(151, 38, 42, 255);
        bannerEdge = ItemUi.Image(banner.transform, "TopBorder");
        bannerEdge.color = ItemUi.Warning;
        warning = ItemUi.Text(banner.transform, "WarningText", font, 14, Color.white);
        warning.fontStyle = FontStyles.Bold;
        warning.alignment = TextAlignmentOptions.Center;
        warning.enableAutoSizing = true;
        warning.fontSizeMin = 11;
        warning.fontSizeMax = 14;
        ItemUi.Stretch(warning.rectTransform, new Vector2(8, 2), new Vector2(-8, -2));
        footerRule = ItemUi.Image(card, "PickupDivider");
        footerRule.color = rule.color;
        footer = ItemUi.Text(card, "PickupBinding", font, 15, ItemUi.Ink);
        footer.alignment = TextAlignmentOptions.Center;
        footer.fontStyle = FontStyles.Bold;
        stack = ItemUi.Text(card, "StackQuantity", font, 17, ItemUi.Accent);
        stack.alignment = TextAlignmentOptions.MidlineRight;
        weight = ItemUi.Text(card, "Weight", font, 15, ItemUi.Ink);
        weight.alignment = TextAlignmentOptions.MidlineRight;
        weightIcon = ItemUi.Image(card, "WeightIcon", art["weight"]);
        weightIcon.color = ItemUi.Accent;
        durabilityTrack = ItemUi.Image(card, "DurabilityTrack", art["slot"]);
        durabilityFill = ItemUi.Image(durabilityTrack.transform, "DurabilityFill", art["meter-fill"]);
        durabilityFill.type = Image.Type.Filled;
        durabilityFill.fillMethod = Image.FillMethod.Horizontal;
        ItemUi.Stretch(durabilityFill.rectTransform, Vector2.one * 2, -Vector2.one * 2);
        card.gameObject.SetActive(false);
    }

    internal static UITooltip Current => CurrentField?.GetValue(null) as UITooltip;

    internal void Refresh(UITooltip owner, RectTransform slot, ItemDrop.ItemData item)
    {
        var popup = PopupField?.GetValue(null) as GameObject;
        if (owner && slot && item == null && popup && popup.activeInHierarchy)
            UITooltip.HideTooltip();
        if (!owner || item == null || !popup || !popup.activeInHierarchy)
        {
            Hide();
            return;
        }
        bool anchored = ZInput.IsExclusiveGamepadActive() || ZInput.IsTouchActive();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = anchored ? RectTransformUtility.WorldToScreenPoint(camera, slot.TransformPoint(slot.rect.center)) : ZInput.pointerPosition;
        Show(item, null, owner.m_topic, owner.m_text, "", screen);
    }

    internal void RefreshPointer(UITooltip owner, ItemDrop.ItemData item)
    {
        if (item == null || !owner)
        {
            Hide();
            return;
        }
        // The pointer card covers the entire cell, independently of the stock
        // tooltip's input rectangle. Gamepad and touch keep the native owner.
        if (Current == owner && PopupField?.GetValue(null) is GameObject popup && popup.activeInHierarchy)
        {
            Refresh(owner, (RectTransform)owner.transform, item);
            return;
        }
        string text = owner.m_text;
        if (string.IsNullOrEmpty(text))
            text = item == lastItem && card.gameObject.activeSelf ? lastText : item.GetTooltip();
        Show(item, null, item.m_shared.m_name, text, "", ZInput.pointerPosition);
    }

    internal void RefreshWorld(ItemDrop.ItemData item, Vector2 screen, string pickupHint)
    {
        if (item == null)
        {
            Hide();
            return;
        }
        string text = item == lastItem && card.gameObject.activeSelf ? lastText : item.GetTooltip();
        Show(item, null, item.m_shared.m_name, text, pickupHint, screen);
    }

    // A card in the same style for something that is not an item: a title with an optional icon,
    // the details, and an optional line at the bottom.
    internal void RefreshInfo(Sprite sprite, string topic, string text, string footnote, Vector2 screen) =>
        Show(null, sprite, topic, text ?? "", footnote ?? "", screen, 18);

    // An item card next to the pointer, with a line of its own at the bottom.
    internal void RefreshItem(ItemDrop.ItemData item, string footnote, Vector2 screen)
    {
        if (item == null)
        {
            Hide();
            return;
        }
        string text = item == lastItem && card.gameObject.activeSelf ? lastText : item.GetTooltip();
        Show(item, null, item.m_shared.m_name, text, footnote ?? "", screen, 18);
    }

    internal void Clear() => Hide();

    private void Show(ItemDrop.ItemData item, Sprite sprite, string topic, string text, string pickupHint, Vector2 screen,
        float offset = -1)
    {
        if (!card.gameObject.activeSelf) card.gameObject.SetActive(true);
        var canvasRect = (RectTransform)canvas.transform;
        var bounds = canvasRect.rect;
        float width = Mathf.Max(1, Mathf.Min(340, bounds.width - 24));
        float maxHeight = Mathf.Max(1, bounds.height - 24);
        bool cheated = item != null && item.m_cheated;
        int count = item?.m_stack ?? 0;
        if (lastItem != item || lastSprite != sprite || lastText != text || lastTopic != topic || lastFooter != pickupHint
            || lastCheated != cheated || lastStack != count || lastWidth != width || lastMaxHeight != maxHeight)
        {
            lastItem = item;
            lastSprite = sprite;
            lastStack = count;
            lastText = text;
            lastTopic = topic;
            lastFooter = pickupHint;
            lastCheated = cheated;
            lastWidth = width;
            lastMaxHeight = maxHeight;
            Layout(item, sprite, width, maxHeight);
        }
        if (item != null && durabilityTrack.enabled)
        {
            float remaining = Mathf.Clamp01(item.GetDurabilityPercentage());
            durabilityFill.fillAmount = remaining > 0 ? remaining : 1;
            durabilityFill.color = DurabilityEdge.MeterColor(remaining);
        }
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, camera, out var pointer);
        float height = card.rect.height;
        if (offset < 0) offset = pickupHint.Length > 0 ? 62 : 18;
        float x = pointer.x + offset;
        if (x + width > bounds.xMax - 12) x = pointer.x - width - offset;
        float y = pointer.y - 16;
        if (y - height < bounds.yMin + 12) y = pointer.y + height + 18;
        x = Mathf.Clamp(x, bounds.xMin + 12, Mathf.Max(bounds.xMin + 12, bounds.xMax - width - 12));
        y = Mathf.Clamp(y, Mathf.Min(bounds.yMax - 12, bounds.yMin + height + 12), bounds.yMax - 12);
        card.localPosition = new Vector3(x, y);
        if (card.GetSiblingIndex() != canvas.transform.childCount - 1) card.SetAsLastSibling();
    }

    private void Layout(ItemDrop.ItemData item, Sprite sprite, float width, float maxHeight)
    {
        const int padding = 16;
        bool isItem = item != null;
        int bannerHeight = isItem && item.m_cheated ? 26 : 0;
        int start = bannerHeight + 12;
        int bodyY = start + 60;
        float bodyWidth = Mathf.Max(1, width - padding * 2);
        string warningText = Localization.instance.Localize("$achievements_cheated_item_inventory");
        string nativeWarning = "\n<color=#808080><i>" + warningText + "</i></color>";
        string text = CleanDescription(lastText.Replace(nativeWarning, ""));
        text = text.Replace("<color=orange>", "<color=#E8BC77>").Replace("<color=yellow>", "<color=#F0D59B>");
        title.text = Localization.instance.Localize(lastTopic);
        details.text = text;
        icon.sprite = isItem ? item.GetIcon() : sprite;
        icon.enabled = icon.sprite;
        header.enabled = icon.enabled || isItem;
        banner.gameObject.SetActive(bannerHeight > 0);
        if (bannerHeight > 0) warning.text = warningText;

        bool withFooter = lastFooter.Length > 0;
        const int footerHeight = 36;
        stack.enabled = weight.enabled = weightIcon.enabled = isItem;
        durabilityTrack.enabled = durabilityFill.enabled = isItem && item.m_shared.m_useDurability;
        stack.text = isItem ? $"×{item.m_stack}" : "";
        weight.text = isItem ? $"{item.GetWeight():0.##} " + MenuStyle.Translate("kg", "кг") : "";
        float rightColumn = isItem ? Mathf.Max(65, Mathf.Ceil(stack.GetPreferredValues(stack.text).x) + 11) : 0;
        // Item cards always keep the bottom row for the weight; other cards only when they have a line there.
        float bottomSpace = isItem || withFooter ? padding + footerHeight : padding;
        float available = Mathf.Max(1, maxHeight - bodyY - bottomSpace);
        details.fontSize = 16;
        ItemUi.TopLeft(details.rectTransform, padding, bodyY, bodyWidth, available);
        // Initialize TMP's text and font metrics before measuring the first card.
        details.ForceMeshUpdate(forceTextReparsing: true);
        float bodyHeight = details.GetPreferredValues(text, bodyWidth, Mathf.Infinity).y;
        while (bodyHeight > available && details.fontSize > 12)
        {
            details.fontSize--;
            bodyHeight = details.GetPreferredValues(text, bodyWidth, Mathf.Infinity).y;
        }
        float height = Mathf.Min(maxHeight, Mathf.Ceil(bodyY + bodyHeight + bottomSpace));
        ItemUi.TopLeft(card, 0, 0, width, height);
        ItemUi.TopLeft(banner.rectTransform, 0, 0, width, bannerHeight + 2);
        ItemUi.TopLeft(bannerEdge.rectTransform, 0, 0, width, 2);
        ItemUi.TopLeft(surface.rectTransform, 0, bannerHeight, width, Mathf.Max(1, height - bannerHeight));
        ItemUi.TopLeft(header.rectTransform, padding, start, 42, 42);
        // Items with levels name theirs under the title, in the color of the slot backlight.
        bool levels = isItem && ItemQualityBadge.HasLevels(item);
        float titleLeft = header.enabled ? 54 : 0;
        float titleWidth = Mathf.Max(1, bodyWidth - titleLeft - rightColumn);
        ItemUi.TopLeft(title.rectTransform, padding + titleLeft, start, titleWidth, levels ? 26 : 42);
        quality.enabled = levels;
        qualityGlow.enabled = levels && item.m_quality > 1;
        if (levels)
        {
            var tint = ItemQualityBadge.Tint(item.m_quality);
            quality.color = tint;
            quality.text = MenuStyle.Translate("Quality ", "Качество ") + item.m_quality +
                (item.m_shared.m_maxQuality > 1 ? $" / {item.m_shared.m_maxQuality}" : "");
            tint.a = 0.8f;
            qualityGlow.color = tint;
            ItemUi.TopLeft(quality.rectTransform, padding + 54, start + 26, titleWidth, 17);
        }
        ItemUi.TopLeft(stack.rectTransform, width - padding - rightColumn, start, rightColumn, 23);
        ItemUi.TopLeft(durabilityTrack.rectTransform, width - padding - rightColumn, start + 27, rightColumn, 8);
        ItemUi.TopLeft(rule.rectTransform, padding, start + 51, bodyWidth, 1);
        ItemUi.TopLeft(details.rectTransform, padding, bodyY, bodyWidth, Mathf.Max(1, height - bodyY - bottomSpace + 5));
        footer.enabled = withFooter;
        footer.text = lastFooter;
        ItemUi.TopLeft(footerRule.rectTransform, padding, height - footerHeight - 7, bodyWidth, 1);
        footerRule.enabled = isItem || withFooter;
        float weightWidth = isItem ? Mathf.Ceil(weight.GetPreferredValues(weight.text).x) : 0;
        footer.alignment = TextAlignmentOptions.MidlineLeft;
        footer.enableAutoSizing = true;
        footer.fontSizeMin = 11;
        footer.fontSizeMax = 15;
        float footerTop = height - footerHeight - 4;
        ItemUi.TopLeft(footer.rectTransform, padding, footerTop, Mathf.Max(1, bodyWidth - (isItem ? weightWidth + 30 : 0)), footerHeight);
        ItemUi.TopLeft(weight.rectTransform, width - padding - weightWidth, footerTop, weightWidth, footerHeight);
        ItemUi.TopLeft(weightIcon.rectTransform, width - padding - weightWidth - 21, footerTop + 10, 16, 16);
    }

    private static string CleanDescription(string text)
    {
        // InventoryGrid supplies Valheim's untranslated tooltip, including its stat tokens.
        var lines = text.Split('\n');
        return Localization.instance.Localize(string.Join("\n", Array.FindAll(lines, line =>
        {
            string label = line.TrimStart();
            return !label.StartsWith("$item_weight:", StringComparison.Ordinal)
                && !label.StartsWith("$item_durability:", StringComparison.Ordinal)
                && !label.StartsWith("$item_stack:", StringComparison.Ordinal)
                && !label.StartsWith("$item_quantity:", StringComparison.Ordinal)
                && !label.StartsWith("$item_quality:", StringComparison.Ordinal);
        }))).Trim();
    }

    private void Hide()
    {
        if (card && card.gameObject.activeSelf) card.gameObject.SetActive(false);
    }

    internal void Dispose()
    {
        Hide();
        ItemUi.Destroy(card);
    }
}

[HarmonyPatch]
internal static class InventoryTooltipPopup
{
    private static CanvasGroup mutedPopup;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.OnHoverStart))]
    [HarmonyPatch(typeof(UITooltip), "LateUpdate")]
    private static void SuppressItemPopup(UITooltip __instance, UITooltip ___m_current, GameObject ___m_tooltip)
    {
        if (__instance != ___m_current || !___m_tooltip) return;
        var cell = NativeMessagePatch.Enabled && InventoryGui.IsVisible()
            ? __instance.GetComponentInParent<InventoryElement>() : null;
        bool mute = cell && cell.m_tooltip == __instance;
        if (!mute || mutedPopup && mutedPopup.gameObject != ___m_tooltip) Restore();
        if (!mute || mutedPopup) return;
        // Keep Valheim's focus and timer alive while preventing any popup frame from drawing.
        mutedPopup = ___m_tooltip.AddComponent<CanvasGroup>();
        mutedPopup.alpha = 0;
        mutedPopup.blocksRaycasts = false;
    }

    internal static void Restore()
    {
        if (mutedPopup)
        {
            mutedPopup.alpha = 1;
            mutedPopup.blocksRaycasts = true;
            UnityEngine.Object.Destroy(mutedPopup);
        }
        mutedPopup = null;
    }
}
