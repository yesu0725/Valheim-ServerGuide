using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ValheimServerGuide.State
{
    /// Per-player record of which in-progress quests the player has pinned to the HUD progress
    /// panel (the F10 tracker), plus the player's custom on-screen position for that panel.
    /// Stored in Player.m_customData alongside the other VSG.* state buckets.
    ///   Tracked set:  "VSG.trk"        = CSV of pinned entry ids; absent = none pinned.
    ///   Panel position: "VSG.tpos"     = "x,y" (canvas-space anchoredPosition); absent = use config anchor.
    ///
    /// The tracker only shows a quest's progress while its id is in the tracked set (toggled on
    /// from the Guide Codex). The set persists with the character save, so pinned quests survive
    /// a relog (though the panel itself starts hidden each session until shown — see the tracker).
    public static class TrackedQuestState
    {
        private const string TrackedKey = "VSG.trk";
        private const string PosKey     = "VSG.tpos";

        public static bool IsTracked(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return false;
            return GetSet(player).Contains(entryId);
        }

        /// Pins (tracked = true) or unpins (tracked = false) a quest. No-op if already in the
        /// requested state. Returns true if the set actually changed.
        public static bool SetTracked(Player player, string entryId, bool tracked)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return false;
            var set = GetSet(player);
            var changed = tracked ? set.Add(entryId) : set.Remove(entryId);
            if (changed) Save(player, set);
            return changed;
        }

        public static IReadOnlyCollection<string> GetAll(Player player)
        {
            if (player == null) return System.Array.Empty<string>();
            return GetSet(player);
        }

        public static void Clear(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return;
            var set = GetSet(player);
            if (set.Remove(entryId)) Save(player, set);
        }

        /// Removes ALL tracked-quest pins. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            player?.m_customData?.Remove(TrackedKey);
        }

        // ── Panel position ────────────────────────────────────────────────────────────────────

        /// Returns the saved custom panel position, or null if the player has not moved the panel
        /// (in which case the configured corner anchor is used).
        public static UnityEngine.Vector2? GetPosition(Player player)
        {
            if (player?.m_customData == null) return null;
            if (!player.m_customData.TryGetValue(PosKey, out var val) || string.IsNullOrEmpty(val))
                return null;
            var parts = val.Split(',');
            if (parts.Length != 2) return null;
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return new UnityEngine.Vector2(x, y);
            return null;
        }

        public static void SetPosition(Player player, UnityEngine.Vector2 pos)
        {
            if (player?.m_customData == null) return;
            player.m_customData[PosKey] =
                pos.x.ToString("0.##", CultureInfo.InvariantCulture) + "," +
                pos.y.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ── Internals ─────────────────────────────────────────────────────────────────────────

        private static HashSet<string> GetSet(Player player)
        {
            if (!player.m_customData.TryGetValue(TrackedKey, out var csv) || string.IsNullOrEmpty(csv))
                return new HashSet<string>();
            return new HashSet<string>(csv.Split(',').Where(s => !string.IsNullOrEmpty(s)));
        }

        private static void Save(Player player, HashSet<string> set)
        {
            if (set.Count == 0) player.m_customData.Remove(TrackedKey);
            else player.m_customData[TrackedKey] = string.Join(",", set);
        }
    }
}
