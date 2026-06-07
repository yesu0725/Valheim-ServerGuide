using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// Per-player progress for multi-count npc_item_submit entries, stored in
    /// Player.m_customData alongside SeenTracker / ChainState data.
    ///   Progress keys: "VSG.is.{entryId}" = items submitted so far (int string); absent = 0.
    ///
    /// Progress is cleared when the goal is reached (the entry then fires and is marked via
    /// SeenTracker like any other entry). For repeatable (once: false) entries the next
    /// submission starts a fresh cycle from 0.
    public static class SubmitState
    {
        private const string ProgressPrefix = "VSG.is.";

        private static string Key(string entryId) => ProgressPrefix + entryId;

        /// Items submitted so far toward this entry's goal. 0 when nothing submitted yet.
        public static int Get(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return 0;
            if (!player.m_customData.TryGetValue(Key(entryId), out var val)) return 0;
            return int.TryParse(val, out var n) ? n : 0;
        }

        public static void Set(Player player, string entryId, int value)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return;
            player.m_customData[Key(entryId)] = value.ToString();
        }

        public static void Clear(Player player, string entryId)
        {
            player?.m_customData?.Remove(Key(entryId));
        }

        /// Removes ALL item-submit progress keys. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            if (player?.m_customData == null) return;
            var toRemove = new List<string>();
            foreach (var key in player.m_customData.Keys)
                if (key.StartsWith(ProgressPrefix)) toRemove.Add(key);
            foreach (var key in toRemove)
                player.m_customData.Remove(key);
        }
    }
}
