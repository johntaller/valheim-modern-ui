using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Stamina as one curved bar on the right of the crosshair, filling from the bottom up.
internal sealed class StaminaArcs : MaskableGraphic
{
    private const float HalfSweep = 51 * Mathf.Deg2Rad;
    private const float BorderMargin = 2.5f * Mathf.Deg2Rad;
    private const int Segments = 24;
    private Color32 track, fill;
    private float ratio, exhaustedAt;
    private bool exhausted;

    internal float Ratio => exhausted ? 0 : ratio;

    internal void Style(Theme theme)
    {
        track = theme.Background;
        fill = Color.Lerp(theme.Stamina, theme.Text, 0.60f);
        SetVerticesDirty();
    }

    internal void Value(float current, float maximum)
    {
        float next = maximum > 0 ? Mathf.Clamp01(current / maximum) : 0;
        bool empty = maximum > 0 && current <= 0;
        if (next != ratio || empty != exhausted)
        {
            if (empty != exhausted)
            {
                exhausted = empty;
                if (empty) exhaustedAt = Time.unscaledTime;
                else canvasRenderer.SetAlpha(1);
            }
            ratio = next;
            SetVerticesDirty();
        }
        if (!empty) return;

        const float period = 0.6f;
        float flash = 0.5f + 0.5f * Mathf.Cos((Time.unscaledTime - exhaustedAt) * (2 * Mathf.PI / period));
        canvasRenderer.SetAlpha(Mathf.Lerp(0.25f, 1, flash));
    }

    // Where the filled part ends, pushed outwards from the arc by the given distance.
    internal Vector2 FillEnd(float outwards)
    {
        var rect = rectTransform.rect;
        float angle = Mathf.Lerp(-HalfSweep, HalfSweep, Ratio);
        return rect.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (Radius(rect) + outwards);
    }

    private static float Radius(Rect rect) => Mathf.Min(rect.width, rect.height) * 0.5f - 4;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        float radius = Radius(rect);
        if (radius <= 4) return;

        Arc(mesh, rect.center, radius, -HalfSweep - BorderMargin, HalfSweep + BorderMargin, 3.3f, track);
        float fraction = exhausted ? 1 : ratio;
        if (fraction > 0)
            Arc(mesh, rect.center, radius, -HalfSweep, Mathf.Lerp(-HalfSweep, HalfSweep, fraction),
                1.95f, exhausted ? ItemUi.Warning : fill);
    }

    // A band along the circle with rounded ends, between two angles measured from the right.
    private static void Arc(VertexHelper mesh, Vector2 center, float radius, float from, float to, float halfWidth, Color32 tint)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(Segments * (to - from) / (2 * HalfSweep)), 1, Segments);
        int first = mesh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(from, to, (float)i / steps);
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            mesh.AddVert(center + direction * (radius - halfWidth), tint, Vector2.zero);
            mesh.AddVert(center + direction * (radius + halfWidth), tint, Vector2.zero);
            if (i == 0) continue;
            int vertex = first + i * 2;
            mesh.AddTriangle(vertex - 2, vertex, vertex + 1);
            mesh.AddTriangle(vertex - 2, vertex + 1, vertex - 1);
        }
        Cap(mesh, center, radius, from, halfWidth, -1, tint);
        Cap(mesh, center, radius, to, halfWidth, 1, tint);
    }

    // A half disc closing one end of the band.
    private static void Cap(VertexHelper mesh, Vector2 center, float radius, float angle, float halfWidth, float side, Color32 tint)
    {
        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var along = new Vector2(-direction.y, direction.x) * side;
        var end = center + direction * radius;
        int start = mesh.currentVertCount;
        mesh.AddVert(end, tint, Vector2.zero);
        const int steps = 6;
        for (int i = 0; i <= steps; i++)
        {
            float t = Mathf.PI * i / steps;
            mesh.AddVert(end + (direction * Mathf.Cos(t) + along * Mathf.Sin(t)) * halfWidth, tint, Vector2.zero);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
