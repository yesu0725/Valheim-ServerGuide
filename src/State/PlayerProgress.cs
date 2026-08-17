using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimServerGuide.State
{
    /// One pending change to the progress store, queued for the server.
    /// `Removed` distinguishes "key deleted" from "key set to empty string".
    public struct ProgressDelta
    {
        public string Key;
        public string Value;
        public bool Removed;
    }

    /// The single source of truth for a character's VSG.* quest progress.
    ///
    /// Every state bucket (SeenTracker, ChainState, SubmitState, …) used to read and write
    /// Player.m_customData, which rides with the *character* save (.fch). That made progress
    /// portable in the wrong direction: a player could complete quests in single-player and
    /// arrive on the server with everything already done. Progress now lives in a per-character
    /// file the SERVER owns (see PlayerProgressStore) and this class is the accessor every
    /// bucket goes through.
    ///
    /// Modes:
    ///   Local   — this process owns the file (dedicated server host character, listen-server
    ///             host, single-player). Reads and writes hit the store's dictionary directly.
    ///   Remote  — pure client. We hold a mirror the server pushed on spawn; every mutation is
    ///             queued as a delta and flushed to the server on the next frame.
    ///   Legacy  — the server never answered the handshake (no VSG server-side, or an older
    ///             build). Falls back to m_customData so a client is never left with a blank
    ///             quest log; nothing is lost, it just is not server-authoritative.
    ///   Unbound — no session yet, or the handshake is still in flight. Reads see an empty
    ///             store, so the dispatcher must not fire while unbound (see IsReady).
    ///
    /// See CRIT-12 for the storage contract and CRIT-26 for the sync/migration protocol.
    public static class PlayerProgress
    {
        /// Every key this store owns. Also the filter used to pull legacy progress out of
        /// m_customData at migration time.
        public const string KeyPrefix = "VSG.";

        public enum StoreMode { Unbound, Local, Remote, Legacy }

        /// How long a client waits for the server's progress push before falling back to
        /// m_customData. Generous: the push is sent from the server's spawn handler and a
        /// loaded world can be busy.
        private const float HandshakeTimeoutSeconds = 20f;

        private static Dictionary<string, string> _data = NewMap();
        private static readonly Dictionary<string, ProgressDelta> _pending =
            new Dictionary<string, ProgressDelta>(StringComparer.Ordinal);

        private static StoreMode _mode = StoreMode.Unbound;
        private static long _characterId;
        private static string _playerName = "";
        private static string _fileKey;
        private static float _handshakeDeadline;
        private static bool _warnedUnboundWrite;

        public static StoreMode Mode => _mode;
        public static long CharacterId => _characterId;
        public static string FileKey => _fileKey;
        public static int Count => _mode == StoreMode.Legacy ? -1 : _data.Count;

        /// True once the store holds this character's real progress. The dispatcher gates every
        /// fire path on this: firing against an empty store would re-run every `once` quest and
        /// then persist the duplicate fire back to the server.
        public static bool IsReady => _mode != StoreMode.Unbound;

        public static bool HasPending => _pending.Count > 0;

        private static Dictionary<string, string> NewMap()
            => new Dictionary<string, string>(StringComparer.Ordinal);

        // ── Accessors (the m_customData replacement) ──────────────────────────────────────────
        //
        // All of these take a Player for source compatibility with the old call sites, and
        // because Legacy mode still needs the character's dictionary. Every caller in this mod
        // passes the local player: state is only ever read/written for Player.m_localPlayer.

        public static bool TryGet(Player player, string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key)) return false;
            var map = MapFor(player);
            return map != null && map.TryGetValue(key, out value);
        }

        public static bool Has(Player player, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var map = MapFor(player);
            return map != null && map.ContainsKey(key);
        }

        public static string Get(Player player, string key)
            => TryGet(player, key, out var v) ? v : null;

        public static void Set(Player player, string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            var map = MapFor(player);
            if (map == null) return;

            if (map.TryGetValue(key, out var existing) && string.Equals(existing, value, StringComparison.Ordinal))
                return; // no-op write — do not dirty the file or send a delta

            map[key] = value;
            OnMutated(key, value, removed: false);
        }

        public static bool Remove(Player player, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            var map = MapFor(player);
            if (map == null || !map.Remove(key)) return false;
            OnMutated(key, null, removed: true);
            return true;
        }

        /// Snapshot of every key under `prefix`. Returns a copy so callers can remove while
        /// iterating (which every ResetAll does).
        public static List<string> KeysWithPrefix(Player player, string prefix)
        {
            var map = MapFor(player);
            if (map == null) return new List<string>();
            return map.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        /// Removes every key under `prefix`. Returns how many were removed.
        public static int RemoveWithPrefix(Player player, string prefix)
        {
            var removed = 0;
            foreach (var key in KeysWithPrefix(player, prefix))
                if (Remove(player, key)) removed++;
            return removed;
        }

        /// Every key in the store, for the admin dump (vsg_debug / vsg_list_player).
        public static List<string> AllKeys(Player player)
        {
            var map = MapFor(player);
            return map == null ? new List<string>() : map.Keys.ToList();
        }

        /// The dictionary reads and writes land in, or null when there is nowhere to put them.
        private static Dictionary<string, string> MapFor(Player player)
        {
            switch (_mode)
            {
                case StoreMode.Local:
                case StoreMode.Remote:
                    return _data;
                case StoreMode.Legacy:
                    return player?.m_customData;
                default:
                    // Unbound. Reads return "nothing recorded"; writes are buffered in _data and
                    // reconciled by ApplyServerPush, which keeps any key the server did not send.
                    return _data;
            }
        }

        private static void OnMutated(string key, string value, bool removed)
        {
            switch (_mode)
            {
                case StoreMode.Local:
                    PlayerProgressStore.MarkDirty(_fileKey);
                    break;
                case StoreMode.Remote:
                    _pending[key] = new ProgressDelta { Key = key, Value = value, Removed = removed };
                    break;
                case StoreMode.Unbound:
                    if (!_warnedUnboundWrite)
                    {
                        _warnedUnboundWrite = true;
                        Plugin.Log.LogWarning(
                            $"[progress] write to '{key}' before the store was bound — buffered until the server responds.");
                    }
                    break;
            }
        }

        // ── Session lifecycle ────────────────────────────────────────────────────────────────

        /// Called on local-player spawn. On an authoritative process this binds (and migrates)
        /// synchronously and returns true. On a pure client it arms the handshake timeout and
        /// returns false — the caller then sends the request RPC.
        public static bool BeginSession(Player player)
        {
            if (player == null) return false;

            var characterId = ResolveCharacterId(player);
            var playerName = player.GetPlayerName() ?? "";

            if (characterId == 0)
            {
                // No stable identity to key a file by. Never silently share one file between
                // characters — fall back to the character save for this session.
                if (_mode == StoreMode.Legacy) return true; // already reported
                _mode = StoreMode.Legacy;
                Plugin.Log.LogError("[progress] could not resolve this character's id — " +
                                    "using character-file storage for this session.");
                return true;
            }

            // Same character re-spawning (death / teleport) into an already-bound session:
            // keep the store we have, do not re-handshake.
            if (_mode != StoreMode.Unbound && _characterId == characterId)
                return true;

            EndSession();
            _characterId = characterId;
            _playerName = playerName;

            var authoritative = ZNet.instance == null || ZNet.instance.IsServer();
            if (authoritative)
            {
                _fileKey = PlayerProgressStore.FileKeyFor(playerName, characterId);
                _data = PlayerProgressStore.Bind(_fileKey, playerName, characterId,
                    () => CollectLegacyProgress(player), out var migrated);
                _mode = StoreMode.Local;
                Plugin.Log.LogInfo($"[progress] bound local store '{_fileKey}' " +
                                   $"({_data.Count} key(s){(migrated ? ", migrated from character file" : "")}).");
                return true;
            }

            _handshakeDeadline = UnityEngine.Time.realtimeSinceStartup + HandshakeTimeoutSeconds;
            Plugin.Log.LogInfo($"[progress] requesting server-side progress for '{playerName}' (character {characterId}).");
            return false;
        }

        /// The character's stable id. Player.m_playerID is copied from the profile when the
        /// character loads, so the profile is the more reliable of the two at spawn time; the
        /// Player is the fallback. Both are the same value once loading has finished.
        private static long ResolveCharacterId(Player player)
        {
            var fromProfile = Game.instance?.GetPlayerProfile()?.GetPlayerID() ?? 0L;
            if (fromProfile != 0L) return fromProfile;
            return player?.GetPlayerID() ?? 0L;
        }

        /// The VSG.* keys still sitting in this character's save file — the one-time migration
        /// seed. Empty for any character created after the move to server-side storage.
        public static Dictionary<string, string> CollectLegacyProgress(Player player)
        {
            var seed = NewMap();
            if (player?.m_customData == null) return seed;
            foreach (var kv in player.m_customData)
                if (kv.Key.StartsWith(KeyPrefix, StringComparison.Ordinal))
                    seed[kv.Key] = kv.Value;
            return seed;
        }

        /// Client side: the server pushed this character's progress. Replaces the mirror.
        /// Any key written while unbound that the server did not send is kept and queued as a
        /// delta, so nothing done in the first moments after spawn is dropped.
        public static void ApplyServerPush(long characterId, Dictionary<string, string> data, bool migrated)
        {
            if (characterId != _characterId)
            {
                Plugin.Log.LogWarning($"[progress] ignoring push for character {characterId} " +
                                      $"(current is {_characterId}).");
                return;
            }

            if (data == null) data = NewMap();

            // Local writes the server's copy cannot know about yet: values buffered during the
            // handshake, plus deltas queued this frame that Tick has not flushed. Both are
            // re-applied on top of the push and re-queued, so a push never eats a local change.
            var carry = new Dictionary<string, ProgressDelta>(StringComparer.Ordinal);
            if (_mode == StoreMode.Unbound)
            {
                foreach (var kv in _data)
                    if (!data.ContainsKey(kv.Key))
                        carry[kv.Key] = new ProgressDelta { Key = kv.Key, Value = kv.Value, Removed = false };
            }
            foreach (var kv in _pending) carry[kv.Key] = kv.Value; // queued deltas are newer

            _data = data;
            _mode = StoreMode.Remote;
            _pending.Clear();

            foreach (var d in carry.Values)
            {
                if (d.Removed) _data.Remove(d.Key);
                else _data[d.Key] = d.Value;
                _pending[d.Key] = d;
            }

            Plugin.Log.LogInfo($"[progress] server pushed {data.Count} key(s)" +
                               (migrated ? " (migrated from character file this login)" : "") +
                               (carry.Count > 0 ? $"; re-applied {carry.Count} unflushed local change(s)" : "") + ".");

            OnBecameReady();
        }

        /// Spawn-time work that had to be skipped while the store was unbound. Player.OnSpawned
        /// has already come and gone by the time a client's progress arrives, so anything that
        /// only runs on spawn has to be given a second chance here or it never runs at all.
        private static void OnBecameReady()
        {
            var player = Player.m_localPlayer;
            if (player != null)
            {
                Triggers.FirstLoginTrigger.RunIfNeeded(player);
                // Re-seed item_acquired count goals from the inventory the player spawned with.
                Triggers.ItemAcquiredTrigger.CheckAllCountGoals();
                // Re-deliver any completed chain whose guide version was bumped since completion.
                Triggers.GuidanceDispatcher.CheckVersionUpdates(player, Plugin.CurrentConfig);
            }

            Display.GuidanceHudTracker.Instance?.Refresh();
            Display.GuidanceCodex.Instance?.RepopulateIfOpen();
        }

        /// Pumped from Plugin.Update. Flushes queued deltas and enforces the handshake timeout.
        public static void Tick()
        {
            if (_mode == StoreMode.Remote && _pending.Count > 0)
            {
                Net.GuidanceSync.SendProgressDelta(_characterId, _playerName, _pending.Values.ToList());
                _pending.Clear();
            }

            if (_mode == StoreMode.Unbound && _characterId != 0
                && UnityEngine.Time.realtimeSinceStartup > _handshakeDeadline)
            {
                // Anything written during the handshake window went into the buffer; Legacy mode
                // reads m_customData, so carry it across or those writes vanish.
                var player = Player.m_localPlayer;
                if (player?.m_customData != null)
                    foreach (var kv in _data)
                        player.m_customData[kv.Key] = kv.Value;

                _mode = StoreMode.Legacy;
                Plugin.Log.LogWarning(
                    "[progress] server did not send quest progress within " +
                    $"{HandshakeTimeoutSeconds:0}s — falling back to character-file storage for this session. " +
                    "Progress will save locally, not on the server (is ValheimServerGuide installed server-side?).");
                OnBecameReady();
            }
        }

        /// Force any queued deltas out now (logout, world teardown).
        public static void FlushNow()
        {
            if (_mode == StoreMode.Remote && _pending.Count > 0)
            {
                Net.GuidanceSync.SendProgressDelta(_characterId, _playerName, _pending.Values.ToList());
                _pending.Clear();
            }
            if (_mode == StoreMode.Local) PlayerProgressStore.SaveAll();
        }

        /// Drop the session (returning to the main menu, joining a different server).
        public static void EndSession()
        {
            FlushNow();
            _data = NewMap();
            _pending.Clear();
            _mode = StoreMode.Unbound;
            _characterId = 0;
            _playerName = "";
            _fileKey = null;
            _warnedUnboundWrite = false;
        }

        /// One-line status for vsg_debug.
        public static string DescribeMode()
        {
            switch (_mode)
            {
                case StoreMode.Local:  return $"local file ({_fileKey})";
                case StoreMode.Remote: return "server-side (mirrored from server)";
                case StoreMode.Legacy: return "character file (legacy fallback — server did not respond)";
                default:               return "not bound yet (waiting for server)";
            }
        }
    }
}
