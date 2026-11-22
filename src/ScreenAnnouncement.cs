using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimNewUI;

internal sealed class ScreenAnnouncement
{
    private readonly RectTransform root, shadow;
    private readonly CanvasGroup group;
    private readonly TextMeshProUGUI title, details;

    internal ScreenAnnouncement(Transform parent, Artwork art, TMP_FontAsset font)
    {
        root = ItemUi.Rect(parent, "ScreenAnnouncement");
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        shadow = HudStyle.Shadow(root, art, 1).rectTransform;
        title = ItemUi.Text(root, "Title", HudStyle.DisplayFont(font), 88, ItemUi.Ink);
        title.richText = false;
        title.alignment = TextAlignmentOptions.Center;
        title.enableAutoSizing = true;
        title.fontSizeMin = 35;
        title.fontSizeMax = 88;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.fontStyle = FontStyles.Bold;
        details = ItemUi.Text(root, "Details", font, 20, ItemUi.Ink);
        details.richText = false;
        details.alignment = TextAlignmentOptions.Top;
        details.textWrappingMode = TextWrappingModes.Normal;
        details.overflowMode = TextOverflowModes.Ellipsis;
    }

    internal void Show(NotificationHud.Notice notice, float now)
    {
        bool show = notice != null && now < notice.Expires;
        root.gameObject.SetActive(show);
        if (!show) return;
        if (root.GetSiblingIndex() != root.parent.childCount - 1) root.SetAsLastSibling();
        bool death = notice.Type == NotificationHud.Kind.Death;
        float width = Mathf.Clamp(root.rect.width - 32, 120, 1160);
        HudStyle.Center(shadow, 0, death ? -42 : 0, width + 160, death ? 414 : 265);
        HudStyle.Center(title.rectTransform, 0, 0, width, 116);
        HudStyle.Center(details.rectTransform, 0, -122, Mathf.Min(width, 780), 137);
        if (title.text != notice.Title) title.text = notice.Title;
        if (details.text != notice.Subtitle) details.text = notice.Subtitle;
        title.color = death ? new Color32(214, 49, 44, 255) : ItemUi.Ink;
        float age = now - notice.Created;
        group.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / (death ? 1.05f : 0.65f)))
            * Mathf.SmoothStep(0, 1, Mathf.Clamp01((notice.Expires - now) / (death ? 2.1f : 4.2f)));
    }
}

[HarmonyPatch]
internal static class LifeNotices
{
    private static Player observed;
    [HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer)), HarmonyPostfix]
    private static void TrackLife(Player __instance)
    {
        if (NativeMessagePatch.Enabled) Observe(__instance);
    }

    internal static void Observe(Player player)
    {
        if (observed == player || !ZNet.instance) return;
        observed = player;
        if (player && !player.IsDead()) LifeStart(player);
    }

    private static string Key(long world) => "ValheimNewUI.lifeStart." + world.ToString(CultureInfo.InvariantCulture);

    private static double LifeStart(Player player)
    {
        if (!ZNet.instance) return 0;
        string key = Key(ZNet.instance.GetWorldUID());
        double now = ZNet.instance.GetTimeSeconds();
        if (player.m_customData.TryGetValue(key, out var value) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double start) &&
            start >= 0 && start <= now) return start;
        player.m_customData[key] = now.ToString("R", CultureInfo.InvariantCulture);
        return now;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath)), HarmonyPrefix]
    private static void BeforeDeath(Player __instance, out Dictionary<Skills.SkillType, float> __state)
    {
        __state = null;
        if (!NativeMessagePatch.Enabled || __instance != Player.m_localPlayer) return;
        LifeStart(__instance);
        __state = new Dictionary<Skills.SkillType, float>();
        foreach (var skill in __instance.GetSkills().GetSkillList())
            __state[skill.m_info.m_skill] = skill.m_level;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath)), HarmonyPostfix]
    private static void AfterDeath(Player __instance, Dictionary<Skills.SkillType, float> __state)
    {
        if (__state == null) return;
        double now = ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0;
        double days = EnvMan.instance && EnvMan.instance.m_dayLengthSec > 0
            ? (now - LifeStart(__instance)) / EnvMan.instance.m_dayLengthSec : 0;
        if (ZNet.instance) __instance.m_customData[Key(ZNet.instance.GetWorldUID())] = "-1";

        var lost = new List<(string Name, float Levels)>();
        foreach (var pair in __state)
        {
            float loss = pair.Value - __instance.GetSkills().GetSkillLevel(pair.Key);
            if (loss > 0.001f)
                lost.Add((Localization.instance.Localize("$skill_" + pair.Key.ToString().ToLowerInvariant()), loss));
        }
        lost.Sort((left, right) => right.Levels.CompareTo(left.Levels));
        string heading = MenuStyle.Translate("Days survived", "Дней прожито");
        string number = days is > 0 and < 0.01 ? "< 0.01" : days.ToString("0.##", CultureInfo.CurrentCulture);
        var lines = new List<string>();
        foreach (var loss in lost) lines.Add($"{loss.Name} −{loss.Levels:0.###}");
        string summary = lines.Count == 0 ? MenuStyle.Translate("No skill levels lost", "Уровни навыков не потеряны")
            : string.Join("  ·  ", lines);
        string title = Localization.instance.Localize("$msg_youdied").ToUpper(CultureInfo.CurrentCulture);
        NativeMessagePatch.Enqueue(MessageHud.instance, new NotificationHud.Notice(title, $"{heading}  {number}\n{summary}",
            null, 0, NotificationHud.Kind.Death, true, 10));
    }

    [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect)), HarmonyPrefix]
    private static void BeforeRest(SE_Cozy __instance, Character ___m_character, out float __state)
    {
        __state = !NativeMessagePatch.Enabled || !___m_character || ___m_character != Player.m_localPlayer ||
            __instance.GetDuration() + 0.2f < __instance.m_delay ? -1 : Remaining(___m_character, out _);
    }

    [HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect)), HarmonyPostfix]
    private static void AfterRest(Character ___m_character, float __state)
    {
        if (__state < 0) return;
        float time = Remaining(___m_character, out var effect);
        // While the player stays at a fire, the game refreshes the timer every tick.
        if (!effect || time <= 0 || (__state > 0 && time - __state < 2)) return;
        string name = Localization.instance.Localize(effect.m_name);
        int minutes = Mathf.CeilToInt(time / 60);
        string text = MenuStyle.Translate("Gained {0} for {1} min", "Получен статус «{0}» на {1} мин");
        NativeMessagePatch.Enqueue(MessageHud.instance, new NotificationHud.Notice(string.Format(text, name, minutes),
            "", effect.m_icon, 0, NotificationHud.Kind.General, false, 5.2f));
    }

    private static float Remaining(Character character, out StatusEffect effect)
    {
        effect = character.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
        return effect ? Mathf.Max(0, effect.m_ttl - effect.GetDuration()) : 0;
    }
}
