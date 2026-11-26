using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Equipping and other timed actions: a ring around the crosshair and the action's name under
// it, where notifications never cover them. The game's bar at the bottom stays hidden.
internal sealed class ActionProgressHud : IDisposable
{
    private readonly RectTransform root;
    private readonly ProgressRing ring;
    private readonly TextMeshProUGUI label;
    private readonly Image shade;
    private HiddenGraphics native;
    private Hud owner;

    internal ActionProgressHud(Hud hud, RectTransform parent, Artwork art)
    {
        root = ItemUi.Rect(parent, "ActionProgress");
        HudStyle.Center(root, 0, 0, 84, 84);
        shade = HudStyle.Shadow(root, art, 0.8f);
        // The name sits under the ring, below the eitr and adrenaline values shown with the inventory.
        HudStyle.Center(shade.rectTransform, 0, -94, 260, 56);
        ring = ItemUi.Rect(root, "Ring").gameObject.AddComponent<ProgressRing>();
        ring.raycastTarget = false;
        ItemUi.Stretch(ring.rectTransform, Vector2.zero, Vector2.zero);
        label = ItemUi.Text(root, "Action", HudStyle.DisplayFont(hud.m_healthText.font), 16, ItemUi.Ink);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        HudStyle.Center(label.rectTransform, 0, -92, 320, 22);
        root.gameObject.SetActive(false);
    }

    internal void Refresh(Hud hud, Player player, bool visible)
    {
        if (owner != hud)
        {
            native?.Restore();
            native = hud && hud.m_actionBarRoot ? new HiddenGraphics(hud.m_actionBarRoot) : null;
            owner = hud;
        }
        string name = null;
        float progress = 0;
        if (visible && player) player.GetActionProgress(out name, out progress);
        bool show = visible && progress > 0 && !string.IsNullOrEmpty(name);
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;
        ring.Set(Mathf.Clamp01(progress));
        string text = Localization.instance.Localize(name);
        if (label.text != text) label.text = text;
    }

    public void Dispose()
    {
        native?.Restore();
        ItemUi.Destroy(root);
    }
}

internal sealed class ProgressRing : MaskableGraphic
{
    private static readonly Color32 Track = new(16, 22, 24, 170);
    private static readonly Color32 Fill = new(246, 214, 140, 255);
    private float value = -1;

    internal void Set(float fraction)
    {
        if (Mathf.Abs(fraction - value) < 0.002f) return;
        value = fraction;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
        Arc(mesh, rect.center, outer, outer - 6, 0, 1, Track);
        if (value > 0) Arc(mesh, rect.center, outer - 1, outer - 5, 0, value, Fill);
    }

    // Clockwise from the top.
    private static void Arc(VertexHelper mesh, Vector2 c, float outer, float inner, float from, float to, Color color)
    {
        int segments = Mathf.Max(2, Mathf.CeilToInt(64 * (to - from)));
        int start = mesh.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float t = Mathf.Lerp(from, to, i / (float)segments);
            float angle = Mathf.PI * 0.5f - t * Mathf.PI * 2;
            var unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            mesh.AddVert(c + unit * outer, color, Vector2.zero);
            mesh.AddVert(c + unit * inner, color, Vector2.zero);
            if (i == 0) continue;
            int at = start + i * 2;
            mesh.AddTriangle(at - 2, at, at + 1);
            mesh.AddTriangle(at - 2, at + 1, at - 1);
        }
    }
}

// The game's control hints run along the bottom of the screen. In play they move to a faint
// column on the left of the crosshair, where the eye already is. Menus fill the middle of the
// screen (the inventory and its panels, the build menu, the radial menu), so while one is open
// the column settles in the bottom right corner instead, clear of every panel.
internal sealed class KeyHintsColumn : IDisposable
{
    private const float CornerMargin = 24;
    // Layout groups cannot share a GameObject, so each row group is replaced and later rebuilt.
    private readonly List<(GameObject owner, RectOffset padding, float spacing, TextAnchor alignment, bool controlWidth,
        bool controlHeight, bool expandWidth, bool expandHeight)> rows = new();
    private RectTransform root;
    private CanvasGroup group;
    private bool addedGroup;
    private Vector2 anchorMin, anchorMax, pivot, position, size;
    private Vector3 scale;
    private GameObject measured;
    private float measuredAt;
    private Bounds bounds;

    internal void Refresh(bool visible)
    {
        var hints = KeyHints.instance;
        if (!hints)
        {
            if (root) Restore();
            return;
        }
        var rect = (RectTransform)hints.transform;
        if (rect != root)
        {
            if (root) Restore();
            Take(rect);
        }
        var menu = MenuHints(hints);
        if (menu) Corner(menu);
        else Beside();
        if (group) group.alpha = !visible ? 0 : menu ? 0.8f : 0.55f;
    }

    // The hint set of an open menu, if one is showing.
    private static GameObject MenuHints(KeyHints hints)
    {
        foreach (var set in new[] { hints.m_inventoryHints, hints.m_inventoryWithContainerHints, hints.m_buildUIHints,
                     hints.m_buildHints, hints.m_radialHints, hints.m_barberHints })
            if (set && set.activeInHierarchy) return set;
        return null;
    }

    private void Beside()
    {
        measured = null;
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = new Vector2(-96, 48);
    }

    // Lines the shown rows up against the bottom right corner of the screen.
    private void Corner(GameObject set)
    {
        float now = Time.unscaledTime;
        if (set != measured || now >= measuredAt)
        {
            // The rows settle a frame after the set appears, so the measure is taken again shortly.
            measuredAt = now + (set != measured ? 0.05f : 0.3f);
            measured = set;
            bounds = Visible(set);
        }
        if (bounds.size.x <= 0 || bounds.size.y <= 0) return;
        var area = root.rect;
        float factor = root.localScale.x;
        root.anchorMin = root.anchorMax = new Vector2(1, 0);
        root.anchoredPosition = new Vector2(-CornerMargin - (bounds.max.x - area.xMax) * factor,
            CornerMargin - (bounds.min.y - area.yMax) * factor);
    }

    private void Take(RectTransform rect)
    {
        root = rect;
        anchorMin = rect.anchorMin;
        anchorMax = rect.anchorMax;
        pivot = rect.pivot;
        position = rect.anchoredPosition;
        size = rect.sizeDelta;
        scale = rect.localScale;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        // The column hangs from just above the crosshair, so longer hint sets grow downward and
        // stay clear of the panels above.
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-96, 48);
        rect.sizeDelta = new Vector2(460, 360);
        rect.localScale = Vector3.one * 0.85f;
        group = rect.GetComponent<CanvasGroup>();
        addedGroup = !group;
        if (addedGroup) group = rect.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        // Each hint set lays its keys out in a row; stack them in a right-aligned column instead.
        foreach (var row in rect.GetComponentsInChildren<HorizontalLayoutGroup>(true))
        {
            var parent = row.transform.parent;
            if (!parent || !parent.GetComponent<UIInputHint>()) continue;
            var owner = row.gameObject;
            rows.Add((owner, row.padding, row.spacing, row.childAlignment, row.childControlWidth, row.childControlHeight,
                row.childForceExpandWidth, row.childForceExpandHeight));
            UnityEngine.Object.DestroyImmediate(row);
            var column = owner.AddComponent<VerticalLayoutGroup>();
            column.childAlignment = TextAnchor.UpperRight;
            column.spacing = 2;
            // Each row takes its own size: the width keeps one clean right edge, and the height matters
            // for hint sets that were hidden at start, whose rows were never given one and would
            // squeeze their auto-sized labels down to nothing.
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = column.childForceExpandHeight = false;
        }
    }

    // The box around what the set actually draws, in the column's space: its own rectangle and
    // those of its layout rows are larger than the keys and labels in them.
    private readonly Vector3[] corners = new Vector3[4];

    private Bounds Visible(GameObject set)
    {
        var box = new Bounds();
        bool any = false;
        foreach (var graphic in set.GetComponentsInChildren<Graphic>())
        {
            if (!graphic.enabled || graphic.color.a <= 0) continue;
            graphic.rectTransform.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var local = root.InverseTransformPoint(corner);
                if (!any) box = new Bounds(local, Vector3.zero);
                else box.Encapsulate(local);
                any = true;
            }
        }
        return box;
    }

    private void Restore()
    {
        foreach (var row in rows)
        {
            if (!row.owner) continue;
            var column = row.owner.GetComponent<VerticalLayoutGroup>();
            if (column) UnityEngine.Object.DestroyImmediate(column);
            var restored = row.owner.AddComponent<HorizontalLayoutGroup>();
            restored.padding = row.padding;
            restored.spacing = row.spacing;
            restored.childAlignment = row.alignment;
            restored.childControlWidth = row.controlWidth;
            restored.childControlHeight = row.controlHeight;
            restored.childForceExpandWidth = row.expandWidth;
            restored.childForceExpandHeight = row.expandHeight;
        }
        rows.Clear();
        if (root)
        {
            root.anchorMin = anchorMin;
            root.anchorMax = anchorMax;
            root.pivot = pivot;
            root.anchoredPosition = position;
            root.sizeDelta = size;
            root.localScale = scale;
            if (group)
            {
                if (addedGroup) UnityEngine.Object.Destroy(group);
                else group.alpha = 1;
            }
        }
        root = null;
        group = null;
    }

    public void Dispose() => Restore();
}
