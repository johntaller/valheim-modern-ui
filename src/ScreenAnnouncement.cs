using System;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimNewUI;

internal sealed class ScreenAnnouncement : IDisposable
{
    private readonly RectTransform root, shadow;
    private readonly CanvasGroup group;
    private readonly TextMeshProUGUI title, details;
    private readonly Material thinTitle, negativeTitle;

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
        thinTitle = new Material(title.fontSharedMaterial);
        thinTitle.SetFloat(ShaderUtilities.ID_FaceDilate, -0.075f);
        var shader = LoadLocationShader();
        if (shader)
        {
            negativeTitle = new Material(shader);
            negativeTitle.mainTexture = title.font.atlasTexture;
            negativeTitle.SetFloat(ShaderUtilities.ID_GradientScale, thinTitle.GetFloat(ShaderUtilities.ID_GradientScale));
        }
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
        shadow.gameObject.SetActive(death || !negativeTitle);
        var material = death || !negativeTitle ? thinTitle : negativeTitle;
        if (title.fontSharedMaterial != material) title.fontSharedMaterial = material;
        float width = Mathf.Clamp(root.rect.width - 32, 120, 1160);
        HudStyle.Center(shadow, 0, death ? -42 : 0, width + 160, death ? 414 : 265);
        HudStyle.Center(title.rectTransform, 0, 0, width, 116);
        HudStyle.Center(details.rectTransform, 0, -122, Mathf.Min(width, 780), 137);
        string heading = notice.Title.TrimEnd('!', '！', ' ');
        if (title.text != heading) title.text = heading;
        if (details.text != notice.Subtitle) details.text = notice.Subtitle;
        title.color = death ? new Color32(214, 49, 44, 255) : Color.white;
        float age = now - notice.Created;
        group.alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / (death ? 1.05f : 0.65f)))
            * Mathf.SmoothStep(0, 1, Mathf.Clamp01((notice.Expires - now) / (death ? 2.1f : 4.2f)));
    }

    private static Shader LoadLocationShader()
    {
        string platform = Application.platform switch
        {
            RuntimePlatform.OSXPlayer => "standaloneosx",
            RuntimePlatform.WindowsPlayer => "standalonewindows64",
            RuntimePlatform.LinuxPlayer => "standalonelinux64",
            _ => ""
        };
        using var stream = typeof(ScreenAnnouncement).Assembly.GetManifestResourceStream($"ValheimNewUI.location-negative.{platform}.bundle");
        if (stream == null) return null;
        using var bytes = new System.IO.MemoryStream();
        stream.CopyTo(bytes);
        var bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
        if (!bundle) return null;
        var shader = bundle.LoadAsset<Shader>("assets/locationnegative.shader");
        bundle.Unload(false);
        return shader && shader.isSupported ? shader : null;
    }

    public void Dispose()
    {
        if (negativeTitle)
        {
            UnityEngine.Object.Destroy(negativeTitle.shader);
            UnityEngine.Object.Destroy(negativeTitle);
        }
        UnityEngine.Object.Destroy(thinTitle);
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
    private static void BeforeDeath(Player __instance, out double? __state)
    {
        __state = NativeMessagePatch.Enabled && __instance == Player.m_localPlayer ? LifeStart(__instance) : null;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath)), HarmonyPostfix]
    private static void AfterDeath(Player __instance, double? __state)
    {
        if (!__state.HasValue) return;
        double now = ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0;
        double days = EnvMan.instance && EnvMan.instance.m_dayLengthSec > 0
            ? (now - __state.Value) / EnvMan.instance.m_dayLengthSec : 0;
        if (ZNet.instance) __instance.m_customData[Key(ZNet.instance.GetWorldUID())] = "-1";

        string heading = MenuStyle.Translate("Days survived", "Дней прожито");
        string number = days is > 0 and < 0.01 ? "< 0.01" : days.ToString("0.##", CultureInfo.CurrentCulture);
        string title = Localization.instance.Localize("$msg_youdied").ToUpper(CultureInfo.CurrentCulture);
        NativeMessagePatch.Enqueue(MessageHud.instance, new NotificationHud.Notice(title, $"{heading}  {number}",
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
