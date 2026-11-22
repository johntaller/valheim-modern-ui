using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class Compass
{
    private const float HalfAngle = 65;
    private readonly RectTransform rect, heading, pins;
    private readonly List<Image> markers = new();
    private readonly List<(Minimap.PinData Pin, float Angle, float Distance)> visiblePins = new();
    private readonly RectTransform[] ticks = new RectTransform[24];

    internal Compass(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        rect = ItemUi.Image(parent, "Compass", art["item-card"]).rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, -23);
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
                var label = ItemUi.Text(tick, "Direction", font, 14, ItemUi.Ink);
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.text = directions[i / 6];
                ItemUi.Stretch(label.rectTransform, new Vector2(0, 5), Vector2.zero);
            }
            var mark = ItemUi.Image(tick, "Tick");
            mark.color = major ? ItemUi.Accent : new Color32(186, 177, 157, 155);
            ItemUi.TopLeft(mark.rectTransform, 11, major ? 18 : 14, 2, major ? 5 : 4);
        }
        var center = ItemUi.Image(rect, "Center");
        center.color = ItemUi.Accent;
        center.rectTransform.anchorMin = center.rectTransform.anchorMax = new Vector2(0.5f, 0);
        center.rectTransform.anchoredPosition = new Vector2(0, 2);
        center.rectTransform.sizeDelta = new Vector2(2, 5);
    }

    internal void Hide() => rect.gameObject.SetActive(false);

    internal void Refresh(Player player, List<Minimap.PinData> source, bool[] visibleTypes, float sharedFade)
    {
        if (!rect.gameObject.activeSelf) rect.gameObject.SetActive(true);
        float width = Mathf.Clamp(((RectTransform)rect.parent).rect.width * 0.31f, 190, 430);
        rect.sizeDelta = new Vector2(width, 43);
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
            image.enabled = true;
        }
        for (int i = visiblePins.Count; i < markers.Count; i++) markers[i].enabled = false;
    }
}
