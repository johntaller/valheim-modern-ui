using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class StaminaArcs : MaskableGraphic
{
    private const float HalfSweep = 51 * Mathf.Deg2Rad;
    private const float BorderMargin = 2.5f * Mathf.Deg2Rad;
    private const int Segments = 24;
    private Color32 track, fill;
    private float ratio, exhaustedAt;
    private bool exhausted;

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

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 4;
        if (radius <= 4) return;

        DrawPair(mesh, rect.center, radius, -HalfSweep - BorderMargin, HalfSweep + BorderMargin, 3.3f, track);
        float fraction = exhausted ? 1 : ratio;
        if (fraction > 0)
            DrawPair(mesh, rect.center, radius, -HalfSweep, Mathf.Lerp(-HalfSweep, HalfSweep, fraction),
                1.95f, exhausted ? ItemUi.Warning : fill);
    }

    private static void DrawPair(VertexHelper mesh, Vector2 center, float radius, float from, float to,
        float halfWidth, Color32 tint)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(Segments * (to - from) / (2 * HalfSweep)), 1, Segments);
        int first = mesh.currentVertCount;
        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(from, to, (float)i / steps);
            var right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var left = new Vector2(-right.x, right.y);
            mesh.AddVert(center + right * (radius - halfWidth), tint, Vector2.zero);
            mesh.AddVert(center + right * (radius + halfWidth), tint, Vector2.zero);
            mesh.AddVert(center + left * (radius + halfWidth), tint, Vector2.zero);
            mesh.AddVert(center + left * (radius - halfWidth), tint, Vector2.zero);
            if (i == 0) continue;

            int vertex = first + i * 4;
            mesh.AddTriangle(vertex - 4, vertex, vertex + 1);
            mesh.AddTriangle(vertex - 4, vertex + 1, vertex - 3);
            mesh.AddTriangle(vertex - 2, vertex + 2, vertex + 3);
            mesh.AddTriangle(vertex - 2, vertex + 3, vertex - 1);
        }
    }
}
