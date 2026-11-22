using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace ValheimNewUI;

internal sealed class AutoPickupToggle
{
    private static readonly FieldInfo AutoPickup = typeof(Player).GetField("m_enableAutoPickup",
        BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly Color32 On = new(110, 181, 135, 255);
    private static readonly Color32 Off = new(104, 104, 99, 255);
    private readonly RectTransform rect;
    private readonly Button button;
    private readonly Image track, knob;
    private readonly TextMeshProUGUI key;
    private readonly RectTransform badge;
    private bool? previous;

    internal AutoPickupToggle(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        rect = ItemUi.Rect(parent, "AutoPickup");
        track = ItemUi.Image(rect, "SwitchTrack", art["meter-fill"]);
        track.raycastTarget = true;
        ItemUi.TopLeft(track.rectTransform, 0, 8, 30, 15);
        button = track.gameObject.AddComponent<Button>();
        button.targetGraphic = track;
        button.interactable = AutoPickup != null;
        button.onClick.AddListener(Toggle);
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        knob = ItemUi.Image(track.transform, "SwitchKnob", art["meter-fill"]);
        knob.color = ItemUi.Ink;

        var label = ItemUi.Text(rect, "Label", font, 14, ItemUi.Ink);
        label.text = Localization.instance.Localize("$hud_autopickup");
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;
        const float labelLeft = 39;
        float labelWidth = Mathf.Ceil(label.GetPreferredValues(label.text).x) + 2;
        ItemUi.TopLeft(label.rectTransform, labelLeft, 0, labelWidth, 31);
        badge = ItemUi.Image(rect, "BindingBackground", art["button"]).rectTransform;
        ItemUi.TopLeft(badge, labelLeft + labelWidth + 7, 5, 25, 21);
        key = ItemUi.Text(badge, "Binding", font, 13, ItemUi.Muted);
        key.alignment = TextAlignmentOptions.Center;
        ItemUi.Stretch(key.rectTransform, Vector2.zero, Vector2.zero);
        SetVisible(false);
    }

    internal void Align(float left, float top, float width) => ItemUi.TopLeft(rect, left, top, width, 31);
    internal void SetVisible(bool visible)
    {
        if (rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
    }

    internal void Dispose() => ItemUi.Destroy(rect);

    internal void Refresh()
    {
        bool enabled = AutoPickup?.GetValue(null) is true;
        if (previous != enabled)
        {
            previous = enabled;
            track.color = enabled ? On : Off;
            ItemUi.TopLeft(knob.rectTransform, enabled ? 17 : 2, 2, 11, 11);
        }
        string binding = ZInput.instance?.GetBoundKeyString("AutoPickup", emptyStringOnMissing: true);
        bool bound = !string.IsNullOrEmpty(binding);
        if (badge.gameObject.activeSelf != bound) badge.gameObject.SetActive(bound);
        if (bound) binding = Localization.instance.Localize(binding);
        if (key.text != binding)
        {
            key.text = binding;
            badge.sizeDelta = new Vector2(Mathf.Max(25, key.GetPreferredValues(binding ?? "").x + 12), 21);
        }
    }

    private void Toggle()
    {
        if (!Player.m_localPlayer || AutoPickup?.GetValue(null) is not bool enabled) return;
        AutoPickup.SetValue(null, !enabled);
        Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
            "$hud_autopickup:" + (enabled ? "$hud_off" : "$hud_on"));
        Refresh();
    }
}
