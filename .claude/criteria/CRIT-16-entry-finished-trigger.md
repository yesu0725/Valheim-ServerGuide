# CRIT-16 — `entry_finished` Trigger

**Status:** `done`

Fires a new guidance entry automatically when a specified entry has completed.
Works for both single-entry fires and the final step of a chain.

---

## Overview

The `entry_finished` trigger lets one guidance entry react to the completion of another.
This enables sequencing without chains — a separate entry waits until a named entry has
fired and then starts automatically with no further player action required.

**Dispatcher file:** `src/Triggers/GuidanceDispatcher.cs`
**Config file:** `src/Config/GuidanceConfig.cs`

---

## Trigger YAML

```yaml
- id: followup_tip
  trigger:
    type: entry_finished
    entry: some_other_entry_id   # fires when this entry completes
  display:
    mode: raven
    topic: "Next Step"
    text: "Great work! Here is what to do next..."
```

---

## TriggerSpec Field

Add `Entry` (string) to `TriggerSpec`:

```csharp
/// entry_finished: the ID of the entry whose completion triggers this one.
public string Entry { get; set; }
```

---

## Dispatcher Matching

In `GuidanceDispatcher.MatchesTrigger`:

```csharp
case "entry_finished": return Eq(t.Entry, evt.Subject);
```

---

## Event Raising

When a single entry fires OR when a chain advances to completion, a deferred
`entry_finished` event is raised **after** the current `Raise()` loop finishes.

### Why deferred (not immediate)?

Calling `Raise()` recursively inside the `foreach` loop over `config.Guidances` would
walk the list while it is still being iterated and could stack-overflow if two entries
form a mutual completion cycle. Collecting completions in a `List<string>` and calling
`Raise()` for each **after** the loop exits is safe and bounds the recursion to one
extra level per fire event.

### Guard against infinite cycles

`entry_finished` entries default to `once: true` (the system-wide default), so
mutual completion cycles (A finishes → B starts → B finishes → A starts → …) naturally
terminate after each entry fires once. Authors who set `once: false` are responsible for
using `cooldown` or `stop_when` to prevent runaway loops.

---

## Implementation Locations

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add `Entry` field to `TriggerSpec` |
| `src/Triggers/GuidanceDispatcher.cs` | Collect deferred IDs; raise `entry_finished` after loop; add `case "entry_finished"` to `MatchesTrigger` |
| `.claude/criteria/CRIT-02-triggers.md` | Document `entry_finished` in Implemented Trigger Types section |

---

## Criteria

- [x] `entry_finished` raises after a single-entry fires (player scope).
- [x] `entry_finished` raises after a single-entry fires (global scope, on the receiving client).
- [x] `entry_finished` raises when the final step of a chain completes.
- [x] `entry_finished` does NOT raise when an intermediate chain step advances (only on full chain completion).
- [x] `entry_finished` events are deferred until the primary `Raise()` loop finishes.
- [x] `trigger.entry` matching is case-insensitive.
- [x] A null or absent `trigger.entry` never matches anything.
- [x] Two entries mutually referencing each other via `entry_finished` do not infinite-loop when both use `once: true` (default).
- [x] Unknown `trigger.entry` IDs log a debug message and never match.
