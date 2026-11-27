using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// Change only the lookup in AddInworldText. Valheim still handles routing, projection, map pins
// and expiry. Keep one world ping per sender, independent of that sender's normal chat bubble.
[HarmonyPatch(typeof(Chat), "AddInworldText", typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type), typeof(UserInfo), typeof(string))]
internal static class PingWorldTextPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var lookup = AccessTools.Method(typeof(Chat), "FindExistingWorldText", new[] { typeof(long) });
        var replacement = AccessTools.Method(typeof(PingWorldTextPatch), nameof(FindWorldText));
        foreach (var instruction in instructions)
        {
            if (lookup != null && instruction.Calls(lookup))
            {
                // Argument 4 (after this, go, sender ID and position) is the message type.
                var type = new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
                type.labels.AddRange(instruction.labels);
                type.blocks.AddRange(instruction.blocks);
                yield return type;
                yield return new CodeInstruction(OpCodes.Call, replacement);
            }
            else yield return instruction;
        }
    }

    private static Chat.WorldTextInstance FindWorldText(Chat chat, long sender, Talker.Type type)
    {
        var texts = chat.WorldTexts;
        bool ping = type == Talker.Type.Ping;
        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (text.m_talkerID != sender) continue;
            if (NativeMessagePatch.Enabled)
            {
                if ((text.m_type == Talker.Type.Ping) == ping) return text;
                continue;
            }
            // Back to the native sender-only lookup. Retire the extra entry created by the split.
            for (int j = texts.Count - 1; j > i; j--)
            {
                var extra = texts[j];
                if (extra.m_talkerID != sender) continue;
                texts.RemoveAt(j);
                if (!extra.m_gui || extra.m_gui == text.m_gui) continue;
                extra.m_gui.SetActive(false);
                Object.Destroy(extra.m_gui);
            }
            return text;
        }
        return null;
    }
}

[HarmonyPatch(typeof(Chat), "UpdateWorldTextField", typeof(Chat.WorldTextInstance))]
internal static class PingMarkerPatch
{
    private static void Postfix(Chat __instance, Chat.WorldTextInstance __0)
    {
        if (!__0.m_gui) return;
        var marker = __0.m_gui.GetComponent<PingMarker>();
        if (!NativeMessagePatch.Enabled || __0.m_type != Talker.Type.Ping || !__0.m_textMeshField)
        {
            if (marker) marker.Restore();
            return;
        }
        if (!marker) marker = __0.m_gui.AddComponent<PingMarker>();
        marker.Restart(__0, __instance.m_worldTextTTL);
    }
}

[HarmonyPatch(typeof(Chat), "UpdateWorldTexts", typeof(float))]
internal static class PingNamePlacement
{
    private static readonly List<Rect> Placed = new();

    private static void Postfix(Chat __instance)
    {
        if (!NativeMessagePatch.Enabled) return;
        var canvas = __instance.GetComponentInParent<Canvas>();
        if (!canvas) return;
        var root = canvas.rootCanvas;
        var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        Placed.Clear();
        foreach (var text in __instance.WorldTexts)
            if (text.m_type == Talker.Type.Ping && text.m_gui && text.m_gui.TryGetComponent<PingMarker>(out var marker))
                marker.PlaceName(camera, Placed);
    }
}

internal sealed class PingMarker : MonoBehaviour
{
    private const float Size = 76;
    private PingPulse pulse;
    private TextMeshProUGUI label;
    private SoftShade shade;
    private CanvasGroup group;
    private readonly List<Graphic> muted = new();
    private float started, lifetime, nameY;
    private RectTransform nativeRect;
    private Vector2 nativeSize;
    private Chat.WorldTextInstance entry;

    internal void Restart(Chat.WorldTextInstance text, float ttl)
    {
        if (!pulse) Build(text.m_textMeshField.font);
        if (entry == null) nativeSize = nativeRect.sizeDelta;
        entry = text;
        group.gameObject.SetActive(true);
        started = Time.time;
        lifetime = Mathf.Max(1, ttl);
        string name = text.m_name ?? "";
        if (label.text != name) label.text = name;
        float width = Mathf.Clamp(Mathf.Ceil(label.GetPreferredValues(name).x) + 4, 30, 420);
        float height = Mathf.Clamp(Mathf.Ceil(label.GetPreferredValues(name, width, Mathf.Infinity).y) + 6, 28, 100);
        nameY = -Size * 0.5f - (height - 28) / 2 - 4;
        HudStyle.Center(label.rectTransform, 0, nameY, width, height);
        HudStyle.Center(shade.rectTransform, 0, nameY, width + 34, height + 6);
        // Chat clamps the prefab's rect, not the custom children. Include the name below the
        // pulse in those bounds so pings at the screen edges don't cut letters off.
        nativeRect.sizeDelta = nativeSize;
        nativeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(nativeRect.rect.width, width + 34));
        nativeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(nativeRect.rect.height, -2 * nameY + height + 6));
        // The game sets its text on every ping; it stays hidden behind the pulse.
        Mute(text);
    }

    private void Build(TMP_FontAsset font)
    {
        nativeRect = (RectTransform)transform;
        var root = ItemUi.Rect(transform, "NewUI_Ping");
        HudStyle.Center(root, 0, 0, Size, Size);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        shade = ItemUi.Rect(root, "NameShade").gameObject.AddComponent<SoftShade>();
        shade.raycastTarget = false;
        HudStyle.Center(shade.rectTransform, 0, -Size * 0.5f - 4, 120, 34);
        pulse = ItemUi.Rect(root, "Pulse").gameObject.AddComponent<PingPulse>();
        pulse.raycastTarget = false;
        ItemUi.Stretch(pulse.rectTransform, Vector2.zero, Vector2.zero);
        // Nicknames are not headings: use the chat font and its fallback glyphs without markup.
        label = ItemUi.Text(root, "Sender", font, 17, ItemUi.Ink);
        label.alignment = TextAlignmentOptions.Center;
        label.richText = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12;
        label.fontSizeMax = 17;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Truncate;
    }

    internal void PlaceName(Camera camera, List<Rect> placed)
    {
        if (!group || !group.gameObject.activeInHierarchy) return;
        var rect = (RectTransform)group.transform;
        var home = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector2(0, nameY)));
        var corner = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(new Vector2(
            shade.rectTransform.rect.width / 2, nameY + shade.rectTransform.rect.height / 2)));
        var half = new Vector2(Mathf.Abs(corner.x - home.x), Mathf.Abs(corner.y - home.y));
        var center = new Vector2(ClampCenter(home.x, half.x, Screen.width), ClampCenter(home.y, half.y, Screen.height));
        var box = new Rect(center - half, half * 2);
        // Keep pulses at their map positions. Only move names when their backgrounds overlap.
        var below = box;
        for (int i = placed.Count - 1; i >= 0; i--)
            if (below.Overlaps(placed[i])) below.y = placed[i].yMin - box.height - 2;
        if (below.yMin >= 2) box = below;
        else
        {
            var above = box;
            foreach (var other in placed)
                if (above.Overlaps(other)) above.y = other.yMax + 2;
            if (above.yMax <= Screen.height - 2) box = above;
        }
        int index = placed.Count;
        while (index > 0 && placed[index - 1].yMin > box.yMin) index--;
        placed.Insert(index, box);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, box.center, camera, out var point))
            label.rectTransform.anchoredPosition = shade.rectTransform.anchoredPosition = point;
    }

    private static float ClampCenter(float center, float half, float size) =>
        half * 2 + 4 >= size ? size / 2 : Mathf.Clamp(center, half + 2, size - half - 2);

    private void Mute(Chat.WorldTextInstance text)
    {
        foreach (var graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.transform.IsChildOf(group.transform)) continue;
            MuteGraphic(graphic);
        }
        MuteGraphic(text.m_textMeshField);
    }

    private void MuteGraphic(Graphic graphic)
    {
        if (!graphic || !graphic.enabled) return;
        if (!muted.Contains(graphic)) muted.Add(graphic);
        graphic.enabled = false;
    }

    internal void Restore()
    {
        if (entry == null) return;
        entry = null;
        if (group) group.gameObject.SetActive(false);
        if (nativeRect) nativeRect.sizeDelta = nativeSize;
        foreach (var graphic in muted)
            if (graphic) graphic.enabled = true;
        muted.Clear();
    }

    private void OnDisable() => Restore();

    private void Update()
    {
        if (!group || !group.gameObject.activeSelf) return;
        if (!NativeMessagePatch.Enabled || entry?.m_type != Talker.Type.Ping) { Restore(); return; }
        float age = Time.time - started;
        // The marker fades out over its last second.
        group.alpha = Mathf.Clamp01((lifetime - age) / 1f) * Mathf.Clamp01(age * 6);
        pulse.Animate(age);
    }
}

// Rings that spread out of a bright core again and again, over a soft dark spot that keeps them
// readable on snow and sky.
internal sealed class PingPulse : MaskableGraphic
{
    private const float Period = 1.3f;
    private static readonly Color32 Ring = new(246, 214, 140, 255);
    private static readonly Color32 Core = new(255, 244, 214, 255);
    private float age;

    internal void Animate(float time)
    {
        age = time;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        var c = rect.center;
        float reach = Mathf.Min(rect.width, rect.height) * 0.5f;
        Disc(mesh, c, reach * 0.55f, new Color(0, 0, 0, 0.42f), new Color(0, 0, 0, 0));
        for (int i = 0; i < 3; i++)
        {
            float phase = Mathf.Repeat(age / Period - i / 3f, 1);
            // Rings only start once the marker is up, not mid-flight.
            if (age < i * Period / 3f) continue;
            float radius = Mathf.Lerp(5, reach - 2, 1 - (1 - phase) * (1 - phase));
            var tint = (Color)Ring;
            tint.a = (1 - phase) * 0.9f;
            Band(mesh, c, radius, Mathf.Lerp(2.6f, 1, phase), tint);
        }
        // The core: a diamond that beats with each new ring.
        float beat = 1 + 0.25f * Mathf.Pow(1 - Mathf.Repeat(age / (Period / 3f), 1), 3);
        Diamond(mesh, c, 7.5f * beat, new Color(0.08f, 0.05f, 0.03f, 0.85f));
        Diamond(mesh, c, 5.5f * beat, Core);
    }

    private static void Disc(VertexHelper mesh, Vector2 c, float radius, Color inside, Color outside)
    {
        const int sides = 40;
        int start = mesh.currentVertCount;
        mesh.AddVert(c, inside, Vector2.zero);
        for (int i = 0; i <= sides; i++)
        {
            float a = Mathf.PI * 2 * i / sides;
            mesh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, outside, Vector2.zero);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }

    private static void Band(VertexHelper mesh, Vector2 c, float radius, float half, Color tint)
    {
        const int sides = 48;
        int start = mesh.currentVertCount;
        for (int i = 0; i <= sides; i++)
        {
            float a = Mathf.PI * 2 * i / sides;
            var unit = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            mesh.AddVert(c + unit * (radius - half), tint, Vector2.zero);
            mesh.AddVert(c + unit * (radius + half), tint, Vector2.zero);
            if (i == 0) continue;
            int at = start + i * 2;
            mesh.AddTriangle(at - 2, at, at + 1);
            mesh.AddTriangle(at - 2, at + 1, at - 1);
        }
    }

    private static void Diamond(VertexHelper mesh, Vector2 c, float half, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(c + new Vector2(0, half), tint, Vector2.zero);
        mesh.AddVert(c + new Vector2(half, 0), tint, Vector2.zero);
        mesh.AddVert(c + new Vector2(0, -half), tint, Vector2.zero);
        mesh.AddVert(c + new Vector2(-half, 0), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}

// A soft dark ellipse behind a line of text.
internal sealed class SoftShade : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var rect = GetPixelAdjustedRect();
        var c = rect.center;
        var radius = rect.size * 0.5f;
        const int sides = 40;
        int start = mesh.currentVertCount;
        mesh.AddVert(c, new Color(0, 0, 0, 0.62f), Vector2.zero);
        for (int i = 0; i <= sides; i++)
        {
            float a = Mathf.PI * 2 * i / sides;
            mesh.AddVert(c + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y), new Color(0, 0, 0, 0), Vector2.zero);
            if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
