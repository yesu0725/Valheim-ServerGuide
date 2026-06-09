# CRIT-07 — Intro Cinematic

**File:** `src/Display/GuidanceDisplay.cs`
**Relevant patches:** `PlayerTakeInputPatch`, `MenuShowPatch`, `TextViewerHidePatch`, `TextViewerHideIntroPatch`, `MusicManUpdatePatch`

---

## Full Sequence

```
ShowIntroWithFade(topic, text)
  │
  ├─ EngageGhostMode()              player invulnerable + hidden from creatures
  │
  ├─ [fadeIn <= 0 && preDelay <= 0] ── fast path (no transition)
  │     EngageIntroMusic()
  │     TextViewer.ShowText(Intro)
  │     return
  │
  └─ StartCoroutine(IntroFadeRoutine)
        IntroLockActive = true       player input frozen, ESC blocked
        EnsureOverlay()              create black canvas if needed
        overlay.SetActive(true)
        overlayGroup.alpha = 0

        [fadeIn > 0]
          animate alpha 0 → 1 over fadeIn seconds (unscaled time)

        overlayGroup.alpha = 1

        [preDelay > 0]
          WaitForSecondsRealtime(preDelay)

        EngageIntroMusic()
        TextViewer.ShowText(Style.Intro, topic, text, autoHide: true)

        animate alpha 1 → 0 over 1.5s (fade-out, world reveals)
        overlay.SetActive(false)

        IntroLockActive stays true until TextViewer is dismissed
```

---

## Config Entries (BepInEx `Display` section)

| Key | Default | Description |
|---|---|---|
| `IntroFadeInDuration` | `3.0` | Seconds to fade screen to black before text appears |
| `IntroPreDelay` | `1.0` | Seconds to hold black screen after fade-in, before text |
| `IntroMusicName` | `"intro"` | Vanilla music track name (`MusicMan.StartMusic`) |
| `IntroMusicDuration` | `60.0` | Seconds the music stays pinned after starting |

---

## Black Overlay (`EnsureOverlay`)

Built once and reused. Never activates the vanilla loading screen.

```
GameObject "VSG_IntroOverlay"  (DontDestroyOnLoad)
  └─ Canvas
       renderMode: ScreenSpaceOverlay
       sortingOrder: 32760          (above all vanilla UI)
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

Ghost mode makes the player **invulnerable** and **invisible to creature AI** (creatures stop pathing toward / attacking the player).

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

## Lock Release (`TextViewerHidePatch` + `TextViewerHideIntroPatch`)

Both `TextViewer.Hide` and `TextViewer.HideIntro` postfixes call:

```csharp
GuidanceDisplay.ReleaseGhostMode();
GuidanceDisplay.ReleaseIntroLock();
```

`ReleaseIntroLock` also has a safety teardown: if the overlay is still active when the lock releases (e.g., exception mid-coroutine), it force-hides the overlay so the player can't be stuck on a black screen.

---

## Criteria

- [ ] No vanilla loading screen (Hud.m_loadingScreen) is activated during intro.
- [ ] The custom black overlay sits above ALL other vanilla UI (sortingOrder 32760).
- [ ] Fade uses `Time.unscaledDeltaTime` so it works correctly even when the game is paused or slow.
- [ ] `IntroLockActive = true` is set before the fade starts — the player is frozen from the very beginning of the cinematic.
- [ ] Ghost mode is engaged before the fade starts — the player is invulnerable during the dark transition.
- [ ] The ESC (pause) menu cannot be opened while `IntroLockActive` is true.
- [ ] All player input (movement, camera, attacks, interactions) is blocked while `IntroLockActive` is true.
- [ ] Dismissing the text does NOT stop the music — music plays for `IntroMusicDuration` seconds regardless.
- [ ] `IntroLockActive` is released when the TextViewer closes (any path: click, auto-hide animator).
- [ ] If the coroutine fails mid-execution, the safety teardown in `ReleaseIntroLock` ensures the player is never stuck on a black screen.
- [ ] `fadeIn: 0` + `preDelay: 0` uses the fast path (no coroutine, no overlay, no delay).
- [ ] The overlay GameObject survives scene transitions (`DontDestroyOnLoad`).
