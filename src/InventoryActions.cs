using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimNewUI;

[HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
internal sealed class InventoryActions : IDisposable
{
    private static InventoryActions current;
    private readonly InventoryGui gui;
    private readonly Canvas canvas;
    private readonly RectTransform layer, menu;
    private readonly TextMeshProUGUI title;
    private readonly TextMeshProUGUI hint;
    private ItemDrop.ItemData selected;

    internal InventoryActions(InventoryGui gui, Canvas canvas, Transform parent, Artwork art, TMP_FontAsset font)
    {
        this.gui = gui;
        this.canvas = canvas;
        hint = MenuStyle.Label(parent, "InventoryActionsHint", font,
            MenuStyle.Translate("Right-click a resource for actions", "ПКМ по ресурсу — меню действий"),
            13, ItemUi.Muted);

        layer = ItemUi.Rect(canvas.transform, "InventoryActionsOverlay");
        ItemUi.Stretch(layer, Vector2.zero, Vector2.zero);
        var dismiss = ItemUi.Image(layer, "Dismiss");
        dismiss.color = Color.clear;
        dismiss.raycastTarget = true;
        ItemUi.Stretch(dismiss.rectTransform, Vector2.zero, Vector2.zero);
        dismiss.gameObject.AddComponent<InventoryActionDismiss>().Close = Close;
        menu = ItemUi.Image(layer, "ResourceActions", art["panel"]).rectTransform;
        menu.GetComponent<Image>().raycastTarget = true;
        title = MenuStyle.Label(menu, "ResourceName", font, "", 15, ItemUi.Accent);
        title.richText = false;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Ellipsis;
        ItemUi.TopLeft(title.rectTransform, 12, 5, 209, 23);
        var one = MenuStyle.Button(menu, art, font, MenuStyle.Translate("Drop 1", "Выбросить 1"),
            () => Drop(all: false), out _);
        var all = MenuStyle.Button(menu, art, font, MenuStyle.Translate("Drop all stacks", "Выбросить все стаки"),
            () => Drop(all: true), out _);
        ItemUi.TopLeft((RectTransform)one.transform, 8, 32, 216, 30);
        ItemUi.TopLeft((RectTransform)all.transform, 8, 67, 216, 30);
        Close();
        current = this;
    }

    internal bool IsOpen => layer.gameObject.activeSelf;

    internal void AlignHint(float x, float y, float width)
    {
        ItemUi.TopLeft(hint.rectTransform, x, y, width, 18);
    }

    internal void Refresh(bool open)
    {
        hint.enabled = open;
        if (!open || Menu.IsVisible() || Console.IsVisible() || gui.m_splitDialog.IsActive ||
            !Player.m_localPlayer || selected != null && !Player.m_localPlayer.GetInventory().ContainsItem(selected))
            Close();
    }

    private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, GameObject ___m_dragGo)
    {
        if (current == null || current.gui != __instance || !InventoryGui.IsVisible() || ___m_dragGo ||
            ZInput.IsExclusiveGamepadActive() || ZInput.IsTouchActive() ||
            grid != __instance.m_playerGrid || item?.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Material ||
            item.m_shared.m_questItem) return true;
        current.Open(item);
        return false;
    }

    private void Open(ItemDrop.ItemData item)
    {
        selected = item;
        title.text = Localization.instance.Localize(item.m_shared.m_name);
        layer.SetAsLastSibling();
        layer.gameObject.SetActive(true);
        UITooltip.HideTooltip();
        var rect = layer.rect;
        var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, ZInput.pointerPosition, camera, out var point);
        var position = point - new Vector2(rect.xMin, rect.yMax);
        ItemUi.TopLeft(menu, Mathf.Clamp(position.x + 5, 4, Mathf.Max(4, rect.width - 236)),
            Mathf.Clamp(-position.y + 5, 4, Mathf.Max(4, rect.height - 111)), 232, 106);
    }

    private void Drop(bool all)
    {
        var item = selected;
        Close();
        var player = Player.m_localPlayer;
        if (!player || player.IsDead() || !InventoryGui.IsVisible() || item == null) return;
        var inventory = player.GetInventory();
        if (!inventory.ContainsItem(item)) return;
        if (!all)
        {
            player.DropItem(inventory, item, 1);
            return;
        }

        var stacks = new List<ItemDrop.ItemData>();
        foreach (var candidate in inventory.GetAllItems())
            if (candidate.m_shared.m_itemType == item.m_shared.m_itemType && !candidate.m_shared.m_questItem
                && candidate.m_shared.m_name == item.m_shared.m_name)
                stacks.Add(candidate);
        foreach (var stack in stacks)
            if (inventory.ContainsItem(stack)) player.DropItem(inventory, stack, stack.m_stack);
    }

    private void Close()
    {
        selected = null;
        layer.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        if (current == this) current = null;
        ItemUi.Destroy(layer);
        ItemUi.Destroy(hint);
    }
}

internal sealed class InventoryActionDismiss : MonoBehaviour, IPointerDownHandler
{
    internal Action Close;
    public void OnPointerDown(PointerEventData eventData) => Close?.Invoke();
}
