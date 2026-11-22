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
    private bool? previous;

    internal AutoPickupToggle(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        var face = ItemUi.Image(parent, "AutoPickup", art["slot"]);
        rect = face.rectTransform;
        face.raycastTarget = true;
        button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.interactable = AutoPickup != null;
        button.onClick.AddListener(Toggle);
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;

        track = ItemUi.Image(rect, "SwitchTrack", art["meter-fill"]);
        knob = ItemUi.Image(track.transform, "SwitchKnob", art["meter-fill"]);
        knob.color = ItemUi.Ink;
        ItemUi.TopLeft(track.rectTransform, 8, 9, 26, 13);

        var label = ItemUi.Text(rect, "Label", font, 14, ItemUi.Ink);
        label.text = Localization.instance.Localize("$hud_autopickup");
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.overflowMode = TextOverflowModes.Ellipsis;
        ItemUi.Stretch(label.rectTransform, new Vector2(42, 0), new Vector2(-34, 0));
        var badge = ItemUi.Image(rect, "BindingBackground", art["button"]);
        badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(1, 0.5f);
        badge.rectTransform.pivot = new Vector2(1, 0.5f);
        badge.rectTransform.anchoredPosition = new Vector2(-7, 0);
        badge.rectTransform.sizeDelta = new Vector2(25, 21);
        key = ItemUi.Text(badge.transform, "Binding", font, 13, ItemUi.Muted);
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
            ItemUi.TopLeft(knob.rectTransform, enabled ? 14 : 2, 2, 9, 9);
        }
        string binding = ZInput.instance?.GetBoundKeyString("AutoPickup", emptyStringOnMissing: true);
        bool bound = !string.IsNullOrEmpty(binding);
        if (key.transform.parent.gameObject.activeSelf != bound) key.transform.parent.gameObject.SetActive(bound);
        if (bound) binding = Localization.instance.Localize(binding);
        if (key.text != binding) key.text = binding;
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
