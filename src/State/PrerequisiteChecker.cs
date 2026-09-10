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

        // `Exists(e => e.Id == reqId)` walked all 197 entries and allocated a delegate every
        // time a requirement came back unsatisfied - which is the normal case for a quest the
        // player has not finished, on every event, for every chain still in flight. That scan
        // outweighed everything the trigger-type index in GuidanceDispatcher saves, so it gets
        // the same treatment: a set built once per config and rebuilt on reference change.
        private static GuidanceConfig _idsFor;
        private static HashSet<string> _ids;

        private static bool KnownId(GuidanceConfig config, string id)
        {
            if (config == null) return false;
            if (!ReferenceEquals(_idsFor, config))
            {
                _idsFor = config;
                _ids = new HashSet<string>(System.StringComparer.Ordinal);
                if (config.Guidances != null)
                    foreach (var e in config.Guidances)
                        if (!string.IsNullOrEmpty(e?.Id)) _ids.Add(e.Id);
            }
            return _ids.Contains(id);
        }

        private static bool IsSatisfied(string reqId, Player player, GuidanceConfig config)
        {
            if (ChainState.IsComplete(player, reqId)) return true;
            if (SeenTracker.HasFired(player, reqId, "player")) return true;

            if (!KnownId(config, reqId))
                Plugin.Log.LogWarning($"[prereq] '{reqId}' not found in config — treating as unsatisfied.");
            return false;
        }
    }
}
