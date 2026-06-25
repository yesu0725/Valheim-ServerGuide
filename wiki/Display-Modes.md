# Display Modes

The `display.mode` field controls how the guidance message appears to the player. All modes use vanilla Valheim UI — no custom assets or textures.

```yaml
display:
  mode: raven
  topic: "My Topic"
  text: "My message text."
```

---

## `raven`

Shows Hugin (the raven) flying in with a popup message — the same style used for vanilla tutorial hints.

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

**Best for:** First-time lore, progression hints, new-player guidance.

**Notes:**
- Raven popups respect the player's "Dismiss" click — they can close it early.
- `display.topic` becomes the popup's title.
- The mod's raven hints fire even when the player has disabled vanilla tutorials in game options. This is controlled by the `RavenEnabled` option in the BepInEx config (default on), independently of the vanilla "Tutorials Enabled" setting.
- **Text source:** Write the message in the top-level `message:` field (same as all other modes). Alternatively use `display.text`. `message:` takes priority if both are set.
- **Text tokens are supported** (`{player_name}`, `{biome}`, etc.). The text is updated with the rendered value each time the entry fires.
- **One at a time — automatically queued.** If multiple raven entries fire before the player has interacted with the current one, they are held in a FIFO queue. Each raven waits for the previous one to be acknowledged (or auto-dismissed by the raven) before appearing. No message is lost.
- **Dungeon deferral.** If a raven entry fires while the player is inside a dungeon or interior, it is held in a separate deferred queue. As soon as the player exits the interior, the deferred entries drain into the normal raven queue and show in order. The player will never miss a raven message because they were underground when it triggered.
- **Correctly re-shows after `vsg_reset`.** The vanilla raven system stores pending texts in a static list (`Raven.m_tempTexts`) and only evicts a text when `Player.HaveSeenTutorial` is true. Because `vsg_reset` clears that seen-flag so VSG owns repeat logic, we explicitly remove stale entries from that list before every re-show and on every reset path. This prevents the raven being silently blocked by a leftover text from a previous fire.

---

## `message`

Shows a text notification via Valheim's `MessageHud` — the same system used for item pickups and server messages.

```yaml
display:
  mode: message
  position: TopLeft     # TopLeft | Center
  text: "Bring an antidote potion into the swamp."
```

**Best for:** Quick recurring tips, action feedback, reminders.

**Notes:**
- `position: TopLeft` is the small notification in the corner (like item pickup messages).
- `position: Center` is the large centered toast (like "You are well rested").
- Text tokens are supported and expanded live.
- `display.topic` is not used in message mode.

---

## `chat`

Adds a line to the chat panel, formatted with a distinct color so it reads apart from player chat.

```yaml
display:
  mode: chat
  text: "[Guide] The Black Forest is dangerous at night."
```

**Best for:** Verbose lore, tips that players may want to scroll back and re-read, server announcements.

**Notes:**
- The chat panel auto-opens when the message is added.
- Chat message color defaults to `#E0C078` (warm gold). Change it in the BepInEx config (`ChatColor`).
- `display.topic` is not used in chat mode.
- Text tokens are supported.

---

## `rune`

Shows a full-screen dark overlay with centered text in the Valheim rune-tablet style.

```yaml
display:
  mode: rune
  topic: "Of Crows and Kings"
  text: "Long before the gods carved the realms, Hugin watched from the World Tree..."
```

**Best for:** Lore reveals, milestone text, dramatic in-world reading moments.

**Notes:**
- Screen darkens while the text is displayed.
- The player is placed in ghost mode (untargetable, like the vanilla `ghost` admin cheat) for the duration, so nothing can attack them mid-read.
- Ghost mode is restored when the viewer closes (click-through, ESC, or auto-hide).
- `display.topic` is used as the heading/title above the text body.
- Text tokens are supported.

---

## `intro`

Shows scrolling text over a fading screen in the style of Valheim's Valkyrie intro — the most dramatic option.

```yaml
display:
  mode: intro
  topic: "The Burning Shore"
  text: "Smoke rises where the world cracks. Tread carefully, traveler."
```

**Best for:** Story beats, world-event reveals, first-time biome entry cinematics.

**Notes:**
- The screen fades to black (configurable fade duration, default 3 seconds) before text appears.
- Music is pinned to the vanilla Valkyrie intro track for the display duration (configurable in BepInEx config via `IntroMusicName`). Music returns to normal after the duration elapses.
- The player is in ghost mode for the duration — nothing can target or damage them.
- `display.topic` is used as the heading above the text.
- Text tokens are supported.
- Fade and music duration are controlled by `IntroFadeInDuration`, `IntroMusicDuration`, and `IntroPreDelay` in the BepInEx config.

---

## `conversation`

Opens a dialogue panel near the bottom of the screen with a title, body text, and clickable choice buttons.

```yaml
display:
  mode: conversation
  topic: "Haldor"
  text: "Well met, traveler! I have rare goods from distant lands."
```

**Best for:** NPC dialogue trees, quest acceptance/handoff, choice-driven interactions.

**Notes:**
- Always paired with `trigger.type: npc_conversation`.
- Requires a `conversation:` block in the entry to define the choice buttons.
- While the panel is open, player movement, attacks, inventory, camera, and the pause menu are all disabled.
- If no choices are defined, a default "Dismiss" button is shown.
- See [NPC Conversations](NPC-Conversations) for full setup.

---

## `bubble`

Floats text in world-space above an NPC's head — like a speech bubble — without opening any panel or interrupting the player. Good for ambient flavour as the player walks past an NPC.

```yaml
display:
  mode: bubble
  npc_name: Haldor        # prefab name of the NPC to float the text above
  duration: 6             # seconds visible before fading (default 6)
  text: "Mind the trolls out east, friend."
```

**Best for:** ambient one-liners, reactions, non-blocking flavour text.

**Notes:**
- `npc_name` is matched against nearby NPCs — both creatures (`Character`) and trader NPCs (Haldor, Hildir, BogWitch), so it works for traders even though they aren't `Character`s.
- The bubble does not lock input — the player keeps moving and looking normally.
- Vanilla auto-generated trader bubbles are suppressed while a VSG bubble is showing so they don't overlap.

---

## Server Display Mode Assignment Rules

When authoring guides for a server modpack, every trigger type should map to a fixed
display mode. The rules below reflect the Hearthbound modpack convention and are
documented fully in `wiki/Guide-Authoring-Reference.md`.

### Rune triggers (action events — player actively did something)

Use `rune` for triggers that fire in response to a deliberate player action. The
full-screen presentation + ghost mode ensures the player stops and reads the text.

| Trigger type | Why rune |
|---|---|
| `craft` | Player just crafted something — brief pause is natural |
| `item_acquired` | Major pickup moment; lore/tip reinforces the discovery |
| `kill` | Kill confirmation; ghost mode prevents kill-stealing mid-read |
| `build` | Piece just placed; player is in build mode anyway |
| `chest_opened` | First container interaction — tutorial moment |
| `skill_level` | Level-up milestone — deserves drama |
| `timed` | Follow-up tips in a chain — must be dismissable so nothing goes unread |
| `boss_defeated` | Boss victory moment — most dramatic possible timing |

### Raven triggers (environmental / existential events)

Use `raven` for triggers that fire because of something *that happened to* the player
or because of a world state transition, not a deliberate action.

| Trigger type | Why raven |
|---|---|
| `first_login` | Welcome message; ambient delivery suits a new arrival |
| `player_death` | Post-death tip; raven appearing after respawn feels thematically correct |
| `biome` | Entering a biome for the first time — discovery moment |
| `distance` | Proximity alert — ambient warning, not an action |
| `discover_location` *(planned)* | Map discovery — world speaks to the player |

### Message / conversation triggers (NPC-based or minor contextual tips)

Use `message` or `conversation` for triggers tied to NPC interactions or minor tips
that should not interrupt gameplay.

| Trigger type | Recommended mode |
|---|---|
| `npc_interacted` | `message` (Center or TopLeft) |
| `npc_conversation` | `conversation` (holds E, choice panel) |
| `npc_item_submit` | `rune` (submission is a deliberate dramatic act) |
| `location_entered` | `message` (brief, non-blocking) |
| `equip` | `message` (fires on equip + load; rune would be too disruptive) |
| `entry_finished` | inherits from the entry being chained to |

### Quick reference table

| Trigger | Mode |
|---|---|
| craft, item_acquired, kill, build, chest_opened, skill_level, timed, boss_defeated | `rune` |
| first_login, player_death, biome, distance | `raven` |
| npc_interacted, equip, location_entered | `message` |
| npc_conversation | `conversation` |
| npc_item_submit | `rune` |

---

## Display Config (BepInEx)

These settings in `com.valheimserverguide.cfg` affect display behaviour:

| Config Key | Default | Description |
|---|---|---|
| `RavenEnabled` | `true` | Enable raven mode popups (independent of vanilla Tutorials setting) |
| `IntroMusicName` | `intro` | Music clip to play during intro mode |
| `IntroMusicDuration` | `60` | Seconds intro music stays pinned |
| `IntroFadeInDuration` | `3.0` | Screen fade duration before intro text appears |
| `IntroPreDelay` | `1.0` | Pause on black screen after fade, before text appears |
| `ChatColor` | `#E0C078` | Hex color for chat-mode guidance messages |
