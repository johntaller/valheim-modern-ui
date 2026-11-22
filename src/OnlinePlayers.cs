using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class OnlinePlayers : IDisposable
{
    private readonly List<Peer> peers = new();
    private readonly HashSet<string> pinned = new(StringComparer.Ordinal);
    private readonly RectTransform root;
    private readonly HudRow[] rows = new HudRow[5];
    private readonly PortraitCamera portraits;
    private float nextRefresh;

    internal IReadOnlyList<Peer> Peers => peers;

    internal OnlinePlayers(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        root = ItemUi.Rect(parent, "NearbyPlayers");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        root.sizeDelta = new Vector2(228, rows.Length * 51);
        portraits = new GameObject("NewUI_PortraitCamera").AddComponent<PortraitCamera>();
        for (int i = 0; i < rows.Length; i++) rows[i] = new HudRow(root, art, font, i);
    }

    internal void Refresh(Player player, bool visible, float range)
    {
        float mapSize = Mathf.Clamp(((RectTransform)root.parent).rect.width * 0.15f, 170, 250);
        root.anchoredPosition = new Vector2(-24, -mapSize - 144);
        root.gameObject.SetActive(visible);
        if (!player)
        {
            pinned.Clear();
            peers.Clear();
            nextRefresh = 0;
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
                rows[shown++].Refresh(peer, portraits);
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
            var instance = scene.FindInstance(info.m_characterID);
            var remote = instance ? instance.GetComponent<Player>() : null;
            if (remote == player) continue;
            var zdo = info.m_characterID.IsNone() ? null : ZDOMan.instance?.GetZDO(info.m_characterID);
            // Use the in-world character when it is streamed and the public map
            // position when it isn't. A private position stays private at range.
            Vector3? position = remote ? remote.transform.position : info.m_publicPosition ? info.m_position : null;
            float? distance = position.HasValue ? Vector3.Distance(player.transform.position, position.Value) : null;
            float health = remote ? remote.GetHealth() : zdo?.GetFloat(ZDOVars.s_health, -1) ?? -1;
            float maximum = remote ? remote.GetMaxHealth() : zdo?.GetFloat(ZDOVars.s_maxHealth, -1) ?? -1;
            bool dead = (remote && remote.IsDead()) || zdo?.GetBool(ZDOVars.s_dead, false) == true || health == 0 && maximum > 0;
            string platform = info.m_userInfo.m_id.ToString();
            string key = platform + "|" + info.m_name;
            Heightmap.Biome? biome = position.HasValue ? Heightmap.FindBiome(position.Value) : null;
            peers.Add(new Peer(key, info.m_name, remote, distance, biome, health, maximum, dead,
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
        foreach (var row in rows) row.Dispose();
        if (portraits) UnityEngine.Object.Destroy(portraits.gameObject);
        ItemUi.Destroy(root);
    }

    internal sealed class Peer
    {
        internal readonly string Key, Name;
        internal readonly Player Character;
        internal readonly float? Distance;
        internal readonly Heightmap.Biome? Biome;
        internal readonly float Health, MaxHealth;
        internal readonly bool Dead, Pinned, InRange;

        internal Peer(string key, string name, Player character, float? distance, Heightmap.Biome? biome,
            float health, float maxHealth, bool dead, bool pinned, bool inRange)
        {
            Key = key;
            Name = name;
            Character = character;
            Distance = distance;
            Biome = biome;
            Health = health;
            MaxHealth = maxHealth;
            Dead = dead;
            Pinned = pinned;
            InRange = inRange;
        }
    }

    private sealed class HudRow
    {
        private readonly RectTransform row;
        private readonly Image silhouette, fill;
        private readonly RawImage avatar;
        private readonly TextMeshProUGUI name, health;
        private RenderTexture portrait;
        private string previous;
        private float nextDraw;

        internal HudRow(Transform parent, Artwork art, TMP_FontAsset font, int index)
        {
            row = ItemUi.Rect(parent, "Player");
            ItemUi.TopLeft(row, 0, index * 49, 228, 45);
            ItemUi.TopLeft(HudStyle.Shadow(row, art, 0.9f).rectTransform, -22, -10, 265, 65);
            var well = ItemUi.Image(row, "PortraitWell", art["slot"]);
            ItemUi.TopLeft(well.rectTransform, 2, 2, 41, 41);
            silhouette = ItemUi.Image(well.transform, "PortraitPlaceholder", art["player-portrait"]);
            silhouette.color = ItemUi.Muted;
            ItemUi.Stretch(silhouette.rectTransform, Vector2.one * 5, Vector2.one * -5);
            avatar = ItemUi.Rect(well.transform, "Portrait").gameObject.AddComponent<RawImage>();
            avatar.raycastTarget = false;
            ItemUi.Stretch(avatar.rectTransform, Vector2.one * 2, Vector2.one * -2);
            name = MenuStyle.Label(row, "Name", font, "", 16, ItemUi.Ink);
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

        internal void Refresh(Peer player, PortraitCamera camera)
        {
            row.gameObject.SetActive(true);
            if (previous != player.Key)
            {
                previous = player.Key;
                nextDraw = 0;
                avatar.enabled = false;
            }
            name.text = (player.Pinned ? "◆  " : "") + player.Name;
            health.text = player.Dead ? MenuStyle.Translate("Dead", "Погиб")
                : player.Health >= 0 ? $"{Mathf.CeilToInt(player.Health)} / " +
                  (player.MaxHealth > 0 ? Mathf.CeilToInt(player.MaxHealth).ToString() : "—") : "—";
            health.color = player.Dead ? (Color)ItemUi.Warning : ItemUi.Muted;
            fill.fillAmount = !player.Dead && player.MaxHealth > 0 ? Mathf.Clamp01(player.Health / player.MaxHealth) : 0;
            if (!player.Character || player.Dead)
            {
                avatar.enabled = false;
                silhouette.enabled = true;
                return;
            }
            if (Time.unscaledTime >= nextDraw)
            {
                nextDraw = Time.unscaledTime + 1.5f;
                if (!portrait) portrait = new RenderTexture(96, 96, 16, RenderTextureFormat.ARGB32);
                avatar.texture = portrait;
                camera.Request(player.Character, portrait, avatar);
            }
            silhouette.enabled = !avatar.enabled;
        }

        internal void Hide() => row.gameObject.SetActive(false);

        internal void Dispose()
        {
            if (portrait) UnityEngine.Object.Destroy(portrait);
        }
    }
}

internal sealed class PortraitCamera : MonoBehaviour
{
    private readonly List<(Player Player, RenderTexture Target, RawImage Output)> requests = new();
    private Camera camera;
    private CommandBuffer commands;

    private void Awake()
    {
        camera = gameObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 0;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.orthographicSize = 0.28f;
        camera.nearClipPlane = 0.19f;
        camera.farClipPlane = 0.98f;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.renderingPath = RenderingPath.Forward;
        commands = new CommandBuffer { name = "Valheim New UI portraits" };
        camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commands);
    }

    internal void Request(Player player, RenderTexture target, RawImage output) => requests.Add((player, target, output));

    private void Update()
    {
        // Render outside the canvas callback so the portrait pass cannot reenter HUD layout.
        foreach (var request in requests)
        {
            var player = request.Player;
            if (!player || player.IsDead() || !request.Target || !request.Output || !request.Output.gameObject.activeInHierarchy)
                continue;
            var visual = player.GetVisual();
            if (!visual) continue;
            Vector3 forward = visual.transform.forward;
            Vector3 eye = player.GetEyePoint();
            Vector3 center = eye - Vector3.up * 0.025f;
            camera.transform.SetPositionAndRotation(center + forward * 0.68f, Quaternion.LookRotation(-forward, Vector3.up));
            commands.Clear();
            int count = 0;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is not (SkinnedMeshRenderer or MeshRenderer)) continue;
                if (renderer.bounds.SqrDistance(eye) > 0.16f) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i]) continue;
                    commands.DrawRenderer(renderer, materials[i], i, 0);
                    count++;
                }
            }
            if (count == 0) continue;
            camera.targetTexture = request.Target;
            camera.Render();
            request.Output.enabled = true;
        }
        requests.Clear();
        camera.targetTexture = null;
    }

    private void OnDestroy()
    {
        if (camera && commands != null) camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commands);
        commands?.Dispose();
    }
}
