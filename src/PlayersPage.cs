using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class PlayersPage
{
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
        var scroll = Root.gameObject.AddComponent<ScrollRect>();
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
        empty = MenuStyle.Label(Root, "Empty", font, MenuStyle.Translate("No other players online", "Других игроков онлайн нет"),
            17, ItemUi.Muted, TextAlignmentOptions.Center);
        CraftingMenu.Put(empty.rectTransform, 4, 8, 4, 38);
        Root.gameObject.SetActive(false);
    }

    internal void Refresh()
    {
        if (!Root.gameObject.activeInHierarchy || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.3f;
        var peers = players.Peers;
        empty.enabled = peers.Count == 0;
        for (int i = 0; i < peers.Count; i++)
        {
            if (i == rows.Count) rows.Add(new Row(list, art, font, players, i));
            rows[i].Refresh(peers[i]);
        }
        for (int i = peers.Count; i < rows.Count; i++) rows[i].Rect.gameObject.SetActive(false);
        list.sizeDelta = new Vector2(0, peers.Count * 88);
    }

    private sealed class Row
    {
        private readonly TextMeshProUGUI name, location, metrics, pin;
        private OnlinePlayers.Peer peer;
        internal readonly RectTransform Rect;

        internal Row(RectTransform parent, Artwork art, TMP_FontAsset font, OnlinePlayers players, int i)
        {
            Rect = ItemUi.Image(parent, "PlayerCard", art["slot"]).rectTransform;
            CraftingMenu.Put(Rect, 0, i * 88, 0, 82);
            name = MenuStyle.Label(Rect, "Name", HudStyle.DisplayFont(font), "", 19, ItemUi.Ink);
            name.richText = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            CraftingMenu.Put(name.rectTransform, 11, 5, 112, 24);
            location = MenuStyle.Label(Rect, "Location", font, "", 14, ItemUi.Accent);
            CraftingMenu.Put(location.rectTransform, 11, 30, 8, 20);
            metrics = MenuStyle.Label(Rect, "Metrics", font, "", 13, ItemUi.Muted);
            metrics.textWrappingMode = TextWrappingModes.Normal;
            CraftingMenu.Put(metrics.rectTransform, 11, 49, 8, 29);
            var button = MenuStyle.Button(Rect, art, font, "", () => players.TogglePin(peer.Key), out pin, 13);
            var buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = Vector2.one;
            buttonRect.sizeDelta = new Vector2(94, 24);
            buttonRect.anchoredPosition = new Vector2(-8, -6);
        }

        internal void Refresh(OnlinePlayers.Peer player)
        {
            peer = player;
            Rect.gameObject.SetActive(true);
            name.text = player.Name;
            string distance = player.Distance is float meters ? $"{meters:0} " + MenuStyle.Translate("m", "м") : "—";
            string biome = player.Biome is Heightmap.Biome area && area != Heightmap.Biome.None
                ? Localization.instance.Localize("$biome_" + area.ToString().ToLowerInvariant()) : "—";
            location.text = $"{distance}   ·   {biome}" + (player.Dead ? "   ·   " + MenuStyle.Translate("Dead", "Погиб") : "");
            metrics.text = MenuStyle.Translate("Armor —    Best skill —", "Броня —    Лучший навык —");
            pin.text = player.Pinned ? MenuStyle.Translate("Unpin", "Открепить") : MenuStyle.Translate("Pin", "Закрепить");
        }
    }
}
