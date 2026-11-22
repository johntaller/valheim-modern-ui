using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class StoragePanel : IDisposable
{
    private const float HeaderHeight = 66, FooterHeight = 34;
    internal const float Overhead = HeaderHeight + FooterHeight + 13;
    private readonly InventoryGui gui;
    private readonly RectTransform root, grid, plate, name, takeAll, placeStacks;
    private readonly MenuRect originalName, originalTakeAll, originalPlaceStacks;
    private readonly Vector3 originalScale;
    private readonly Vector2 originalPosition;
    private readonly HiddenGraphics nativeWeight;
    private readonly Artwork art;
    private readonly TextMeshProUGUI weight;
    private readonly Image weightIcon;
    private readonly List<(Image Image, bool Enabled, bool Raycasts)> backdrops = new();
    private bool styled, gathered;

    internal StoragePanel(InventoryGui gui, Artwork art, TMP_FontAsset font)
    {
        this.gui = gui;
        this.art = art;
        root = gui.m_container;
        grid = gui.ContainerGrid.m_gridRoot;
        originalScale = grid.localScale;
        originalPosition = root.anchoredPosition;
        var background = ItemUi.Image(root, "NewUI_Storage", art["panel"]);
        plate = background.rectTransform;
        plate.SetAsFirstSibling();
        plate.gameObject.SetActive(false);
        name = gui.m_containerName.rectTransform;
        originalName = new MenuRect(name);
        if (gui.m_takeAllButton)
        {
            takeAll = (RectTransform)gui.m_takeAllButton.transform;
            originalTakeAll = new MenuRect(takeAll);
        }
        if (gui.m_stackAllButton)
        {
            placeStacks = (RectTransform)gui.m_stackAllButton.transform;
            originalPlaceStacks = new MenuRect(placeStacks);
        }
        if (gui.m_containerWeight)
        {
            var badge = gui.m_containerWeight.transform;
            if (badge.parent != root && !grid.IsChildOf(badge.parent) && !name.IsChildOf(badge.parent) &&
                (!takeAll || !takeAll.IsChildOf(badge.parent)) &&
                (!placeStacks || !placeStacks.IsChildOf(badge.parent))) badge = badge.parent;
            nativeWeight = new HiddenGraphics(badge.gameObject);
            nativeWeight.SetRaycasts(false);
        }
        weight = MenuStyle.Label(plate, "TotalWeight", font, "", 15, ItemUi.Ink, TextAlignmentOptions.MidlineRight);
        weightIcon = ItemUi.Image(plate, "Weight", art["weight"]);
        weightIcon.color = ItemUi.Accent;
    }

    internal float Scale => grid.localScale.y / originalScale.y;

    internal void Fit(RectTransform first, RectTransform last, float scale, RectTransform backpack)
    {
        if (!first || !last) return;
        GatherBackdrops();
        foreach (var entry in backdrops)
        {
            if (!entry.Image) continue;
            entry.Image.enabled = false;
            entry.Image.raycastTarget = false;
        }
        grid.localScale = originalScale * scale;
        Vector3 firstPoint = first.TransformPoint(new Vector2(first.rect.xMin, first.rect.yMax));
        Vector3 lastPoint = last.TransformPoint(new Vector2(last.rect.xMax, last.rect.yMin));
        Vector3 desired = backpack.TransformPoint(new Vector2(backpack.rect.xMin + 16, backpack.rect.yMin - 10))
            - root.TransformVector(Vector3.up * HeaderHeight);
        root.position += desired - firstPoint;
        Vector3 firstLocal = root.InverseTransformPoint(desired);
        Vector3 lastLocal = root.InverseTransformPoint(lastPoint + desired - firstPoint);
        float x = firstLocal.x - root.rect.xMin - 16;
        float y = root.rect.yMax - firstLocal.y - HeaderHeight;
        float width = Mathf.Max(224, lastLocal.x - firstLocal.x + 32);
        float gridHeight = Mathf.Max(0, firstLocal.y - lastLocal.y);
        ItemUi.TopLeft(plate, x, y, width, gridHeight + HeaderHeight + FooterHeight);
        PositionNative(name, x + 13, y + 4, width - 26, 26);
        if (takeAll) PositionNative(takeAll, x + 13, y + 33, 91, 25);
        if (placeStacks) PositionNative(placeStacks, x + 111, y + 33, 100, 25);
        float total = gui.ContainerGrid.GetInventory().GetTotalWeight();
        weight.text = $"{total:0.##} " + MenuStyle.Translate("kg", "кг");
        float textWidth = Mathf.Ceil(weight.GetPreferredValues(weight.text).x);
        ItemUi.TopLeft(weight.rectTransform, width - textWidth - 15, gridHeight + HeaderHeight + 3, textWidth, 24);
        ItemUi.TopLeft(weightIcon.rectTransform, width - textWidth - 36, gridHeight + HeaderHeight + 7, 16, 16);
        plate.gameObject.SetActive(true);
        styled = true;
    }

    private void PositionNative(RectTransform rect, float x, float y, float width, float height)
    {
        // The original controls can be nested under the chest's stock backdrop.
        var parent = (RectTransform)rect.parent;
        Vector3 point = root.TransformPoint(new Vector2(root.rect.xMin + x, root.rect.yMax - y));
        var local = parent.InverseTransformPoint(point);
        var size = parent.InverseTransformVector(root.TransformVector(new Vector2(width, height)));
        ItemUi.TopLeft(rect, local.x - parent.rect.xMin, parent.rect.yMax - local.y, size.x, size.y);
    }

    private void GatherBackdrops()
    {
        if (gathered) return;
        gathered = true;
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            var transform = image.transform;
            if (transform.IsChildOf(grid) || transform.IsChildOf(plate) ||
                takeAll && transform.IsChildOf(takeAll) || placeStacks && transform.IsChildOf(placeStacks)) continue;
            if (image.name == "Darken" || image.sprite == art["panel"]
                || image.sprite && image.sprite.name.StartsWith("woodpanel", StringComparison.Ordinal))
                backdrops.Add((image, image.enabled, image.raycastTarget));
        }
    }

    internal void Close()
    {
        if (!styled) return;
        styled = false;
        if (!root) return;
        root.anchoredPosition = originalPosition;
        grid.localScale = originalScale;
        plate.gameObject.SetActive(false);
        originalName.Restore();
        if (takeAll) originalTakeAll.Restore();
        if (placeStacks) originalPlaceStacks.Restore();
        foreach (var entry in backdrops)
        {
            if (!entry.Image) continue;
            entry.Image.enabled = entry.Enabled;
            entry.Image.raycastTarget = entry.Raycasts;
        }
    }

    public void Dispose()
    {
        Close();
        nativeWeight?.Restore();
        ItemUi.Destroy(plate);
    }
}
