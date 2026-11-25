using HarmonyLib;

namespace ValheimNewUI;

// Valheim forgets which marker types were hidden on the large map (right-click on a legend icon)
// whenever the map is rebuilt. Keep the choice in the character's profile and apply it on spawn.
internal static class MapFilterMemory
{
    private const string Key = "valheimnewui_hidden_pins";
    private static readonly AccessTools.FieldRef<Minimap, bool[]> Visible =
        AccessTools.FieldRefAccess<Minimap, bool[]>("m_visibleIconTypes");
    private static readonly System.Action<Minimap, Minimap.PinType> Toggle =
        AccessTools.MethodDelegate<System.Action<Minimap, Minimap.PinType>>(AccessTools.Method(typeof(Minimap), "ToggleIconFilter"));
    private static bool restoring;

    [HarmonyPatch(typeof(Minimap), "ToggleIconFilter")]
    private static class Remember
    {
        private static void Postfix(Minimap __instance)
        {
            var player = Player.m_localPlayer;
            var visible = Visible(__instance);
            if (restoring || !player || visible == null) return;
            var hidden = new System.Collections.Generic.List<string>();
            for (int i = 0; i < visible.Length; i++)
                if (!visible[i]) hidden.Add(i.ToString());
            if (hidden.Count > 0) player.AddUniqueKeyValue(Key, string.Join(",", hidden));
            else player.RemoveUniqueKeyValue(Key);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    private static class Restore
    {
        private static void Postfix(Player __instance)
        {
            var map = Minimap.instance;
            var visible = map ? Visible(map) : null;
            if (__instance != Player.m_localPlayer || visible == null) return;
            var hidden = new bool[visible.Length];
            if (__instance.TryGetUniqueKeyValue(Key, out var stored))
                foreach (var part in stored.Split(','))
                    if (int.TryParse(part, out int type) && type >= 0 && type < hidden.Length) hidden[type] = true;
            restoring = true;
            try
            {
                // The native toggle also greys the legend icon and refreshes the pins.
                for (int i = 0; i < visible.Length; i++)
                    if (visible[i] == hidden[i]) Toggle(map, (Minimap.PinType)i);
            }
            finally { restoring = false; }
        }
    }
}
