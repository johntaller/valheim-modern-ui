using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimNewUI;

// Holding Shift on the repair button repairs every worn item the current station
// can repair, with one effect and one summary message.
internal static class CraftingRepair
{
    private static readonly Func<InventoryGui, ItemDrop.ItemData, bool> CanRepair =
        AccessTools.MethodDelegate<Func<InventoryGui, ItemDrop.ItemData, bool>>(AccessTools.Method(typeof(InventoryGui), "CanRepair"));
    private static readonly Action<InventoryGui> UpdateRepair =
        AccessTools.MethodDelegate<Action<InventoryGui>>(AccessTools.Method(typeof(InventoryGui), "UpdateRepair"));
    private static readonly Action<InventoryGui, bool> UpdateCraftingPanel =
        AccessTools.MethodDelegate<Action<InventoryGui, bool>>(AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));
    private static readonly Action<InventoryGui, UIGroupHandler, bool> SetActiveGroup =
        AccessTools.MethodDelegate<Action<InventoryGui, UIGroupHandler, bool>>(
            AccessTools.Method(typeof(InventoryGui), "SetActiveGroup", new[] { typeof(UIGroupHandler), typeof(bool) }));
    private static readonly List<ItemDrop.ItemData> worn = new();

    internal static bool ShiftHeld =>
        ZInput.GetKey(KeyCode.LeftShift, true) || ZInput.GetKey(KeyCode.RightShift, true);

    internal static void RepairAll(InventoryGui gui)
    {
        var player = Player.m_localPlayer;
        if (!player || !gui) return;
        var station = player.GetCurrentCraftingStation();
        if (!station && !player.NoCostCheat() || station && !station.CheckUsable(player, false)) return;

        worn.Clear();
        player.GetInventory().GetWornItems(worn);
        ItemDrop.ItemData last = null;
        int repaired = 0;
        foreach (var item in worn)
        {
            if (!CanRepair(gui, item)) continue;
            // Match the skill gain and the full durability of a native single repair.
            player.RaiseSkill(Skills.SkillType.Crafting, 1 - item.m_durability / item.GetMaxDurability());
            item.m_durability = item.GetMaxDurability();
            last = item;
            repaired++;
        }
        worn.Clear();

        if (repaired == 0)
        {
            player.Message(MessageHud.MessageType.Center, MenuStyle.Translate("Nothing to repair", "Нечего ремонтировать"));
            return;
        }
        if (station)
            station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
        string message = repaired == 1
            ? Localization.instance.Localize("$msg_repaired", last.m_shared.m_name)
            : MenuStyle.Translate($"Repaired items: {repaired}", $"Отремонтировано предметов: {repaired}");
        player.Message(MessageHud.MessageType.Center, message);
    }

    [HarmonyPatch(typeof(InventoryGui), "OnRepairPressed")]
    private static class RepairEverything
    {
        private static bool Prefix(InventoryGui __instance)
        {
            if (!NativeMessagePatch.Enabled || !ShiftHeld) return true;
            if (__instance.m_uiGroups.Length > 3) SetActiveGroup(__instance, __instance.m_uiGroups[3], true);
            RepairAll(__instance);
            UpdateRepair(__instance);
            UpdateCraftingPanel(__instance, false);
            return false;
        }
    }
}
