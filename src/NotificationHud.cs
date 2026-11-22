using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class NotificationHud : IDisposable
{
    private const int MaxMessages = 5;
    private static readonly System.Reflection.FieldInfo Unlocks = AccessTools.Field(typeof(MessageHud), "m_unlockMessages");
    private static readonly System.Reflection.FieldInfo Biome = AccessTools.Field(typeof(MessageHud), "m_biomeMsgInstance");
    private readonly RectTransform root;
    private readonly Toast skill;
    private readonly Toast[] messages = new Toast[MaxMessages];
    private readonly List<Notice> active = new();
    private readonly List<HiddenGraphics> stock = new();
    private readonly Dictionary<GameObject, HiddenGraphics> popups = new();
    private readonly List<GameObject> expiredPopups = new();
    private MessageHud owner;
    private Notice skillNotice;

    internal NotificationHud(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        root = ItemUi.Rect(parent, "Notifications");
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);
        skill = new Toast(root, art, font, true);
        for (int i = 0; i < messages.Length; i++) messages[i] = new Toast(root, art, font, false);
    }

    internal void Refresh(bool showVitals, Theme theme, bool covered)
    {
        if (owner != MessageHud.instance)
        {
            Restore();
            active.Clear();
            skillNotice = null;
            owner = MessageHud.instance;
            if (owner)
            {
                HideStock(owner.m_messageText);
                HideStock(owner.m_messageCenterText);
                HideStock(owner.m_messageIcon);
            }
        }
        if (owner)
        {
            foreach (var notice in NativeMessagePatch.Drain(owner)) Add(notice);
            if (Unlocks?.GetValue(owner) is List<GameObject> list)
                foreach (var popup in list) CapturePopup(popup, true);
            CapturePopup(Biome?.GetValue(owner) as GameObject, false);
            foreach (var popup in popups.Keys)
                if (!popup) expiredPopups.Add(popup);
            foreach (var popup in expiredPopups) popups.Remove(popup);
            expiredPopups.Clear();
        }

        float now = Time.unscaledTime;
        active.RemoveAll(notice => notice.Expires <= now);
        bool hidden = Hud.IsUserHidden();
        skill.Show(!covered && !hidden ? skillNotice : null, now, -78);
        float bottom = 42;
        if (showVitals && theme.Anchor.y == 0)
        {
            float barCenter = (theme.Anchor.x - 0.5f) * root.rect.width + theme.Offset.x;
            if (Mathf.Abs(barCenter) < (theme.Width + 470) * 0.5f)
                bottom = Mathf.Max(bottom, theme.Offset.y + 2 * theme.Height + theme.Gap + 18);
        }
        int shown = 0;
        if (!covered)
        {
            foreach (var notice in active)
            {
                if (hidden && !notice.Force) continue;
                bottom += messages[shown++].Show(notice, now, bottom) + 5;
            }
        }
        while (shown < messages.Length) messages[shown++].Show(null, now, 0);
    }

    private void Add(Notice notice)
    {
        if (notice.Skill)
        {
            skillNotice = notice;
            return;
        }
        int repeated = active.FindIndex(other => other.Title == notice.Title && other.Subtitle == notice.Subtitle && other.Icon == notice.Icon);
        if (repeated >= 0)
        {
            var previous = active[repeated];
            if (notice.Amount > 0) notice.Amount += previous.Amount;
            active.RemoveAt(repeated);
        }
        active.Insert(0, notice);
        if (active.Count > MaxMessages) active.RemoveAt(active.Count - 1);
    }

    private void HideStock(Graphic graphic)
    {
        if (!graphic) return;
        var hidden = new HiddenGraphics(graphic.gameObject);
        hidden.SetRaycasts(false);
        stock.Add(hidden);
    }

    private void CapturePopup(GameObject popup, bool unlock)
    {
        if (!popup || popups.ContainsKey(popup)) return;
        var title = (unlock ? popup.transform.Find("UnlockMessage/UnlockTitle") : Utils.FindChild(popup.transform, "Title"))
            ?.GetComponent<TMP_Text>();
        if (!title) return;
        var details = unlock ? popup.transform.Find("UnlockMessage/UnlockDescription")?.GetComponent<TMP_Text>() : null;
        var icon = unlock ? popup.transform.Find("UnlockMessage/icon_bkg/UnlockIcon")?.GetComponent<Image>() : null;
        Add(new Notice(title.text, details ? details.text : "", icon ? icon.sprite : null, 0, false, false, 6));
        var hidden = new HiddenGraphics(popup);
        hidden.SetRaycasts(false);
        popups.Add(popup, hidden);
    }

    private void Restore()
    {
        foreach (var hidden in stock) hidden.Restore();
        stock.Clear();
        foreach (var hidden in popups.Values) hidden.Restore();
        popups.Clear();
    }

    public void Dispose()
    {
        Restore();
        ItemUi.Destroy(root);
    }

    internal sealed class Notice
    {
        internal readonly string Title, Subtitle;
        internal readonly Sprite Icon;
        internal readonly bool Skill, Force;
        internal readonly float Expires;
        internal int Amount;

        internal Notice(string title, string subtitle, Sprite icon, int amount, bool skill, bool force, float duration)
        {
            Title = title;
            Subtitle = subtitle;
            Icon = icon;
            Amount = amount;
            Skill = skill;
            Force = force;
            Expires = Time.unscaledTime + duration;
        }
    }

    private sealed class Toast
    {
        private readonly RectTransform rect;
        private readonly CanvasGroup group;
        private readonly TextMeshProUGUI title, subtitle;
        private readonly Image icon, line;
        private readonly bool skill;
        private Notice current;
        private float width, height;

        internal Toast(RectTransform parent, Artwork art, TMP_FontAsset font, bool skill)
        {
            this.skill = skill;
            var card = ItemUi.Image(parent, skill ? "SkillIncrease" : "Message", art["item-card"]);
            rect = card.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, skill ? 1 : 0);
            rect.pivot = new Vector2(0.5f, skill ? 1 : 0);
            group = card.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            line = ItemUi.Image(rect, "Accent");
            line.color = ItemUi.Accent;
            icon = ItemUi.Image(rect, "Icon");
            icon.preserveAspect = true;
            title = ItemUi.Text(rect, "Title", font, skill ? 21 : 16, ItemUi.Ink);
            title.fontStyle = FontStyles.Bold;
            title.overflowMode = TextOverflowModes.Ellipsis;
            subtitle = ItemUi.Text(rect, "Subtitle", font, 14, ItemUi.Muted);
            subtitle.overflowMode = TextOverflowModes.Ellipsis;
            rect.gameObject.SetActive(false);
        }

        internal float Show(Notice notice, float now, float y)
        {
            bool visible = notice != null && now < notice.Expires;
            if (rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
            if (!visible) return 0;
            float available = Mathf.Max(100, Mathf.Min(470, ((RectTransform)rect.parent).rect.width - 32));
            if (current != notice || width != available)
            {
                current = notice;
                width = available;
                title.text = notice.Amount > 1 ? $"{notice.Title} ×{notice.Amount}" : notice.Title;
                subtitle.text = notice.Subtitle;
                bool hasIcon = notice.Icon;
                bool hasSubtitle = notice.Subtitle.Length > 0;
                icon.enabled = hasIcon;
                icon.sprite = notice.Icon;
                line.enabled = skill;
                subtitle.enabled = hasSubtitle;
                int left = hasIcon ? (skill ? 55 : 45) : 14;
                float textWidth = width - left - 14;
                float main = Mathf.Min(skill ? 50 : 42, Mathf.Ceil(title.GetPreferredValues(title.text, textWidth, Mathf.Infinity).y));
                float secondary = hasSubtitle ? Mathf.Min(36, Mathf.Ceil(subtitle.GetPreferredValues(notice.Subtitle, textWidth, Mathf.Infinity).y)) : 0;
                float measured = Mathf.Max(title.GetPreferredValues(title.text).x, subtitle.GetPreferredValues(notice.Subtitle).x);
                float cardWidth = Mathf.Clamp(Mathf.Ceil(measured + left + 14), Mathf.Min(skill ? 215 : 142, width), width);
                height = Mathf.Max(skill ? 56 : 35, main + secondary + (hasSubtitle ? 16 : 12));
                rect.sizeDelta = new Vector2(cardWidth, height);
                int iconSize = skill ? 34 : 25;
                ItemUi.TopLeft(icon.rectTransform, 10, (height - iconSize) / 2, iconSize, iconSize);
                ItemUi.TopLeft(line.rectTransform, 12, 0, cardWidth - 24, 2);
                float top = (height - main - secondary) / 2;
                title.alignment = hasIcon ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
                subtitle.alignment = title.alignment;
                ItemUi.TopLeft(title.rectTransform, left, top, cardWidth - left - 14, main);
                ItemUi.TopLeft(subtitle.rectTransform, left, top + main, cardWidth - left - 14, secondary);
            }
            group.alpha = Mathf.Clamp01((notice.Expires - now) / 0.55f);
            rect.anchoredPosition = new Vector2(0, y);
            return height;
        }
    }
}

[HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
internal static class NativeMessagePatch
{
    private static readonly List<NotificationHud.Notice> Queue = new();
    private static MessageHud owner;
    internal static bool Enabled { get; private set; }

    internal static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (enabled) return;
        Queue.Clear();
        owner = null;
    }

    private static void Postfix(MessageHud __instance, MessageHud.MessageType type, string text, int amount,
        Sprite icon, bool showDespiteHiddenHUD)
    {
        if (!Enabled || string.IsNullOrEmpty(text) || Hud.IsUserHidden() && !showDespiteHiddenHUD) return;
        if (type != MessageHud.MessageType.TopLeft && type != MessageHud.MessageType.Center) return;
        if (owner != __instance)
        {
            owner = __instance;
            Queue.Clear();
        }
        bool skill = text.StartsWith("$msg_skillup ", StringComparison.Ordinal);
        Queue.Add(new NotificationHud.Notice(Localization.instance.Localize(text), "", icon, amount, skill,
            showDespiteHiddenHUD, skill ? 5 : 4.5f));
        if (Queue.Count > 32) Queue.RemoveAt(0);
    }

    internal static IEnumerable<NotificationHud.Notice> Drain(MessageHud instance)
    {
        if (instance != owner) yield break;
        foreach (var notice in Queue) yield return notice;
        Queue.Clear();
    }
}
