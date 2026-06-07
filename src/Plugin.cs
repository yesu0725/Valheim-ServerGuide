using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Commands;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.Net;
using ValheimServerGuide.Triggers;

namespace ValheimServerGuide
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheimserverguide";
        public const string PluginName = "ValheimServerGuide";
        public const string PluginVersion = "0.1.0";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static GuidanceConfig CurrentConfig { get; internal set; } = GuidanceConfig.Empty;

        // BepInEx config — independent of the vanilla "tutorials enabled" toggle.
        public static ConfigEntry<bool> RavenEnabled { get; private set; }
        public static ConfigEntry<string> IntroMusicName { get; private set; }
        public static ConfigEntry<float> IntroMusicDuration { get; private set; }
        public static ConfigEntry<float> IntroFadeInDuration { get; private set; }
        public static ConfigEntry<float> IntroPreDelay { get; private set; }
        public static ConfigEntry<string> ChatColor { get; private set; }

        // Codex (Phase 05)
        public static ConfigEntry<bool> CodexEnabled { get; private set; }
        public static ConfigEntry<string> CodexKey { get; private set; }

        // HUD Tracker (Phase 04 / 04b)
        public static ConfigEntry<bool> TrackerEnabled { get; private set; }
        public static ConfigEntry<string> TrackerPosition { get; private set; }
        public static ConfigEntry<int> TrackerMaxVisible { get; private set; }
        public static ConfigEntry<string> TrackerHotkey { get; private set; }
        public static ConfigEntry<bool> TrackerBadgeEnabled { get; private set; }

        // Discord — server-side only. URL is a secret; do not surface to clients.
        public static ConfigEntry<string> DiscordWebhookUrl { get; private set; }
        public static ConfigEntry<string> DiscordDefaultTemplate { get; private set; }
        public static ConfigEntry<string> DiscordBotUsername { get; private set; }
        public static ConfigEntry<bool> DiscordGuideEnabled { get; private set; }
        public static ConfigEntry<string> DiscordGuideFormat { get; private set; }

        private Harmony _harmony;
        private static GuidanceConfigLoader _loader;
        private static readonly object _loaderLock = new object();
        private static string _configDir;
        private static string _yamlPath;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            RavenEnabled = Config.Bind(
                "Display", "RavenEnabled", true,
                "Enable raven (Hugin) popup mode. Independent of Valheim's 'Tutorials' setting — " +
                "this mod's raven popups will fire even when vanilla raven hints are turned off in game options.");
            IntroMusicName = Config.Bind(
                "Display", "IntroMusicName", "intro",
                "Music track to play while a guidance is shown in 'intro' display mode. " +
                "'intro' is the vanilla Valkyrie-intro track.");
            IntroMusicDuration = Config.Bind(
                "Display", "IntroMusicDuration", 60f,
                "Seconds the intro music stays pinned once it starts. The music plays for at " +
                "least this long even if the player dismisses the on-screen text early. After " +
                "the duration elapses, vanilla MusicMan resumes normal environment-based selection.");
            IntroFadeInDuration = Config.Bind(
                "Display", "IntroFadeInDuration", 3.0f,
                "Seconds to fade the screen to black before the intro text + music start. " +
                "Uses vanilla Hud.m_loadingScreen so no custom assets are needed. Set 0 to disable.");
            IntroPreDelay = Config.Bind(
                "Display", "IntroPreDelay", 1.0f,
                "Seconds to hold on a black screen after the fade-in, before the intro text " +
                "appears. Adds dramatic weight to the transition.");
            ChatColor = Config.Bind(
                "Display", "ChatColor", "#E0C078",
                "Hex color (with or without leading '#') applied to chat-mode guidance messages, " +
                "so they read distinct from regular say (white) and shout (yellow). Set to empty " +
                "string to disable coloring.");

            TrackerEnabled = Config.Bind(
                "HudTracker", "TrackerEnabled", true,
                "Show the objective tracker widget on the HUD. Set false to hide it entirely " +
                "(the widget GameObject remains in the scene — just inactive).");
            TrackerPosition = Config.Bind(
                "HudTracker", "TrackerPosition", "TopRight",
                "Corner the tracker widget anchors to: TopRight | TopLeft | BottomRight | BottomLeft. " +
                "Takes effect on next session start (Hud.Awake).");
            TrackerMaxVisible = Config.Bind(
                "HudTracker", "TrackerMaxVisible", 3,
                "Maximum number of active guide chains shown simultaneously. " +
                "Chains beyond this limit are collapsed into a '+N more' label.");
            TrackerHotkey = Config.Bind(
                "HudTracker", "TrackerHotkey", "F10",
                "KeyCode name for the tracker toggle hotkey (e.g. F9, F10, H). " +
                "See UnityEngine.KeyCode enum for valid values. YAML tracker.hotkey wins when set.");
            TrackerBadgeEnabled = Config.Bind(
                "HudTracker", "TrackerBadgeEnabled", true,
                "Show the persistent corner hint badge (e.g. '[F9] Quests (2)') even when the " +
                "main tracker panel is hidden. YAML tracker.badge_enabled wins when set.");

            CodexEnabled = Config.Bind(
                "Codex", "CodexEnabled", true,
                "Enable the in-game Guide Codex panel. Set false to disable the keybind and " +
                "skip instantiating the panel entirely.");
            CodexKey = Config.Bind(
                "Codex", "CodexKey", "F3",
                "KeyCode name for the Codex toggle hotkey (e.g. F2, F3). " +
                "See UnityEngine.KeyCode enum for valid values.");

            DiscordWebhookUrl = Config.Bind(
                "Discord", "WebhookUrl", "",
                "Discord webhook URL. Set on the server only — never share this with clients. " +
                "Leave empty to disable all discord announcements.");
            DiscordDefaultTemplate = Config.Bind(
                "Discord", "DefaultTemplate", "**{playerName}** triggered **{topic}**",
                "Default message template when a guidance entry has `announce: { discord: \"\" }` " +
                "(empty string = use default). Tokens: {playerName}, {id}, {topic}, {text}.");
            DiscordBotUsername = Config.Bind(
                "Discord", "BotUsername", "ValheimServerGuide",
                "Username shown for webhook messages in Discord.");
            DiscordGuideEnabled = Config.Bind(
                "Discord", "DiscordGuideEnabled", true,
                "Enable guide-completion webhook POSTs (discord_on_complete). " +
                "Set false to suppress these without affecting kill/event POSTs.");
            DiscordGuideFormat = Config.Bind(
                "Discord", "DiscordGuideFormat", "plain",
                "Format for guide-completion messages: 'plain' (content string) or 'embed' (rich embed).");

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            // Surface the patched-method list so we can see at a glance that
            // Harmony actually attached our hooks (and didn't silently fail).
            foreach (var m in _harmony.GetPatchedMethods())
                Log.LogInfo($"Harmony patched: {m.DeclaringType?.Name}.{m.Name}");

            _configDir = Path.Combine(Paths.ConfigPath, PluginName);
            _yamlPath = Path.Combine(_configDir, "guidance.yaml");

            GuidanceSync.Register();
            GuidanceDisplay.Initialize();
            AdminCommands.Register();

            // YAML generation policy:
            //   - Dedicated server (Application.isBatchMode == true): start loader immediately.
            //     It's the source of truth and there's no client to wait for.
            //   - Client process: defer. ZNet.Awake postfix decides per-session whether
            //     to start the loader (host/SP -> yes, pure client -> no).
            if (Application.isBatchMode)
            {
                Log.LogInfo("Running in batch mode (dedicated server). Loading guidance YAML now.");
                EnsureLoaderStarted();
            }
            else
            {
                Log.LogInfo("Client process. Guidance YAML will be loaded only if this session hosts a world.");
            }

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        /// Idempotent: safe to call from both batch-mode Awake and ZNet.Awake (host path).
        public static void EnsureLoaderStarted()
        {
            lock (_loaderLock)
            {
                if (_loader != null) return;
                Directory.CreateDirectory(_configDir);
                _loader = new GuidanceConfigLoader(_yamlPath);
                _loader.ConfigChanged += Instance.OnConfigChanged;
                _loader.Start();
                Log.LogInfo($"Guidance YAML loader started ({_yamlPath}).");
            }
        }

        public static void ShutdownLoader()
        {
            lock (_loaderLock)
            {
                if (_loader == null) return;
                _loader.Dispose();
                _loader = null;
                Log.LogInfo("Guidance YAML loader stopped.");
            }
        }

        private void OnConfigChanged(GuidanceConfig newConfig)
        {
            // Server authority guard: a client's local YAML must never override
            // what the remote server has pushed. Only the authoritative process
            // (dedicated server, host, or single-player) accepts local edits.
            var authoritative = ZNet.instance == null || ZNet.instance.IsServer();
            if (!authoritative)
            {
                Log.LogInfo("Local YAML edit ignored — remote server's config takes priority.");
                return;
            }

            CurrentConfig = newConfig;
            GuidanceDisplay.RegisterTutorials(newConfig);
            TimedTrigger.OnConfigChanged(newConfig);

            // Server pushes the new config to all connected clients.
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                GuidanceSync.BroadcastToClients(newConfig);
            }

            // Re-apply tracker layout (position/size/font from the YAML `tracker:` section) and
            // refresh rows so any in-progress chains from the reloaded config appear immediately.
            Display.GuidanceHudTracker.Instance?.ApplyLayout();
            Display.GuidanceHudTracker.Instance?.Refresh();

            // 10-F: Notify local admins of the reload. Only shown to admin players; non-admins
            // see nothing. The SynchronizationManager.PlayerIsAdmin check uses Jötunn's cached
            // admin list so it works for both host and dedicated-server admin clients.
            if (Player.m_localPlayer != null && MessageHud.instance != null
                && Jotunn.Managers.SynchronizationManager.Instance?.PlayerIsAdmin == true)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft,
                    $"[VSG] Guide config reloaded — {newConfig.Guidances.Count} entries loaded.");
            }
        }

        private void OnDestroy()
        {
            ShutdownLoader();
            _harmony?.UnpatchSelf();
        }
    }
}
