using System.Collections.Generic;

namespace ValheimServerGuide.State
{
    /// Persistent "quest-start debug log already sent" latch, stored in
    /// Player.m_customData alongside the other VSG.* buckets.
    ///   Keys: "VSG.qs.{entryId}" = "1" once the start-of-quest Discord log has been posted.
    ///
    /// This exists ONLY to dedupe the quest-start debug webhook (see DiscordAnnouncer
    /// .AnnounceQuestStart) so a single quest logs "started" exactly once per character —
    /// not on every repeat fire, cooldown re-fire, or chain-step advance. It is cleared on
    /// vsg_reset / vsg_reset_player so a quest can be re-tested and re-log its start.
    public static class QuestStartLogState
    {
        private const string LoggedPrefix = "VSG.qs.";

        private static string Key(string entryId) => LoggedPrefix + entryId;

        public static bool WasLogged(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return false;
            return player.m_customData.ContainsKey(Key(entryId));
        }

        public static void MarkLogged(Player player, string entryId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(entryId)) return;
            player.m_customData[Key(entryId)] = "1";
        }

        public static void Clear(Player player, string entryId)
        {
            player?.m_customData?.Remove(Key(entryId));
        }

        /// Removes ALL quest-start log latches. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            if (player?.m_customData == null) return;
            var toRemove = new List<string>();
            foreach (var key in player.m_customData.Keys)
                if (key.StartsWith(LoggedPrefix)) toRemove.Add(key);
            foreach (var key in toRemove)
                player.m_customData.Remove(key);
        }
    }
}
