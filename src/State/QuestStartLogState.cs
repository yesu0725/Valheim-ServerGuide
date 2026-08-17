namespace ValheimServerGuide.State
{
    /// Persistent "quest-start debug log already sent" latch, stored via PlayerProgress
    /// (server-owned progress file) alongside the other VSG.* buckets.
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
            if (player == null || string.IsNullOrEmpty(entryId)) return false;
            return PlayerProgress.Has(player, Key(entryId));
        }

        public static void MarkLogged(Player player, string entryId)
        {
            if (player == null || string.IsNullOrEmpty(entryId)) return;
            PlayerProgress.Set(player, Key(entryId), "1");
        }

        public static void Clear(Player player, string entryId)
        {
            if (player == null || string.IsNullOrEmpty(entryId)) return;
            PlayerProgress.Remove(player, Key(entryId));
        }

        /// Removes ALL quest-start log latches. Called by vsg_reset all.
        public static void ResetAll(Player player)
        {
            if (player == null) return;
            PlayerProgress.RemoveWithPrefix(player, LoggedPrefix);
        }
    }
}
