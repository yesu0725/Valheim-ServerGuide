using System.Collections.Generic;
using System.Linq;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.Net;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Commands
{
    /// Console commands for inspecting and resetting guidance fire state.
    /// All commands are gated onlyAdmin: in single-player or as the host you
    /// always count as admin; on a dedicated server you must be in adminlist.txt.
    ///
    ///   vsg_reset all          clear every fired id + cooldown for this character
    ///   vsg_reset <id>         clear a single fired id + its cooldown
    ///   vsg_list               show fired ids on this character and all configured ids
    public static class AdminCommands
    {
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand(
                "vsg_reset",
                "[all|<id>]  Reset fired guidance state for the current character.",
                Reset,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: false,
                optionsFetcher: KnownIdsForResetTab,
                alwaysRefreshTabOptions: true,
                remoteCommand: false,
                onlyAdmin: true);

            new Terminal.ConsoleCommand(
                "vsg_list",
                "Show fired guidance ids on this character, plus all ids configured by the server.",
                List,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: false,
                optionsFetcher: null,
                alwaysRefreshTabOptions: false,
                remoteCommand: false,
                onlyAdmin: true);

            Plugin.Log.LogInfo("Registered console commands: vsg_reset, vsg_list");
        }

        private static void Reset(Terminal.ConsoleEventArgs args)
        {
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("vsg_reset: no local player."); return; }
            if (args.Length < 2) { args.Context.AddString("usage: vsg_reset all | vsg_reset <id>"); return; }

            var target = args[1];
            if (string.Equals(target, "all", System.StringComparison.OrdinalIgnoreCase))
            {
                var n = SeenTracker.ClearAllFired(player);
                ChainState.ResetAll(player);
                SubmitState.ResetAll(player);
                // Raven entries also carry vanilla's per-character "seen tutorial" flag
                // (Player.m_shownTutorials), which gates the raven independently of VSG
                // state. Clear it for our ids or reset raven entries would never re-show.
                GuidanceDisplay.ClearAllVsgTutorialSeen();
                GuidanceDisplay.ClearRavenState();
                args.Context.AddString($"vsg_reset: cleared {n} player-scope fired id(s) + all chain state + item-submit progress + all cooldowns + raven queue for {player.GetPlayerName()}.");
                args.Context.AddString("(global-scope entries are NOT touched by 'all' — clear individually or use removekey VSG.<id>.)");
                Plugin.Log.LogInfo($"[cmd] {player.GetPlayerName()} reset all player-scope guidance + chain state (n={n}).");
                GuidanceHudTracker.Instance?.Refresh();
                return;
            }

            // Look up the entry to find its scope (default player).
            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == target);
            var scope = entry?.Scope ?? "player";

            if (SeenTracker.IsGlobalScope(scope))
            {
                if (ZNet.instance == null)
                {
                    args.Context.AddString($"vsg_reset: not connected to a world.");
                    return;
                }

                // Server/host: clear locally and let vanilla replication push the
                // removal to every connected client.
                if (ZNet.instance.IsServer())
                {
                    if (SeenTracker.ClearFired(player, target, "global"))
                        args.Context.AddString($"vsg_reset: cleared global '{target}' (world-wide, broadcast to clients).");
                    else
                        args.Context.AddString($"vsg_reset: global '{target}' was not set (nothing to clear).");
                    return;
                }

                // Admin client: ask the server to do it. The console command is already
                // gated onlyAdmin: true, so reaching this point means we're an admin
                // (the server will re-verify via the synced admin list anyway).
                Net.GuidanceSync.SendAdminResetGlobal(target);
                args.Context.AddString($"vsg_reset: sent reset request for global '{target}' to server.");
                return;
            }

            // Raven entries carry vanilla's per-character seen-tutorial flag too; clear it
            // (for the entry and any chain-step keys) so a reset raven can re-show.
            GuidanceDisplay.ClearVsgTutorialSeenForEntry(target);
            // Remove from the display queue so a reset raven doesn't show a stale popup.
            GuidanceDisplay.ClearRavenQueueForId(target);

            // Single-entry guidances store state in SeenTracker (VSG.fired).
            var singleCleared = SeenTracker.ClearFired(player, target, "player");

            // Chain entries store state in ChainState (VSG.cd.* / VSG.cp.* / VSG.cc.*).
            // They never write to VSG.fired, so SeenTracker.ClearFired always returns false
            // for chain IDs. Call ChainState.Reset unconditionally for chain entries.
            var isChain = entry?.Steps?.Count > 0;
            if (isChain) ChainState.Reset(player, target);

            // Item-submit entries also keep in-progress collection counters (VSG.is.<id>).
            var hadSubmitProgress = SubmitState.Get(player, target) > 0;
            if (hadSubmitProgress) SubmitState.Clear(player, target);

            if (singleCleared || isChain || hadSubmitProgress)
            {
                args.Context.AddString($"vsg_reset: cleared '{target}'" + (isChain ? " (chain state)" : "") +
                    (hadSubmitProgress ? " (item-submit progress)" : "") + ".");
                Plugin.Log.LogInfo($"[cmd] {player.GetPlayerName()} reset '{target}' (chain={isChain}).");
                GuidanceHudTracker.Instance?.Refresh();
            }
            else
            {
                args.Context.AddString($"vsg_reset: '{target}' was not set (nothing to clear).");
            }
        }

        private static void List(Terminal.ConsoleEventArgs args)
        {
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("vsg_list: no local player."); return; }

            var fired = SeenTracker.GetFiredIds(player).OrderBy(s => s).ToList();
            var configured = ConfiguredIds().OrderBy(s => s).ToList();

            args.Context.AddString($"=== ValheimServerGuide ({player.GetPlayerName()}) ===");
            args.Context.AddString($"Fired ({fired.Count}):");
            if (fired.Count == 0) args.Context.AddString("  (none)");
            else foreach (var id in fired) args.Context.AddString($"  - {id}");

            args.Context.AddString($"Configured by server ({configured.Count}):");
            if (configured.Count == 0)
            {
                args.Context.AddString("  (no guidance loaded — server hasn't synced or YAML is empty)");
            }
            else
            {
                foreach (var g in Plugin.CurrentConfig?.Guidances ?? new System.Collections.Generic.List<GuidanceEntry>())
                {
                    if (string.IsNullOrEmpty(g.Id)) continue;
                    var tags = new System.Collections.Generic.List<string>();
                    if (SeenTracker.IsGlobalScope(g.Scope)) tags.Add("global");
                    if (g.Announce?.Discord != null) tags.Add("discord");
                    var hasFired = SeenTracker.HasFired(player, g.Id, g.Scope);
                    if (hasFired) tags.Add("fired");

                    // 10-D: version stamp for completed chains where stored version ≠ config version.
                    if (g.Steps?.Count > 0 && ChainState.IsComplete(player, g.Id))
                    {
                        var seenVer = ChainState.GetCompletedVersion(player, g.Id);
                        tags.Add(seenVer != g.Version
                            ? $"Complete ✓ (v{g.Version}, seen v{seenVer} — will refresh on next login)"
                            : $"Complete ✓ (v{g.Version})");
                    }

                    var tagStr = tags.Count == 0 ? "" : "  [" + string.Join(", ", tags) + "]";
                    args.Context.AddString($"  - {g.Id}{tagStr}");
                }
            }
        }

        /// Tab-complete: "all" plus any id the player has already fired.
        /// (Configured-but-not-fired ids are also useful, but we don't want to
        /// surface server-only ids on a client; fired-set is always known locally.)
        private static List<string> KnownIdsForResetTab()
        {
            var opts = new List<string> { "all" };
            var player = Player.m_localPlayer;
            if (player != null) opts.AddRange(SeenTracker.GetFiredIds(player));
            // Configured ids are also handy when issuing the command from the host/server.
            opts.AddRange(ConfiguredIds());
            return opts.Distinct().OrderBy(s => s).ToList();
        }

        private static IEnumerable<string> ConfiguredIds()
        {
            var cfg = Plugin.CurrentConfig;
            if (cfg?.Guidances == null) yield break;
            foreach (var g in cfg.Guidances)
                if (!string.IsNullOrEmpty(g.Id)) yield return g.Id;
        }
    }
}
