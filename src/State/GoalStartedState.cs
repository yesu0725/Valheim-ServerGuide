using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// Persistent "started" flag for item_acquired multi-goal entries, stored in
    /// Player.m_customData alongside SeenTracker / ChainState / SubmitState data.
    ///   Started keys: "VSG.ig.{entryId}" = "1" once the player has collected toward any goal.
    ///
    /// Inventory counts alone cannot tell "never started" from "started then items removed"
    /// (e.g. crafted away, dropped, died). This flag latches the first observed progress so the
    /// entry stays visible in the HUD tracker and Codex even when every goal item drops back to 0.
    /// It is cleared when the entry fires (goal reached) and on vsg_reset.
    public static class GoalStartedState
    {
        private const string StartedPrefix = "VSG.ig.";

        private static string Key(string entryId) => StartedPrefix + entryId;

        public static bool IsStarted(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return false;
            return player.m_customData.ContainsKey(Key(entryId));
        }

        public static void MarkStarted(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return;
            player.m_customData[Key(entryId)] = "1";
        }

        public static void Clear(Player player, string entryId)
        {
            player?.m_customData?.Remove(Key(entryId));
        }

        /// Removes ALL goal-started flags. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            if (player?.m_customData == null) return;
            var toRemove = new List<string>();
            foreach (var key in player.m_customData.Keys)
                if (key.StartsWith(StartedPrefix)) toRemove.Add(key);
            foreach (var key in toRemove)
                player.m_customData.Remove(key);
        }
    }
}
