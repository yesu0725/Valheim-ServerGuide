using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using ValheimServerGuide.Config;
using ValheimServerGuide.Discord;
using ValheimServerGuide.Display;
using ValheimServerGuide.State;
using ValheimServerGuide.Triggers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ValheimServerGuide.Net
{
    /// Server -> client config sync via vanilla ZRoutedRpc.
    /// The server owns the YAML; clients only ever receive bytes and deserialize them.
    public static class GuidanceSync
    {
        private const string RpcName = "VSG_SyncConfig";
        private const string RpcTriggerGlobal = "VSG_TriggerGlobal";
        private const string RpcPlayGlobal = "VSG_PlayGlobal";
        private const string RpcAnnounce = "VSG_AnnounceRequest";
        private const string RpcAdminResetGlobal = "VSG_AdminResetGlobal";
        private const string RpcTimedGuidance = "VSG_TimedGuidance";
        private const string RpcChainStepUpdate = "VSG_ChainStepUpdate";
        private const string RpcChainStateRequest = "VSG_ChainStateRequest";
        private const string RpcChainStatePush = "VSG_ChainStatePush";
        private const string RpcCompleteAnnounce = "VSG_CompleteAnnounce";
        private static bool _registered;
        private static bool _rpcsBound;
        // Server-side: player name -> (entryId -> step value or "done")
        private static readonly Dictionary<string, Dictionary<string, string>> _playerChainData
            = new Dictionary<string, Dictionary<string, string>>();

        public static void Register()
        {
            if (_registered) return;
            _registered = true;
            // ZRoutedRpc isn't ready until ZNet starts; the patch below registers on demand.
        }

        /// Idempotent. ZRoutedRpc.Register throws ArgumentException if a name is
        /// registered twice, so the second caller (RPC_PeerInfo postfix on the first
        /// client connect) used to crash mid-postfix and skip SendToPeer — leaving
        /// the joining client with no synced config.
        private static void EnsureRegistered()
        {
            if (_rpcsBound) return;
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(RpcName, OnReceive);
            ZRoutedRpc.instance.Register<string, string>(RpcTriggerGlobal, OnTriggerGlobal);
            ZRoutedRpc.instance.Register<string, string>(RpcPlayGlobal, OnPlayGlobal);
            ZRoutedRpc.instance.Register<string, string>(RpcAnnounce, OnAnnounceRequest);
            ZRoutedRpc.instance.Register<string>(RpcAdminResetGlobal, OnAdminResetGlobal);
            ZRoutedRpc.instance.Register<string>(RpcTimedGuidance, OnTimedGuidance);
            ZRoutedRpc.instance.Register<string, string, string>(RpcChainStepUpdate, OnChainStepUpdate);
            ZRoutedRpc.instance.Register<string>(RpcChainStateRequest, OnChainStateRequest);
            ZRoutedRpc.instance.Register<string, string>(RpcChainStatePush, OnChainStatePush);
            ZRoutedRpc.instance.Register<string, string>(RpcCompleteAnnounce, OnCompleteAnnounce);
            _rpcsBound = true;
            Plugin.Log.LogInfo("RPCs registered with ZRoutedRpc.");
        }

        private static void OnReceive(long sender, ZPackage pkg)
        {
            // Clients receive from the server; the server itself ignores incoming syncs.
            if (ZNet.instance != null && ZNet.instance.IsServer()) return;

            var yaml = pkg.ReadString();
            try
            {
                var config = Deserialize(yaml);
                Plugin.CurrentConfig = config;
                GuidanceDisplay.RegisterTutorials(config);
                Plugin.Log.LogInfo($"Received guidance config from server: {config.Guidances.Count} entries.");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Failed to apply synced config: {ex.Message}");
            }
        }

        public static void BroadcastToClients(GuidanceConfig config)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(Serialize(config));
            // 0L = broadcast to everyone connected.
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcName, pkg);
        }

        public static void SendToPeer(long peerId, GuidanceConfig config)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(Serialize(config));
            ZRoutedRpc.instance.InvokeRoutedRPC(peerId, RpcName, pkg);
        }

        // ---- Global-scope guidance broadcast ----

        /// Called from the dispatcher on a client when a global-scope entry matches.
        /// Routes to the server, which is the only authority allowed to mark global
        /// keys and broadcast the play to every peer.
        public static void SendTriggerGlobal(string entryId, string playerName)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcTriggerGlobal, entryId, playerName ?? "");
        }

        /// Server handler for VSG_TriggerGlobal. Validates the entry, marks the world's
        /// global key, broadcasts the play to every connected client, and (if configured)
        /// fires the discord announcement.
        private static void OnTriggerGlobal(long sender, string entryId, string playerName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            if (entry == null) { Plugin.Log.LogWarning($"[global] server got trigger for unknown id '{entryId}' from {sender}."); return; }
            if (!SeenTracker.IsGlobalScope(entry.Scope)) { Plugin.Log.LogWarning($"[global] server got trigger for non-global id '{entryId}'; ignoring."); return; }

            // Re-check "once" on server side too: a client may have raced.
            if (entry.Once && SeenTracker.HasFired(null, entry.Id, entry.Scope))
            {
                Plugin.Log.LogInfo($"[global] '{entryId}' already fired world-wide; ignoring duplicate trigger from {playerName}.");
                return;
            }

            SeenTracker.MarkFired(null, entry.Id, entry.Scope);
            Plugin.Log.LogInfo($"[global] '{entryId}' marked & broadcasting (triggered by {playerName}).");
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcPlayGlobal, entryId, playerName ?? "");

            if (entry.Announce?.Discord != null)
                DiscordAnnouncer.Announce(entry, playerName);
        }

        /// Client (and host) handler for VSG_PlayGlobal — runs the visual display.
        private static void OnPlayGlobal(long sender, string entryId, string playerName)
        {
            // Dedicated server has no local player; nothing visual to show.
            if (Player.m_localPlayer == null) return;
            GuidanceDispatcher.PlayGlobalReceived(entryId, playerName);
        }

        // ---- Discord announcement request (player-scope) ----

        /// Client → server when a player-scope event with announce.discord fires.
        public static void SendAnnounceRequest(string entryId, string playerName)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcAnnounce, entryId, playerName ?? "");
        }

        private static void OnAnnounceRequest(long sender, string entryId, string playerName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            if (entry?.Announce?.Discord == null) return;
            DiscordAnnouncer.Announce(entry, playerName);
        }

        // ---- Guide/chain completion Discord announce (client → server) ----

        /// Client → server when an entry or chain with discord_on_complete fires/completes.
        public static void SendCompleteAnnounce(string entryId, string playerName)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcCompleteAnnounce, entryId ?? "", playerName ?? "");
        }

        private static void OnCompleteAnnounce(long sender, string entryId, string playerName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            if (entry == null || !entry.DiscordOnComplete) return;
            DiscordAnnouncer.AnnounceChainComplete(playerName, entry.Title ?? entryId);
        }

        // ---- Timed guidance broadcast (server → all clients) ----

        /// Dedicated-server path: timer fires server-side, broadcasts entry ID to all clients.
        /// Each client raises the trigger through the dispatcher so per-player gates (once,
        /// cooldown, max_fires) are evaluated independently on each machine.
        public static void BroadcastTimedGuidance(string entryId)
        {
            if (ZRoutedRpc.instance == null) return;
            Plugin.Log.LogInfo($"[timed] server broadcasting '{entryId}' to all clients.");
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcTimedGuidance, entryId);
        }

        private static void OnTimedGuidance(long sender, string entryId)
        {
            if (Player.m_localPlayer == null) return; // dedicated server has no local player
            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            if (entry?.Trigger == null) return;
            var subject = entry.Trigger.Id ?? entryId;
            GuidanceDispatcher.Raise(new Triggers.TriggerEvent { Type = "timed", Subject = subject });
        }

        // ---- Chain state sync (Client <-> Server) ----

        /// Client → server: notify the server whenever a chain step advances or completes.
        /// Value is the next step index as a string, or "done" when the chain completes.
        public static void SendChainStepUpdate(string playerName, string entryId, string value)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcChainStepUpdate, playerName ?? "", entryId ?? "", value ?? "");
        }

        private static void OnChainStepUpdate(long sender, string playerName, string entryId, string value)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(entryId)) return;
            if (!_playerChainData.TryGetValue(playerName, out var data))
                _playerChainData[playerName] = data = new Dictionary<string, string>();
            data[entryId] = value;
            Plugin.Log.LogInfo($"[chain-sync] stored '{entryId}'='{value}' for player '{playerName}'.");
        }

        /// Client → server: request any chain state the server has stored for this player.
        /// Called on every player spawn so reconnects get server-pushed state back.
        public static void RequestChainState(string playerName)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcChainStateRequest, playerName ?? "");
        }

        private static void OnChainStateRequest(long sender, string playerName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!_playerChainData.TryGetValue(playerName, out var data) || data.Count == 0) return;

            // Encode as "entryId=value|entryId=value".
            var parts = new List<string>(data.Count);
            foreach (var kv in data) parts.Add(kv.Key + "=" + kv.Value);
            var encoded = string.Join("|", parts);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcChainStatePush, playerName, encoded);
        }

        /// Server → client: apply stored chain state, taking the maximum (server may be ahead
        /// of the client if a previous session's data was cached server-side).
        /// Key encoding:
        ///   "{entryId}=done"             — chain complete
        ///   "{entryId}={stepNum}"        — step progress
        ///   "{entryId}:{stepIdx}={count}" — counter progress (Phase 03)
        private static void OnChainStatePush(long sender, string playerName, string encoded)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.GetPlayerName() != playerName) return;
            if (string.IsNullOrEmpty(encoded)) return;

            foreach (var part in encoded.Split('|'))
            {
                var eq = part.IndexOf('=');
                if (eq < 0) continue;
                var key   = part.Substring(0, eq);
                var value = part.Substring(eq + 1);

                // Counter key: "{chainId}:{stepIndex}=count"
                var colon = key.IndexOf(':');
                if (colon >= 0)
                {
                    var chainId = key.Substring(0, colon);
                    var stepStr = key.Substring(colon + 1);
                    if (int.TryParse(stepStr, out var stepIdx) && int.TryParse(value, out var count))
                    {
                        var current = ChainState.GetCounter(player, chainId, stepIdx);
                        if (count > current)
                        {
                            ChainState.SetCounter(player, chainId, stepIdx, count);
                            Plugin.Log.LogInfo($"[chain-sync] server pushed counter {count} for '{chainId}' step {stepIdx} (was {current}).");
                        }
                    }
                    continue;
                }

                // Chain done or step progress: "{entryId}=done|{entryId}={stepNum}"
                var entryId = key;
                if (value == "done")
                {
                    if (!ChainState.IsComplete(player, entryId))
                    {
                        ChainState.MarkComplete(player, entryId);
                        Plugin.Log.LogInfo($"[chain-sync] server pushed complete for '{entryId}'.");
                    }
                }
                else if (int.TryParse(value, out var step))
                {
                    var current = ChainState.GetStep(player, entryId);
                    if (step > current)
                    {
                        ChainState.SetStep(player, entryId, step);
                        Plugin.Log.LogInfo($"[chain-sync] server pushed step {step} for '{entryId}' (was {current}).");
                    }
                }
            }

            // Refresh the HUD tracker now that server-restored state is applied.
            GuidanceHudTracker.Instance?.Refresh();
        }

        // ---- Admin-initiated global reset (vsg_reset <id> from an admin client) ----

        /// Client-side: admin asks the server to clear a global-scope guidance.
        public static void SendAdminResetGlobal(string entryId)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcAdminResetGlobal, entryId);
        }

        /// Server-side: re-verifies the sender is in the admin list before touching state.
        /// The Terminal.ConsoleCommand onlyAdmin gate already restricts who can type the
        /// command, but we re-check on the server because a modded/malicious client could
        /// craft the RPC directly without going through our command.
        private static void OnAdminResetGlobal(long sender, string entryId)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var peer = ZNet.instance.GetPeer(sender);
            var hostName = peer?.m_socket?.GetHostName();
            if (string.IsNullOrEmpty(hostName) || !ZNet.instance.IsAdmin(hostName))
            {
                Plugin.Log.LogWarning($"[admin-reset] non-admin sender ({sender}, host='{hostName}') tried to reset '{entryId}' — denied.");
                return;
            }

            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            if (entry == null)
            {
                Plugin.Log.LogWarning($"[admin-reset] '{entryId}' not in current config; ignoring.");
                return;
            }
            if (!SeenTracker.IsGlobalScope(entry.Scope))
            {
                Plugin.Log.LogWarning($"[admin-reset] '{entryId}' is not global-scope; ignoring.");
                return;
            }

            // Player param is unused for the global path -- pass null.
            var removed = SeenTracker.ClearFired(null, entryId, "global");
            Plugin.Log.LogInfo(removed
                ? $"[admin-reset] cleared global '{entryId}' for admin {hostName}."
                : $"[admin-reset] global '{entryId}' was not set; nothing to clear (request by {hostName}).");
        }

        private static string Serialize(GuidanceConfig config)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();
            using var sw = new StringWriter();
            serializer.Serialize(sw, config);
            return sw.ToString();
        }

        private static GuidanceConfig Deserialize(string yaml)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            return deserializer.Deserialize<GuidanceConfig>(yaml) ?? GuidanceConfig.Empty;
        }

        /// Register the RPC as soon as ZNet exists, and push current config to each joining peer.
        /// Also kicks the YAML loader on if this session is the authority (host / single-player).
        /// Pure clients reach this same hook but with IsServer() == false, so no YAML is generated.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
        private static class ZNetAwakePatch
        {
            private static void Postfix(ZNet __instance)
            {
                EnsureRegistered();
                if (__instance.IsServer())
                {
                    Plugin.Log.LogInfo("ZNet started as server/host — loading guidance YAML.");
                    Plugin.EnsureLoaderStarted();
                }
                else
                {
                    Plugin.Log.LogInfo("ZNet started as pure client — waiting for server config push.");
                }
            }
        }

        /// When the ZNet world is torn down (player returns to main menu),
        /// stop the loader so a subsequent join to a different server doesn't
        /// keep the previous session's YAML watcher alive.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnDestroy))]
        private static class ZNetOnDestroyPatch
        {
            private static void Postfix()
            {
                // ZRoutedRpc is torn down with ZNet; the next ZNet.Awake will create
                // a fresh instance that we must re-bind to.
                _rpcsBound = false;
                _playerChainData.Clear();

                // On dedicated server (batch mode) the loader was started at plugin Awake
                // and is independent of any ZNet lifecycle — leave it alone.
                if (UnityEngine.Application.isBatchMode) return;
                Plugin.ShutdownLoader();
                Plugin.CurrentConfig = GuidanceConfig.Empty;
            }
        }

        /// On every player spawn (initial and respawn) ask the server for any stored chain
        /// state. The server pushes it back via VSG_ChainStatePush; the client merges it,
        /// taking the maximum progress. This ensures reconnects pick up where the server left off.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class PlayerSpawnedPatch
        {
            private static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                RequestChainState(__instance.GetPlayerName());
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
        private static class PeerInfoPatch
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                if (!__instance.IsServer()) return;
                var peer = __instance.GetPeer(rpc);
                if (peer == null) return;
                EnsureRegistered();
                SendToPeer(peer.m_uid, Plugin.CurrentConfig);
            }
        }
    }
}
