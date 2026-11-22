using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ValheimNewUI;

[BepInPlugin("local.valheimnewui.vitals", "Valheim New UI", "0.0.3")]
public sealed class Plugin : BaseUnityPlugin
{
    private string themePath, lastContent, lastReadError;
    private Theme theme;
    private float nextRead;
    private Hud owner, failedHud;
    private VitalsView view;
    private Artwork artwork;
    private InterfaceSkin skin;
    private Harmony harmony;
    private bool quitting;

    private void Awake()
    {
        themePath = Path.Combine(Paths.ConfigPath, "ValheimNewUI", "theme.json");
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("ValheimNewUI.theme.json");
        using var reader = new StreamReader(stream);
        string defaults = reader.ReadToEnd();
        theme = new Theme(defaults);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(themePath));
            if (!File.Exists(themePath)) File.WriteAllText(themePath, defaults);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Could not create {themePath}: {ex.Message}");
        }
        Logger.LogInfo($"HUD theme file: {themePath}");
        artwork = new Artwork(Path.Combine(Path.GetDirectoryName(themePath), "artwork"), Logger);
        harmony = new Harmony("local.valheimnewui.patches");
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            harmony = null;
            Logger.LogError($"Could not install UI hooks: {ex}");
        }
        ReloadTheme();
    }

    private void ReloadTheme()
    {
        string content;
        try
        {
            content = File.ReadAllText(themePath);
            lastReadError = null;
        }
        catch (Exception ex)
        {
            if (lastReadError != ex.Message) Logger.LogWarning($"Cannot read theme; keeping current settings. {ex.Message}");
            lastReadError = ex.Message;
            return;
        }
        if (content == lastContent) return;
        lastContent = content;
        try
        {
            theme = new Theme(content);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Invalid theme; keeping current settings. {ex.Message}");
            return;
        }
        NativeMessagePatch.SetEnabled(theme.Enabled && harmony != null);
        if (theme.Enabled) view?.Apply(theme);
        else Release();
        failedHud = null;
        Logger.LogInfo($"Theme applied (enabled={theme.Enabled}).");
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextRead)
        {
            nextRead = Time.unscaledTime + 0.75f;
            ReloadTheme();
        }
        if (harmony != null) EquipmentInventory.Refresh(theme.Enabled && !failedHud);
    }

    private void BeforeCanvasRender()
    {
        var hud = Hud.instance;
        if (skin != null && (!hud || owner != hud)) Release();
        if (!theme.Enabled || !hud || !hud.m_rootObject || failedHud == hud) return;
        try
        {
            if (skin == null)
            {
                skin = new InterfaceSkin(hud, artwork);
                owner = hud;
                NativeMessagePatch.SetEnabled(harmony != null);
            }
            skin.PrepareForRender(theme);

            var player = Player.m_localPlayer;
            if (!player || player.IsDead())
            {
                ReleaseVitals();
                return;
            }
            if (view == null)
            {
                if (!hud.m_healthText || !hud.m_staminaText || !hud.m_healthBarRoot || !hud.m_staminaBar2Root) return;
                var canvas = hud.m_healthText.GetComponentInParent<Canvas>(true);
                if (!canvas) return;
                view = new VitalsView(hud, canvas, theme);
                Logger.LogInfo($"Vitals attached. Health {player.GetHealth():0.#}/{player.GetMaxHealth():0.#}, stamina {player.GetStamina():0.#}/{player.GetMaxStamina():0.#}.");
            }
            bool bagOrMap = InventoryGui.IsVisible() || (Minimap.instance && Minimap.instance.m_largeRoot.activeInHierarchy);
            view.Refresh(player, !bagOrMap && hud.isActiveAndEnabled && hud.m_rootObject.activeInHierarchy && hud.IsVisible());
        }
        catch (Exception ex)
        {
            Release();
            NativeMessagePatch.SetEnabled(false);
            failedHud = hud;
            Logger.LogError($"Could not prepare interface; restored original graphics. {ex}");
        }
    }

    private void Release()
    {
        // The scene may already have destroyed the native HUD objects.
        var previousSkin = skin;
        skin = null;
        owner = null;
        try
        {
            previousSkin?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError($"Could not release interface: {ex}");
        }
        finally
        {
            ReleaseVitals();
        }
    }

    private void ReleaseVitals()
    {
        var previousView = view;
        view = null;
        previousView?.Dispose();
    }

    private void OnDestroy()
    {
        NativeMessagePatch.SetEnabled(false);
        harmony?.UnpatchSelf();
        artwork?.Dispose();
    }

    private void OnEnable() => Canvas.preWillRenderCanvases += BeforeCanvasRender;
    private void OnApplicationQuit() => quitting = true;

    private void OnDisable()
    {
        NativeMessagePatch.SetEnabled(false);
        Canvas.preWillRenderCanvases -= BeforeCanvasRender;
        Release();
        if (!quitting && harmony != null) EquipmentInventory.Refresh(false);
    }
}
