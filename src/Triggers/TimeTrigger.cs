using System;
using System.Collections;
using UnityEngine;
using ValheimServerGuide.Config;

namespace ValheimServerGuide.Triggers
{
    /// Polls real-world clock and in-game day/time every 30s and fires matching entries
    /// directly. Each entry's condition depends on its own trigger fields (target fraction,
    /// day, UTC hour/minute, weekday), so this evaluates per-entry like KillCountTracker does,
    /// instead of routing through GuidanceDispatcher.Raise/MatchesTrigger.
    internal static class TimeTrigger
    {
        private const float PollInterval = 30f;
        private static Coroutine _coroutine;

        public static void Start()
        {
            if (_coroutine != null) return;
            _coroutine = Plugin.Instance.StartCoroutine(PollRoutine());
        }

        private static IEnumerator PollRoutine()
        {
            var wait = new WaitForSeconds(PollInterval);
            while (true)
            {
                yield return wait;
                Tick();
            }
        }

        private static void Tick()
        {
            var player = Player.m_localPlayer;
            if (player == null) return;

            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return;

            var now = DateTime.UtcNow;
            var envMan = EnvMan.instance;

            foreach (var entry in config.Guidances)
            {
                var t = entry.Trigger;
                if (t == null) continue;

                bool matches;
                switch (t.Type?.ToLowerInvariant())
                {
                    case "time_of_day":
                        if (envMan == null) continue;
                        matches = MatchesTimeOfDay(envMan, t);
                        break;
                    case "day_number":
                        if (envMan == null) continue;
                        // EnvMan.GetDay() ticks over at the fraction-0.0 (midnight) boundary,
                        // but vanilla's "Day N" announcement (EnvMan.OnMorning) doesn't fire
                        // until the fraction crosses 0.25 (morning). Match the day number AND
                        // require morning has started so this fires alongside that announcement
                        // instead of at night, hours before the new day "feels" new.
                        matches = envMan.GetDay() == ParseDay(t.Day) && envMan.GetDayFraction() >= 0.25f;
                        break;
                    case "real_world_time":
                        matches = now.Hour == t.UtcHour && now.Minute == t.UtcMinute;
                        break;
                    case "day_of_week":
                        matches = string.Equals(now.DayOfWeek.ToString(), t.Day,
                            StringComparison.OrdinalIgnoreCase);
                        break;
                    default:
                        continue;
                }

                if (!matches) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;

                Plugin.Log.LogInfo($"[time] '{entry.Id}' condition matched ({t.Type}).");
                GuidanceDispatcher.FireEntry(entry, new TriggerEvent { Type = t.Type });
            }
        }

        private static bool MatchesTimeOfDay(EnvMan envMan, TriggerSpec t)
        {
            var diff = Mathf.Abs(envMan.GetDayFraction() - t.GameTimeFraction);
            diff = Mathf.Min(diff, 1f - diff);
            return diff <= t.Window;
        }

        private static int ParseDay(string s) => int.TryParse(s, out var d) ? d : -1;
    }
}
