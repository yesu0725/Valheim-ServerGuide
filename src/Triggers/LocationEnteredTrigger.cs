using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires the first time the local player comes within DetectRadius of each known
    /// world location instance (e.g. More World Locations AIO POIs).
    /// YAML `trigger.location` supports a trailing wildcard: `"WL_*"` matches any WL location.
    /// Uses periodic polling on Player.Update to avoid per-frame overhead.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class LocationEnteredTrigger
    {
        private const float CheckInterval = 5f;
        private const float DetectRadius = 40f;
        private const string KeyPrefix = "loc_";

        private static float _nextCheck;

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (ZoneSystem.instance == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            var pos = __instance.transform.position;
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                var loc = kv.Value;
                if (!loc.m_placed) continue;
                if (Vector3.Distance(pos, loc.m_position) > DetectRadius) continue;

                var prefabName = loc.m_location?.m_prefabName;
                if (string.IsNullOrEmpty(prefabName)) continue;

                var key = KeyPrefix + prefabName;
                if (SeenTracker.HasFired(__instance, key)) continue;
                SeenTracker.MarkFired(__instance, key);

                GuidanceDispatcher.Raise(new TriggerEvent
                {
                    Type = "location_entered",
                    Subject = prefabName,
                });
            }
        }
    }
}
