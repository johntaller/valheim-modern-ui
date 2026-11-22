using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class ItemQualityBadge
{
    private static readonly Color32 Gold = new(255, 211, 115, 255);
    private readonly RectTransform root;
    private readonly TextMeshProUGUI level;
    private readonly HiddenGraphics original;
    private int shownQuality;

    internal ItemQualityBadge(RectTransform slot, Artwork art, TMP_FontAsset font, TMP_Text nativeQuality = null)
    {
        if (nativeQuality) original = new HiddenGraphics(nativeQuality.gameObject);
        root = ItemUi.Rect(slot, "NewUI_Quality");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        root.anchoredPosition = new Vector2(-4, -3);
        root.sizeDelta = new Vector2(27, 15);
        var star = ItemUi.Image(root, "Star", art["quality-star"]);
        star.color = Gold;
        ItemUi.TopLeft(star.rectTransform, 0, 1, 13, 13);
        var shade = star.gameObject.AddComponent<Shadow>();
        shade.effectColor = new Color(0, 0, 0, 0.9f);
        shade.effectDistance = new Vector2(0, -1);
        level = ItemUi.Text(root, "Level", font, 13, Gold);
        level.fontStyle = FontStyles.Bold;
        level.alignment = TextAlignmentOptions.MidlineLeft;
        level.textWrappingMode = TextWrappingModes.NoWrap;
        ItemUi.Stretch(level.rectTransform, new Vector2(14, 0), Vector2.zero);
        root.gameObject.SetActive(false);
    }

    internal void Refresh(ItemDrop.ItemData item)
    {
        int quality = item != null && (item.m_shared.m_maxQuality > 1 || item.m_quality > 1) ? item.m_quality : 0;
        if (shownQuality == quality) return;
        shownQuality = quality;
        root.gameObject.SetActive(quality > 0);
        if (quality <= 0) return;
        level.text = quality.ToString();
        root.sizeDelta = new Vector2(16 + level.GetPreferredValues(level.text).x, 15);
    }

    internal void Dispose()
    {
        original?.Restore();
        ItemUi.Destroy(root);
    }
}
