# Phase 04a — Tracker Foundation Fixes

**Status:** `complete`
**Depends on:** Phase 04 (base tracker implementation)
**Blocks:** 04b, 04c (all further tracker features need a stable base)

Resolves two known bugs in the Phase 04 baseline before any new features are layered on:
1. `LiberationSans SDF Font Asset was not found` warnings logged every time a tracker text
   object is created.
2. Tracker is invisible on login when the player already has in-progress chains from a
   previous session.

---

## Bug 1 — Font Warning (LiberationSans SDF)

### Root Cause
`BuildPanel()` calls `FindVanillaFont()` inside `Hud.Awake`. At that point the game has not
yet loaded TMP font assets into memory, so `Resources.FindObjectsOfTypeAll<TMP_FontAsset>()`
returns an empty set. `_font` is left null. When `TextMeshProUGUI` is added to a GameObject
with a null font, TMP tries to fall back to its built-in `LiberationSans SDF`, which does not
exist in Valheim's bundle — generating a warning for every text row created.

### Fix — Lazy Font Assignment on First `Refresh()`
- `BuildPanel()` creates text objects with `_font = null` (no assignment, no warning).
- `Refresh()` checks `_font == null` at the top of every call. If it is still null, it calls
  `FindVanillaFont()`. By the time `Refresh()` is first called from a trigger or from the
  `Player.OnSpawned` patch, TMP fonts have already been loaded.
- Once a non-null font is found, it is assigned retroactively to all existing TMP text
  components (`_headerText`, each `_rowTexts[i]`, `_overflowText`).
- Subsequent `Refresh()` calls skip the font lookup entirely (`_font != null` guard).

### Code Sketch

```csharp
public void Refresh()
{
    if (_panel == null) return;

    // Lazy font resolution — fonts are not loaded at Hud.Awake time.
    if (_font == null)
    {
        _font = FindVanillaFont();
        if (_font != null) ApplyFontToAll(_font);
    }
    // ... rest of existing Refresh logic
}

private void ApplyFontToAll(TMP_FontAsset font)
{
    if (_headerText != null) _headerText.font = font;
    foreach (var t in _rowTexts) if (t != null) t.font = font;
    if (_overflowText != null) _overflowText.font = font;
}
```

---

## Bug 2 — Tracker Invisible on Login with Existing Chains

### Root Cause
`HudAwakePatch.Postfix` calls `Refresh()` before `Player.m_localPlayer` is set. The guard
`if (player == null) { _panel.SetActive(false); return; }` fires and hides the panel. After
the player object loads with in-progress chain state in `m_customData`, nothing calls
`Refresh()` again — so the tracker never appears until the player actually makes progress.

In dedicated-server / single-player, `GuidanceSync.OnChainStatePush` would normally re-call
`Refresh()`, but the server clears its `_playerChainData` cache on `ZNet.OnDestroy` and never
re-pushes restored data on the same session start.

### Fix — Patch `Player.OnSpawned`
Add a Harmony `Postfix` on `Player.OnSpawned`. When `__instance == Player.m_localPlayer`,
call `GuidanceHudTracker.Instance?.Refresh()`. This fires after the player object is fully
initialised and `m_customData` is populated from the save.

```csharp
[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
internal static class PlayerOnSpawnedTrackerPatch
{
    private static void Postfix(Player __instance)
    {
        if (__instance != Player.m_localPlayer) return;
        GuidanceHudTracker.Instance?.Refresh();
    }
}
```

This patch lives inside `GuidanceHudTracker.cs` alongside `HudAwakePatch`.

---

## Bug 3 — Font Warning Still Fires at Build Time (found during testing)

### Root Cause
The initial lazy-font plan deferred *assignment* but still created text children while the panel
was active. Each `TextMeshProUGUI.OnEnable` immediately attempts a mesh render — triggering the
`LiberationSans SDF` warning even though `_font` was null.

### Additional Fix
`_panel.SetActive(false)` moved to **before** any child text objects are created in `BuildPanel()`.
While the panel is inactive, `OnEnable` on child components never runs, so TMP never attempts
a mesh render against the null font. A defensive early-return in `Refresh()` also keeps the panel
hidden until a font resolves.

---

## Bug 4 — Reset Chain Shows as "Started" After Login (found during testing)

### Root Cause
`ChainState.GetStep()` returns `0` both for a brand-new/reset chain (step 0 not yet fired) and
for a chain genuinely working on step 0. The new `PlayerOnSpawnedTrackerPatch` now calls
`Refresh()` on every login, which caused reset chains sitting at step 0 to appear in the tracker
as if they were in progress.

### Fix
Added a guard in the row-building loop in `Refresh()`: when `stepIdx == 0`, the chain is only
shown if it is a counter step **and** its counter has been activated (`GetCounter >= 0`). A
non-counter step at index 0 is hidden — it has not fired yet.

---

## Files Changed

| File | Change |
|---|---|
| `src/Display/GuidanceHudTracker.cs` | `_panel.SetActive(false)` before text-child creation; lazy font in `Refresh()` with early-return guard; `ApplyFontToAll()`; `PlayerOnSpawnedTrackerPatch`; step-0 "has started" guard in row-building loop |

---

## Criteria

- [x] No `LiberationSans SDF Font Asset was not found` warning appears in LogOutput.log after
      loading into a world.
- [x] After font resolves, all tracker text rows use `Valheim-AveriaSansLibre` (confirmed via
      log or visual inspection).
- [x] Tracker displays in-progress chains immediately on login without requiring any player
      action.
- [x] Tracker still hides correctly when there are no active chains.
- [x] Lazy font lookup does not run on every `Refresh()` call — only until resolved.
- [x] No regression to existing tracker visibility, layout, or YAML hot-reload behaviour.
