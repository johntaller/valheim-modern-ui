using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

// A ping reads as a pulse on the spot with the name of whoever sent it underneath, instead of the
// game's blue "PING" text. The game still places, clamps and removes its marker; the pulse and the
// name ride on that marker and take over its look.
[HarmonyPatch(typeof(Chat), "AddInworldText")]
internal static class PingMarkerPatch
{
    private static void Postfix(Chat __instance, long __1, Talker.Type __3)
    {
        if (!__instance) return;
        foreach (var text in __instance.WorldTexts)
        {
            if (text == null || text.m_talkerID != __1 || !text.m_gui) continue;
            var marker = text.m_gui.GetComponent<PingMarker>();
            // Chat reuses the same WorldTextInstance for different messages from this sender.
            if (!NativeMessagePatch.Enabled || __3 != Talker.Type.Ping || text.m_type != Talker.Type.Ping)
            {
                if (marker) marker.Restore();
                return;
            }
            if (!marker) marker = text.m_gui.AddComponent<PingMarker>();
            marker.Restart(text, __instance.m_worldTextTTL);
            return;
        }
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
    private float started, lifetime;

    internal void Restart(Chat.WorldTextInstance text, float ttl)
    {
        if (!pulse) Build(text);
        group.gameObject.SetActive(true);
        started = Time.time;
        lifetime = Mathf.Max(1, ttl);
        string name = text.m_name ?? "";
        if (label.text != name) label.text = name;
        var size = label.GetPreferredValues(name);
        shade.rectTransform.sizeDelta = new Vector2(Mathf.Min(size.x, 240) + 44, 34);
        // The game sets its text on every ping; it stays hidden behind the pulse.
        Mute(text);
    }

    private void Build(Chat.WorldTextInstance text)
    {
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
        var font = text.m_textMeshField ? text.m_textMeshField.font : null;
        label = ItemUi.Text(root, "Sender", HudStyle.BrandLight(font), 17, ItemUi.Ink);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        HudStyle.Center(label.rectTransform, 0, -Size * 0.5f - 4, 240, 22);
    }

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
        if (group) group.gameObject.SetActive(false);
        foreach (var graphic in muted)
            if (graphic) graphic.enabled = true;
        muted.Clear();
    }

    private void OnDisable() => Restore();

    private void Update()
    {
        if (!group || !group.gameObject.activeSelf) return;
        if (!NativeMessagePatch.Enabled) { Restore(); return; }
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
