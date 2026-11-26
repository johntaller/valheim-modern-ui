using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Eitr and adrenaline as upright carved bars on the right of the crosshair, outside the stamina
// arc and the ring of a timed action, so they never cover the character. Outside the inventory a
// bar shows only while it has something to say: eitr below its maximum, adrenaline building up or
// burning. With the inventory open both stay up, with their values under them.
internal sealed class AimBars : IDisposable
{
    // The first bar starts past the stamina number, which rides the end of the stamina arc.
    internal const float Length = 84, Thickness = 15, FirstX = 78, Step = 25;
    private const float ValueTop = -Length * 0.5f - 5, ValueStep = 14;
    private static readonly Color32 EitrFill = new(126, 112, 246, 255);
    private static readonly Color32 EitrBorder = new(48, 38, 118, 255);
    private static readonly Color32 AdrenalineBorder = new(112, 56, 18, 255);
    private static readonly Color32 EmberDeep = new(255, 124, 47, 255);
    private static readonly Color32 EmberLight = new(255, 224, 143, 255);

    private readonly Bar eitr, adrenaline;
    private readonly HudSparks sparks;
    private readonly HiddenGraphics nativeEitr, nativeAdrenaline;
    private readonly Color adrenalineFill;
    private int lastEitr = -1, lastMaxEitr = -1, lastAdrenaline = -1, lastMaxAdrenaline = -1;

    internal AimBars(Hud hud, RectTransform parent, TMP_FontAsset font, Artwork art)
    {
        eitr = new Bar(parent, "Eitr", font, art, EitrFill, EitrBorder, 10);
        adrenalineFill = hud.m_adrenalineBarFast ? hud.m_adrenalineBarFast.GetColor() : ItemUi.Accent;
        if (adrenalineFill.a <= 0) adrenalineFill = ItemUi.Accent;
        adrenalineFill.a = 1;
        adrenaline = new Bar(parent, "Adrenaline", font, art, adrenalineFill, AdrenalineBorder, 0);
        sparks = HudSparks.Create(parent, "Embers", 28, 40, 18);
        sparks.gameObject.SetActive(false);
        if (hud.m_eitrBarRoot) nativeEitr = Hide(hud.m_eitrBarRoot.gameObject);
        if (hud.m_adrenalineBarRoot) nativeAdrenaline = Hide(hud.m_adrenalineBarRoot.gameObject);
    }

    private static HiddenGraphics Hide(GameObject target)
    {
        var hidden = new HiddenGraphics(target);
        hidden.SetRaycasts(false);
        return hidden;
    }

    internal void Refresh(Player player, bool visible, bool inventory)
    {
        float now = Time.unscaledTime;
        float maxEitr = visible ? player.GetMaxEitr() : 0, currentEitr = visible ? player.GetEitr() : 0;
        bool showEitr = maxEitr > 0 && (inventory || currentEitr < maxEitr - 0.5f);

        float maxAdrenaline = visible ? player.GetMaxAdrenaline() : 0;
        float currentAdrenaline = visible ? player.GetAdrenaline() : 0;
        float fill = maxAdrenaline > 0 ? Mathf.Clamp01(currentAdrenaline / maxAdrenaline) : 0;
        float burning = visible ? Burning(player) : 0;
        bool active = burning > 0;
        bool showAdrenaline = maxAdrenaline > 0 && (inventory || active || fill > 0.001f && fill < 0.999f);

        // The shown bars line up outwards from the crosshair and slide when one leaves; their
        // values stack under them in the same order.
        float x = FirstX, row = ValueTop;
        eitr.Place(showEitr, ref x, ref row, now);
        adrenaline.Place(showAdrenaline, ref x, ref row, now);

        if (eitr.Visible)
        {
            eitr.Graphic.Set(maxEitr > 0 ? Mathf.Clamp01(currentEitr / maxEitr) : 0, 0, now);
            eitr.Value(inventory, currentEitr, maxEitr, ref lastEitr, ref lastMaxEitr);
        }
        float level = active ? burning : fill;
        if (adrenaline.Visible)
        {
            Color tint = adrenalineFill;
            float glow = 0;
            if (active)
            {
                tint = Color.Lerp(EmberDeep, EmberLight, 0.55f + 0.3f * Mathf.Sin(now * 9));
                glow = 0.55f + 0.25f * Mathf.Sin(now * 6);
            }
            adrenaline.Graphic.Tint(tint);
            adrenaline.Graphic.Set(level, glow, now);
            adrenaline.Value(inventory, currentAdrenaline, maxAdrenaline, ref lastAdrenaline, ref lastMaxAdrenaline);
        }
        bool embers = adrenaline.Visible && active;
        if (sparks.gameObject.activeSelf != embers) sparks.gameObject.SetActive(embers);
        if (embers)
        {
            // Embers rise off the burning top of the bar.
            float inner = Length - 20;
            sparks.rectTransform.anchoredPosition = new Vector2(adrenaline.X, -inner * 0.5f + inner * level + 8);
            sparks.Animate(now, Color.Lerp(EmberDeep, EmberLight, 0.5f), Thickness * 0.5f, 10);
        }
    }

    // The share of the adrenaline effect still left to burn, or 0 when none is running.
    private static float Burning(Player player)
    {
        float remaining = 0;
        foreach (var item in player.GetInventory().GetAllItems())
        {
            if (!item.m_equipped || !item.m_shared.m_fullAdrenalineSE) continue;
            var effect = player.GetSEMan().GetStatusEffect(item.m_shared.m_fullAdrenalineSE.NameHash());
            if (!effect || effect.m_ttl <= 0) continue;
            remaining = Mathf.Max(remaining, Mathf.Clamp01(1 - effect.GetDuration() / effect.m_ttl));
        }
        return remaining;
    }

    public void Dispose()
    {
        nativeEitr?.Restore();
        nativeAdrenaline?.Restore();
        ItemUi.Destroy(eitr.Root);
        ItemUi.Destroy(eitr.Label);
        ItemUi.Destroy(adrenaline.Root);
        ItemUi.Destroy(adrenaline.Label);
        if (sparks) ItemUi.Destroy(sparks.rectTransform);
    }

    private sealed class Bar
    {
        internal readonly RectTransform Root, Label;
        internal readonly RuneBar Graphic;
        private readonly CanvasGroup group, labelGroup;
        private readonly TextMeshProUGUI value;
        private readonly Image valueShade;
        private float alpha, x = float.NaN, row = float.NaN, lastTime;

        internal bool Visible => alpha > 0.001f;
        internal float X => x;

        internal Bar(RectTransform parent, string name, TMP_FontAsset font, Artwork art, Color fill, Color border, int bubbles)
        {
            // The bar is drawn lying down and stood up, so it fills from the bottom.
            Root = ItemUi.Rect(parent, name);
            HudStyle.Center(Root, FirstX, 0, Length, Thickness);
            Root.localEulerAngles = new Vector3(0, 0, 90);
            group = Root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            group.alpha = 0;
            Graphic = Root.gameObject.AddComponent<RuneBar>();
            Graphic.raycastTarget = false;
            Graphic.Palette(fill, border, bubbles);
            Root.gameObject.SetActive(false);

            Label = ItemUi.Rect(parent, name + "Value");
            Label.anchorMin = Label.anchorMax = new Vector2(0.5f, 0.5f);
            Label.pivot = new Vector2(0, 1);
            Label.sizeDelta = new Vector2(80, 16);
            labelGroup = Label.gameObject.AddComponent<CanvasGroup>();
            labelGroup.blocksRaycasts = labelGroup.interactable = false;
            valueShade = HudStyle.Shadow(Label, art, 0.8f);
            valueShade.rectTransform.anchorMin = valueShade.rectTransform.anchorMax = new Vector2(0, 0.5f);
            valueShade.rectTransform.pivot = new Vector2(0, 0.5f);
            valueShade.rectTransform.anchoredPosition = new Vector2(-14, 0);
            valueShade.rectTransform.sizeDelta = new Vector2(92, 30);
            value = ItemUi.Text(Label, "Value", font, 13, Color.Lerp(fill, Color.white, 0.45f));
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.textWrappingMode = TextWrappingModes.NoWrap;
            ItemUi.Stretch(value.rectTransform, Vector2.zero, Vector2.zero);
            Label.gameObject.SetActive(false);
        }

        internal void Place(bool show, ref float slot, ref float line, float now)
        {
            float delta = lastTime > 0 ? Mathf.Min(0.1f, now - lastTime) : 0;
            lastTime = now;
            alpha = Mathf.MoveTowards(alpha, show ? 1 : 0, delta * 5);
            if (Root.gameObject.activeSelf != Visible) Root.gameObject.SetActive(Visible);
            if (!Visible)
            {
                x = row = float.NaN;
                if (Label.gameObject.activeSelf) Label.gameObject.SetActive(false);
                return;
            }
            x = float.IsNaN(x) ? slot : Mathf.MoveTowards(x, slot, delta * 90);
            row = float.IsNaN(row) ? line : Mathf.MoveTowards(row, line, delta * 90);
            Root.anchoredPosition = new Vector2(x, 0);
            group.alpha = alpha;
            Label.anchoredPosition = new Vector2(FirstX - Thickness * 0.5f, row);
            labelGroup.alpha = alpha;
            if (show)
            {
                slot += Step;
                line -= ValueStep;
            }
        }

        internal void Value(bool shown, float current, float max, ref int cachedCurrent, ref int cachedMax)
        {
            if (Label.gameObject.activeSelf != shown) Label.gameObject.SetActive(shown);
            if (!shown) return;
            int capacity = Mathf.Max(0, Mathf.CeilToInt(max));
            int amount = Mathf.Clamp(Mathf.FloorToInt(current), 0, capacity);
            if (amount == cachedCurrent && capacity == cachedMax) return;
            cachedCurrent = amount;
            cachedMax = capacity;
            value.SetText("{0} / {1}", amount, capacity);
        }
    }
}

// A short bar with pointed ends, framed like the health gauge, with a bronze stud at each end,
// drawn lying down. It can glow and carry small bubbles rising through the filled part.
internal sealed class RuneBar : MaskableGraphic
{
    private static readonly Color32 Shade = new(10, 8, 10, 230);
    private static readonly Color32 TrackTop = new(20, 18, 26, 245);
    private static readonly Color32 TrackBottom = new(34, 30, 42, 245);
    private static readonly Color32 Bronze = new(122, 58, 30, 255);
    private static readonly Color32 BronzeLight = new(196, 128, 70, 255);
    private readonly List<Vector2> shape = new(), scratch = new(), inner = new(), outer = new();
    private Color fill = Color.white, border = Color.gray;
    private float value = -1, glow, time;
    private int bubbles;

    internal void Palette(Color fill, Color border, int bubbles)
    {
        this.fill = fill;
        this.border = border;
        this.bubbles = bubbles;
        SetVerticesDirty();
    }

    internal void Tint(Color fill)
    {
        if (this.fill == fill) return;
        this.fill = fill;
        SetVerticesDirty();
    }

    internal void Set(float value, float glow, float time)
    {
        bool moving = bubbles > 0 && value > 0;
        if (!moving && Mathf.Abs(this.value - value) < 0.001f && Mathf.Abs(this.glow - glow) < 0.01f) return;
        this.value = value;
        this.glow = glow;
        this.time = time;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var r = GetPixelAdjustedRect();
        float cy = r.center.y, half = r.height * 0.5f - 2;
        // The studs grow with the bar; the pointed ends stop just short of them.
        float stud = Mathf.Max(3.4f, half * 0.8f);
        float x0 = r.xMin + stud * 2 + 2 + half, x1 = r.xMax - stud * 2 - 2 - half;
        if (half < 2 || x1 - x0 < half * 2) return;

        if (glow > 0)
        {
            var light = fill;
            light.a = 0.6f * glow;
            var clear = light;
            clear.a = 0;
            Ring(mesh, x0, x1, cy, half + 1.5f, 7, light, clear);
        }
        Stud(mesh, r.xMin + stud + 0.1f, cy, stud);
        Stud(mesh, r.xMax - stud - 0.1f, cy, stud);
        Body(shape, x0, x1, cy, half + 2.4f);
        Fan(mesh, shape, _ => Shade);
        Body(shape, x0, x1, cy, half + 1.2f);
        var rim = Color.Lerp(border, fill, 0.25f + 0.5f * glow);
        Fan(mesh, shape, _ => rim);
        Body(shape, x0, x1, cy, half);
        Fan(mesh, shape, p => Color.Lerp(TrackBottom, TrackTop, (p.y - cy + half) / (2 * half)));

        float amount = Mathf.Clamp01(value);
        if (amount <= 0)
        {
            Notches(mesh, x0, x1, cy, half);
            return;
        }
        float end = Mathf.Lerp(x0 - half, x1 + half, amount);
        var top = Color.Lerp(fill, Color.white, 0.35f);
        var bottom = Color.Lerp(fill, Color.black, 0.35f);
        Body(shape, x0, x1, cy, half);
        Clip(shape, new Vector2(1, 0), end);
        Fan(mesh, shape, p => Color.Lerp(bottom, top, (p.y - cy + half) / (2 * half)));
        // A light seam on the leading edge.
        Body(shape, x0, x1, cy, half);
        Clip(shape, new Vector2(1, 0), end);
        Clip(shape, new Vector2(-1, 0), -(end - 2));
        var seam = Color.Lerp(fill, Color.white, 0.6f);
        Fan(mesh, shape, _ => seam);
        // A sheen along the top of the fill, like light on polished metal.
        Body(shape, x0, x1, cy, half);
        Clip(shape, new Vector2(1, 0), end - 2);
        Clip(shape, new Vector2(0, 1), cy + half * 0.8f);
        Clip(shape, new Vector2(0, -1), -(cy + half * 0.3f));
        var sheen = new Color(1, 1, 1, 0.22f);
        Fan(mesh, shape, _ => sheen);
        Bubbles(mesh, x0, end, cy, half);
        Notches(mesh, x0, x1, cy, half);
    }

    // Rune-like notches mark each quarter of the bar.
    private static void Notches(VertexHelper mesh, float x0, float x1, float cy, float half)
    {
        var dark = new Color(0, 0, 0, 0.5f);
        for (int q = 1; q < 4; q++)
        {
            float x = Mathf.Lerp(x0 - half, x1 + half, q / 4f);
            int v = mesh.currentVertCount;
            mesh.AddVert(new Vector2(x - 0.6f, cy - half), dark, Vector2.zero);
            mesh.AddVert(new Vector2(x - 0.6f, cy + half), dark, Vector2.zero);
            mesh.AddVert(new Vector2(x + 0.6f, cy + half), dark, Vector2.zero);
            mesh.AddVert(new Vector2(x + 0.6f, cy - half), dark, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
            mesh.AddTriangle(v, v + 2, v + 3);
        }
    }

    // Bubbles rise and fade inside the filled part.
    private void Bubbles(VertexHelper mesh, float x0, float end, float cy, float half)
    {
        for (int i = 0; i < bubbles; i++)
        {
            float seed = Mathf.Repeat((i + 1) * 0.618034f, 1);
            // The bar stands upright, so bubbles rise along its length towards the fill's top
            // and wobble across it.
            float phase = Mathf.Repeat(time * (0.22f + seed * 0.25f) + i * 0.37f, 1);
            float radius = (0.7f + 0.8f * seed) * Mathf.Max(1, half / 3.5f);
            float x = Mathf.Lerp(x0 - half * 0.4f, end - 3, phase);
            if (x > end - 3) continue;
            float lane = Mathf.Repeat(seed * 1.7f + i * 0.21f, 1) * 2 - 1;
            float y = cy + Mathf.Clamp(lane * (half - radius - 0.5f) + Mathf.Sin(phase * 9 + i) * 1.1f, -half + radius, half - radius);
            var tint = new Color(0.86f, 0.9f, 1, 0.75f * Mathf.Sin(phase * Mathf.PI));
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector2(x, y), tint, Vector2.zero);
            for (int k = 0; k <= 6; k++)
            {
                float a = Mathf.PI * 2 * k / 6;
                mesh.AddVert(new Vector2(x + Mathf.Cos(a) * radius, y + Mathf.Sin(a) * radius), tint, Vector2.zero);
                if (k > 0) mesh.AddTriangle(start, start + k, start + k + 1);
            }
        }
    }

    // A long hexagon whose ends point left and right.
    private static void Body(List<Vector2> points, float x0, float x1, float cy, float half)
    {
        points.Clear();
        points.Add(new Vector2(x0 - half, cy));
        points.Add(new Vector2(x0, cy + half));
        points.Add(new Vector2(x1, cy + half));
        points.Add(new Vector2(x1 + half, cy));
        points.Add(new Vector2(x1, cy - half));
        points.Add(new Vector2(x0, cy - half));
    }

    private static void Stud(VertexHelper mesh, float x, float y, float half)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector2(x, y), BronzeLight, Vector2.zero);
        mesh.AddVert(new Vector2(x - half, y), Bronze, Vector2.zero);
        mesh.AddVert(new Vector2(x, y + half), Bronze, Vector2.zero);
        mesh.AddVert(new Vector2(x + half, y), Bronze, Vector2.zero);
        mesh.AddVert(new Vector2(x, y - half), Bronze, Vector2.zero);
        for (int i = 0; i < 4; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 4);
    }

    private void Ring(VertexHelper mesh, float x0, float x1, float cy, float half, float width, Color inside, Color outside)
    {
        Body(inner, x0, x1, cy, half);
        Body(outer, x0, x1, cy, half + width);
        for (int i = 0; i < inner.Count; i++)
        {
            int j = (i + 1) % inner.Count;
            int v = mesh.currentVertCount;
            mesh.AddVert(inner[i], inside, Vector2.zero);
            mesh.AddVert(outer[i], outside, Vector2.zero);
            mesh.AddVert(outer[j], outside, Vector2.zero);
            mesh.AddVert(inner[j], inside, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
            mesh.AddTriangle(v, v + 2, v + 3);
        }
    }

    // Keeps the part of a convex polygon where dot(normal, p) <= limit.
    private void Clip(List<Vector2> points, Vector2 normal, float limit)
    {
        if (points.Count == 0) return;
        scratch.Clear();
        for (int i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            float da = Vector2.Dot(normal, a) - limit, db = Vector2.Dot(normal, b) - limit;
            if (da <= 0) scratch.Add(a);
            if (da * db < 0) scratch.Add(Vector2.Lerp(a, b, da / (da - db)));
        }
        points.Clear();
        points.AddRange(scratch);
    }

    private static void Fan(VertexHelper mesh, List<Vector2> points, Func<Vector2, Color> shade)
    {
        if (points.Count < 3) return;
        int start = mesh.currentVertCount;
        foreach (var point in points) mesh.AddVert(point, shade(point), Vector2.zero);
        for (int i = 1; i < points.Count - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
    }
}
