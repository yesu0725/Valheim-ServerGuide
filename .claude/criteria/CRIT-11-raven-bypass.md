# CRIT-11 — Raven Vanilla-Gate Bypass

**File:** `src/Display/GuidanceDisplay.cs`
**Patches:** `RavenSpawnBypassPatch`

---

## Problem

Vanilla Valheim has a per-session static flag `Raven.m_tutorialsEnabled` that is toggled by the in-game "Tutorials Enabled" option. When the player turns off tutorials, `Raven.m_tutorialsEnabled = false` and `Raven.Spawn()` immediately returns without showing any popup — including ours.

We cannot simply set this flag permanently to `true` because that would override the player's preference for vanilla Raven hints, which is explicitly out of scope.

---

## Solution: Per-Call Bypass

`RavenSpawnBypassPatch` is a Harmony Prefix + Postfix on `Raven.Spawn`:

```csharp
[HarmonyPatch(typeof(Raven), nameof(Raven.Spawn))]
internal static class RavenSpawnBypassPatch
{
    private static void Prefix(Raven.RavenText text, out bool __state)
    {
        __state = false;
        if (text == null) return;
        if (!GuidanceDisplay.RegisteredTutorialNames.Contains(text.m_key)) return;
        if (Raven.m_tutorialsEnabled) return;  // already on — nothing to do
        Raven.m_tutorialsEnabled = true;
        __state = true;                         // flag that we changed it
    }

    private static void Postfix(bool __state)
    {
        if (__state) Raven.m_tutorialsEnabled = false;  // restore immediately
    }
}
```

The flag is only modified for the exact duration of the `Spawn()` call, and only for raven texts whose `m_key` is in `RegisteredTutorialNames`. Vanilla Raven hints are never affected.

---

## Parameter Name Gotcha

The Harmony patch uses `Raven.RavenText text` as the parameter name. The actual method signature parameter is named `text` (NOT `raventext`). Harmony binds by name — using the wrong name causes a runtime error: `Parameter 'raventext' not found in method`.

Always verify parameter names via Mono.Cecil on the publicized DLL before writing a Harmony patch.

---

## Mod's Own Toggle: `RavenEnabled`

The mod adds its own BepInEx config entry (`Display > RavenEnabled`, default `true`) that gates raven-mode guidance independently of the vanilla setting.

```
Vanilla tutorials off + RavenEnabled true  → OUR hints still show (bypass active)
Vanilla tutorials on  + RavenEnabled false → OUR hints suppressed; vanilla hints unaffected
Vanilla tutorials off + RavenEnabled false → All raven hints suppressed
Vanilla tutorials on  + RavenEnabled true  → All raven hints show
```

The `RavenEnabled` check happens in `GuidanceDisplay.Show()`, before `Player.ShowTutorial` is called. If disabled, the entry is suppressed with a log message and `ShowTutorial` is never invoked — so the vanilla raven queue is never polluted.

---

## Tutorial Registration

For Raven to spawn a popup, the tutorial entry must be in `Tutorial.instance.m_texts`. We add entries via:

1. **`RegisterTutorials(config)`** — called after every config load/sync when `Tutorial.instance` is available.
2. **`EnsureTutorialRegistered(entry)`** — called lazily at `Show()` time in case `Tutorial.instance` became available after config load.
3. **`UpdateTutorialText(id, renderedText)`** — called in `Show()` immediately after registration to overwrite the `m_text` slot with the live-rendered text. This ensures `message:` fields and template variables (`{player_name}` etc.) are correctly reflected in the popup.

`RegisteredTutorialNames` (a `HashSet<string>`) tracks which IDs are already registered to avoid duplicates. It is `internal` so `RavenSpawnBypassPatch` can check it.

**Text source priority** (both registration and live update):
`entry.Message` → `entry.Display.Text` → `""`

---

## Criteria

- [ ] When vanilla "Tutorials Enabled" is OFF, our raven hints still show if `RavenEnabled = true`.
- [ ] When vanilla "Tutorials Enabled" is ON, vanilla raven hints are unaffected by our bypass.
- [ ] `Raven.m_tutorialsEnabled` is restored to its original value immediately after `Spawn()` returns.
- [ ] The `RavenSpawnBypassPatch` only activates for entry IDs in `RegisteredTutorialNames`.
- [ ] `RavenEnabled = false` suppresses all our raven hints without touching vanilla hints.
- [ ] Tutorial entries are never added to `m_texts` more than once (deduped via `RegisteredTutorialNames`).
- [ ] Harmony patch parameter `text` must match the actual method signature (not `raventext`).
