using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class VitalsView
{
    internal const float Clearance = 166;
    private const float Height = 132;
    private readonly RectTransform root, aim, nativeHealth;
    private readonly Image healthFrame, healthTrack, healthFill, healthShade;
    private readonly TextMeshProUGUI healthValue, staminaValue, guardianName;
    private readonly Image reticle;
    private readonly StaminaArcs stamina;
    private readonly TimedSlot[] foods = new TimedSlot[3];
    private readonly TimedSlot rested, guardian;
    private readonly Button guardianTouch;
    private readonly StatusEffectHud effects;
    private readonly List<HiddenGraphics> hidden = new();
    private readonly HashSet<Graphic> hiddenText = new();
    private Sprite emptyFood, inactiveRested;
    private int lastHealth = -1, lastMaxHealth = -1, lastStamina = -1, lastMaxStamina = -1;

    internal VitalsView(Hud hud, Canvas canvas, Artwork art, Theme theme)
    {
        var canvasRect = (RectTransform)canvas.rootCanvas.transform;
        var font = HudStyle.DisplayFont(hud.m_healthText.font);
        nativeHealth = hud.m_healthBarRoot;
        root = ItemUi.Rect(canvasRect, "NewUI_Vitals");
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = new Vector2(22, 18);
        root.sizeDelta = new Vector2(440, Height);
        root.SetAsFirstSibling();
        root.gameObject.SetActive(false);
        aim = ItemUi.Rect(canvasRect, "NewUI_AimVitals");
        HudStyle.Center(aim, 0, 0, 60, 60);
        aim.SetAsFirstSibling();
        aim.gameObject.SetActive(false);
        reticle = hud.m_crosshair;
        try
        {
            var shade = HudStyle.Shadow(root, art, 0.86f);
            ItemUi.TopLeft(shade.rectTransform, -24, -47, 254, 221);
            healthShade = HudStyle.Shadow(root, art, 0.70f);
            var healthShadowRect = healthShade.rectTransform;
            healthShadowRect.anchorMin = healthShadowRect.anchorMax = healthShadowRect.pivot = Vector2.zero;
            healthShadowRect.anchoredPosition = new Vector2(-14, -14);
            healthFrame = ItemUi.Image(root, "HealthFrame", art["slot"]);
            var frame = healthFrame.rectTransform;
            frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.zero;
            frame.anchoredPosition = new Vector2(20, 26);
            healthTrack = ItemUi.Image(healthFrame.transform, "Track", art["meter-fill"]);
            healthFill = ItemUi.Image(healthTrack.transform, "Fill", art["meter-fill"]);
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Vertical;
            ItemUi.Stretch(healthFill.rectTransform, Vector2.zero, Vector2.zero);
            var cap = ItemUi.Image(frame, "HealthCap", art["reticle"]);
            cap.color = ItemUi.Accent;
            HudStyle.Center(cap.rectTransform, 0, 0, 16, 16);
            cap.rectTransform.anchorMin = cap.rectTransform.anchorMax = new Vector2(0.5f, 1);
            healthValue = Number(root, "Health", font, 18, theme.Text);
            ItemUi.TopLeft(healthValue.rectTransform, -11, 108, 82, 24);
            for (int i = 0; i < foods.Length; i++)
                foods[i] = new TimedSlot(root, art, font, "FoodSlot", 66, 43 * i, 36);

            rested = new TimedSlot(root, art, font, "Rested", 122, 0, 39);
            guardian = new TimedSlot(root, art, font, "GuardianPower", 122, 48, 39);
            guardianName = Number(root, "GuardianName", font, 13, ItemUi.Ink);
            guardianName.overflowMode = TextOverflowModes.Ellipsis;
            ItemUi.TopLeft(guardianName.rectTransform, 104, 101, 75, 16);
            guardianTouch = guardian.Icon.gameObject.AddComponent<Button>();
            guardianTouch.transition = Selectable.Transition.None;
            guardianTouch.navigation = new Navigation { mode = Navigation.Mode.None };
            guardianTouch.onClick.AddListener(() =>
            {
                if (ZInput.HasDoubleTapped() && Player.m_localPlayer) Player.m_localPlayer.StartGuardianPower();
            });
            effects = new StatusEffectHud(hud, root, art);
            stamina = aim.gameObject.AddComponent<StaminaArcs>();
            stamina.raycastTarget = false;
            staminaCounterShade = HudStyle.Shadow(aim, art, 0.85f);
            HudStyle.Center(staminaCounterShade.rectTransform, 0, -40, 140, 68);
            staminaValue = Number(aim, "Stamina", font, 18, theme.Text);
            HudStyle.Center(staminaValue.rectTransform, 0, -40, 122, 22);

            Apply(theme);
            Hide(hud.m_healthPanel);
            if (!hud.m_healthPanel || !hud.m_healthBarRoot.IsChildOf(hud.m_healthPanel)) Hide(hud.m_healthBarRoot);
            Hide(hud.m_staminaBar2Root);
            Hide(hud.m_foodBarRoot);
            Hide(hud.m_gpRoot);
            HideText(hud.m_healthText);
            HideText(hud.m_staminaText);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private readonly Image staminaCounterShade;

    internal void Apply(Theme theme)
    {
        float inset = Mathf.Clamp(theme.Padding + 1, 2, 7);
        ItemUi.Stretch(healthTrack.rectTransform, Vector2.one * inset, Vector2.one * -inset);
        healthFrame.color = theme.Frame;
        healthTrack.color = theme.Background;
        healthFill.color = Color.Lerp(theme.Health, ItemUi.Warning, 0.44f);
        healthValue.color = staminaValue.color = theme.Text;
        stamina.Style(theme);
    }

    internal void Refresh(Player player, bool visible, bool inventoryOpen, bool covered)
    {
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        if (visible)
        {
            float max = player.GetMaxHealth();
            float current = player.GetHealth();
            // Valheim updates the native bar's width from maximum health.
            // Use that length vertically, with space reserved for the hotbar on short screens.
            float healthLength = Mathf.Clamp(nativeHealth.rect.width, 24, ((RectTransform)root.parent).rect.height * 0.55f);
            if (healthFrame.rectTransform.sizeDelta.y != healthLength)
            {
                healthFrame.rectTransform.sizeDelta = new Vector2(20, healthLength);
                healthShade.rectTransform.sizeDelta = new Vector2(88, healthLength + 80);
            }
            healthFill.fillAmount = max > 0 ? Mathf.Clamp01(current / max) : 0;
            UpdateNumber(healthValue, current, max, ref lastHealth, ref lastMaxHealth);
            var food = player.GetFoods();
            if (!emptyFood && ObjectDB.instance)
            {
                var meat = ObjectDB.instance.GetItemPrefab("RawMeat");
                if (meat && meat.TryGetComponent<ItemDrop>(out var drop)) emptyFood = drop.m_itemData.GetIcon();
            }
            for (int i = 0; i < foods.Length; i++) foods[i].RefreshFood(i < food.Count ? food[i] : null, emptyFood);

            var rest = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
            if (!inactiveRested && ObjectDB.instance)
                inactiveRested = ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested)?.m_icon;
            rested.Refresh(rest ? rest.m_icon : inactiveRested, rest, rest ? rest.GetIconText() : "");
            player.GetGuardianPowerHUD(out var power, out float cooldown);
            string name = power ? Localization.instance.Localize(power.m_name) : "";
            if (guardianName.text != name) guardianName.text = name;
            guardian.Refresh(power ? power.m_icon : null, power && cooldown <= 0,
                !power ? "" : cooldown > 0 ? StatusEffect.GetTimeString(cooldown, false, false)
                    : Localization.instance.Localize("$hud_ready"));
            bool touch = power && !inventoryOpen && !covered && ZInput.IsTouchActive();
            guardian.Icon.raycastTarget = guardianTouch.enabled = touch;
            effects.Refresh(player, inventoryOpen && !covered);
        }

        float maximum = visible ? player.GetMaxStamina() : 0;
        float amount = visible ? player.GetStamina() : 0;
        bool show = visible && !covered && (inventoryOpen || reticle && reticle.isActiveAndEnabled && amount < maximum);
        if (aim.gameObject.activeSelf != show) aim.gameObject.SetActive(show);
        if (!show) return;
        if (reticle) aim.position = reticle.rectTransform.TransformPoint(reticle.rectTransform.rect.center);
        stamina.Value(amount, maximum);
        staminaValue.enabled = staminaCounterShade.enabled = inventoryOpen;
        if (inventoryOpen) UpdateNumber(staminaValue, amount, maximum, ref lastStamina, ref lastMaxStamina);
    }

    private void Hide(RectTransform source)
    {
        if (!source) return;
        var state = new HiddenGraphics(source.gameObject);
        state.SetRaycasts(false);
        hidden.Add(state);
    }

    private void HideText(Graphic text)
    {
        if (text && text.enabled && hiddenText.Add(text)) text.enabled = false;
    }

    internal void Dispose()
    {
        effects?.Dispose();
        foreach (var state in hidden) state.Restore();
        foreach (var text in hiddenText)
            if (text) text.enabled = true;
        ItemUi.Destroy(aim);
        ItemUi.Destroy(root);
    }

    private static TextMeshProUGUI Number(Transform parent, string name, TMP_FontAsset font, float size, Color color)
    {
        var text = ItemUi.Text(parent, name, font, size, color);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void UpdateNumber(TMP_Text label, float current, float max, ref int cachedCurrent, ref int cachedMax)
    {
        int capacity = Mathf.Max(0, Mathf.CeilToInt(max));
        int amount = Mathf.Clamp(Mathf.CeilToInt(current), 0, capacity);
        if (amount == cachedCurrent && capacity == cachedMax) return;
        label.SetText("{0} / {1}", amount, capacity);
        cachedCurrent = amount;
        cachedMax = capacity;
    }

    private sealed class TimedSlot
    {
        internal readonly Image Icon;
        private readonly Image timerShade;
        private readonly TextMeshProUGUI timer;
        private int lastSecond = -1;

        internal TimedSlot(Transform parent, Artwork art, TMP_FontAsset font, string name, float x, float y, float size)
        {
            var face = ItemUi.Image(parent, name, art["slot"]);
            ItemUi.TopLeft(face.rectTransform, x, y, size, size);
            Icon = ItemUi.Image(face.transform, "Icon");
            Icon.preserveAspect = true;
            ItemUi.Stretch(Icon.rectTransform, Vector2.one * 3, Vector2.one * -3);
            timerShade = HudStyle.Shadow(face.transform, art, 0.96f);
            ItemUi.TopLeft(timerShade.rectTransform, (size - 66) / 2, size - 13, 66, 26);
            timer = Number(face.transform, "Remaining", font, 14, ItemUi.Ink);
            ItemUi.TopLeft(timer.rectTransform, (size - 66) / 2, size - 9, 66, 18);
            timerShade.enabled = timer.enabled = false;
        }

        internal void RefreshFood(Player.Food food, Sprite empty)
        {
            RefreshIcon(food != null ? food.m_item.GetIcon() : empty, food != null);
            int second = food != null ? Mathf.CeilToInt(food.m_time) : -1;
            if (second == lastSecond) return;
            lastSecond = second;
            RefreshTimer(second >= 0 ? StatusEffect.GetTimeString(second, false, true) : "");
        }

        internal void Refresh(Sprite icon, bool active, string time)
        {
            RefreshIcon(icon, active);
            RefreshTimer(time);
        }

        private void RefreshIcon(Sprite icon, bool active)
        {
            Icon.sprite = icon;
            Icon.enabled = icon;
            Icon.color = active ? Color.white : new Color32(103, 93, 82, 180);
        }

        private void RefreshTimer(string time)
        {
            if (timer.text == time) return;
            timer.text = time;
            timerShade.enabled = timer.enabled = !string.IsNullOrEmpty(time);
        }
    }
}
