using System.Collections.Generic;
using System.Linq;

namespace ValheimServerGuide.State
{
    /// Fire-state tracking.
    ///   player scope -> per-character via Player.m_customData (rides with character save)
    ///   global scope -> per-world via ZoneSystem global keys (rides with world save,
    ///                   auto-replicates to clients via vanilla RPC_GlobalKeys)
    public static class SeenTracker
    {
        private const string Key = "VSG.fired";
        private const string FireCountPrefix = "VSG.fc.";
        // Prefix for our ZoneSystem global keys so they don't collide with vanilla keys
        // (defeatBoss_* etc) or other mods.
        public const string GlobalKeyPrefix = "VSG.";
        private static readonly Dictionary<string, float> CooldownExpiry = new Dictionary<string, float>();

        public static string GlobalKeyFor(string id) => GlobalKeyPrefix + id;
        public static bool IsGlobalScope(string scope)
            => string.Equals(scope, "global", System.StringComparison.OrdinalIgnoreCase);

        public static bool HasFired(Player player, string id, string scope)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (IsGlobalScope(scope))
            {
                return ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeyFor(id));
            }
            if (player == null) return false;
            return GetSet(player).Contains(id);
        }

        /// Legacy overload for callers that don't know the scope (admin commands).
        public static bool HasFired(Player player, string id)
            => HasFired(player, id, "player");

        public static void MarkFired(Player player, string id, string scope)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (IsGlobalScope(scope))
            {
                // Server is the authority on global keys; clients receive via auto-replication.
                if (ZoneSystem.instance != null && (ZNet.instance == null || ZNet.instance.IsServer()))
                {
                    ZoneSystem.instance.SetGlobalKey(GlobalKeyFor(id));
                }
                return;
            }
            if (player == null) return;
            var set = GetSet(player);
            if (set.Add(id))
            {
                player.m_customData[Key] = string.Join(",", set);
            }
        }

        public static void MarkFired(Player player, string id)
            => MarkFired(player, id, "player");

        public static bool CooldownReady(string id, float cooldownSeconds, float now)
        {
            if (cooldownSeconds <= 0f) return true;
            if (!CooldownExpiry.TryGetValue(id, out var expiry)) return true;
            return now >= expiry;
        }

        public static void MarkCooldown(string id, float cooldownSeconds, float now)
        {
            if (cooldownSeconds <= 0f) return;
            CooldownExpiry[id] = now + cooldownSeconds;
        }

        /// Returns true if `id` was actually present and got removed.
        /// Handles both scopes — global removal requires server authority.
        public static bool ClearFired(Player player, string id, string scope = "player")
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (IsGlobalScope(scope))
            {
                if (ZoneSystem.instance == null) return false;
                if (ZNet.instance != null && !ZNet.instance.IsServer()) return false; // server-only
                var key = GlobalKeyFor(id);
                if (!ZoneSystem.instance.GetGlobalKey(key)) return false;
                ZoneSystem.instance.RemoveGlobalKey(key);
                CooldownExpiry.Remove(id);
                return true;
            }
            if (player == null) return false;
            // Always drop the max_fires counter + cooldown for this id, even if it was
            // never in the VSG.fired set (max_fires entries don't write VSG.fired).
            var hadCount = ClearFireCount(player, id);
            CooldownExpiry.Remove(id);
            var set = GetSet(player);
            if (!set.Remove(id)) return hadCount;
            if (set.Count == 0) player.m_customData.Remove(Key);
            else player.m_customData[Key] = string.Join(",", set);
            return true;
        }

        /// Remove the per-entry max_fires counter (VSG.fc.&lt;id&gt;). Returns true if one
        /// existed. vsg_reset must call this or a capped max_fires entry can never fire again.
        public static bool ClearFireCount(Player player, string id)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(id)) return false;
            return player.m_customData.Remove(FireCountPrefix + id);
        }

        /// Clears player-scope fires for this character. Global fires are NOT cleared
        /// (would affect every player on the world) — use vsg_reset <id> on a global id
        /// or vanilla `removekey VSG.<id>` to wipe those.
        /// Returns the count of fired IDs that were cleared.
        public static int ClearAllFired(Player player)
        {
            if (player == null) return 0;
            var count = GetSet(player).Count;
            player.m_customData.Remove(Key);
            // Also wipe every max_fires counter (VSG.fc.*) — otherwise capped entries
            // (e.g. player_death tips) stay blocked after a full reset.
            var fcKeys = player.m_customData.Keys
                .Where(k => k.StartsWith(FireCountPrefix)).ToList();
            foreach (var k in fcKeys) player.m_customData.Remove(k);
            CooldownExpiry.Clear();
            return count;
        }

        public static int GetFireCount(Player player, string id)
        {
            if (player?.m_customData == null) return 0;
            var key = FireCountPrefix + id;
            if (!player.m_customData.TryGetValue(key, out var val)) return 0;
            return int.TryParse(val, out var n) ? n : 0;
        }

        public static void IncrementFireCount(Player player, string id)
        {
            if (player?.m_customData == null) return;
            var key = FireCountPrefix + id;
            player.m_customData[key] = (GetFireCount(player, id) + 1).ToString();
        }

        public static IReadOnlyCollection<string> GetFiredIds(Player player)
        {
            if (player == null) return System.Array.Empty<string>();
            return GetSet(player);
        }

        private static HashSet<string> GetSet(Player player)
        {
            if (!player.m_customData.TryGetValue(Key, out var csv) || string.IsNullOrEmpty(csv))
                return new HashSet<string>();
            return new HashSet<string>(csv.Split(','));
        }
    }
}
