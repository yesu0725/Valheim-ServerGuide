# CRIT-20 — Time & Day Triggers (Roadmap Phase 2)

**Status:** `done`

Four new trigger types fire based on real-world clock or in-game day/time: `time_of_day`,
`day_number`, `real_world_time`, `day_of_week`. No new architecture — a single background
coroutine polls every 30 seconds and fires matching entries directly via
`GuidanceDispatcher.FireEntry`, the same way `Triggers/KillTrigger.cs`'s `KillCountTracker`
evaluates its own condition and fires directly instead of routing through `Raise()`.

See [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) Phase 2.

---

## Why bypass `Raise()` / `MatchesTrigger`

Every other trigger raises a `TriggerEvent` from a real game hook (kill, craft, interact) and
`Raise()` scans entries for a match against that one event's subject. Time conditions are
different: there is no single "now" event to match against — each entry has its *own* target
(a specific day-fraction window, a specific day number, a specific UTC time, a specific weekday)
that must be evaluated against the entry's own trigger fields. So `TimeTrigger.cs` evaluates
each candidate entry's condition itself (mirroring `KillCountTracker.CheckKillCount`) and calls
`GuidanceDispatcher.CheckGates` + `FireEntry` directly. No changes to `Raise()` or
`MatchesTrigger` are needed.

---

## Trigger types

| Trigger type | Condition | Valheim API |
|---|---|---|
| `time_of_day` | `\|EnvMan.GetDayFraction() - game_time_fraction\| <= window` (wraps across midnight) | `EnvMan.instance.GetDayFraction()` |
| `day_number` | `EnvMan.GetDay() == day && EnvMan.GetDayFraction() >= 0.25` | `EnvMan.instance.GetDay()` / `GetDayFraction()` |
| `real_world_time` | `DateTime.UtcNow.Hour == utc_hour && DateTime.UtcNow.Minute == utc_minute` | `System.DateTime.UtcNow` |
| `day_of_week` | `DateTime.UtcNow.DayOfWeek.ToString() == day` (case-insensitive) | `System.DateTime.UtcNow.DayOfWeek` |

```yaml
trigger:
  type: time_of_day
  game_time_fraction: 0.0   # 0.0 = midnight, 0.5 = noon
  window: 0.02              # ± tolerance (~29 in-game minutes at the default day length)

trigger:
  type: day_number
  day: "7"                  # in-game day counter (EnvMan.GetDay())

trigger:
  type: real_world_time
  utc_hour: 20
  utc_minute: 0              # fires at 20:00 UTC

trigger:
  type: day_of_week
  day: Saturday              # real-world weekday name (System.DayOfWeek)
```

`day_number` and `day_of_week` share the YAML key `day` (per the roadmap) but different
semantics — `TriggerSpec.Day` is a `string`; `day_number` parses it as an int, `day_of_week`
matches it as a weekday name.

### `day_number` timing — matches the in-game day announcement, not midnight

`EnvMan.GetDay()` is `(int)(time / dayLengthSec)` — it ticks over at the fraction-0.0 (midnight)
boundary. But vanilla's own "Day N" message (`EnvMan.OnMorning`, confirmed via decompile) doesn't
fire until `EnvMan.UpdateTriggers` sees the day fraction cross from `<0.25` to `>0.25` (morning).
Matching on `GetDay() == day` alone fires hours early, at night, before the day "feels" new.
`day_number` therefore requires `GetDayFraction() >= 0.25f` in addition to the day match, so it
fires alongside the vanilla day-change announcement instead of at midnight.

### Once vs. repeating

No special debounce logic is needed — existing entry gates already cover both cases:
- `once: true` (default) — fires the first time the condition is true, never again (`SeenTracker`).
- `once: false` + `cooldown: <seconds>` — fires every time the condition is true, but `cooldown`
  must be long enough to span the condition's "stays true" window (e.g. a `real_world_time`
  entry firing daily needs `cooldown` > 60s so the same minute's poll ticks don't double-fire,
  and in practice ~82800s/23h so it can't re-fire later the same day).

---

## Polling

`TimeTrigger.Start()` is called once from `Plugin.Awake()` (not tied to config reload — unlike
`TimedTrigger`, there are no per-entry coroutines to restart on YAML changes; the single 30s
poll just re-reads `Plugin.CurrentConfig` each tick). Runs identically on host, dedicated server,
and pure client — each process's local player and local clock decide whether their own gates
allow the fire, exactly like every other player-scope trigger. (Phase 2 does not add global-scope
time triggers; `Scope: global` on a time entry behaves like any other global entry once it
reaches `FireEntry`, which already handles global routing.)

---

## Config (`TriggerSpec`) new fields

```csharp
/// time_of_day: target EnvMan.GetDayFraction() (0.0 = midnight, 0.5 = noon).
public float GameTimeFraction { get; set; }
/// time_of_day: ± tolerance around GameTimeFraction, as a fraction of a full day.
public float Window { get; set; } = 0.02f;
/// day_number: target EnvMan.GetDay() value (parsed as int).
/// day_of_week: target weekday name, e.g. "Saturday" (matched as string).
public string Day { get; set; }
/// real_world_time: target UTC hour (0-23) / minute (0-59).
public int UtcHour { get; set; }
public int UtcMinute { get; set; }
```

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add `GameTimeFraction`, `Window`, `Day`, `UtcHour`, `UtcMinute` to `TriggerSpec` |
| `src/Triggers/TimeTrigger.cs` | New — 30s poll coroutine; evaluates the 4 conditions and fires directly |
| `src/Plugin.cs` | Call `TimeTrigger.Start()` once from `Awake()` |
| `.claude/criteria/CRIT-02-triggers.md` | Document the 4 new trigger types |

---

## Criteria

- [x] `time_of_day` fires when `EnvMan.GetDayFraction()` is within `window` of `game_time_fraction`, including wraparound across midnight (e.g. `game_time_fraction: 0.0`, `window: 0.05`).
- [x] `day_number` fires once `EnvMan.GetDay()` reaches the configured `day` AND morning has started (`GetDayFraction() >= 0.25`) — aligned with vanilla's own "Day N" announcement, not midnight.
- [x] `real_world_time` fires at the configured UTC hour/minute.
- [x] `day_of_week` fires on the configured real-world weekday (UTC).
- [x] `once: true` entries fire exactly once per player and never again.
- [x] `once: false` + a sufficiently long `cooldown` allows a daily/recurring re-fire without firing every 30s poll tick.
- [x] Poll runs on host, dedicated server, and pure client without errors.
- [x] Build is clean: `Build succeeded. 0 Warning(s) 0 Error(s)`.
