using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimNewUI;

// Shares a small player card between clients that run the mod: health, armor, skills, biome,
// visible status effects, the chosen guardian power and a portrait. The game itself only
// shares names and, when allowed, map positions.
internal sealed class PlayerCardSync : IDisposable
{
    private const string StatusRpc = "ValheimNewUI_PlayerCard_v2";
    private const string PortraitRequestRpc = "ValheimNewUI_WantPortrait_v2";
    private const string PortraitRpc = "ValheimNewUI_Portrait_v2";
    internal const int PortraitSize = 64;
    private const int PortraitBytes = PortraitSize * PortraitSize * 4;
    private const int MaxEffects = 8;

    private readonly Dictionary<long, Snapshot> remote = new();
    private readonly List<long> expired = new();
    private readonly PortraitCamera camera;
    private ZRoutedRpc route;
    private byte[] portrait;
    private int portraitVersion;
    private Player subject;
    private long portraitProfile;
    private float nextSend, nextCapture, settledAt;
    private bool active, retake;

    // The local character's own portrait, for its row in the players list.
    internal Texture2D LocalPortrait { get; private set; }
    internal bool Capturing => retake || subject && portrait == null;

    internal PlayerCardSync(Transform parent)
    {
        var obj = new GameObject("NewUI_PlayerCardCamera");
        obj.transform.SetParent(parent, false);
        camera = obj.AddComponent<PortraitCamera>();
    }

    internal void Refresh(bool enabled)
    {
        active = enabled;
        var net = ZNet.instance;
        if (!enabled || !net)
        {
            if (remote.Count > 0) ClearRemote();
            ForgetPortrait();
            portraitProfile = 0;
            retake = false;
            subject = null;
            return;
        }

        var current = ZRoutedRpc.instance;
        float now = Time.unscaledTime;
        var player = Player.m_localPlayer;
        if (player && !player.GetZDOID().IsNone())
        {
            long profile = Game.instance.GetPlayerProfile().GetPlayerID();
            if (portraitProfile != profile)
            {
                ForgetPortrait();
                portraitProfile = profile;
                retake = false;
                Load(profile);
            }
            if (subject != player)
            {
                subject = player;
                settledAt = now + 4;
            }
            if ((portrait == null || retake) && now >= nextCapture && now >= settledAt && IsPosed(player))
            {
                nextCapture = now + 2;
                var bytes = camera.Capture(player, PortraitSize);
                if (bytes != null)
                {
                    SetPortrait(bytes);
                    retake = false;
                    Save(profile);
                    nextSend = 0;
                }
            }
        }

        if (current == null) return;
        if (route != current)
        {
            ClearRemote();
            route = current;
            route.Register<ZPackage>(StatusRpc, ReceiveStatus);
            route.Register<ZDOID>(PortraitRequestRpc, SendPortrait);
            route.Register<ZPackage>(PortraitRpc, ReceivePortrait);
            nextSend = 0;
        }

        if (now < nextSend) return;
        nextSend = now + 2;
        if (player && !player.GetZDOID().IsNone() && net.GetPlayerList().Count > 1) Publish(player);

        foreach (var entry in remote)
            if (now - entry.Value.LastSeen > 9) expired.Add(entry.Key);
        foreach (long id in expired)
        {
            remote[id].Dispose();
            remote.Remove(id);
        }
        expired.Clear();
    }

    internal void Retake()
    {
        retake = true;
        nextCapture = 0;
        settledAt = Time.unscaledTime + 0.3f;
    }

    private void ForgetPortrait()
    {
        portrait = null;
        portraitVersion = 0;
        if (LocalPortrait) UnityEngine.Object.Destroy(LocalPortrait);
        LocalPortrait = null;
    }

    private void SetPortrait(byte[] bytes)
    {
        portrait = bytes;
        // A content hash: other clients fetch the portrait again only when it really changed.
        uint hash = 2166136261;
        for (int i = 0; i < bytes.Length; i += 3) hash = (hash ^ bytes[i]) * 16777619;
        portraitVersion = (int)(hash | 1);
        if (!LocalPortrait) LocalPortrait = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false);
        LocalPortrait.LoadRawTextureData(bytes);
        LocalPortrait.Apply(false, false);
    }

    private static string PortraitPath(long profile) =>
        Path.Combine(Paths.ConfigPath, "ValheimNewUI", "portraits", profile + ".png");

    private void Save(long profile)
    {
        string temporary = null;
        try
        {
            string path = PortraitPath(profile);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, ImageConversion.EncodeToPNG(LocalPortrait));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
            temporary = null;
        }
        catch (Exception ex) { Debug.LogWarning("[Valheim New UI] Portrait not saved: " + ex.Message); }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) { Debug.LogWarning("[Valheim New UI] Portrait temporary file not removed: " + ex.Message); }
            }
        }
    }

    private void Load(long profile)
    {
        string path = PortraitPath(profile);
        if (!File.Exists(path)) return;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false)
                || texture.width != PortraitSize || texture.height != PortraitSize) return;
            var pixels = texture.GetPixels32();
            var bytes = new byte[PortraitBytes];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = 255;
            }
            SetPortrait(bytes);
        }
        catch (Exception ex) { Debug.LogWarning("[Valheim New UI] Portrait not loaded: " + ex.Message); }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    // A portrait taken mid-swing, asleep or during the intro shows a weapon or the back of the head.
    private static bool IsPosed(Player player) =>
        !player.IsDead() && !player.InIntro() && !player.InCutscene() && !player.IsTeleporting() && !player.InBed()
        && !player.IsSleeping() && !player.InAttack() && !player.InDodge() && !player.IsStaggering()
        && !player.InEmote() && !player.IsAttached() && !player.InPlaceMode() && player.IsOnGround();

    internal static void ReadSkills(Player player, out Skills.SkillType best, out float bestLevel, out int total)
    {
        best = Skills.SkillType.None;
        bestLevel = 0;
        float sum = 0;
        foreach (var entry in player.GetSkills().GetSkillList())
        {
            sum += entry.m_level;
            if (entry.m_level <= bestLevel) continue;
            best = entry.m_info.m_skill;
            bestLevel = entry.m_level;
        }
        total = Mathf.FloorToInt(sum + 0.0001f);
    }

    // Status effects a player sees on the HUD: they have an icon.
    internal static void ReadEffects(Player player, List<Effect> into)
    {
        into.Clear();
        foreach (var effect in player.GetSEMan().GetStatusEffects())
        {
            if (!effect || !effect.m_icon) continue;
            float left = effect.m_ttl > 0 ? Mathf.Max(0, effect.GetRemaningTime()) : -1;
            into.Add(new Effect(effect.NameHash(), effect.m_icon, effect.m_name, left));
            if (into.Count == MaxEffects) break;
        }
    }

    private readonly List<Effect> effects = new();

    private void Publish(Player player)
    {
        ReadSkills(player, out var skill, out float level, out int total);
        ReadEffects(player, effects);
        player.GetGuardianPowerHUD(out var power, out float cooldown);

        var packet = new ZPackage();
        packet.Write(player.GetZDOID());
        packet.Write(player.GetBodyArmor());
        packet.Write((int)skill);
        packet.Write(level);
        packet.Write(total);
        packet.Write(player.GetHealth());
        packet.Write(player.GetMaxHealth());
        packet.Write(player.IsDead());
        packet.Write(portraitVersion);
        packet.Write((int)(ZNet.instance.IsReferencePositionPublic() ? player.GetCurrentBiome() : Heightmap.Biome.None));
        packet.Write(power ? power.NameHash() : 0);
        packet.Write(power ? Mathf.Max(0, cooldown) : 0f);
        packet.Write(effects.Count);
        foreach (var effect in effects)
        {
            packet.Write(effect.Hash);
            packet.Write(effect.Remaining);
        }
        route.InvokeRoutedRPC(ZRoutedRpc.Everybody, StatusRpc, packet);
    }

    private void ReceiveStatus(long sender, ZPackage packet)
    {
        if (!active || sender == ZNet.GetUID() || packet == null || packet.Size() > 512) return;
        try
        {
            var id = packet.ReadZDOID();
            if (id.IsNone() || id.UserID != sender || !IsListed(id)) return;

            float armor = packet.ReadSingle();
            var skill = (Skills.SkillType)packet.ReadInt();
            float level = packet.ReadSingle();
            int total = packet.ReadInt();
            float health = packet.ReadSingle();
            float maximum = packet.ReadSingle();
            bool dead = packet.ReadBool();
            int version = packet.ReadInt();
            var biome = (Heightmap.Biome)packet.ReadInt();
            int power = packet.ReadInt();
            float cooldown = packet.ReadSingle();
            int count = packet.ReadInt();
            if (count < 0 || count > MaxEffects) return;
            if (!float.IsFinite(armor) || !float.IsFinite(level) || !float.IsFinite(health) || !float.IsFinite(maximum)
                || !float.IsFinite(cooldown)) return;

            if (!remote.TryGetValue(sender, out var card))
                remote.Add(sender, card = new Snapshot());
            card.Character = id;
            card.Armor = Mathf.Max(0, armor);
            card.Skill = Enum.IsDefined(typeof(Skills.SkillType), skill) ? skill : Skills.SkillType.None;
            card.Level = Mathf.Clamp(level, 0, 100);
            card.TotalLevel = Mathf.Clamp(total, 0, 10000);
            card.Health = Mathf.Max(0, health);
            card.MaxHealth = Mathf.Max(0, maximum);
            card.Dead = dead;
            card.Biome = biome;
            card.LastSeen = Time.unscaledTime;
            var db = ObjectDB.instance;
            var powerEffect = power != 0 && db ? db.GetStatusEffect(power) : null;
            card.Power = powerEffect ? new Effect(power, powerEffect.m_icon, powerEffect.m_name, cooldown) : null;
            card.Effects.Clear();
            for (int i = 0; i < count; i++)
            {
                int hash = packet.ReadInt();
                float left = packet.ReadSingle();
                var effect = db ? db.GetStatusEffect(hash) : null;
                if (effect && effect.m_icon && float.IsFinite(left))
                    card.Effects.Add(new Effect(hash, effect.m_icon, effect.m_name, left));
            }
            if (version != 0 && version != card.PortraitVersion && card.LastSeen >= card.NextRequest)
            {
                card.NextRequest = card.LastSeen + 5;
                route.InvokeRoutedRPC(sender, PortraitRequestRpc, id);
            }
        }
        catch (EndOfStreamException) { }
    }

    private void SendPortrait(long sender, ZDOID character)
    {
        var player = Player.m_localPlayer;
        if (!active || portrait == null || !player || !remote.TryGetValue(sender, out var requester)
            || !IsListed(requester.Character) || player.GetZDOID() != character
            || portraitProfile != Game.instance.GetPlayerProfile().GetPlayerID()
            || Time.unscaledTime < requester.NextResponse) return;

        requester.NextResponse = Time.unscaledTime + 2;
        var packet = new ZPackage();
        packet.Write(character);
        packet.Write(portraitVersion);
        packet.Write(portrait);
        route.InvokeRoutedRPC(sender, PortraitRpc, packet);
    }

    private void ReceivePortrait(long sender, ZPackage packet)
    {
        if (!active || packet == null || packet.Size() > PortraitBytes + 64
            || !remote.TryGetValue(sender, out var card)) return;
        try
        {
            if (packet.ReadZDOID() != card.Character || !IsListed(card.Character)) return;
            int version = packet.ReadInt();
            if (version == 0 || version == card.PortraitVersion) return;
            if (packet.ReadInt() != PortraitBytes) return;
            var bytes = packet.ReadByteArray(PortraitBytes);
            if (bytes?.Length != PortraitBytes) return;
            if (!card.Portrait) card.Portrait = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false);
            card.Portrait.LoadRawTextureData(bytes);
            card.Portrait.Apply(false, false);
            card.PortraitVersion = version;
        }
        catch (EndOfStreamException) { }
    }

    private static bool IsListed(ZDOID id)
    {
        if (!ZNet.instance) return false;
        foreach (var info in ZNet.instance.GetPlayerList())
            if (info.m_characterID == id) return true;
        return false;
    }

    internal Snapshot Get(ZDOID id) =>
        !id.IsNone() && remote.TryGetValue(id.UserID, out var card) && card.Character == id ? card : null;

    private void ClearRemote()
    {
        foreach (var card in remote.Values) card.Dispose();
        remote.Clear();
    }

    public void Dispose()
    {
        active = false;
        ClearRemote();
        ForgetPortrait();
        if (camera) UnityEngine.Object.Destroy(camera.gameObject);
    }

    internal sealed class Effect
    {
        internal readonly int Hash;
        internal readonly Sprite Icon;
        internal readonly string Name;
        // Seconds left when the card was read, or -1 for effects without a timer.
        internal readonly float Remaining;
        internal readonly float ReadAt = Time.unscaledTime;

        internal Effect(int hash, Sprite icon, string name, float remaining)
        {
            Hash = hash;
            Icon = icon;
            Name = name;
            Remaining = remaining;
        }

        internal float Left => Remaining < 0 ? -1 : Mathf.Max(0, Remaining - (Time.unscaledTime - ReadAt));
    }

    internal sealed class Snapshot : IDisposable
    {
        internal ZDOID Character;
        internal Skills.SkillType Skill;
        internal float Level, Armor, Health, MaxHealth, LastSeen, NextRequest, NextResponse;
        internal int TotalLevel, PortraitVersion;
        internal bool Dead;
        internal Heightmap.Biome Biome;
        internal Effect Power;
        internal readonly List<Effect> Effects = new();
        internal Texture2D Portrait;

        public void Dispose()
        {
            if (Portrait) UnityEngine.Object.Destroy(Portrait);
        }
    }
}
