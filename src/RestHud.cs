using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimNewUI;

internal sealed class RestHud
{
    private readonly RectTransform root;
    private readonly Image shelter, fire, track, progress;
    private readonly TextMeshProUGUI comfort;
    private int previousLevel = -1;

    internal RestHud(RectTransform parent, Artwork art, TMP_FontAsset font)
    {
        root = ItemUi.Rect(parent, "CampAndComfort");
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0);
        root.pivot = new Vector2(0.5f, 1);
        root.anchoredPosition = new Vector2(0, -4);
        root.sizeDelta = new Vector2(150, 60);
        var shadow = HudStyle.Shadow(root, art, 0.86f);
        HudStyle.Center(shadow.rectTransform, 0, 0, 200, 104);
        shelter = ItemUi.Image(root, "Shelter", art["shelter"]);
        shelter.color = ItemUi.Ink;
        fire = ItemUi.Image(root, "Campfire", art["campfire"]);
        fire.color = ItemUi.Accent;
        comfort = ItemUi.Text(root, "Comfort", font, 16, ItemUi.Ink);
        comfort.alignment = TextAlignmentOptions.Center;
        ItemUi.TopLeft(comfort.rectTransform, 0, 27, 150, 20);
        track = ItemUi.Image(root, "Resting", art["meter-fill"]);
        track.color = new Color32(38, 32, 24, 220);
        ItemUi.TopLeft(track.rectTransform, 36, 48, 78, 4);
        progress = ItemUi.Image(track.transform, "Progress", art["meter-fill"]);
        ItemUi.Stretch(progress.rectTransform, Vector2.zero, Vector2.zero);
        progress.color = ItemUi.Accent;
        progress.type = Image.Type.Filled;
        progress.fillMethod = Image.FillMethod.Horizontal;
    }

    internal void Refresh(Player player)
    {
        var statuses = player.GetSEMan();
        bool sheltered = statuses.GetStatusEffect(SEMan.s_statusEffectShelter);
        bool byFire = statuses.GetStatusEffect(SEMan.s_statusEffectCampFire);
        var resting = statuses.GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
        root.gameObject.SetActive(sheltered || byFire || resting);
        if (!root.gameObject.activeSelf) return;
        shelter.enabled = sheltered;
        fire.enabled = byFire;
        ItemUi.TopLeft(shelter.rectTransform, byFire ? 49 : 64, 4, 22, 22);
        ItemUi.TopLeft(fire.rectTransform, sheltered ? 79 : 64, 4, 22, 22);
        int level = player.GetComfortLevel();
        if (level != previousLevel)
        {
            previousLevel = level;
            comfort.text = Localization.instance.Localize("$se_rested_comfort") + " " + level;
        }
        comfort.enabled = level > 0;
        float elapsed = resting ? resting.GetDuration() : 0;
        bool charging = resting && resting.m_delay > 0 && elapsed <= resting.m_delay;
        track.gameObject.SetActive(charging);
        if (charging) progress.fillAmount = Mathf.Clamp01(elapsed / resting.m_delay);
    }

    internal static bool HasDedicatedSlot(int hash) => hash == SEMan.s_statusEffectRested
        || hash == SEMan.s_statusEffectShelter || hash == SEMan.s_statusEffectCampFire || hash == SEMan.s_statusEffectResting;

    internal static bool IsStatusMessage(string message)
    {
        if (message.StartsWith("$se_resting_start", StringComparison.Ordinal) ||
            message.StartsWith("$se_rested_start", StringComparison.Ordinal) ||
            message.StartsWith("$se_rested_end", StringComparison.Ordinal)) return true;
        var db = ObjectDB.instance;
        return db && (Matches(db.GetStatusEffect(SEMan.s_statusEffectShelter), message)
            || Matches(db.GetStatusEffect(SEMan.s_statusEffectCampFire), message)
            || Matches(db.GetStatusEffect(SEMan.s_statusEffectResting), message)
            || Matches(db.GetStatusEffect(SEMan.s_statusEffectRested), message));
    }

    private static bool Matches(StatusEffect effect, string message) =>
        effect && (message == effect.m_startMessage || message == effect.m_stopMessage || message == effect.m_repeatMessage);
}
