using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimNewUI;

// macOS reports trackpad and wheel scrolling as many fine deltas, while a Windows wheel notch
// arrives as one tick. Keep the tuned macOS distance and give every wheel tick elsewhere the
// same travel: a few rows per notch.
internal sealed class WheelScrollRect : ScrollRect
{
    // Pixels per wheel notch away from macOS.
    internal float TickDistance = 120;

    public override void OnScroll(PointerEventData data)
    {
        if (Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor)
        {
            base.OnScroll(data);
            return;
        }
        float sensitivity = scrollSensitivity;
        // The Input System UI module reports each notch as scrollDeltaPerTick units; the legacy module as 1.
        scrollSensitivity = TickDistance / TickSize();
        try { base.OnScroll(data); }
        finally { scrollSensitivity = sensitivity; }
    }

    private static float TickSize()
    {
        var module = EventSystem.current ? EventSystem.current.currentInputModule : null;
        if (!module) return 1;
        var property = HarmonyLib.AccessTools.Property(module.GetType(), "scrollDeltaPerTick");
        return property?.GetValue(module) is float tick && tick > 0 ? tick : 1;
    }
}
