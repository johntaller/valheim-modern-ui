using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class WorldPickupHud : IDisposable
{
    private const float FadeDistance = 5;
    private static readonly System.Reflection.FieldInfo InteractMask = AccessTools.Field(typeof(Player), "m_interactMask");
    private static readonly int ItemMask = LayerMask.GetMask("item");
    private readonly Hud hud;
    private readonly Canvas canvas;
    private readonly Artwork art;
    private readonly RectTransform root;
    private readonly ItemTooltip tooltip;
    private readonly List<Image> markers = new();
    private readonly Collider[] nearby = new Collider[128];
    private readonly RaycastHit[] blockers = new RaycastHit[24];
    private readonly Dictionary<ItemDrop, Collider> drops = new();
    private HiddenGraphics vanillaHover;
    private GameCamera cameraOwner;
    private Camera camera;
    private float nextScan;

    internal WorldPickupHud(Hud hud, Canvas canvas, RectTransform parent, Artwork art)
    {
        this.hud = hud;
        this.canvas = canvas;
        this.art = art;
        root = ItemUi.Rect(parent, "Pickups");
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);
        tooltip = new ItemTooltip(canvas, art, hud.m_healthText.font);
    }

    internal void Refresh(Player player, bool visible)
    {
        if (cameraOwner != GameCamera.instance)
        {
            cameraOwner = GameCamera.instance;
            camera = cameraOwner ? cameraOwner.GetComponent<Camera>() : null;
        }
        visible &= camera && hud.m_crosshair && hud.m_crosshair.gameObject.activeInHierarchy;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible)
        {
            SetHovered(null);
            drops.Clear();
            nextScan = 0;
            return;
        }

        Vector3 eye = player.GetEyePoint();
        var hovered = player.GetHoverObject();
        var selected = hovered ? hovered.GetComponentInParent<ItemDrop>() : null;
        if (selected && (selected.IsPiece() || selected.m_itemData?.m_shared == null)) selected = null;
        SetHovered(selected);

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 0.15f;
            drops.Clear();
            int count = Physics.OverlapSphereNonAlloc(eye, player.m_maxInteractDistance + FadeDistance,
                nearby, ItemMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var collider = nearby[i];
                var item = collider.GetComponentInParent<ItemDrop>();
                if (item && !item.IsPiece() && !drops.ContainsKey(item)) drops.Add(item, collider);
            }
            Array.Clear(nearby, 0, count);
        }

        int shown = 0;
        int mask = InteractMask?.GetValue(player) is int value ? value : Physics.DefaultRaycastLayers;
        Rect viewport = camera.rect;
        Rect area = root.rect;
        foreach (var (drop, collider) in drops)
        {
            if (!drop || drop == selected || !collider || !collider.enabled || !drop.isActiveAndEnabled
                || drop.InTar()) continue;
            var bounds = collider.bounds;
            var world = bounds.center;
            float distance = Vector3.Distance(eye, bounds.ClosestPoint(eye));
            float reach = player.m_maxInteractDistance + drop.GetHoverOffset();
            float alpha = 1 - Mathf.InverseLerp(reach, reach + FadeDistance, distance);
            if (alpha <= 0) continue;
            var point = camera.WorldToViewportPoint(world);
            if (point.z <= 0 || point.x <= 0 || point.x >= 1 || point.y <= 0 || point.y >= 1) continue;
            if (Occluded(player, collider, world, mask)) continue;
            var marker = GetMarker(shown++);
            // The world camera can render at a different pixel resolution from the HUD.
            marker.rectTransform.localPosition = new Vector2(
                Mathf.Lerp(area.xMin, area.xMax, viewport.x + point.x * viewport.width),
                Mathf.Lerp(area.yMin, area.yMax, viewport.y + point.y * viewport.height));
            marker.color = new Color(1, 1, 1, alpha);
            marker.enabled = true;
        }
        for (int i = shown; i < markers.Count; i++) markers[i].enabled = false;
    }

    private bool Occluded(Player player, Collider target, Vector3 center, int mask)
    {
        var origin = camera.transform.position;
        var direction = center - origin;
        float length = direction.magnitude;
        if (length < 0.1f) return false;
        direction /= length;
        // Stop just in front of the item's own collider, so other parts of the same drop can't obscure it.
        if (target.Raycast(new Ray(origin, direction), out var hit, length))
            length = Mathf.Max(0, hit.distance - 0.04f);
        int count = Physics.RaycastNonAlloc(origin, direction, blockers, length, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = blockers[i].collider;
            if (collider && !collider.transform.IsChildOf(player.transform)
                && !collider.transform.IsChildOf(target.transform)) return true;
        }
        return false;
    }

    private Image GetMarker(int index)
    {
        if (index < markers.Count) return markers[index];
        var marker = ItemUi.Image(root, "Pickup", art["pickup-ring"]);
        marker.rectTransform.sizeDelta = new Vector2(16, 16);
        markers.Add(marker);
        return marker;
    }

    private void SetHovered(ItemDrop drop)
    {
        if (drop)
        {
            Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var reticle = hud.m_crosshair.rectTransform;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, reticle.TransformPoint(reticle.rect.center));
            string hint = Localization.instance.Localize("<color=#F0D59B>[$KEY_Use]</color>  $inventory_pickup");
            tooltip.RefreshWorld(drop.m_itemData, screen, hint);
            if (vanillaHover == null && hud.m_hoverName) vanillaHover = new HiddenGraphics(hud.m_hoverName.gameObject);
            return;
        }
        tooltip.RefreshWorld(null, Vector2.zero, "");
        vanillaHover?.Restore();
        vanillaHover = null;
    }

    public void Dispose()
    {
        vanillaHover?.Restore();
        tooltip.Dispose();
        ItemUi.Destroy(root);
    }
}
