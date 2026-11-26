using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ValheimNewUI;

// Once per launch the mod asks GitHub for its newest release. When one is newer than the installed
// build, the main menu shows a notice with the way to download it, to be reminded later, or to skip
// that version for good. The skipped version lives in the BepInEx config, so updating the mod keeps it.
internal sealed class UpdateNotice : IDisposable
{
    private const string Releases = "https://api.github.com/repos/johntaller/valheim-modern-ui/releases";
    private const int CheckTimeout = 15;
    private static UpdateNotice visibleNotice;
    private static int closedFrame = -1;

    private readonly Artwork art;
    private readonly ManualLogSource log;
    private readonly ConfigEntry<bool> enabled;
    private readonly ConfigEntry<string> skipped;
    private UpdateRelease release;
    private UnityWebRequest request;
    private bool dismissed;
    private RectTransform root;
    private GameObject attachedMenu, previousSelection;
    private Button laterButton;

    internal static string Installed { get; } = ReadInstalled();

    internal UpdateNotice(ConfigFile config, Artwork art, ManualLogSource log)
    {
        this.art = art;
        this.log = log;
        enabled = config.Bind("Updates", "CheckForUpdates", true,
            "Look for a newer release of the mod on GitHub when the game starts.");
        skipped = config.Bind("Updates", "SkippedVersion", "",
            "A release the update notice stays quiet about. Cleared by setting it to an empty value.");
    }

    internal IEnumerator Check()
    {
        if (!enabled.Value) yield break;
        var releases = new List<JToken>();
        float deadline = Time.realtimeSinceStartup + CheckTimeout;
        for (int page = 1; ; page++)
        {
            float remaining = deadline - Time.realtimeSinceStartup;
            if (remaining <= 0)
            {
                log.LogInfo("Update check skipped: release lookup timed out.");
                yield break;
            }
            var operation = BeginPage(page, Mathf.CeilToInt(remaining));
            if (operation == null) yield break;
            yield return operation;
            try
            {
                if (request.result != UnityWebRequest.Result.Success)
                {
                    log.LogInfo($"Update check skipped: {request.error}");
                    yield break;
                }
                releases.AddRange(JArray.Parse(request.downloadHandler.text));
                if (!UpdateRelease.HasNextPage(request.GetResponseHeader("Link"))) break;
            }
            catch (Exception ex)
            {
                log.LogInfo($"Update check skipped: {ex.Message}");
                yield break;
            }
            finally
            {
                ReleaseRequest();
            }
        }
        release = UpdateRelease.Find(releases, Installed, skipped.Value);
        if (release != null) log.LogInfo($"Update available: {release.Version} (installed {Installed}).");
        else log.LogInfo($"No update to show (installed {Installed}).");
    }

    private UnityWebRequestAsyncOperation BeginPage(int page, int timeout)
    {
        try
        {
            request = UnityWebRequest.Get($"{Releases}?per_page=100&page={page}");
            request.timeout = timeout;
            request.SetRequestHeader("User-Agent", "ValheimNewUI-UpdateCheck");
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            return request.SendWebRequest();
        }
        catch (Exception ex)
        {
            ReleaseRequest();
            log.LogInfo($"Update check skipped: {ex.Message}");
            return null;
        }
    }

    private void ReleaseRequest()
    {
        request?.Dispose();
        request = null;
    }

    // Called before rendering: the request may finish before the main menu exists.
    internal void Refresh()
    {
        if (release == null || dismissed) return;
        var startup = FejdStartup.instance;
        var menu = startup ? startup.m_mainMenu : null;
        if (!enabled.Value || !menu || !menu.activeInHierarchy ||
            (startup.m_eulaWindow && startup.m_eulaWindow.IsWindowActive()))
        {
            Hide();
            return;
        }
        if (root && attachedMenu != menu) Dispose();
        if (!root)
        {
            var parentCanvas = menu.GetComponentInParent<Canvas>();
            if (!parentCanvas) return;
            try
            {
                Build(menu, parentCanvas.rootCanvas);
            }
            catch (Exception ex)
            {
                dismissed = true;
                Dispose();
                log.LogWarning($"Could not show update notice: {ex.Message}");
                return;
            }
        }
        var events = EventSystem.current;
        if (!root.gameObject.activeSelf)
        {
            previousSelection = events ? events.currentSelectedGameObject : null;
            root.gameObject.SetActive(true);
            visibleNotice = this;
        }
        var selected = events ? events.currentSelectedGameObject : null;
        if (events && (!selected || !selected.transform.IsChildOf(root))) laterButton.Select();
        if (root.GetSiblingIndex() != root.parent.childCount - 1) root.SetAsLastSibling();
    }

    private void Build(GameObject menu, Canvas canvas)
    {
        var font = menu.GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;

        // A dim layer over the menu keeps the notice modal.
        var shade = ItemUi.Image(canvas.transform, "NewUI_UpdateNotice");
        shade.color = new Color(0, 0, 0, 0.55f);
        shade.raycastTarget = true;
        root = shade.rectTransform;
        attachedMenu = menu;
        root.gameObject.SetActive(false);
        ItemUi.Stretch(root, Vector2.zero, Vector2.zero);

        var panel = ItemUi.Image(root, "Panel", art["panel"]);
        panel.raycastTarget = true;
        HudStyle.Center(panel.rectTransform, 0, 0, 560, 236);

        var title = MenuStyle.Label(panel.transform, "Title", HudStyle.BrandFont(font),
            MenuStyle.Translate("Update available", "Доступно обновление"), 26, ItemUi.Accent);
        title.fontStyle = FontStyles.Normal;
        CraftingMenu.Put(title.rectTransform, 26, 18, 26, 36);
        var rule = ItemUi.Image(panel.transform, "Rule", art["meter-fill"]);
        rule.color = MenuStyle.Edge;
        CraftingMenu.Put(rule.rectTransform, 24, 60, 24, 2);

        var text = MenuStyle.Label(panel.transform, "Text", font, MenuStyle.Translate(
                $"Valheim New UI <color=#E8BC77>{release.Version}</color> is out.\nYou have {Installed}.",
                $"Вышла Valheim New UI <color=#E8BC77>{release.Version}</color>.\nУ вас установлена {Installed}."),
            18, ItemUi.Ink, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        CraftingMenu.Put(text.rectTransform, 26, 76, 26, 64);

        var buttons = ItemUi.Rect(panel.transform, "Buttons");
        CraftingMenu.Put(buttons, 24, 166, 24, 44);
        var download = MenuStyle.Button(buttons, art, font, MenuStyle.Translate("Download", "Скачать"), Download, out _, 17);
        laterButton = MenuStyle.Button(buttons, art, font, MenuStyle.Translate("Later", "Позже"), Close, out _, 17);
        var skip = MenuStyle.Button(buttons, art, font, MenuStyle.Translate("Skip version", "Пропустить версию"), Skip, out _, 17);
        skip.name = "SkipVersion";
        laterButton.name = "Later";
        download.name = "Download";
        Place(skip, 0, 186);
        Place(laterButton, 196, 110);
        Place(download, 318, 194);
        Link(skip, download, laterButton);
        Link(laterButton, skip, download);
        Link(download, laterButton, skip);
        download.image.sprite = art["selected"];
    }

    private static void Link(Button button, Button left, Button right) => button.navigation = new Navigation
    {
        mode = Navigation.Mode.Explicit, selectOnLeft = left, selectOnRight = right
    };

    private static void Place(Button button, float x, float width)
    {
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
        rect.sizeDelta = new Vector2(width, 40);
    }

    private void Download()
    {
        Application.OpenURL(release.Page);
        Close();
    }

    private void Skip()
    {
        try
        {
            skipped.Value = release.Version;
            log.LogInfo($"Skipping version {release.Version}.");
        }
        catch (Exception ex)
        {
            log.LogWarning($"Could not save skipped version: {ex.Message}");
        }
        Close();
    }

    private void Close()
    {
        dismissed = true;
        Hide();
    }

    private void Hide()
    {
        if (visibleNotice == this)
        {
            visibleNotice = null;
            closedFrame = Time.frameCount;
        }
        if (!root || !root.gameObject.activeSelf) return;
        var events = EventSystem.current;
        var selected = events ? events.currentSelectedGameObject : null;
        bool restore = events && selected && selected.transform.IsChildOf(root);
        root.gameObject.SetActive(false);
        if (restore) events.SetSelectedGameObject(previousSelection && previousSelection.activeInHierarchy ? previousSelection : null);
        previousSelection = null;
    }

    public void Dispose()
    {
        if (request != null)
        {
            request.Abort();
            ReleaseRequest();
        }
        Hide();
        if (root) ItemUi.Destroy(root);
        root = null;
        laterButton = null;
        attachedMenu = null;
    }

    private static string ReadInstalled()
    {
        var attribute = typeof(UpdateNotice).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return UpdateRelease.Normalize(attribute?.InformationalVersion) ?? typeof(UpdateNotice).Assembly.GetName().Version.ToString(3);
    }

    // FejdStartup polls keys independently of the EventSystem. Let the modal's
    // own navigation handle input, including the frame on which it closes.
    [HarmonyPatch]
    private static class StartupInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(FejdStartup), "UpdateKeyboard");
            yield return AccessTools.Method(typeof(FejdStartup), "UpdateGamepad");
        }

        private static bool Prefix() => visibleNotice == null && closedFrame != Time.frameCount;
    }
}
