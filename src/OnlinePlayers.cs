using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class OnlinePlayers : IDisposable
{
    private readonly List<Peer> peers = new();
    // Static so the pins outlive a rebuilt HUD; they are cleared when the session ends.
    private static readonly HashSet<string> pinned = new(StringComparer.Ordinal);
    private readonly RectTransform root;
    private readonly HudRow[] rows = new HudRow[5];
    private readonly PlayerCardSync cards;
    private float nextRefresh;

    internal IReadOnlyList<Peer> Peers => peers;

    internal OnlinePlayers(RectTransform parent, Artwork art, TMP_FontAsset font, PlayerCardSync cards)
    {
        this.cards = cards;
        root = ItemUi.Rect(parent, "NearbyPlayers");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        root.sizeDelta = new Vector2(228, rows.Length * 51);
        for (int i = 0; i < rows.Length; i++) rows[i] = new HudRow(root, art, font, i);
    }

    internal void Refresh(Player player, bool visible, float range)
    {
        float mapSize = Mathf.Clamp(((RectTransform)root.parent).rect.width * 0.15f, 170, 250);
        root.anchoredPosition = new Vector2(-24, -mapSize - 344);
        root.gameObject.SetActive(visible);
        // Pins belong to the session: a dead player has no character for a moment, but keeps them.
        if (!ZNet.instance)
        {
            pinned.Clear();
            peers.Clear();
        }
        if (!player)
        {
            nextRefresh = 0;
            foreach (var row in rows) row.Hide();
            return;
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.35f;
            ReadPeers(player, range);
        }
        int shown = 0;
        if (visible)
        {
            foreach (var peer in peers)
            {
                if (!peer.InRange && !peer.Pinned) continue;
                rows[shown++].Refresh(peer);
                if (shown == rows.Length) break;
            }
        }
        while (shown < rows.Length) rows[shown++].Hide();
    }

    private void ReadPeers(Player player, float range)
    {
        peers.Clear();
        var net = ZNet.instance;
        var scene = ZNetScene.instance;
        if (!net || !scene) return;
        var localId = player.GetZDOID();
        foreach (var info in net.GetPlayerList())
        {
            if (info.m_characterID == localId || string.IsNullOrEmpty(info.m_name)) continue;
            var card = cards.Get(info.m_characterID);
            if (card == null) continue;
            var instance = scene.FindInstance(info.m_characterID);
            var remote = instance ? instance.GetComponent<Player>() : null;
            if (remote == player) continue;
            // A private map position remains private while the character is out of streaming range.
            Vector3? position = remote ? remote.transform.position : info.m_publicPosition ? info.m_position : null;
            float? distance = position.HasValue ? Vector3.Distance(player.transform.position, position.Value) : null;
            string platform = info.m_userInfo.m_id.ToString();
            string key = platform + "|" + info.m_name;
            Heightmap.Biome? biome = position.HasValue ? Heightmap.FindBiome(position.Value) : null;
            peers.Add(new Peer(key, info.m_name, distance, biome, card,
                pinned.Contains(key), distance.HasValue && distance.Value <= range));
        }
        peers.Sort((left, right) =>
        {
            if (left.Pinned != right.Pinned) return left.Pinned ? -1 : 1;
            int distance = (left.Distance ?? float.MaxValue).CompareTo(right.Distance ?? float.MaxValue);
            return distance != 0 ? distance : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        });
    }

    internal void TogglePin(string key)
    {
        if (!pinned.Remove(key)) pinned.Add(key);
        nextRefresh = 0;
    }

    public void Dispose()
    {
        ItemUi.Destroy(root);
    }

    internal sealed class Peer
    {
        internal readonly string Key, Name;
        internal readonly Texture2D Portrait;
        internal readonly Skills.SkillType Skill;
        internal readonly float? Distance;
        internal readonly Heightmap.Biome? Biome;
        internal readonly float Health, MaxHealth, Armor, SkillLevel;
        internal readonly bool Dead, Pinned, InRange;

        internal Peer(string key, string name, float? distance, Heightmap.Biome? biome,
            PlayerCardSync.Snapshot card, bool pinned, bool inRange)
        {
            Key = key;
            Name = name;
            Portrait = card.Portrait;
            Skill = card.Skill;
            SkillLevel = card.Level;
            Armor = card.Armor;
            Distance = distance;
            Biome = biome;
            Health = card.Health;
            MaxHealth = card.MaxHealth;
            Dead = card.Dead;
            Pinned = pinned;
            InRange = inRange;
        }
    }

    private sealed class HudRow
    {
        private readonly RectTransform row;
        private readonly Image well, silhouette, fill;
        private readonly RawImage avatar;
        private readonly TextMeshProUGUI name, health;

        internal HudRow(Transform parent, Artwork art, TMP_FontAsset font, int index)
        {
            row = ItemUi.Rect(parent, "Player");
            ItemUi.TopLeft(row, 0, index * 49, 228, 45);
            ItemUi.TopLeft(HudStyle.Shadow(row, art, 0.9f).rectTransform, -22, -10, 265, 65);
            well = ItemUi.Image(row, "PortraitWell", art["slot"]);
            ItemUi.TopLeft(well.rectTransform, 2, 2, 41, 41);
            silhouette = ItemUi.Image(well.transform, "PortraitPlaceholder", art["player-portrait"]);
            silhouette.color = ItemUi.Muted;
            ItemUi.Stretch(silhouette.rectTransform, Vector2.one * 5, Vector2.one * -5);
            avatar = ItemUi.Rect(well.transform, "Portrait").gameObject.AddComponent<RawImage>();
            avatar.raycastTarget = false;
            ItemUi.Stretch(avatar.rectTransform, Vector2.one * 2, Vector2.one * -2);
            name = MenuStyle.Label(row, "Name", HudStyle.DisplayFont(font), "", 17, ItemUi.Ink);
            name.richText = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            ItemUi.TopLeft(name.rectTransform, 50, 0, 172, 20);
            health = MenuStyle.Label(row, "Health", font, "", 13, ItemUi.Muted);
            ItemUi.TopLeft(health.rectTransform, 50, 20, 172, 17);
            var track = ItemUi.Image(row, "HealthTrack", art["meter-fill"]);
            track.color = new Color32(47, 28, 26, 225);
            ItemUi.TopLeft(track.rectTransform, 50, 39, 169, 3);
            fill = ItemUi.Image(track.transform, "Health", art["meter-fill"]);
            fill.color = ItemUi.Warning;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            ItemUi.Stretch(fill.rectTransform, Vector2.zero, Vector2.zero);
        }

        internal void Refresh(Peer player)
        {
            row.gameObject.SetActive(true);
            // The display font has no symbol glyphs, and a fallback glyph's taller line would hide the whole name.
            name.text = player.Name;
            well.color = player.Pinned ? ItemUi.Accent : Color.white;
            health.text = player.Dead ? MenuStyle.Translate("Dead", "Погиб")
                : player.Health >= 0 ? $"{Mathf.CeilToInt(player.Health)} / " +
                  (player.MaxHealth > 0 ? Mathf.CeilToInt(player.MaxHealth).ToString() : "—") : "—";
            health.color = player.Dead ? (Color)ItemUi.Warning : ItemUi.Muted;
            fill.fillAmount = !player.Dead && player.MaxHealth > 0 ? Mathf.Clamp01(player.Health / player.MaxHealth) : 0;
            avatar.texture = player.Portrait;
            avatar.enabled = player.Portrait;
            avatar.color = player.Dead ? new Color32(115, 110, 110, 255) : Color.white;
            silhouette.enabled = !avatar.enabled;
        }

        internal void Hide() => row.gameObject.SetActive(false);

    }
}

internal sealed class PortraitCamera : MonoBehaviour
{
    private const int StudioLayer = 28;
    private const int Supersample = 4;
    private const float HeadDrop = 0.1f;
    private static readonly Vector3 Studio = new(0, 6000, 0);
    private static readonly Color32 Backdrop = new(18, 23, 27, 255);
    private readonly List<Mesh> bakedMeshes = new();
    private Camera camera;
    private Light lightSource;

    private void Awake()
    {
        camera = gameObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 1 << StudioLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Backdrop;
        camera.orthographic = true;
        camera.orthographicSize = 0.19f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 1.1f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.renderingPath = RenderingPath.Forward;
        lightSource = gameObject.AddComponent<Light>();
        lightSource.type = LightType.Point;
        lightSource.intensity = 2.2f;
        lightSource.range = 3;
        lightSource.cullingMask = camera.cullingMask;
        lightSource.enabled = false;
    }

    internal byte[] Capture(Player player, int size)
    {
        var visual = player.GetVisual();
        if (!visual) return null;
        var subject = new GameObject("NewUI_PortraitSubject");
        int large = size * Supersample;
        var target = RenderTexture.GetTemporary(large, large, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture result = null;
        try
        {
            Vector3 eye = player.GetEyePoint();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                if (renderer.enabled)
                    CopyMesh(renderer, eye, subject.transform);
            if (subject.transform.childCount == 0) return null;

            Vector3 forward = visual.transform.forward;
            Vector3 center = Studio - Vector3.up * HeadDrop;
            lightSource.enabled = true;
            camera.transform.SetPositionAndRotation(center + forward * 0.7f, Quaternion.LookRotation(-forward, Vector3.up));
            camera.targetTexture = target;
            camera.Render();
            result = Downsample(target, size);
            RenderTexture.active = result;
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            var bytes = image.GetRawTextureData();
            bool subjectVisible = false;
            for (int i = 0; i < bytes.Length; i += 4)
            {
                int contrast = Mathf.Abs(bytes[i] - Backdrop.r) + Mathf.Abs(bytes[i + 1] - Backdrop.g)
                    + Mathf.Abs(bytes[i + 2] - Backdrop.b);
                subjectVisible |= contrast > 24;
                bytes[i + 3] = 255;
            }
            return subjectVisible ? bytes : null;
        }
        finally
        {
            lightSource.enabled = false;
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            if (result && result != target) RenderTexture.ReleaseTemporary(result);
            subject.SetActive(false);
            Destroy(subject);
            Destroy(image);
            foreach (var mesh in bakedMeshes) Destroy(mesh);
            bakedMeshes.Clear();
        }
    }

    // Halving with bilinear sampling averages each 2x2 block, so the chain gives a clean box filter.
    private static RenderTexture Downsample(RenderTexture source, int size)
    {
        var current = source;
        while (current.width > size)
        {
            var next = RenderTexture.GetTemporary(current.width / 2, current.height / 2, 0, RenderTextureFormat.ARGB32);
            next.filterMode = FilterMode.Bilinear;
            current.filterMode = FilterMode.Bilinear;
            Graphics.Blit(current, next);
            if (current != source) RenderTexture.ReleaseTemporary(current);
            current = next;
        }
        return current;
    }

    private void CopyMesh(Renderer source, Vector3 eye, Transform parent)
    {
        Mesh mesh;
        Vector3 scale = source.transform.lossyScale;
        if (source is SkinnedMeshRenderer skinned)
        {
            if (!skinned.sharedMesh) return;
            mesh = new Mesh();
            // The baked mesh is already posed at world size; the renderer's own scale would enlarge it again.
            skinned.BakeMesh(mesh);
            bakedMeshes.Add(mesh);
            scale = Vector3.one;
        }
        else if (source is MeshRenderer && source.TryGetComponent<MeshFilter>(out var filter))
            mesh = filter.sharedMesh;
        else return;
        if (!mesh) return;

        var part = new GameObject(source.name) { layer = StudioLayer };
        part.transform.SetParent(parent);
        part.transform.SetPositionAndRotation(Studio + source.transform.position - eye, source.transform.rotation);
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var properties = new MaterialPropertyBlock();
        source.GetPropertyBlock(properties);
        if (!properties.isEmpty) renderer.SetPropertyBlock(properties);
    }
}
