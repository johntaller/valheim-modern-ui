using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class MenuStyle : IDisposable
{
    internal static readonly Color32 Dark = new(16, 25, 28, 250);
    internal static readonly Color32 Surface = new(31, 46, 49, 255);
    internal static readonly Color32 Edge = new(74, 90, 87, 255);
    private readonly Artwork art;
    private readonly List<Action> undo = new();
    private readonly HashSet<Graphic> visited = new();
    private readonly List<Image> images = new();
    private readonly List<Selectable> controls = new();

    internal MenuStyle(Artwork art) => this.art = art;

    internal void Remember(Action restore) => undo.Add(restore);

    internal void Hide(Graphic graphic)
    {
        if (!graphic) return;
        var previous = graphic.enabled;
        var raycast = graphic.raycastTarget;
        Remember(() =>
        {
            if (!graphic) return;
            graphic.enabled = previous;
            graphic.raycastTarget = raycast;
        });
        graphic.enabled = false;
        graphic.raycastTarget = false;
    }

    internal void Hide(GameObject target)
    {
        if (!target) return;
        var hidden = new HiddenGraphics(target);
        hidden.SetRaycasts(false);
        Remember(hidden.Restore);
    }

    internal void Sprite(Image image, string role, Color tint, bool force = false)
    {
        if (!image || (!force && !visited.Add(image))) return;
        var current = image.sprite;
        var type = image.type;
        var color = image.color;
        Remember(() =>
        {
            if (!image) return;
            image.sprite = current;
            image.type = type;
            image.color = color;
        });
        image.sprite = art[role];
        image.type = image.sprite.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
        image.color = tint;
    }

    internal void Control(Selectable control)
    {
        if (!control || control.targetGraphic is not Image image || visited.Contains(image) ||
            string.Equals(image.name, "icon", StringComparison.OrdinalIgnoreCase)) return;
        if (control is Toggle && image.rectTransform.rect.width > 1.65f * image.rectTransform.rect.height) return;
        string role = control is Slider || control is Scrollbar ? "meter-fill" : control is Button ? "button" : "slot";
        Sprite(image, role, control is Scrollbar ? MutedScrollbar : Color.white);

        var transition = control.transition;
        var states = control.spriteState;
        var colors = control.colors;
        Remember(() =>
        {
            if (!control) return;
            control.transition = transition;
            control.spriteState = states;
            control.colors = colors;
        });
        if (transition == Selectable.Transition.SpriteSwap && control is Button)
        {
            control.spriteState = new SpriteState
            {
                highlightedSprite = art["selected"],
                selectedSprite = art["button"],
                pressedSprite = art["selected"],
                disabledSprite = art["slot"]
            };
        }
        else
        {
            control.transition = Selectable.Transition.ColorTint;
            control.colors = Palette;
        }
    }

    internal static readonly Color32 MutedScrollbar = new(142, 164, 155, 218);

    internal static ColorBlock Palette => new()
    {
        normalColor = Color.white,
        highlightedColor = new Color32(255, 222, 152, 255),
        selectedColor = new Color32(245, 211, 142, 255),
        pressedColor = new Color32(211, 177, 111, 255),
        disabledColor = new Color32(155, 156, 150, 200),
        colorMultiplier = 1,
        fadeDuration = 0.08f
    };

    internal void Scan(Transform root, Transform excluded = null)
    {
        root.GetComponentsInChildren(false, controls);
        foreach (var control in controls)
            if (!excluded || !control.transform.IsChildOf(excluded)) Control(control);
        root.GetComponentsInChildren(false, images);
        foreach (var image in images)
        {
            if (!image || !image.sprite || excluded && image.transform.IsChildOf(excluded) ||
                visited.Contains(image) || image.sprite.name.StartsWith("ValheimNewUI/")) continue;
            string source = image.sprite.name.ToLowerInvariant();
            string role = source switch
            {
                "checkbox" or "panel_bkg" or "crafting_panel_bkg" or "text_field" or "sunken" or "item_background" => "slot",
                "selection_frame" or "woodpanel_mask" => "selected",
                "button" or "button_tab" or "button_small" => "button",
                _ when source.StartsWith("woodpanel") => "panel",
                _ when source.StartsWith("bar_") => "meter-fill",
                _ => null
            };
            if (role != null) Sprite(image, role, new Color(1, 1, 1, image.color.a));
        }
        if (visited.Count > 450)
        {
            // The owner releases the original style when its scene is closed.
            visited.RemoveWhere(graphic => !graphic);
        }
    }

    internal static void Ink(TMP_Text text, Color color, float size = 0)
    {
        if (!text) return;
        text.color = color;
        if (size > 0) text.fontSize = size;
    }

    internal void Text(TMP_Text text, Color color, float size = 0)
    {
        if (!text) return;
        var previous = text.color;
        var fontSize = text.fontSize;
        Remember(() =>
        {
            if (!text) return;
            text.color = previous;
            text.fontSize = fontSize;
        });
        Ink(text, color, size);
    }

    internal void Dispose()
    {
        for (int i = undo.Count - 1; i >= 0; i--) undo[i]();
        undo.Clear();
        visited.Clear();
    }

    void IDisposable.Dispose() => Dispose();

    internal static TextMeshProUGUI Label(Transform parent, string name, TMP_FontAsset font, string value, float size,
        Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var label = ItemUi.Text(parent, name, font, size, color);
        label.text = value;
        label.alignment = alignment;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    internal static string Translate(string english, string russian) =>
        Localization.instance?.GetSelectedLanguage() == "Russian" ? russian : english;

    internal static Button Button(Transform parent, Artwork art, TMP_FontAsset font, string title, Action action,
        out TextMeshProUGUI label, float size = 15)
    {
        var image = ItemUi.Image(parent, title, art["button"]);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = Palette;
        button.onClick.AddListener(() => action());
        label = Label(button.transform, "Label", font, title, size, ItemUi.Ink, TextAlignmentOptions.Center);
        label.enableAutoSizing = true;
        label.fontSizeMin = 11;
        label.fontSizeMax = size;
        ItemUi.Stretch(label.rectTransform, new Vector2(4, 0), new Vector2(-4, 0));
        return button;
    }
}

internal readonly struct MenuRect
{
    private readonly RectTransform rect;
    private readonly Transform parent;
    private readonly int index;
    private readonly Vector2 min, max, pivot, size;
    private readonly Vector3 position, scale;
    private readonly Quaternion rotation;

    internal MenuRect(RectTransform rect)
    {
        this.rect = rect;
        parent = rect.parent;
        index = rect.GetSiblingIndex();
        min = rect.anchorMin;
        max = rect.anchorMax;
        pivot = rect.pivot;
        size = rect.sizeDelta;
        position = rect.anchoredPosition3D;
        scale = rect.localScale;
        rotation = rect.localRotation;
    }

    internal void ApplyTo(RectTransform target)
    {
        target.anchorMin = min;
        target.anchorMax = max;
        target.pivot = pivot;
        target.sizeDelta = size;
        target.anchoredPosition3D = position;
        target.localScale = scale;
        target.localRotation = rotation;
    }

    internal void Restore()
    {
        if (!rect || !parent) return;
        if (rect.parent != parent) rect.SetParent(parent, false);
        rect.SetSiblingIndex(index);
        ApplyTo(rect);
    }
}
