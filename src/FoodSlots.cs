using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// A diamond slot of the vitals block: a food, the rested bonus or the guardian power. Its outline
// doubles as the time band, a glow behind it asks for attention, and with the inventory open a
// card slides out of it with the name, the details and the time.
internal sealed class DiamondSlot
{
    internal static readonly Color32 EmptyInk = new(150, 160, 140, 120);

    private readonly RectTransform root, card;
    private readonly FoodDiamond diamond;
    private readonly Image glow;
    private readonly TextMeshProUGUI title, details, time;
    private readonly CanvasGroup cardGroup;
    private float open;

    internal readonly Image Icon;

    internal DiamondSlot(Transform parent, Artwork art, TMP_FontAsset displayFont, TMP_FontAsset font, string name,
        float x, float y, float size, float cardWidth)
    {
        root = ItemUi.Rect(parent, name);
        ItemUi.TopLeft(root, x, y, size, size);

        float height = Mathf.Min(size * 0.86f, 46);
        card = ItemUi.Rect(root, "Card");
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0, 0.5f);
        card.anchoredPosition = Vector2.zero;
        card.sizeDelta = new Vector2(cardWidth, height);
        cardGroup = card.gameObject.AddComponent<CanvasGroup>();
        cardGroup.blocksRaycasts = cardGroup.interactable = false;
        var face = ItemUi.Rect(card, "Face").gameObject.AddComponent<FoodCard>();
        face.raycastTarget = false;
        ItemUi.Stretch(face.rectTransform, Vector2.zero, Vector2.zero);
        float textLeft = size * 0.5f + 10;
        title = ItemUi.Text(card, "Name", displayFont, 15, Color.white);
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.alignment = TextAlignmentOptions.BottomLeft;
        ItemUi.TopLeft(title.rectTransform, textLeft, 1, cardWidth - textLeft - 60, height * 0.54f);
        details = ItemUi.Text(card, "Details", font, 12, ItemUi.Muted);
        details.textWrappingMode = TextWrappingModes.NoWrap;
        details.overflowMode = TextOverflowModes.Ellipsis;
        details.richText = true;
        details.alignment = TextAlignmentOptions.TopLeft;
        ItemUi.TopLeft(details.rectTransform, textLeft, 1 + height * 0.54f, cardWidth - textLeft - 34, height * 0.42f);
        time = ItemUi.Text(card, "Time", displayFont, 15, ItemUi.Accent);
        time.textWrappingMode = TextWrappingModes.NoWrap;
        time.alignment = TextAlignmentOptions.Right;
        ItemUi.TopLeft(time.rectTransform, cardWidth - 76, 0, 54, height);
        cardGroup.alpha = 0;
        card.gameObject.SetActive(false);

        glow = ItemUi.Image(root, "Glow", art["glow"]);
        HudStyle.Center(glow.rectTransform, 0, 0, size * 1.05f, size * 1.05f);
        glow.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
        glow.enabled = false;
        diamond = ItemUi.Rect(root, "Diamond").gameObject.AddComponent<FoodDiamond>();
        diamond.raycastTarget = false;
        ItemUi.Stretch(diamond.rectTransform, Vector2.zero, Vector2.zero);
        Icon = ItemUi.Image(root, "Icon");
        Icon.preserveAspect = true;
        // A large slot keeps its icon clear of the corner marks.
        float icon = size * (size > 50 ? 0.52f : 0.6f);
        HudStyle.Center(Icon.rectTransform, 0, 0, icon, icon);
    }

    // Whether the pointer is over the slot or its unfolded card.
    internal bool Contains(Vector2 screen, Camera camera) =>
        RectTransformUtility.RectangleContainsScreenPoint(root, screen, camera)
        || card.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(card, screen, camera);

    internal void SetX(float x)
    {
        if (root.anchoredPosition.x != x) root.anchoredPosition = new Vector2(x, root.anchoredPosition.y);
    }

    // band is the lit share of the outline; hot turns it yellow. glow is the strength of the halo
    // behind the slot and swell its extra size.
    internal void Show(Sprite sprite, Color ink, bool filled, float band, float hot, float glowStrength, float swell)
    {
        if (Icon.sprite != sprite) Icon.sprite = sprite;
        Icon.enabled = sprite;
        Icon.color = ink;
        diamond.Set(filled, band, hot);
        glow.enabled = glowStrength > 0.01f;
        if (!glow.enabled) return;
        glow.color = new Color(1, 0.78f, 0.25f, glowStrength);
        float scale = 1.15f + swell;
        glow.rectTransform.localScale = new Vector3(scale, scale, 1);
    }

    // The card unfolds out of the slot with the inventory open.
    internal void Card(bool expanded, string name, string text, string remaining)
    {
        open = Mathf.MoveTowards(open, expanded ? 1 : 0, Time.unscaledDeltaTime * 6);
        bool show = open > 0.001f;
        if (card.gameObject.activeSelf != show) card.gameObject.SetActive(show);
        if (!show) return;
        float eased = 1 - (1 - open) * (1 - open);
        cardGroup.alpha = eased;
        card.localScale = new Vector3(Mathf.Lerp(0.6f, 1, eased), 1, 1);
        if (!expanded) return;
        if (title.text != name) title.text = name;
        if (details.text != text) details.text = text;
        if (time.text != remaining) time.text = remaining;
    }
}

// A food in a diamond slot. The band shrinks from the right corner towards the left one as the
// food runs out; once it can be eaten again a yellow glow pulses behind it and the slot flickers.
internal sealed class FoodSlot
{
    private static readonly Color32 HealthInk = new(232, 74, 66, 255);
    private static readonly Color32 StaminaInk = new(240, 196, 84, 255);
    private static readonly Color32 EitrInk = new(96, 156, 255, 255);

    private readonly DiamondSlot slot;
    private readonly Sprite emptyIcon;
    private ItemDrop.ItemData lastItem;
    private string name = "", stats = "", time = "";
    private int lastSecond = -2, lastHealth = -1, lastStamina = -1, lastEitr = -1;

    internal const float CardWidth = 188;

    internal bool Contains(Vector2 screen, Camera camera) => slot.Contains(screen, camera);

    internal FoodSlot(Transform parent, Artwork art, TMP_FontAsset displayFont, TMP_FontAsset font, float x, float y, float size)
    {
        slot = new DiamondSlot(parent, art, displayFont, font, "FoodSlot", x, y, size, CardWidth);
        emptyIcon = art["food-empty"];
        // A slot that starts empty never sees its contents change, so it needs the empty mark up front.
        slot.Show(emptyIcon, DiamondSlot.EmptyInk, false, 0, 0, 0, 0);
    }

    internal void Refresh(Player.Food food, bool expanded)
    {
        bool has = food != null && food.m_item != null;
        if (has != (lastItem != null) || has && food.m_item != lastItem)
        {
            name = has ? Localization.instance.Localize(food.m_item.m_shared.m_name) : "";
            lastItem = has ? food.m_item : null;
            lastSecond = lastHealth = lastStamina = lastEitr = -2;
        }
        float burn = has ? Mathf.Max(1, food.m_item.m_shared.m_foodBurnTime) : 1;
        float left = has ? Mathf.Clamp01(food.m_time / burn) : 0;
        bool ready = has && food.CanEatAgain();
        float now = Time.time;
        // A slow pulse behind the slot and a quicker flicker on it while the food can be replaced.
        float pulse = ready ? 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2 * 0.9f) : 0;
        float flicker = ready ? 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2 * 2.6f) : 0;
        var ink = has ? new Color(1, 1, 1, ready ? 0.72f + 0.28f * flicker : 1) : (Color)DiamondSlot.EmptyInk;
        slot.Show(has ? food.m_item.GetIcon() : emptyIcon, ink, has, left, flicker,
            ready ? 0.35f + 0.55f * pulse : 0, 0.3f * pulse);
        if (has && expanded) Describe(food);
        slot.Card(expanded && has, name, stats, time);
    }

    private void Describe(Player.Food food)
    {
        int second = Mathf.CeilToInt(food.m_time);
        if (second != lastSecond)
        {
            lastSecond = second;
            time = StatusEffect.GetTimeString(second, false, true);
        }
        // What the food adds right now: the values shrink as it is digested.
        int health = Mathf.RoundToInt(food.m_health), stamina = Mathf.RoundToInt(food.m_stamina), eitr = Mathf.RoundToInt(food.m_eitr);
        if (health == lastHealth && stamina == lastStamina && eitr == lastEitr) return;
        lastHealth = health;
        lastStamina = stamina;
        lastEitr = eitr;
        stats = $"<color=#{ColorUtility.ToHtmlStringRGB(HealthInk)}>+{health}</color>  "
            + $"<color=#{ColorUtility.ToHtmlStringRGB(StaminaInk)}>+{stamina}</color>  "
            + $"<color=#{ColorUtility.ToHtmlStringRGB(EitrInk)}>+{eitr}</color>";
    }
}

// The diamond face: dark fill with an inner shadow, a thin frame, inner corner marks and the band.
internal sealed class FoodDiamond : MaskableGraphic
{
    private static readonly Color32 Face = new(27, 41, 43, 238);
    private static readonly Color32 Frame = new(122, 128, 104, 255);
    private static readonly Color32 Marks = new(176, 166, 120, 150);
    private static readonly Color32 Band = new(246, 222, 172, 255);
    private static readonly Color32 BandReady = new(255, 214, 84, 255);
    private static readonly Color InnerShadow = new(0, 0, 0, 0.62f);
    private bool filled;
    private float left = -1, flicker = -1;

    internal void Set(bool filled, float left, float flicker)
    {
        if (this.filled == filled && Mathf.Abs(this.left - left) < 0.002f && Mathf.Abs(this.flicker - flicker) < 0.02f) return;
        this.filled = filled;
        this.left = left;
        this.flicker = flicker;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        var c = rect.center;
        float half = Mathf.Min(rect.width, rect.height) * 0.5f;
        if (half < 4) return;

        // Fill.
        int start = mesh.currentVertCount;
        mesh.AddVert(c, Face, Vector2.zero);
        for (int i = 0; i < 4; i++) mesh.AddVert(Point(c, half, i), Face, Vector2.zero);
        for (int i = 0; i < 4; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 4);

        // The face sinks under its frame: a shadow fades in from every edge.
        var clear = InnerShadow;
        clear.a = 0;
        Gradient(mesh, c, half, 1, Mathf.Max(4, half * 0.3f), InnerShadow, clear);

        Stroke(mesh, c, half, 0, 1.4f, 0, 4, Frame);
        // Short marks inside each corner, as on carved runestones.
        float markInset = half * 0.2f;
        for (int corner = 0; corner < 4; corner++)
            Stroke(mesh, c, half, markInset, markInset + 1.3f, corner - 0.24f, corner + 0.24f, Marks);

        if (!filled || left <= 0) return;
        var band = Color.Lerp(Band, BandReady, flicker);
        float width = Mathf.Clamp(half * 0.15f, 3.2f, 5);
        float reach = 2 * Mathf.Clamp01(left);
        // Two arms from the left corner: one over the top, one under the bottom.
        Stroke(mesh, c, half, 0, width, 0, reach, band);
        Stroke(mesh, c, half, 0, width, 4 - reach, 4, band);
    }

    // Perimeter position u runs 0..4: left, top, right, bottom corners, then back to left.
    private static Vector2 Point(Vector2 c, float half, float u)
    {
        u = Mathf.Repeat(u, 4);
        int edge = Mathf.FloorToInt(u);
        float t = u - edge;
        Vector2 Corner(int i) => (i % 4) switch
        {
            0 => new Vector2(-half, 0),
            1 => new Vector2(0, half),
            2 => new Vector2(half, 0),
            _ => new Vector2(0, -half),
        };
        return c + Vector2.Lerp(Corner(edge), Corner(edge + 1), t);
    }

    // A band between two insets of the diamond along the perimeter from u0 to u1.
    private static void Stroke(VertexHelper mesh, Vector2 c, float half, float outer, float inner, float u0, float u1, Color color) =>
        Gradient(mesh, c, half, outer, inner, color, color, u0, u1);

    private static void Gradient(VertexHelper mesh, Vector2 c, float half, float outer, float inner, Color outside,
        Color inside, float u0 = 0, float u1 = 4)
    {
        float a = half - outer * 1.4142f, b = half - inner * 1.4142f;
        if (b <= 0 || u1 <= u0) return;
        float u = u0;
        while (u < u1 - 0.0001f)
        {
            float next = Mathf.Min(u1, Mathf.Floor(u + 1.0001f));
            if (next <= u) next = Mathf.Min(u1, u + 1);
            int at = mesh.currentVertCount;
            mesh.AddVert(Point(c, a, u), outside, Vector2.zero);
            mesh.AddVert(Point(c, a, next), outside, Vector2.zero);
            mesh.AddVert(Point(c, b, next), inside, Vector2.zero);
            mesh.AddVert(Point(c, b, u), inside, Vector2.zero);
            mesh.AddTriangle(at, at + 1, at + 2);
            mesh.AddTriangle(at, at + 2, at + 3);
            u = next;
        }
    }
}

// The card behind an expanded slot: a dark plate with a pointed right end and a thin rim.
internal sealed class FoodCard : MaskableGraphic
{
    private static readonly Color32 Plate = new(27, 41, 43, 236);
    private static readonly Color32 Rim = new(110, 118, 98, 255);

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var r = GetPixelAdjustedRect();
        float tip = r.height * 0.5f;
        Shape(mesh, r, tip, 0, Rim);
        Shape(mesh, r, tip, 1.4f, Plate);
    }

    private static void Shape(VertexHelper mesh, Rect r, float tip, float inset, Color color)
    {
        float x0 = r.xMin, y0 = r.yMin + inset, y1 = r.yMax - inset, x1 = r.xMax - tip;
        float tipX = r.xMax - inset * 1.4142f;
        int at = mesh.currentVertCount;
        mesh.AddVert(new Vector2(x0, y0), color, Vector2.zero);
        mesh.AddVert(new Vector2(x0, y1), color, Vector2.zero);
        mesh.AddVert(new Vector2(x1, y1), color, Vector2.zero);
        mesh.AddVert(new Vector2(tipX, r.center.y), color, Vector2.zero);
        mesh.AddVert(new Vector2(x1, y0), color, Vector2.zero);
        mesh.AddTriangle(at, at + 1, at + 2);
        mesh.AddTriangle(at, at + 2, at + 4);
        mesh.AddTriangle(at + 4, at + 2, at + 3);
    }
}
