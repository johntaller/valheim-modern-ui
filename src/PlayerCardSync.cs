using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimNewUI;

internal sealed class PlayerCardSync : IDisposable
{
    private const string StatusRpc = "ValheimNewUI_PlayerCard_v1";
    private const string PortraitRequestRpc = "ValheimNewUI_WantPortrait_v1";
    private const string PortraitRpc = "ValheimNewUI_Portrait_v1";
    private const int PortraitSize = 64;
    private const int PortraitBytes = PortraitSize * PortraitSize * 4;
    private const int CharacterIdBytes = sizeof(long) + sizeof(uint);
    private const int StatusBytes = CharacterIdBytes + 5 * sizeof(int) + 2 * sizeof(bool);
    private const int PortraitPacketBytes = CharacterIdBytes + sizeof(int) + PortraitBytes;
    private static readonly AccessTools.FieldRef<FejdStartup, List<PlayerProfile>> Profiles =
        AccessTools.FieldRefAccess<FejdStartup, List<PlayerProfile>>("m_profiles");
    private static readonly AccessTools.FieldRef<FejdStartup, int> ProfileIndex =
        AccessTools.FieldRefAccess<FejdStartup, int>("m_profileIndex");

    private readonly Dictionary<long, Snapshot> remote = new();
    private readonly List<long> expired = new();
    private readonly PortraitCamera camera;
    private ZRoutedRpc route;
    private byte[] portrait;
    private Player preview;
    private long portraitProfile;
    private float nextSend, nextCapture;
    private bool active;

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
            if (enabled) CapturePreview();
            return;
        }

        var current = ZRoutedRpc.instance;
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

        float now = Time.unscaledTime;
        if (now < nextSend) return;
        nextSend = now + 2.5f;

        var player = Player.m_localPlayer;
        if (player && !player.GetZDOID().IsNone() && net.GetPlayerList().Count > 1)
        {
            long profile = Game.instance.GetPlayerProfile().GetPlayerID();
            if ((portrait == null || portraitProfile != profile) && now >= nextCapture && !player.IsDead())
            {
                nextCapture = now + 2;
                portrait = camera.Capture(player, PortraitSize);
                portraitProfile = profile;
            }
            Publish(player, portrait != null && portraitProfile == profile);
        }

        foreach (var entry in remote)
            if (now - entry.Value.LastSeen > 9) expired.Add(entry.Key);
        foreach (long id in expired)
        {
            remote[id].Dispose();
            remote.Remove(id);
        }
        expired.Clear();
    }

    private void CapturePreview()
    {
        var startup = FejdStartup.instance;
        if (!startup || !startup.m_characterSelectScreen.activeInHierarchy || !startup.m_selectCharacterPanel.activeInHierarchy)
        {
            preview = null;
            return;
        }
        var profiles = Profiles(startup);
        int index = ProfileIndex(startup);
        var player = startup.GetPreviewPlayer();
        if (!player || profiles == null || index < 0 || index >= profiles.Count) return;
        long profile = profiles[index].GetPlayerID();
        if (preview != player)
        {
            preview = player;
            nextCapture = Time.unscaledTime + 0.35f;
        }
        if (Time.unscaledTime < nextCapture || (portrait != null && portraitProfile == profile)) return;
        nextCapture = Time.unscaledTime + 2;
        portrait = camera.Capture(player, PortraitSize);
        portraitProfile = profile;
    }

    private void Publish(Player player, bool withPortrait)
    {
        Skills.SkillType skill = Skills.SkillType.None;
        float level = 0;
        foreach (var entry in player.GetSkills().GetSkillList())
        {
            if (entry.m_level <= level) continue;
            skill = entry.m_info.m_skill;
            level = entry.m_level;
        }

        var packet = new ZPackage();
        packet.Write(player.GetZDOID());
        packet.Write(player.GetBodyArmor());
        packet.Write((int)skill);
        packet.Write(level);
        packet.Write(player.GetHealth());
        packet.Write(player.GetMaxHealth());
        packet.Write(player.IsDead());
        packet.Write(withPortrait);
        route.InvokeRoutedRPC(ZRoutedRpc.Everybody, StatusRpc, packet);
    }

    private void ReceiveStatus(long sender, ZPackage packet)
    {
        if (!active || sender == ZNet.GetUID() || packet == null || packet.Size() != StatusBytes) return;
        var id = packet.ReadZDOID();
        if (id.IsNone() || id.UserID != sender || !IsListed(id)) return;

        float armor = packet.ReadSingle();
        var skill = (Skills.SkillType)packet.ReadInt();
        float level = packet.ReadSingle();
        float health = packet.ReadSingle();
        float maximum = packet.ReadSingle();
        bool dead = packet.ReadBool();
        bool hasPortrait = packet.ReadBool();
        if (!float.IsFinite(armor) || !float.IsFinite(level) || !float.IsFinite(health) || !float.IsFinite(maximum)) return;

        if (!remote.TryGetValue(sender, out var card))
            remote.Add(sender, card = new Snapshot());
        card.Character = id;
        card.Armor = Mathf.Max(0, armor);
        card.Skill = Enum.IsDefined(typeof(Skills.SkillType), skill) ? skill : Skills.SkillType.None;
        card.Level = Mathf.Clamp(level, 0, 100);
        card.Health = Mathf.Max(0, health);
        card.MaxHealth = Mathf.Max(0, maximum);
        card.Dead = dead;
        card.LastSeen = Time.unscaledTime;
        if (hasPortrait && !card.Portrait && card.LastSeen >= card.NextRequest)
        {
            card.NextRequest = card.LastSeen + 5;
            route.InvokeRoutedRPC(sender, PortraitRequestRpc, id);
        }
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
        packet.Write(portrait);
        route.InvokeRoutedRPC(sender, PortraitRpc, packet);
    }

    private void ReceivePortrait(long sender, ZPackage packet)
    {
        if (!active || packet == null || packet.Size() != PortraitPacketBytes
            || !remote.TryGetValue(sender, out var card) || card.Portrait
            || packet.ReadZDOID() != card.Character || !IsListed(card.Character)) return;
        if (packet.ReadInt() != PortraitBytes) return;
        var bytes = packet.ReadByteArray(PortraitBytes);
        if (bytes?.Length != PortraitBytes) return;
        card.Portrait = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false);
        card.Portrait.LoadRawTextureData(bytes);
        card.Portrait.Apply(false, true);
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
        if (camera) UnityEngine.Object.Destroy(camera.gameObject);
    }

    internal sealed class Snapshot : IDisposable
    {
        internal ZDOID Character;
        internal Skills.SkillType Skill;
        internal float Level, Armor, Health, MaxHealth, LastSeen, NextRequest, NextResponse;
        internal bool Dead;
        internal Texture2D Portrait;

        public void Dispose()
        {
            if (Portrait) UnityEngine.Object.Destroy(Portrait);
        }
    }
}
