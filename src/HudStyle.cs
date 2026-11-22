using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal static class HudStyle
{
    internal static TMP_FontAsset DisplayFont(TMP_FontAsset fallback)
    {
        var button = Menu.instance ? Menu.instance.m_continueButton : null;
        var label = button ? button.GetComponentInChildren<TMP_Text>(true) : null;
        return label && label.font ? label.font : fallback;
    }

    internal static Image Shadow(Transform parent, Artwork art, float opacity)
    {
        var shadow = ItemUi.Image(parent, "Shadow", art["radial-shadow"]);
        shadow.color = new Color(1, 1, 1, opacity);
        return shadow;
    }

    internal static Scrollbar Scrollbar(Transform parent, Artwork art, string name)
    {
        var rail = ItemUi.Image(parent, name, art["slot"]);
        rail.raycastTarget = true;
        var slider = ItemUi.Rect(rail.transform, "SlidingArea");
        ItemUi.Stretch(slider, Vector2.zero, Vector2.zero);
        var handle = ItemUi.Image(slider, "Handle", art["meter-fill"]);
        handle.color = ItemUi.Accent;
        handle.raycastTarget = true;
        ItemUi.Stretch(handle.rectTransform, Vector2.zero, Vector2.zero);
        var scroll = rail.gameObject.AddComponent<Scrollbar>();
        scroll.handleRect = handle.rectTransform;
        scroll.targetGraphic = handle;
        scroll.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scroll.navigation = new Navigation { mode = Navigation.Mode.None };
        return scroll;
    }

    internal static void Center(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
