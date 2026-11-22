using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class CharacterPage
{
    private readonly TextMeshProUGUI health, stamina, eitr, armor, weight, comfort, power;
    private readonly TextMeshProUGUI[] foods = new TextMeshProUGUI[3], times = new TextMeshProUGUI[3];
    private readonly Image[] icons = new Image[3], fills = new Image[3];
    private readonly Image powerIcon;
    private float nextUpdate;

    internal RectTransform Root { get; }

    internal CharacterPage(Transform parent, Artwork art, TMP_FontAsset font)
    {
        Root = ItemUi.Rect(parent, "Character");
        ItemUi.Stretch(Root, Vector2.zero, Vector2.zero);

        var metrics = ItemUi.Rect(Root, "Vitals");
        CraftingMenu.Put(metrics, 0, 0, 0, 138);
        var tiles = metrics.gameObject.AddComponent<GridLayoutGroup>();
        tiles.spacing = new Vector2(7, 7);
        tiles.cellSize = new Vector2(160, 60);
        tiles.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        tiles.constraintCount = 3;
        metrics.gameObject.AddComponent<CharacterMetricLayout>().Grid = tiles;
        health = Tile("Health", "Здоровье", new Color32(235, 143, 132, 255));
        stamina = Tile("Stamina", "Выносливость", ItemUi.Accent);
        eitr = Tile("Eitr", "Эйтр", new Color32(187, 167, 240, 255));
        armor = Tile("Armor", "Броня", ItemUi.Ink);
        weight = Tile("Carry weight", "Вес", ItemUi.Ink);
        comfort = Tile("Comfort", "Комфорт", ItemUi.Ink);

        var heading = MenuStyle.Label(Root, "FoodTitle", font, MenuStyle.Translate("Food", "Еда"), 17, ItemUi.Accent);
        CraftingMenu.Put(heading.rectTransform, 4, 142, 4, 22);
        for (int i = 0; i < foods.Length; i++)
        {
            var row = ItemUi.Image(Root, "FoodRow", art["slot"]).rectTransform;
            CraftingMenu.Put(row, 0, 171 + i * 47, 0, 42);
            icons[i] = ItemUi.Image(row, "FoodIcon");
            icons[i].preserveAspect = true;
            ItemUi.TopLeft(icons[i].rectTransform, 7, 5, 31, 31);
            foods[i] = MenuStyle.Label(row, "FoodName", font, "", 15, ItemUi.Ink);
            CraftingMenu.Put(foods[i].rectTransform, 46, 2, 75, 27);
            var track = ItemUi.Image(row, "RemainingTrack", art["meter-fill"]);
            track.color = MenuStyle.Edge;
            CraftingMenu.Put(track.rectTransform, 46, 32, 74, 3);
            fills[i] = ItemUi.Image(track.transform, "Remaining", art["meter-fill"]);
            fills[i].color = ItemUi.Accent;
            fills[i].type = Image.Type.Filled;
            fills[i].fillMethod = Image.FillMethod.Horizontal;
            ItemUi.Stretch(fills[i].rectTransform, Vector2.zero, Vector2.zero);
            times[i] = MenuStyle.Label(row, "Time", font, "", 15, ItemUi.Muted, TextAlignmentOptions.MidlineRight);
            ItemUi.Stretch(times[i].rectTransform, new Vector2(0, 3), new Vector2(-12, -3));
        }

        var guardian = ItemUi.Image(Root, "ForsakenPower", art["slot"]).rectTransform;
        CraftingMenu.Put(guardian, 0, 325, 0, 64);
        powerIcon = ItemUi.Image(guardian, "PowerIcon");
        powerIcon.preserveAspect = true;
        ItemUi.TopLeft(powerIcon.rectTransform, 11, 10, 44, 44);
        var powerLabel = MenuStyle.Label(guardian, "PowerTitle", font,
            MenuStyle.Translate("Forsaken power", "Сила отрёкшегося"), 13, ItemUi.Muted);
        CraftingMenu.Put(powerLabel.rectTransform, 67, 8, 9, 19);
        power = MenuStyle.Label(guardian, "PowerValue", font, "", 17, ItemUi.Accent);
        CraftingMenu.Put(power.rectTransform, 67, 29, 9, 25);

        TextMeshProUGUI Tile(string name, string russian, Color tint)
        {
            var box = ItemUi.Image(metrics, name, art["slot"]).rectTransform;
            var label = MenuStyle.Label(box, "Metric", font, MenuStyle.Translate(name, russian), 13, ItemUi.Muted);
            CraftingMenu.Put(label.rectTransform, 11, 6, 9, 18);
            var value = MenuStyle.Label(box, "Value", font, "", 21, tint);
            CraftingMenu.Put(value.rectTransform, 11, 26, 9, 26);
            return value;
        }
    }

    internal void Refresh(Player player)
    {
        if (!Root.gameObject.activeInHierarchy || Time.unscaledTime < nextUpdate) return;
        nextUpdate = Time.unscaledTime + 0.25f;
        health.text = Pair(player.GetHealth(), player.GetMaxHealth());
        stamina.text = Pair(player.GetStamina(), player.GetMaxStamina());
        eitr.text = Pair(player.GetEitr(), player.GetMaxEitr());
        armor.text = player.GetBodyArmor().ToString("0.#");
        weight.text = Pair(player.GetInventory().GetTotalWeight(), player.GetMaxCarryWeight());
        comfort.text = player.GetComfortLevel().ToString();

        var localize = Localization.instance;
        var food = player.GetFoods();
        for (int i = 0; i < foods.Length; i++)
        {
            var portion = i < food.Count ? food[i] : null;
            bool active = portion?.m_item != null;
            icons[i].enabled = active;
            fills[i].enabled = active;
            foods[i].text = active ? localize.Localize(portion.m_item.m_shared.m_name)
                : MenuStyle.Translate("Empty food slot", "Свободный слот еды");
            times[i].text = active ? TimeLeft(portion.m_time) : "—";
            if (!active) continue;
            icons[i].sprite = portion.m_item.GetIcon();
            fills[i].fillAmount = Mathf.Clamp01(portion.m_time / Mathf.Max(1, portion.m_item.m_shared.m_foodBurnTime));
        }

        player.GetGuardianPowerHUD(out var effect, out float remaining);
        powerIcon.enabled = effect && effect.m_icon;
        if (effect) powerIcon.sprite = effect.m_icon;
        power.text = effect ? localize.Localize(effect.m_name) + "  ·  " + (remaining > 0
            ? TimeLeft(remaining) : MenuStyle.Translate("Ready", "Готово")) : "—";
    }

    private static string Pair(float value, float max) => $"{value:0.#} / {max:0.#}";

    private static string TimeLeft(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{whole / 60}:{whole % 60:00}";
    }
}

internal sealed class CharacterMetricLayout : MonoBehaviour
{
    internal GridLayoutGroup Grid;
    private void OnRectTransformDimensionsChange()
    {
        if (!Grid) return;
        var size = Grid.cellSize;
        size.x = Mathf.Max(1, (((RectTransform)transform).rect.width - Grid.spacing.x * 2) / 3);
        Grid.cellSize = size;
    }

    private void OnEnable() => OnRectTransformDimensionsChange();
}
