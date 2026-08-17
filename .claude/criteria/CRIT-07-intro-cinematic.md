# CRIT-07 — Intro Cinematic

**File:** `src/Display/GuidanceDisplay.cs`
**Relevant patches:** `PlayerTakeInputPatch`, `MenuShowPatch`, `CharacterDamageIntroPatch`, `TextViewerHidePatch`, `TextViewerHideIntroPatch`, `MusicManUpdatePatch`

---

## Full Sequence

```
ShowIntroWithFade(topic, text)
  │
  ├─ IntroLockActive = true         player input frozen, ESC blocked, damage suppressed
  ├─ EngageGhostMode()              creatures stop targeting the player
  │
  └─ StartCoroutine(IntroRoutine)   (single unified path — no separate fast path)
        [fadeIn > 0 || preDelay > 0]
          EnsureOverlay(); overlay.SetActive(true); alpha = 0
          [fadeIn > 0]   animate alpha 0 to 1 over fadeIn seconds (unscaled time)
          alpha = 1
          [preDelay > 0] WaitForSecondsRealtime(preDelay)

        EngageIntroMusic()
        TextViewer.ShowText(Style.Intro, topic, text, autoHide: true)

        [overlay active] animate alpha 1 to 0 over 1.5s (world reveals), overlay.SetActive(false)

        -- hold on screen --
        loop until one of:
          - shown >= IntroDisplaySeconds                     (fixed display time)
          - shown >= minSkip && Use/JoyUse/Escape pressed     (deliberate skip)

        FadeOutIntro()               fade intro-root CanvasGroup 1->0 over IntroFadeOutDuration,
                                     then HideIntro() (reset alpha to 1 for next time)
        ReleaseGhostMode()
        ReleaseIntroLock()           freeze lifted in the SAME step as the close
```

Release is **owned by the watchdog**, not by `TextViewer.Hide`. Vanilla's
`TextViewer.LateUpdate` calls `Hide()` on any stray `Use`/`Escape` (read straight from
`ZInput`, bypassing `TakeInput`) **without** closing the Intro animation — releasing there
previously let the player move and take damage while the intro was still on screen (the
input-freeze + ghost mode were dropped early). Conversely, nothing calls `Hide`/`HideIntro`
when the animation ends on its own, so tying release to those postfixes also left the lock
**stuck** (all input dead) after an un-skipped login intro. The watchdog fixes both.

---

## Config Entries (BepInEx `Display` section)

| Key | Default | Description |
|---|---|---|
| `IntroFadeInDuration` | `3.0` | Seconds to fade screen to black before text appears |
| `IntroPreDelay` | `1.0` | Seconds to hold black screen after fade-in, before text |
| `IntroMusicName` | `"intro"` | Vanilla music track name (`MusicMan.StartMusic`) |
| `IntroMusicDuration` | `60.0` | Seconds the music stays pinned after starting |
| `IntroDisplaySeconds` | `15.0` | How long the intro text stays on screen before auto-fading out; player stays frozen + invulnerable this whole time (clamped 1-300) |
| `IntroFadeOutDuration` | `1.0` | Seconds to fade the intro text out on skip / timeout (0 = instant cut) |

---

## Black Overlay (`EnsureOverlay`)

Built once and reused. Never activates the vanilla loading screen.

```
GameObject "VSG_IntroOverlay"  (DontDestroyOnLoad)
  └─ Canvas
       renderMode: ScreenSpaceOverlay
       sortingOrder: UiLayers.Intro (32760 — above all vanilla UI)
  └─ "Black" panel
       RectTransform: anchors (0,0)→(1,1), offsets (0,0)
       Image: color=black, raycastTarget=false
       CanvasGroup: alpha=0, blocksRaycasts=false, interactable=false
```

The `CanvasGroup` is what's animated (alpha only). The Image `raycastTarget=false` means click-through when the overlay is transparent.

---

## Player Freeze (`PlayerTakeInputPatch`)

```csharp
[HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
Prefix(ref bool __result):
    if (!IntroLockActive) return true   // run vanilla
    __result = false
    return false                        // skip vanilla — character takes no input
```

`Player.TakeInput` gates: movement, mouse-look/camera, attacks, item use, interactions, inventory open, skill hotkeys. Returning `false` from this method makes the character completely inert.

---

## ESC Menu Block (`MenuShowPatch`)

```csharp
[HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
Prefix():
    return !IntroLockActive             // false = skip Menu.Show
```

`Menu.Show` is parameterless and non-static. Verified via Mono.Cecil — called from `Menu.Update`, `SaveFinished`, `OnManualSave`, `OnQuitYes`, `OnLogoutYes`.

---

## Ghost Mode

Engaged by `EngageGhostMode()`:
- Saves `Player.InGhostMode()` as `_priorGhostState`.
- Calls `Player.SetGhostMode(true)`.
- Sets `_ghostEngaged = true`.

Released by `ReleaseGhostMode()`:
- If `_ghostEngaged` and prior state was false, calls `Player.SetGhostMode(false)`.
- Resets `_ghostEngaged` and `_priorGhostState`.

Ghost mode makes the player **invisible to creature AI** (creatures stop pathing toward / attacking the player). It is **NOT** sufficient for invulnerability: `Character.RPC_Damage` has no `InGhostMode()` guard, so a ghost still takes every hit — ghost mode only clamps otherwise-lethal damage to 1 HP. True invulnerability during the intro comes from `CharacterDamageIntroPatch`.

---

## Damage Immunity (`CharacterDamageIntroPatch`)

```csharp
[HarmonyPatch(typeof(Character), "RPC_Damage")]
Prefix(Character __instance):
    if (!IntroLockActive) return true              // run vanilla
    return __instance != Player.m_localPlayer      // skip all damage to the local player
```

Suppresses every incoming hit on the local player for the whole intro span, mirroring vanilla's own cutscene damage-immunity (`RPC_Damage` early-returns when `InCutscene()`). Only the local player is affected; other characters take damage normally.

---

## Music Lock (`MusicManUpdatePatch`)

```csharp
[HarmonyPatch(typeof(MusicMan), nameof(MusicMan.UpdateCurrentMusic))]
Prefix(MusicMan __instance):
    if (!IntroMusicLockActive) return true
    if (Time.time >= IntroMusicLockUntil)
        IntroMusicLockActive = false
        return true
    if (__instance.GetCurrentMusic() != name) __instance.StartMusic(name)
    return false   // skip vanilla music selection this tick
```

The lock is **time-based** (`IntroMusicLockUntil = Time.time + IntroMusicDuration`), not text-based. Dismissing the on-screen text early does NOT stop the music. Only the time duration ending releases vanilla music control.

---

## Lock Release (watchdog-owned)

The intro lock is released **only** by `IntroRoutine`'s watchdog loop (see Full Sequence),
which ends after `IntroDisplaySeconds` on screen or on an earlier deliberate `Use`/`Escape`
skip — then fades the intro out (`FadeOutIntro`) and calls `ReleaseGhostMode()` +
`ReleaseIntroLock()` together. The close and the un-freeze happen in the same step, so there
is never a window where the player is free while the intro is still shown.

- `TextViewerHidePatch` releases **ghost mode only, and only when `IntroLockActive` is false** — i.e. for `rune` mode (which has no input lock and whose `Hide()` genuinely closes the viewer). During an intro it is a no-op, because vanilla fires `Hide()` on stray `Use`/`Escape` without closing the intro.
- `TextViewerHideIntroPatch` still releases as a safety net (idempotent) in case anything else calls `HideIntro`.
- `ZNetDestroyRavenPatch` (ZNet.OnDestroy) also calls `ReleaseIntroLock()` + `ReleaseGhostMode()` so a mid-intro disconnect/logout can't carry the static freeze flag into the next session.

`ReleaseIntroLock` has a safety teardown: if the overlay is still active when the lock releases (e.g., exception mid-coroutine), it force-hides the overlay so the player can't be stuck on a black screen.

---

## Criteria

- [ ] No vanilla loading screen (Hud.m_loadingScreen) is activated during intro.
- [ ] The custom black overlay sits above ALL other vanilla UI (`UiLayers.Intro`).
- [ ] Fade uses `Time.unscaledDeltaTime` so it works correctly even when the game is paused or slow.
- [ ] `IntroLockActive = true` and ghost mode are set before anything renders — the player is frozen and hidden from AI from the very first frame of the cinematic (both fade and no-fade paths).
- [ ] `CharacterDamageIntroPatch` suppresses ALL damage to the local player while `IntroLockActive` is true — the player cannot be harmed during the intro (ghost mode alone does not achieve this).
- [ ] The ESC (pause) menu cannot be opened while `IntroLockActive` is true.
- [ ] All player input (movement, camera, attacks, interactions) stays blocked for the WHOLE visible intro — a stray `Use`/`Escape` no longer drops the freeze early via `TextViewer.Hide`.
- [ ] Dismissing the text does NOT stop the music — music plays for `IntroMusicDuration` seconds regardless.
- [ ] The freeze is ALWAYS released: after `IntroDisplaySeconds` on screen, or on an earlier deliberate `Use`/`Escape` skip (after `minSkip`) — never left stuck after an un-skipped login intro.
- [ ] The intro visual fades out (`FadeOutIntro`, over `IntroFadeOutDuration`) and is closed in the same step the freeze is lifted — no window where the player is free while the intro is still on screen.
- [ ] A mid-intro disconnect/logout releases the lock (ZNet.OnDestroy) so it cannot carry into the next session.
- [ ] If the coroutine fails mid-execution, the safety teardown in `ReleaseIntroLock` ensures the player is never stuck on a black screen.
- [ ] `fadeIn: 0` + `preDelay: 0` skips the fade but still freezes + protects via the same coroutine/watchdog.
- [ ] The overlay GameObject survives scene transitions (`DontDestroyOnLoad`).
