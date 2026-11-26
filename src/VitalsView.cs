using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class VitalsView
{
    // Players found the vitals small; the whole block is drawn a fifth larger.
    private const float Scale = 1.2f;
    private const float Height = 132;
    internal const float Clearance = 18 + Height * Scale + 16;
    // Three food diamonds stacked beside the gauge, then a column of two larger diamonds for the
    // rested bonus and the guardian power, both columns as tall as the block and resting on the
    // same line as the tip of the gauge. Status effects follow on the right.
    private const float FoodX = 72, FoodSize = 42, FoodStep = 44, SideX = 120, SideSize = 64, SideStep = 66;
    private const float SideCardWidth = 196, EffectsGap = 12;
    // The gauge's lowest pixel (its bronze tip) sits this far above its own rectangle.
    private const float GaugeFoot = 1;
    private readonly RectTransform root, aim, nativeHealth;
    private const float GaugeWidth = 48;
    // At its shortest the gauge stands as tall as the column of slots beside it.
    private const float GaugeMinimum = 136;
    private readonly RectTransform healthRoot;
    private readonly HealthGauge gauge;
    private readonly LowHealthVignette vignette;
    private readonly Image healthShade;
    private readonly TextMeshProUGUI healthValue, healthMax, staminaValue;
    private readonly Image reticle;
    private readonly StaminaArcs stamina;
    private readonly CanvasGroup staminaGroup;
    private readonly AimBars bars;
    private readonly FoodSlot[] foods = new FoodSlot[3];
    private readonly DiamondSlot rested, guardian;
    private readonly Button guardianTouch;
    private readonly StatusEffectHud effects;
    private readonly VitalsTips tips;
    private readonly List<HiddenGraphics> hidden = new();
    private readonly HashSet<Graphic> hiddenText = new();
    private Sprite inactiveRested;
    private readonly HudSound alarmSound;
    private float alarmThreshold = 0.25f, alarmVolume = 1, alarmAt = -10, sideX = SideX, effectsX = SideX + SideSize + EffectsGap;
    private float staminaAlpha, powerReadyAt = -10;
    private bool powerWasReady = true;
    private int lastHealth = -1, lastMaxHealth = -1, lastStamina = -1;
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
        root.localScale = Vector3.one * Scale;
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
            ItemUi.TopLeft(shade.rectTransform, -24, -47, 268, 221);
            vignette = ItemUi.Rect(canvasRect, "NewUI_LowHealth").gameObject.AddComponent<LowHealthVignette>();
            vignette.raycastTarget = false;
            ItemUi.Stretch(vignette.rectTransform, Vector2.zero, Vector2.zero);
            vignette.rectTransform.SetAsFirstSibling();
            healthShade = HudStyle.Shadow(root, art, 0.70f);
            var healthShadowRect = healthShade.rectTransform;
            healthShadowRect.anchorMin = healthShadowRect.anchorMax = healthShadowRect.pivot = Vector2.zero;
            healthShadowRect.anchoredPosition = new Vector2(-22, -18 - (14 - BlockBottom + GaugeFoot));
            healthRoot = ItemUi.Rect(root, "Health");
            healthRoot.anchorMin = healthRoot.anchorMax = healthRoot.pivot = Vector2.zero;
            healthRoot.anchoredPosition = new Vector2(12, BlockBottom - GaugeFoot);
            healthRoot.sizeDelta = new Vector2(GaugeWidth, GaugeMinimum);
            gauge = ItemUi.Rect(healthRoot, "Gauge").gameObject.AddComponent<HealthGauge>();
            gauge.raycastTarget = false;
            ItemUi.Stretch(gauge.rectTransform, Vector2.zero, Vector2.zero);
            healthValue = Number(healthRoot, "Value", font, 25, HealthInk);
            healthValue.enableAutoSizing = true;
            healthValue.fontSizeMin = 14;
            healthValue.fontSizeMax = 25;
            HudStyle.Center(healthValue.rectTransform, 0, 2, GaugeWidth - 8, 28);
            healthMax = Number(healthRoot, "Maximum", font, 16, HealthInk);
            healthMax.enableAutoSizing = true;
            healthMax.fontSizeMin = 10;
            healthMax.fontSizeMax = 16;
            HudStyle.Center(healthMax.rectTransform, 0, -19, GaugeWidth - 4, 18);
            // Later slots are drawn first so an unfolded card never covers the slot above it.
            for (int i = foods.Length - 1; i >= 0; i--)
                foods[i] = new FoodSlot(root, art, font, hud.m_healthText.font, FoodX, FoodStep * i, FoodSize);

            rested = new DiamondSlot(root, art, font, hud.m_healthText.font, "Rested", SideX, 0, SideSize, SideCardWidth);
            guardian = new DiamondSlot(root, art, font, hud.m_healthText.font, "GuardianPower", SideX, SideStep, SideSize, SideCardWidth);
            try { alarmSound = new HudSound("low-health"); }
            catch (System.Exception ex) { Debug.LogWarning("[Valheim New UI] Low health sound: " + ex.Message); }
            guardianTouch = guardian.Icon.gameObject.AddComponent<Button>();
            guardianTouch.transition = Selectable.Transition.None;
            guardianTouch.navigation = new Navigation { mode = Navigation.Mode.None };
            guardianTouch.onClick.AddListener(() =>
            {
                if (ZInput.HasDoubleTapped() && Player.m_localPlayer) Player.m_localPlayer.StartGuardianPower();
            });
            effects = new StatusEffectHud(hud, root, art, effectsX);
            tips = new VitalsTips(canvas.rootCanvas, art, hud.m_healthText.font);
            var staminaRoot = ItemUi.Rect(aim, "Stamina");
            ItemUi.Stretch(staminaRoot, Vector2.zero, Vector2.zero);
            staminaGroup = staminaRoot.gameObject.AddComponent<CanvasGroup>();
            staminaGroup.blocksRaycasts = staminaGroup.interactable = false;
            staminaGroup.alpha = 0;
            stamina = staminaRoot.gameObject.AddComponent<StaminaArcs>();
            stamina.raycastTarget = false;
            // The number rides on the end of the bar.
            staminaCounterShade = HudStyle.Shadow(staminaRoot, art, 0.85f);
            staminaCounterShade.rectTransform.anchorMin = staminaCounterShade.rectTransform.anchorMax = Vector2.one * 0.5f;
            staminaCounterShade.rectTransform.pivot = new Vector2(0.2f, 0.5f);
            staminaCounterShade.rectTransform.sizeDelta = new Vector2(70, 36);
            staminaValue = Number(staminaRoot, "Value", font, 16, theme.Text);
            staminaValue.alignment = TextAlignmentOptions.MidlineLeft;
            staminaValue.rectTransform.anchorMin = staminaValue.rectTransform.anchorMax = Vector2.one * 0.5f;
            staminaValue.rectTransform.pivot = new Vector2(0, 0.5f);
            staminaValue.rectTransform.sizeDelta = new Vector2(48, 20);
            bars = new AimBars(hud, aim, font, art);

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
    // The bottom of the block: the tips of the gauge and of every diamond rest on this line.
    private const float BlockBottom = Height - FoodStep * 2 - FoodSize;

    private static readonly Color32 HealthInk = new(255, 190, 184, 255);

    internal void Apply(Theme theme)
    {
        gauge.SetTint(theme.Health);
        alarmThreshold = theme.LowHealthAlarm;
        alarmVolume = theme.LowHealthVolume;
        staminaInk = theme.Text;
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
            for (int i = 0; i < foods.Length; i++) foods[i].Refresh(i < food.Count ? food[i] : null, inventoryOpen && !covered);
            bool expanded = inventoryOpen && !covered;
            // Rested and the guardian power step aside for the unfolded food cards, and the
            // effects for their own cards.
            float side = expanded && food.Count > 0 ? FoodX + FoodSize * 0.5f + FoodSlot.CardWidth + 10 : SideX;
            float step = Time.unscaledDeltaTime * 900;
            sideX = Mathf.MoveTowards(sideX, side, step);
            float effectsTarget = side + (expanded ? SideSize * 0.5f + SideCardWidth : SideSize) + EffectsGap;
            effectsX = Mathf.MoveTowards(effectsX, effectsTarget, step);
            rested.SetX(sideX);
            guardian.SetX(sideX);
            effects.SetX(effectsX);
            RefreshRested(player, expanded);
            RefreshPower(player, expanded, inventoryOpen || covered);
            effects.Refresh(player, expanded);
            RefreshTips(player, expanded, food);
        }
        else tips.Clear();

        float maximum = visible ? player.GetMaxStamina() : 0;
        float amount = visible ? player.GetStamina() : 0;
        bool aiming = visible && !covered && (inventoryOpen || reticle && reticle.isActiveAndEnabled);
        if (aim.gameObject.activeSelf != aiming) aim.gameObject.SetActive(aiming);
        if (!aiming) return;
        if (reticle) aim.position = reticle.rectTransform.TransformPoint(reticle.rectTransform.rect.center);
        bars.Refresh(player, true, inventoryOpen);

        // Stamina shows while it is being spent, and always with the inventory open.
        staminaAlpha = Mathf.MoveTowards(staminaAlpha, inventoryOpen || amount < maximum ? 1 : 0, Time.unscaledDeltaTime * 5);
        staminaGroup.alpha = staminaAlpha;
        if (staminaAlpha <= 0) return;
        stamina.Value(amount, maximum);
        // The number appears once stamina runs low, at the end of the remaining bar.
        bool low = maximum > 0 && amount < maximum * 0.3f;
        bool number = inventoryOpen || low;
        staminaValue.enabled = staminaCounterShade.enabled = number;
        if (!number) return;
        var end = stamina.FillEnd(7);
        staminaValue.rectTransform.anchoredPosition = end;
        staminaCounterShade.rectTransform.anchoredPosition = end;
        staminaValue.color = low ? Color.Lerp(staminaInk, ItemUi.Warning, 0.7f) : staminaInk;
        int value = Mathf.Max(0, Mathf.CeilToInt(amount));
        if (value != lastStamina)
        {
            lastStamina = value;
            staminaValue.SetText("{0}", value);
        }
    }

    private Color staminaInk = Color.white;

    private void RefreshRested(Player player, bool expanded)
    {
        var rest = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested);
        if (!inactiveRested && ObjectDB.instance)
            inactiveRested = ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested)?.m_icon;
        float left = rest && rest.m_ttl > 0 ? Mathf.Clamp01(rest.GetRemaningTime() / rest.m_ttl) : 0;
        rested.Show(rest ? rest.m_icon : inactiveRested, rest ? Color.white : (Color)new Color32(103, 93, 82, 180), rest, left, 0, 0, 0);
        if (!expanded)
        {
            rested.Card(false, "", "", "");
            return;
        }
        string title = rest ? Localization.instance.Localize(rest.m_name)
            : MenuStyle.Translate("Not rested", "Нет отдыха");
        string comfort = Localization.instance.Localize("$se_rested_comfort") + " " + player.GetComfortLevel();
        rested.Card(true, title, comfort, rest ? rest.GetIconText() : "");
    }

    // The band refills while the power recharges; a short glow says it is ready again.
    private void RefreshPower(Player player, bool expanded, bool blocked)
    {
        player.GetGuardianPowerHUD(out var power, out float cooldown);
        bool ready = power && cooldown <= 0;
        float now = Time.time;
        if (ready && !powerWasReady) powerReadyAt = now;
        powerWasReady = ready || !power;
        float since = now - powerReadyAt;
        float glow = since >= 0 && since < 2.4f ? (0.5f + 0.5f * Mathf.Sin(since * Mathf.PI * 2 * 1.2f)) * (1 - since / 2.4f) : 0;
        float band = !power ? 0 : ready ? 1 : power.m_cooldown > 0 ? Mathf.Clamp01(1 - cooldown / power.m_cooldown) : 0;
        var ink = !power ? (Color)DiamondSlot.EmptyInk : ready ? Color.white : new Color(1, 1, 1, 0.45f);
        guardian.Show(power ? power.m_icon : null, ink, power, band, glow, 0.8f * glow, 0.3f * glow);
        bool touch = power && !blocked && ZInput.IsTouchActive();
        guardian.Icon.raycastTarget = guardianTouch.enabled = touch;
        if (!expanded || !power)
        {
            guardian.Card(false, "", "", "");
            return;
        }
        string state = ready ? Localization.instance.Localize("$hud_ready") : MenuStyle.Translate("Recharging", "Перезарядка");
        guardian.Card(true, Localization.instance.Localize(power.m_name), state,
            ready ? "" : StatusEffect.GetTimeString(Mathf.Ceil(cooldown), false, true));
    }

    private void RefreshHealth(Player player, bool inventoryOpen)
    {
        float max = player.GetMaxHealth();
        float current = Mathf.Clamp(player.GetHealth(), 0, Mathf.Max(max, 0));
        float now = Time.time;
        // Valheim lengthens its own bar with maximum health; this gauge grows the same way, upwards.
        float room = ((RectTransform)root.parent).rect.height * 0.5f / Scale;
        float target = Mathf.Clamp(GaugeMinimum - 34 + nativeHealth.rect.width * 1.3f, GaugeMinimum, Mathf.Max(GaugeMinimum, room));
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
        RefreshAlarm(player, visible, fraction);
    }

    // Low health makes the bar flicker red; each flicker cycle starts with the warning sound.
    private void RefreshAlarm(Player player, bool visible, float fraction)
    {
        bool low = visible && alarmThreshold > 0 && fraction < alarmThreshold && !player.IsDead();
        float now = Time.time;
        float cycle = Mathf.Max(1.6f, (alarmSound?.Length ?? 0) + 0.5f);
        float alarm = 0;
        if (low)
        {
            if (now - alarmAt >= cycle || alarmAt < 0)
            {
                alarmAt = now;
                alarmSound?.Play(alarmVolume);
            }
            float t = now - alarmAt;
            alarm = Mathf.Max(Pulse(t, 0.45f), 0.65f * Pulse(t - 0.5f, 0.45f), 0.25f);
        }
        else if (alarmAt >= 0)
        {
            alarmAt = -10;
            alarmSound?.Stop();
        }
        gauge.SetAlarm(alarm);
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

    // With the inventory open, whatever the pointer rests on in the block explains itself.
    private void RefreshTips(Player player, bool expanded, List<Player.Food> food)
    {
        if (!expanded || !VitalsTips.Pointer(out var screen))
        {
            tips.Clear();
            return;
        }
        var camera = tips.Camera;
        var effect = effects.At(screen, camera);
        if (effect)
        {
            tips.Effect(effect, screen);
            return;
        }
        if (rested.Contains(screen, camera))
        {
            tips.Rested(player, screen);
            return;
        }
        if (guardian.Contains(screen, camera))
        {
            tips.Power(player, screen);
            return;
        }
        for (int i = 0; i < foods.Length; i++)
        {
            if (i >= food.Count || !foods[i].Contains(screen, camera)) continue;
            tips.Food(food[i], screen);
            return;
        }
        if (RectTransformUtility.RectangleContainsScreenPoint(healthRoot, screen, camera))
        {
            tips.Health(player, screen);
            return;
        }
        tips.Clear();
    }

    internal void Dispose()
    {
        tips?.Dispose();
        effects?.Dispose();
        bars?.Dispose();
        foreach (var state in hidden) state.Restore();
        foreach (var text in hiddenText)
            if (text) text.enabled = true;
        ItemUi.Destroy(aim);
        ItemUi.Destroy(root);
        ItemUi.Destroy(vignette);
        alarmSound?.Dispose();
    }

    private static TextMeshProUGUI Number(Transform parent, string name, TMP_FontAsset font, float size, Color color)
    {
        var text = ItemUi.Text(parent, name, font, size, color);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }
}
