using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class OnlinePlayers : IDisposable
{
    private const float RowWidth = 236, RowHeight = 54, RowStep = 62;
    private const int MaxRows = 5;
    private readonly List<Peer> peers = new();
    // Static so the pins outlive a rebuilt HUD; they are cleared when the session ends.
    private static readonly HashSet<string> pinned = new(StringComparer.Ordinal);
    private readonly RectTransform root;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly List<HudRow> rows = new();
    private readonly List<string> wanted = new();
    private readonly PlayerCardSync cards;
    private float nextRefresh;

    internal IReadOnlyList<Peer> Peers => peers;
    internal Peer Self { get; private set; }
    internal PlayerCardSync Cards => cards;

    internal OnlinePlayers(RectTransform parent, Artwork art, TMP_FontAsset font, PlayerCardSync cards)
    {
        this.cards = cards;
        this.art = art;
        this.font = font;
        root = ItemUi.Rect(parent, "NearbyPlayers");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
        root.sizeDelta = new Vector2(RowWidth, MaxRows * RowStep);
    }

    internal void Refresh(Player player, bool visible, float range)
    {
        float mapSize = Mathf.Clamp(((RectTransform)root.parent).rect.width * 0.15f, 170, 250);
        root.anchoredPosition = new Vector2(-24, -mapSize - 344);
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        // Pins belong to the session: a dead player has no character for a moment, but keeps them.
        if (!ZNet.instance)
        {
            pinned.Clear();
            peers.Clear();
            Self = null;
        }
        if (!player)
        {
            nextRefresh = 0;
            Self = null;
            foreach (var row in rows) row.Hide();
            return;
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.35f;
            ReadPeers(player, range);
        }
        // Hidden behind the inventory or a menu, the cards stay where they are instead of sliding away.
        if (!visible) return;
        wanted.Clear();
        {
            foreach (var peer in peers)
            {
                if (!peer.InRange && !peer.Pinned) continue;
                wanted.Add(peer.Key);
                if (wanted.Count == MaxRows) break;
            }
        }
        // Rows keep their player while they slide out, so a leaving card never shows someone else.
        foreach (var row in rows)
            if (row.Key != null && !wanted.Contains(row.Key)) row.Leave();
        for (int i = 0; i < wanted.Count; i++)
        {
            var row = rows.Find(r => r.Key == wanted[i] && !r.Leaving)
                ?? rows.Find(r => r.Key == null)
                ?? Add();
            row.Show(Find(wanted[i]), i * RowStep, visible);
        }
        foreach (var row in rows) row.Animate();
    }

    private HudRow Add()
    {
        var row = new HudRow(root, art, font);
        rows.Add(row);
        return row;
    }

    private Peer Find(string key)
    {
        foreach (var peer in peers)
            if (peer.Key == key) return peer;
        return null;
    }

    private readonly List<PlayerCardSync.Effect> selfEffects = new();

    private void ReadPeers(Player player, float range)
    {
        peers.Clear();
        var net = ZNet.instance;
        var scene = ZNetScene.instance;
        PlayerCardSync.ReadSkills(player, out var bestSkill, out float bestLevel, out int total);
        PlayerCardSync.ReadEffects(player, selfEffects);
        player.GetGuardianPowerHUD(out var power, out float cooldown);
        Self = new Peer("self", player.GetPlayerName(), null, player.GetCurrentBiome(), cards.LocalPortrait,
            bestSkill, bestLevel, total, player.GetBodyArmor(), player.GetHealth(), player.GetMaxHealth(), player.IsDead(),
            new List<PlayerCardSync.Effect>(selfEffects),
            power ? new PlayerCardSync.Effect(power.NameHash(), power.m_icon, power.m_name, cooldown) : null,
            false, false, true);
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
            // Terrain far away is not loaded, so the biome of a distant player comes from their own client.
            var biome = remote ? Heightmap.FindBiome(remote.transform.position) : Heightmap.Biome.None;
            if (biome == Heightmap.Biome.None && info.m_publicPosition) biome = card.Biome;
            peers.Add(new Peer(key, info.m_name, distance, biome, card.Portrait, card.Skill, card.Level, card.TotalLevel,
                card.Armor, card.Health, card.MaxHealth, card.Dead, card.Effects, card.Power,
                pinned.Contains(key), distance.HasValue && distance.Value <= range, false));
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

    internal static string BiomeName(Heightmap.Biome? biome) =>
        biome is Heightmap.Biome area && area != Heightmap.Biome.None
            ? Localization.instance.Localize("$biome_" + area.ToString().ToLowerInvariant()) : null;

    internal static string Meters(float meters) =>
        meters >= 1000 ? $"{meters / 1000:0.0} " + MenuStyle.Translate("km", "км") : $"{meters:0} " + MenuStyle.Translate("m", "м");

    internal sealed class Peer
    {
        internal readonly string Key, Name;
        internal readonly Texture2D Portrait;
        internal readonly Skills.SkillType Skill;
        internal readonly float? Distance;
        internal readonly Heightmap.Biome? Biome;
        internal readonly float Health, MaxHealth, Armor, SkillLevel;
        internal readonly int TotalLevel;
        internal readonly bool Dead, Pinned, InRange, IsSelf;
        internal readonly IReadOnlyList<PlayerCardSync.Effect> Effects;
        internal readonly PlayerCardSync.Effect Power;

        internal Peer(string key, string name, float? distance, Heightmap.Biome? biome, Texture2D portrait,
            Skills.SkillType skill, float skillLevel, int totalLevel, float armor, float health, float maxHealth, bool dead,
            IReadOnlyList<PlayerCardSync.Effect> effects, PlayerCardSync.Effect power, bool pinned, bool inRange, bool self)
        {
            Key = key;
            Name = name;
            Portrait = portrait;
            Skill = skill;
            SkillLevel = skillLevel;
            TotalLevel = totalLevel;
            Armor = armor;
            Distance = distance;
            Biome = biome;
            Health = health;
            MaxHealth = maxHealth;
            Dead = dead;
            Effects = effects;
            Power = power;
            Pinned = pinned;
            InRange = inRange;
            IsSelf = self;
        }

        internal float HealthFraction => !Dead && MaxHealth > 0 ? Mathf.Clamp01(Health / MaxHealth) : 0;
    }

    // A nearby or pinned player under the minimap. Cards bounce in from the right edge and slide
    // back out when the player leaves, dies far away or is unpinned.
    private sealed class HudRow
    {
        private const int MaxIcons = 5;
        private readonly RectTransform row;
        private readonly CanvasGroup group;
        private readonly Image plate, alarm, well, silhouette, fill, powerIcon, powerGlow;
        private readonly RawImage avatar;
        private readonly TextMeshProUGUI name, distance, health, powerTime, state;
        private readonly Image[] icons = new Image[MaxIcons];
        private float y, enterAt = -10, leaveAt = -10;
        private Peer peer;
        internal string Key { get; private set; }
        internal bool Leaving => leaveAt >= 0;

        internal HudRow(Transform parent, Artwork art, TMP_FontAsset font)
        {
            row = ItemUi.Rect(parent, "Player");
            ItemUi.TopLeft(row, 0, 0, RowWidth, RowHeight);
            group = row.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = group.interactable = false;
            ItemUi.TopLeft(HudStyle.Shadow(row, art, 0.9f).rectTransform, -22, -10, RowWidth + 40, RowHeight + 22);
            // A framed plate keeps the small text readable over snow, sand and sky.
            plate = ItemUi.Image(row, "Plate", art["slot"]);
            plate.color = new Color(1, 1, 1, 0.92f);
            ItemUi.TopLeft(plate.rectTransform, -5, -2, RowWidth + 9, RowHeight + 3);
            alarm = ItemUi.Image(row, "LowHealth", art["glow"]);
            ItemUi.TopLeft(alarm.rectTransform, -16, -13, 84, 84);
            alarm.enabled = false;
            well = ItemUi.Image(row, "PortraitWell", art["slot"]);
            ItemUi.TopLeft(well.rectTransform, 2, 3, 48, 48);
            silhouette = ItemUi.Image(well.transform, "PortraitPlaceholder", art["player-portrait"]);
            silhouette.color = ItemUi.Muted;
            ItemUi.Stretch(silhouette.rectTransform, Vector2.one * 6, Vector2.one * -6);
            avatar = ItemUi.Rect(well.transform, "Portrait").gameObject.AddComponent<RawImage>();
            avatar.raycastTarget = false;
            ItemUi.Stretch(avatar.rectTransform, Vector2.one * 2, Vector2.one * -2);

            var display = HudStyle.DisplayFont(font);
            name = MenuStyle.Label(row, "Name", display, "", 17, ItemUi.Ink);
            name.richText = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            ItemUi.TopLeft(name.rectTransform, 58, 1, 122, 21);
            distance = MenuStyle.Label(row, "Distance", font, "", 12, ItemUi.Muted, TextAlignmentOptions.Right);
            ItemUi.TopLeft(distance.rectTransform, RowWidth - 58, 3, 56, 17);

            for (int i = 0; i < MaxIcons; i++)
            {
                icons[i] = ItemUi.Image(row, "Status", null);
                icons[i].preserveAspect = true;
                ItemUi.TopLeft(icons[i].rectTransform, 58 + i * 18, 24, 16, 16);
            }
            state = MenuStyle.Label(row, "State", font, "", 13, ItemUi.Warning);
            ItemUi.TopLeft(state.rectTransform, 58, 22, 100, 19);
            health = MenuStyle.Label(row, "Health", font, "", 12, ItemUi.Muted, TextAlignmentOptions.Right);
            ItemUi.TopLeft(health.rectTransform, 140, 23, 66, 18);

            powerGlow = ItemUi.Image(row, "PowerReady", art["glow"]);
            ItemUi.TopLeft(powerGlow.rectTransform, RowWidth - 32, 15, 32, 32);
            powerGlow.color = new Color(ItemUi.Accent.r / 255f, ItemUi.Accent.g / 255f, ItemUi.Accent.b / 255f, 0.55f);
            powerIcon = ItemUi.Image(row, "Power", null);
            powerIcon.preserveAspect = true;
            ItemUi.TopLeft(powerIcon.rectTransform, RowWidth - 27, 20, 22, 22);
            powerTime = MenuStyle.Label(row, "PowerCooldown", font, "", 10, ItemUi.Ink, TextAlignmentOptions.Center);
            powerTime.fontStyle = FontStyles.Bold;
            ItemUi.TopLeft(powerTime.rectTransform, RowWidth - 32, 33, 32, 14);

            var track = ItemUi.Image(row, "HealthTrack", art["meter-fill"]);
            track.color = new Color32(47, 28, 26, 225);
            ItemUi.TopLeft(track.rectTransform, 58, 46, RowWidth - 62, 4);
            fill = ItemUi.Image(track.transform, "Health", art["meter-fill"]);
            fill.color = ItemUi.Warning;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            ItemUi.Stretch(fill.rectTransform, Vector2.zero, Vector2.zero);
            row.gameObject.SetActive(false);
        }

        internal void Show(Peer player, float top, bool visible)
        {
            if (player == null) return;
            if (Key != player.Key || Leaving)
            {
                bool fresh = Key == null || Leaving && Key != player.Key;
                Key = player.Key;
                leaveAt = -10;
                if (fresh)
                {
                    enterAt = Time.unscaledTime;
                    y = top;
                }
            }
            peer = player;
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
            // Rows glide to a new place when the order changes.
            y = Mathf.Lerp(y, top, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 12));
            Fill(player);
        }

        internal void Leave()
        {
            if (Leaving || Key == null) return;
            leaveAt = Time.unscaledTime;
        }

        internal void Hide()
        {
            Key = null;
            peer = null;
            leaveAt = -10;
            row.gameObject.SetActive(false);
        }

        internal void Animate()
        {
            if (Key == null) return;
            float now = Time.unscaledTime;
            float offset;
            if (Leaving)
            {
                float t = (now - leaveAt) / 0.38f;
                if (t >= 1)
                {
                    Hide();
                    return;
                }
                offset = (RowWidth + 40) * BackIn(t);
            }
            else
            {
                float t = Mathf.Clamp01((now - enterAt) / 0.5f);
                offset = (RowWidth + 40) * (1 - BackOut(t));
            }
            row.anchoredPosition = new Vector2(offset, -y);
            group.alpha = Mathf.Clamp01(1 - offset / (RowWidth + 40) * 1.2f);
            if (peer != null) Pulse(peer);
        }

        // Ease curves with a small overshoot: the card bounces into place and winds up before leaving.
        private static float BackOut(float t)
        {
            const float c = 1.9f;
            t -= 1;
            return 1 + (c + 1) * t * t * t + c * t * t;
        }

        private static float BackIn(float t)
        {
            const float c = 1.7f;
            return (c + 1) * t * t * t - c * t * t;
        }

        private void Fill(Peer player)
        {
            // The display font has no symbol glyphs, and a fallback glyph's taller line would hide the whole name.
            if (name.text != player.Name) name.text = player.Name;
            distance.text = player.Distance is float meters ? Meters(meters) : "";
            avatar.texture = player.Portrait;
            avatar.enabled = player.Portrait;
            avatar.color = player.Dead ? new Color32(115, 110, 110, 255) : Color.white;
            silhouette.enabled = !avatar.enabled;

            state.enabled = player.Dead;
            if (player.Dead) state.text = MenuStyle.Translate("Dead", "Погиб");
            int shownIcons = 0;
            if (!player.Dead && player.Effects != null)
            {
                foreach (var effect in player.Effects)
                {
                    if (shownIcons == MaxIcons) break;
                    icons[shownIcons].sprite = effect.Icon;
                    icons[shownIcons].enabled = true;
                    shownIcons++;
                }
            }
            for (int i = shownIcons; i < MaxIcons; i++) icons[i].enabled = false;
            health.enabled = !player.Dead && player.MaxHealth > 0;
            if (health.enabled) health.text = $"{Mathf.CeilToInt(player.Health)}/{Mathf.CeilToInt(player.MaxHealth)}";

            var power = player.Power;
            powerIcon.enabled = power != null && power.Icon;
            bool ready = powerIcon.enabled && power.Left <= 0;
            powerGlow.enabled = ready && !player.Dead;
            powerTime.enabled = powerIcon.enabled && !ready;
            if (powerIcon.enabled)
            {
                powerIcon.sprite = power.Icon;
                powerIcon.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.55f, 0.6f);
                if (powerTime.enabled) powerTime.text = StatusEffect.GetTimeString(Mathf.Ceil(power.Left), false, true);
            }
            fill.fillAmount = player.HealthFraction;
        }

        // Low health makes the card's frame and bar beat red.
        private void Pulse(Peer player)
        {
            float fraction = player.HealthFraction;
            bool low = !player.Dead && player.MaxHealth > 0 && fraction < 0.3f;
            float beat = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2 * 1.6f) : 0;
            Color frame = player.Pinned ? ItemUi.Accent : Color.white;
            well.color = Color.Lerp(frame, new Color(1, 0.25f, 0.2f, 1), beat * 0.85f);
            plate.color = Color.Lerp(new Color(1, 1, 1, 0.92f), new Color(1, 0.42f, 0.38f, 0.96f), beat * 0.7f);
            alarm.enabled = low;
            if (low) alarm.color = new Color(1, 0.16f, 0.12f, 0.25f + 0.6f * beat);
            fill.color = Color.Lerp(ItemUi.Warning, new Color(1, 0.45f, 0.4f, 1), beat);
            float scale = 1 + 0.03f * beat;
            well.rectTransform.localScale = new Vector3(scale, scale, 1);
        }
    }
}

internal sealed class PortraitCamera : MonoBehaviour
{
    private const int StudioLayer = 28;
    private const int Supersample = 4;
    // The head bone sits at the base of the skull; the face is a little higher.
    private const float FaceRise = 0.09f;
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
            // The eye point follows the camera rig and often sits above the head; the head bone does not.
            var head = FindHead(visual);
            Vector3 eye = head ? head.position + Vector3.up * FaceRise : player.GetEyePoint() - Vector3.up * 0.1f;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                if (renderer.enabled)
                    CopyMesh(renderer, eye, subject.transform);
            if (subject.transform.childCount == 0) return null;

            Vector3 forward = visual.transform.forward;
            Vector3 center = Studio;
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

    private static Transform FindHead(GameObject visual)
    {
        var animator = visual.GetComponentInChildren<Animator>();
        if (animator && animator.isHuman)
        {
            var bone = animator.GetBoneTransform(HumanBodyBones.Head);
            if (bone) return bone;
        }
        foreach (var bone in visual.GetComponentsInChildren<Transform>())
            if (string.Equals(bone.name, "Head", StringComparison.OrdinalIgnoreCase)) return bone;
        return null;
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
