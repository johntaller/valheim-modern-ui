using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Skin live scene instances. Valheim still owns input, item icons, map textures,
// marker positions, selection, progress and all save-related operations.
internal sealed class InterfaceSkin : IDisposable
{
    private readonly Hud hud;
    private readonly Artwork art;
    private readonly HashSet<Image> visited = new();
    private readonly List<Original> originals = new();
    private readonly List<Image> scan = new();
    private readonly List<GameObject> layers = new();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly ItemInterface items;
    private readonly RectTransform worldRoot;
    private readonly WorldInteractionHud worldInteractions;
    private readonly MapHud mapHud;
    private readonly NotificationHud notifications;
    private readonly CombatReticle combat;
    private readonly OnlinePlayers onlinePlayers;
    private readonly ActionProgressHud actionProgress;
    private readonly KeyHintsColumn keyHints = new();
    private VitalsView vitals;
    private RectTransform pausePlate;
    private bool pauseWasOpen;
    private float nextPrune;

    internal InterfaceSkin(Hud hud, Artwork art, PlayerCardSync cards)
    {
        this.hud = hud;
        this.art = art;

        var canvas = hud.m_healthText ? hud.m_healthText.GetComponentInParent<Canvas>(true) : null;
        if (!canvas) return;
        worldRoot = ItemUi.Rect(canvas.transform, "NewUI_WorldHud");
        ItemUi.Stretch(worldRoot, Vector2.zero, Vector2.zero);
        onlinePlayers = new OnlinePlayers(worldRoot, art, hud.m_healthText.font, cards);
        var hotbar = hud.m_rootObject.GetComponentInChildren<HotkeyBar>(true);
        if (hotbar && hotbar.m_elementPrefab && hud.m_healthText)
            items = new ItemInterface(hotbar, art, hud.m_healthText.font, onlinePlayers);

        worldInteractions = new WorldInteractionHud(hud, canvas, worldRoot, art);
        mapHud = new MapHud(hud, worldRoot, art);
        combat = new CombatReticle(hud, worldRoot, art);
        notifications = new NotificationHud(worldRoot, art, hud.m_healthText.font);
        actionProgress = new ActionProgressHud(hud, worldRoot, art);
    }

    internal void Apply(Theme theme) => vitals?.Apply(theme);

    internal void PrepareForRender(Theme theme)
    {
        // The game can create more inventory cells and recipes in the frames after Show().
        // Visit active objects before uGUI rebuilds their geometry, including on those frames.
        Scan(hud.m_rootObject.transform);
        if (EnemyHud.instance && EnemyHud.instance.m_hudRoot) Scan(EnemyHud.instance.m_hudRoot.transform);
        var inventory = InventoryGui.instance;
        if (inventory) Scan(inventory.m_inventoryRoot);
        var map = Minimap.instance;
        if (map)
        {
            if (map.m_largeRoot) Scan(map.m_largeRoot.transform);
        }
        var pause = Menu.instance;
        bool paused = pause && pause.m_root && pause.m_root.gameObject.activeInHierarchy;
        if (paused)
        {
            Scan(pause.m_root);
            StylePause(pause, !pauseWasOpen);
        }
        pauseWasOpen = paused;

        var player = Player.m_localPlayer;

        bool inventoryOpen = InventoryGui.IsVisible();
        bool mapOpen = Minimap.IsOpen();
        bool visible = player && !player.IsDead() && hud.isActiveAndEnabled && hud.m_rootObject.activeInHierarchy && hud.IsVisible();
        bool covered = paused || mapOpen || Console.IsVisible() || (TextViewer.instance && TextViewer.instance.IsVisible());
        // The build menu, a teleport and a seat at the helm or on a bench all take over the screen centre.
        bool building = Hud.IsPieceSelectionVisible();
        bool busy = player && (player.IsTeleporting() || player.IsAttached() || player.IsAttachedToShip());
        worldInteractions?.Refresh(player, visible && !covered && !inventoryOpen && !building && !busy);
        mapHud?.Refresh(player, visible && !mapOpen && !inventoryOpen, theme.CompassHighlightDistance,
            theme.CompassMarkerDistance);
        LifeNotices.Observe(player);
        onlinePlayers?.Refresh(player, visible && !covered && !inventoryOpen, theme.PlayerDetectionDistance);
        items?.Refresh(inventory, player);
        combat?.Refresh(player, visible && !covered && !inventoryOpen && !building && !(player && player.IsTeleporting()));
        notifications?.Refresh(covered);
        actionProgress?.Refresh(hud, player, visible && !covered);
        keyHints.Refresh(true);
        if (vitals == null && worldRoot && hud.m_crosshair && hud.m_healthBarRoot && hud.m_staminaBar2Root)
        {
            var canvas = worldRoot.GetComponentInParent<Canvas>(true);
            vitals = new VitalsView(hud, canvas, art, theme);
        }
        vitals?.Refresh(player, visible && !mapOpen, inventoryOpen, covered);
        if (Time.unscaledTime >= nextPrune)
        {
            nextPrune = Time.unscaledTime + 10;
            visited.RemoveWhere(i => !i);
            originals.RemoveAll(i => !i.Image);
            layers.RemoveAll(g => !g);
        }
    }

    private void Scan(Transform root)
    {
        if (!root || !root.gameObject.activeInHierarchy) return;
        root.GetComponentsInChildren(false, scan);
        foreach (var image in scan)
        {
            if (!image || !visited.Add(image)) continue;
            string name = image.name;
            string source = image.sprite ? image.sprite.name : "";
            bool crosshair = image == hud.m_crosshair;
            string role = crosshair ? "reticle" : Role(source, name, image);
            if (role == null) continue;
            bool keepTint = role == "meter-fill" || name == "dropFocus" || name == "queued" || name == "equiped";
            var control = image.GetComponentInParent<Selectable>();
            if (control && (control.targetGraphic != image || control.transition != Selectable.Transition.SpriteSwap))
                control = null;
            originals.Add(new Original(image, !keepTint, control, crosshair));
            image.sprite = art[role];
            image.type = role is "meter-fill" or "reticle" or "player-marker" ? Image.Type.Simple : Image.Type.Sliced;
            if (crosshair) image.rectTransform.localScale *= 0.84f;
            if (control)
            {
                var state = control.spriteState;
                state.highlightedSprite = art["button"];
                state.pressedSprite = art["selected"];
                state.selectedSprite = art["button"];
                state.disabledSprite = art["slot"];
                control.spriteState = state;
            }
            if (!keepTint)
            {
                // Keep faded resources, animated hints and keyboard focus visible in their native states.
                float alpha = image.color.a;
                if (role is "panel" or "map-frame" || role == "slot" && name.StartsWith("food", StringComparison.Ordinal)) alpha = 1;
                image.color = new Color(1, 1, 1, alpha);
            }
        }
    }

    private static string Role(string sprite, string name, Image image)
    {
        if (sprite.StartsWith("woodpanel", StringComparison.Ordinal))
            return sprite.EndsWith("_mask", StringComparison.Ordinal) ? "selected" : "panel";
        if (sprite.StartsWith("bar_", StringComparison.Ordinal) && sprite != "bar_food_overlay") return "meter-fill";
        switch (sprite)
        {
            case "button": case "button_tab": case "button_small": case "key_base": return "button";
            case "item_background":
                return name is "equiped" or "queued" or "dropFocus" ? "selected" : "slot";
            case "selection_frame": return "selected";
            case "crafting_panel_bkg": case "panel_bkg": case "sunken": case "checkbox": case "text_field": return "slot";
            case "map_marker": return "player-marker";
            case "crosshair": return "reticle";
            case "InputFieldBackground": return name is "large" or "small" ? "map-frame" : "slot";
            case "Background":
                if (name is "food0" or "food1" or "food2" or "Bkg" || name.StartsWith("res_bkg", StringComparison.Ordinal)) return "slot";
                if (name == "Selected") return "selected";
                break;
            case "":
                if (image.transform.parent && image.transform.parent.name == "RecipeElement(Clone)")
                    return name switch { "bkg" => "slot", "selected" => "selected", _ => null };
                break;
        }
        return null;
    }

    private void StylePause(Menu menu, bool opening)
    {
        var entries = menu.menuEntriesParent;
        if (!entries || !entries.gameObject.activeInHierarchy) return;
        if (!pausePlate)
        {
            pausePlate = Backdrop(entries, "NewUI_MenuPanel", "panel", 0);
            pausePlate.anchorMin = pausePlate.anchorMax = entries.pivot;
        }

        foreach (RectTransform entry in entries)
        {
            if (entry.gameObject.activeSelf && !entry.Find("NewUI_MenuRow") && entry.GetComponent<Button>())
            {
                var row = Backdrop(entry, "NewUI_MenuRow", "button", 0);
                row.anchorMin = new Vector2(0.5f, 0);
                row.anchorMax = new Vector2(0.5f, 1);
                row.sizeDelta = new Vector2(300, -6);
                row.anchoredPosition = Vector2.zero;
                // Pointer events on this wider face bubble to the existing menu button.
                row.GetComponent<Image>().raycastTarget = true;
            }
        }
        // The first opening can precede the first layout pass of these entries.
        if (opening) LayoutRebuilder.ForceRebuildLayoutImmediate(entries);

        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        foreach (RectTransform entry in entries)
        {
            if (entry == pausePlate || !entry.gameObject.activeSelf) continue;
            entry.GetWorldCorners(corners);
            var min = entries.InverseTransformPoint(corners[0]);
            var max = entries.InverseTransformPoint(corners[2]);
            low = Mathf.Min(low, min.y);
            high = Mathf.Max(high, max.y);
            left = Mathf.Min(left, min.x);
            right = Mathf.Max(right, max.x);
        }
        if (low > high) return;
        pausePlate.anchoredPosition = new Vector2((left + right) / 2, (low + high) / 2);
        pausePlate.sizeDelta = new Vector2(Mathf.Max(300, right - left) + 32, high - low + 28);
    }

    private RectTransform Backdrop(Transform parent, string name, string role, float inset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        layers.Add(go);
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.SetAsFirstSibling();
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var image = go.AddComponent<Image>();
        visited.Add(image);
        image.raycastTarget = false;
        image.sprite = art[role];
        image.type = Image.Type.Sliced;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = -rect.offsetMin;
        return rect;
    }

    private readonly struct Original
    {
        internal readonly Image Image;
        private readonly Sprite sprite;
        private readonly Image.Type type;
        private readonly Color color;
        private readonly bool tinted;
        private readonly Selectable control;
        private readonly SpriteState states;
        private readonly Vector3? originalScale;
        internal Original(Image image, bool tinted, Selectable control, bool scaleChanged)
        {
            Image = image;
            sprite = image.sprite;
            type = image.type;
            color = image.color;
            this.tinted = tinted;
            this.control = control;
            states = control ? control.spriteState : default;
            originalScale = scaleChanged ? image.rectTransform.localScale : null;
        }
        internal void Restore()
        {
            if (!Image) return;
            Image.sprite = sprite;
            Image.type = type;
            if (tinted) Image.color = color;
            if (control) control.spriteState = states;
            if (originalScale.HasValue) Image.rectTransform.localScale = originalScale.Value;
        }
    }

    public void Dispose()
    {
        vitals?.Dispose();
        notifications?.Dispose();
        actionProgress?.Dispose();
        keyHints.Dispose();
        combat?.Dispose();
        onlinePlayers?.Dispose();
        worldInteractions?.Dispose();
        mapHud?.Dispose();
        ItemUi.Destroy(worldRoot);
        items?.Dispose();
        foreach (var original in originals) original.Restore();
        foreach (var layer in layers)
        {
            if (!layer) continue;
            layer.SetActive(false);
            UnityEngine.Object.Destroy(layer);
        }
        layers.Clear();
        originals.Clear();
        visited.Clear();
    }
}
