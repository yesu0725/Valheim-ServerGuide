namespace ValheimServerGuide.State
{
    /// Per-player chain progress stored in Player.m_customData alongside SeenTracker data.
    /// Step keys   : "VSG.cp.{chainId}"            = next step index (int string)
    /// Done keys   : "VSG.cd.{chainId}"            = "1" when chain complete
    /// Counter keys: "VSG.cc.{chainId}:{stepIdx}"  = current count (int string); absent = not yet activated
    /// Version keys: "VSG.cv.{chainId}"            = guide version at time of completion (Phase 10-A)
    public static class ChainState
    {
        private const string StepPrefix    = "VSG.cp.";
        private const string DonePrefix    = "VSG.cd.";
        private const string CounterPrefix = "VSG.cc.";
        private const string VersionPrefix = "VSG.cv.";

        public static int GetStep(Player player, string chainId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return 0;
            if (!player.m_customData.TryGetValue(StepPrefix + chainId, out var val)) return 0;
            return int.TryParse(val, out var n) ? n : 0;
        }

        public static void SetStep(Player player, string chainId, int step)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return;
            player.m_customData[StepPrefix + chainId] = step.ToString();
        }

        public static bool IsComplete(Player player, string chainId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return false;
            return player.m_customData.TryGetValue(DonePrefix + chainId, out var val) && val == "1";
        }

        public static void MarkComplete(Player player, string chainId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return;
            player.m_customData[DonePrefix + chainId] = "1";
            player.m_customData.Remove(StepPrefix + chainId);
        }

        public static void Reset(Player player, string chainId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return;
            player.m_customData.Remove(StepPrefix + chainId);
            player.m_customData.Remove(DonePrefix + chainId);
            player.m_customData.Remove(VersionPrefix + chainId);
            // Also remove any counter keys for this chain (VSG.cc.<chainId>:*).
            var counterPrefix = CounterPrefix + chainId + ":";
            var toRemove = new System.Collections.Generic.List<string>();
            foreach (var key in player.m_customData.Keys)
                if (key.StartsWith(counterPrefix)) toRemove.Add(key);
            foreach (var key in toRemove)
                player.m_customData.Remove(key);
        }

        /// Removes ALL chain-state keys (step, done, counter, version) from m_customData.
        /// Called by vsg_reset all so a full player-scope reset is truly complete.
        public static void ResetAll(Player player)
        {
            if (player?.m_customData == null) return;
            var toRemove = new System.Collections.Generic.List<string>();
            foreach (var key in player.m_customData.Keys)
            {
                if (key.StartsWith(StepPrefix) ||
                    key.StartsWith(DonePrefix) ||
                    key.StartsWith(CounterPrefix) ||
                    key.StartsWith(VersionPrefix))
                    toRemove.Add(key);
            }
            foreach (var key in toRemove)
                player.m_customData.Remove(key);
        }

        // ── Version helpers (Phase 10-A) ────────────────────────────────────────

        /// Returns the guide version at which this chain was completed, or 0 if never completed
        /// or completed before version tracking existed (treated as version 0 → always re-delivers
        /// if current version is >= 1).
        public static int GetCompletedVersion(Player player, string chainId)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return 0;
            if (!player.m_customData.TryGetValue(VersionPrefix + chainId, out var val)) return 0;
            return int.TryParse(val, out var n) ? n : 0;
        }

        public static void SetCompletedVersion(Player player, string chainId, int version)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return;
            player.m_customData[VersionPrefix + chainId] = version.ToString();
        }

        // ── Counter helpers (Phase 03) ──────────────────────────────────────────

        private static string CounterKey(string chainId, int stepIndex)
            => CounterPrefix + chainId + ":" + stepIndex;

        /// Returns -1 when the counter has not been activated yet (primary trigger not yet fired).
        /// Returns ≥ 0 once activated; the value is the current count toward the goal.
        public static int GetCounter(Player player, string chainId, int stepIndex)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return -1;
            if (!player.m_customData.TryGetValue(CounterKey(chainId, stepIndex), out var val)) return -1;
            return int.TryParse(val, out var n) ? n : -1;
        }

        public static void SetCounter(Player player, string chainId, int stepIndex, int value)
        {
            if (player?.m_customData == null || string.IsNullOrEmpty(chainId)) return;
            player.m_customData[CounterKey(chainId, stepIndex)] = value.ToString();
        }

        public static void ClearCounter(Player player, string chainId, int stepIndex)
        {
            player?.m_customData?.Remove(CounterKey(chainId, stepIndex));
        }
    }
}
