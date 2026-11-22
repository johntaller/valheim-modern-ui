using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ValheimNewUI.LocalTest;

[BepInPlugin("local.valheimnewui.multiplayerqa", "Valheim New UI Local Multiplayer", "0.1.0")]
public sealed class LocalTest : BaseUnityPlugin
{
    private const int Port = 24679;
    private const string TestWorld = "DEVBLACKT";
    private const string TestProfile = "devblackt2";
    private static bool host;
    private static bool client;
    private bool joinStarted;
    private Harmony harmony;
    private FejdStartup lastStartup;
    private float readyAt;
    private bool selectRequested;

    private void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        host = Array.IndexOf(args, "-newui-local-host") >= 0;
        client = Array.IndexOf(args, "-newui-local-client") >= 0;
        if (host == client)
        {
            if (host) Logger.LogWarning("Choose either -newui-local-host or -newui-local-client.");
            host = client = false;
            return;
        }
        harmony = new Harmony("local.valheimnewui.multiplayerqa");
        if (host)
            harmony.Patch(AccessTools.Method(typeof(ZNet), "Start"), postfix: new HarmonyMethod(typeof(LocalTest), nameof(OpenLoopback)));
        if (client)
            harmony.Patch(AccessTools.Method(typeof(ZNet), "ClientConnect"), prefix: new HarmonyMethod(typeof(LocalTest), nameof(ConnectLoopback)));
    }

    private void Update()
    {
        if (!client || joinStarted || !FejdStartup.instance) return;
        var startup = FejdStartup.instance;
        if (startup != lastStartup)
        {
            lastStartup = startup;
            readyAt = Time.unscaledTime + 3;
        }
        if (Time.unscaledTime < readyAt || !startup.m_characterSelectScreen.activeInHierarchy) return;
        var profile = new PlayerProfile(TestProfile, FileHelpers.FileSource.Local);
        string path = Path.Combine(SaveSystem.GetCharacterFolderPath(FileHelpers.FileSource.Local), TestProfile + ".fch");
        if (!File.Exists(path))
        {
            if (PlayerProfile.HaveProfile(TestProfile)) return;
            var preview = startup.GetPreviewPlayer();
            if (!startup.m_newCharacterPanel.activeInHierarchy || !preview) return;
            preview.GiveDefaultItems();
            profile.SetName("DEVBLACKT2");
            profile.SavePlayerData(preview);
            if (!profile.Save()) return;
            startup.OnNewCharacterCancel();
        }
        else if (!profile.Load()) return;
        if (!string.Equals(profile.GetName(), TestProfile, StringComparison.OrdinalIgnoreCase)
            || !startup.m_selectCharacterPanel.activeInHierarchy) return;

        if (!selectRequested)
        {
            selectRequested = true;
            AccessTools.Field(typeof(FejdStartup), "m_profiles").SetValue(startup, null);
            AccessTools.Method(typeof(FejdStartup), "SetSelectedProfile").Invoke(startup, new object[] { TestProfile });
            readyAt = Time.unscaledTime + 2;
            return;
        }

        joinStarted = true;
        Game.SetProfile(TestProfile, FileHelpers.FileSource.Local);
        ZNet.SetServer(false, false, false, "", "", null);
        ZNet.SetServerHost(IPAddress.Loopback.ToString(), Port, OnlineBackendType.CustomSocket);
        AccessTools.Method(typeof(FejdStartup), "LoadMainScene").Invoke(startup, null);
        Logger.LogInfo($"Joining loopback as {profile.GetName()}");
    }

    private static bool ConnectLoopback(ZNet __instance)
    {
        if (!client || ZNet.m_onlineBackend != OnlineBackendType.CustomSocket ||
            !string.Equals(Game.instance?.GetPlayerProfile()?.GetName(), TestProfile, StringComparison.OrdinalIgnoreCase))
            return true;

        var tcp = ZSocket2.CreateSocket();
        try
        {
            tcp.Connect(IPAddress.Loopback, Port);
            // Current retail ZNet never advances its legacy asynchronous connector.
            var socket = new ZSocket2(tcp, IPAddress.Loopback.ToString());
            AccessTools.Method(typeof(ZNet), "Connect", new[] { typeof(ISocket) }).Invoke(__instance, new object[] { socket });
        }
        catch (SocketException exception)
        {
            tcp.Dispose();
            AccessTools.Field(typeof(ZNet), "m_connectionStatus").SetValue(null, ZNet.ConnectionStatus.ErrorConnectFailed);
            Debug.LogWarning($"Valheim New UI could not connect to loopback: {exception.Message}");
        }
        return false;
    }

    private static void OpenLoopback(ref ISocket ___m_hostSocket)
    {
        if (!host || ___m_hostSocket != null || ZNet.GetWorldIfIsHost()?.m_name != TestWorld) return;
        var profile = Game.instance?.GetPlayerProfile();
        if (profile == null || !string.Equals(profile.GetName(), TestWorld, StringComparison.OrdinalIgnoreCase)) return;

        var listener = new TcpListener(IPAddress.Loopback, Port);
        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            listener.Stop();
            Debug.LogWarning($"Valheim New UI could not listen on loopback: {exception.Message}");
            return;
        }
        var socket = new ZSocket2();
        AccessTools.Field(typeof(ZSocket2), "m_listner").SetValue(socket, listener);
        AccessTools.Field(typeof(ZSocket2), "m_listenPort").SetValue(socket, Port);
        ZNet.m_onlineBackend = OnlineBackendType.CustomSocket;
        ___m_hostSocket = socket;
        Debug.Log($"Valheim New UI loopback listening at {listener.LocalEndpoint}");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();
}
