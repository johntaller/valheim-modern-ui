using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class Compass
{
    private const float HalfAngle = 65;
    private readonly RectTransform rect, heading, pins, details;
    private readonly Image shadow, focusedMarker;
    private readonly TextMeshProUGUI pinName, pinDistance;
    private readonly List<Image> markers = new();
    private readonly List<(Minimap.PinData Pin, float Angle, float Distance)> visiblePins = new();
    private readonly RectTransform[] ticks = new RectTransform[24];

    internal Compass(RectTransform parent, Artwork art, TMP_FontAsset font, TMP_FontAsset displayFont)
    {
        rect = ItemUi.Rect(parent, "Compass");
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, -23);
        shadow = HudStyle.Shadow(rect, art, 0.88f);
        heading = ItemUi.Rect(rect, "Heading");
        ItemUi.Stretch(heading, Vector2.zero, Vector2.zero);
        pins = ItemUi.Rect(rect, "MapPins");
        ItemUi.Stretch(pins, Vector2.zero, Vector2.zero);
        string[] directions = Localization.instance.GetSelectedLanguage() == "Russian"
            ? new[] { "С", "В", "Ю", "З" } : new[] { "N", "E", "S", "W" };
        for (int i = 0; i < ticks.Length; i++)
        {
            bool major = i % 6 == 0;
            var tick = ItemUi.Rect(heading, "Bearing" + i);
            tick.sizeDelta = new Vector2(24, 23);
            ticks[i] = tick;
            if (major)
            {
                var label = ItemUi.Text(tick, "Direction", displayFont, 16, ItemUi.Ink);
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.text = directions[i / 6];
                ItemUi.Stretch(label.rectTransform, new Vector2(0, 5), Vector2.zero);
            }
            var mark = ItemUi.Image(tick, "Tick");
            mark.color = major ? ItemUi.Accent : new Color32(186, 177, 157, 155);
            ItemUi.TopLeft(mark.rectTransform, 11, major ? 18 : 14, 2, major ? 5 : 4);
        }
        var center = ItemUi.Image(rect, "Center", art["reticle"]);
        center.color = ItemUi.Accent;
        center.rectTransform.anchorMin = center.rectTransform.anchorMax = new Vector2(0.5f, 0);
        center.rectTransform.anchoredPosition = new Vector2(0, 4);
        center.rectTransform.sizeDelta = new Vector2(14, 14);

        focusedMarker = ItemUi.Image(rect, "FocusedMapPin");
        focusedMarker.preserveAspect = true;
        var outline = focusedMarker.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(239, 187, 93, 216);
        outline.effectDistance = new Vector2(1.25f, 1.25f);
        details = ItemUi.Rect(rect, "Target");
        HudStyle.Center(details, 0, -43, 390, 46);
        var detailShadow = HudStyle.Shadow(details, art, 0.80f);
        HudStyle.Center(detailShadow.rectTransform, 0, 0, 452, 122);
        pinName = ItemUi.Text(details, "PinName", displayFont, 22, ItemUi.Ink);
        pinName.richText = false;
        pinName.alignment = TextAlignmentOptions.Center;
        pinName.overflowMode = TextOverflowModes.Ellipsis;
        pinName.textWrappingMode = TextWrappingModes.NoWrap;
        ItemUi.TopLeft(pinName.rectTransform, 0, 2, 390, 25);
        pinDistance = ItemUi.Text(details, "Distance", font, 15, ItemUi.Accent);
        pinDistance.alignment = TextAlignmentOptions.Center;
        ItemUi.TopLeft(pinDistance.rectTransform, 0, 26, 390, 18);
    }

    internal void Hide() => rect.gameObject.SetActive(false);

    internal void Refresh(Player player, List<Minimap.PinData> source, bool[] visibleTypes, float sharedFade, float highlightDistance)
    {
        if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
        float width = Mathf.Clamp(((RectTransform)rect.parent).rect.width * 0.31f, 190, 430);
        rect.sizeDelta = new Vector2(width, 43);
        HudStyle.Center(shadow.rectTransform, 0, 0, width * 1.45f, 112);
        float scale = (width * 0.5f - 13) / HalfAngle;
        float facing = (GameCamera.instance ? GameCamera.instance.transform : player.transform).eulerAngles.y;

        for (int i = 0; i < ticks.Length; i++)
        {
            float angle = Mathf.DeltaAngle(facing, i * 15);
            bool visible = Mathf.Abs(angle) <= HalfAngle;
            if (ticks[i].gameObject.activeSelf != visible) ticks[i].gameObject.SetActive(visible);
            if (visible) ticks[i].anchoredPosition = new Vector2(angle * scale, 9);
        }

        visiblePins.Clear();
        if (source != null)
        {
            foreach (var pin in source)
            {
                int type = (int)pin.m_type;
                if (!pin.m_icon || pin.m_shouldDelete || pin.m_worldSize > 0 || (pin.m_ownerID != 0 && sharedFade <= 0)
                    || (visibleTypes != null && (type < 0 || type >= visibleTypes.Length || !visibleTypes[type]))) continue;
                Vector3 offset = pin.m_pos - player.transform.position;
                float distance = new Vector2(offset.x, offset.z).magnitude;
                if (distance < 1) continue;
                float angle = Mathf.DeltaAngle(facing, Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg);
                if (Mathf.Abs(angle) <= HalfAngle) visiblePins.Add((pin, angle, distance));
            }
        }
        // Draw far pins before near pins so overlap also communicates proximity.
        visiblePins.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        int focused = -1;
        float closestAngle = 9 / scale;
        for (int i = 0; i < visiblePins.Count; i++)
        {
            float angle = Mathf.Abs(visiblePins[i].Angle);
            if (visiblePins[i].Distance > highlightDistance || angle > closestAngle) continue;
            closestAngle = angle;
            focused = i;
        }
        for (int i = 0; i < visiblePins.Count; i++)
        {
            if (i == markers.Count)
            {
                var marker = ItemUi.Image(pins, "BearingPin");
                marker.preserveAspect = true;
                markers.Add(marker);
            }
            var (pin, angle, distance) = visiblePins[i];
            var image = markers[i];
            float near = 1 / (1 + distance / 140);
            float brightness = 0.40f + 0.60f * near;
            image.sprite = pin.m_icon;
            image.color = new Color(brightness, brightness, brightness,
                Mathf.Lerp(0.62f, 1, near) * (pin.m_ownerID != 0 ? sharedFade : 1));
            image.rectTransform.anchoredPosition = new Vector2(angle * scale, -8);
            image.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(14, 20, near);
            image.enabled = i != focused;
        }
        for (int i = visiblePins.Count; i < markers.Count; i++) markers[i].enabled = false;
        focusedMarker.enabled = focused >= 0;
        details.gameObject.SetActive(focused >= 0);
        if (focused >= 0)
        {
            var (pin, angle, distance) = visiblePins[focused];
            focusedMarker.sprite = pin.m_icon;
            focusedMarker.color = ItemUi.Ink;
            HudStyle.Center(focusedMarker.rectTransform, angle * scale, -8, 24, 24);
            string name = string.IsNullOrWhiteSpace(pin.m_name)
                ? Localization.instance.GetSelectedLanguage() == "Russian" ? "Метка" : "Map marker"
                : Localization.instance.Localize(pin.m_name);
            if (pinName.text != name) pinName.text = name;
            pinDistance.SetText(Localization.instance.GetSelectedLanguage() == "Russian" ? "{0} м" : "{0} m",
                Mathf.RoundToInt(distance));
        }
    }
}
