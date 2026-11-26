using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimNewUI;

// Rebuilds one settings tab as a scrolling list of rows: the setting name on the left and the game's own control
// on the right. The native controls keep their components and listeners, so the game reads and saves them as usual.
internal sealed class SettingsPageView
{
    private enum Kind { Toggle, Slider, Dropdown, Stepper, Key, Action, Block }

    private const float RowHeight = 46;
    private const float NoteRowHeight = 60;
    private const float Inset = 14;
    private const float ToggleWidth = 48;
    private const float SliderWidth = 190;
    private const float ValueWidth = 124;
    private const float FieldWidth = 240;
    private const float KeyWidth = 140;
    private static readonly Color Hover = new(1, 1, 1, 0.05f);
    private static readonly Color Focus = new Color32(225, 187, 116, 30);
    private static readonly Color32 Trough = new(8, 13, 15, 235);
    private static readonly Color32 SwitchOff = new(43, 58, 60, 255);
    private static readonly Color32 RuleColor = new(74, 90, 87, 90);

    // Native containers that start a new section of a tab.
    private static readonly Dictionary<string, (string English, string Russian)> Sections = new()
    {
        ["Graphics"] = ("Display", "Экран"),
        ["GraphicsMode"] = ("Quality", "Качество"),
        ["QualityToggles"] = ("Effects", "Эффекты"),
        ["Bindings"] = ("Key bindings", "Назначение клавиш"),
        ["GamepadMap"] = ("Controller layout", "Схема контроллера"),
    };

    private sealed class Item
    {
        internal Kind Kind;
        internal RectTransform Control;
        internal Selectable Selectable;
        internal TMP_Text Label, Note, Value;
        internal string Section;
        internal readonly List<GameObject> Ancestry = new();
    }

    private sealed class Row
    {
        internal RectTransform Rect;
        internal Image Background, Marker;
        internal Item Item;
        internal Toggle Toggle;
        internal Image Track, Knob;
        internal float Position = -1;
        internal bool Hovered;
        internal List<Graphic> Lines;
    }

    private readonly RectTransform page, host, root, viewport, content;
    private readonly MenuStyle style;
    private readonly Artwork art;
    private readonly TMP_FontAsset sans;
    private readonly ScrollRect scroll;
    private readonly List<Row> rows = new();
    private readonly List<GridLayoutGroup> keyGrids = new();
    private readonly List<(LayoutElement Row, RectTransform Block, Vector2 Size)> blocks = new();
    private readonly List<RectTransform> actionBars = new();
    private RectTransform actionHeader;
    private GameObject lastSelected;
    private float lastWidth;
    private bool built;

    internal Button Back { get; private set; }
    internal RectTransform Page => page;

    internal SettingsPageView(RectTransform page, RectTransform host, MenuStyle style, Artwork art, TMP_FontAsset sans)
    {
        this.page = page;
        this.host = host;
        this.style = style;
        this.art = art;
        this.sans = sans;

        root = ItemUi.Rect(host, "NewUI_" + page.name);
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);
        viewport = ItemUi.Rect(root, "Viewport");
        ItemUi.Stretch(viewport, Vector2.zero, new Vector2(-16, 0));
        viewport.gameObject.AddComponent<RectMask2D>();
        var wheel = ItemUi.Image(viewport, "ScrollInput");
        wheel.color = Color.clear;
        wheel.raycastTarget = true;
        ItemUi.Stretch(wheel.rectTransform, Vector2.zero, Vector2.zero);
        content = ItemUi.Rect(viewport, "Rows");
        content.anchorMin = Vector2.up;
        content.anchorMax = Vector2.one;
        content.pivot = Vector2.up;
        content.sizeDelta = Vector2.zero;
        var list = content.gameObject.AddComponent<VerticalLayoutGroup>();
        list.childControlWidth = list.childControlHeight = true;
        list.childForceExpandWidth = true;
        list.childForceExpandHeight = false;
        list.spacing = 0;
        list.padding = new RectOffset(0, 0, 2, 14);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = root.gameObject.AddComponent<WheelScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40;
        var bar = HudStyle.Scrollbar(root, art, "Scrollbar");
        var barRect = (RectTransform)bar.transform;
        barRect.anchorMin = Vector2.right;
        barRect.anchorMax = Vector2.one;
        barRect.pivot = Vector2.one;
        barRect.offsetMin = new Vector2(-7, 4);
        barRect.offsetMax = new Vector2(0, -4);
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        root.gameObject.SetActive(false);

        // The page stays above the rows so its own dialogs (key binding, resolution confirmation) cover them.
        Move(page, host);
        ItemUi.Stretch(page, Vector2.zero, Vector2.zero);
        page.SetAsLastSibling();
    }

    internal void Refresh(bool selected)
    {
        bool active = selected && page && page.gameObject.activeInHierarchy;
        if (root.gameObject.activeSelf != active) root.gameObject.SetActive(active);
        if (!active) return;
        if (!built)
        {
            built = true;
            Build();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            // Setting the normalized position would force a canvas update, which runs inside canvas preparation here.
            content.anchoredPosition = Vector2.zero;
        }

        float width = viewport.rect.width;
        if (Mathf.Abs(width - lastWidth) > 0.5f)
        {
            lastWidth = width;
            Resize(width);
        }

        var focus = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        foreach (var row in rows) Sync(row, focus);
        // The game hides some actions, such as account deletion on platforms without it; an empty section goes too.
        bool anyAction = false;
        foreach (var bar in actionBars)
        {
            bool used = false;
            foreach (Transform holder in bar) used |= holder.gameObject.activeSelf;
            if (bar.gameObject.activeSelf != used) bar.gameObject.SetActive(used);
            anyAction |= used;
        }
        if (actionHeader && actionHeader.gameObject.activeSelf != anyAction) actionHeader.gameObject.SetActive(anyAction);
        if (focus != lastSelected)
        {
            lastSelected = focus;
            if (focus && focus.transform.IsChildOf(content)) Reveal((RectTransform)focus.transform);
        }
    }

    private void Build()
    {
        var items = new List<Item>();
        Collect(page, Sections.ContainsKey(page.name) ? page.name : null, items);

        string section = null;
        var shown = new HashSet<string>();
        var actions = new List<Item>();
        GridLayoutGroup grid = null;
        foreach (var item in items)
        {
            if (item.Kind == Kind.Action)
            {
                if (item.Control.name == "Back" && item.Selectable is Button back) Back = back;
                else actions.Add(item);
                continue;
            }
            // Items after a nested section may fall back to the page's own section; that heading is not repeated.
            if (item.Section != null && item.Section != section && shown.Add(item.Section))
            {
                section = item.Section;
                var title = Sections[section];
                Header(MenuStyle.Translate(title.English, title.Russian));
                grid = null;
            }
            if (item.Kind == Kind.Key)
            {
                grid ??= KeyGrid();
                KeyCell(grid, item);
                continue;
            }
            grid = null;
            if (item.Kind == Kind.Block) Block(item);
            else Setting(item);
        }
        if (actions.Count > 0)
        {
            actionHeader = Header(MenuStyle.Translate("More", "Дополнительно"));
            ActionBars(actions);
        }
        HideLeftovers();
    }

    private void Collect(Transform node, string section, List<Item> items)
    {
        if (IsOverlay(node)) return;
        if (node != page && Sections.ContainsKey(node.name)) section = node.name;
        var kind = Classify(node, out var selectable);
        if (kind is Kind found)
        {
            var item = new Item
            {
                Kind = found,
                Control = (RectTransform)node,
                Selectable = selectable,
                Section = section
            };
            item.Label = found == Kind.Key ? FindSibling(node) : FindLabel(node, selectable as TMP_Dropdown);
            if (found == Kind.Slider) item.Value = FindText(node, "Value");
            if (found is Kind.Stepper or Kind.Dropdown) item.Note = FindNote(node, item.Label);
            for (var parent = node.parent; parent && parent != page; parent = parent.parent)
                item.Ancestry.Add(parent.gameObject);
            items.Add(item);
            return;
        }
        for (int i = 0; i < node.childCount; i++) Collect(node.GetChild(i), section, items);
    }

    private static bool IsOverlay(Transform node) =>
        node.name.Contains("Dialog") || node.name.Contains("Tooltip") || node.name == "Template";

    private Kind? Classify(Transform node, out Selectable selectable)
    {
        selectable = null;
        foreach (var behaviour in node.GetComponents<MonoBehaviour>())
        {
            if (!behaviour) continue;
            string type = behaviour.GetType().Name;
            if (type == "GuiStepper") return Kind.Stepper;
            if (type is "GamepadMapController" or "GamepadMap") return Kind.Block;
        }
        selectable = node.GetComponent<Selectable>();
        return selectable switch
        {
            Slider => Kind.Slider,
            Toggle => Kind.Toggle,
            TMP_Dropdown => Kind.Dropdown,
            Button => InBindings(node) ? Kind.Key : Kind.Action,
            _ => null
        };
    }

    private bool InBindings(Transform node)
    {
        for (var parent = node.parent; parent && parent != page; parent = parent.parent)
            if (parent.name == "Bindings") return true;
        return false;
    }

    private static bool IsLabel(Transform node) => node.name is "Label" or "LabelLeft";

    private static TMP_Text FindLabel(Transform control, TMP_Dropdown dropdown)
    {
        var caption = dropdown ? dropdown.captionText : null;
        foreach (Transform child in control)
            if (IsLabel(child) && child.TryGetComponent<TMP_Text>(out var text) && text != caption) return text;
        if (!control.parent) return null;
        foreach (Transform sibling in control.parent)
            if (sibling != control && IsLabel(sibling) && sibling.TryGetComponent<TMP_Text>(out var text)) return text;
        return null;
    }

    private static TMP_Text FindSibling(Transform control)
    {
        if (!control.parent) return null;
        foreach (Transform sibling in control.parent)
            if (sibling != control && IsLabel(sibling) && sibling.TryGetComponent<TMP_Text>(out var text)) return text;
        return null;
    }

    private static TMP_Text FindText(Transform control, string name)
    {
        foreach (var text in control.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == name) return text;
        return null;
    }

    // Steppers carry an extra line, such as the community translation notice or a preset description.
    private static TMP_Text FindNote(Transform control, TMP_Text label)
    {
        if (!control.parent) return null;
        foreach (Transform sibling in control.parent)
        {
            if (sibling == control || IsLabel(sibling) || sibling.GetComponent<Selectable>()) continue;
            if (sibling.TryGetComponent<TMP_Text>(out var text) && text != label) return text;
        }
        return null;
    }

    private RectTransform Header(string title)
    {
        var rect = ItemUi.Rect(content, "NewUI_Section");
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = rows.Count == 0 && keyGrids.Count == 0 ? 30 : 44;
        var label = MenuStyle.Label(rect, "Title", HudStyle.BrandFont(sans), title.ToUpperInvariant(), 16, ItemUi.Accent,
            TextAlignmentOptions.BottomLeft);
        label.characterSpacing = 3;
        ItemUi.Stretch(label.rectTransform, new Vector2(Inset, 6), new Vector2(-Inset, 0));
        return rect;
    }

    private Row NewRow(Transform parent, Item item, float height)
    {
        var rect = ItemUi.Rect(parent, "NewUI_Row");
        if (height > 0)
        {
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
        }
        var row = new Row { Rect = rect, Item = item, Toggle = item?.Selectable as Toggle };
        row.Background = ItemUi.Image(rect, "Hover");
        row.Background.color = Color.clear;
        row.Background.raycastTarget = true;
        ItemUi.Stretch(row.Background.rectTransform, Vector2.zero, Vector2.zero);
        // On the row itself, so hovering the control keeps the highlight and clicks on the name bubble up here.
        var pointer = rect.gameObject.AddComponent<SettingsRowPointer>();
        pointer.Hover = value => row.Hovered = value;
        pointer.Click = () =>
        {
            // The whole row toggles, not only the small switch.
            if (row.Toggle && row.Toggle.IsInteractable()) row.Toggle.isOn = !row.Toggle.isOn;
        };
        row.Marker = ItemUi.Image(rect, "Marker", art["pill"]);
        row.Marker.color = ItemUi.Accent;
        row.Marker.enabled = false;
        row.Marker.rectTransform.anchorMin = new Vector2(0, 0.5f);
        row.Marker.rectTransform.anchorMax = new Vector2(0, 0.5f);
        row.Marker.rectTransform.pivot = new Vector2(0, 0.5f);
        row.Marker.rectTransform.sizeDelta = new Vector2(3, 24);
        row.Marker.rectTransform.anchoredPosition = new Vector2(2, 0);
        rows.Add(row);
        return row;
    }

    private static void Rule(RectTransform row)
    {
        var line = ItemUi.Image(row, "Rule");
        line.color = RuleColor;
        line.rectTransform.anchorMin = Vector2.zero;
        line.rectTransform.anchorMax = Vector2.right;
        line.rectTransform.pivot = new Vector2(0.5f, 0);
        line.rectTransform.offsetMin = new Vector2(Inset, 0);
        line.rectTransform.offsetMax = new Vector2(-Inset, 1);
    }

    private void Setting(Item item)
    {
        bool note = item.Note && item.Note.gameObject.activeSelf && !string.IsNullOrWhiteSpace(item.Note.text);
        var row = NewRow(content, item, note ? NoteRowHeight : RowHeight);
        Rule(row.Rect);
        float controlWidth = item.Kind switch
        {
            Kind.Toggle => ToggleWidth,
            Kind.Slider => SliderWidth + 12 + ValueWidth,
            _ => FieldWidth
        };

        var area = ItemUi.Rect(row.Rect, "Control");
        area.anchorMin = new Vector2(1, 0);
        area.anchorMax = Vector2.one;
        area.pivot = new Vector2(1, 0.5f);
        area.sizeDelta = new Vector2(controlWidth, 0);
        area.anchoredPosition = new Vector2(-Inset, 0);

        var labels = ItemUi.Rect(row.Rect, "Text");
        ItemUi.Stretch(labels, new Vector2(Inset + 4, 0), new Vector2(-(controlWidth + Inset + 18), 0));
        if (item.Label)
        {
            Move(item.Label.rectTransform, labels);
            if (note) Band(item.Label.rectTransform, 0.42f, 1, 2);
            else ItemUi.Stretch(item.Label.rectTransform, Vector2.zero, Vector2.zero);
            Restyle(item.Label, 16, ItemUi.Ink, TextAlignmentOptions.MidlineLeft, 12);
        }
        if (note)
        {
            Move(item.Note.rectTransform, labels);
            Band(item.Note.rectTransform, 0, 0.46f, -2);
            Restyle(item.Note, 12.5f, ItemUi.Muted, TextAlignmentOptions.TopLeft, 10);
        }

        Move(item.Control, area);
        switch (item.Kind)
        {
            case Kind.Toggle: Switch(row, item.Control); break;
            case Kind.Slider: SliderControl(item, area); break;
            case Kind.Dropdown: DropdownControl(item); break;
            case Kind.Stepper: StepperControl(item); break;
        }
    }

    private static void Band(RectTransform rect, float bottom, float top, float shift)
    {
        rect.anchorMin = new Vector2(0, bottom);
        rect.anchorMax = new Vector2(1, top);
        rect.offsetMin = new Vector2(0, shift);
        rect.offsetMax = new Vector2(0, shift);
    }

    private static void Pin(RectTransform rect, float x, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(x, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = Vector2.zero;
    }

    private void Switch(Row row, RectTransform control)
    {
        Pin(control, 1, ToggleWidth, 24);
        foreach (var graphic in control.GetComponentsInChildren<Graphic>(true)) style.Hide(graphic);
        row.Track = ItemUi.Image(control, "NewUI_Switch", art["pill"]);
        row.Track.raycastTarget = true;
        ItemUi.Stretch(row.Track.rectTransform, Vector2.zero, Vector2.zero);
        row.Knob = ItemUi.Image(row.Track.transform, "Knob", art["disc"]);
        row.Knob.rectTransform.anchorMin = row.Knob.rectTransform.anchorMax = new Vector2(0, 0.5f);
        row.Knob.rectTransform.sizeDelta = new Vector2(18, 18);
        style.Claim(row.Track);
        style.Claim(row.Knob);
        var track = row.Track.gameObject;
        style.Remember(() => { if (track) UnityEngine.Object.Destroy(track); });

        var toggle = row.Toggle;
        var target = toggle.targetGraphic;
        var transition = toggle.transition;
        var colors = toggle.colors;
        style.Remember(() =>
        {
            if (!toggle) return;
            toggle.targetGraphic = target;
            toggle.transition = transition;
            toggle.colors = colors;
        });
        toggle.targetGraphic = row.Track;
        toggle.transition = Selectable.Transition.ColorTint;
        toggle.colors = MenuStyle.Palette;
    }

    private void SliderControl(Item item, RectTransform area)
    {
        var slider = (Slider)item.Selectable;
        Pin(item.Control, 0, SliderWidth, 20);
        var background = item.Control.Find("Background") as RectTransform;
        if (background)
        {
            Remember(background);
            Pin(background, 0.5f, 0, 6);
            background.anchorMin = new Vector2(0, 0.5f);
            background.anchorMax = new Vector2(1, 0.5f);
            if (background.TryGetComponent<Image>(out var image)) style.Sprite(image, "pill", Trough, true);
        }
        if (slider.fillRect && slider.fillRect.parent is RectTransform fillArea && fillArea != item.Control)
        {
            Remember(fillArea);
            fillArea.anchorMin = new Vector2(0, 0.5f);
            fillArea.anchorMax = new Vector2(1, 0.5f);
            fillArea.pivot = Vector2.one * 0.5f;
            fillArea.sizeDelta = new Vector2(0, 6);
            fillArea.anchoredPosition = Vector2.zero;
            Remember(slider.fillRect);
            slider.fillRect.offsetMin = slider.fillRect.offsetMax = Vector2.zero;
            if (slider.fillRect.TryGetComponent<Image>(out var fill)) style.Sprite(fill, "pill", ItemUi.Accent, true);
        }
        if (slider.handleRect)
        {
            if (slider.handleRect.parent is RectTransform handleArea && handleArea != item.Control)
            {
                Remember(handleArea);
                ItemUi.Stretch(handleArea, new Vector2(9, 0), new Vector2(-9, 0));
            }
            Remember(slider.handleRect);
            slider.handleRect.sizeDelta = new Vector2(18, 0);
            slider.handleRect.anchoredPosition = Vector2.zero;
            if (slider.handleRect.TryGetComponent<Image>(out var handle)) style.Sprite(handle, "disc", ItemUi.Ink, true);
        }
        if (item.Value)
        {
            Move(item.Value.rectTransform, area);
            var value = item.Value.rectTransform;
            value.anchorMin = new Vector2(1, 0);
            value.anchorMax = Vector2.one;
            value.pivot = new Vector2(1, 0.5f);
            value.sizeDelta = new Vector2(ValueWidth, 0);
            value.anchoredPosition = Vector2.zero;
            Restyle(item.Value, 14, ItemUi.Muted, TextAlignmentOptions.MidlineRight, 10.5f);
        }
    }

    private void DropdownControl(Item item)
    {
        var dropdown = (TMP_Dropdown)item.Selectable;
        Pin(item.Control, 1, FieldWidth, 34);
        if (dropdown.captionText)
        {
            Remember(dropdown.captionText.rectTransform);
            ItemUi.Stretch(dropdown.captionText.rectTransform, new Vector2(14, 0), new Vector2(-30, 0));
            Restyle(dropdown.captionText, 15, ItemUi.Ink, TextAlignmentOptions.MidlineLeft, 11);
        }
        if (dropdown.template) StyleList(dropdown.template);
        // A small chevron tells a list from a button.
        var chevron = ItemUi.Rect(item.Control, "NewUI_Chevron");
        chevron.anchorMin = chevron.anchorMax = new Vector2(1, 0.5f);
        chevron.sizeDelta = new Vector2(12, 8);
        chevron.anchoredPosition = new Vector2(-17, -1);
        for (int side = -1; side <= 1; side += 2)
        {
            var stroke = ItemUi.Image(chevron, "Stroke");
            stroke.color = ItemUi.Accent;
            stroke.rectTransform.sizeDelta = new Vector2(2, 8);
            stroke.rectTransform.anchoredPosition = new Vector2(side * 2.4f, 0);
            stroke.rectTransform.localRotation = Quaternion.Euler(0, 0, -side * 45);
        }
        var node = chevron.gameObject;
        style.Remember(() => { if (node) UnityEngine.Object.Destroy(node); });
    }

    // The list is cloned from this template each time it opens, so styling the template styles every list.
    private void StyleList(RectTransform template)
    {
        foreach (var text in template.GetComponentsInChildren<TMP_Text>(true))
        {
            Restyle(text, 15, ItemUi.Ink, TextAlignmentOptions.MidlineLeft, 11);
            text.margin = new Vector4(10, 0, 6, 0);
        }
        foreach (var option in template.GetComponentsInChildren<Toggle>(true))
        {
            var colors = option.colors;
            var target = option;
            style.Remember(() => { if (target) target.colors = colors; });
            option.colors = new ColorBlock
            {
                normalColor = new Color(1, 1, 1, 0),
                highlightedColor = new Color(1, 1, 1, 0.08f),
                selectedColor = new Color(1, 1, 1, 0.08f),
                pressedColor = new Color(1, 1, 1, 0.14f),
                disabledColor = new Color(1, 1, 1, 0),
                colorMultiplier = 1,
                fadeDuration = 0.06f
            };
            if (option.targetGraphic is Image row) style.Sprite(row, "pill", Color.white, true);
            if (option.graphic is Image mark)
            {
                style.Sprite(mark, "pill", new Color32(225, 187, 116, 60), true);
                Remember(mark.rectTransform);
                ItemUi.Stretch(mark.rectTransform, Vector2.zero, Vector2.zero);
            }
        }
    }

    private void StepperControl(Item item)
    {
        Pin(item.Control, 1, FieldWidth, 34);
        foreach (Transform child in item.Control)
        {
            var rect = (RectTransform)child;
            switch (child.name)
            {
                case "Left":
                case "Right":
                    Remember(rect);
                    Pin(rect, child.name == "Left" ? 0 : 1, 34, 34);
                    foreach (var arrow in child.GetComponentsInChildren<TMP_Text>(true))
                    {
                        Remember(arrow.rectTransform);
                        ItemUi.Stretch(arrow.rectTransform, Vector2.zero, Vector2.zero);
                        Restyle(arrow, 20, ItemUi.Ink, TextAlignmentOptions.Center, 20);
                        // A glyph taller than its box would be hidden entirely by the ellipsis mode.
                        arrow.overflowMode = TextOverflowModes.Overflow;
                        arrow.textWrappingMode = TextWrappingModes.NoWrap;
                    }
                    break;
                case "Value":
                    Remember(rect);
                    ItemUi.Stretch(rect, new Vector2(38, 0), new Vector2(-38, 0));
                    foreach (var text in child.GetComponentsInChildren<TMP_Text>(true))
                    {
                        Remember(text.rectTransform);
                        ItemUi.Stretch(text.rectTransform, new Vector2(4, 0), new Vector2(-4, 0));
                        Restyle(text, 15, ItemUi.Ink, TextAlignmentOptions.Center, 11);
                    }
                    break;
            }
        }
    }

    private GridLayoutGroup KeyGrid()
    {
        var rect = ItemUi.Rect(content, "NewUI_Keys");
        var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
        grid.startAxis = GridLayoutGroup.Axis.Vertical;
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = 1;
        grid.spacing = new Vector2(16, 0);
        grid.cellSize = new Vector2(300, 40);
        keyGrids.Add(grid);
        return grid;
    }

    private void KeyCell(GridLayoutGroup grid, Item item)
    {
        var row = NewRow(grid.transform, item, 0);
        Rule(row.Rect);
        grid.constraintCount = Mathf.CeilToInt(grid.transform.childCount / 2f);
        var labels = ItemUi.Rect(row.Rect, "Text");
        ItemUi.Stretch(labels, new Vector2(Inset + 4, 0), new Vector2(-(KeyWidth + Inset + 10), 0));
        if (item.Label)
        {
            Move(item.Label.rectTransform, labels);
            ItemUi.Stretch(item.Label.rectTransform, Vector2.zero, Vector2.zero);
            Restyle(item.Label, 15, ItemUi.Ink, TextAlignmentOptions.MidlineLeft, 11);
        }
        // Valheim looks for each key button under its own binding row when it opens the
        // bind dialog, refreshes captions or closes, so the whole row moves into the cell.
        var binding = item.Control.parent as RectTransform;
        if (binding && binding != page && binding.GetComponentsInChildren<Selectable>(true).Length == 1)
        {
            Move(binding, row.Rect);
            ItemUi.Stretch(binding, Vector2.zero, Vector2.zero);
        }
        else Move(item.Control, row.Rect);
        Pin(item.Control, 1, KeyWidth, 30);
        item.Control.anchoredPosition = new Vector2(-Inset, 0);
        foreach (var text in item.Control.GetComponentsInChildren<TMP_Text>(true))
            Restyle(text, 14, ItemUi.Ink, TextAlignmentOptions.Center, 10);
    }

    private void Block(Item item)
    {
        var row = NewRow(content, item, 1);
        row.Background.raycastTarget = false;
        var size = item.Control.rect.size;
        if (size.x < 1 || size.y < 1) size = new Vector2(900, 360);
        // The game resets the map's own scale, so a holder of ours carries the scale instead.
        var holder = ItemUi.Rect(row.Rect, "Holder");
        holder.anchorMin = holder.anchorMax = holder.pivot = Vector2.one * 0.5f;
        holder.sizeDelta = size;
        Move(item.Control, holder);
        item.Control.anchorMin = item.Control.anchorMax = item.Control.pivot = Vector2.one * 0.5f;
        item.Control.sizeDelta = size;
        item.Control.anchoredPosition = Vector2.zero;
        blocks.Add((row.Rect.GetComponent<LayoutElement>(), holder, size));
        row.Lines = new List<Graphic>();
        foreach (var graphic in item.Control.GetComponentsInChildren<Graphic>(true))
            if (graphic.GetType().Name == "UILineRenderer") row.Lines.Add(graphic);
        var lines = row.Lines;
        style.Remember(() =>
        {
            foreach (var line in lines)
                if (line) line.canvasRenderer.cull = false;
        });
    }

    private void ActionBars(List<Item> actions)
    {
        float available = Mathf.Max(200, host.rect.width - 16 - Inset * 2);
        RectTransform bar = null;
        float used = 0;
        foreach (var item in actions)
        {
            var label = item.Control.GetComponentInChildren<TMP_Text>(true);
            bool destructive = item.Control.name.IndexOf("Delete", StringComparison.OrdinalIgnoreCase) >= 0;
            if (label) Restyle(label, 15, destructive ? ItemUi.Warning : ItemUi.Ink, TextAlignmentOptions.Center, 11);
            float width = Mathf.Clamp((label ? label.GetPreferredValues(label.text, 999, 36).x : 120) + 40, 132, available);
            if (!bar || used + width > available)
            {
                bar = ItemUi.Rect(content, "NewUI_Actions");
                actionBars.Add(bar);
                var layout = bar.gameObject.AddComponent<LayoutElement>();
                layout.minHeight = layout.preferredHeight = 50;
                var line = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = line.childForceExpandHeight = false;
                line.spacing = 10;
                line.padding = new RectOffset((int)Inset, (int)Inset, 6, 6);
                used = 0;
            }
            used += width + 10;
            var holder = ItemUi.Rect(bar, "Action");
            var size = holder.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = width;
            size.preferredHeight = 38;
            var row = new Row { Rect = holder, Item = item };
            rows.Add(row);
            Move(item.Control, holder);
            ItemUi.Stretch(item.Control, Vector2.zero, Vector2.zero);
            if (label)
            {
                Remember(label.rectTransform);
                ItemUi.Stretch(label.rectTransform, new Vector2(10, 0), new Vector2(-10, 0));
            }
        }
    }

    // Whatever the rows did not take — native frames, spacers and scrollbars — stays in the page, hidden.
    private void HideLeftovers()
    {
        foreach (Transform child in page)
        {
            if (!IsOverlay(child)) continue;
            if (child.name.Contains("Dialog"))
            {
                Remember((RectTransform)child);
                ItemUi.Stretch((RectTransform)child, Vector2.zero, Vector2.zero);
                // The stock backdrop lets the rows show through the dialog card.
                if (child.TryGetComponent<Image>(out var backdrop))
                {
                    var tint = backdrop.color;
                    style.Remember(() => { if (backdrop) backdrop.color = tint; });
                    backdrop.color = new Color32(4, 8, 10, 205);
                }
                bool first = true;
                foreach (var text in child.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.GetComponentInParent<Selectable>()) continue;
                    style.Text(text, first ? ItemUi.Accent : ItemUi.Muted);
                    first = false;
                }
            }
        }
        foreach (var graphic in page.GetComponentsInChildren<Graphic>(true))
        {
            if (Overlaid(graphic.transform)) continue;
            style.Hide(graphic);
            style.Claim(graphic);
        }
        foreach (var leftover in page.GetComponentsInChildren<Selectable>(true))
        {
            if (Overlaid(leftover.transform)) continue;
            var navigation = leftover.navigation;
            var control = leftover;
            style.Remember(() => { if (control) control.navigation = navigation; });
            leftover.navigation = new Navigation { mode = Navigation.Mode.None };
        }
    }

    private bool Overlaid(Transform node)
    {
        for (var current = node; current && current != page; current = current.parent)
            if (IsOverlay(current) || Back && current == Back.transform) return true;
        return false;
    }

    private void Resize(float width)
    {
        foreach (var grid in keyGrids)
            grid.cellSize = new Vector2(Mathf.Floor((width - grid.spacing.x) / 2), 40);
        foreach (var (row, block, size) in blocks)
        {
            // Callout labels reach past the map's own rect on both sides.
            float scale = Mathf.Min(1, (width - Inset * 2) / (size.x + 160));
            row.minHeight = row.preferredHeight = size.y * scale + 12;
            block.localScale = new Vector3(scale, scale, 1);
        }
        LayoutRebuilder.MarkLayoutForRebuild(content);
    }

    private void Sync(Row row, GameObject focus)
    {
        var item = row.Item;
        if (item == null || !item.Control) return;
        bool visible = item.Control.gameObject.activeSelf;
        foreach (var parent in item.Ancestry)
            if (parent && !parent.activeSelf) visible = false;
        if (row.Rect.gameObject.activeSelf != visible) row.Rect.gameObject.SetActive(visible);
        if (!visible) return;

        // The game keeps a selected control even for mouse users; only gamepad navigation needs to see it.
        bool focused = focus && ZInput.IsGamepadActive() && focus.transform.IsChildOf(item.Control);
        if (row.Background)
            row.Background.color = focused ? Focus : row.Hovered ? Hover : Color.clear;
        if (row.Marker) row.Marker.enabled = focused;

        if (row.Lines != null)
        {
            // Connector lines ignore the viewport mask, so they show only while the whole map is in view.
            bool whole = Inside(row.Rect);
            foreach (var line in row.Lines)
                if (line && line.canvasRenderer.cull == whole) line.canvasRenderer.cull = !whole;
        }

        if (row.Toggle && row.Track)
        {
            float target = row.Toggle.isOn ? 1 : 0;
            row.Position = row.Position < 0 ? target : Mathf.MoveTowards(row.Position, target, Time.unscaledDeltaTime * 8);
            var knob = row.Knob.rectTransform;
            knob.anchoredPosition = new Vector2(Mathf.Lerp(12, ToggleWidth - 12, row.Position), 0);
            row.Track.color = Color.Lerp(SwitchOff, ItemUi.Accent, row.Position);
            row.Knob.color = Color.Lerp(ItemUi.Muted, MenuStyle.Dark, row.Position);
            if (!row.Toggle.IsInteractable()) row.Track.color *= new Color(1, 1, 1, 0.45f);
        }
    }

    private bool Inside(RectTransform target)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        var bounds = viewport.rect;
        return viewport.InverseTransformPoint(corners[0]).y >= bounds.yMin - 1
            && viewport.InverseTransformPoint(corners[1]).y <= bounds.yMax + 1;
    }

    private void Reveal(RectTransform target)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        float bottom = viewport.InverseTransformPoint(corners[0]).y;
        float top = viewport.InverseTransformPoint(corners[1]).y;
        var bounds = viewport.rect;
        float shift = 0;
        if (top > bounds.yMax - 8) shift = top - (bounds.yMax - 8);
        else if (bottom < bounds.yMin + 8) shift = bottom - (bounds.yMin + 8);
        if (Mathf.Abs(shift) < 0.5f) return;
        float limit = Mathf.Max(0, content.rect.height - bounds.height);
        var position = content.anchoredPosition;
        position.y = Mathf.Clamp(position.y - shift, 0, limit);
        content.anchoredPosition = position;
    }

    private void Remember(RectTransform rect) => style.Remember(new MenuRect(rect).Restore);

    private void Move(RectTransform rect, Transform parent)
    {
        Remember(rect);
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void Restyle(TMP_Text text, float size, Color color, TextAlignmentOptions alignment, float minimum)
    {
        if (!text) return;
        var font = text.font;
        var material = text.fontSharedMaterial;
        var fontSize = text.fontSize;
        var tint = text.color;
        var align = text.alignment;
        var wrapping = text.textWrappingMode;
        var overflow = text.overflowMode;
        var autoSize = text.enableAutoSizing;
        var min = text.fontSizeMin;
        var max = text.fontSizeMax;
        var weight = text.fontStyle;
        var margin = text.margin;
        var body = text.text;
        style.Remember(() =>
        {
            if (!text) return;
            text.font = font;
            text.fontSharedMaterial = material;
            text.fontSize = fontSize;
            text.color = tint;
            text.alignment = align;
            text.textWrappingMode = wrapping;
            text.overflowMode = overflow;
            text.enableAutoSizing = autoSize;
            text.fontSizeMin = min;
            text.fontSizeMax = max;
            text.fontStyle = weight;
            text.margin = margin;
            if (text.text == body.Trim()) text.text = body;
        });
        if (sans && text.font != sans) text.font = sans;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMax = size;
        text.fontSizeMin = minimum;
        text.fontSize = size;
        text.fontStyle = FontStyles.Normal;
        text.margin = Vector4.zero;
        // Some labels end in a line break, which pushes them off the row's centre.
        if (body != null && body != body.Trim()) text.text = body.Trim();
    }

    internal void Dispose() => ItemUi.Destroy(root);
}

internal sealed class SettingsRowPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    internal Action<bool> Hover;
    internal Action Click;

    public void OnPointerEnter(PointerEventData eventData) => Hover?.Invoke(true);
    public void OnPointerExit(PointerEventData eventData) => Hover?.Invoke(false);

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Click?.Invoke();
    }
}
