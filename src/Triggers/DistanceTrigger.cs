using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player comes within trigger.radius metres of a world location
    /// whose prefab name matches trigger.location (trailing * wildcard supported).
    /// trigger.radius defaults to 50 m when absent or zero.
    /// Uses the same polling + per-location SeenTracker pattern as LocationEnteredTrigger:
    /// the event fires at most once per location per character.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class DistanceTrigger
    {
        private const float CheckInterval = 5f;
        private const float DefaultRadius = 50f;
        private const string KeyPrefix = "dist_";

        private static float _nextCheck;

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (ZoneSystem.instance == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            var pos = __instance.transform.position;
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                var loc = kv.Value;
                if (!loc.m_placed) continue;

                var prefabName = loc.m_location?.m_prefabName;
                if (string.IsNullOrEmpty(prefabName)) continue;

                var key = KeyPrefix + prefabName;
                if (SeenTracker.HasFired(__instance, key)) continue;

                if (!AnyEntryInRange(config, prefabName, pos, loc.m_position)) continue;

                SeenTracker.MarkFired(__instance, key);
                Plugin.Log.LogInfo($"[distance] entered range of '{prefabName}'.");
                GuidanceDispatcher.Raise(new TriggerEvent
                {
                    Type = "distance",
                    Subject = prefabName,
                });
            }
        }

        private static bool AnyEntryInRange(GuidanceConfig config, string prefabName,
            Vector3 playerPos, Vector3 locPos)
        {
            foreach (var entry in config.Guidances)
            {
                if (CheckTrigger(entry.Trigger, prefabName, playerPos, locPos)) return true;
                if (entry.Steps == null) continue;
                foreach (var step in entry.Steps)
                    if (CheckTrigger(step?.Trigger, prefabName, playerPos, locPos)) return true;
            }
            return false;
        }

        private static bool CheckTrigger(TriggerSpec t, string prefabName,
            Vector3 playerPos, Vector3 locPos)
        {
            if (t == null) return false;
            if (!string.Equals(t.Type, "distance", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (!LocationMatches(t.Location, prefabName)) return false;
            var radius = t.Radius > 0 ? t.Radius : DefaultRadius;
            return Vector3.Distance(playerPos, locPos) <= radius;
        }

        private static bool LocationMatches(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(value)) return false;
            if (pattern.EndsWith("*"))
                return value.StartsWith(pattern.Substring(0, pattern.Length - 1),
                    System.StringComparison.OrdinalIgnoreCase);
            return string.Equals(pattern, value, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
