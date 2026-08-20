using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player comes within trigger.radius metres of a world location
    /// whose prefab name matches trigger.location (trailing * wildcard supported).
    /// trigger.radius defaults to 50 m when absent or zero.
    /// The event fires at most once per location prefab name per character
    /// (per-location SeenTracker key, same pattern as LocationEnteredTrigger).
    ///
    /// Three candidate sources are polled, because no single one is populated on every
    /// kind of process:
    ///   1. Location.s_allLocations   — location prefabs actually spawned in the scene.
    ///      The ONLY source that works for a client connected to a dedicated server, so it
    ///      is the primary path. Limited to the loaded zones around the player (well beyond
    ///      the 50 m default radius).
    ///   2. ZoneSystem.m_locationInstances — the world's full location list. Populated by
    ///      world generation, which runs on the SERVER only: on a dedicated-server client
    ///      this dictionary is permanently empty (vanilla itself branches on
    ///      ZNet.IsServer() before reading it — see ZoneSystem.GetLocationIcons). Kept for
    ///      single-player / listen-server hosts, where it catches locations that are placed
    ///      but not yet spawned, and prefabs with no Location component.
    ///   3. ZoneSystem.m_locationIcons — the position+name list the server pushes to every
    ///      client over the "LocationIcons" RPC. Covers only icon-bearing locations (boss
    ///      altars, vendors, …) but does so at any range, so a large trigger.radius still
    ///      works for those on a dedicated server.
    [HarmonyPatch(typeof(Player), nameof(Player.Update))]
    internal static class DistanceTrigger
    {
        private const float CheckInterval = 5f;
        private const float DefaultRadius = 50f;
        private const string KeyPrefix = "dist_";

        private static float _nextCheck;

        // Reused across ticks so the 5-second poll allocates nothing.
        private static readonly List<KeyValuePair<string, Vector3>> Candidates =
            new List<KeyValuePair<string, Vector3>>();
        private static readonly HashSet<string> SeenThisTick =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            // The per-location SeenTracker keys below are one-shots; skip the whole poll until
            // the progress store has loaded so we never mark one the dispatcher can't act on.
            // The next poll (CheckInterval later) picks it up.
            if (!PlayerProgress.IsReady) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            // Nothing in the config uses this trigger — skip the world scan entirely.
            var maxRadius = MaxConfiguredRadius(config);
            if (maxRadius <= 0f) return;

            var pos = __instance.transform.position;
            CollectCandidates(pos, maxRadius);

            foreach (var candidate in Candidates)
            {
                var prefabName = candidate.Key;
                var key = KeyPrefix + prefabName;
                if (SeenTracker.HasFired(__instance, key)) continue;

                if (!AnyEntryInRange(config, prefabName, pos, candidate.Value)) continue;

                SeenTracker.MarkFired(__instance, key);
                Plugin.Log.LogInfo($"[distance] entered range of '{prefabName}'.");
                GuidanceDispatcher.Raise(new TriggerEvent
                {
                    Type = "distance",
                    Subject = prefabName,
                });
            }
        }

        /// Gathers (prefabName, worldPos) pairs from every source available on this process.
        /// Candidates farther away than the largest configured radius are dropped here so the
        /// per-entry matching below only ever looks at plausible hits.
        private static void CollectCandidates(Vector3 playerPos, float maxRadius)
        {
            Candidates.Clear();
            SeenThisTick.Clear();
            var maxSqr = maxRadius * maxRadius;

            // ── Source 1: scene-spawned Location components (works on every process) ─────────
            foreach (var loc in Location.s_allLocations)
            {
                if (loc == null) continue;
                var locPos = loc.transform.position;
                if ((locPos - playerPos).sqrMagnitude > maxSqr) continue;

                var name = TriggerUtils.NormalizePrefabName(loc.gameObject.name);
                if (string.IsNullOrEmpty(name)) continue;
                if (!SeenThisTick.Add(name)) continue;

                Plugin.Log.LogDebug($"[distance] scene scan in range: '{name}'");
                Candidates.Add(new KeyValuePair<string, Vector3>(name, locPos));
            }

            if (ZoneSystem.instance == null) return;

            // ── Source 2: ZoneSystem location instances (host / single-player only) ──────────
            foreach (var kv in ZoneSystem.instance.m_locationInstances)
            {
                var loc = kv.Value;
                if (!loc.m_placed) continue;
                if ((loc.m_position - playerPos).sqrMagnitude > maxSqr) continue;

                var prefabName = loc.m_location?.m_prefabName;
                if (string.IsNullOrEmpty(prefabName)) prefabName = loc.m_location?.m_name;
                if (string.IsNullOrEmpty(prefabName)) continue;
                if (!SeenThisTick.Add(prefabName)) continue;

                Plugin.Log.LogDebug($"[distance] ZoneSystem instance in range: '{prefabName}'");
                Candidates.Add(new KeyValuePair<string, Vector3>(prefabName, loc.m_position));
            }

            // ── Source 3: location icons pushed to clients over RPC ──────────────────────────
            foreach (var kv in ZoneSystem.instance.m_locationIcons)
            {
                if ((kv.Key - playerPos).sqrMagnitude > maxSqr) continue;
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (!SeenThisTick.Add(kv.Value)) continue;

                Plugin.Log.LogDebug($"[distance] location icon in range: '{kv.Value}'");
                Candidates.Add(new KeyValuePair<string, Vector3>(kv.Value, kv.Key));
            }
        }

        /// Largest radius any `distance` trigger in the config asks for, or 0 when the config
        /// has no `distance` trigger at all.
        private static float MaxConfiguredRadius(GuidanceConfig config)
        {
            var max = 0f;
            foreach (var entry in config.Guidances)
            {
                Consider(entry.Trigger, ref max);
                if (entry.Steps == null) continue;
                foreach (var step in entry.Steps)
                    Consider(step?.Trigger, ref max);
            }
            return max;
        }

        private static void Consider(TriggerSpec t, ref float max)
        {
            if (!IsDistance(t)) return;
            var radius = t.Radius > 0 ? t.Radius : DefaultRadius;
            if (radius > max) max = radius;
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
            if (!IsDistance(t)) return false;
            if (!LocationMatches(t.Location, prefabName)) return false;
            var radius = t.Radius > 0 ? t.Radius : DefaultRadius;
            return Vector3.Distance(playerPos, locPos) <= radius;
        }

        private static bool IsDistance(TriggerSpec t)
        {
            return t != null &&
                   string.Equals(t.Type, "distance", System.StringComparison.OrdinalIgnoreCase);
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
