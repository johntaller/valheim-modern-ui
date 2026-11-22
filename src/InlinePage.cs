using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class InlinePage
{
    private readonly RectTransform page, target;
    private readonly MenuStyle style;
    private readonly Vector2 originalSize;
    private readonly string[] titles;
    private Rect footprint;
    private bool fitted;
    private Vector2 lastSize;

    internal bool Active => page && page.gameObject.activeSelf;

    internal InlinePage(Transform native, RectTransform target, MenuStyle style, params string[] titles)
    {
        page = (RectTransform)native;
        this.target = target;
        this.style = style;
        this.titles = titles;
        var placement = new MenuRect(page);
        style.Remember(placement.Restore);
        originalSize = new Vector2(Mathf.Max(1, page.rect.width), Mathf.Max(1, page.rect.height));
        footprint = page.rect;
        page.SetParent(target, false);
        page.anchorMin = page.anchorMax = new Vector2(0.5f, 0.5f);
        page.sizeDelta = originalSize;
        page.localScale = Vector3.one;
        page.localRotation = Quaternion.identity;
        page.anchoredPosition3D = Vector3.zero;
    }

    internal void Fit()
    {
        if (!Active) return;
        if (!fitted)
        {
            FitToContents();
            fitted = true;
        }
        Vector2 size = target.rect.size;
        if (size == lastSize || size.x <= 1 || size.y <= 1) return;
        lastSize = size;
        float scale = Mathf.Min(size.x / Mathf.Max(1, footprint.width), size.y / Mathf.Max(1, footprint.height));
        page.localScale = new Vector3(scale, scale, 1);
        page.anchoredPosition = -footprint.center * scale;
        if (titles.Length > 0) page.anchoredPosition += Vector2.up * ((size.y - footprint.height * scale) * 0.5f);
    }

    private void FitToContents()
    {
        var xy = new Vector3[4];
        var bounds = new Bounds();
        bool first = true;
        var rootImage = page.GetComponent<Image>();
        if (rootImage && !page.GetComponent<Selectable>()) style.Hide(rootImage);
        foreach (var image in page.GetComponentsInChildren<Image>(false))
        {
            var rect = image.rectTransform;
            if (rect == page || !image.enabled || image.canvasRenderer.cull) continue;
            bool fullPage = rect.rect.width >= originalSize.x * 0.98f && rect.rect.height >= originalSize.y * 0.98f;
            string name = image.name.ToLowerInvariant();
            if (fullPage && (name.Contains("shadow") || name.Contains("dark") || !image.sprite))
            {
                style.Hide(image);
                continue;
            }
            // A scroll content can be much taller than what the player can actually see.
            if (image.GetComponentInParent<ScrollRect>() is ScrollRect scroll && scroll.transform != rect) continue;
            rect.GetWorldCorners(xy);
            for (int i = 0; i < xy.Length; i++)
            {
                Vector3 p = page.InverseTransformPoint(xy[i]);
                if (first) bounds = new Bounds(p, Vector3.zero);
                else bounds.Encapsulate(p);
                first = false;
            }
        }
        if (!first && titles.Length == 0)
        {
            // Some settings labels extend to the left of the native controls.
            // Include their glyphs without counting the vertical extent of scrolled rows.
            foreach (var label in page.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!label.enabled || string.IsNullOrWhiteSpace(label.text)) continue;
                var text = label.textBounds;
                if (text.size.x <= 0) continue;
                var left = page.InverseTransformPoint(label.transform.TransformPoint(text.min));
                var right = page.InverseTransformPoint(label.transform.TransformPoint(text.max));
                bounds.Encapsulate(new Vector3(left.x, bounds.center.y));
                bounds.Encapsulate(new Vector3(right.x, bounds.center.y));
            }
        }
        if (!first && bounds.size.x >= 100 && bounds.size.y >= 100)
        {
            const float pad = 6;
            footprint = Rect.MinMaxRect(bounds.min.x - pad, bounds.min.y - pad, bounds.max.x + pad, bounds.max.y + pad);
        }
        if (titles.Length == 0) return;
        foreach (var image in page.GetComponentsInChildren<Image>(false))
        {
            if (image == rootImage || !image.sprite || image.GetComponentInParent<ScrollRect>() ||
                image.GetComponentInParent<Selectable>()) continue;
            var rect = image.rectTransform.rect;
            if (rect.width >= footprint.width * 0.9f && rect.height >= footprint.height * 0.9f)
                style.Hide(image);
        }
        foreach (var label in page.GetComponentsInChildren<TMP_Text>(false))
        {
            if (label.GetComponentInParent<ScrollRect>() || label.GetComponentInParent<Selectable>()) continue;
            foreach (var title in titles)
            {
                if (!string.Equals(label.text.Trim(), title, StringComparison.CurrentCultureIgnoreCase)) continue;
                style.Hide(label);
                break;
            }
        }
    }
}
