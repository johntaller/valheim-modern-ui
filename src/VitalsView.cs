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
    private const float GaugeWidth = 34;
    private readonly RectTransform healthRoot;
    private readonly HealthGauge gauge;
    private readonly LowHealthVignette vignette;
    private readonly Image healthShade;
    private readonly TextMeshProUGUI healthValue, healthMax, staminaValue, guardianName;
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
    // Health events: the previous reading, the length on screen and the phases of each effect.
    private Player watched;
    private float seenHealth = -1, seenMax = -1, shownLength, lengthVelocity;
    private float trail, trailHold, flashAt = -10, hurtAt = -10, growAt = -10;

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
            vignette = ItemUi.Rect(canvasRect, "NewUI_LowHealth").gameObject.AddComponent<LowHealthVignette>();
            vignette.raycastTarget = false;
            ItemUi.Stretch(vignette.rectTransform, Vector2.zero, Vector2.zero);
            vignette.rectTransform.SetAsFirstSibling();
            healthShade = HudStyle.Shadow(root, art, 0.70f);
            var healthShadowRect = healthShade.rectTransform;
            healthShadowRect.anchorMin = healthShadowRect.anchorMax = healthShadowRect.pivot = Vector2.zero;
            healthShadowRect.anchoredPosition = new Vector2(-22, -18);
            healthRoot = ItemUi.Rect(root, "Health");
            healthRoot.anchorMin = healthRoot.anchorMax = healthRoot.pivot = Vector2.zero;
            healthRoot.anchoredPosition = new Vector2(12, 14);
            healthRoot.sizeDelta = new Vector2(GaugeWidth, 120);
            gauge = ItemUi.Rect(healthRoot, "Gauge").gameObject.AddComponent<HealthGauge>();
            gauge.raycastTarget = false;
            ItemUi.Stretch(gauge.rectTransform, Vector2.zero, Vector2.zero);
            healthValue = Number(healthRoot, "Value", font, 19, HealthInk);
            healthValue.enableAutoSizing = true;
            healthValue.fontSizeMin = 12;
            healthValue.fontSizeMax = 19;
            HudStyle.Center(healthValue.rectTransform, 0, 0, GaugeWidth - 6, 22);
            healthMax = Number(healthRoot, "Maximum", font, 13, HealthInk);
            healthMax.enableAutoSizing = true;
            healthMax.fontSizeMin = 9;
            healthMax.fontSizeMax = 13;
            HudStyle.Center(healthMax.rectTransform, 0, -16, GaugeWidth, 16);
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

    private static readonly Color32 HealthInk = new(255, 190, 184, 255);

    internal void Apply(Theme theme)
    {
        gauge.SetTint(theme.Health);
        staminaValue.color = theme.Text;
        stamina.Style(theme);
    }

    internal void Refresh(Player player, bool visible, bool inventoryOpen, bool covered)
    {
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        RefreshVignette(player, visible);
        if (visible)
        {
            RefreshHealth(player, inventoryOpen);
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

    private void RefreshHealth(Player player, bool inventoryOpen)
    {
        float max = player.GetMaxHealth();
        float current = Mathf.Clamp(player.GetHealth(), 0, Mathf.Max(max, 0));
        float now = Time.time;
        // Valheim lengthens its own bar with maximum health; this gauge grows the same way, upwards.
        float room = ((RectTransform)root.parent).rect.height * 0.5f;
        float target = Mathf.Clamp(70 + nativeHealth.rect.width * 1.3f, 104, Mathf.Max(104, room));
        if (player != watched)
        {
            watched = player;
            seenHealth = current;
            seenMax = max;
            shownLength = target;
            lengthVelocity = 0;
            trail = max > 0 ? current / max : 0;
            flashAt = hurtAt = growAt = -10;
        }
        else
        {
            if (max > seenMax + 0.5f) growAt = now;
            // A new maximum rescales the bar; the trail only marks health that was lost.
            if (Mathf.Abs(max - seenMax) > 0.5f) trail = Mathf.Min(trail, max > 0 ? current / max : 0);
            // Fading food lowers the maximum every second and drags a full bar down with it;
            // only a drop beyond the lost maximum is damage.
            if (seenHealth - current > Mathf.Max(0, seenMax - max) + 0.05f)
            {
                // The trail keeps the lost part visible for a moment before draining.
                if (now - hurtAt > 0.5f || trail < seenHealth / Mathf.Max(1, seenMax)) trail = seenHealth / Mathf.Max(1, seenMax);
                hurtAt = now;
                trailHold = now + 0.45f;
            }
            else if (current > seenHealth + 0.05f && max <= seenMax + 0.5f) flashAt = now;
            seenHealth = current;
            seenMax = max;
        }
        shownLength = Mathf.SmoothDamp(shownLength, target, ref lengthVelocity, 0.35f, Mathf.Infinity, Time.deltaTime);
        if (Mathf.Abs(shownLength - target) < 0.25f) shownLength = target;
        float length = Mathf.Round(shownLength);
        if (healthRoot.sizeDelta.y != length)
        {
            healthRoot.sizeDelta = new Vector2(GaugeWidth, length);
            healthShade.rectTransform.sizeDelta = new Vector2(GaugeWidth + 44, length + 40);
        }
        float fill = max > 0 ? current / max : 0;
        if (now >= trailHold) trail = Mathf.MoveTowards(trail, fill, Time.deltaTime * 0.9f);
        if (trail < fill) trail = fill;

        float flash = Pulse(now - flashAt, 0.55f);
        float hurt = Pulse(now - hurtAt, 0.35f);
        float sinceHurt = now - hurtAt;
        float pulse = sinceHurt >= 0 && sinceHurt < 0.55f ? sinceHurt / 0.55f : -1;
        float grow = Pulse(now - growAt, 0.9f);
        gauge.Set(fill, trail, flash, pulse, hurt, grow);
        // A short shake sells the hit without moving the numbers out of reach.
        float shake = sinceHurt >= 0 && sinceHurt < 0.3f ? Mathf.Sin(sinceHurt * 70) * 3 * (1 - sinceHurt / 0.3f) : 0;
        gauge.rectTransform.anchoredPosition = new Vector2(shake, 0);
        healthRoot.localScale = Vector3.one * (1 + 0.05f * flash);

        int amount = Mathf.CeilToInt(current), capacity = Mathf.Max(0, Mathf.CeilToInt(max));
        if (amount != lastHealth || capacity != lastMaxHealth)
        {
            lastHealth = amount;
            lastMaxHealth = capacity;
            healthValue.SetText("{0}", amount);
            healthMax.SetText("/{0}", capacity);
        }
        // The value sits in the middle of the bar; the maximum appears with the inventory.
        healthValue.rectTransform.anchoredPosition = new Vector2(0, inventoryOpen ? 7 : 0);
        healthMax.rectTransform.anchoredPosition = new Vector2(0, -12);
        healthMax.enabled = inventoryOpen;
        healthValue.color = Color.Lerp(HealthInk, Color.white, Mathf.Max(flash, hurt));
    }

    // 1 at the event, fading to 0 after the given duration.
    private static float Pulse(float elapsed, float duration) =>
        elapsed < 0 || elapsed >= duration ? 0 : Mathf.Pow(1 - elapsed / duration, 2);

    private void RefreshVignette(Player player, bool visible)
    {
        float max = visible ? player.GetMaxHealth() : 0;
        float fraction = max > 0 ? Mathf.Clamp01(player.GetHealth() / max) : 1;
        float strength = fraction < 0.5f ? Mathf.Lerp(0.18f, 0.85f, Mathf.InverseLerp(0.5f, 0.05f, fraction)) : 0;
        float beat = 0;
        if (fraction < 0.2f && strength > 0)
        {
            // A fast double beat, like a heartbeat under stress.
            float phase = Mathf.Repeat(Time.time * 1.6f, 1);
            beat = Mathf.Max(Pulse(phase, 0.22f), 0.7f * Pulse(phase - 0.24f, 0.22f));
            strength *= 0.7f + 0.3f * beat;
        }
        bool show = strength > 0;
        if (vignette.gameObject.activeSelf != show) vignette.gameObject.SetActive(show);
        if (show) vignette.Set(strength, beat);
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
        ItemUi.Destroy(vignette);
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
