using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Item = ItemDrop.ItemData;
using ItemType = ItemDrop.ItemData.ItemType;

namespace ValheimNewUI;

internal static class EquipmentInventory
{
    internal const int SlotCount = 6;
    internal const int QuickSlotCount = 3;
    // The equipment row and the quick slot row follow the backpack.
    private const int ExtraRows = 2;
    private const int MaxBackpackRows = 9;
    private const string OriginalRowsKey = "valheimnewui_base_rows";
    private const string QuickSlotsKey = "valheimnewui_quick_slots";
    private static readonly ItemType[] SlotTypes =
    {
        ItemType.Helmet, ItemType.Chest, ItemType.Legs, ItemType.Shoulder, ItemType.Utility, ItemType.Trinket
    };
    private static readonly AccessTools.FieldRef<InventoryGui, Item> DragItem =
        AccessTools.FieldRefAccess<InventoryGui, Item>("m_dragItem");
    private static readonly AccessTools.FieldRef<InventoryGui, Inventory> DragInventory =
        AccessTools.FieldRefAccess<InventoryGui, Inventory>("m_dragInventory");
    private static readonly System.Action<InventoryGui, Item, Inventory, int> SetupDrag =
        AccessTools.MethodDelegate<System.Action<InventoryGui, Item, Inventory, int>>(
            AccessTools.Method(typeof(InventoryGui), "SetupDragItem"));

    private static Player spawned;
    private static Inventory active;
    private static int rows;
    private static bool recoveryBlocked;
    private static bool enabledInWorld, spawning;
    private static Item upgrading;
    private static bool dropping;

    internal static int ReservedRow(Inventory inventory) => active != null && inventory == active ? rows : -1;

    internal static int QuickRow(Inventory inventory) => active != null && inventory == active ? rows + 1 : -1;

    internal static Item QuickItem(Inventory inventory, int slot)
    {
        int row = QuickRow(inventory);
        return row < 0 || slot < 0 || slot >= QuickSlotCount ? null : inventory.GetItemAt(slot, row);
    }

    internal static void Refresh(bool enabled)
    {
        enabledInWorld = enabled;
        var player = Player.m_localPlayer;
        if (!player || player != spawned || !InventoryGui.instance || player.IsTeleporting() || player.IsDead())
        {
            if (!player || player != spawned) active = null;
            return;
        }

        var inventory = player.GetInventory();
        bool stored = TryReadBackpackRows(player, out int original);
        if (!stored || inventory.GetHeight() != original + ExtraRows)
        {
            // Earlier versions reserved a single row after the backpack.
            if (!stored || inventory.GetHeight() != original + 1) original = inventory.GetHeight();
            if (enabled && original >= 1 && original <= MaxBackpackRows && inventory.GetWidth() >= SlotCount)
                Resize(player, inventory, original);
            else
                active = null;
            return;
        }

        if (!enabled)
        {
            active = null;
            Recover(player, inventory, original);
            return;
        }

        active = inventory;
        rows = original;
        recoveryBlocked = false;
        PlaceEquipment(inventory);
    }

    private static bool TryReadBackpackRows(Player player, out int original)
    {
        original = 0;
        return player.TryGetUniqueKeyValue(OriginalRowsKey, out var stored) && int.TryParse(stored, out original)
            && original >= 1 && original <= MaxBackpackRows;
    }

    private static int EquipmentSlot(Item item) =>
        item.m_equipped ? System.Array.IndexOf(SlotTypes, item.m_shared.m_itemType) : -1;

    private static void Resize(Player player, Inventory inventory, int backpackRows)
    {
        var items = inventory.GetAllItems();
        int quests = 0;
        foreach (var item in items)
            if (item.m_shared.m_questItem && EquipmentSlot(item) < 0) quests++;
        if (quests > backpackRows * inventory.GetWidth())
        {
            player.Message(MessageHud.MessageType.Center, "$inventory_full");
            return;
        }

        // Repack before the native invalid-item sweep. Equipped items retain their
        // actual ItemData and equipped state across both growth and shrinkage.
        int quickRow = backpackRows + 1, parked = backpackRows + ExtraRows;
        // While spawning, the saved backpack size is the one being restored.
        int previousQuick = spawning ? backpackRows + 1 : active == inventory ? rows + 1 : -1;
        var quick = new List<Item>();
        foreach (var item in items)
            if (item.m_gridPos.y == previousQuick && item.m_gridPos.x < QuickSlotCount && EquipmentSlot(item) < 0)
                quick.Add(item);
        foreach (var item in items)
            if (!quick.Contains(item) && (EquipmentSlot(item) >= 0 || item.m_gridPos.y >= backpackRows))
                item.m_gridPos = new Vector2i(item.m_gridPos.x, parked);
        foreach (var item in quick) item.m_gridPos = new Vector2i(item.m_gridPos.x, quickRow);
        foreach (var item in items)
        {
            int slot = EquipmentSlot(item);
            if (slot >= 0 && inventory.GetItemAt(slot, backpackRows) == null)
                item.m_gridPos = new Vector2i(slot, backpackRows);
        }
        foreach (var item in items)
        {
            if (!item.m_shared.m_questItem || item.m_gridPos.y != parked) continue;
            var free = FindBackpackSlot(inventory, backpackRows, false);
            if (free.x < 0)
            {
                var regular = items.Find(other => !other.m_shared.m_questItem && other.m_gridPos.y < backpackRows);
                free = regular.m_gridPos;
                regular.m_gridPos = new Vector2i(free.x, parked);
            }
            item.m_gridPos = free;
        }
        foreach (var item in items)
        {
            if (item.m_gridPos.y != parked) continue;
            var free = FindBackpackSlot(inventory, backpackRows, false);
            if (free.x < 0) break;
            item.m_gridPos = free;
        }

        if (DragInventory(InventoryGui.instance) == inventory) SetupDrag(InventoryGui.instance, null, null, 1);
        active = inventory;
        rows = backpackRows;
        recoveryBlocked = false;
        inventory.SetHeight(rows + ExtraRows);
        if (spawning) RestoreQuickSlots(player, inventory, rows + 1);
        player.AddUniqueKeyValue(OriginalRowsKey, rows.ToString());
        player.AddUniqueKeyValue("invrows", rows.ToString());
        InventoryGui.instance.SetInventorySize(rows + ExtraRows);
        player.DropInvalidItems();
    }

    // Saves keep quick slot items in ordinary backpack cells. The key records
    // which of those cells return to which quick slot on the next spawn.
    private static void RestoreQuickSlots(Player player, Inventory inventory, int quickRow)
    {
        if (!player.TryGetUniqueKeyValue(QuickSlotsKey, out var stored)) return;
        foreach (var entry in stored.Split(','))
        {
            var parts = entry.Split(':');
            if (parts.Length != 3 || !int.TryParse(parts[0], out int slot) || !int.TryParse(parts[1], out int x)
                || !int.TryParse(parts[2], out int y) || slot < 0 || slot >= QuickSlotCount) continue;
            var item = inventory.GetItemAt(x, y);
            if (item == null || y >= quickRow - 1 || EquipmentSlot(item) >= 0 || inventory.GetItemAt(slot, quickRow) != null) continue;
            item.m_gridPos = new Vector2i(slot, quickRow);
        }
    }

    private static void PlaceEquipment(Inventory inventory)
    {
        foreach (var item in inventory.GetAllItems())
        {
            int slot = EquipmentSlot(item);
            if (slot < 0) continue;
            var occupant = inventory.GetItemAt(slot, rows);
            if (occupant == item) continue;
            if (occupant != null)
            {
                // A replacement uses the incoming item's previous backpack or quick slot cell.
                bool quickCell = item.m_gridPos.y == rows + 1 && item.m_gridPos.x < QuickSlotCount;
                var position = item.m_gridPos.y < rows || quickCell ? item.m_gridPos : FindBackpackSlot(inventory, rows, false);
                if (position.x < 0) continue;
                occupant.m_gridPos = position;
            }
            item.m_gridPos = new Vector2i(slot, rows);
        }

        foreach (var item in inventory.GetAllItems())
        {
            bool loose = item.m_gridPos.y == rows && !item.m_equipped
                || item.m_gridPos.y == rows + 1 && item.m_gridPos.x >= QuickSlotCount;
            if (!loose) continue;
            var position = FindBackpackSlot(inventory, rows, false);
            if (position.x >= 0) item.m_gridPos = position;
        }
    }

    private static void Recover(Player player, Inventory inventory, int baseRows)
    {
        // Quest items cannot be dropped by Valheim, so give them the free cells first.
        bool questStillOutside = false;
        foreach (var item in inventory.GetAllItems())
        {
            if (item.m_gridPos.y < baseRows || !item.m_shared.m_questItem) continue;
            var position = FindBackpackSlot(inventory, baseRows, false);
            if (position.x < 0) questStillOutside = true;
            else item.m_gridPos = position;
        }

        if (questStillOutside)
        {
            if (!recoveryBlocked) player.Message(MessageHud.MessageType.Center, "$inventory_full");
            recoveryBlocked = true;
            return;
        }

        foreach (var item in inventory.GetAllItems())
        {
            if (item.m_gridPos.y < baseRows) continue;
            var position = FindBackpackSlot(inventory, baseRows, false);
            if (position.x < 0) break;
            item.m_gridPos = position;
        }

        if (InventoryGui.instance && DragInventory(InventoryGui.instance) == inventory)
            SetupDrag(InventoryGui.instance, null, null, 1);
        // Valheim drops any remaining out-of-bounds items and clears their equipped state.
        player.SetInventorySize(baseRows);
        player.RemoveUniqueKeyValue(OriginalRowsKey);
        player.RemoveUniqueKeyValue(QuickSlotsKey);
        recoveryBlocked = false;
    }

    private static Vector2i FindBackpackSlot(Inventory inventory, int height, bool topFirst)
    {
        for (int row = 0; row < height; row++)
        {
            int y = topFirst ? row : height - 1 - row;
            for (int x = 0; x < inventory.GetWidth(); x++)
                if (inventory.GetItemAt(x, y) == null) return new Vector2i(x, y);
        }
        return new Vector2i(-1, -1);
    }

    private static int BackpackSpace(Inventory inventory)
    {
        int free = inventory.GetWidth() * rows;
        foreach (var item in inventory.GetAllItems())
            if (item.m_gridPos.y < rows) free--;
        return free;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    private static class Spawned
    {
        private static void Prefix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            spawning = true;
            spawned = __instance;
            active = null;
            recoveryBlocked = false;
        }

        private static void Finalizer(Player __instance)
        {
            if (__instance == spawned) spawning = false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetInventorySize))]
    private static class ResizeBackpack
    {
        private static bool Prefix(Player __instance, int rows)
        {
            if (!enabledInWorld || __instance != Player.m_localPlayer || !InventoryGui.instance) return true;
            var inventory = __instance.GetInventory();
            if (inventory.GetWidth() < SlotCount) return true;
            if (spawning && TryReadBackpackRows(__instance, out int saved)) rows = saved;
            else if (ReservedRow(inventory) < 0) return true;
            Resize(__instance, inventory, Mathf.Clamp(rows, 1, MaxBackpackRows));
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Save), typeof(ZPackage))]
    private static class SaveBackpack
    {
        private static void Prefix(Player __instance, out List<(Item Item, Vector2i Position)> __state)
        {
            __state = null;
            if (__instance != Player.m_localPlayer) return;
            var inventory = __instance.GetInventory();
            int reserved = ReservedRow(inventory);
            if (reserved < 0) return;
            // Save into ordinary free cells whenever possible. When the DLL is
            // removed, Valheim drops only the overflow on its next spawn.
            string quick = "";
            foreach (var item in inventory.GetAllItems())
            {
                if (item.m_gridPos.y < reserved) continue;
                var free = FindBackpackSlot(inventory, reserved, false);
                if (free.x < 0) break;
                if (item.m_gridPos.y == reserved + 1)
                    quick += (quick.Length > 0 ? "," : "") + $"{item.m_gridPos.x}:{free.x}:{free.y}";
                (__state ??= new()).Add((item, item.m_gridPos));
                item.m_gridPos = free;
            }
            if (quick.Length > 0) __instance.AddUniqueKeyValue(QuickSlotsKey, quick);
            else __instance.RemoveUniqueKeyValue(QuickSlotsKey);
        }

        private static void Finalizer(List<(Item Item, Vector2i Position)> __state)
        {
            if (__state == null) return;
            foreach (var (item, position) in __state) item.m_gridPos = position;
        }
    }

    [HarmonyPatch(typeof(Inventory), "FindEmptySlot")]
    private static class FreeCell
    {
        private static bool Prefix(Inventory __instance, bool topFirst, ref Vector2i __result)
        {
            if (ReservedRow(__instance) < 0) return true;
            __result = FindBackpackSlot(__instance, rows, topFirst);
            if (__result.x < 0 && upgrading != null && __instance.GetItemAt(upgrading.m_gridPos.x, rows) == null)
                __result = upgrading.m_gridPos;
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.GetEmptySlots))]
    private static class FreeCells
    {
        private static void Postfix(Inventory __instance, ref int __result)
        {
            if (ReservedRow(__instance) >= 0) __result = BackpackSpace(__instance);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveEmptySlot))]
    private static class HasFreeCell
    {
        private static void Postfix(Inventory __instance, ref bool __result)
        {
            if (ReservedRow(__instance) >= 0) __result = BackpackSpace(__instance) > 0;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CanAddItem), typeof(Item), typeof(int))]
    private static class CanAdd
    {
        private static bool Prefix(Inventory __instance, Item item, int stack, ref bool __result)
        {
            if (ReservedRow(__instance) < 0) return true;
            int capacity = __instance.FindFreeStackSpace(item.m_shared.m_name, item.m_worldLevel)
                + BackpackSpace(__instance) * item.m_shared.m_maxStackSize;
            __result = capacity >= (stack > 0 ? stack : item.m_stack);
            return false;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(Item), typeof(int), typeof(int), typeof(int), typeof(bool))]
    private static class AddAtCell
    {
        // Take-all and other native bulk moves can also request explicit coordinates.
        private static bool Prefix(Inventory __instance, int y) =>
            y < 0 || ReservedRow(__instance) != y && (QuickRow(__instance) != y || dropping);
    }

    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    private static class UpgradeEquipment
    {
        private static void Prefix(Item ___m_craftUpgradeItem)
        {
            if (active != null && ___m_craftUpgradeItem?.m_gridPos.y == rows) upgrading = ___m_craftUpgradeItem;
        }

        private static void Finalizer() => upgrading = null;
    }

    [HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
    private static class UseGear
    {
        private static bool Prefix(InventoryGrid grid, Item item, Vector2i pos)
        {
            var inventory = grid.GetInventory();
            if (ReservedRow(inventory) != pos.y || pos.y < 0 || item?.m_equipped != true
                || FindBackpackSlot(inventory, rows, true).x >= 0) return true;
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$inventory_full");
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.GetGamepadSelectedElement))]
    private static class KeepEquipmentSelectionVisible
    {
        private static void Prefix(InventoryGrid __instance, ref Vector2i ___m_selected)
        {
            int reserved = ReservedRow(__instance.GetInventory());
            if (reserved >= 0 && ___m_selected.y == reserved && ___m_selected.x >= SlotCount)
                ___m_selected.x = SlotCount - 1;
            if (reserved >= 0 && ___m_selected.y == reserved + 1 && ___m_selected.x >= QuickSlotCount)
                ___m_selected.x = QuickSlotCount - 1;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    private static class MoveGear
    {
        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, Item item, Vector2i pos)
        {
            var inventory = grid.GetInventory();
            bool gearCell = pos.y >= 0 && ReservedRow(inventory) == pos.y;
            if (gearCell && (pos.x < 0 || pos.x >= SlotCount)) return false;
            bool quickCell = pos.y >= 0 && QuickRow(inventory) == pos.y;
            if (quickCell && (pos.x < 0 || pos.x >= QuickSlotCount)) return false;
            // Quick slots accept any item through the native drop and swap rules.
            dropping = quickCell;

            var dragged = DragItem(__instance);
            if (dragged == null || active == null) return true;
            var player = Player.m_localPlayer;
            if (!player || player.IsTeleporting()) return false;

            var source = DragInventory(__instance);
            if (gearCell)
            {
                if (source == null || !source.ContainsItem(dragged) || SlotTypes[pos.x] != dragged.m_shared.m_itemType)
                    return false;
                if (source != active && !dragged.m_shared.m_questItem && active.HaveEmptySlot())
                    active.MoveItemToThis(source, dragged);
                if (!active.ContainsItem(dragged)) return false;
                if (!dragged.m_equipped && !player.IsEquipActionQueued(dragged))
                    player.UseItem(active, dragged, fromInventoryGui: true);
                SetupDrag(__instance, null, null, 1);
                return false;
            }

            if (source != active || dragged.m_gridPos.y != rows || !source.ContainsItem(dragged)) return true;
            // A stock swap would move the target into the reserved cell.
            if (item != null) return false;
            if (inventory != active) return true;

            player.RemoveEquipAction(dragged);
            player.UnequipItem(dragged);
            dragged.m_gridPos = pos;
            SetupDrag(__instance, null, null, 1);
            return false;
        }

        private static void Finalizer() => dropping = false;
    }
}
