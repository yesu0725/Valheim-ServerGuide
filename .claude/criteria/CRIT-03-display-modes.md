# CRIT-03 — Display Modes

**File:** `src/Display/GuidanceDisplay.cs`

**Every mode renders authored text in full — no display surface truncates. See CRIT-25 for the
layout rules and for the `highlight:` system, which colours chosen words in any of these modes.**

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

- Rendered by the custom **`RunePanel`** (`src/Display/RunePanel.cs`) — a Valheim-themed
  reading card on a dedicated `ScreenSpaceOverlay` canvas (`sortingOrder = 210`), not the
  vanilla `TextViewer`. Uses the game font (resolved the same way as the Codex / Conversation
  panels) plus `Image` color fills. No custom assets (CRIT-14).
- **Layout:** a full-screen darkening backdrop behind a centered card. The card is a
  `VerticalLayoutGroup` + `ContentSizeFitter` that stacks **header → divider → body → list →
  footer**; its height grows to fit the content, its width is fixed (`rune.width`, default 620).
- **Default content (no `rune:` block):** header = `display.topic`, body = `message:` / `display.text`,
  styled with themed defaults (gold header, parchment body on dark stone). The header + divider are
  hidden when there is no topic; the body is hidden when there is no text; the list is hidden when empty.
- Screen darkens; ghost mode is engaged on open (invulnerable + undetected) and released after the
  panel finishes closing. If an intro cinematic takes over, the intro owns the ghost-mode release.
- **Fade in/out:** a `CanvasGroup` on the panel's root fades the whole card (backdrop + panel) in on
  open and out on close, via `RuneStyleSpec.FadeIn` / `FadeOut` (seconds, default `0.35` each; `0` =
  instant). Re-triggering `Open()` while a fade-out is still running crossfades smoothly from the
  current alpha rather than flashing to black first.
- No music, no input lock (player can move; the reading is dismissed at will).
- **Dismissal:** press **Use (E)** / **Escape**, or click the backdrop (a 0.3 s grace prevents an
  already-held key from skipping instantly) — dismissal via `Close()` fades out. An intro cinematic
  starting, or session teardown (`ZNet.OnDestroy`), closes the panel instantly via `CloseImmediate()`
  (no fade), since a lingering fade coroutine would otherwise carry stale state across the transition.

### Customization — `display.rune` (`RuneStyleSpec`)

All fields optional; unset → themed defaults. Colors are hex (`#RRGGBB` / `#RRGGBBAA`, leading `#`
optional). Font styles accept any combination of `Normal | Bold | Italic | Underline | Uppercase |
Strikethrough` (e.g. `"Bold Italic"`). Alignment is `Left | Center | Right`. All text fields support
`{player_name}` and the other template variables.

| Field | Default | Purpose |
|---|---|---|
| `header` | `display.topic` | Header text override |
| `header_color` | gold | Header color |
| `header_font_size` | `26` | Header size |
| `header_style` | `Bold` | Header font style |
| `header_alignment` | `Center` | Header alignment |
| `body_color` | parchment | Body color |
| `body_font_size` | `17` | Body size |
| `body_style` | `Normal` | Body font style |
| `body_alignment` | `Left` | Body alignment |
| `items` | — | Bullet-list rows (each styled + templated) |
| `bullet` | `•` | Row glyph (`""` = none) |
| `item_color` | warm | List row color |
| `item_font_size` | `16` | List row size |
| `item_style` | `Normal` | List row font style |
| `background_color` | dark stone | Panel fill |
| `accent_color` | bronze | Header/body divider rule |
| `width` | `620` | Panel width (px, clamped 240–1200) |
| `fade_in` | `0.35` | Fade-in duration, seconds (`0` = instant) |
| `fade_out` | `0.35` | Fade-out duration, seconds (`0` = instant) |

**Config example (default themed look):**
```yaml
display:
  mode: rune
  topic: "Ancient Inscription"
  text: "Long ago, the gods carved these words..."
```

**Config example (fully customized with a list):**
```yaml
display:
  mode: rune
  topic: "The Elder's Charge"
  text: "Heed these trials, {player_name}, and Valhalla awaits."
  rune:
    header_color: "#E6B34A"
    header_font_size: 30
    header_style: "Bold Uppercase"
    body_color: "#D8D2C2"
    body_style: "Italic"
    accent_color: "#8A6A2A"
    background_color: "#0F0C08F0"
    width: 680
    bullet: "◆"
    item_color: "#C8B87A"
    items:
      - "Slay the Elder in the Black Forest."
      - "Recover the swamp key from Bonemass."
      - "Return to the sacred stones."
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
- **Position:** anchored at `(0.5, 0)` with pivot `(0.5, 0)`, 110 px above the bottom edge —
  the panel grows UPWARD from a fixed baseline as the text gets longer, so it can never slide
  off the bottom of the screen.
- **Dimensions:** 750 px wide; height is driven entirely by the content via
  `VerticalLayoutGroup` + `ContentSizeFitter`. Dark fill `(0.02, 0.02, 0.02, 0.97)`.
- **Header:** `TextMeshProUGUI`, bold, gold, from `display.topic` or `entry.title`. Wraps.
- **Body:** `TextMeshProUGUI` inside a `ScrollRect`, `enableWordWrapping = true` and
  `overflowMode = Overflow`. **Never `Ellipsis`** — the viewport's `preferredHeight` matches the
  text exactly until the panel would exceed 82% of screen height, and only then clamps and lets
  the body scroll. Text is never cut off. See CRIT-25.
- **Choices:** a `VerticalLayoutGroup` of rows, each a `HorizontalLayoutGroup`. Up to 3 buttons
  share a row; past 3 choices the row capacity drops to 2 so labels stay readable. Each button
  grows to fit a label that wrapped onto extra lines. If `conversation.choices` is absent, a
  default "Dismiss" button is inserted automatically.
- Font is resolved lazily from `GuidanceHudTracker.FindVanillaFontStatic()` and assigned
  before any `SetActive(true)` call (TMP Awake rule). The panel is then activated **before**
  the body is measured — TMP returns zero preferred sizes for an inactive hierarchy.
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
- [ ] `rune` engages ghost mode; releases ghost mode when the panel finishes closing.
- [ ] `rune` renders header (topic) + body (message/text) with themed defaults when no `rune:` block is present.
- [ ] `rune` honors `display.rune` overrides: header/body/item colors, font sizes, styles, alignment, bullet list, width, background.
- [ ] `rune` is dismissed by Use (E) / Escape / backdrop click, fading out over `fade_out` seconds; auto-closes instantly (no fade) on intro start and session teardown.
- [ ] `rune` fades in over `fade_in` seconds on open; re-opening mid fade-out crossfades from the current alpha instead of flashing.
- [ ] `intro` engages ghost mode + input freeze + ESC block + music; all released when text is dismissed.
- [ ] Ghost mode state before the display is restored exactly (if already ghost, stays ghost after release).
- [ ] Unknown `mode` values log a warning and do nothing; they do not throw.
- [x] `conversation` panel sits above the bottom edge and grows upward as its text gets longer.
- [x] `conversation` body text word-wraps within the panel width and is never truncated —
      it scrolls once the panel would exceed 82% of screen height (CRIT-25).
- [x] `conversation` choices wrap onto multiple rows and grow to fit wrapped labels.
- [x] `conversation` frees the OS cursor on open and restores it on close.
- [x] `conversation` blocks all player input (movement, look, interact, inventory, ESC) while open.
- [x] `conversation` fires `GuidanceDispatcher.FireById(goto)` after a choice with a `goto` is clicked.
- [x] `conversation` marks the entry as fired (once / cooldown) on any choice selection.
- [x] `conversation` inserts a default "Dismiss" button when no choices are defined in YAML.
- [x] No custom assets used; all visuals are `Image` color fills and TMP text.
