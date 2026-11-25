using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// A vertical health gauge with chevron ends. It animates the events a player should notice
// without looking at it: a longer bar after eating, a blink on each regeneration tick and a
// pulse that runs down the bar on damage, leaving a pale trail where the health was lost.
internal sealed class HealthGauge : MaskableGraphic
{
    private const float Outline = 2;
    private static readonly Color32 Shade = new(22, 5, 7, 235);
    private static readonly Color32 Border = new(104, 20, 25, 255);
    private static readonly Color32 TrackTop = new(46, 8, 11, 250);
    private static readonly Color32 TrackBottom = new(66, 11, 15, 250);
    private static readonly Color32 Bronze = new(122, 58, 30, 255);
    private static readonly Color32 BronzeLight = new(171, 96, 50, 255);
    private static readonly Color32 Trail = new(255, 208, 182, 235);
    private static readonly Color32 Halo = new(255, 86, 82, 255);

    private readonly List<Vector2> polygon = new(), clipped = new(), scratch = new();
    private Color fillCenter = new Color32(210, 36, 45, 255), fillEdge = new Color32(146, 19, 27, 255);
    private float fill, trail, flash, pulse = -1, hurt, grow;
    private float shownFill = -1, shownTrail, shownFlash, shownPulse, shownHurt, shownGrow;

    internal void SetTint(Color health)
    {
        // The reference palette is a saturated red; a theme color shifts its hue.
        fillCenter = Color.Lerp(health, new Color32(214, 36, 45, 255), 0.72f);
        fillEdge = Color.Lerp(fillCenter, Color.black, 0.32f);
        SetVerticesDirty();
    }

    // fill and trail are fractions of the bar; flash, pulse and hurt are animation phases from 0 to 1.
    internal void Set(float fill, float trail, float flash, float pulse, float hurt, float grow)
    {
        this.fill = fill;
        this.trail = Mathf.Max(fill, trail);
        this.flash = flash;
        this.pulse = pulse;
        this.hurt = hurt;
        this.grow = grow;
        if (Mathf.Abs(shownFill - fill) < 0.0005f && Mathf.Abs(shownTrail - this.trail) < 0.0005f
            && shownFlash == flash && shownPulse == pulse && shownHurt == hurt && shownGrow == grow) return;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        shownFill = fill;
        shownTrail = trail;
        shownFlash = flash;
        shownPulse = pulse;
        shownHurt = hurt;
        shownGrow = grow;
        var rect = GetPixelAdjustedRect();
        float half = rect.width * 0.5f - Outline - 3;
        if (half <= 1 || rect.height <= half * 4) return;
        float cx = rect.center.x;
        float chevron = half;
        float top = rect.yMax - Outline - 3;
        float bottom = rect.yMin + Outline + 7;

        if (flash > 0)
        {
            var glow = Halo;
            glow.a = (byte)(150 * flash);
            Ring(mesh, cx, top, bottom, half, Outline, 9 * flash + 3, glow);
        }
        if (grow > 0)
        {
            var gold = (Color)ItemUi.Accent;
            gold.a = 0.55f * grow;
            Ring(mesh, cx, top, bottom, half, Outline, 7, gold);
        }

        // Bronze chevron under the bottom tip, then the dark shade and the red border.
        Band(mesh, cx, bottom - Outline - 3, half + Outline, 3, Bronze, BronzeLight);
        Body(polygon, cx, top + Outline + 1, bottom - Outline - 1, half + Outline + 1);
        Fan(mesh, polygon, _ => Shade);
        Body(polygon, cx, top + Outline, bottom - Outline, half + Outline);
        var border = Color.Lerp(Border, Halo, flash * 0.6f);
        Fan(mesh, polygon, _ => border);

        float height = top - bottom;
        Body(polygon, cx, top, bottom, half);
        Fan(mesh, polygon, p => Color.Lerp(TrackBottom, TrackTop, (p.y - bottom) / height));

        float fillTop = bottom + height * Mathf.Clamp01(fill);
        float trailTop = bottom + height * Mathf.Clamp01(trail);
        Color center = Color.Lerp(fillCenter, new Color32(255, 112, 104, 255), flash * 0.75f + hurt * 0.35f);
        Color edge = Color.Lerp(fillEdge, fillCenter, flash * 0.6f);
        if (trailTop > fillTop + 0.5f)
        {
            var paleTrail = (Color)Trail;
            Strips(mesh, cx, half, x => Clip(polygon, cx, x, trailTop, true, fillTop), _ => paleTrail);
        }
        if (fill > 0)
        {
            const float seam = 4;
            Color Tint(Vector2 p)
            {
                float across = Mathf.Abs(p.x - cx) / half;
                return Color.Lerp(center, edge, across * across);
            }
            Strips(mesh, cx, half, x => Clip(polygon, cx, x, fillTop - seam, false, 0), Tint);
            // A light seam along the fill's leading edge.
            var lit = new Color32(255, 128, 118, 255);
            Strips(mesh, cx, half, x => Clip(polygon, cx, x, fillTop, true, fillTop - seam), p =>
                Color.Lerp(Tint(p), lit, 0.6f * Mathf.Clamp01(1 - (fillTop - Mathf.Abs(p.x - cx) - p.y) / seam)));
            if (pulse >= 0 && pulse < 1)
            {
                // A bright band travels from the leading edge down to the bottom tip.
                float at = Mathf.Lerp(fillTop, bottom, pulse);
                float reach = 18;
                float strength = 0.85f * (1 - pulse);
                var light = new Color(1, 0.86f, 0.80f, 0);
                for (int side = 0; side < 2; side++)
                {
                    float low = side == 0 ? at - reach : at, high = side == 0 ? at : at + reach;
                    Strips(mesh, cx, half, x =>
                    {
                        Clip(polygon, cx, x, fillTop, false, 0);
                        ClipAbove(clipped, low);
                        ClipBelow(clipped, high);
                        return clipped;
                    }, p =>
                    {
                        var c = light;
                        c.a = strength * (1 - Mathf.Abs(p.y - at) / reach);
                        return c;
                    });
                }
            }
        }
    }

    // A hexagon with chevron ends: the top points up and the bottom points down.
    private static void Body(List<Vector2> shape, float cx, float top, float bottom, float half)
    {
        shape.Clear();
        shape.Add(new Vector2(cx, top));
        shape.Add(new Vector2(cx + half, top - half));
        shape.Add(new Vector2(cx + half, bottom + half));
        shape.Add(new Vector2(cx, bottom));
        shape.Add(new Vector2(cx - half, bottom + half));
        shape.Add(new Vector2(cx - half, top - half));
    }

    // The body clipped to one vertical strip and below a chevron with its tip at "limit".
    // With "above" set, the region also starts above a second chevron at "floor".
    private List<Vector2> Clip(List<Vector2> body, float cx, Vector2 strip, float limit, bool above, float floor)
    {
        clipped.Clear();
        clipped.AddRange(body);
        ClipPlane(clipped, new Vector2(-1, 0), -(strip.x));
        ClipPlane(clipped, new Vector2(1, 0), strip.y);
        // The strips never cross the centre line, so each side of a chevron is one half-plane.
        float side = strip.x + strip.y >= 2 * cx ? 1 : -1;
        // y + side * (x - cx) <= limit
        ClipPlane(clipped, new Vector2(side, 1), limit + side * cx);
        if (above) ClipPlane(clipped, new Vector2(-side, -1), -(floor + side * cx));
        return clipped;
    }

    private void Strips(VertexHelper mesh, float cx, float half, System.Func<Vector2, List<Vector2>> region,
        System.Func<Vector2, Color> shade)
    {
        // Strip edges let vertex colors approximate a curved horizontal gradient.
        for (int i = 0; i < 6; i++)
        {
            float a = cx - half + half * 2 * i / 6, b = cx - half + half * 2 * (i + 1) / 6;
            var shape = region(new Vector2(a, b));
            Fan(mesh, shape, shade);
        }
    }

    private void ClipAbove(List<Vector2> shape, float y) => ClipPlane(shape, new Vector2(0, -1), -y);
    private void ClipBelow(List<Vector2> shape, float y) => ClipPlane(shape, new Vector2(0, 1), y);

    // Keeps the part of a convex polygon where dot(normal, p) <= limit.
    private void ClipPlane(List<Vector2> shape, Vector2 normal, float limit)
    {
        if (shape.Count == 0) return;
        scratch.Clear();
        for (int i = 0; i < shape.Count; i++)
        {
            var a = shape[i];
            var b = shape[(i + 1) % shape.Count];
            float da = Vector2.Dot(normal, a) - limit, db = Vector2.Dot(normal, b) - limit;
            if (da <= 0) scratch.Add(a);
            if (da * db < 0) scratch.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        shape.Clear();
        shape.AddRange(scratch);
    }

    private static void Fan(VertexHelper mesh, List<Vector2> shape, System.Func<Vector2, Color> shade)
    {
        if (shape.Count < 3) return;
        int start = mesh.currentVertCount;
        foreach (var point in shape) mesh.AddVert(point, shade(point), Vector2.zero);
        for (int i = 1; i < shape.Count - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
    }

    // A soft glow: a band around the outline that fades to transparent.
    private readonly List<Vector2> inner = new(), outer = new();

    private void Ring(VertexHelper mesh, float cx, float top, float bottom, float half, float inset, float width, Color tint)
    {
        Body(inner, cx, top + inset, bottom - inset, half + inset);
        Body(outer, cx, top + inset + width, bottom - inset - width, half + inset + width);
        var clear = tint;
        clear.a = 0;
        for (int i = 0; i < inner.Count; i++)
        {
            int j = (i + 1) % inner.Count;
            int v = mesh.currentVertCount;
            mesh.AddVert(inner[i], tint, Vector2.zero);
            mesh.AddVert(outer[i], clear, Vector2.zero);
            mesh.AddVert(outer[j], clear, Vector2.zero);
            mesh.AddVert(inner[j], tint, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
            mesh.AddTriangle(v, v + 2, v + 3);
        }
    }

    // A downward chevron stripe of the given thickness below "tip".
    private static void Band(VertexHelper mesh, float cx, float tip, float half, float thickness, Color dark, Color light)
    {
        int v = mesh.currentVertCount;
        mesh.AddVert(new Vector2(cx - half, tip + half), dark, Vector2.zero);
        mesh.AddVert(new Vector2(cx, tip), light, Vector2.zero);
        mesh.AddVert(new Vector2(cx + half, tip + half), dark, Vector2.zero);
        mesh.AddVert(new Vector2(cx + half, tip + half - thickness), dark, Vector2.zero);
        mesh.AddVert(new Vector2(cx, tip - thickness), dark, Vector2.zero);
        mesh.AddVert(new Vector2(cx - half, tip + half - thickness), dark, Vector2.zero);
        mesh.AddTriangle(v, v + 1, v + 4);
        mesh.AddTriangle(v, v + 4, v + 5);
        mesh.AddTriangle(v + 1, v + 2, v + 3);
        mesh.AddTriangle(v + 1, v + 3, v + 4);
    }
}

// A red edge that grows below half health and beats quickly below a fifth.
internal sealed class LowHealthVignette : MaskableGraphic
{
    private const int Segments = 56;
    private float strength, beat, shownStrength = -1, shownBeat = -1;

    internal void Set(float strength, float beat)
    {
        this.strength = strength;
        this.beat = beat;
        if (Mathf.Abs(shownStrength - strength) < 0.002f && Mathf.Abs(shownBeat - beat) < 0.002f) return;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        shownStrength = strength;
        shownBeat = beat;
        if (strength <= 0) return;
        var rect = GetPixelAdjustedRect();
        var center = rect.center;
        var halfSize = rect.size * 0.5f;
        // A stronger warning reaches further towards the centre.
        float inner = Mathf.Lerp(0.86f, 0.52f, strength) - 0.07f * beat;
        float middle = Mathf.Lerp(inner, 1, 0.62f);
        var edge = new Color32(150, 0, 8, 0);
        Color outer = edge, mid = edge, clear = edge;
        outer.a = Mathf.Clamp01(strength * 0.78f + beat * 0.22f);
        mid.a = outer.a * 0.42f;
        clear.a = 0;
        for (int i = 0; i < Segments; i++)
        {
            float angle = i * Mathf.PI * 2 / Segments;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var ellipse = Vector2.Scale(direction, halfSize);
            // Push the outer ring to the screen edge along the same direction.
            float reach = Mathf.Min(Mathf.Abs(direction.x) > 1e-4f ? halfSize.x / Mathf.Abs(direction.x) : float.MaxValue,
                Mathf.Abs(direction.y) > 1e-4f ? halfSize.y / Mathf.Abs(direction.y) : float.MaxValue);
            mesh.AddVert(center + ellipse * inner, clear, Vector2.zero);
            mesh.AddVert(center + ellipse * middle, mid, Vector2.zero);
            mesh.AddVert(center + direction * reach, outer, Vector2.zero);
        }
        for (int i = 0; i < Segments; i++)
        {
            int a = i * 3, b = (i + 1) % Segments * 3;
            mesh.AddTriangle(a, b, b + 1);
            mesh.AddTriangle(a, b + 1, a + 1);
            mesh.AddTriangle(a + 1, b + 1, b + 2);
            mesh.AddTriangle(a + 1, b + 2, a + 2);
        }
        // The corners lie beyond the outer ring's straight edges; fill them at full strength.
        Corner(mesh, center, halfSize, outer);
    }

    private static void Corner(VertexHelper mesh, Vector2 center, Vector2 half, Color tint)
    {
        // Each ring segment ends on the screen border, so only thin slivers at the corners remain uncovered.
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * Mathf.PI * 2 / Segments, a1 = (i + 1) * Mathf.PI * 2 / Segments;
            var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            // A corner lies between two directions when their border hits are on different edges.
            var p0 = Border(d0, half);
            var p1 = Border(d1, half);
            if (Mathf.Abs(p0.x) == half.x == (Mathf.Abs(p1.x) == half.x)) continue;
            var corner = new Vector2(Mathf.Sign(d0.x + d1.x) * half.x, Mathf.Sign(d0.y + d1.y) * half.y);
            int v = mesh.currentVertCount;
            mesh.AddVert(center + p0, tint, Vector2.zero);
            mesh.AddVert(center + corner, tint, Vector2.zero);
            mesh.AddVert(center + p1, tint, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
        }
    }

    private static Vector2 Border(Vector2 direction, Vector2 half)
    {
        float tx = Mathf.Abs(direction.x) > 1e-4f ? half.x / Mathf.Abs(direction.x) : float.MaxValue;
        float ty = Mathf.Abs(direction.y) > 1e-4f ? half.y / Mathf.Abs(direction.y) : float.MaxValue;
        var point = direction * Mathf.Min(tx, ty);
        // Snap to the exact edge so the corner test compares equal values.
        if (tx <= ty) point.x = Mathf.Sign(direction.x) * half.x;
        else point.y = Mathf.Sign(direction.y) * half.y;
        return point;
    }
}
