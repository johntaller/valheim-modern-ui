using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Shows an item's quality as a soft backlight behind its icon: none at level 1, then
// green, blue, purple and red, and gold from level 6.
internal sealed class ItemQualityBadge
{
    private readonly Image glow;
    private readonly HiddenGraphics original;
    private int shownQuality = -1;

    internal ItemQualityBadge(RectTransform slot, Artwork art, TMP_FontAsset font, TMP_Text nativeQuality = null,
        Graphic icon = null)
    {
        if (nativeQuality) original = new HiddenGraphics(nativeQuality.gameObject);
        glow = ItemUi.Image(slot, "NewUI_QualityGlow", art["glow"]);
        ItemUi.Stretch(glow.rectTransform, Vector2.one * 3, Vector2.one * -3);
        // Behind the icon, above the slot face.
        if (icon && icon.transform.parent == slot) glow.transform.SetSiblingIndex(icon.transform.GetSiblingIndex());
        else glow.transform.SetAsFirstSibling();
        glow.enabled = false;
    }

    internal static bool HasLevels(ItemDrop.ItemData item) =>
        item != null && (item.m_shared.m_maxQuality > 1 || item.m_quality > 1);

    internal static Color Tint(int quality) => quality switch
    {
        <= 1 => ItemUi.Muted,
        2 => new Color32(92, 214, 104, 255),
        3 => new Color32(84, 150, 255, 255),
        4 => new Color32(180, 98, 255, 255),
        5 => new Color32(244, 66, 66, 255),
        _ => new Color32(255, 204, 84, 255),
    };

    internal void Refresh(ItemDrop.ItemData item)
    {
        int quality = HasLevels(item) ? item.m_quality : 0;
        if (shownQuality == quality) return;
        shownQuality = quality;
        glow.enabled = quality > 1;
        if (!glow.enabled) return;
        var tint = Tint(quality);
        tint.a = 0.6f;
        glow.color = tint;
    }

    internal void Dispose()
    {
        original?.Restore();
        ItemUi.Destroy(glow);
    }
}
