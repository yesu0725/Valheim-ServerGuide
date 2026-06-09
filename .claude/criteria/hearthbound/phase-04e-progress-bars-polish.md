# Phase 04e — Progress Bars, Completion Flash & Polish

**Status:** `done`
**Depends on:** Phase 04c (highlight/fade infrastructure already in place)
**Blocks:** nothing (final tracker polish layer before Phase 05)

Replaces raw `N/Goal` counter text with inline progress bars for counter steps, adds a
completion flash effect when a chain finishes, updates the badge with a live quest count,
and optionally plays a vanilla sound on step progress.

---

## Features

### 1. Inline Progress Bar for Counter Steps

Counter steps currently show `2/5` as plain text. This replaces that suffix with an inline
ASCII fill bar and numeric label:

```
> Slayer Skills    [====    ] 2/5
```

Bar characters:
- Filled segment: `=`
- Empty segment: ` ` (space within brackets)
- Brackets: `[` and `]`
- Width: 8 segments total (fixed, not configurable in Phase 04e)

The bar is part of the row's single `TextMeshProUGUI` string, not a separate UI element.
Pure ASCII — no sprites or custom glyphs needed.

Format string:
```csharp
private static string ProgressBar(int current, int goal, int width = 8)
{
    var filled = goal > 0 ? Mathf.RoundToInt((float)current / goal * width) : 0;
    filled = Mathf.Clamp(filled, 0, width);
    return "[" + new string('=', filled) + new string(' ', width - filled) + "] " + current + "/" + goal;
}
```

Non-counter steps (step N/total style) continue to show plain `1/3` without brackets.

### 2. Quest Count Badge Update

The hint badge (04b) text changes from a static format to a live-updated one:

- 0 active chains → `[F9] Quests`
- 1+ active chains → `[F9] Quests (2)` (badge shows count in parentheses)
- Badge is updated from `Refresh()` so it reflects the live chain count.

### 3. Completion Flash

When a chain completes (all steps done, `ChainState.IsComplete` becomes true), its row:
1. Briefly flashes white-gold (`new Color(1f, 1f, 0.7f)`) for 0.4 s.
2. Then fades to transparent over 0.6 s.
3. Then `Refresh()` removes it from the list (it disappears naturally).

This is implemented via a `_completingRows` dictionary mapping row index → elapsed flash time,
driven in `Update()`. `GuidanceDispatcher.AdvanceChain()` calls a new
`GuidanceHudTracker.Instance?.FlashCompletion(string chainId)` method that looks up the row
index for that chain and starts the flash timer.

### 4. Vanilla SFX on Step Progress

When `fromProgress = true` and at least one counter or step advances, play a vanilla
sound to give tactile feedback.

- Sound: `"sfx_skilllevelup"` (the same ping used for skill level-up events in Valheim).
- Played via `Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "", 0, null)` is
  **not** the right path — use `FejdStartup`? No — use `ZSFXSystem` (the vanilla sound effect
  player): `ZSFXSystem.instance.PlayAudio(sfxHash)` where `sfxHash` is `"sfx_skilllevelup"`.

  Actually the simplest path: `ZSFX.SetVolume()` is internal. Use:
  ```csharp
  var clip = ZoneSystem.instance != null
      ? AudioMan.instance?.GetAudioClip("sfx_skilllevelup")
      : null;
  if (clip != null) AudioSource.PlayClipAtPoint(clip, Player.m_localPlayer.transform.position, 0.4f);
  ```
  Fallback: if no clip found, silently skip (no error).

- SFX is configurable: new YAML field `tracker.progress_sfx_enabled` (bool, default `true`).
- SFX does NOT fire on login Refresh or YAML reload — only on `fromProgress: true` calls.

---

## YAML Shape

```yaml
tracker:
  progress_sfx_enabled: true    # play a sound when a step or counter advances
```

---

## New YAML Field in `TrackerSpec`

```csharp
public bool ProgressSfxEnabled { get; set; } = true;
```

---

## Files Changed

| File | Change |
|---|---|
| `src/Display/GuidanceHudTracker.cs` | `ProgressBar()` helper; `FlashCompletion()`; `_completingRows`; SFX call in `Refresh()`; badge count update |
| `src/Config/GuidanceConfig.cs` | `TrackerSpec.ProgressSfxEnabled` |
| `src/Triggers/GuidanceDispatcher.cs` | Call `FlashCompletion(entry.Id)` after chain completes |

---

## Criteria

- [x] Counter steps show an inline `[====    ] N/Goal` bar instead of plain `N/Goal` text.
- [x] Bar fill is proportional: 0/5 shows empty `[        ]`, 5/5 shows full `[========]`.
- [x] Non-counter steps (step-index progress) still show plain `1/3` without brackets.
- [x] Badge correctly shows `(N)` count suffix when N > 0 active chains; no suffix when 0.
- [x] Badge count updates after every Refresh() call.
- [x] Chain completion triggers a brief white-gold flash on the row before it disappears.
- [x] Flash does not linger past chain removal (cleanup in Update when timer expires).
- [x] `completion_vfx_enabled: true` spawns vanilla level-up VFX/SFX on chain completion (design changed from progress SFX to completion VFX per user preference).
- [x] `completion_vfx_enabled: false` suppresses VFX without error.
- [x] VFX does not fire on login Refresh or YAML reload (only on chain completion).
- [x] If the EffectList has no effects configured, the mod fails gracefully with no exception.
- [x] All bar and flash logic uses vanilla-only components. No custom assets. See CRIT-14.
- [x] No regression to fade, highlight, hotkey, tooltip, or layout behaviour.
