using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class VitalsView
{
    private readonly RectTransform root;
    private readonly Meter health;
    private readonly Image reticle;
    private readonly StaminaArcs stamina;
    private readonly HashSet<Graphic> hidden = new();

    internal VitalsView(Hud hud, Canvas canvas, Theme theme)
    {
        root = Node("ValheimNewUI", canvas.rootCanvas.transform);
        root.gameObject.SetActive(false);
        try
        {
            // Keep the game's menus, fade and loading screen above the meters.
            root.SetAsFirstSibling();
            health = new Meter(root, hud.m_healthText);
            reticle = hud.m_crosshair;
            var arcs = ItemUi.Rect(reticle.rectTransform, "NewUI_StaminaArcs");
            arcs.gameObject.SetActive(false);
            arcs.anchorMin = arcs.anchorMax = arcs.pivot = new Vector2(0.5f, 0.5f);
            arcs.anchoredPosition = Vector2.zero;
            arcs.sizeDelta = new Vector2(60, 60);
            stamina = arcs.gameObject.AddComponent<StaminaArcs>();
            stamina.raycastTarget = false;
            Apply(theme);
            HideBar(hud.m_healthBarRoot);
            HideBar(hud.m_staminaBar2Root);
            HideGraphic(hud.m_healthText);
            HideGraphic(hud.m_staminaText);
            root.gameObject.SetActive(true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal void Apply(Theme theme)
    {
        root.anchorMin = root.anchorMax = root.pivot = theme.Anchor;
        root.anchoredPosition = theme.Offset;
        root.sizeDelta = new Vector2(theme.Width, theme.HealthTop);
        health.Style(theme);
        stamina.Style(theme);
    }

    internal void Refresh(Player player, bool visible)
    {
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (visible) health.Value(player.GetHealth(), player.GetMaxHealth());

        bool showStamina = visible && reticle && reticle.isActiveAndEnabled && !Menu.IsVisible() && !Console.IsVisible();
        if (stamina.gameObject.activeSelf != showStamina) stamina.gameObject.SetActive(showStamina);
        if (showStamina) stamina.Value(player.GetStamina(), player.GetMaxStamina());
    }

    private void HideBar(RectTransform bar)
    {
        foreach (var graphic in bar.GetComponentsInChildren<Graphic>(true)) HideGraphic(graphic);
    }

    private void HideGraphic(Graphic graphic)
    {
        if (!graphic || !graphic.enabled || !hidden.Add(graphic)) return;
        graphic.enabled = false;
    }

    internal void Dispose()
    {
        foreach (var graphic in hidden)
            if (graphic) graphic.enabled = true;
        hidden.Clear();
        ItemUi.Destroy(stamina);
        ItemUi.Destroy(root);
    }

    private static RectTransform Node(string name, Transform parent)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static Image Layer(string name, Transform parent)
    {
        var image = Node(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 from, Vector2 to)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = from;
        rect.offsetMax = to;
    }

    private sealed class Meter
    {
        private readonly Image frame, track, fill, markA, markB;
        private readonly RectTransform icon;
        private readonly TextMeshProUGUI label, value;
        private int lastCurrent = -1, lastMax = -1;
        private float lastRatio = -1;

        internal Meter(Transform parent, TMP_Text template)
        {
            frame = Layer("HealthFrame", parent);
            track = Layer("Background", frame.transform);
            fill = Layer("Fill", track.transform);
            Stretch(fill.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            icon = Node("Icon", frame.transform);
            icon.anchorMin = icon.anchorMax = new Vector2(0, 0.5f);
            icon.sizeDelta = new Vector2(14, 14);
            icon.anchoredPosition = new Vector2(16, 0);
            markA = Layer("StrokeA", icon);
            markB = Layer("StrokeB", icon);
            markA.rectTransform.sizeDelta = new Vector2(12, 3);
            markB.rectTransform.sizeDelta = new Vector2(3, 12);

            label = MakeText("Label", template, TextAlignmentOptions.MidlineLeft);
            value = MakeText("Value", template, TextAlignmentOptions.MidlineRight);
            Stretch(label.rectTransform, Vector2.zero, new Vector2(0.60f, 1), new Vector2(29, 1), new Vector2(-2, -1));
            Stretch(value.rectTransform, new Vector2(0.60f, 0), Vector2.one, new Vector2(0, 1), new Vector2(-9, -1));
        }

        private TextMeshProUGUI MakeText(string name, TMP_Text template, TextAlignmentOptions alignment)
        {
            var text = Node(name, frame.transform).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = template.font;
            text.raycastTarget = false;
            text.richText = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 9;
            text.alignment = alignment;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        internal void Style(Theme theme)
        {
            var rect = frame.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(theme.Width, theme.Height);
            rect.anchoredPosition = new Vector2(0, theme.HealthTop - theme.Height);
            var inset = Vector2.one * theme.Padding;
            Stretch(track.rectTransform, Vector2.zero, Vector2.one, inset, -inset);
            frame.color = theme.Frame;
            track.color = theme.Background;
            fill.color = theme.Health;
            label.color = value.color = markA.color = markB.color = theme.Text;
            label.fontSizeMax = value.fontSizeMax = Mathf.Min(theme.Height - 5, 22);
            label.text = theme.HealthLabel;
        }

        internal void Value(float current, float max)
        {
            float ratio = max > 0 ? Mathf.Clamp01(current / max) : 0;
            if (ratio != lastRatio)
            {
                fill.rectTransform.anchorMax = new Vector2(ratio, 1);
                lastRatio = ratio;
            }
            int capacity = Mathf.Max(0, Mathf.CeilToInt(max));
            int amount = Mathf.Clamp(Mathf.CeilToInt(current), 0, capacity);
            if (amount == lastCurrent && capacity == lastMax) return;
            value.SetText("{0} / {1}", amount, capacity);
            lastCurrent = amount;
            lastMax = capacity;
        }
    }
}
