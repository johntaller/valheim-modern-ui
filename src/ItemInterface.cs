using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace ValheimNewUI;

internal sealed class ItemInterface : IDisposable
{
    private const int QuickSlots = 8;
    private readonly HotkeyBar hotbar;
    private readonly RectTransform hotbarRoot;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly OnlinePlayers players;
    private readonly RecipePins pins = new();
    private readonly RecipePinsView hudPins;
    private readonly QuickSlot[] quickSlots = new QuickSlot[QuickSlots];
    private readonly QuickSlot[] sideSlots = new QuickSlot[EquipmentInventory.QuickSlotCount];
    private float nextBindingLabels;
    // The quick slots sit in a row under the hotbar in play and move to its right end with the
    // inventory open, where the backpack takes the room under the hotbar.
    private readonly Vector2[] sideClosed = new Vector2[EquipmentInventory.QuickSlotCount];
    private readonly Vector2[] sideOpen = new Vector2[EquipmentInventory.QuickSlotCount];
    private float sideBlend = -1;
    private readonly Dictionary<Transform, NativeQuickSlot> nativeQuickSlots = new();
    private InventoryPanel panel;
    private CraftingMenu crafting;

    internal ItemInterface(HotkeyBar hotbar, Artwork art, TMP_FontAsset font, OnlinePlayers players)
    {
        this.hotbar = hotbar;
        hotbarRoot = (RectTransform)hotbar.transform;
        this.art = art;
        this.font = font;
        this.players = players;
        var template = (RectTransform)hotbar.m_elementPrefab.transform;
        float pitch = hotbar.m_elementSpace;
        Vector2 size = template.rect.size;
        for (int i = 0; i < quickSlots.Length; i++)
            quickSlots[i] = new QuickSlot(hotbarRoot, template, "NewUI_QuickSlot" + (i + 1), size,
                new Vector2(i * pitch, 0), 1, art, font, (i + 1).ToString());

        // Quick slots sit after a wider gap and are a third smaller than hotbar cells.
        const float ratio = 1 / 1.5f;
        Vector2 small = size * ratio;
        float spacing = Mathf.Max(2, (pitch - size.x) * ratio);
        float gap = Mathf.Max(14, (pitch - size.x) * 3);
        var pivot = template.pivot;
        float right = (QuickSlots - 1) * pitch + (1 - pivot.x) * size.x;
        float y = (0.5f - pivot.y) * (size.y - small.y);
        // Under the hotbar: from its left edge, a little below the bottom of its cells.
        float under = -pivot.y * size.y - 6 - (1 - pivot.y) * small.y;
        for (int i = 0; i < sideSlots.Length; i++)
        {
            float left = right + gap + i * (small.x + spacing);
            sideOpen[i] = new Vector2(left + pivot.x * small.x, y);
            sideClosed[i] = new Vector2(-pivot.x * size.x + i * (small.x + spacing) + pivot.x * small.x, under);
            sideSlots[i] = new QuickSlot(hotbarRoot, template, "NewUI_SideSlot" + (i + 1), small,
                sideClosed[i], ratio, art, font, "");
        }
        hudPins = new RecipePinsView(hotbarRoot, "NewUI_HudRecipePins", pins, art, font);
    }

    internal void Refresh(InventoryGui gui, Player player, bool showHudPins)
    {
        pins.Refresh(player);
        if (panel != null && (!gui || panel.Gui != gui)) ReleasePanel();
        if (crafting != null && (!gui || crafting.Gui != gui)) ReleaseCrafting();
        if (crafting == null && gui && gui.m_crafting && gui.m_info && gui.m_recipeListRoot)
            crafting = new CraftingMenu(gui, art, font, players, pins);
        crafting?.Refresh(player);
        if (panel == null && gui && gui.m_player && gui.m_playerGrid && gui.m_inventoryRoot && gui.m_weight)
        {
            var canvas = gui.m_inventoryRoot.GetComponentInParent<Canvas>(true);
            var animator = gui.GetComponent<Animator>();
            if (canvas && animator) panel = new InventoryPanel(gui, animator, canvas.rootCanvas, art, font, pins);
        }

        bool alive = player && !player.IsDead();
        bool open = alive && panel != null && panel.IsOpen;
        var inventory = alive ? player.GetInventory() : null;
        panel?.Refresh(open, player, quickSlots[0].Rect, quickSlots[QuickSlots - 1].Rect, hotbar.m_elementSpace);
        foreach (var slot in quickSlots) slot.SetVisible(alive);
        bool sides = alive && EquipmentInventory.QuickRow(inventory) >= 0;
        foreach (var slot in sideSlots) slot.SetVisible(sides);
        float blend = sideBlend < 0 ? (open ? 1 : 0) : Mathf.MoveTowards(sideBlend, open ? 1 : 0, Time.unscaledDeltaTime * 5);
        if (blend != sideBlend)
        {
            sideBlend = blend;
            float eased = blend * blend * (3 - 2 * blend);
            for (int i = 0; i < sideSlots.Length; i++)
                sideSlots[i].Rect.anchoredPosition = Vector2.Lerp(sideClosed[i], sideOpen[i], eased);
        }
        hudPins.Refresh(showHudPins && alive && !open, quickSlots[0].Rect, quickSlots[QuickSlots - 1].Rect,
            sides ? sideSlots[0].Rect : quickSlots[0].Rect);

        foreach (Transform child in hotbarRoot)
        {
            if (!child.TryGetComponent<Button>(out _)) continue;
            if (!nativeQuickSlots.TryGetValue(child, out var native))
            {
                native = new NativeQuickSlot(child);
                nativeQuickSlots.Add(child, native);
            }
            native.Hidden.SetRaycasts(!open);
        }

        bool bindings = alive && !ZInput.IsGamepadActive();
        for (int i = 0; i < quickSlots.Length; i++)
        {
            var slot = quickSlots[i];
            var input = open ? panel.GetQuickTarget(i) : null;
            slot.Refresh(inventory?.GetItemAt(i, 0), player, bindings, input);
        }
        if (sides)
        {
            if (Time.unscaledTime >= nextBindingLabels)
            {
                // Keys can change in the settings while the HUD stays open.
                nextBindingLabels = Time.unscaledTime + 0.5f;
                for (int i = 0; i < sideSlots.Length; i++) sideSlots[i].SetBinding(QuickSlotKeys.Label(i));
            }
            for (int i = 0; i < sideSlots.Length; i++)
                sideSlots[i].Refresh(EquipmentInventory.QuickItem(inventory, i), player, bindings,
                    open ? panel.GetSideTarget(i) : null);
        }

        // Native buttons still handle the HUD gamepad/touch controls. Mirror
        // their selection into our persistent visuals while inventory is closed.
        if (!open)
        {
            foreach (var entry in nativeQuickSlots)
            {
                if (!entry.Key || !entry.Value.Selected || !entry.Value.Selected.gameObject.activeInHierarchy) continue;
                int index = Mathf.RoundToInt(entry.Key.localPosition.x / hotbar.m_elementSpace);
                if (index >= 0 && index < QuickSlots) quickSlots[index].SetFocused(true);
            }
        }

        if (open)
        {
            panel.PositionQuickTargets(quickSlots);
            if (sides) panel.PositionSideTargets(sideSlots);
        }
        if (nativeQuickSlots.Count > QuickSlots * 2)
        {
            var dead = new List<Transform>();
            foreach (var entry in nativeQuickSlots)
                if (!entry.Key) dead.Add(entry.Key);
            foreach (var key in dead) nativeQuickSlots.Remove(key);
        }
    }

    private void ReleasePanel()
    {
        var previousPanel = panel;
        panel = null;
        previousPanel?.Dispose();
    }

    public void Dispose()
    {
        hudPins.Dispose();
        ReleaseCrafting();
        ReleasePanel();
        foreach (var entry in nativeQuickSlots.Values) entry.Hidden.Restore();
        foreach (var slot in quickSlots) ItemUi.Destroy(slot.Rect);
        foreach (var slot in sideSlots) ItemUi.Destroy(slot.Rect);
    }

    private void ReleaseCrafting()
    {
        var previous = crafting;
        crafting = null;
        previous?.Dispose();
    }

    private sealed class NativeQuickSlot
    {
        internal readonly HiddenGraphics Hidden;
        internal readonly Transform Selected;

        internal NativeQuickSlot(Transform cell)
        {
            Hidden = new HiddenGraphics(cell.gameObject);
            Selected = cell.Find("selected");
        }
    }

    private sealed class QuickSlot
    {
        internal readonly RectTransform Rect;
        private static readonly Color32 EquippedTint = new(168, 255, 222, 255);
        private readonly Image icon, selection, equipped, equippedGlow;
        private readonly DurabilityEdge edge;
        private readonly TextMeshProUGUI binding, amount, cheated;
        private readonly ItemQualityBadge quality;
        private readonly bool compact;
        private ItemDrop.ItemData lastItem;
        private int lastStack = -1;

        internal QuickSlot(RectTransform parent, RectTransform template, string name, Vector2 size, Vector2 position,
            float scale, Artwork art, TMP_FontAsset font, string key)
        {
            compact = scale < 1;
            Rect = ItemUi.Rect(parent, name);
            Rect.anchorMin = Rect.anchorMax = parent.pivot;
            Rect.pivot = template.pivot;
            Rect.sizeDelta = size;
            Rect.anchoredPosition = position;

            var face = ItemUi.Image(Rect, "Face", art["slot"]);
            ItemUi.Stretch(face.rectTransform, Vector2.zero, Vector2.zero);
            equipped = ItemUi.Image(Rect, "Equipped", art["selected"]);
            equipped.color = EquippedTint;
            // A second frame just inside the first doubles the line, so the equipped slot reads at a glance
            // without tinting the quality backlight.
            equippedGlow = ItemUi.Image(Rect, "EquippedInner", art["selected"]);
            equippedGlow.color = new Color32(150, 255, 214, 170);
            ItemUi.Stretch(equippedGlow.rectTransform, Vector2.one * 2, Vector2.one * -2);
            ItemUi.Stretch(equipped.rectTransform, Vector2.zero, Vector2.zero);

            selection = ItemUi.Image(Rect, "Focus", art["selected"]);
            selection.color = ItemUi.Accent;
            ItemUi.Stretch(selection.rectTransform, Vector2.zero, Vector2.zero);
            icon = ItemUi.Image(Rect, "ItemIcon");
            icon.preserveAspect = true;
            ItemUi.Stretch(icon.rectTransform, new Vector2(8, 9) * scale, new Vector2(-8, -6) * scale);

            binding = ItemUi.Text(Rect, "Binding", font, scale < 1 ? 12 : 15, ItemUi.Ink);
            binding.fontStyle = FontStyles.Bold;
            binding.alignment = TextAlignmentOptions.TopLeft;
            // Rebound keys such as "Left Shift" shrink to the slot width.
            binding.textWrappingMode = TextWrappingModes.NoWrap;
            binding.overflowMode = TextOverflowModes.Ellipsis;
            binding.enableAutoSizing = true;
            binding.fontSizeMin = 8;
            binding.fontSizeMax = binding.fontSize;
            ItemUi.Stretch(binding.rectTransform, new Vector2(7 * scale, 0), new Vector2(-3, -4 * scale));
            binding.text = key;

            amount = ItemUi.Text(Rect, "Stack", font, scale < 1 ? 11 : 14, ItemUi.Ink);
            amount.fontStyle = FontStyles.Bold;
            amount.alignment = TextAlignmentOptions.Bottom;
            amount.textWrappingMode = TextWrappingModes.NoWrap;
            ItemUi.Stretch(amount.rectTransform, new Vector2(2, 5 * scale), new Vector2(-2, 0));
            cheated = MakeCheatBadge(Rect, font, hotbar: true);
            if (compact) cheated.fontSize = 15;
            quality = new ItemQualityBadge(Rect, art, font, null, icon);
            edge = MakeDurability(Rect);
        }

        internal void SetVisible(bool value)
        {
            if (Rect.gameObject.activeSelf != value) Rect.gameObject.SetActive(value);
        }

        internal void SetBinding(string key)
        {
            if (binding.text != key) binding.text = key;
        }

        internal void Refresh(ItemDrop.ItemData item, Player player, bool bindings, InventoryElement target)
        {
            binding.enabled = bindings;
            icon.enabled = item != null;
            if (item != lastItem)
            {
                icon.sprite = item?.GetIcon();
                lastItem = item;
                lastStack = -1;
            }
            icon.color = target && target.m_used ? target.m_icon.color : Color.white;
            equipped.enabled = equippedGlow.enabled = item != null && (item.m_equipped || player.IsEquipActionQueued(item));
            SetFocused(target && target.m_selected && target.m_selected.activeSelf);
            cheated.enabled = item?.m_cheated == true;
            quality.Refresh(item);
            amount.enabled = item != null && item.m_shared.m_maxStackSize > 1;
            if (amount.enabled && lastStack != item.m_stack)
            {
                lastStack = item.m_stack;
                amount.text = $"{lastStack}/{item.m_shared.m_maxStackSize}";
            }
            edge.SetItem(item);
        }

        internal void SetFocused(bool value) => selection.enabled = value;
    }

    private static TextMeshProUGUI MakeCheatBadge(RectTransform slot, TMP_FontAsset font, bool hotbar)
    {
        var badge = ItemUi.Text(slot, "CheatedItem", font, 20, new Color32(255, 104, 101, 255));
        badge.fontStyle = FontStyles.Bold;
        badge.alignment = hotbar ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.TopLeft;
        badge.text = "!";
        ItemUi.Stretch(badge.rectTransform, new Vector2(6, 5), new Vector2(-6, -3));
        return badge;
    }

    private static DurabilityEdge MakeDurability(RectTransform slot)
    {
        var rect = ItemUi.Rect(slot, "NewUI_DurabilityEdge");
        ItemUi.Stretch(rect, Vector2.zero, Vector2.zero);
        var edge = rect.gameObject.AddComponent<DurabilityEdge>();
        edge.raycastTarget = false;
        return edge;
    }

    private sealed class InventoryPanel : IDisposable
    {
        private static readonly int Visible = Animator.StringToHash("visible");
        private const float SlideSeconds = 0.27f;
        internal readonly InventoryGui Gui;
        private readonly Animator animator;
        private readonly Artwork art;
        private readonly TMP_FontAsset font;
        private readonly RectTransform root, rowHost, canvasRect, playerGridRoot;
        private readonly Vector3 gridScale;
        private readonly Vector2 restingPosition;
        private readonly StoragePanel storage;
        private readonly InventoryGrid containerGrid;
        private readonly RectTransform body;
        private readonly Image shadow;
        private readonly Vector2 shadowAnchorMin, shadowAnchorMax, shadowOffsetMin, shadowOffsetMax;
        private readonly bool shadowWasEnabled;
        private readonly Image weightTrack, weightFill;
        private readonly TextMeshProUGUI weightValue, weightLabel;
        private readonly HiddenGraphics nativeWeight;
        private readonly AutoPickupToggle pickup;
        private readonly EquipmentPanel equipment;
        private HiddenGraphics nativeArmor;
        private HiddenGraphics closedBackpack;
        private readonly RectTransform armorBadge;
        private readonly Vector2 armorPosition;
        private readonly List<PanelBackdrop> oldBackdrops = new();
        private readonly Dictionary<InventoryElement, BagSlot> cells = new();
        private readonly List<InventoryElement> deadCells = new();
        private readonly BagSlot[] row = new BagSlot[QuickSlots];
        private readonly BagSlot[] sideRow = new BagSlot[EquipmentInventory.QuickSlotCount];
        private readonly List<BagSlot> sideCells = new();
        private readonly SortedDictionary<int, BagSlot> gear = new();
        private readonly ItemTooltip tooltip;
        private readonly InventoryActions actions;
        private readonly RecipePinsView recipePins;
        private readonly Canvas canvas;
        private readonly List<RaycastResult> hits = new();
        private PointerEventData pointer;
        private InventoryElement pointerCell;
        private float pointedAt;
        private int inputFrame = -1;
        private bool wasOpen, laidOut, foundBackdrops;
        private float slideStarted;
        private int lastWeight = -1, lastCapacity = -1;
        private Rect lastBounds;

        internal bool IsOpen => animator && animator.GetBool(Visible);

        internal InventoryPanel(InventoryGui gui, Animator animator, Canvas canvas, Artwork art, TMP_FontAsset font, RecipePins pins)
        {
            Gui = gui;
            this.animator = animator;
            this.art = art;
            this.font = font;
            root = gui.m_player;
            canvasRect = (RectTransform)canvas.transform;
            playerGridRoot = gui.m_playerGrid.m_gridRoot;
            gridScale = playerGridRoot.localScale;
            restingPosition = root.anchoredPosition;
            this.canvas = canvas;
            containerGrid = gui.ContainerGrid;
            if (gui.m_container && containerGrid && gui.m_containerName)
                storage = new StoragePanel(gui, art, font);
            rowHost = ItemUi.Rect(gui.m_inventoryRoot, "NewUI_QuickSlotInput");
            ItemUi.Stretch(rowHost, Vector2.zero, Vector2.zero);
            rowHost.gameObject.SetActive(false);

            var nativeWeightBadge = Badge(gui.m_weight.rectTransform, gui.m_armor);
            nativeWeight = new HiddenGraphics(nativeWeightBadge.gameObject);
            nativeWeight.SetRaycasts(false);
            if (gui.m_armor)
            {
                armorBadge = Badge(gui.m_armor.rectTransform, gui.m_weight);
                armorPosition = armorBadge.anchoredPosition;
            }

            var plate = ItemUi.Image(root, "NewUI_Backpack", art["panel"]);
            body = plate.rectTransform;
            var nativeShadow = root.Find("Darken");
            if (nativeShadow && nativeShadow.TryGetComponent(out shadow))
            {
                var rect = shadow.rectTransform;
                shadowAnchorMin = rect.anchorMin;
                shadowAnchorMax = rect.anchorMax;
                shadowOffsetMin = rect.offsetMin;
                shadowOffsetMax = rect.offsetMax;
                shadowWasEnabled = shadow.enabled;
                shadow.enabled = false;
                body.SetSiblingIndex(rect.GetSiblingIndex() + 1);
            }
            else body.SetAsFirstSibling();
            body.gameObject.SetActive(false);
            weightTrack = ItemUi.Image(body, "WeightTrack", art["slot"]);
            weightFill = ItemUi.Image(weightTrack.transform, "WeightFill", art["meter-fill"]);
            weightFill.type = UnityEngine.UI.Image.Type.Filled;
            weightFill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            ItemUi.Stretch(weightFill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
            weightLabel = ItemUi.Text(body, "WeightLabel", font, 15, ItemUi.Muted);
            weightLabel.text = Localization.instance.Localize("$item_weight");
            weightLabel.alignment = TextAlignmentOptions.MidlineLeft;
            weightValue = ItemUi.Text(body, "WeightValue", font, 15, ItemUi.Ink);
            weightValue.alignment = TextAlignmentOptions.MidlineRight;
            weightValue.fontStyle = FontStyles.Bold;

            equipment = new EquipmentPanel(root, art, font);
            // The backpack plate is behind the native input surfaces. Keep the
            // interactive footer above them while sharing its position.
            pickup = new AutoPickupToggle(root, art, font);
            tooltip = new ItemTooltip(canvas, art, font);
            actions = new InventoryActions(gui, canvas, root, art, font);
            recipePins = new RecipePinsView(gui.m_inventoryRoot, "NewUI_InventoryRecipePins", pins, art, font, gui);
        }

        private RectTransform Badge(RectTransform label, TMP_Text neighboringStat)
        {
            var badge = label;
            while (badge.parent is RectTransform parent && parent != root && parent.IsChildOf(root)
                && !Gui.m_playerGrid.transform.IsChildOf(parent)
                && (!neighboringStat || !neighboringStat.transform.IsChildOf(parent)))
                badge = parent;
            return badge;
        }

        internal InventoryElement GetQuickTarget(int index) => row[index]?.Element;

        internal InventoryElement GetSideTarget(int index) => sideRow[index]?.Element;

        internal void Refresh(bool open, Player player, RectTransform firstQuickSlot, RectTransform lastQuickSlot, float pitch)
        {
            actions.Refresh(open);
            float pinSpace = recipePins.Refresh(open, firstQuickSlot, lastQuickSlot, firstQuickSlot, actions.IsOpen);
            if (!open)
            {
                if (wasOpen) Close();
                wasOpen = false;
                return;
            }
            if (!wasOpen)
            {
                slideStarted = Time.unscaledTime;
                wasOpen = true;
                closedBackpack?.Restore();
                closedBackpack = null;
                rowHost.gameObject.SetActive(true);
            }

            HideNativeBackdrops();
            int equipmentRow = EquipmentInventory.ReservedRow(player.GetInventory());
            if (equipmentRow >= 0)
            {
                if (armorBadge && nativeArmor == null)
                {
                    nativeArmor = new HiddenGraphics(armorBadge.gameObject);
                    nativeArmor.SetRaycasts(false);
                }
            }
            else
            {
                nativeArmor?.Restore();
                nativeArmor = null;
                equipment.Rect.gameObject.SetActive(false);
            }
            var currentTooltip = ItemTooltip.Current;
            InventoryElement hover = null;
            ItemDrop.ItemData hoverItem = null;
            RectTransform firstBagSlot = null;
            RectTransform lastBagSlot = null;
            // Collect the first row before reparenting it out of the grid. Its
            // InventoryElements stay registered with the game's player grid.
            RefreshGrid(Gui.m_playerGrid, equipmentRow, currentTooltip, ref hover, ref hoverItem, ref firstBagSlot, ref lastBagSlot);
            RectTransform storageFirst = null, storageLast = null;
            if (storage != null && Gui.IsContainerOpen())
            {
                RefreshGrid(containerGrid, -1, currentTooltip,
                    ref hover, ref hoverItem, ref storageFirst, ref storageLast);
            }
            if (equipmentRow >= 0)
            {
                foreach (var entry in gear)
                {
                    var slot = entry.Value;
                    if (!slot.Element || entry.Key >= EquipmentInventory.SlotCount) continue;
                    var item = slot.Item;
                    slot.Refresh(item);
                    if (slot.Element.m_tooltip != currentTooltip) continue;
                    hover = slot.Element;
                    hoverItem = item;
                }
            }
            foreach (var slot in row)
                if (slot != null && slot.Element && slot.Element.m_tooltip == currentTooltip)
                {
                    hover = slot.Element;
                    hoverItem = slot.Item;
                }
            foreach (var slot in sideRow)
                if (slot != null && slot.Element && slot.Element.m_tooltip == currentTooltip)
                {
                    hover = slot.Element;
                    hoverItem = slot.Item;
                }
            foreach (var pair in cells)
                if (!pair.Key) deadCells.Add(pair.Key);
            foreach (var dead in deadCells) cells.Remove(dead);
            deadCells.Clear();

            if (firstBagSlot && lastBagSlot || equipmentRow == 1 && gear.Count > 0)
            {
                float scale = FitBackpack(firstBagSlot, lastBagSlot, firstQuickSlot, pitch, pinSpace, storageFirst, storageLast);
                AlignBag(firstBagSlot, lastBagSlot, firstQuickSlot, pitch, pinSpace, equipmentRow >= 0);
                if (storageFirst && storageLast) storage.Fit(storageFirst, storageLast, scale, body);
                else storage?.Close();
                if (equipmentRow >= 0) equipment.Refresh(player, equipmentRow);
                UpdateWeight(player);
                pickup.Refresh();
                pickup.SetVisible(true);
                if (!body.gameObject.activeSelf) body.gameObject.SetActive(true);
            }
            else
            {
                // Show() can precede creation of the grid by a frame.
                SlideAway(firstQuickSlot, pitch, equipmentRow >= 0);
            }

            if (ZInput.IsExclusiveGamepadActive() || ZInput.IsTouchActive())
                tooltip.Refresh(currentTooltip, hover ? (RectTransform)hover.transform : null, hoverItem);
            else RefreshPointerTooltip(hover);
        }

        private void RefreshPointerTooltip(InventoryElement nativeHover)
        {
            bool blocked = actions.IsOpen || Menu.IsVisible() || Console.IsVisible()
                || Gui.m_splitDialog.IsActive || Gui.m_variantDialog.gameObject.activeSelf;
            if (blocked)
            {
                pointerCell = null;
                tooltip.Refresh(null, null, null);
                return;
            }
            if (inputFrame != Time.frameCount)
            {
                inputFrame = Time.frameCount;
                var eventSystem = EventSystem.current;
                InventoryElement found = null;
                if (eventSystem)
                {
                    if (pointer == null) pointer = new PointerEventData(eventSystem);
                    pointer.position = ZInput.pointerPosition;
                    eventSystem.RaycastAll(pointer, hits);
                    if (hits.Count > 0) found = hits[0].gameObject.GetComponentInParent<InventoryElement>();
                    hits.Clear();
                }
                if (pointerCell != found)
                {
                    pointerCell = found;
                    pointedAt = Time.unscaledTime;
                    if (nativeHover && nativeHover != pointerCell) UITooltip.HideTooltip();
                }
            }
            if (pointerCell && cells.TryGetValue(pointerCell, out var slot) && Time.unscaledTime - pointedAt >= 0.5f)
                tooltip.RefreshPointer(pointerCell.m_tooltip, slot.Item);
            else tooltip.Refresh(null, null, null);
        }

        private void HideNativeBackdrops()
        {
            if (!foundBackdrops)
            {
                // Resolve the native panel after its first layout pass. Its
                // original image is also visited by the general interface skin.
                if (root.rect.width <= 0 || !root.gameObject.activeInHierarchy) return;
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                {
                    if (image.transform.IsChildOf(body) || image.transform.IsChildOf(equipment.Rect) || !image.sprite) continue;
                    if (image.sprite != art["panel"] && !image.sprite.name.StartsWith("woodpanel", StringComparison.Ordinal)) continue;
                    if (image.rectTransform.rect.width < root.rect.width * 0.65f) continue;
                    oldBackdrops.Add(new PanelBackdrop(image));
                }
                foundBackdrops = oldBackdrops.Count > 0;
            }
            foreach (var backdrop in oldBackdrops) backdrop.Hide();
        }

        private sealed class PanelBackdrop
        {
            private readonly Image image;
            private readonly bool enabled, raycasts;

            internal PanelBackdrop(Image image)
            {
                this.image = image;
                enabled = image.enabled;
                raycasts = image.raycastTarget;
            }

            internal void Hide()
            {
                if (!image) return;
                if (image.enabled) image.enabled = false;
                if (image.raycastTarget) image.raycastTarget = false;
            }

            internal void Restore()
            {
                if (!image) return;
                image.enabled = enabled;
                image.raycastTarget = raycasts;
            }
        }

        private void RefreshGrid(InventoryGrid grid, int equipmentRow, UITooltip current, ref InventoryElement hover, ref ItemDrop.ItemData hoverItem,
            ref RectTransform first, ref RectTransform last)
        {
            if (!grid || !grid.m_gridRoot || !grid.gameObject.activeInHierarchy) return;
            var inventory = grid.GetInventory();
            if (inventory == null) return;
            var gridRoot = grid.m_gridRoot;
            for (int i = gridRoot.childCount - 1; i >= 0; i--)
            {
                if (!gridRoot.GetChild(i).TryGetComponent<InventoryElement>(out var element)) continue;
                var position = element.Position;
                bool playerGrid = grid == Gui.m_playerGrid;
                bool quick = playerGrid && position.y == 0 && position.x >= 0 && position.x < row.Length;
                bool equipped = playerGrid && position.y == equipmentRow;
                bool side = playerGrid && equipmentRow >= 0 && position.y == equipmentRow + 1;
                if (!cells.TryGetValue(element, out var slot))
                {
                    slot = new BagSlot(element, grid, art, font, quick || side ? rowHost : null, equipped ? equipment : null);
                    cells.Add(element, slot);
                    if (quick) row[position.x] = slot;
                    if (equipped) gear[position.x] = slot;
                    if (side)
                    {
                        sideCells.Add(slot);
                        if (position.x < sideRow.Length) sideRow[position.x] = slot;
                        else slot.Park();
                    }
                }
                if (equipped || side) continue;
                var item = inventory.GetItemAt(position.x, position.y);
                if (!quick) slot.Refresh(item);
                int startRow = playerGrid ? 1 : 0;
                if (position.y >= startRow)
                {
                    if (position.x == 0 && position.y == startRow) first = (RectTransform)element.transform;
                    int lastRow = equipmentRow >= 0 ? equipmentRow - 1 : inventory.GetHeight() - 1;
                    if (position.x == inventory.GetWidth() - 1 && position.y == lastRow)
                        last = (RectTransform)element.transform;
                }
                if (element.m_tooltip == current)
                {
                    hover = element;
                    hoverItem = item;
                }
            }
        }

        private void SlideAway(RectTransform firstQuickSlot, float pitch, bool showEquipment)
        {
            var left = firstQuickSlot.TransformPoint(firstQuickSlot.rect.center);
            float width = SlideWidth(pitch, showEquipment);
            root.position = new Vector3(left.x - Mathf.Abs(root.TransformVector(Vector3.right * width).x), root.position.y, root.position.z);
        }

        private float SlideWidth(float pitch, bool showEquipment) => root.rect.width + pitch * 2
            + (showEquipment ? EquipmentPanel.Width + EquipmentPanel.Gap : 0);

        private float FitBackpack(RectTransform first, RectTransform last, RectTransform quickSlot, float pitch, float pinSpace,
            RectTransform storageFirst, RectTransform storageLast)
        {
            float current = playerGridRoot.localScale.y / gridScale.y;
            float bagHeight = first && last ? WorldHeight(first, last) / current : 0;
            float baseline = quickSlot.TransformPoint(quickSlot.rect.center + Vector2.down * (pitch * 0.5f + 22)).y - pinSpace;
            bool hasStorage = storageFirst && storageLast;
            float floor = canvasRect.TransformPoint(new Vector2(0, canvasRect.rect.yMin +
                (hasStorage ? 30 : VitalsView.Clearance) + 76)).y;
            float storageHeight = hasStorage ? WorldHeight(storageFirst, storageLast) / storage.Scale : 0;
            if (hasStorage) floor += root.TransformVector(Vector3.up * StoragePanel.Overhead).y;
            float scale = Mathf.Clamp((baseline - floor) / Mathf.Max(1, bagHeight + storageHeight), 0.2f, 1);
            if (!Mathf.Approximately(current, scale)) playerGridRoot.localScale = gridScale * scale;
            return scale;
        }

        private static float WorldHeight(RectTransform first, RectTransform last) =>
            first.TransformPoint(new Vector2(first.rect.xMin, first.rect.yMax)).y
            - last.TransformPoint(new Vector2(last.rect.xMax, last.rect.yMin)).y;

        private void AlignBag(RectTransform first, RectTransform last, RectTransform quickSlot, float pitch, float pinSpace, bool showEquipment)
        {
            var baseline = quickSlot.TransformPoint(quickSlot.rect.center + Vector2.down * (pitch + 22));
            baseline.y -= pinSpace;
            var center = first ? first.TransformPoint(first.rect.center) :
                root.TransformPoint(new Vector2(root.rect.xMin + pitch / 2, root.rect.yMax - pitch / 2));
            var target = root.position + baseline - center;
            float time = Mathf.Clamp01((Time.unscaledTime - slideStarted) / SlideSeconds);
            float remaining = 1 - time;
            float distance = Mathf.Abs(root.TransformVector(Vector3.right * SlideWidth(pitch, showEquipment)).x);
            root.position = new Vector3(target.x - distance * remaining * remaining * remaining, target.y, target.z);

            var topLeft = first ? root.InverseTransformPoint(first.TransformPoint(new Vector2(first.rect.xMin, first.rect.yMax)))
                : new Vector3(root.rect.xMin, root.rect.yMax - pitch / 2);
            var bottomRight = last ? root.InverseTransformPoint(last.TransformPoint(new Vector2(last.rect.xMax, last.rect.yMin)))
                : topLeft + Vector3.right * (pitch * QuickSlots);
            var bounds = Rect.MinMaxRect(topLeft.x, bottomRight.y, bottomRight.x, topLeft.y);
            if (!laidOut || (bounds.position - lastBounds.position).sqrMagnitude > 0.01f
                || (bounds.size - lastBounds.size).sqrMagnitude > 0.01f)
            {
                laidOut = true;
                lastBounds = bounds;
                const float frame = 16;
                float width = bounds.width + frame * 2;
                float height = bounds.height + frame * 2 + 56;
                ItemUi.TopLeft(body, bounds.xMin - root.rect.xMin - frame, root.rect.yMax - bounds.yMax - frame, width, height);
                if (shadow)
                {
                    // Keep the shadow's top edge at the backpack, below the hotbar.
                    const float spread = 32;
                    var rect = shadow.rectTransform;
                    rect.anchorMin = rect.anchorMax = Vector2.up;
                    rect.offsetMin = body.offsetMin - new Vector2(spread, spread);
                    rect.offsetMax = body.offsetMax + new Vector2(spread, 0);
                }
                float footer = frame + bounds.height + 9;
                float half = (width - frame * 2) / 2;
                ItemUi.TopLeft(weightLabel.rectTransform, frame + half + 2, footer, half - 4, 21);
                ItemUi.TopLeft(weightValue.rectTransform, frame + half + 2, footer, half - 4, 21);
                ItemUi.TopLeft(weightTrack.rectTransform, frame + half, footer + 23, half, 9);
                pickup.Align(body.anchoredPosition.x + frame, -body.anchoredPosition.y + footer + 1, half - 12);
                actions.AlignHint(body.anchoredPosition.x + frame, -body.anchoredPosition.y + footer + 34, width - frame * 2);
                equipment.Align(body);
                if (armorBadge) armorBadge.anchoredPosition = armorPosition + Vector2.down * pitch;
            }
            if (shadow && shadow.enabled != shadowWasEnabled) shadow.enabled = shadowWasEnabled;
        }

        private void UpdateWeight(Player player)
        {
            float current = player.GetInventory().GetTotalWeight();
            float capacity = player.GetMaxCarryWeight();
            weightFill.fillAmount = capacity > 0 ? Mathf.Clamp01(current / capacity) : 0;
            bool overloaded = current > capacity;
            weightFill.color = overloaded ? ItemUi.Warning : ItemUi.Accent;
            weightValue.color = overloaded ? ItemUi.Warning : ItemUi.Ink;
            int rounded = Mathf.CeilToInt(current);
            int roundedMax = Mathf.CeilToInt(capacity);
            if (lastWeight == rounded && lastCapacity == roundedMax) return;
            lastWeight = rounded;
            lastCapacity = roundedMax;
            weightValue.text = $"{rounded} / {roundedMax}";
        }

        internal void PositionQuickTargets(QuickSlot[] slots) => PositionTargets(row, slots);

        internal void PositionSideTargets(QuickSlot[] slots) => PositionTargets(sideRow, slots);

        private static void PositionTargets(BagSlot[] cells, QuickSlot[] slots)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                var slot = cells[i];
                if (slot == null || !slot.Element) continue;
                var source = slots[i].Rect;
                var target = (RectTransform)slot.Element.transform;
                var size = target.InverseTransformVector(source.TransformVector(source.rect.size));
                target.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
                var delta = source.TransformPoint(source.rect.center) - target.TransformPoint(target.rect.center);
                if (delta.sqrMagnitude > 0.0001f) target.position += delta;
            }
        }

        private void Close()
        {
            root.anchoredPosition = restingPosition;
            playerGridRoot.localScale = gridScale;
            // The stock closing tween moves these cells through the stationary
            // hotbar and can freeze midway when the pause menu opens.
            closedBackpack = new HiddenGraphics(playerGridRoot.gameObject);
            closedBackpack.SetRaycasts(false);
            storage?.Close();
            pointerCell = null;
            if (shadow) shadow.enabled = false;
            body.gameObject.SetActive(false);
            equipment.Rect.gameObject.SetActive(false);
            pickup.SetVisible(false);
            rowHost.gameObject.SetActive(false);
            tooltip.Refresh(null, null, null);
        }

        public void Dispose()
        {
            recipePins.Dispose();
            tooltip.Dispose();
            // Reinsert the detached cells in their original sibling order.
            foreach (var slot in row) slot?.Dispose();
            foreach (var slot in sideCells) slot.Dispose();
            foreach (var slot in gear.Values) slot.Dispose();
            foreach (var slot in cells.Values)
                if (!slot.IsDetached) slot.Dispose();
            foreach (var backdrop in oldBackdrops) backdrop.Restore();
            nativeWeight.Restore();
            nativeArmor?.Restore();
            closedBackpack?.Restore();
            pickup.Dispose();
            if (root) root.anchoredPosition = restingPosition;
            if (playerGridRoot) playerGridRoot.localScale = gridScale;
            if (armorBadge) armorBadge.anchoredPosition = armorPosition;
            storage?.Dispose();
            actions.Dispose();
            if (shadow)
            {
                var rect = shadow.rectTransform;
                rect.anchorMin = shadowAnchorMin;
                rect.anchorMax = shadowAnchorMax;
                rect.offsetMin = shadowOffsetMin;
                rect.offsetMax = shadowOffsetMax;
                shadow.enabled = shadowWasEnabled;
            }
            ItemUi.Destroy(rowHost);
            ItemUi.Destroy(body);
            ItemUi.Destroy(equipment.Rect);
        }
    }

    private sealed class BagSlot
    {
        internal readonly InventoryElement Element;
        private readonly HiddenGraphics hidden, nativeDurability;
        private readonly ItemQualityBadge quality;
        private readonly bool iconPreservedAspect;
        private readonly UIItemDropTarget dropTarget;
        private readonly RectTransform rect, originalParent;
        private readonly Vector2 originalAnchoredPosition, originalSize, originalAnchorMin, originalAnchorMax, originalPivot;
        private readonly int originalOrder;
        private readonly Vector3 originalScale;
        private readonly Image nativeFace;
        private readonly Sprite originalFaceSprite;
        private readonly UnityEngine.UI.Image.Type originalFaceType;
        private readonly Color originalFaceColor;
        private readonly Image fallbackFace, pointerSurface;
        private readonly bool amountRaycast;
        private readonly DurabilityEdge edge;
        private readonly TextMeshProUGUI cheated;

        internal bool IsDetached => originalParent != null;

        internal ItemDrop.ItemData Item
        {
            get
            {
                var position = Element.Position;
                return dropTarget.Grid.GetInventory()?.GetItemAt(position.x, position.y);
            }
        }

        internal BagSlot(InventoryElement element, InventoryGrid grid, Artwork art, TMP_FontAsset font, RectTransform rowHost,
            EquipmentPanel equipment)
        {
            Element = element;
            rect = (RectTransform)Element.transform;
            var inputHost = element.GetComponentInChildren<UIDragHandler>(true);
            dropTarget = (inputHost ? inputHost.gameObject : element.gameObject).AddComponent<UIItemDropTarget>();
            dropTarget.Element = element;
            dropTarget.Grid = grid;
            if (element.m_amount)
            {
                amountRaycast = element.m_amount.raycastTarget;
                element.m_amount.raycastTarget = false;
            }
            // Cover the full cell without displacing its native button or drag handler.
            pointerSurface = ItemUi.Image(dropTarget.transform, "SlotPointerSurface");
            pointerSurface.color = Color.clear;
            pointerSurface.raycastTarget = true;
            ItemUi.Stretch(pointerSurface.rectTransform, Vector2.zero, Vector2.zero);
            pointerSurface.rectTransform.SetAsFirstSibling();
            if (rowHost || equipment != null)
            {
                originalParent = (RectTransform)rect.parent;
                originalOrder = rect.GetSiblingIndex();
                originalAnchoredPosition = rect.anchoredPosition;
                originalSize = rect.sizeDelta;
                originalPivot = rect.pivot;
                originalAnchorMin = rect.anchorMin;
                originalAnchorMax = rect.anchorMax;
                originalScale = rect.localScale;
                if (rowHost)
                {
                    hidden = new HiddenGraphics(element.gameObject);
                    rect.SetParent(rowHost, false);
                    rect.anchorMin = rect.anchorMax = rowHost.pivot;
                    return;
                }
                equipment.AttachCell(rect, element.Position.x);
                if (element.Position.x >= EquipmentInventory.SlotCount)
                {
                    hidden = new HiddenGraphics(element.gameObject);
                    hidden.SetRaycasts(false);
                    return;
                }
            }

            nativeFace = element.m_button ? element.m_button.targetGraphic as Image : null;
            if (nativeFace)
            {
                originalFaceSprite = nativeFace.sprite;
                originalFaceType = nativeFace.type;
                originalFaceColor = nativeFace.color;
                nativeFace.sprite = art["slot"];
                nativeFace.type = UnityEngine.UI.Image.Type.Sliced;
                nativeFace.color = Color.white;
            }
            else
            {
                fallbackFace = ItemUi.Image(rect, "NewUI_SlotFace", art["slot"]);
                ItemUi.Stretch(fallbackFace.rectTransform, Vector2.zero, Vector2.zero);
                fallbackFace.rectTransform.SetAsFirstSibling();
            }
            if (element.m_icon)
            {
                iconPreservedAspect = element.m_icon.preserveAspect;
                element.m_icon.preserveAspect = true;
            }
            if (element.m_durability) nativeDurability = new HiddenGraphics(element.m_durability.gameObject);
            cheated = MakeCheatBadge(rect, font, hotbar: false);
            quality = new ItemQualityBadge(rect, art, font, element.m_quality, element.m_icon);
            edge = MakeDurability(rect);
        }

        // Cells of the quick slot row past the third slot are not shown anywhere.
        internal void Park()
        {
            hidden?.SetRaycasts(false);
            rect.anchoredPosition = new Vector2(-10000, 0);
        }

        internal void Refresh(ItemDrop.ItemData item)
        {
            cheated.enabled = item?.m_cheated == true;
            quality.Refresh(item);
            edge.SetItem(item);
        }

        internal void Dispose()
        {
            if (!Element) return;
            if (dropTarget)
            {
                dropTarget.enabled = false;
                UnityEngine.Object.Destroy(dropTarget);
            }
            if (originalParent)
            {
                rect.SetParent(originalParent, false);
                rect.SetSiblingIndex(originalOrder);
                rect.anchorMin = originalAnchorMin;
                rect.anchorMax = originalAnchorMax;
                rect.pivot = originalPivot;
                rect.anchoredPosition = originalAnchoredPosition;
                rect.sizeDelta = originalSize;
                rect.localScale = originalScale;
            }
            hidden?.Restore();
            if (Element.m_amount) Element.m_amount.raycastTarget = amountRaycast;
            ItemUi.Destroy(pointerSurface);
            if (nativeFace)
            {
                nativeFace.sprite = originalFaceSprite;
                nativeFace.type = originalFaceType;
                nativeFace.color = originalFaceColor;
            }
            if (quality != null && Element.m_icon) Element.m_icon.preserveAspect = iconPreservedAspect;
            nativeDurability?.Restore();
            quality?.Dispose();
            ItemUi.Destroy(fallbackFace);
            ItemUi.Destroy(cheated);
            ItemUi.Destroy(edge);
        }
    }
}

// Stock mouse input picks up items on pointer-down. Accept a release over a
// different cell too, with the same native transaction used by touch drag.
internal sealed class UIItemDropTarget : MonoBehaviour, IDropHandler
{
    internal InventoryElement Element;
    internal InventoryGrid Grid;

    public void OnDrop(PointerEventData eventData)
    {
        if (ZInput.IsTouchActive() || eventData.button != PointerEventData.InputButton.Left || !eventData.pointerDrag) return;
        var source = eventData.pointerDrag.GetComponentInParent<UIItemDropTarget>();
        if (!source || source == this || !InventoryGui.IsVisible() || !Grid) return;
        var position = Element.Position;
        var inventory = Grid.GetInventory();
        if (inventory != null) Grid.m_onReleased?.Invoke(Grid, inventory.GetItemAt(position.x, position.y), position);
    }

}
