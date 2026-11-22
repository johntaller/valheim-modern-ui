using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class WorldInteractionHud : IDisposable
{
    private const float FadeDistance = 5;
    private const float ScanInterval = 0.15f;
    private static readonly System.Reflection.FieldInfo InteractMask = AccessTools.Field(typeof(Player), "m_interactMask");
    private static readonly int ItemMask = LayerMask.GetMask("item");
    private readonly Hud hud;
    private readonly Canvas canvas;
    private readonly Artwork art;
    private readonly RectTransform root;
    private readonly ItemTooltip tooltip;
    private readonly List<Image> markers = new();
    private readonly RaycastHit[] blockers = new RaycastHit[24];
    private readonly Dictionary<MonoBehaviour, (Collider Collider, float Reach, float Distance)> targets = new();
    private Collider[] nearby = new Collider[256];
    private HiddenGraphics vanillaHover;
    private GameCamera cameraOwner;
    private Camera camera;
    private float nextScan;
    private int interactMask;

    internal WorldInteractionHud(Hud hud, Canvas canvas, RectTransform parent, Artwork art)
    {
        this.hud = hud;
        this.canvas = canvas;
        this.art = art;
        root = ItemUi.Rect(parent, "WorldInteractions");
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
            targets.Clear();
            nextScan = 0;
            return;
        }

        Vector3 eye = player.GetEyePoint();
        var hovered = GetInteraction(player.GetHoverObject());
        var drop = hovered as ItemDrop;
        bool showTooltip = IsLooseItem(drop);
        SetHovered(showTooltip ? drop : null);
        bool showPrompt = hud.m_hoverName && !string.IsNullOrWhiteSpace(hud.m_hoverName.text);

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + ScanInterval;
            ScanNearby(player, eye);
        }

        int shown = 0;
        Rect viewport = camera.rect;
        Rect area = root.rect;
        foreach (var (interaction, target) in targets)
        {
            var collider = target.Collider;
            if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy || !CanShow(interaction)) continue;
            if (interaction == hovered && (showTooltip || showPrompt)) continue;
            var bounds = collider.bounds;
            var world = bounds.center;
            float distance = Vector3.Distance(eye, bounds.ClosestPoint(eye));
            float alpha = 1 - Mathf.InverseLerp(target.Reach, target.Reach + FadeDistance, distance);
            if (alpha <= 0) continue;
            var point = camera.WorldToViewportPoint(world);
            if (point.z <= 0 || point.x <= 0 || point.x >= 1 || point.y <= 0 || point.y >= 1) continue;
            if (Occluded(player, interaction, collider, world)) continue;
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

    private void ScanNearby(Player player, Vector3 eye)
    {
        targets.Clear();
        interactMask = InteractMask?.GetValue(player) is int value ? value : Physics.DefaultRaycastLayers;
        int scanMask = interactMask | ItemMask;
        // Leave room for individual hover offsets. Each ring fades using its own reach.
        float radius = player.m_maxInteractDistance + 2 * FadeDistance;
        int count = Physics.OverlapSphereNonAlloc(eye, radius, nearby, scanMask, QueryTriggerInteraction.Collide);
        while (count == nearby.Length)
        {
            Array.Resize(ref nearby, nearby.Length * 2);
            count = Physics.OverlapSphereNonAlloc(eye, radius, nearby, scanMask, QueryTriggerInteraction.Collide);
        }

        for (int i = 0; i < count; i++)
        {
            var collider = nearby[i];
            var body = collider.attachedRigidbody;
            if (body && body.gameObject == player.gameObject) continue;
            // Match the collider/body choice made by Player.FindHoverObject.
            var hoverTarget = body && collider.GetComponent<Hoverable>() == null ? body.gameObject : collider.gameObject;
            var interaction = GetInteraction(hoverTarget);
            if (!CanShow(interaction) || interaction.transform.IsChildOf(player.transform)) continue;

            float reach = player.m_maxInteractDistance + (collider.GetComponentInParent<Hoverable>()?.GetHoverOffset() ?? 0);
            float distance = Vector3.Distance(eye, collider.bounds.ClosestPoint(eye));
            if (distance >= reach + FadeDistance) continue;
            if (targets.TryGetValue(interaction, out var other) && other.Distance <= distance) continue;
            targets[interaction] = (collider, reach, distance);
        }
        Array.Clear(nearby, 0, count);
    }

    private static MonoBehaviour GetInteraction(GameObject target) =>
        target ? target.GetComponentInParent<Interactable>() as MonoBehaviour : null;

    private static bool IsLooseItem(ItemDrop drop) => drop && drop.m_itemData?.m_shared != null && !drop.IsPiece();

    private static bool CanShow(MonoBehaviour interaction)
    {
        if (!interaction || !interaction.isActiveAndEnabled) return false;
        // Some world objects remain active after their interaction is no longer available.
        return interaction switch
        {
            ItemDrop drop => IsLooseItem(drop) && !drop.InTar(),
            Pickable plant => plant.CanBePicked(),
            PickableItem item => !string.IsNullOrEmpty(item.GetHoverText()),
            Fish fish => fish.IsOutOfWater(),
            Tameable animal => animal.IsTamed(),
            Raven raven => !string.IsNullOrEmpty(raven.GetHoverText()),
            _ => true,
        };
    }

    private bool Occluded(Player player, MonoBehaviour interaction, Collider target, Vector3 center)
    {
        var origin = camera.transform.position;
        var direction = center - origin;
        float length = direction.magnitude;
        if (length < 0.1f) return false;
        direction /= length;
        // Stop at the visible surface and allow colliders that belong to this interaction.
        if (target.Raycast(new Ray(origin, direction), out var hit, length))
            length = Mathf.Max(0, hit.distance - 0.04f);
        var body = target.attachedRigidbody;
        int count = Physics.RaycastNonAlloc(origin, direction, blockers, length, interactMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = blockers[i].collider;
            if (!collider || collider == target || collider.transform.IsChildOf(player.transform)
                || collider.transform.IsChildOf(interaction.transform)) continue;
            if (!body || collider.attachedRigidbody != body) return true;
        }
        return false;
    }

    private Image GetMarker(int index)
    {
        if (index < markers.Count) return markers[index];
        var marker = ItemUi.Image(root, "InteractionRing", art["pickup-ring"]);
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
