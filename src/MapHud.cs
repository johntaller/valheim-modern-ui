using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class MapHud : IDisposable
{
    private static readonly System.Reflection.FieldInfo Pins = AccessTools.Field(typeof(Minimap), "m_pins");
    private static readonly System.Reflection.FieldInfo VisibleTypes = AccessTools.Field(typeof(Minimap), "m_visibleIconTypes");
    private static readonly System.Reflection.FieldInfo SharedFade = AccessTools.Field(typeof(Minimap), "m_sharedMapDataFade");

    private readonly RectTransform root, area, pinRoot, mapRoot, caption;
    // The ring artwork, in its source pixels: the inner circle frames the map and the small
    // circle below it holds the wind indicator.
    private const float RingWidth = 865, RingHeight = 961, RingInner = 396.7f, RingOuter = 420;
    private static readonly Vector2 RingCenter = new(432.4f, 433.5f), WindCenter = new(432.4f, 865.8f);
    private const float WindRadius = 68.3f;
    // The map reaches into the middle of the band so the frame always covers its edge.
    private const float MapRadius = (RingInner + RingOuter) * 0.5f;

    private readonly CircleMap terrain;
    private readonly Image marker, ship, wind, windFace, ring, ringShadow;
    private readonly TextMeshProUGUI biome, clock;
    private readonly Image leftOrnament, rightOrnament;
    private readonly List<Image> mapPins = new();
    private readonly Compass compass;
    private readonly RestHud rest;
    private HiddenGraphics nativeMap;
    private Minimap owner;
    private Image windSource, shipSource;
    private float lastSize;
    private int lastMinute = -1;

    internal MapHud(Hud hud, RectTransform parent, Artwork art)
    {
        var font = hud.m_healthText.font;
        var displayFont = HudStyle.DisplayFont(font);
        root = ItemUi.Rect(parent, "MapHud");
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);
        area = ItemUi.Rect(root, "MinimapArea");
        area.anchorMin = area.anchorMax = area.pivot = Vector2.one;
        area.anchoredPosition = new Vector2(-24, -23);
        var map = mapRoot = ItemUi.Rect(area, "RoundMinimap");
        ItemUi.Stretch(map, Vector2.zero, Vector2.zero);
        ringShadow = HudStyle.Shadow(map, art, 0.8f);
        var mapBack = ItemUi.Image(map, "Backing", art["disc"]);
        mapBack.color = new Color32(14, 22, 21, 255);
        ItemUi.Stretch(mapBack.rectTransform, Vector2.zero, Vector2.zero);
        terrain = ItemUi.Rect(map, "MapTexture").gameObject.AddComponent<CircleMap>();
        terrain.raycastTarget = false;
        ItemUi.Stretch(terrain.rectTransform, Vector2.zero, Vector2.zero);
        pinRoot = ItemUi.Rect(map, "Pins");
        ItemUi.Stretch(pinRoot, Vector2.zero, Vector2.zero);
        // Opaque, and above the map, so nothing of the map shows inside the small circle.
        windFace = ItemUi.Image(map, "WindBadge", art["disc"]);
        windFace.color = new Color32(14, 22, 21, 255);
        ring = ItemUi.Image(map, "Ring", art["map-ring"]);
        ring.preserveAspect = true;
        marker = ItemUi.Image(map, "Player", art["player-marker"]);
        marker.rectTransform.sizeDelta = new Vector2(23, 23);
        ship = ItemUi.Image(map, "Ship");
        ship.preserveAspect = true;
        ship.rectTransform.sizeDelta = new Vector2(21, 21);
        wind = ItemUi.Image(map, "WindDirection", art["wind"]);
        wind.preserveAspect = true;

        caption = ItemUi.Rect(map, "Location");
        caption.anchorMin = caption.anchorMax = new Vector2(0.5f, 0);
        caption.pivot = new Vector2(0.5f, 1);
        caption.anchoredPosition = new Vector2(0, -7);
        var shade = HudStyle.Shadow(caption, art, 0.79f);
        ItemUi.Stretch(shade.rectTransform, new Vector2(-68, -39), new Vector2(68, 34));
        leftOrnament = ItemUi.Image(caption, "LeftOrnament", art["map-ornament"]);
        rightOrnament = ItemUi.Image(caption, "RightOrnament", art["map-ornament"]);
        leftOrnament.color = rightOrnament.color = ItemUi.Accent;
        rightOrnament.rectTransform.localScale = new Vector3(-1, 1, 1);
        biome = ItemUi.Text(caption, "Biome", displayFont, 20, ItemUi.Ink);
        biome.alignment = TextAlignmentOptions.Center;
        biome.overflowMode = TextOverflowModes.Ellipsis;
        biome.textWrappingMode = TextWrappingModes.NoWrap;
        clock = ItemUi.Text(caption, "WorldTime", displayFont, 17, ItemUi.Accent);
        clock.alignment = TextAlignmentOptions.Center;
        rest = new RestHud(caption, art, displayFont);
        compass = new Compass(root, art, font, displayFont);
    }

    internal void Refresh(Player player, bool visible, float highlightDistance, float markerDistance)
    {
        var map = Minimap.instance;
        if (map != owner)
        {
            nativeMap?.Restore();
            nativeMap = null;
            owner = map;
            if (map && map.m_smallRoot)
            {
                nativeMap = new HiddenGraphics(map.m_smallRoot);
                nativeMap.SetRaycasts(false);
                windSource = map.m_windMarker ? map.m_windMarker.GetComponentInChildren<Image>(true) : null;
                shipSource = map.m_smallShipMarker ? map.m_smallShipMarker.GetComponentInChildren<Image>(true) : null;
            }
        }
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return;

        bool mapVisible = map && map.m_smallRoot && map.m_smallRoot.activeInHierarchy && map.m_mapImageSmall;
        if (mapRoot.gameObject.activeSelf != mapVisible) mapRoot.gameObject.SetActive(mapVisible);

        float size = Mathf.Clamp(root.rect.width * 0.15f, 170, 250);
        if (size != lastSize)
        {
            area.sizeDelta = new Vector2(size, size);
            caption.sizeDelta = new Vector2(size, 48);
            LayoutRing(size);
            ItemUi.TopLeft(biome.rectTransform, 24, 1, size - 48, 27);
            ItemUi.TopLeft(clock.rectTransform, 0, 27, size, 20);
            AlignOrnaments(size);
            lastSize = size;
        }
        var pins = map ? Pins?.GetValue(map) as List<Minimap.PinData> : null;
        if (mapVisible)
        {
            RefreshMap(map, pins, size);
            rest.Refresh(player);
            compass.Refresh(player, pins, FindLastDeathPin(pins), VisibleTypes?.GetValue(map) as bool[],
                SharedFade?.GetValue(map) is float fade ? fade : 0, highlightDistance, markerDistance);
        }
        else
        {
            compass.Hide();
        }
    }

    private static Minimap.PinData FindLastDeathPin(List<Minimap.PinData> pins)
    {
        var profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
        if (pins == null || profile == null || !profile.HaveDeathPoint()) return null;

        Vector3 death = profile.GetDeathPoint();
        for (int i = pins.Count - 1; i >= 0; i--)
        {
            var pin = pins[i];
            if (pin.m_type != Minimap.PinType.Death || pin.m_ownerID != 0 || pin.m_shouldDelete) continue;
            if (Utils.DistanceXZ(pin.m_pos, death) < 1) return pin;
        }
        return null;
    }

    private void RefreshMap(Minimap map, List<Minimap.PinData> pins, float size)
    {
        var source = map.m_mapImageSmall;
        if (terrain.texture != source.texture) terrain.texture = source.texture;
        if (terrain.material != source.material) terrain.material = source.material;
        if (terrain.uvRect != source.uvRect) terrain.uvRect = source.uvRect;

        if (map.m_biomeNameSmall && biome.text != map.m_biomeNameSmall.text)
        {
            biome.text = map.m_biomeNameSmall.text;
            AlignOrnaments(size);
        }
        if (EnvMan.instance)
        {
            int minute = Mathf.FloorToInt(Mathf.Repeat(EnvMan.instance.GetDayFraction(), 1) * 1440);
            if (minute != lastMinute)
            {
                lastMinute = minute;
                clock.text = $"{minute / 60:00}:{minute % 60:00}";
            }
        }

        marker.enabled = map.m_smallMarker && map.m_smallMarker.gameObject.activeInHierarchy;
        if (marker.enabled) marker.rectTransform.rotation = map.m_smallMarker.rotation;
        ship.enabled = shipSource && shipSource.gameObject.activeInHierarchy;
        marker.enabled &= !ship.enabled;
        if (ship.enabled)
        {
            ship.sprite = shipSource.sprite;
            ship.rectTransform.rotation = map.m_smallShipMarker.rotation;
        }
        wind.enabled = EnvMan.instance;
        if (wind.enabled)
        {
            // The minimap keeps north up, so the icon turns to where the wind blows.
            var direction = EnvMan.instance.GetWindDir();
            float intensity = Mathf.Clamp01(EnvMan.instance.GetWindIntensity());
            wind.rectTransform.localEulerAngles = new Vector3(0, 0, -Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
            // Calm air fades the gusts; a gale makes them bright and a little larger.
            wind.color = Color.Lerp(new Color32(150, 190, 180, 150), new Color32(170, 255, 236, 255), intensity);
            float scale = Mathf.Lerp(0.85f, 1.08f, intensity);
            wind.rectTransform.localScale = new Vector3(scale, scale, 1);
        }

        int shown = 0;
        Vector2 sourceSize = source.rectTransform.rect.size;
        if (pins != null && sourceSize.x > 0 && sourceSize.y > 0)
        {
            foreach (var pin in pins)
            {
                if (!pin.m_icon || !pin.m_uiElement || pin.m_uiElement.parent != map.m_pinRootSmall) continue;
                Vector2 relative = pin.m_uiElement.anchoredPosition / sourceSize - Vector2.one * 0.5f;
                if (relative.sqrMagnitude > 0.46f * 0.46f) continue;
                if (shown == mapPins.Count) mapPins.Add(ItemUi.Image(pinRoot, "Pin"));
                var image = mapPins[shown++];
                image.sprite = pin.m_icon;
                image.color = pin.m_checked ? Color.Lerp(pin.m_iconElement.color, ItemUi.Muted, 0.45f) : pin.m_iconElement.color;
                image.preserveAspect = true;
                image.rectTransform.anchoredPosition = relative * size;
                image.rectTransform.sizeDelta = pin.m_uiElement.rect.size * (size / sourceSize.x);
                image.enabled = true;
            }
        }
        for (int i = shown; i < mapPins.Count; i++) mapPins[i].enabled = false;
    }

    private void LayoutRing(float size)
    {
        float scale = size / (MapRadius * 2);
        // Keep the whole ring on screen: it reaches past the map at the top and on the right.
        area.anchoredPosition = new Vector2(-16 - (RingWidth - RingCenter.x - MapRadius) * scale,
            -14 - (RingCenter.y - MapRadius) * scale);
        var center = new Vector2(RingWidth, RingHeight) * 0.5f;
        // Source pixels run downwards; UI coordinates run upwards from the map centre.
        Vector2 Place(Vector2 source) => new Vector2(source.x - RingCenter.x, RingCenter.y - source.y) * scale;
        HudStyle.Center(ring.rectTransform, 0, 0, RingWidth * scale, RingHeight * scale);
        ring.rectTransform.anchoredPosition = Place(center);
        HudStyle.Center(ringShadow.rectTransform, 0, 0, RingWidth * scale * 1.35f, RingHeight * scale * 1.3f);
        ringShadow.rectTransform.anchoredPosition = Place(center) + new Vector2(0, -4);
        var windAt = Place(WindCenter);
        float face = (WindRadius + 8) * scale * 2;
        HudStyle.Center(windFace.rectTransform, windAt.x, windAt.y, face, face);
        HudStyle.Center(wind.rectTransform, windAt.x, windAt.y, WindRadius * scale * 1.45f, WindRadius * scale * 1.45f);
        // The location caption sits under the ring's lowest point.
        caption.anchoredPosition = new Vector2(0, -((RingHeight - RingCenter.y) * scale - size * 0.5f) + 2);
    }

    private void AlignOrnaments(float size)
    {
        float textWidth = Mathf.Min(size - 48, biome.GetPreferredValues(biome.text).x);
        float width = (size - textWidth) * 0.5f - 8;
        HudStyle.Center(leftOrnament.rectTransform, (width - size) * 0.5f, 9, width, 10);
        HudStyle.Center(rightOrnament.rectTransform, (size - width) * 0.5f, 9, width, 10);
    }

    public void Dispose()
    {
        nativeMap?.Restore();
        ItemUi.Destroy(root);
    }
}

// The game's terrain material can render on a circular mesh without relying on stencil support.
internal sealed class CircleMap : RawImage
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        const int segments = 72;
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        var uv = uvRect;
        mesh.AddVert(rect.center, color, uv.center);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            var unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
            mesh.AddVert(rect.center + unit * rect.size, color, uv.center + unit * uv.size);
            if (i > 0) mesh.AddTriangle(0, i + 1, i);
        }
    }
}
