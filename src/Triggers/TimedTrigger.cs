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
    ///
    /// Because player-scope timers live on the CLIENT, this must be re-run wherever a client
    /// receives config — that is `GuidanceSync.OnReceive`, not just `Plugin.OnConfigChanged`
    /// (which returns early on any non-authoritative process).
    internal static class TimedTrigger
    {
        /// Live timers, keyed by entry id. The schedule is kept alongside the coroutine so a
        /// config push that does not change a timer can leave it running.
        private class Timer
        {
            public Coroutine Routine;
            public string TriggerId;
            public float Interval;
            public bool IsGlobal;
        }

        private static readonly Dictionary<string, Timer> _timers = new Dictionary<string, Timer>();

        public static void OnConfigChanged(GuidanceConfig config)
        {
            if (Plugin.Instance == null) return;
            if (config?.Guidances == null) { StopAll(); return; }

            var isDedicatedServer = Application.isBatchMode && IsServerOrHost();
            var isPureClient      = !IsServerOrHost();

            // Entries this process should be running a timer for, after the change.
            var wanted = new Dictionary<string, Timer>();

            foreach (var entry in config.Guidances)
            {
                WarnIfTimedStep(entry);

                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "timed", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.Id)) continue;

                var interval = ParseInterval(entry.Trigger.Interval);
                if (interval <= 0f)
                {
                    Plugin.Log.LogWarning($"[timed] '{entry.Id}' has invalid interval " +
                                          $"'{entry.Trigger.Interval}'; skipping. Use seconds (900), " +
                                          $"a suffixed value (15m / 2h / 1d), or daily / hourly.");
                    continue;
                }

                var isGlobal = SeenTracker.IsGlobalScope(entry.Scope);

                // Dedicated server: runs only global-scope timers (broadcasts them to clients).
                //                   Player-scope timers are owned by each client individually.
                if (isDedicatedServer && !isGlobal) continue;

                // Pure client: runs only player-scope timers locally.
                //              Global-scope timers arrive via RPC from the server.
                if (isPureClient && isGlobal) continue;

                wanted[entry.Id] = new Timer
                {
                    TriggerId = entry.Trigger.Id ?? entry.Id,
                    Interval  = interval,
                    IsGlobal  = isGlobal,
                };
            }

            // Stop timers that are gone or whose schedule changed. An unchanged timer keeps
            // running: a server-side YAML edit broadcasts to every client, and restarting the
            // coroutine there would reset the countdown — a 15-minute timer on a server whose
            // config is touched every 10 minutes would never reach the end of a single interval.
            var stale = new List<string>();
            foreach (var kv in _timers)
            {
                var keep = wanted.TryGetValue(kv.Key, out var w)
                           && w.Interval == kv.Value.Interval
                           && w.IsGlobal == kv.Value.IsGlobal
                           && string.Equals(w.TriggerId, kv.Value.TriggerId, System.StringComparison.Ordinal);
                if (!keep) stale.Add(kv.Key);
            }
            foreach (var id in stale)
            {
                if (_timers[id].Routine != null) Plugin.Instance.StopCoroutine(_timers[id].Routine);
                _timers.Remove(id);
            }

            foreach (var kv in wanted)
            {
                if (_timers.ContainsKey(kv.Key)) continue; // already running, unchanged
                var t = kv.Value;
                t.Routine = Plugin.Instance.StartCoroutine(
                    TimerRoutine(kv.Key, t.TriggerId, t.Interval, t.IsGlobal));
                _timers[kv.Key] = t;
                Plugin.Log.LogInfo($"[timed] scheduled '{kv.Key}' every {t.Interval}s " +
                                   $"({(t.IsGlobal ? "global" : "player")}; first fire in {t.Interval}s).");
            }
        }

        /// `timed` only works as a top-level entry trigger — this scans entries, never steps — so
        /// a chain step asking for it would sit there doing nothing. Say so rather than let the
        /// author debug silence.
        private static void WarnIfTimedStep(GuidanceEntry entry)
        {
            if (entry?.Steps == null) return;
            for (var i = 0; i < entry.Steps.Count; i++)
            {
                var st = entry.Steps[i]?.Trigger;
                if (st != null && string.Equals(st.Type, "timed", System.StringComparison.OrdinalIgnoreCase))
                    Plugin.Log.LogWarning($"[timed] '{entry.Id}' step {i + 1} uses type: timed, which is " +
                                          $"only supported on a top-level entry trigger. That step will never fire.");
            }
        }

        internal static void StopAll()
        {
            if (Plugin.Instance == null) { _timers.Clear(); return; }
            foreach (var t in _timers.Values)
                if (t?.Routine != null) Plugin.Instance.StopCoroutine(t.Routine);
            _timers.Clear();
        }

        private static IEnumerator TimerRoutine(string entryId, string triggerId, float interval, bool isGlobal)
        {
            yield return new WaitForSeconds(interval);
            while (true)
            {
                Plugin.Log.LogInfo($"[timed] '{entryId}' firing (subject '{triggerId}').");

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

        /// Accepts raw seconds ("900"), a suffixed duration ("30s", "15m", "2h", "1d"), or the
        /// named intervals "daily" / "hourly". Parsed with the invariant culture so a machine
        /// whose locale uses ',' as the decimal separator reads "1.5h" the same way.
        /// Returns 0 for anything unparseable, which the caller reports and skips.
        internal static float ParseInterval(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            s = s.Trim();

            if (string.Equals(s, "daily",  System.StringComparison.OrdinalIgnoreCase)) return 86400f;
            if (string.Equals(s, "hourly", System.StringComparison.OrdinalIgnoreCase)) return 3600f;

            var multiplier = 1f;
            var last = s[s.Length - 1];
            switch (char.ToLowerInvariant(last))
            {
                case 's': multiplier = 1f;     break;
                case 'm': multiplier = 60f;    break;
                case 'h': multiplier = 3600f;  break;
                case 'd': multiplier = 86400f; break;
                default:  multiplier = 0f;     break; // no suffix — plain seconds
            }
            if (multiplier > 0f) s = s.Substring(0, s.Length - 1).Trim();
            else multiplier = 1f;

            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0f
                ? f * multiplier
                : 0f;
        }

        /// True when this process is the world authority — dedicated server or session host.
        private static bool IsServerOrHost()
            => ZNet.instance == null || ZNet.instance.IsServer();
    }
}
