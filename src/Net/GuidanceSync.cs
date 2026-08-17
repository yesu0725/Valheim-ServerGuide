using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
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
        // Server-side quest-progress store (see CRIT-26): client asks for its character's
        // progress on spawn, server pushes it, client streams every later change back as deltas.
        private const string RpcProgressRequest = "VSG_ProgReq";
        private const string RpcProgressPush = "VSG_ProgPush";
        private const string RpcProgressDelta = "VSG_ProgDelta";
        private const string RpcCompleteAnnounce = "VSG_CompleteAnnounce";
        private const string RpcRewardDiscord = "VSG_RewardDiscord";
        private const string RpcShareKillProgress = "VSG_ShareKillProgress";
        private const string RpcQuestStartLog = "VSG_QuestStartLog";
        private const string RpcConfigRequest = "VSG_ConfigReq";
        // Admin per-player state commands (list / reset another player's guidance state)
        private const string RpcAdminPlayerListReq  = "VSG_APListReq";
        private const string RpcAdminPlayerListFwd  = "VSG_APListFwd";
        private const string RpcAdminPlayerListResp = "VSG_APListResp";
        private const string RpcAdminPlayerListOut  = "VSG_APListOut";
        private const string RpcAdminPlayerResetReq = "VSG_APResetReq";
        private const string RpcAdminPlayerResetFwd = "VSG_APResetFwd";
        private const string RpcAdminPlayerResetAck = "VSG_APResetAck";
        private const string RpcAdminPlayerResetOut = "VSG_APResetOut";
        private static bool _registered;
        private static bool _rpcsBound;
        // Server-side: peer uid -> progress file key, so a disconnect can flush and unload that
        // character's file without waiting for the periodic save.
        private static readonly Dictionary<long, string> _peerFileKeys = new Dictionary<long, string>();

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
            ZRoutedRpc.instance.Register<ZPackage>(RpcProgressRequest, OnProgressRequest);
            ZRoutedRpc.instance.Register<ZPackage>(RpcProgressPush, OnProgressPush);
            ZRoutedRpc.instance.Register<ZPackage>(RpcProgressDelta, OnProgressDelta);
            ZRoutedRpc.instance.Register<string, string>(RpcCompleteAnnounce, OnCompleteAnnounce);
            ZRoutedRpc.instance.Register<string>(RpcRewardDiscord, OnRewardDiscord);
            ZRoutedRpc.instance.Register<string, string, Vector3>(RpcShareKillProgress, OnShareKillProgress);
            ZRoutedRpc.instance.Register<string>(RpcQuestStartLog, OnQuestStartLog);
            ZRoutedRpc.instance.Register<string>(RpcConfigRequest, OnConfigRequest);
            ZRoutedRpc.instance.Register<string>(RpcAdminPlayerListReq,  OnAdminPlayerListReq);
            ZRoutedRpc.instance.Register<string>(RpcAdminPlayerListFwd,  OnAdminPlayerListFwd);
            ZRoutedRpc.instance.Register<string, string>(RpcAdminPlayerListResp, OnAdminPlayerListResp);
            ZRoutedRpc.instance.Register<string>(RpcAdminPlayerListOut,  OnAdminPlayerListOut);
            ZRoutedRpc.instance.Register<string, string>(RpcAdminPlayerResetReq, OnAdminPlayerResetReq);
            ZRoutedRpc.instance.Register<string, string>(RpcAdminPlayerResetFwd, OnAdminPlayerResetFwd);
            ZRoutedRpc.instance.Register<string, string>(RpcAdminPlayerResetAck, OnAdminPlayerResetAck);
            ZRoutedRpc.instance.Register<string>(RpcAdminPlayerResetOut, OnAdminPlayerResetOut);
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
                // Player-scope `timed` entries are owned by the CLIENT — the dedicated server
                // deliberately skips them. This is the only place a client ever learns about the
                // config, so without this call no client ever scheduled one and every
                // player-scope timer silently did nothing on a dedicated-server setup.
                // (Plugin.OnConfigChanged also calls this, but returns early on any client.)
                Triggers.TimedTrigger.OnConfigChanged(config);
                Plugin.Log.LogInfo($"Received guidance config from server: {config.Guidances.Count} entries.");

                // The HUD is built at Hud.Awake, which can run before this push lands (and runs
                // again on every hot-reload broadcast). Repaint both surfaces so an already-open
                // codex / visible tracker reflects the config that just arrived instead of the
                // empty or previous one it was populated from.
                GuidanceHudTracker.Instance?.ApplyLayout();
                GuidanceHudTracker.Instance?.Refresh();
                GuidanceCodex.Instance?.RepopulateIfOpen();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"Failed to apply synced config: {ex.Message}");
            }
        }

        // ---- Config re-sync on demand (client → server) ----

        /// Client → server: "push me the current config again". Used by `vsg_refresh` to recover
        /// a client whose config never arrived (joined during a hot-reload) or was applied before
        /// the HUD existed. The server stays the sole authority — this only asks it to re-send.
        public static void RequestConfigResync()
        {
            if (ZRoutedRpc.instance == null) return;
            if (ZNet.instance == null || ZNet.instance.IsServer()) return; // host/SP owns the YAML
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcConfigRequest, "");
        }

        private static void OnConfigRequest(long sender, string _)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            SendToPeer(sender, Plugin.CurrentConfig);
            Plugin.Log.LogInfo($"[sync] re-sent config to peer {sender} on request.");
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

        // ---- Per-reward Discord announce (Phase 5: type: discord) ----

        /// Client → server. The webhook URL is a server-side secret (CRIT-08), so a
        /// `type: discord` reward can't post directly from the client — it sends the
        /// already-expanded message text and the server does the actual POST.
        public static void SendRewardDiscord(string message)
        {
            if (ZRoutedRpc.instance == null) return;
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcRewardDiscord, message ?? "");
        }

        private static void OnRewardDiscord(long sender, string message)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            DiscordAnnouncer.AnnounceRaw(message);
        }

        // ---- Group/party kill-progress sharing (Phase 6: trigger.share_progress) ----

        /// Broadcast (no server round-trip needed — purely a convenience UX nudge, not
        /// security-sensitive) so every connected client can decide locally whether the
        /// killer was close enough to count as "in the party" for this entry.
        public static void SendShareKillProgress(string entryId, string playerName, Vector3 position)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcShareKillProgress, entryId ?? "", playerName ?? "", position);
        }

        private static void OnShareKillProgress(long sender, string entryId, string playerName, Vector3 position)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            // Don't re-apply to the player who actually landed the kill — their own
            // KillCountTracker.CheckKillCount call already incremented it locally.
            if (string.Equals(playerName, player.GetPlayerName(), System.StringComparison.Ordinal)) return;
            KillCountTracker.ApplySharedIncrement(entryId, player, position);
        }

        // ---- Quest-start debug log (client → server) ----

        /// Client → server when a player begins a new quest. The quest-start webhook URL is a
        /// server-side secret (CRIT-08), so the client can't POST directly — it forwards the
        /// entry id + player + location and the server resolves base quest info from its config
        /// and does the POST. Fields are packed into one delimited string (unit-separator) so we
        /// stay on the single-arg Register<string> overload.
        public static void SendQuestStartLog(string entryId, string playerName, string biome, Vector3 position)
        {
            if (ZRoutedRpc.instance == null) return;
            var sep = ((char)0x1f).ToString(); // unit separator — won't appear in ids/names/biomes
            var encoded = string.Join(sep, new[]
            {
                entryId ?? "", playerName ?? "", biome ?? "",
                position.x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                position.y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                position.z.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            var serverPeer = ZRoutedRpc.instance.GetServerPeerID();
            ZRoutedRpc.instance.InvokeRoutedRPC(serverPeer, RpcQuestStartLog, encoded);
        }

        private static void OnQuestStartLog(long sender, string encoded)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (string.IsNullOrEmpty(encoded)) return;

            var parts = encoded.Split((char)0x1f);
            if (parts.Length < 6) return;
            var entryId    = parts[0];
            var playerName = parts[1];
            var biome      = parts[2];
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float.TryParse(parts[3], System.Globalization.NumberStyles.Float, ci, out var x);
            float.TryParse(parts[4], System.Globalization.NumberStyles.Float, ci, out var y);
            float.TryParse(parts[5], System.Globalization.NumberStyles.Float, ci, out var z);

            var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == entryId);
            DiscordAnnouncer.AnnounceQuestStart(entry, entryId, playerName, biome, new Vector3(x, y, z));
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

        // ---- Server-side quest progress (client <-> server) ----
        //
        // Progress no longer rides with the character save. On spawn the client asks the server
        // for its character's progress file and blocks all firing until it arrives (see
        // PlayerProgress.IsReady); afterwards every state write is streamed back as a delta.
        // The request carries whatever VSG.* keys are still in the character save so the server
        // can seed a brand-new file from them exactly once — the migration.

        /// Client → server: "send me character `characterId`'s progress". `legacy` is the
        /// migration seed (VSG.* keys from the character save); the server uses it only when it
        /// has no file for this character yet.
        public static void SendProgressRequest(long characterId, string playerName,
                                               Dictionary<string, string> legacy)
        {
            if (ZRoutedRpc.instance == null) return;
            var pkg = new ZPackage();
            pkg.Write(characterId);
            pkg.Write(playerName ?? "");
            WriteMap(pkg, legacy);
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcProgressRequest, pkg);
        }

        private static void OnProgressRequest(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var characterId = pkg.ReadLong();
            var playerName = pkg.ReadString();
            var legacy = ReadMap(pkg);

            var fileKey = PlayerProgressStore.FileKeyFor(playerName, characterId);
            _peerFileKeys[sender] = fileKey;

            PlayerProgressStore.Bind(fileKey, playerName, characterId, () => legacy, out var migrated);
            var data = PlayerProgressStore.Snapshot(fileKey);

            var reply = new ZPackage();
            reply.Write(characterId);
            reply.Write(migrated);
            WriteMap(reply, data);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcProgressPush, reply);

            Plugin.Log.LogInfo($"[progress] sent {data.Count} key(s) to '{playerName}' " +
                               $"(character {characterId}, peer {sender}){(migrated ? " after migrating their character file" : "")}.");
        }

        /// Server → client: here is your character's progress. Replaces the client's mirror.
        private static void OnProgressPush(long sender, ZPackage pkg)
        {
            if (Player.m_localPlayer == null) return;
            var characterId = pkg.ReadLong();
            var migrated = pkg.ReadBool();
            var data = ReadMap(pkg);
            PlayerProgress.ApplyServerPush(characterId, data, migrated);
        }

        /// Client → server: the changes made since the last flush. Called from
        /// PlayerProgress.Tick, so at most once per frame and only when something changed.
        public static void SendProgressDelta(long characterId, string playerName,
                                             List<ProgressDelta> deltas)
        {
            if (ZRoutedRpc.instance == null || deltas == null || deltas.Count == 0) return;
            var pkg = new ZPackage();
            pkg.Write(characterId);
            pkg.Write(playerName ?? "");
            pkg.Write(deltas.Count);
            foreach (var d in deltas)
            {
                pkg.Write(d.Key ?? "");
                pkg.Write(d.Removed);
                pkg.Write(d.Value ?? "");
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.instance.GetServerPeerID(), RpcProgressDelta, pkg);
        }

        private static void OnProgressDelta(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;

            var characterId = pkg.ReadLong();
            var playerName = pkg.ReadString();
            var count = pkg.ReadInt();
            var deltas = new List<ProgressDelta>(count);
            for (var i = 0; i < count; i++)
            {
                var key = pkg.ReadString();
                var removed = pkg.ReadBool();
                var value = pkg.ReadString();
                deltas.Add(new ProgressDelta { Key = key, Removed = removed, Value = value });
            }

            var fileKey = PlayerProgressStore.FileKeyFor(playerName, characterId);
            _peerFileKeys[sender] = fileKey;
            PlayerProgressStore.ApplyDeltas(fileKey, playerName, characterId, deltas);
        }

        /// Client: re-request the progress file (vsg_refresh). Re-binds the mirror from the
        /// server without clearing anything locally that the server does not know about.
        public static void RequestProgressResync()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (ZNet.instance == null || ZNet.instance.IsServer()) return; // host/SP owns the file
            SendProgressRequest(player.GetPlayerID(), player.GetPlayerName(),
                PlayerProgress.CollectLegacyProgress(player));
        }

        // ZPackage helpers: a length-prefixed string→string map.
        private static void WriteMap(ZPackage pkg, Dictionary<string, string> map)
        {
            if (map == null) { pkg.Write(0); return; }
            pkg.Write(map.Count);
            foreach (var kv in map)
            {
                pkg.Write(kv.Key ?? "");
                pkg.Write(kv.Value ?? "");
            }
        }

        private static Dictionary<string, string> ReadMap(ZPackage pkg)
        {
            var count = pkg.ReadInt();
            var map = new Dictionary<string, string>(System.StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var key = pkg.ReadString();
                var value = pkg.ReadString();
                if (!string.IsNullOrEmpty(key)) map[key] = value;
            }
            return map;
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

        // ---- Admin per-player list/reset commands ----

        /// Called from AdminCommands when the admin IS the server (listen server).
        /// Sends the forward RPC directly to the target peer; adminMarker = "server"
        /// so the response path outputs to the local console instead of relaying.
        /// Returns false if the target player is not currently online.
        public static bool ListPlayerForLocalAdmin(string targetName)
        {
            var peer = FindPeerByPlayerName(targetName);
            if (peer == null) return false;
            ZRoutedRpc.instance?.InvokeRoutedRPC(peer.m_uid, RpcAdminPlayerListFwd, "server");
            return true;
        }

        /// Called from AdminCommands when the admin IS the server (listen server).
        /// Returns false if the target player is not currently online.
        public static bool ResetPlayerForLocalAdmin(string targetName, string resetArg)
        {
            var peer = FindPeerByPlayerName(targetName);
            if (peer == null) return false;
            ZRoutedRpc.instance?.InvokeRoutedRPC(peer.m_uid, RpcAdminPlayerResetFwd, "server", resetArg ?? "all");
            return true;
        }

        /// Admin client → server: request another player's guidance state.
        public static void SendAdminPlayerListReq(string targetName)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAdminPlayerListReq, targetName ?? "");
        }

        /// Admin client → server: reset another player's guidance state.
        public static void SendAdminPlayerResetReq(string targetName, string resetArg)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAdminPlayerResetReq, targetName ?? "", resetArg ?? "all");
        }

        /// Returns online peer names for tab completion (server has the peer list; clients get empty).
        public static IEnumerable<string> GetOnlinePeerNames()
        {
            if (ZNet.instance == null) yield break;
            foreach (var p in ZNet.instance.GetPeers())
                if (!string.IsNullOrEmpty(p.m_playerName)) yield return p.m_playerName;
        }

        // Server handler: admin client asks to list a player's state.
        private static void OnAdminPlayerListReq(long sender, string targetName)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var peer = ZNet.instance.GetPeer(sender);
            var hostName = peer?.m_socket?.GetHostName();
            if (string.IsNullOrEmpty(hostName) || !ZNet.instance.IsAdmin(hostName))
            {
                Plugin.Log.LogWarning($"[admin-plist] non-admin ({sender}) tried to list '{targetName}' — denied.");
                return;
            }
            var targetPeer = FindPeerByPlayerName(targetName);
            if (targetPeer == null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminPlayerListOut,
                    $"vsg_list_player: '{targetName}' is not currently online.");
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeer.m_uid, RpcAdminPlayerListFwd, sender.ToString());
        }

        // Target player handler: server asked us to collect and return our guidance state.
        // adminMarker is either "server" (listen server admin) or the remote admin's peer UID.
        private static void OnAdminPlayerListFwd(long sender, string adminMarker)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            var payload = CollectPlayerStatePayload(player);
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAdminPlayerListResp, adminMarker, payload);
        }

        // Server handler: target player sent back their state; relay to the admin.
        private static void OnAdminPlayerListResp(long sender, string adminMarker, string payload)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (adminMarker == "server")
            {
                foreach (var line in payload.Split('\n'))
                    Console.instance?.AddString(line);
                return;
            }
            if (long.TryParse(adminMarker, out var adminPeerId))
                ZRoutedRpc.instance.InvokeRoutedRPC(adminPeerId, RpcAdminPlayerListOut, payload);
        }

        // Admin client handler: server relayed the state payload; print it.
        private static void OnAdminPlayerListOut(long sender, string payload)
        {
            if (Player.m_localPlayer == null) return;
            foreach (var line in payload.Split('\n'))
                Console.instance?.AddString(line);
        }

        // Server handler: admin client asks to reset a player's state.
        private static void OnAdminPlayerResetReq(long sender, string targetName, string resetArg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            var peer = ZNet.instance.GetPeer(sender);
            var hostName = peer?.m_socket?.GetHostName();
            if (string.IsNullOrEmpty(hostName) || !ZNet.instance.IsAdmin(hostName))
            {
                Plugin.Log.LogWarning($"[admin-preset] non-admin ({sender}) tried to reset '{targetName}' — denied.");
                return;
            }
            var targetPeer = FindPeerByPlayerName(targetName);
            if (targetPeer == null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcAdminPlayerResetOut,
                    $"vsg_reset_player: '{targetName}' is not currently online.");
                return;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeer.m_uid, RpcAdminPlayerResetFwd, sender.ToString(), resetArg);
        }

        // Target player handler: perform the requested reset on the local player.
        // adminMarker is either "server" or the remote admin's peer UID string.
        private static void OnAdminPlayerResetFwd(long sender, string adminMarker, string resetArg)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            string resultMsg;
            if (string.Equals(resetArg, "all", System.StringComparison.OrdinalIgnoreCase))
            {
                var n = SeenTracker.ClearAllFired(player);
                ChainState.ResetAll(player);
                SubmitState.ResetAll(player);
                GoalStartedState.ResetAll(player);
                KillCountState.ResetAll(player);
                ConversationNodeState.ResetAll(player);
                TrackedQuestState.ResetAll(player);
                QuestStartLogState.ResetAll(player);
                HiddenQuestState.ClearAll(player);
                GuidanceDisplay.ClearAllVsgTutorialSeen();
                GuidanceDisplay.ClearRavenState();
                GuidanceHudTracker.Instance?.Refresh();
                GuidanceCodex.Instance?.RepopulateIfOpen();
                resultMsg = $"vsg_reset_player: cleared {n} fired id(s) + all chain/submit/goal state for {player.GetPlayerName()}.";
            }
            else
            {
                var entry = Plugin.CurrentConfig?.Guidances?.Find(g => g.Id == resetArg);
                var isChain = entry?.Steps?.Count > 0;
                if (isChain) ChainState.Reset(player, resetArg);
                GuidanceDisplay.ClearVsgTutorialSeenForEntry(resetArg);
                GuidanceDisplay.ClearRavenQueueForId(resetArg);
                var singleCleared = SeenTracker.ClearFired(player, resetArg, "player");
                var hadSubmit = SubmitState.Get(player, resetArg) > 0;
                if (hadSubmit) SubmitState.Clear(player, resetArg);
                var hadGoal = GoalStartedState.IsStarted(player, resetArg);
                if (hadGoal) GoalStartedState.Clear(player, resetArg);
                var hadKill = KillCountState.Get(player, resetArg) > 0;
                if (hadKill) KillCountState.Clear(player, resetArg);
                var hadNode = ConversationNodeState.GetCurrentNode(player, resetArg) != null;
                if (hadNode) ConversationNodeState.Clear(player, resetArg);
                QuestStartLogState.Clear(player, resetArg);
                HiddenQuestState.Clear(player, resetArg);

                if (singleCleared || isChain || hadSubmit || hadGoal || hadKill || hadNode)
                {
                    GuidanceHudTracker.Instance?.Refresh();
                    GuidanceCodex.Instance?.RepopulateIfOpen();
                    resultMsg = $"vsg_reset_player: cleared '{resetArg}'"
                        + (isChain   ? " (chain)"  : "")
                        + (hadSubmit ? " (submit)"  : "")
                        + (hadGoal   ? " (goal)"    : "")
                        + (hadKill   ? " (kill)"    : "")
                        + (hadNode   ? " (node)"    : "")
                        + $" for {player.GetPlayerName()}.";
                }
                else
                {
                    resultMsg = $"vsg_reset_player: '{resetArg}' was not set for {player.GetPlayerName()} (nothing cleared).";
                }
            }

            Plugin.Log.LogInfo($"[admin-preset] {resultMsg}");
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RpcAdminPlayerResetAck, adminMarker, resultMsg);
        }

        // Server handler: target player confirmed the reset; relay result to the admin.
        private static void OnAdminPlayerResetAck(long sender, string adminMarker, string resultMsg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (adminMarker == "server")
            {
                Console.instance?.AddString(resultMsg);
                return;
            }
            if (long.TryParse(adminMarker, out var adminPeerId))
                ZRoutedRpc.instance.InvokeRoutedRPC(adminPeerId, RpcAdminPlayerResetOut, resultMsg);
        }

        // Admin client handler: print the reset result relayed from the server.
        private static void OnAdminPlayerResetOut(long sender, string resultMsg)
        {
            if (Player.m_localPlayer == null) return;
            Console.instance?.AddString(resultMsg);
        }

        /// Collect all VSG state for the local player into a human-readable multi-line payload.
        private static string CollectPlayerStatePayload(Player player)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== ValheimServerGuide ({player.GetPlayerName()}) ===");

            var fired = SeenTracker.GetFiredIds(player).OrderBy(s => s).ToList();
            sb.AppendLine($"Fired ({fired.Count}):");
            if (fired.Count == 0) sb.AppendLine("  (none)");
            else foreach (var id in fired) sb.AppendLine($"  - {id}");

            sb.AppendLine($"Storage: {PlayerProgress.DescribeMode()}");

            // max_fires counters (VSG.fc.*)
            var fcEntries = PlayerProgress.KeysWithPrefix(player, "VSG.fc.").OrderBy(k => k).ToList();
            if (fcEntries.Count > 0)
            {
                sb.AppendLine($"Fire counts ({fcEntries.Count}):");
                foreach (var k in fcEntries)
                    sb.AppendLine($"  - {k.Substring("VSG.fc.".Length)} = {PlayerProgress.Get(player, k)}");
            }

            // Chain state (VSG.cd.* = done, VSG.cp.* = step)
            var chainDone = PlayerProgress.KeysWithPrefix(player, "VSG.cd.").OrderBy(k => k).ToList();
            var chainStep = PlayerProgress.KeysWithPrefix(player, "VSG.cp.").OrderBy(k => k).ToList();
            if (chainDone.Count > 0 || chainStep.Count > 0)
            {
                sb.AppendLine("Chain state:");
                foreach (var k in chainDone) sb.AppendLine($"  - {k.Substring("VSG.cd.".Length)}: complete");
                foreach (var k in chainStep) sb.AppendLine($"  - {k.Substring("VSG.cp.".Length)}: step {PlayerProgress.Get(player, k)}");
            }

            // Submit state (VSG.is.*)
            var submitEntries = PlayerProgress.KeysWithPrefix(player, "VSG.is.").OrderBy(k => k).ToList();
            if (submitEntries.Count > 0)
            {
                sb.AppendLine("Submit state:");
                foreach (var k in submitEntries)
                    sb.AppendLine($"  - {k.Substring("VSG.is.".Length)}: {PlayerProgress.Get(player, k)} submitted");
            }

            // Goal state (VSG.ig.*)
            var goalEntries = PlayerProgress.KeysWithPrefix(player, "VSG.ig.").OrderBy(k => k).ToList();
            if (goalEntries.Count > 0)
            {
                sb.AppendLine("Goal state:");
                foreach (var k in goalEntries) sb.AppendLine($"  - {k.Substring("VSG.ig.".Length)}: started");
            }

            return sb.ToString().TrimEnd('\n', '\r');
        }

        private static ZNetPeer FindPeerByPlayerName(string name)
        {
            if (ZNet.instance == null || string.IsNullOrEmpty(name)) return null;
            foreach (var p in ZNet.instance.GetPeers())
                if (string.Equals(p.m_playerName, name, System.StringComparison.OrdinalIgnoreCase)) return p;
            return null;
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
                // Flush any queued progress before the session goes away, then drop the
                // in-memory store so a join to a different server starts from that server's files.
                PlayerProgress.EndSession();
                PlayerProgressStore.SaveAll();

                // ZRoutedRpc is torn down with ZNet; the next ZNet.Awake will create
                // a fresh instance that we must re-bind to.
                _rpcsBound = false;
                _peerFileKeys.Clear();

                // On dedicated server (batch mode) the loader was started at plugin Awake
                // and is independent of any ZNet lifecycle — leave it alone.
                if (UnityEngine.Application.isBatchMode) return;
                Plugin.ShutdownLoader();
                Plugin.CurrentConfig = GuidanceConfig.Empty;
            }
        }

        /// Bind this character's progress store, and on a pure client ask the server for it.
        /// Host / single-player binds synchronously off disk; a client stays "not ready"
        /// (no quest can fire) until the push lands. Idempotent — safe to call every frame.
        public static void EnsureProgressSession(Player player)
        {
            if (player == null) return;
            if (PlayerProgress.BeginSession(player)) return;
            SendProgressRequest(PlayerProgress.CharacterId, player.GetPlayerName(),
                PlayerProgress.CollectLegacyProgress(player));
        }

        /// A PREFIX, not a postfix: several other patches hook Player.OnSpawned and read state
        /// (FirstLoginTrigger, the tracker refresh). Harmony does not order patches across
        /// classes, so binding here guarantees the host's store is live before any of them run.
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        private static class PlayerSpawnedPatch
        {
            private static void Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return;
                EnsureProgressSession(__instance);
            }
        }

        /// Server-side: a peer left. Write their progress file now and drop it from memory
        /// instead of waiting for the periodic save — a server crash minutes later would
        /// otherwise lose whatever they did after the last flush.
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect))]
        private static class ZNetDisconnectPatch
        {
            private static void Prefix(ZNet __instance, ZNetPeer peer)
            {
                if (peer == null || !__instance.IsServer()) return;
                if (!_peerFileKeys.TryGetValue(peer.m_uid, out var fileKey)) return;
                _peerFileKeys.Remove(peer.m_uid);
                PlayerProgressStore.Unload(fileKey);
                Plugin.Log.LogInfo($"[progress] saved and unloaded '{fileKey}' ({peer.m_playerName} disconnected).");
            }
        }

        /// Client-side: flush queued progress before the world tears down, while ZRoutedRpc is
        /// still alive. ZNet.OnDestroy also flushes, but by then the RPC channel may be gone.
        [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
        private static class GameLogoutPatch
        {
            private static void Prefix()
            {
                try { PlayerProgress.FlushNow(); }
                catch (System.Exception ex) { Plugin.Log.LogError($"[progress] logout flush failed: {ex.Message}"); }
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
