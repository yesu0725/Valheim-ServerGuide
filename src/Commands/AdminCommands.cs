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

            new Terminal.ConsoleCommand(
                "vsg_list_player",
                "<playerName>  Show guidance fired state for another online player.",
                ListPlayer,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: false,
                optionsFetcher: OnlinePlayerNamesTab,
                alwaysRefreshTabOptions: true,
                remoteCommand: false,
                onlyAdmin: true);

            new Terminal.ConsoleCommand(
                "vsg_reset_player",
                "<playerName> [all|<id>]  Reset guidance state for another online player.",
                ResetPlayer,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: false,
                optionsFetcher: OnlinePlayersAndIdsTab,
                alwaysRefreshTabOptions: true,
                remoteCommand: false,
                onlyAdmin: true);

            new Terminal.ConsoleCommand(
                "vsg_debug",
                "Dump eligible entries, all VSG.* custom-data keys, and the last 10 fired ids for this character.",
                Debug,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: false,
                allowInDevBuild: false,
                optionsFetcher: null,
                alwaysRefreshTabOptions: false,
                remoteCommand: false,
                onlyAdmin: true);

            Plugin.Log.LogInfo("Registered console commands: vsg_reset, vsg_list, vsg_list_player, vsg_reset_player, vsg_debug");
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
                GoalStartedState.ResetAll(player);
                KillCountState.ResetAll(player);
                ConversationNodeState.ResetAll(player);
                TrackedQuestState.ResetAll(player);
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

            // item_acquired multi-goal entries latch a "started" flag (VSG.ig.<id>).
            var hadGoalStarted = GoalStartedState.IsStarted(player, target);
            if (hadGoalStarted) GoalStartedState.Clear(player, target);

            // kill count entries keep a persistent accumulator (VSG.kc.<id>).
            var hadKillProgress = KillCountState.Get(player, target) > 0;
            if (hadKillProgress) KillCountState.Clear(player, target);

            // multi-node conversations keep a current-node pointer (VSG.cn.<id>).
            var hadNodeProgress = ConversationNodeState.GetCurrentNode(player, target) != null;
            if (hadNodeProgress) ConversationNodeState.Clear(player, target);

            // A reset quest is no longer in progress — drop its tracker pin so it doesn't linger.
            TrackedQuestState.Clear(player, target);

            if (singleCleared || isChain || hadSubmitProgress || hadGoalStarted || hadKillProgress || hadNodeProgress)
            {
                args.Context.AddString($"vsg_reset: cleared '{target}'" + (isChain ? " (chain state)" : "") +
                    (hadSubmitProgress ? " (item-submit progress)" : "") +
                    (hadGoalStarted ? " (goal progress)" : "") +
                    (hadKillProgress ? " (kill progress)" : "") +
                    (hadNodeProgress ? " (conversation node)" : "") + ".");
                Plugin.Log.LogInfo($"[cmd] {player.GetPlayerName()} reset '{target}' (chain={isChain}).");
                GuidanceHudTracker.Instance?.Refresh();
            }
            else
            {
                args.Context.AddString($"vsg_reset: '{target}' was not set (nothing to clear).");
            }
        }

        private static void Debug(Terminal.ConsoleEventArgs args)
        {
            var player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("vsg_debug: no local player."); return; }

            var config = Plugin.CurrentConfig;
            args.Context.AddString($"=== VSG Debug ({player.GetPlayerName()}) ===");

            // 1. Entries currently eligible to fire (gates passing). Chains use their own
            // prerequisite + completion check since CheckGates' once/cooldown logic doesn't
            // apply to them the same way.
            args.Context.AddString("-- Eligible now (gates passing) --");
            var eligible = new List<string>();
            foreach (var g in config?.Guidances ?? new List<GuidanceEntry>())
            {
                if (string.IsNullOrEmpty(g.Id)) continue;
                bool isEligible;
                if (g.Steps != null && g.Steps.Count > 0)
                    isEligible = !ChainState.IsComplete(player, g.Id)
                        && PrerequisiteChecker.AllSatisfied(g.Requires, player, config);
                else
                    isEligible = ValheimServerGuide.Triggers.GuidanceDispatcher.CheckGates(g, player);
                if (isEligible) eligible.Add(g.Id);
            }
            if (eligible.Count == 0) args.Context.AddString("  (none)");
            else foreach (var id in eligible) args.Context.AddString($"  - {id}");

            // 2. All VSG.* keys in m_customData.
            args.Context.AddString("-- VSG.* custom-data keys --");
            var vsgKeys = player.m_customData.Keys
                .Where(k => k.StartsWith("VSG.", System.StringComparison.Ordinal))
                .OrderBy(k => k).ToList();
            if (vsgKeys.Count == 0) args.Context.AddString("  (none)");
            else foreach (var k in vsgKeys) args.Context.AddString($"  - {k} = {player.m_customData[k]}");

            // 3. Last 10 fired entry ids with timestamps (session-only log).
            args.Context.AddString("-- Last fired (this session) --");
            var history = DebugFireLog.Get(player.GetPlayerName());
            if (history.Count == 0) args.Context.AddString("  (none yet this session)");
            else foreach (var h in history) args.Context.AddString($"  - {h.When:HH:mm:ss}  {h.Id}");
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

                    // max_fires entries don't write VSG.fired; surface their counter so
                    // they're visible as "fired" and you can confirm a reset cleared them.
                    var maxFires = g.Trigger?.MaxFires ?? 0;
                    if (maxFires > 0)
                    {
                        var fc = SeenTracker.GetFireCount(player, g.Id);
                        if (fc > 0) tags.Add($"fired {fc}/{maxFires}");
                    }

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

        private static void ListPlayer(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 2) { args.Context.AddString("usage: vsg_list_player <playerName>"); return; }
            if (ZNet.instance == null) { args.Context.AddString("vsg_list_player: not connected to a world."); return; }

            var targetName = args[1];

            // On listen server: send the forward RPC directly to the target peer.
            if (ZNet.instance.IsServer())
            {
                if (!Net.GuidanceSync.ListPlayerForLocalAdmin(targetName))
                    args.Context.AddString($"vsg_list_player: '{targetName}' is not currently online. (Use vsg_list for your own state.)");
                else
                    args.Context.AddString($"vsg_list_player: requesting state for '{targetName}'...");
                return;
            }

            // Admin client: route through server.
            args.Context.AddString($"vsg_list_player: requesting state for '{targetName}'...");
            Net.GuidanceSync.SendAdminPlayerListReq(targetName);
        }

        private static void ResetPlayer(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 3) { args.Context.AddString("usage: vsg_reset_player <playerName> all | vsg_reset_player <playerName> <id>"); return; }
            if (ZNet.instance == null) { args.Context.AddString("vsg_reset_player: not connected to a world."); return; }

            var targetName = args[1];
            var resetArg  = args[2];

            // On listen server: send the forward RPC directly to the target peer.
            if (ZNet.instance.IsServer())
            {
                if (!Net.GuidanceSync.ResetPlayerForLocalAdmin(targetName, resetArg))
                    args.Context.AddString($"vsg_reset_player: '{targetName}' is not currently online.");
                else
                    args.Context.AddString($"vsg_reset_player: sent reset '{resetArg}' to '{targetName}' — result incoming...");
                return;
            }

            // Admin client: route through server.
            args.Context.AddString($"vsg_reset_player: sent reset '{resetArg}' to '{targetName}' — result incoming...");
            Net.GuidanceSync.SendAdminPlayerResetReq(targetName, resetArg);
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

        private static List<string> OnlinePlayerNamesTab()
            => Net.GuidanceSync.GetOnlinePeerNames().OrderBy(s => s).ToList();

        // Combined tab options for vsg_reset_player: online player names + "all" + configured IDs.
        private static List<string> OnlinePlayersAndIdsTab()
        {
            var opts = new List<string>();
            opts.AddRange(Net.GuidanceSync.GetOnlinePeerNames());
            opts.Add("all");
            opts.AddRange(ConfiguredIds());
            return opts.Distinct().OrderBy(s => s).ToList();
        }
    }
}
