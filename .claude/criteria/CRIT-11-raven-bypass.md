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

## Raven TempText Lifecycle and Reset

`Raven.AddTempText(key, …)` (vanilla) **early-returns if `m_key` already exists** in the static `Raven.m_tempTexts` list. Vanilla only removes a temp text in `RemoveSeendTempTexts()`, which fires only when `Player.HaveSeenTutorial(key)` is true.

`vsg_reset` clears `Player.m_shownTutorials` (the vanilla seen-flag) so VSG owns repeat semantics. This permanently disables vanilla's own cleanup — a leftover `RavenText` blocks `AddTempText` forever and the raven never re-shows.

**Fix:** Always call `RemoveVanillaTempText(id)` / `RemoveAllVanillaTempTexts()` before (re-)submitting a raven:

| Call site | What it does |
|---|---|
| `ShowRavenNow(entry, …)` | Evicts any stale temp text for `entry.Id` defensively before calling `Tutorial.ShowText`. Handles the "player walked away without clicking" orphan case too. |
| `ClearRavenQueueForId(id)` | Called by `vsg_reset <id>` — also evicts from `m_tempTexts`. |
| `ClearRavenState()` | Called by `vsg_reset all` / session teardown — evicts all VSG-registered temp texts. |

```csharp
internal static void RemoveVanillaTempText(string id)
    => Raven.m_tempTexts?.RemoveAll(t => t != null &&
           string.Equals(t.m_key, id, StringComparison.OrdinalIgnoreCase));

internal static void RemoveAllVanillaTempTexts()
    => Raven.m_tempTexts?.RemoveAll(t => t != null &&
           RegisteredTutorialNames.Contains(t.m_key));
```

---

## The Raven Tab (vanilla compendium) — `RavenCompendium`

Reading a raven makes vanilla file its text as a page in the compendium's Raven tab:
`Raven.Talk → Player.AddKnownText(label, text)`, stored in the character save
(`Player.m_knownTexts`). `AddKnownText` only **adds** — an existing label keeps its old text —
and nothing ever removes a page. A VSG raven's page therefore froze at the first text read:
editing the entry never reached it and deleting the entry left it forever, while the Guide
Codex (which re-reads the YAML) looked right.

`Display/RavenCompendium.cs` owns that page:

| When | What |
|---|---|
| Read (`Raven.Talk` brackets `AddKnownText`) | VSG raven + `compendium: false` (or entry gone) → nothing filed. Otherwise record `VSG.rc.<ravenKey>` = label + source hash, and overwrite a stale page. Vanilla ravens untouched. |
| Config load / progress ready / spawn | Rewrite pages whose source (topic + text) hash changed, re-file pages whose label changed, remove pages whose entry was deleted, left raven mode, or opted out. |
| `raven_compendium_remove:` | Delete the named labels — the way out for pages written before records existed. |

Rules that are easy to break:

- **Ownership comes from the record, never from the page.** Vanilla strips `\u0016` from labels
  on save, so no marker survives in the page itself. A page with no record is touched only when
  (a) a raven in the current config files under that label **and** the player has been shown
  that raven (`m_shownTutorials`) — "adoption" of pre-0.18.0 pages — or (b) the operator names it.
- **Records live in the server-side progress file, not the character.** A character that visits a
  second VSG server must not lose the first server's pages because the second server's config
  does not contain them.
- **Never reconcile against a config that is not this server's.** Until a client receives the
  server's config, `Plugin.CurrentConfig` is the main-menu local YAML; reconciling then would
  delete every page the server wrote. `ConfigTrusted` = host/single-player, or a config received
  in the current `ZNet` session.
- **An unchanged source keeps its page text.** A page rendered at fire time can hold event tokens
  (`{creatureName}`) that `TemplateLocal` cannot fill; only a changed source is re-rendered.
- Two entries may share a topic: a label still claimed by a live entry is never deleted.

## Criteria

- [x] When vanilla "Tutorials Enabled" is OFF, our raven hints still show if `RavenEnabled = true`.
- [x] When vanilla "Tutorials Enabled" is ON, vanilla raven hints are unaffected by our bypass.
- [x] `Raven.m_tutorialsEnabled` is restored to its original value immediately after `Spawn()` returns.
- [x] The `RavenSpawnBypassPatch` only activates for entry IDs in `RegisteredTutorialNames`.
- [x] `RavenEnabled = false` suppresses all our raven hints without touching vanilla hints.
- [x] Tutorial entries are never added to `m_texts` more than once (deduped via `RegisteredTutorialNames`).
- [x] Harmony patch parameter `text` must match the actual method signature (not `raventext`).
- [x] Stale `RavenText` entries are evicted from `Raven.m_tempTexts` before re-show and on all reset paths so a reset entry can always re-fire.
- [ ] Editing a raven entry's text updates its Raven-tab page on the next login / config reload.
- [ ] Changing a raven entry's topic moves its page to the new label (old label gone).
- [ ] Deleting a raven entry removes its page; vanilla and other-mod pages are untouched.
- [ ] `display.compendium: false` files no page on read and removes an existing one.
- [ ] Re-reading a raven replaces its page's text instead of keeping the first version.
- [ ] Pages written before 0.18.0 are adopted and updated when their entry still exists.
- [ ] `raven_compendium_remove:` deletes the listed labels.
- [ ] A client never reconciles before it has received the server's config.
