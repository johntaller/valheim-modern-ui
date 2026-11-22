using System.Reflection;
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
    private readonly Image surface, header, icon, rule, banner, bannerEdge;
    private readonly TextMeshProUGUI title, details, warning;
    private GameObject nativePopup;
    private HiddenGraphics nativeVisuals;
    private ItemDrop.ItemData lastItem;
    private string lastText, lastTopic;
    private bool lastCheated;
    private float lastWidth, lastMaxHeight;

    internal ItemTooltip(Canvas canvas, Artwork art, TMP_FontAsset font)
    {
        this.canvas = canvas;
        card = ItemUi.Rect(canvas.transform, "NewUI_ItemTooltip");
        surface = ItemUi.Image(card, "Card", art["item-card"]);
        header = ItemUi.Image(card, "IconWell", art["slot"]);
        icon = ItemUi.Image(header.transform, "Icon");
        icon.preserveAspect = true;
        ItemUi.Stretch(icon.rectTransform, Vector2.one * 5, -Vector2.one * 5);
        title = ItemUi.Text(card, "ItemName", font, 22, ItemUi.Ink);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.textWrappingMode = TextWrappingModes.Normal;
        title.enableAutoSizing = true;
        title.fontSizeMin = 14;
        title.fontSizeMax = 22;
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
        if (popup != nativePopup)
        {
            nativeVisuals?.Restore();
            nativePopup = popup;
            nativeVisuals = new HiddenGraphics(popup);
            nativeVisuals.SetRaycasts(false);
        }

        if (!card.gameObject.activeSelf) card.gameObject.SetActive(true);
        var canvasRect = (RectTransform)canvas.transform;
        var bounds = canvasRect.rect;
        float width = Mathf.Max(1, Mathf.Min(340, bounds.width - 24));
        float maxHeight = Mathf.Max(1, bounds.height - 24);
        if (lastItem != item || lastText != owner.m_text || lastTopic != owner.m_topic
            || lastCheated != item.m_cheated || lastWidth != width || lastMaxHeight != maxHeight)
        {
            lastItem = item;
            lastText = owner.m_text;
            lastTopic = owner.m_topic;
            lastCheated = item.m_cheated;
            lastWidth = width;
            lastMaxHeight = maxHeight;
            Layout(item, width, maxHeight);
        }

        bool anchored = ZInput.IsExclusiveGamepadActive() || ZInput.IsTouchActive();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screen = anchored ? RectTransformUtility.WorldToScreenPoint(camera, slot.TransformPoint(slot.rect.center)) : ZInput.pointerPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, camera, out var pointer);
        float height = card.rect.height;
        float x = pointer.x + 18;
        if (x + width > bounds.xMax - 12) x = pointer.x - width - 18;
        float y = pointer.y - 16;
        if (y - height < bounds.yMin + 12) y = pointer.y + height + 18;
        x = Mathf.Clamp(x, bounds.xMin + 12, Mathf.Max(bounds.xMin + 12, bounds.xMax - width - 12));
        y = Mathf.Clamp(y, Mathf.Min(bounds.yMax - 12, bounds.yMin + height + 12), bounds.yMax - 12);
        card.localPosition = new Vector3(x, y);
        if (card.GetSiblingIndex() != canvas.transform.childCount - 1) card.SetAsLastSibling();
    }

    private void Layout(ItemDrop.ItemData item, float width, float maxHeight)
    {
        const int padding = 16;
        int bannerHeight = item.m_cheated ? 26 : 0;
        int start = bannerHeight + 12;
        int bodyY = start + 60;
        float bodyWidth = Mathf.Max(1, width - padding * 2);
        string warningText = Localization.instance.Localize("$achievements_cheated_item_inventory");
        string nativeWarning = "\n<color=#808080><i>" + warningText + "</i></color>";
        string text = Localization.instance.Localize(lastText.Replace(nativeWarning, "")).Trim();
        text = text.Replace("<color=orange>", "<color=#E8BC77>").Replace("<color=yellow>", "<color=#F0D59B>");
        title.text = Localization.instance.Localize(lastTopic);
        details.text = text;
        icon.sprite = item.GetIcon();
        icon.enabled = icon.sprite;
        banner.gameObject.SetActive(item.m_cheated);
        if (item.m_cheated) warning.text = warningText;

        float available = Mathf.Max(1, maxHeight - bodyY - padding);
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
        float height = Mathf.Min(maxHeight, Mathf.Ceil(bodyY + bodyHeight + padding));
        ItemUi.TopLeft(card, 0, 0, width, height);
        ItemUi.TopLeft(banner.rectTransform, 0, 0, width, bannerHeight + 2);
        ItemUi.TopLeft(bannerEdge.rectTransform, 0, 0, width, 2);
        ItemUi.TopLeft(surface.rectTransform, 0, bannerHeight, width, Mathf.Max(1, height - bannerHeight));
        ItemUi.TopLeft(header.rectTransform, padding, start, 42, 42);
        ItemUi.TopLeft(title.rectTransform, padding + 54, start, Mathf.Max(1, bodyWidth - 54), 42);
        ItemUi.TopLeft(rule.rectTransform, padding, start + 51, bodyWidth, 1);
        ItemUi.TopLeft(details.rectTransform, padding, bodyY, bodyWidth, Mathf.Max(1, height - bodyY - 9));
    }

    private void Hide()
    {
        if (card && card.gameObject.activeSelf) card.gameObject.SetActive(false);
        nativeVisuals?.Restore();
        nativeVisuals = null;
        nativePopup = null;
    }

    internal void Dispose()
    {
        Hide();
        ItemUi.Destroy(card);
    }
}
