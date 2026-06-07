using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Net;

namespace ValheimServerGuide.Triggers
{
    /// Server/host-side recurring timer that fires guidance entries on a schedule.
    /// On a dedicated server (batch mode): broadcasts VSG_TimedGuidance RPC so each
    /// client evaluates its own dispatcher gates (once, cooldown, max_fires).
    /// On a host/single-player: raises the event directly via GuidanceDispatcher.
    /// Pure clients never run timers — they receive broadcasts from the server instead.
    internal static class TimedTrigger
    {
        private static readonly Dictionary<string, Coroutine> _coroutines =
            new Dictionary<string, Coroutine>();

        public static void OnConfigChanged(GuidanceConfig config)
        {
            StopAll();
            if (config?.Guidances == null) return;
            if (!IsServerOrHost()) return;

            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "timed", System.StringComparison.OrdinalIgnoreCase)) continue;

                var interval = ParseInterval(entry.Trigger.Interval);
                if (interval <= 0f)
                {
                    Plugin.Log.LogWarning($"[timed] '{entry.Id}' has invalid interval '{entry.Trigger.Interval}'; skipping.");
                    continue;
                }

                var entryId = entry.Id;
                var triggerId = entry.Trigger.Id ?? entry.Id;
                var coroutine = Plugin.Instance.StartCoroutine(TimerRoutine(entryId, triggerId, interval));
                _coroutines[entryId] = coroutine;
                Plugin.Log.LogInfo($"[timed] scheduled '{entryId}' every {interval}s.");
            }
        }

        private static void StopAll()
        {
            if (Plugin.Instance == null) return;
            foreach (var c in _coroutines.Values)
                if (c != null) Plugin.Instance.StopCoroutine(c);
            _coroutines.Clear();
        }

        private static IEnumerator TimerRoutine(string entryId, string triggerId, float interval)
        {
            yield return new WaitForSeconds(interval);
            while (true)
            {
                Plugin.Log.LogInfo($"[timed] '{entryId}' firing.");

                if (Application.isBatchMode)
                {
                    // Dedicated server: let each client evaluate its own dispatcher gates.
                    GuidanceSync.BroadcastTimedGuidance(entryId);
                }
                else
                {
                    // Host or single-player: raise locally.
                    GuidanceDispatcher.Raise(new TriggerEvent { Type = "timed", Subject = triggerId });
                }

                yield return new WaitForSeconds(interval);
            }
        }

        private static float ParseInterval(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            if (string.Equals(s, "daily",  System.StringComparison.OrdinalIgnoreCase)) return 86400f;
            if (string.Equals(s, "hourly", System.StringComparison.OrdinalIgnoreCase)) return 3600f;
            return float.TryParse(s, out var f) ? f : 0f;
        }

        /// True when this process is the world authority — dedicated server or session host.
        private static bool IsServerOrHost()
            => ZNet.instance == null || ZNet.instance.IsServer();
    }
}
