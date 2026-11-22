using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace ValheimNewUI;

internal static class ItemUi
{
    internal static readonly Color32 Ink = new(244, 239, 225, 255);
    internal static readonly Color32 Muted = new(197, 191, 174, 255);
    internal static readonly Color32 Accent = new(225, 187, 116, 255);
    internal static readonly Color32 Warning = new(221, 75, 72, 255);

    internal static RectTransform Rect(Transform parent, string name)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        return rect;
    }

    internal static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    internal static void TopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    internal static Image Image(Transform parent, string name, Sprite sprite = null)
    {
        var graphic = Rect(parent, name).gameObject.AddComponent<Image>();
        graphic.raycastTarget = false;
        graphic.sprite = sprite;
        graphic.type = sprite && sprite.border != Vector4.zero ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
        return graphic;
    }

    internal static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, float size, Color color)
    {
        var node = Rect(parent, name).gameObject;
        node.SetActive(false);
        var label = node.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.color = color;
        label.raycastTarget = false;
        node.SetActive(true);
        return label;
    }

    internal static void Destroy(Component component)
    {
        if (!component) return;
        var obj = component.gameObject;
        obj.SetActive(false);
        Object.Destroy(obj);
    }
}

// Replaces the stock white bar with a rounded inset stroke following the bottom
// corners of the slot. The fill tracks remaining durability from left to right.
internal sealed class DurabilityEdge : MaskableGraphic
{
    private static readonly Color32 High = new(101, 211, 131, 255);
    private static readonly Color32 Middle = new(250, 200, 82, 255);
    private static readonly Color32 Low = ItemUi.Warning;
    private float value = -1;

    internal void SetItem(ItemDrop.ItemData item)
    {
        bool show = item != null && item.m_shared.m_useDurability;
        if (gameObject.activeSelf != show) gameObject.SetActive(show);
        if (!show) return;
        float next = Mathf.Clamp01(item.GetDurabilityPercentage());
        if (next == 0)
        {
            canvasRenderer.SetColor(MeterColor(0));
        }
        else if (value == 0)
        {
            canvasRenderer.SetColor(Color.white);
        }
        if (next == value) return;
        value = next;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = rectTransform.rect;
        if (rect.width <= 8 || value < 0) return;

        if (value == 0)
        {
            // Keep the full edge visible so its renderer can pulse at zero durability.
            DrawStroke(mesh, rect, 1, Color.white);
            return;
        }

        DrawStroke(mesh, rect, 1, new Color32(18, 22, 21, 225));
        DrawStroke(mesh, rect, value, MeterColor(value));
    }

    internal static Color32 MeterColor(float remaining)
    {
        if (remaining <= 0)
        {
            const float period = 0.9f;
            float flash = 0.5f + 0.5f * Mathf.Cos(Time.unscaledTime * (2 * Mathf.PI / period));
            return Color32.Lerp(Low, Color.white, flash);
        }
        return remaining <= 0.5f
            ? Color32.Lerp(Low, Middle, remaining * 2)
            : Color32.Lerp(Middle, High, (remaining - 0.5f) * 2);
    }

    private static void DrawStroke(VertexHelper mesh, Rect rect, float fraction, Color32 tint)
    {
        const float inset = 3.5f;
        const float stroke = 3.5f;
        float width = rect.width - inset * 2;
        float end = Mathf.Min(width, Mathf.Max(3, width * fraction));
        float corner = Mathf.Min(6, width * 0.5f);
        float cap = Mathf.Min(1.4f, end * 0.5f);
        int stepCount = Mathf.CeilToInt(end / 1.2f);
        int first = mesh.currentVertCount;

        for (int i = 0; i <= stepCount; i++)
        {
            float x = end * i / stepCount;
            float dx = Mathf.Max(0, corner - Mathf.Min(x, width - x));
            float curve = corner - Mathf.Sqrt(Mathf.Max(0, corner * corner - dx * dx));
            float tip = Mathf.Min(x, end - x);
            float round = tip >= cap ? 0 : cap - Mathf.Sqrt(Mathf.Max(0, cap * cap - (cap - tip) * (cap - tip)));
            float y = rect.yMin + inset + curve;
            mesh.AddVert(new Vector3(rect.xMin + inset + x, y + round), tint, Vector2.zero);
            mesh.AddVert(new Vector3(rect.xMin + inset + x, y + stroke - round), tint, Vector2.zero);
            if (i == 0) continue;
            int v = first + i * 2;
            mesh.AddTriangle(v - 2, v - 1, v + 1);
            mesh.AddTriangle(v - 2, v + 1, v);
        }
    }
}

// Used for decorative replacements on existing interactive objects. Alpha zero
// keeps their native raycast handlers and controller focus available.
internal sealed class HiddenGraphics
{
    private readonly CanvasGroup group;
    private readonly bool added;
    private readonly float alpha;
    private readonly bool raycasts;

    internal HiddenGraphics(GameObject target)
    {
        group = target.GetComponent<CanvasGroup>();
        added = !group;
        if (added) group = target.AddComponent<CanvasGroup>();
        alpha = group.alpha;
        raycasts = group.blocksRaycasts;
        group.alpha = 0;
    }

    internal void SetRaycasts(bool value)
    {
        if (group && group.blocksRaycasts != value) group.blocksRaycasts = value;
    }

    internal void Restore()
    {
        if (!group) return;
        group.alpha = alpha;
        group.blocksRaycasts = raycasts;
        if (added) Object.Destroy(group);
    }
}
