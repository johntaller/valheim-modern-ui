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
    internal enum Kind { General, Skill, Pickup, Drop, Item, Building, Biome, Death }
    private static readonly System.Reflection.FieldInfo Unlocks = AccessTools.Field(typeof(MessageHud), "m_unlockMessages");
    private static readonly System.Reflection.FieldInfo Biome = AccessTools.Field(typeof(MessageHud), "m_biomeMsgInstance");
    private readonly RectTransform root;
    private readonly Toast skill;
    private readonly ScreenAnnouncement announcement;
    private readonly Toast[] messages, pickups, items, buildings;
    private readonly List<Notice> active = new(), gained = new(), itemDiscoveries = new(), buildingDiscoveries = new();
    private readonly List<HiddenGraphics> stock = new();
    private readonly Dictionary<GameObject, HiddenGraphics> popups = new();
    private readonly List<GameObject> expiredPopups = new();
    private MessageHud owner;
    private Notice skillNotice, biomeNotice, deathNotice;

    internal NotificationHud(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        root = ItemUi.Rect(parent, "Notifications");
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);
        announcement = new ScreenAnnouncement(root, art, font);
        skill = new Toast(root, art, font, Kind.Skill);
        messages = CreateToasts(Kind.General, MaxMessages, art, font);
        pickups = CreateToasts(Kind.Pickup, 3, art, font);
        items = CreateToasts(Kind.Item, MaxMessages, art, font);
        buildings = CreateToasts(Kind.Building, MaxMessages, art, font);
    }

    private Toast[] CreateToasts(Kind kind, int count, Artwork art, TMP_FontAsset font)
    {
        var toasts = new Toast[count];
        for (int i = 0; i < count; i++) toasts[i] = new Toast(root, art, font, kind);
        return toasts;
    }

    internal void Refresh(bool covered)
    {
        if (owner != MessageHud.instance)
        {
            Restore();
            active.Clear();
            gained.Clear();
            itemDiscoveries.Clear();
            buildingDiscoveries.Clear();
            skillNotice = biomeNotice = deathNotice = null;
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
        bool hidden = Hud.IsUserHidden();
        float width = root.rect.width;
        float column = Mathf.Clamp((width - 40) / 2, 110, 254);
        const float bottom = 211;
        skill.Show(!covered && !hidden ? skillNotice : null, now, 0, -173, 470);
        // Messages rise from the bottom; pickups hang below the action name under the
        // crosshair and stop above the highest message, so the two never cover each other.
        float messagesTop = Stack(messages, active, now, 0, bottom, 470, covered, hidden);
        Stack(pickups, gained, now, 0, -118, 232, covered, hidden, -1, messagesTop - root.rect.height * 0.5f + 6);
        const float discoveryBottom = 24;
        bool hasBuildings = buildingDiscoveries.Exists(notice => notice.Expires > now && (!hidden || notice.Force));
        Stack(items, itemDiscoveries, now, -24 - (hasBuildings ? column + 12 : 0), discoveryBottom, column, covered, hidden);
        Stack(buildings, buildingDiscoveries, now, -24, discoveryBottom, column, covered, hidden);
        bool death = deathNotice != null && now < deathNotice.Expires;
        announcement.Show(death ? deathNotice : !covered && !hidden ? biomeNotice : null, now);
    }

    // Returns where the next toast would go. A downward stack stops before crossing the limit.
    private static float Stack(Toast[] views, List<Notice> notices, float now, float x, float y, float width,
        bool covered, bool hidden, int direction = 1, float limit = float.NegativeInfinity)
    {
        notices.RemoveAll(notice => notice.Expires <= now);
        int shown = 0;
        if (!covered)
        {
            foreach (var notice in notices)
            {
                if (hidden && !notice.Force) continue;
                float height = views[shown++].Show(notice, now, x, y, width);
                if (direction < 0 && y - height * 0.5f < limit)
                {
                    views[--shown].Show(null, now, 0, 0, width);
                    break;
                }
                y += direction * (height + 5);
                if (shown == views.Length) break;
            }
        }
        while (shown < views.Length) views[shown++].Show(null, now, 0, 0, width);
        return y;
    }

    private void Add(Notice notice)
    {
        if (notice.Type == Kind.Skill)
        {
            skillNotice = notice;
            return;
        }
        if (notice.Type == Kind.Biome)
        {
            biomeNotice = notice;
            return;
        }
        if (notice.Type == Kind.Death)
        {
            deathNotice = notice;
            return;
        }
        var list = notice.Type switch
        {
            Kind.Pickup or Kind.Drop => gained,
            Kind.Item => itemDiscoveries,
            Kind.Building => buildingDiscoveries,
            _ => active
        };
        int repeated = list.FindIndex(other => other.Type == notice.Type && other.Title == notice.Title && other.Subtitle == notice.Subtitle && other.Icon == notice.Icon);
        if (repeated >= 0)
        {
            var previous = list[repeated];
            if (notice.Amount > 0 && previous.Expires > notice.Created) notice.Amount += previous.Amount;
            list.RemoveAt(repeated);
        }
        list.Insert(0, notice);
        if (list.Count > MaxMessages) list.RemoveAt(list.Count - 1);
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
        Kind kind = unlock ? ClassifyDiscovery(title.text) : Kind.Biome;
        Add(new Notice(title.text, details ? details.text : "", icon ? icon.sprite : null, 0, kind, false, unlock ? 6 : 9));
        var hidden = new HiddenGraphics(popup);
        hidden.SetRaycasts(false);
        popups.Add(popup, hidden);
    }

    private static Kind ClassifyDiscovery(string title)
    {
        var locale = Localization.instance;
        if (title == locale.Localize("$msg_newpiece") || title == locale.Localize("$msg_newstation")
            || title == locale.Localize("$msg_newstation_level")) return Kind.Building;
        if (title == locale.Localize("$msg_newitem") || title == locale.Localize("$msg_newrecipe")
            || title == locale.Localize("$msg_newmaterial") || title == locale.Localize("$msg_newtrophy")
            || title == locale.Localize("$msg_newdish")) return Kind.Item;
        return Kind.General;
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
        announcement.Dispose();
        ItemUi.Destroy(root);
    }

    internal sealed class Notice
    {
        internal readonly string Title, Subtitle;
        internal readonly Sprite Icon;
        internal readonly Kind Type;
        internal readonly bool Force;
        internal readonly float Created, Expires;
        internal int Amount;

        internal Notice(string title, string subtitle, Sprite icon, int amount, Kind kind, bool force, float duration)
        {
            Title = title;
            Subtitle = subtitle;
            Icon = icon;
            Amount = amount;
            Type = kind;
            Force = force;
            Created = Time.unscaledTime;
            Expires = Created + duration;
        }
    }

    private sealed class Toast
    {
        private readonly RectTransform rect;
        private readonly CanvasGroup group;
        private readonly TextMeshProUGUI title, subtitle, plus;
        private readonly Image icon, line;
        private readonly bool skill, pickup;
        private readonly HudSparks sparks;
        private Notice current;
        private float width, height;

        internal Toast(RectTransform parent, Artwork art, TMP_FontAsset font, Kind kind)
        {
            skill = kind == Kind.Skill;
            pickup = kind == Kind.Pickup;
            var card = pickup ? HudStyle.Shadow(parent, art, 0.92f) : ItemUi.Image(parent, kind.ToString(), art["item-card"]);
            rect = card.rectTransform;
            bool discovery = kind is Kind.Item or Kind.Building;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(discovery ? 1 : 0.5f, skill ? 1 : pickup ? 0.5f : 0);
            group = card.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            if (skill)
            {
                card.enabled = false;
                var shadow = HudStyle.Shadow(rect, art, 0.95f);
                HudStyle.Center(shadow.rectTransform, 0, -25, 390, 140);
                var halo = ItemUi.Rect(rect, "SkillHalo");
                // A soft golden rim, with animated motes behind the text.
                var glow = halo.gameObject.AddComponent<GlowGraphic>();
                glow.raycastTarget = false;
                HudStyle.Center(halo, 0, -25, 340, 106);
                sparks = HudSparks.Create(rect, "GoldMotes", 340, 120, 27);
                sparks.rectTransform.anchoredPosition = new Vector2(0, -25);
            }
            line = ItemUi.Image(rect, "Accent");
            line.color = ItemUi.Accent;
            icon = ItemUi.Image(rect, "Icon");
            icon.preserveAspect = true;
            title = ItemUi.Text(rect, "Title", font, skill ? 21 : pickup ? 14 : 16, ItemUi.Ink);
            title.fontStyle = FontStyles.Bold;
            title.overflowMode = TextOverflowModes.Ellipsis;
            subtitle = ItemUi.Text(rect, "Subtitle", font, 14, ItemUi.Muted);
            subtitle.overflowMode = TextOverflowModes.Ellipsis;
            if (pickup)
            {
                plus = ItemUi.Text(rect, "Plus", font, 20, ItemUi.Accent);
                plus.alignment = TextAlignmentOptions.Center;
                plus.text = "+";
            }
            rect.gameObject.SetActive(false);
        }

        internal float Show(Notice notice, float now, float x, float y, float maxWidth)
        {
            bool visible = notice != null && now < notice.Expires;
            if (rect.gameObject.activeSelf != visible) rect.gameObject.SetActive(visible);
            if (!visible) return 0;
            float available = Mathf.Max(100, Mathf.Min(maxWidth, ((RectTransform)rect.parent).rect.width - 32));
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
                int left = pickup ? (hasIcon ? 56 : 31) : hasIcon ? (skill ? 55 : 45) : 14;
                float textWidth = width - left - 14;
                float main = Mathf.Min(skill ? 50 : 42, Mathf.Ceil(title.GetPreferredValues(title.text, textWidth, Mathf.Infinity).y));
                float secondary = hasSubtitle ? Mathf.Min(36, Mathf.Ceil(subtitle.GetPreferredValues(notice.Subtitle, textWidth, Mathf.Infinity).y)) : 0;
                float measured = Mathf.Max(title.GetPreferredValues(title.text).x, subtitle.GetPreferredValues(notice.Subtitle).x);
                float cardWidth = Mathf.Clamp(Mathf.Ceil(measured + left + 14), Mathf.Min(skill ? 215 : 142, width), width);
                height = Mathf.Max(skill ? 56 : pickup ? 27 : 35, main + secondary + (hasSubtitle ? 16 : 8));
                float margin = pickup ? 16 : 0;
                rect.sizeDelta = new Vector2(cardWidth + margin * 2, height + margin * 2);
                int iconSize = skill ? 34 : pickup ? 22 : 25;
                ItemUi.TopLeft(icon.rectTransform, margin + (pickup ? 28 : 10), margin + (height - iconSize) / 2, iconSize, iconSize);
                ItemUi.TopLeft(line.rectTransform, 12, 0, cardWidth - 24, 2);
                float top = margin + (height - main - secondary) / 2;
                title.alignment = hasIcon || pickup ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
                subtitle.alignment = title.alignment;
                ItemUi.TopLeft(title.rectTransform, margin + left, top, cardWidth - left - 14, main);
                ItemUi.TopLeft(subtitle.rectTransform, margin + left, top + main, cardWidth - left - 14, secondary);
                if (pickup)
                {
                    bool dropped = notice.Type == Kind.Drop;
                    plus.text = dropped ? "−" : "+";
                    plus.color = dropped ? (Color)ItemUi.Warning : ItemUi.Accent;
                    ItemUi.TopLeft(plus.rectTransform, margin + 7, margin, 18, height);
                }
            }
            group.alpha = Mathf.Clamp01((notice.Expires - now) / 0.55f);
            if (skill)
                sparks.Animate(now - notice.Created, new Color32(255, 200, 99, 255), 142, 16);
            if (pickup)
            {
                float t = Mathf.Clamp01((now - notice.Created) / 0.38f);
                float remaining = t - 1;
                x += (notice.Type == Kind.Drop ? -55 : 55) * remaining * remaining * (2.3f * remaining + 1.3f);
                group.alpha *= Mathf.Clamp01(t * 4);
            }
            rect.anchoredPosition = new Vector2(x, y);
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

    // Valheim overwrites the text argument with its translation inside ShowMessage.
    private static void Prefix(MessageHud __instance, MessageHud.MessageType type, string text, int amount,
        Sprite icon, bool showDespiteHiddenHUD)
    {
        if (!Enabled || string.IsNullOrEmpty(text) || Hud.IsUserHidden() && !showDespiteHiddenHUD) return;
        if (type != MessageHud.MessageType.TopLeft && type != MessageHud.MessageType.Center || RestHud.IsStatusMessage(text)) return;
        if (text == "$msg_youdied" || text == "$msg_softdeath") return;
        bool skill = text.StartsWith("$msg_skillup ", StringComparison.Ordinal);
        bool pickup = text.StartsWith("$msg_added ", StringComparison.Ordinal);
        bool dropped = text.StartsWith("$msg_dropped ", StringComparison.Ordinal);
        var kind = skill ? NotificationHud.Kind.Skill : pickup ? NotificationHud.Kind.Pickup
            : dropped ? NotificationHud.Kind.Drop : NotificationHud.Kind.General;
        if (pickup || dropped) text = text.Substring((pickup ? "$msg_added " : "$msg_dropped ").Length);
        Enqueue(__instance, new NotificationHud.Notice(Localization.instance.Localize(text), "", icon, amount, kind,
            showDespiteHiddenHUD, skill ? 5 : pickup || dropped ? 2.6f : 4.5f));
    }

    internal static void Enqueue(MessageHud instance, NotificationHud.Notice notice)
    {
        if (!Enabled || !instance) return;
        if (owner != instance)
        {
            owner = instance;
            Queue.Clear();
        }
        Queue.Add(notice);
        if (Queue.Count > 32) Queue.RemoveAt(0);
    }

    internal static IEnumerable<NotificationHud.Notice> Drain(MessageHud instance)
    {
        if (instance != owner) yield break;
        foreach (var notice in Queue) yield return notice;
        Queue.Clear();
    }
}
