using System.Collections.Generic;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.State
{
    /// Evaluates whether all `requires:` entries are satisfied for a given player.
    /// A prerequisite is satisfied when the player has either completed a chain with
    /// that ID or fired a single-entry guidance with that ID.
    public static class PrerequisiteChecker
    {
        public static bool AllSatisfied(List<string> requires, Player player, GuidanceConfig config)
        {
            if (requires == null || requires.Count == 0) return true;
            foreach (var reqId in requires)
                if (!IsSatisfied(reqId, player, config)) return false;
            return true;
        }

        private static bool IsSatisfied(string reqId, Player player, GuidanceConfig config)
        {
            if (ChainState.IsComplete(player, reqId)) return true;
            if (SeenTracker.HasFired(player, reqId, "player")) return true;

            var exists = config?.Guidances?.Exists(e => e.Id == reqId) ?? false;
            if (!exists)
                Plugin.Log.LogWarning($"[prereq] '{reqId}' not found in config — treating as unsatisfied.");
            return false;
        }
    }
}
