using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires the first time the local player comes within DetectRadius of each known
    /// world location instance (e.g. More World Locations AIO POIs).
    /// YAML `trigger.location` supports a trailing wildcard: `"MWL_*"` matches any MWL location.
    ///
    /// Two detection paths run each tick:
    ///   1. Location.s_allLocations  — physically-spawned Location components in the scene.
    ///      Reliable even when ZoneSystem.m_locationInstances.m_placed hasn't been synced yet.
    ///   2. ZoneSystem.m_locationInstances — catches placed locations whose prefab lacks a
    ///      Location component.  Skipped if the scene-scan already fired the same name.
    ///
    /// Both paths emit LogDebug lines so you can verify the exact prefab names in the BepInEx
    /// log (set LogLevel = Debug in BepInEx.cfg to see them).
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class LocationEnteredTrigger
    {
        private const float CheckInterval = 5f;
        private const float DetectRadius  = 40f;
        private const string KeyPrefix    = "loc_";

        private static float _nextCheck;

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            if (ZoneSystem.instance == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            var pos = __instance.transform.position;

            // Names fired during this tick — prevents double-firing when the same location
            // appears in both detection paths.
            var firedThisTick = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // ── Path 1: scene-spawned Location components ────────────────────────────────────
            // These are Location MonoBehaviours that exist as actual GameObjects right now.
            // Using this as the primary path avoids the m_placed sync-lag problem: a zone
            // generated after login is immediately visible here even before the server
            // re-broadcasts updated m_placed flags to clients.
            foreach (var loc in Location.s_allLocations)
            {
                if (loc == null) continue;
                if (Vector3.Distance(pos, loc.transform.position) > DetectRadius) continue;

                // Unity appends "(Clone)" when Instantiating; strip it for a clean prefab name.
                var name = loc.gameObject.name;
                if (name.EndsWith("(Clone)"))
                    name = name.Substring(0, name.Length - 7).TrimEnd();
                if (string.IsNullOrEmpty(name)) continue;

                Plugin.Log.LogDebug($"[location_entered] Scene scan in range: '{name}'");

                var key = KeyPrefix + name;
                if (SeenTracker.HasFired(__instance, key)) continue;
                SeenTracker.MarkFired(__instance, key);
                firedThisTick.Add(name);

                GuidanceDispatcher.Raise(new TriggerEvent { Type = "location_entered", Subject = name });
            }

            // ── Path 2: ZoneSystem location instances ────────────────────────────────────────
            // Covers placed locations whose prefab root does not have a Location component
            // (unusual but possible for some mods).  Falls back from m_prefabName to m_name
            // when the prefab name is empty.
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                var loc = kv.Value;

                var prefabName = loc.m_location?.m_prefabName;
                if (string.IsNullOrEmpty(prefabName))
                    prefabName = loc.m_location?.m_name;
                if (string.IsNullOrEmpty(prefabName)) continue;

                var dist = Vector3.Distance(pos, loc.m_position);
                if (dist > DetectRadius) continue;

                if (!loc.m_placed)
                {
                    // Log even unplaced entries so the user can confirm names in the log.
                    Plugin.Log.LogDebug($"[location_entered] ZoneSystem in range but unplaced: '{prefabName}' dist={dist:F0}");
                    continue;
                }

                Plugin.Log.LogDebug($"[location_entered] ZoneSystem in range (placed): '{prefabName}' dist={dist:F0}");

                // Skip if Path 1 already fired this name.
                if (firedThisTick.Contains(prefabName)) continue;

                var key = KeyPrefix + prefabName;
                if (SeenTracker.HasFired(__instance, key)) continue;
                SeenTracker.MarkFired(__instance, key);

                GuidanceDispatcher.Raise(new TriggerEvent { Type = "location_entered", Subject = prefabName });
            }
        }
    }
}
