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
    private readonly QuickSlot[] quickSlots = new QuickSlot[QuickSlots];
    private readonly Dictionary<Transform, NativeQuickSlot> nativeQuickSlots = new();
    private InventoryPanel panel;

    internal ItemInterface(HotkeyBar hotbar, Artwork art, TMP_FontAsset font)
    {
        this.hotbar = hotbar;
        hotbarRoot = (RectTransform)hotbar.transform;
        this.art = art;
        this.font = font;
        var template = (RectTransform)hotbar.m_elementPrefab.transform;
        for (int i = 0; i < quickSlots.Length; i++)
            quickSlots[i] = new QuickSlot(hotbarRoot, template, i, hotbar.m_elementSpace, art, font);
    }

    internal void Refresh(InventoryGui gui, Player player)
    {
        if (panel != null && (!gui || panel.Gui != gui)) ReleasePanel();
        if (panel == null && gui && gui.m_player && gui.m_playerGrid && gui.m_inventoryRoot && gui.m_weight)
        {
            var canvas = gui.m_inventoryRoot.GetComponentInParent<Canvas>(true);
            var animator = gui.GetComponent<Animator>();
            if (canvas && animator) panel = new InventoryPanel(gui, animator, canvas.rootCanvas, art, font);
        }

        bool alive = player && !player.IsDead();
        bool open = alive && panel != null && panel.IsOpen;
        var inventory = alive ? player.GetInventory() : null;
        panel?.Refresh(open, player, quickSlots[0].Rect, hotbar.m_elementSpace);
        foreach (var slot in quickSlots) slot.SetVisible(alive);

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

        if (open) panel.PositionQuickTargets(quickSlots);
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
        ReleasePanel();
        foreach (var entry in nativeQuickSlots.Values) entry.Hidden.Restore();
        foreach (var slot in quickSlots) ItemUi.Destroy(slot.Rect);
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
        private readonly Image icon, selection, equipped;
        private readonly DurabilityEdge edge;
        private readonly TextMeshProUGUI binding, amount, cheated;
        private ItemDrop.ItemData lastItem;
        private int lastStack = -1;

        internal QuickSlot(RectTransform parent, RectTransform template, int index, float pitch, Artwork art, TMP_FontAsset font)
        {
            Rect = ItemUi.Rect(parent, "NewUI_QuickSlot" + (index + 1));
            Rect.anchorMin = Rect.anchorMax = parent.pivot;
            Rect.pivot = template.pivot;
            Rect.sizeDelta = template.rect.size;
            Rect.anchoredPosition = new Vector2(index * pitch, 0);

            var face = ItemUi.Image(Rect, "Face", art["slot"]);
            ItemUi.Stretch(face.rectTransform, Vector2.zero, Vector2.zero);
            equipped = ItemUi.Image(Rect, "Equipped", art["selected"]);
            equipped.color = new Color32(112, 189, 160, 210);
            ItemUi.Stretch(equipped.rectTransform, Vector2.zero, Vector2.zero);

            selection = ItemUi.Image(Rect, "Focus", art["selected"]);
            selection.color = ItemUi.Accent;
            ItemUi.Stretch(selection.rectTransform, Vector2.zero, Vector2.zero);
            icon = ItemUi.Image(Rect, "ItemIcon");
            icon.preserveAspect = true;
            ItemUi.Stretch(icon.rectTransform, new Vector2(8, 9), new Vector2(-8, -6));

            binding = ItemUi.Text(Rect, "Binding", font, 15, ItemUi.Ink);
            binding.fontStyle = FontStyles.Bold;
            binding.alignment = TextAlignmentOptions.TopLeft;
            ItemUi.Stretch(binding.rectTransform, new Vector2(7, 0), new Vector2(0, -4));
            binding.text = (index + 1).ToString();

            amount = ItemUi.Text(Rect, "Stack", font, 14, ItemUi.Ink);
            amount.fontStyle = FontStyles.Bold;
            amount.alignment = TextAlignmentOptions.Bottom;
            ItemUi.Stretch(amount.rectTransform, new Vector2(2, 5), new Vector2(-2, 0));
            cheated = MakeCheatBadge(Rect, font, true);
            edge = MakeDurability(Rect);
        }

        internal void SetVisible(bool value)
        {
            if (Rect.gameObject.activeSelf != value) Rect.gameObject.SetActive(value);
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
            equipped.enabled = item != null && (item.m_equipped || player.IsEquipActionQueued(item));
            SetFocused(target && target.m_selected && target.m_selected.activeSelf);
            cheated.enabled = item?.m_cheated == true;
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

    private static TextMeshProUGUI MakeCheatBadge(RectTransform slot, TMP_FontAsset font, bool right)
    {
        var badge = ItemUi.Text(slot, "CheatedItem", font, 20, new Color32(255, 104, 101, 255));
        badge.fontStyle = FontStyles.Bold;
        badge.alignment = right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
        badge.text = "!";
        ItemUi.Stretch(badge.rectTransform, new Vector2(6, 0), new Vector2(-6, -3));
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
        private readonly RectTransform root, rowHost;
        private readonly Vector2 restingPosition, containerPosition;
        private readonly RectTransform container;
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
        private readonly RectTransform armorBadge;
        private readonly Vector2 armorPosition;
        private readonly List<PanelBackdrop> oldBackdrops = new();
        private readonly Dictionary<InventoryElement, BagSlot> cells = new();
        private readonly List<InventoryElement> deadCells = new();
        private readonly BagSlot[] row = new BagSlot[QuickSlots];
        private readonly SortedDictionary<int, BagSlot> gear = new();
        private readonly ItemTooltip tooltip;
        private bool wasOpen, laidOut, shiftedContainer, foundBackdrops;
        private float slideStarted;
        private int lastWeight = -1, lastCapacity = -1;
        private Rect lastBounds;

        internal bool IsOpen => animator && animator.GetBool(Visible);

        internal InventoryPanel(InventoryGui gui, Animator animator, Canvas canvas, Artwork art, TMP_FontAsset font)
        {
            Gui = gui;
            this.animator = animator;
            this.art = art;
            this.font = font;
            root = gui.m_player;
            restingPosition = root.anchoredPosition;
            container = gui.m_container;
            if (container)
            {
                containerPosition = container.anchoredPosition;
                containerGrid = container.GetComponentInChildren<InventoryGrid>(true);
            }
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

        internal void Refresh(bool open, Player player, RectTransform firstQuickSlot, float pitch)
        {
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
            if (Gui.IsContainerOpen() && container && container.gameObject.activeInHierarchy)
            {
                RectTransform unusedFirst = null, unusedLast = null;
                RefreshGrid(containerGrid, -1, currentTooltip,
                    ref hover, ref hoverItem, ref unusedFirst, ref unusedLast);
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
            for (int i = 0; i < row.Length; i++)
            {
                var slot = row[i];
                if (slot == null || !slot.Element) continue;
                var item = slot.Item;
                if (slot.Element.m_tooltip == currentTooltip)
                {
                    hover = slot.Element;
                    hoverItem = item;
                }
            }
            foreach (var pair in cells)
                if (!pair.Key) deadCells.Add(pair.Key);
            foreach (var dead in deadCells) cells.Remove(dead);
            deadCells.Clear();

            if (firstBagSlot && lastBagSlot)
            {
                AlignBag(firstBagSlot, lastBagSlot, firstQuickSlot, pitch, equipmentRow >= 0);
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

            tooltip.Refresh(currentTooltip, hover ? (RectTransform)hover.transform : null, hoverItem);
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
                if (!cells.TryGetValue(element, out var slot))
                {
                    slot = new BagSlot(element, grid, art, font, quick ? rowHost : null, equipped ? equipment : null);
                    cells.Add(element, slot);
                    if (quick) row[position.x] = slot;
                    if (equipped) gear[position.x] = slot;
                }
                if (equipped) continue;
                var item = inventory.GetItemAt(position.x, position.y);
                if (!quick) slot.Refresh(item);
                if (playerGrid && position.y >= 1)
                {
                    if (position.x == 0 && position.y == 1) first = (RectTransform)element.transform;
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

        private void AlignBag(RectTransform first, RectTransform last, RectTransform quickSlot, float pitch, bool showEquipment)
        {
            var baseline = quickSlot.TransformPoint(quickSlot.rect.center + Vector2.down * (pitch + 22));
            var center = first.TransformPoint(first.rect.center);
            var target = root.position + baseline - center;
            float time = Mathf.Clamp01((Time.unscaledTime - slideStarted) / SlideSeconds);
            float remaining = 1 - time;
            float distance = Mathf.Abs(root.TransformVector(Vector3.right * SlideWidth(pitch, showEquipment)).x);
            root.position = new Vector3(target.x - distance * remaining * remaining * remaining, target.y, target.z);

            var topLeft = root.InverseTransformPoint(first.TransformPoint(new Vector2(first.rect.xMin, first.rect.yMax)));
            var bottomRight = root.InverseTransformPoint(last.TransformPoint(new Vector2(last.rect.xMax, last.rect.yMin)));
            var bounds = Mathf.Max(0, topLeft.y - bottomRight.y) <= root.rect.height
                ? Rect.MinMaxRect(topLeft.x, bottomRight.y, bottomRight.x, topLeft.y)
                : Rect.MinMaxRect(topLeft.x, Mathf.Max(root.rect.yMin + 12, bottomRight.y), bottomRight.x, topLeft.y);
            if (!laidOut || (bounds.position - lastBounds.position).sqrMagnitude > 0.01f
                || (bounds.size - lastBounds.size).sqrMagnitude > 0.01f)
            {
                laidOut = true;
                lastBounds = bounds;
                const float frame = 16;
                float width = bounds.width + frame * 2;
                float height = bounds.height + frame * 2 + 34;
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
                equipment.Align(body, height);
                if (armorBadge) armorBadge.anchoredPosition = armorPosition + Vector2.down * pitch;
            }
            if (shadow && shadow.enabled != shadowWasEnabled) shadow.enabled = shadowWasEnabled;

            if (container && container.gameObject.activeSelf && Gui.IsContainerOpen())
            {
                container.anchoredPosition = containerPosition;
                float bottom = body.TransformPoint(new Vector2(0, body.rect.yMin)).y;
                float top = container.TransformPoint(new Vector2(0, container.rect.yMax)).y;
                float gap = root.TransformVector(Vector3.up * 10).y;
                float overlap = top + gap - bottom;
                if (overlap > 0) container.position -= Vector3.up * overlap;
                shiftedContainer = true;
            }
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

        internal void PositionQuickTargets(QuickSlot[] slots)
        {
            for (int i = 0; i < row.Length; i++)
            {
                var slot = row[i];
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
            if (shiftedContainer && container) container.anchoredPosition = containerPosition;
            shiftedContainer = false;
            if (shadow) shadow.enabled = false;
            body.gameObject.SetActive(false);
            equipment.Rect.gameObject.SetActive(false);
            pickup.SetVisible(false);
            rowHost.gameObject.SetActive(false);
            tooltip.Refresh(null, null, null);
        }

        public void Dispose()
        {
            tooltip.Dispose();
            // Reinsert the detached cells in their original sibling order.
            foreach (var slot in row) slot?.Dispose();
            foreach (var slot in gear.Values) slot.Dispose();
            foreach (var slot in cells.Values)
                if (!slot.IsDetached) slot.Dispose();
            foreach (var backdrop in oldBackdrops) backdrop.Restore();
            nativeWeight.Restore();
            nativeArmor?.Restore();
            pickup.Dispose();
            if (root) root.anchoredPosition = restingPosition;
            if (armorBadge) armorBadge.anchoredPosition = armorPosition;
            if (shiftedContainer && container) container.anchoredPosition = containerPosition;
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
        private readonly UIItemDropTarget dropTarget;
        private readonly RectTransform rect, originalParent;
        private readonly Vector2 originalAnchoredPosition, originalSize, originalAnchorMin, originalAnchorMax;
        private readonly int originalOrder;
        private readonly Vector3 originalScale;
        private readonly Image nativeFace;
        private readonly Sprite originalFaceSprite;
        private readonly UnityEngine.UI.Image.Type originalFaceType;
        private readonly Color originalFaceColor;
        private readonly Image fallbackFace;
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
            if (rowHost || equipment != null)
            {
                originalParent = (RectTransform)rect.parent;
                originalOrder = rect.GetSiblingIndex();
                originalAnchoredPosition = rect.anchoredPosition;
                originalSize = rect.sizeDelta;
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
            if (element.m_durability) nativeDurability = new HiddenGraphics(element.m_durability.gameObject);
            cheated = MakeCheatBadge(rect, font, false);
            edge = MakeDurability(rect);
        }

        internal void Refresh(ItemDrop.ItemData item)
        {
            cheated.enabled = item?.m_cheated == true;
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
                rect.anchoredPosition = originalAnchoredPosition;
                rect.sizeDelta = originalSize;
                rect.localScale = originalScale;
            }
            hidden?.Restore();
            if (nativeFace)
            {
                nativeFace.sprite = originalFaceSprite;
                nativeFace.type = originalFaceType;
                nativeFace.color = originalFaceColor;
            }
            nativeDurability?.Restore();
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
