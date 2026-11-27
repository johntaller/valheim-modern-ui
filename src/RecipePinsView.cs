using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Read-only tracker in the HUD; an independently paged copy with unpin controls in inventory/chests.
internal sealed class RecipePinsView : IDisposable
{
    private const float HeaderHeight = 27, LineHeight = 27, Padding = 7, PageSeconds = 6;
    private static readonly Color32 Enough = new(166, 225, 175, 255);
    private static readonly Color32 Missing = new(245, 139, 126, 255);
    private readonly RectTransform root;
    private readonly RecipePins pins;
    private readonly InventoryGui gui;
    private readonly CanvasGroup controls;
    private readonly TextMeshProUGUI title, pageNumber;
    private readonly Button previous, next;
    private readonly Row[] visibleRows;
    private readonly List<Line> lines = new();
    private readonly bool interactive;
    private int revision = -1, session = -1, page;
    private float width, titleWidth, nextPage;
    private int columns;

    internal RecipePinsView(Transform parent, string name, RecipePins pins, Artwork art, TMP_FontAsset font,
        InventoryGui gui = null)
    {
        this.pins = pins;
        this.gui = gui;
        interactive = gui;
        root = ItemUi.Image(parent, name, art["panel"]).rectTransform;
        controls = root.gameObject.AddComponent<CanvasGroup>();
        controls.interactable = controls.blocksRaycasts = interactive;
        title = Text(root, "Title", font, 16);
        pageNumber = Text(root, "PageNumber", font, 12, TextAlignmentOptions.Center);
        if (interactive)
        {
            previous = MakeButton(root, "PreviousPage", "‹", art, font, () => Move(-1));
            next = MakeButton(root, "NextPage", "›", art, font, () => Move(1));
        }
        visibleRows = new Row[interactive ? 3 : 6];
        for (int i = 0; i < visibleRows.Length; i++) visibleRows[i] = new Row(this, root, art, font);
        root.gameObject.SetActive(false);
    }

    // Matches the rendered hotbar, regardless of the native CanvasScaler and template pivot.
    // The inventory layout reserves the returned height to keep its grid clear of this view.
    internal float Refresh(bool visible, RectTransform first, RectTransform last, RectTransform below, bool blocked = false)
    {
        visible &= pins.Ids.Count > 0 && pins.Plan != null;
        bool appeared = visible && !root.gameObject.activeSelf;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (!visible) return 0;
        if (interactive) controls.interactable = controls.blocksRaycasts = !blocked && RecipePins.CanEdit(gui);
        var parent = (RectTransform)root.parent;
        var left = parent.InverseTransformPoint(first.TransformPoint(new Vector2(first.rect.xMin, 0)));
        var right = parent.InverseTransformPoint(last.TransformPoint(new Vector2(last.rect.xMax, 0)));
        var bottom = parent.InverseTransformPoint(below.TransformPoint(new Vector2(0, below.rect.yMin)));
        float availableWidth = Mathf.Max(240, right.x - left.x);
        bool resized = !Mathf.Approximately(width, availableWidth);
        if (resized)
        {
            width = availableWidth;
            titleWidth = Mathf.Clamp(width * 0.32f, 94, 164);
            columns = Mathf.Clamp(Mathf.FloorToInt((width - Padding * 2 - titleWidth - (interactive ? 27 : 0)) / 120), 1, 4);
        }
        if (session != pins.Session)
        {
            session = pins.Session;
            page = 0;
            appeared = true;
        }
        if (resized || revision != pins.Revision)
        {
            revision = pins.Revision;
            BuildLines();
            page = Mathf.Min(page, PageCount - 1);
            Draw();
        }
        if (appeared) nextPage = Time.unscaledTime + PageSeconds;
        if (!interactive && PageCount > 1 && Time.unscaledTime >= nextPage)
        {
            page = (page + 1) % PageCount;
            nextPage = Time.unscaledTime + PageSeconds;
            Draw();
        }
        // Keep all pages at a fixed height so paging never moves the backpack/chest.
        float height = HeaderHeight + Mathf.Min(visibleRows.Length, lines.Count) * LineHeight + Padding;
        ItemUi.TopLeft(root, left.x - parent.rect.xMin, parent.rect.yMax - bottom.y + Padding, width, height);
        return parent.TransformVector(Vector3.up * (height + Padding)).y;
    }

    private int PageCount => Mathf.Max(1, (lines.Count + visibleRows.Length - 1) / visibleRows.Length);

    private void Move(int step)
    {
        if (!controls.interactable || !RecipePins.CanEdit(gui) || PageCount < 2) return;
        page = (page + step + PageCount) % PageCount;
        Draw();
    }

    private void BuildLines()
    {
        lines.Clear();
        var plan = pins.Plan;
        bool hasChoices = false;
        for (int i = 0; i < pins.Ids.Count; i++) hasChoices |= pins.HasAlternatives(i);
        AddLines(-1, hasChoices ? MenuStyle.Translate("Total · plan", "Всего · план") : MenuStyle.Translate("Total", "Всего"),
            null, plan.Ready, plan.Total, "");
        for (int i = 0; i < pins.Ids.Count; i++)
        {
            var result = pins.Result(i);
            string name = result == null ? MenuStyle.Translate("Unavailable recipe", "Рецепт недоступен") : Name(result, "");
            var row = plan.Recipes[i];
            string note = result == null ? "—" : pins.HasAlternatives(i)
                ? MenuStyle.Translate("One of these", "Любой из списка") : MenuStyle.Translate("No materials", "Без ресурсов");
            if (pins.HasAlternatives(i) && row.Materials.Length > 0) name += MenuStyle.Translate(" (or)", " (или)");
            AddLines(i, name, result?.GetIcon(), row.Ready, row.Materials, note);
        }
    }

    private void AddLines(int pinIndex, string name, Sprite icon, bool ready, PinMaterial[] materials, string note)
    {
        for (int start = 0; start < Mathf.Max(1, materials.Length); start += columns)
            lines.Add(new Line(pinIndex, name, icon, ready, materials, start, note));
    }

    private void Draw()
    {
        title.text = MenuStyle.Translate("Pinned recipes · Have / Need", "Закреплённые рецепты · Есть / Нужно");
        title.color = ItemUi.Accent;
        CraftingMenu.Put(title.rectTransform, 10, 2, PageCount > 1 ? 102 : 7, 23);
        SetVisible(pageNumber, PageCount > 1);
        if (PageCount > 1)
        {
            pageNumber.text = $"{page + 1}/{PageCount}";
            ItemUi.TopLeft(pageNumber.rectTransform, width - (interactive ? 71 : 45), 2, 38, 23);
        }
        if (interactive)
        {
            SetVisible(previous, PageCount > 1);
            SetVisible(next, PageCount > 1);
            ItemUi.TopLeft((RectTransform)previous.transform, width - 96, 3, 23, 21);
            ItemUi.TopLeft((RectTransform)next.transform, width - 30, 3, 23, 21);
        }
        for (int i = 0; i < visibleRows.Length; i++)
        {
            int index = page * visibleRows.Length + i;
            if (index >= lines.Count) SetVisible(visibleRows[i].Root, false);
            else visibleRows[i].Draw(lines[index], i);
        }
    }

    private static void SetVisible(Component component, bool visible)
    {
        if (component.gameObject.activeSelf != visible) component.gameObject.SetActive(visible);
    }

    private static string Name(ItemDrop.ItemData item, string fallback)
    {
        string value = item?.m_shared.m_name ?? fallback;
        return Localization.instance?.Localize(value) ?? value;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        var label = MenuStyle.Label(parent, name, font, "", size, ItemUi.Ink, alignment);
        label.richText = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Truncate;
        return label;
    }

    private static Button MakeButton(Transform parent, string name, string text, Artwork art, TMP_FontAsset font, Action action)
    {
        var button = MenuStyle.Button(parent, art, font, text, action, out var label, 16);
        button.name = name;
        label.font = font;
        return button;
    }

    public void Dispose() => ItemUi.Destroy(root);

    private readonly struct Line
    {
        internal readonly int PinIndex, Start;
        internal readonly string Name, Note;
        internal readonly Sprite Icon;
        internal readonly bool Ready;
        internal readonly PinMaterial[] Materials;
        internal Line(int pinIndex, string name, Sprite icon, bool ready, PinMaterial[] materials, int start, string note) =>
            (PinIndex, Name, Icon, Ready, Materials, Start, Note) = (pinIndex, name, icon, ready, materials, start, note);
    }

    private sealed class Row
    {
        internal readonly RectTransform Root;
        private readonly RecipePinsView view;
        private readonly Image icon;
        private readonly TextMeshProUGUI name, note;
        private readonly Button remove;
        private readonly MaterialCell[] materials = new MaterialCell[4];
        private string id;

        internal Row(RecipePinsView view, Transform parent, Artwork art, TMP_FontAsset font)
        {
            this.view = view;
            Root = ItemUi.Image(parent, "RecipePinRow", art["slot"]).rectTransform;
            icon = ItemUi.Image(Root, "RecipeIcon");
            icon.preserveAspect = true;
            name = Text(Root, "RecipeName", font, 14);
            note = Text(Root, "Note", font, 12);
            note.color = ItemUi.Muted;
            if (view.interactive)
            {
                remove = MakeButton(Root, "UnpinRecipe", "×", art, font, Remove);
                if (view.gui.m_repairButton && view.gui.m_repairButton.TryGetComponent<UITooltip>(out var nativeTip)
                    && nativeTip.m_tooltipPrefab)
                {
                    var tip = remove.gameObject.AddComponent<UITooltip>();
                    tip.m_tooltipPrefab = nativeTip.m_tooltipPrefab;
                    tip.m_text = MenuStyle.Translate("Unpin recipe", "Открепить рецепт");
                }
            }
            for (int i = 0; i < materials.Length; i++) materials[i] = new MaterialCell(Root, font);
        }

        private void Remove()
        {
            if (id != null && view.controls.interactable && RecipePins.CanEdit(view.gui)) view.pins.Remove(id);
        }

        internal void Draw(Line line, int position)
        {
            SetVisible(Root, true);
            float width = view.width - Padding * 2;
            ItemUi.TopLeft(Root, Padding, HeaderHeight + position * LineHeight, width, LineHeight - 2);
            if (line.Icon) ItemUi.TopLeft(icon.rectTransform, 4, 3, 19, 19);
            icon.sprite = line.Icon;
            icon.enabled = line.Icon;
            float left = line.Icon ? 27 : 7;
            ItemUi.TopLeft(name.rectTransform, left, 0, view.titleWidth - left - 3, LineHeight - 2);
            name.text = line.Name;
            name.color = line.Ready ? Enough : Missing;
            id = line.PinIndex >= 0 ? view.pins.Ids[line.PinIndex] : null;
            if (remove)
            {
                SetVisible(remove, id != null);
                ItemUi.TopLeft((RectTransform)remove.transform, width - 24, 2, 22, 21);
            }
            float area = width - view.titleWidth - (view.interactive ? 27 : 2);
            int count = Mathf.Clamp(line.Materials.Length - line.Start, 1, view.columns);
            float cellWidth = area / count;
            for (int i = 0; i < materials.Length; i++)
            {
                int index = line.Start + i;
                bool show = i < view.columns && index < line.Materials.Length;
                SetVisible(materials[i].Root, show);
                if (show) materials[i].Draw(view.pins, line.Materials[index], view.titleWidth + i * cellWidth, cellWidth);
            }
            note.enabled = line.Materials.Length == 0;
            if (note.enabled)
            {
                note.text = line.Note;
                ItemUi.TopLeft(note.rectTransform, view.titleWidth, 0, area, LineHeight - 2);
            }
        }
    }

    private sealed class MaterialCell
    {
        internal readonly RectTransform Root;
        private readonly Image icon;
        private readonly TextMeshProUGUI name, amount;

        internal MaterialCell(RectTransform parent, TMP_FontAsset font)
        {
            Root = ItemUi.Rect(parent, "Material");
            icon = ItemUi.Image(Root, "Icon");
            icon.preserveAspect = true;
            name = Text(Root, "Name", font, 14);
            name.color = ItemUi.Muted;
            amount = Text(Root, "Count", font, 16, TextAlignmentOptions.MidlineRight);
            amount.enableAutoSizing = true;
            amount.fontSizeMin = 8;
            amount.fontSizeMax = 16;
        }

        internal void Draw(RecipePins pins, PinMaterial material, float left, float width)
        {
            var item = pins.Material(material.Name);
            ItemUi.TopLeft(Root, left, 0, width, LineHeight - 2);
            icon.sprite = item?.GetIcon();
            icon.enabled = icon.sprite;
            ItemUi.TopLeft(icon.rectTransform, 1, 4, 16, 16);
            float amountWidth = Mathf.Clamp(width * 0.38f, 32, 65);
            ItemUi.TopLeft(name.rectTransform, 20, 0, width - 23 - amountWidth, LineHeight - 2);
            ItemUi.TopLeft(amount.rectTransform, width - amountWidth - 4, 0, amountWidth, LineHeight - 2);
            name.text = Name(item, material.Name);
            amount.text = $"{material.Have}/{material.Need}";
            amount.color = material.Ready ? Enough : Missing;
        }
    }
}
