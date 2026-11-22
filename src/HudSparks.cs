using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class HudSparks : MaskableGraphic
{
    private float elapsed, spread, rise;
    private int count;

    internal static HudSparks Create(Transform parent, string name, float width, float height, int count)
    {
        var rect = ItemUi.Rect(parent, name);
        HudStyle.Center(rect, 0, 0, width, height);
        var sparks = rect.gameObject.AddComponent<HudSparks>();
        sparks.raycastTarget = false;
        sparks.count = count;
        return sparks;
    }

    internal void Animate(float time, Color tint, float horizontalSpread, float lift)
    {
        elapsed = time;
        color = tint;
        spread = horizontalSpread;
        rise = lift;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        for (int i = 0; i < count; i++)
        {
            float seed = Mathf.Repeat((i + 1) * 0.618034f, 1);
            float phase = Mathf.Repeat(elapsed * (0.48f + seed * 0.37f) + i * 0.3117f, 1);
            float x = (seed * 2 - 1) * spread + Mathf.Sin(i * 4.11f + phase * 4) * 7;
            float y = -rise + phase * (rise * 2 + 9 * seed);
            float radius = (0.85f + 1.55f * seed) * (1 - phase * 0.45f);
            Color tint = color;
            tint.a *= Mathf.Sin(phase * Mathf.PI) * (0.34f + seed * 0.52f);
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector2(x, y + radius * 1.4f), tint, Vector2.zero);
            mesh.AddVert(new Vector2(x - radius, y), tint, Vector2.zero);
            mesh.AddVert(new Vector2(x, y - radius * 1.4f), tint, Vector2.zero);
            mesh.AddVert(new Vector2(x + radius, y), tint, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}

internal sealed class GlowGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        const int sides = 56;
        var radius = rectTransform.rect.size * 0.5f;
        for (int i = 0; i < 3; i++)
        {
            float scale = i == 0 ? 0.38f : i == 1 ? 0.65f : 1;
            Color32 tint = new(255, 179, 64, (byte)(i == 1 ? 118 : 0));
            for (int j = 0; j <= sides; j++)
            {
                float angle = Mathf.PI * 2 * j / sides;
                mesh.AddVert(new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y) * scale,
                    tint, Vector2.zero);
                if (i == 0 || j == 0) continue;
                int v = mesh.currentVertCount - 1;
                int next = v - sides - 1;
                mesh.AddTriangle(v - 1, v, next);
                mesh.AddTriangle(v - 1, next, next - 1);
            }
        }
    }
}
