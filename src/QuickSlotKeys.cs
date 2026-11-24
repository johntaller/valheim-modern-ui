using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Valheim.SettingsGui;

namespace ValheimNewUI;

// Rebindable keyboard actions for the three quick slots beside the hotbar. They are
// ordinary ZInput buttons, so Valheim saves, loads and rebinds them with the rest.
internal static class QuickSlotKeys
{
    internal static readonly string[] Names = { "NewUI_QuickSlot1", "NewUI_QuickSlot2", "NewUI_QuickSlot3" };
    // UnityEngine.InputSystem.Key values for Z, X and C.
    private static readonly int[] DefaultKeys = { 40, 38, 17 };
    private const string DefaultsAppliedKey = "newui_quickslot_keys_v2";

    private static readonly AccessTools.FieldRef<ZInput, Dictionary<string, ZInput.ButtonDef>> Buttons =
        AccessTools.FieldRefAccess<ZInput, Dictionary<string, ZInput.ButtonDef>>("m_buttons");
    private static readonly MethodInfo AddButton = AccessTools.Method(typeof(ZInput), "AddButton",
        new[] { typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(float), typeof(float) });
    private static readonly MethodInfo KeyToPath = AccessTools.Method(typeof(ZInput), "KeyToPath");
    private static readonly Func<Character, bool> TakeInput =
        AccessTools.MethodDelegate<Func<Character, bool>>(AccessTools.Method(typeof(Character), "TakeInput"));
    private static readonly AccessTools.FieldRef<KeyboardMouseSettings, List<KeySetting>> KeyList =
        AccessTools.FieldRefAccess<KeyboardMouseSettings, List<KeySetting>>("m_keys");

    private static string rebinding;
    private static bool defaultsChecked;

    internal static bool IsQuickSlot(string name) => Array.IndexOf(Names, name) >= 0;

    internal static string Label(int slot)
    {
        if (ZInput.instance == null || Localization.instance == null) return "";
        string bound = Localization.instance.GetBoundKeyString(Names[slot], true);
        return string.IsNullOrWhiteSpace(bound) ? "" : bound;
    }

    internal static string Title(int slot) =>
        MenuStyle.Translate("Quick slot ", "Быстрый слот ") + (slot + 1);

    private static string PathOf(ZInput.ButtonDef button)
    {
        try
        {
            return button?.GetActionPath(true) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    // The most recent binding keeps its key. Others that share it are cleared.
    private static void Resolve(string winner)
    {
        var input = ZInput.instance;
        if (input == null || string.IsNullOrEmpty(winner)) return;
        var buttons = Buttons(input);
        if (!buttons.TryGetValue(winner, out var bound)) return;
        string path = PathOf(bound);
        if (path.Length == 0) return;
        bool quick = IsQuickSlot(winner);
        foreach (var pair in buttons)
        {
            var other = pair.Value;
            if (pair.Key == winner || !other.Rebindable) continue;
            // A quick slot clears native actions and the other quick slots. A native
            // action clears only quick slots, keeping Valheim's own shared defaults.
            if (!quick && !IsQuickSlot(pair.Key)) continue;
            if (!string.Equals(PathOf(other), path, StringComparison.OrdinalIgnoreCase)) continue;
            other.Rebind("");
            Debug.Log($"[Valheim New UI] Cleared {pair.Key}: its key now belongs to {winner}.");
        }
    }

    internal static void Update(Player player)
    {
        if (!player || player != Player.m_localPlayer || !TakeInput(player) || player.InPlaceMode()) return;
        var inventory = player.GetInventory();
        for (int i = 0; i < Names.Length; i++)
        {
            if (!ZInput.GetButtonDown(Names[i])) continue;
            var item = EquipmentInventory.QuickItem(inventory, i);
            if (item != null) player.UseItem(null, item, false);
        }
    }

    [HarmonyPatch(typeof(Player), "Update")]
    private static class UseQuickSlots
    {
        private static void Postfix(Player __instance) => Update(__instance);
    }

    [HarmonyPatch(typeof(KeyboardMouseSettings), nameof(KeyboardMouseSettings.ResetBindings))]
    private static class ResolveDefaults
    {
        private static void Postfix()
        {
            foreach (var name in Names) Resolve(name);
        }
    }

    [HarmonyPatch(typeof(ZInput), "ResetKBMButtons")]
    private static class Register
    {
        private static void Postfix(ZInput __instance)
        {
            var buttons = Buttons(__instance);
            for (int i = 0; i < Names.Length; i++)
            {
                if (buttons.ContainsKey(Names[i])) continue;
                var key = Enum.ToObject(KeyToPath.GetParameters()[0].ParameterType, DefaultKeys[i]);
                string path = (string)KeyToPath.Invoke(null, new[] { key });
                AddButton.Invoke(__instance, new object[] { Names[i], path, false, true, true, 0f, 0f });
                // ZInput.Load may already have run, so apply a saved binding here as well.
                string saved = "kbmBinding_" + Names[i];
                if (PlatformPrefs.HasKey(saved) && buttons.TryGetValue(Names[i], out var button))
                    button.Rebind(PlatformPrefs.GetString(saved, ""));
            }
        }
    }

    // Z, X and C are taken from native actions once. Later changes are the player's.
    internal static void EnsureDefaults()
    {
        if (defaultsChecked) return;
        var input = ZInput.instance;
        if (input == null || !Buttons(input).ContainsKey(Names[0])) return;
        defaultsChecked = true;
        if (PlatformPrefs.HasKey(DefaultsAppliedKey)) return;
        foreach (var name in Names) Resolve(name);
        input.Save();
        PlatformPrefs.SetInt(DefaultsAppliedKey, 1);
    }

    [HarmonyPatch(typeof(KeyboardMouseSettings), "OpenBindDialog")]
    private static class RememberRebind
    {
        private static void Prefix(KeySetting key) => rebinding = key?.m_keyName;
    }

    [HarmonyPatch(typeof(KeyboardMouseSettings), "UpdateBindings")]
    private static class ResolveRebind
    {
        private static void Prefix()
        {
            if (ZInput.s_IsRebindActive || rebinding == null) return;
            Resolve(rebinding);
            rebinding = null;
        }
    }

    [HarmonyPatch(typeof(KeyboardMouseSettings), nameof(KeyboardMouseSettings.Initialize))]
    private static class AddRows
    {
        private static void Prefix(KeyboardMouseSettings __instance)
        {
            var keys = KeyList(__instance);
            if (keys == null || keys.Count == 0 || keys.Exists(key => IsQuickSlot(key.m_keyName))) return;
            // Some rows, such as camera zoom, stay hidden. Copy the last visible one.
            var template = keys.FindLast(key => key.m_keyTransform && key.m_keyTransform.gameObject.activeSelf);
            if (template == null) return;
            int sibling = template.m_keyTransform.GetSiblingIndex();
            for (int i = 0; i < Names.Length; i++)
            {
                var row = UnityEngine.Object.Instantiate(template.m_keyTransform, template.m_keyTransform.parent, false);
                row.name = "NewUI_" + Names[i].Substring("NewUI_".Length);
                row.SetSiblingIndex(++sibling);
                row.gameObject.SetActive(true);
                // A localization component would put back the copied action's caption.
                foreach (var behaviour in row.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour.GetType().Name == "Localize") UnityEngine.Object.DestroyImmediate(behaviour);
                var button = row.GetComponentInChildren<UnityEngine.UI.Button>(true);
                foreach (var text in row.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    if (!button || !text.transform.IsChildOf(button.transform)) text.text = Title(i);
                keys.Add(new KeySetting
                {
                    m_keyName = Names[i],
                    m_keyTransform = row,
                    m_blockedButtons = template.m_blockedButtons
                });
            }
        }
    }
}
