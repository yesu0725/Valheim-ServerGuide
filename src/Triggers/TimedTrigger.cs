using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Net;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Recurring timer that fires guidance entries on a schedule.
    ///
    /// Scope routing:
    ///   player-scope — each client runs its own coroutine and raises the event locally so
    ///                  per-player gates (requires, once, cooldown) are evaluated independently.
    ///                  The dedicated server does NOT broadcast player-scope timers.
    ///   global-scope — server/host runs the coroutine; dedicated server broadcasts via RPC so
    ///                  every client receives the event. Pure clients skip global timers.
    internal static class TimedTrigger
    {
        private static readonly Dictionary<string, Coroutine> _coroutines =
            new Dictionary<string, Coroutine>();

        public static void OnConfigChanged(GuidanceConfig config)
        {
            StopAll();
            if (config?.Guidances == null) return;

            var isDedicatedServer = Application.isBatchMode && IsServerOrHost();
            var isPureClient      = !IsServerOrHost();

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

                var isGlobal = SeenTracker.IsGlobalScope(entry.Scope);

                // Dedicated server: runs only global-scope timers (broadcasts them to clients).
                //                   Player-scope timers are owned by each client individually.
                if (isDedicatedServer && !isGlobal) continue;

                // Pure client: runs only player-scope timers locally.
                //              Global-scope timers arrive via RPC from the server.
                if (isPureClient && isGlobal) continue;

                var entryId   = entry.Id;
                var triggerId = entry.Trigger.Id ?? entry.Id;
                var coroutine = Plugin.Instance.StartCoroutine(TimerRoutine(entryId, triggerId, interval, isGlobal));
                _coroutines[entryId] = coroutine;
                Plugin.Log.LogInfo($"[timed] scheduled '{entryId}' every {interval}s ({(isGlobal ? "global" : "player")}).");
            }
        }

        private static void StopAll()
        {
            if (Plugin.Instance == null) return;
            foreach (var c in _coroutines.Values)
                if (c != null) Plugin.Instance.StopCoroutine(c);
            _coroutines.Clear();
        }

        private static IEnumerator TimerRoutine(string entryId, string triggerId, float interval, bool isGlobal)
        {
            yield return new WaitForSeconds(interval);
            while (true)
            {
                Plugin.Log.LogInfo($"[timed] '{entryId}' firing.");

                if (isGlobal && Application.isBatchMode)
                {
                    // Dedicated server + global scope: broadcast to all clients.
                    GuidanceSync.BroadcastTimedGuidance(entryId);
                }
                else
                {
                    // Host, single-player, or client running a player-scope timer: raise locally.
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
