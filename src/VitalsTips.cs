using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimNewUI;

// Cards for the vitals while the inventory is open: hovering the health gauge, a food, the rested
// or guardian power slot or a status effect explains it in the same card the items use.
internal sealed class VitalsTips
{
    private const string Highlight = "#E8BC77";
    private static readonly AccessTools.FieldRef<Player, float> RegenTimer =
        AccessTools.FieldRefAccess<Player, float>("m_foodRegenTimer");

    private readonly Canvas canvas;
    private readonly ItemTooltip card;
    private readonly StringBuilder text = new();

    internal VitalsTips(Canvas canvas, Artwork art, TMP_FontAsset font)
    {
        this.canvas = canvas;
        card = new ItemTooltip(canvas, art, font);
    }

    internal Camera Camera => canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;

    // The pointer, or nothing when a gamepad drives the menus and no pointer is shown.
    internal static bool Pointer(out Vector2 screen)
    {
        screen = ZInput.pointerPosition;
        return !ZInput.IsExclusiveGamepadActive();
    }

    internal void Clear() => card.Clear();

    // Health: how much it recovers on each tick of the food and when the next one comes.
    internal void Health(Player player, Vector2 screen)
    {
        text.Clear();
        float max = player.GetMaxHealth(), current = Mathf.Clamp(player.GetHealth(), 0, max);
        Line(MenuStyle.Translate("Health", "Здоровье"), $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}");
        float regen = 0;
        var foods = player.GetFoods();
        foreach (var food in foods) regen += food.m_item.m_shared.m_foodRegen;
        float factor = 1;
        player.GetSEMan().ModifyHealthRegen(ref factor);
        if (regen <= 0)
        {
            text.Append('\n').Append(MenuStyle.Translate("Without food, health does not recover.",
                "Без еды здоровье не восстанавливается."));
        }
        else
        {
            Line(MenuStyle.Translate("Recovery per tick", "Восстановление за тик"), $"+{regen * factor:0.#}");
            Line(MenuStyle.Translate("Tick", "Тик"), MenuStyle.Translate("every 10 s", "каждые 10 с"));
            float next = Mathf.Clamp(10 - RegenTimer(player), 0, 10);
            Line(MenuStyle.Translate("Next in", "Следующий через"), $"{Mathf.CeilToInt(next)} " + MenuStyle.Translate("s", "с"));
            if (Mathf.Abs(factor - 1) > 0.005f)
                Line(MenuStyle.Translate("Effects", "Эффекты"), $"×{factor:0.##}");
            text.Append("\n\n").Append(MenuStyle.Translate("From food:", "От еды:"));
            foreach (var food in foods)
                text.Append("\n  ").Append(Localization.instance.Localize(food.m_item.m_shared.m_name))
                    .Append(": <color=").Append(Highlight).Append(">+").Append(food.m_item.m_shared.m_foodRegen.ToString("0.#"))
                    .Append("</color>");
        }
        card.RefreshInfo(null, MenuStyle.Translate("Health", "Здоровье"), text.ToString(), "", screen);
    }

    // A food: its own item card, with the time it has left.
    internal void Food(Player.Food food, Vector2 screen)
    {
        string note = food.CanEatAgain()
            ? MenuStyle.Translate("Can be eaten again", "Можно съесть снова")
            : MenuStyle.Translate("Time left: ", "Осталось: ") + StatusEffect.GetTimeString(Mathf.Ceil(food.m_time), false, true);
        card.RefreshItem(food.m_item, note, screen);
    }

    // Rested: what it gives and how to get it, or to keep it longer.
    internal void Rested(Player player, Vector2 screen)
    {
        var active = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
        var template = ObjectDB.instance ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested) : null;
        var effect = active ? active : template;
        text.Clear();
        if (effect) text.Append(Localization.instance.Localize(effect.GetTooltipString()).Trim());
        int comfort = player.GetComfortLevel();
        text.Append("\n");
        Line(MenuStyle.Translate("Comfort", "Комфорт"), comfort.ToString());
        if (effect is SE_Rested rested)
        {
            float duration = rested.m_baseTTL + Mathf.Max(0, comfort - 1) * rested.m_TTLPerComfortLevel;
            Line(MenuStyle.Translate("Lasts at this comfort", "Длится при этом комфорте"),
                StatusEffect.GetTimeString(duration, false, true));
        }
        text.Append("\n\n<color=").Append(Highlight).Append('>').Append(MenuStyle.Translate("How to get it", "Как получить"))
            .Append("</color>\n").Append(MenuStyle.Translate(
                "Sit or rest by a fire under a roof, sheltered from the weather. Beds, chairs, tables, banners and rugs nearby raise comfort, and each level of comfort makes the rest last longer.",
                "Посидите или отдохните у огня под крышей, в укрытии от непогоды. Кровати, стулья, столы, знамёна и ковры рядом повышают комфорт, и каждый его уровень продлевает отдых."));
        string title = effect ? Localization.instance.Localize(effect.m_name) : MenuStyle.Translate("Rested", "Отдых");
        string note = active ? MenuStyle.Translate("Time left: ", "Осталось: ") + active.GetIconText()
            : MenuStyle.Translate("Not rested", "Нет отдыха");
        card.RefreshInfo(effect ? effect.m_icon : null, title, text.ToString(), note, screen);
    }

    // The guardian power: what it grants, its recharge, and the key that calls it.
    internal void Power(Player player, Vector2 screen)
    {
        player.GetGuardianPowerHUD(out var power, out float cooldown);
        if (!power)
        {
            card.RefreshInfo(null, MenuStyle.Translate("Forsaken power", "Сила Отрёкшихся"), MenuStyle.Translate(
                "Defeat a forsaken and offer its trophy at the stone in the starting area to take its power.",
                "Победите Отрёкшегося и положите его трофей на камень в начальной области, чтобы взять его силу."), "", screen);
            return;
        }
        text.Clear();
        text.Append(Localization.instance.Localize(power.GetTooltipString()).Trim()).Append('\n');
        Line(MenuStyle.Translate("Recharge", "Перезарядка"), StatusEffect.GetTimeString(power.m_cooldown, false, true));
        string note = cooldown > 0
            ? MenuStyle.Translate("Ready in ", "Готова через ") + StatusEffect.GetTimeString(Mathf.Ceil(cooldown), false, true)
            : Localization.instance.Localize("$hud_ready") + " · " + MenuStyle.Translate("Use: ", "Применить: ")
              + Localization.instance.Localize("$KEY_GP");
        card.RefreshInfo(power.m_icon, Localization.instance.Localize(power.m_name), text.ToString(), note, screen);
    }

    // A status effect: what it does to the player right now.
    internal void Effect(StatusEffect effect, Vector2 screen)
    {
        string details = Localization.instance.Localize(effect.GetTooltipString()).Trim();
        string note = effect.m_ttl > 0
            ? MenuStyle.Translate("Time left: ", "Осталось: ") + StatusEffect.GetTimeString(Mathf.Ceil(effect.GetRemaningTime()), false, true)
            : "";
        card.RefreshInfo(effect.m_icon, Localization.instance.Localize(effect.m_name), details, note, screen);
    }

    private void Line(string label, string value)
    {
        if (text.Length > 0) text.Append('\n');
        text.Append(label).Append(": <color=").Append(Highlight).Append('>').Append(value).Append("</color>");
    }

    internal void Dispose() => card.Dispose();
}
