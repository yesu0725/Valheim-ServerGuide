# CRIT-03 — Display Modes

**File:** `src/Display/GuidanceDisplay.cs`

Seven modes — `raven`, `message`, `chat`, `rune`, `intro`, `conversation`, `bubble` — all using
vanilla Unity/Valheim components. No custom assets.

---

## Mode: `raven`

- Uses `Tutorial.instance.ShowText(id, true)` → Hugin (Raven) popup via `ShowRavenNow()`.
- The entry must be registered in `Tutorial.instance.m_texts` before the raven can spawn it.
  Registration happens in `RegisterTutorials()` (called after config load/sync) and lazily in `EnsureTutorialRegistered()` at show time.
- At `Show()` time, `UpdateTutorialText()` overwrites the registered slot with the live `renderedText`
  so that (a) top-level `message:` fields are honoured and (b) template variables are expanded before display.
- Bypasses the vanilla "Tutorials Enabled" game setting via `RavenSpawnBypassPatch` (see CRIT-11).
- Gated by mod's own `RavenEnabled` BepInEx config (`Display > RavenEnabled`, default `true`).
- If `RavenEnabled = false`, the popup is suppressed with a log message; no error.

### Raven queue & dungeon deferral

- **One at a time.** `_activeRavenKey` tracks the key of the raven currently in `Raven.m_tempTexts`.
  `GuidanceDisplay.Tick()` polls each frame; when the key is no longer present (player interacted /
  raven auto-dismissed), `_activeRavenKey` is cleared and the next entry in `_ravenQueue` is submitted.
  A 1-frame grace delay prevents a false "gone" read on the same frame as the `ShowText` call.
- **FIFO queue.** `Show()` for raven mode: if `_activeRavenKey != null`, enqueue to `_ravenQueue`
  instead of calling `ShowRavenNow()`. Entries drain one-at-a-time as each raven is acknowledged.
- **Dungeon deferral.** `Show()` calls `Player.InInterior()` first. If `true`, the entry goes to
  `_dungeonDeferred` instead. `Tick()` detects the `_wasInInterior → false` transition and drains
  `_dungeonDeferred` into the raven pipeline (respecting the queue if a raven is already active).
- **Session cleanup.** `ZNetDestroyRavenPatch` calls `ClearRavenState()` on `ZNet.OnDestroy` so
  stale entries don't carry over across sessions.
- **vsg_reset integration.** `vsg_reset all` calls `ClearRavenState()`. `vsg_reset <id>` calls
  `ClearRavenQueueForId(id)`, which cancels `_activeRavenKey` if it matches and filters both queues.

**Text source priority:** `message:` (top-level) → `display.text` → `""`.
  The initial registration uses this priority; `Show()` always overwrites with the fully rendered text.

**Config example (using top-level message):**
```yaml
- id: first_axe_hint
  trigger:
    type: item_acquired
    item: AxeStone
  message: "Welcome, {player_name}! Stone is plentiful — craft tools at a workbench."
  display:
    mode: raven
    topic: "Stone Axe"
  once: true
```

**Config example (using display.text):**
```yaml
display:
  mode: raven
  topic: "Raven Tip"
  text: "This is a hint from the mod."
```

---

## Mode: `message`

- Uses `MessageHud.instance.ShowMessage(type, text)`.
- `position` field controls `MessageHud.MessageType`:
  - `"TopLeft"` (default) → `MessageType.TopLeft`
  - `"Center"` → `MessageType.Center`
- No ghost mode, no music, no input lock.

**Config example:**
```yaml
display:
  mode: message
  text: "You picked up a sword!"
  position: Center
```

---

## Mode: `chat`

- Uses `Chat.instance.AddString(colorizedText)`.
- Text is wrapped in Unity rich-text `<color=#{hex}>...</color>` tags using the `ChatColor` BepInEx config (`Display > ChatColor`, default `#E0C078` — a warm gold distinct from white say and yellow shout).
- Setting `ChatColor` to empty string disables coloring.
- Immediately sets `chat.m_hideTimer = 0f` to force the chat panel visible for a full `m_hideDelay` window.
  (Chat.Update counts the timer UP; the panel hides when `m_hideTimer >= m_hideDelay`; resetting to 0 gives maximum visibility.)
- No ghost mode, no music, no input lock.

**Config example:**
```yaml
display:
  mode: chat
  text: "The server welcomes you, {playerName}!"
```

---

## Mode: `rune`

- Uses `TextViewer.instance.ShowText(TextViewer.Style.Rune, topic, text, autoHide: false)`.
- Screen darkens; centered text styled like a vanilla runestone reading.
- Ghost mode is engaged on display, released when `TextViewer.Hide` or `TextViewer.HideIntro` fires.
- No fade transition, no music, no input lock (player can dismiss at will).
- No ESC block.

**Config example:**
```yaml
display:
  mode: rune
  topic: "Ancient Inscription"
  text: "Long ago, the gods carved these words..."
```

---

## Mode: `intro`

- Uses `TextViewer.instance.ShowText(TextViewer.Style.Intro, topic, text, autoHide: true)`.
- Styled like the Valkyrie intro (scrolling text).
- Full cinematic sequence — see **CRIT-07** for complete spec.
- Engages ghost mode (invulnerability + hidden from creatures).
- Plays vanilla intro music (`IntroMusicName` config, default `"intro"`).
- Freezes all player input via `Player.TakeInput` patch.
- Blocks ESC menu via `Menu.Show` patch.
- Custom black overlay canvas fades in before text appears.

**Config example:**
```yaml
display:
  mode: intro
  topic: "The Fallen"
  text: "A great darkness has descended upon the realm..."
```

---

## Mode: `conversation`

- Opened by `NpcConversationPanel.Get().Open(entry, renderedText)`.
- Panel is a dedicated `Canvas` (`ScreenSpaceOverlay`, `sortingOrder = 200`) kept inactive
  between conversations. Activated by `Open()`, deactivated by `Close()`.
- **Position:** anchor Y = 0.25 (mid-point between screen centre and bottom edge), pivot
  `(0.5, 0.5)` — the box occupies the lower-middle band of the screen.
- **Dimensions:** 750 × 185 px, dark fill `(0.02, 0.02, 0.02, 0.97)`.
- **Header:** `TextMeshProUGUI`, bold, gold, from `display.topic` or `entry.title`.
- **Body:** `TextMeshProUGUI` with `enableWordWrapping = true` and `overflowMode = Ellipsis`.
- **Choices:** `HorizontalLayoutGroup` — all buttons on a single row, equal-width flexible.
  If `conversation.choices` is absent, a default "Dismiss" button is inserted automatically.
- Font is resolved lazily from `GuidanceHudTracker.FindVanillaFontStatic()` and assigned
  before any `SetActive(true)` call (TMP Awake rule).
- **Cursor:** freed on `Open()` (`GameCamera.m_mouseCapture = false`, `Cursor.lockState = None`,
  `Cursor.visible = true`), re-asserted every frame in `Update()`, restored on `Close()`.
- **Input lock:** four Harmony patches gated by `NpcConversationPanel.IsOpen`:
  - `Player.TakeInput` → false (movement, attack, interact-E, inventory key)
  - `PlayerController.TakeInput` → false (mouse-look, WASD camera)
  - `Menu.Show` → suppressed (ESC pause menu)
  - `InventoryGui.Show` → suppressed (Tab/I inventory)
- Selecting a choice calls `Close()`, marks fire state on the conversation entry, and (if
  `choice.goto` is set) calls `GuidanceDispatcher.FireById(choice.goto)`.
- No ghost mode, no music.

**Config example:**
```yaml
- id: haldor_greeting
  trigger:
    type: npc_conversation
    npc: Haldor
  display:
    mode: conversation
    topic: "Haldor"
  message: "Well met, traveler! What brings you here?"
  once: false
  conversation:
    choices:
      - text: "Tell me about your wares."
        goto: haldor_wares_entry
      - text: "Nothing, thanks."
```

---

## Mode: `bubble` (Phase 6 — see [CRIT-24](/.claude/criteria/CRIT-24-phase6-system-polish.md))

- World-space floating text above an NPC's head — no panel, no input lock. For ambient/flavor
  lines as the player passes an NPC.
- Rendered by `NpcChatBubble.Show(transform, text, duration)`: a `MonoBehaviour` that creates a
  3D `TextMeshPro` (not UGUI) 2.2m above the anchor, billboards to `Camera.main` each frame,
  fades out over the final second, then self-destroys.
- NPC located by `display.npc_name` (prefab name via `TriggerUtils.NormalizePrefabName`), searching
  **both** `Character.GetAllCharacters()` and `Object.FindObjectsOfType<Trader>()` within 50m —
  Trader NPCs (Haldor/BogWitch/Hildir) have no `Character` component so the Trader list is required.
- While the bubble is active, the NPC's vanilla speech bubble is suppressed (Harmony prefix on
  `Chat.SetNpcText` + immediate `ClearNpcText`); it resumes when the bubble is destroyed.
- No matching nearby NPC → warning logged, no crash; the entry's state/reward side effects still apply.

**Config example:**
```yaml
display:
  mode: bubble
  npc_name: Haldor
  duration: 6        # seconds visible (default 6)
  text: "Wares from across the world..."
```

---

## Ghost Mode (rune + intro)

Engaged via `Player.SetGhostMode(true)`:
- Player becomes invulnerable.
- Creatures can no longer detect or target the player.
- Prior ghost state is preserved and restored on release (if the player was already in ghost mode for another reason, it stays on after release).

Released via `TextViewer.Hide` / `TextViewer.HideIntro` postfix patches.
Released by calling `GuidanceDisplay.ReleaseGhostMode()`.

---

## Criteria

- [ ] `raven` suppressed (not errored) when `RavenEnabled = false`.
- [x] `raven` queues when a raven is already active; shows one at a time until each is acknowledged.
- [x] `raven` defers when player is in a dungeon/interior; drains on interior exit.
- [x] `vsg_reset all` clears the raven queue and dungeon-deferred queue.
- [x] `vsg_reset <id>` removes a specific entry from both queues and cancels it if currently active.
- [ ] `message` respects `position: Center` vs `position: TopLeft`.
- [ ] `chat` text must be visually distinct from white say and yellow shout (gold color by default).
- [ ] `chat` must force the chat panel visible immediately (not rely on the player having the panel open).
- [ ] `rune` engages ghost mode; releases ghost mode when viewer closes.
- [ ] `intro` engages ghost mode + input freeze + ESC block + music; all released when text is dismissed.
- [ ] Ghost mode state before the display is restored exactly (if already ghost, stays ghost after release).
- [ ] Unknown `mode` values log a warning and do nothing; they do not throw.
- [x] `conversation` panel appears in the lower-middle band of the screen (anchor Y = 0.25).
- [x] `conversation` body text word-wraps within the panel width.
- [x] `conversation` choices render as a single horizontal row of equal-width buttons.
- [x] `conversation` frees the OS cursor on open and restores it on close.
- [x] `conversation` blocks all player input (movement, look, interact, inventory, ESC) while open.
- [x] `conversation` fires `GuidanceDispatcher.FireById(goto)` after a choice with a `goto` is clicked.
- [x] `conversation` marks the entry as fired (once / cooldown) on any choice selection.
- [x] `conversation` inserts a default "Dismiss" button when no choices are defined in YAML.
- [x] No custom assets used; all visuals are `Image` color fills and TMP text.
