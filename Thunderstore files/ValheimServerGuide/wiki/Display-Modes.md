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
display:
  mode: raven
  topic: "Stone Axe"
  text: "A crude edge, but it bites. Fell trees, gather wood."
```

**Best for:** First-time lore, progression hints, new-player guidance.

**Notes:**
- Raven popups respect the player's "Dismiss" click — they can close it early.
- `display.topic` becomes the popup's title.
- The mod's raven hints fire even when the player has disabled vanilla tutorials in game options. This is controlled by the `RavenEnabled` option in the BepInEx config (default on), independently of the vanilla "Tutorials Enabled" setting.
- Text tokens (`{playerName}` etc.) are **not** expanded in raven mode because text is baked in at config load. Use `message` or `chat` mode for personalised messages.

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
