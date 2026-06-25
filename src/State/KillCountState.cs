using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// Per-player accumulator for multi-count `kill` trigger entries, stored in
    /// Player.m_customData alongside SeenTracker / SubmitState / ChainState data.
    ///   Progress keys: "VSG.kc.{entryId}" = kills so far (int string); absent = 0.
    ///
    /// Kills cannot be recounted from the inventory the way item_acquired goals are, so they
    /// need a persistent counter. Progress is cleared when the goal is reached (the entry then
    /// fires and is marked via SeenTracker like any other entry). For repeatable (once: false)
    /// entries the next kill starts a fresh cycle from 0.
    public static class KillCountState
    {
        private const string ProgressPrefix = "VSG.kc.";

        private static string Key(string entryId) => ProgressPrefix + entryId;

        /// Kills counted so far toward this entry's goal. 0 when nothing counted yet.
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

        /// Removes ALL kill-count progress keys. Called by vsg_reset all.
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
