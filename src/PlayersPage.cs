using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class PlayersPage
{
    private const float RowGap = 8;
    private readonly OnlinePlayers players;
    private readonly RectTransform list;
    private readonly Artwork art;
    private readonly TMP_FontAsset font;
    private readonly TextMeshProUGUI empty;
    private readonly List<Row> rows = new();
    private float nextRefresh;

    internal RectTransform Root { get; }

    internal PlayersPage(Transform parent, OnlinePlayers players, Artwork art, TMP_FontAsset font)
    {
        this.players = players;
        this.art = art;
        this.font = font;
        Root = ItemUi.Rect(parent, "Players");
        ItemUi.Stretch(Root, Vector2.zero, Vector2.zero);
        var scroll = Root.gameObject.AddComponent<WheelScrollRect>();
        var viewport = ItemUi.Rect(Root, "Viewport");
        ItemUi.Stretch(viewport, Vector2.zero, new Vector2(-14, 0));
        viewport.gameObject.AddComponent<RectMask2D>();
        var input = ItemUi.Image(viewport, "ScrollInput");
        input.color = Color.clear;
        input.raycastTarget = true;
        ItemUi.Stretch(input.rectTransform, Vector2.zero, Vector2.zero);
        list = ItemUi.Rect(viewport, "Rows");
        list.anchorMin = Vector2.up;
        list.anchorMax = Vector2.one;
        list.pivot = Vector2.up;
        scroll.viewport = viewport;
        scroll.content = list;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 34;
        var rail = HudStyle.Scrollbar(Root, art, "Scrollbar");
        var railRect = (RectTransform)rail.transform;
        railRect.anchorMin = Vector2.right;
        railRect.anchorMax = Vector2.one;
        railRect.offsetMin = new Vector2(-8, 0);
        railRect.offsetMax = Vector2.zero;
        scroll.verticalScrollbar = rail;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        empty = MenuStyle.Label(list, "Empty", font, MenuStyle.Translate("No other players online. This list shows players with Valheim New UI only.", "Других игроков онлайн нет. Здесь отображаются только игроки с Valheim New UI."),
            16, ItemUi.Muted, TextAlignmentOptions.Center);
        empty.textWrappingMode = TextWrappingModes.Normal;
        CraftingMenu.Put(empty.rectTransform, 4, 0, 4, 60);
        Root.gameObject.SetActive(false);
    }

    internal void Refresh()
    {
        if (!Root.gameObject.activeInHierarchy || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.3f;
        var peers = players.Peers;
        int count = 0;
        float top = 0;
        // The player's own card comes first, with the button that retakes the portrait.
        if (players.Self != null) top = Fill(count++, players.Self, top);
        float afterSelf = top;
        foreach (var peer in peers) top = Fill(count++, peer, top);
        for (int i = count; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(false);
        empty.enabled = peers.Count == 0;
        if (empty.enabled) CraftingMenu.Put(empty.rectTransform, 4, afterSelf + 8, 4, 60);
        list.sizeDelta = new Vector2(0, top + (empty.enabled ? 70 : 0));
    }

    // Each card is as tall as its status lines need, so a player without effects takes less room.
    private float Fill(int index, OnlinePlayers.Peer peer, float top)
    {
        if (index == rows.Count) rows.Add(new Row(list, art, font, players));
        float height = rows[index].Refresh(peer);
        CraftingMenu.Put(rows[index].Rect, 0, top, 0, height);
        return top + height + RowGap;
    }

    private sealed class Row
    {
        private const int MaxEntries = 6;
        private readonly TextMeshProUGUI name, level, location, metrics, action;
        private readonly Button button;
        private readonly RawImage portrait;
        private readonly Image silhouette, well, healthFill;
        private readonly Image[] icons = new Image[MaxEntries];
        private readonly TextMeshProUGUI[] labels = new TextMeshProUGUI[MaxEntries];
        private readonly OnlinePlayers players;
        private OnlinePlayers.Peer peer;
        internal readonly RectTransform Rect;

        internal Row(RectTransform parent, Artwork art, TMP_FontAsset font, OnlinePlayers players)
        {
            this.players = players;
            Rect = ItemUi.Image(parent, "PlayerCard", art["slot"]).rectTransform;
            well = ItemUi.Image(Rect, "PortraitWell", art["slot"]);
            ItemUi.TopLeft(well.rectTransform, 10, 12, 68, 68);
            silhouette = ItemUi.Image(well.transform, "PortraitPlaceholder", art["player-portrait"]);
            silhouette.color = ItemUi.Muted;
            ItemUi.Stretch(silhouette.rectTransform, Vector2.one * 8, Vector2.one * -8);
            portrait = ItemUi.Rect(well.transform, "Portrait").gameObject.AddComponent<RawImage>();
            portrait.raycastTarget = false;
            ItemUi.Stretch(portrait.rectTransform, Vector2.one * 2, Vector2.one * -2);
            var track = ItemUi.Image(Rect, "HealthTrack", art["meter-fill"]);
            track.color = new Color32(47, 28, 26, 225);
            ItemUi.TopLeft(track.rectTransform, 10, 86, 68, 5);
            healthFill = ItemUi.Image(track.transform, "Health", art["meter-fill"]);
            healthFill.color = ItemUi.Warning;
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            ItemUi.Stretch(healthFill.rectTransform, Vector2.zero, Vector2.zero);

            name = MenuStyle.Label(Rect, "Name", HudStyle.DisplayFont(font), "", 20, ItemUi.Ink);
            name.richText = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            CraftingMenu.Put(name.rectTransform, 90, 6, 260, 26);
            // Character level: every skill level added up and rounded down.
            level = MenuStyle.Label(Rect, "Level", HudStyle.BrandFont(font), "", 17, ItemUi.Accent, TextAlignmentOptions.Right);
            var levelRect = level.rectTransform;
            levelRect.anchorMin = levelRect.anchorMax = levelRect.pivot = Vector2.one;
            levelRect.sizeDelta = new Vector2(110, 24);
            levelRect.anchoredPosition = new Vector2(-144, -8);
            location = MenuStyle.Label(Rect, "Location", font, "", 14, ItemUi.Accent);
            CraftingMenu.Put(location.rectTransform, 90, 33, 8, 20);
            metrics = MenuStyle.Label(Rect, "Metrics", font, "", 13, ItemUi.Muted);
            metrics.textWrappingMode = TextWrappingModes.NoWrap;
            CraftingMenu.Put(metrics.rectTransform, 90, 53, 8, 19);

            // Status effects with names and time left, two rows of three.
            for (int k = 0; k < MaxEntries; k++)
            {
                float x = 90 + (k % 3) * 150, y = 78 + (k / 3) * 24;
                icons[k] = ItemUi.Image(Rect, "StatusIcon", null);
                icons[k].preserveAspect = true;
                ItemUi.TopLeft(icons[k].rectTransform, x, y, 20, 20);
                labels[k] = MenuStyle.Label(Rect, "Status", font, "", 13, ItemUi.Ink);
                labels[k].textWrappingMode = TextWrappingModes.NoWrap;
                ItemUi.TopLeft(labels[k].rectTransform, x + 24, y, 122, 20);
            }

            button = MenuStyle.Button(Rect, art, font, "", OnButton, out action, 13);
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.one;
            buttonRect.sizeDelta = new Vector2(128, 26);
            buttonRect.anchoredPosition = new Vector2(-8, -7);
        }

        private void OnButton()
        {
            if (peer == null) return;
            if (peer.IsSelf) players.Cards.Retake();
            else players.TogglePin(peer.Key);
        }

        internal float Refresh(OnlinePlayers.Peer player)
        {
            peer = player;
            Rect.gameObject.SetActive(true);
            name.text = player.IsSelf ? player.Name + MenuStyle.Translate("  (you)", "  (вы)") : player.Name;
            level.text = string.Format(MenuStyle.Translate("Level {0}", "Уровень {0}"), player.TotalLevel);
            portrait.texture = player.Portrait;
            portrait.enabled = player.Portrait;
            portrait.color = player.Dead ? new Color32(115, 110, 110, 255) : Color.white;
            silhouette.enabled = !portrait.enabled;
            well.color = player.Pinned ? ItemUi.Accent : Color.white;
            healthFill.fillAmount = player.HealthFraction;

            var parts = new List<string>(4);
            if (!player.IsSelf) parts.Add(player.Distance is float meters ? OnlinePlayers.Meters(meters) : "—");
            // The biome of a distant, unloaded area is unknown; a lone dash there reads as missing data.
            if (OnlinePlayers.BiomeName(player.Biome) is string biome) parts.Add(biome);
            parts.Add(player.Dead ? MenuStyle.Translate("Dead", "Погиб")
                : $"{MenuStyle.Translate("Health", "Здоровье")} {Mathf.CeilToInt(player.Health)} / {Mathf.CeilToInt(player.MaxHealth)}");
            location.text = string.Join("   ·   ", parts);
            string skill = player.Skill == Skills.SkillType.None ? "—"
                : Localization.instance.Localize("$skill_" + player.Skill.ToString().ToLowerInvariant()) + $" {player.SkillLevel:0.#}";
            metrics.text = string.Format(MenuStyle.Translate("Armor {0}    Best skill {1}", "Броня {0}    Лучший навык {1}"),
                player.Armor.ToString("0.#"), skill);

            int shown = 0;
            if (player.Power is PlayerCardSync.Effect power && power.Icon)
            {
                bool ready = power.Left <= 0;
                Entry(shown++, power.Icon, Localization.instance.Localize(power.Name),
                    ready ? MenuStyle.Translate("ready", "готова") : StatusEffect.GetTimeString(Mathf.Ceil(power.Left), false, true),
                    ready ? ItemUi.Accent : ItemUi.Muted, ready ? 1 : 0.5f);
            }
            if (player.Effects != null)
            {
                foreach (var effect in player.Effects)
                {
                    if (shown == MaxEntries) break;
                    if (player.Power != null && effect.Hash == player.Power.Hash) continue;
                    float left = effect.Left;
                    Entry(shown++, effect.Icon, Localization.instance.Localize(effect.Name),
                        left >= 0 ? StatusEffect.GetTimeString(Mathf.Ceil(left), false, true) : "", ItemUi.Ink, 1);
                }
            }
            for (int k = shown; k < MaxEntries; k++) icons[k].enabled = labels[k].enabled = false;

            if (player.IsSelf)
            {
                bool busy = players.Cards.Capturing;
                action.text = busy ? MenuStyle.Translate("Taking photo…", "Снимаю фото…") : MenuStyle.Translate("Retake photo", "Переснять фото");
                button.interactable = !busy;
            }
            else
            {
                action.text = player.Pinned ? MenuStyle.Translate("Unpin", "Открепить") : MenuStyle.Translate("Pin", "Закрепить");
                button.interactable = true;
            }
            // The portrait and its health bar need 100; each line of effects adds a row below them.
            return shown == 0 ? 100 : shown <= 3 ? 108 : 132;
        }

        private void Entry(int index, Sprite icon, string title, string time, Color color, float iconAlpha)
        {
            icons[index].sprite = icon;
            icons[index].color = new Color(1, 1, 1, iconAlpha);
            icons[index].enabled = true;
            labels[index].text = string.IsNullOrEmpty(time) ? title : $"{title}  <color=#{ColorUtility.ToHtmlStringRGB(ItemUi.Muted)}>{time}</color>";
            labels[index].color = color;
            labels[index].enabled = true;
        }
    }
}
