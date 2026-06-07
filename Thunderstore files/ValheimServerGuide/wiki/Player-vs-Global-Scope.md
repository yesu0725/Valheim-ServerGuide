# Player vs Global Scope

Guidance entries have a `scope` field that controls whose fire state is tracked and who sees the display.

---

## `scope: player` (default)

Each player has their own independent fire state. If Player A triggers the entry, Player B's state is unaffected — they can still see the same entry when they trigger the same condition.

```yaml
- id: first_bronze_sword
  scope: player           # default — this line is optional
  trigger: { type: craft, item: SwordBronze }
  display:
    mode: raven
    topic: "Bronze Edge"
    text: "Forged in fire, this blade cleaves the Black Forest's beasts."
  once: true
```

Player A crafts a bronze sword → sees the raven popup, entry marked fired for Player A.
Player B crafts their first bronze sword later → also sees the popup, independently.

**State storage:** The fired ID is stored in `Player.m_customData["VSG.fired"]` — it rides with the character save. If the player creates a new character, their state is clean.

---

## `scope: global`

The entry fires world-wide. The **first** player to trigger it sends the event to the server. The server:

1. Marks a world-level flag (`VSG.<id>` in `ZoneSystem` global keys, persisted in the world save).
2. Broadcasts a "play now" RPC to every currently connected player, so they all see the display simultaneously.

```yaml
- id: world_eikthyr_fell
  scope: global
  trigger: { type: kill, creature: Eikthyr }
  display:
    mode: intro
    topic: "The Stag-King Falls"
    text: "Eikthyr is slain. The realm trembles. Your trial begins, traveler."
  once: true
  announce:
    discord: "⚔️ **{playerName}** has slain **Eikthyr**!"
```

When any player kills Eikthyr:
- Every player online sees the `intro` cinematic.
- The entry is permanently marked as fired for the entire world.
- No other player can trigger it again.
- Players who log in later will see that the entry has already fired (they won't see the cinematic retroactively, but any `requires` that depend on it will resolve correctly).

**State storage:** Stored in `ZoneSystem` global keys (e.g. `VSG.world_eikthyr_fell`). These persist in the world save file and auto-replicate to connecting clients.

---

## Choosing the Right Scope

| Use case | Scope |
|---|---|
| First-time craft/pickup guidance | `player` |
| Personal progression tips | `player` |
| "Tell all players when a boss dies" | `global` |
| Server-wide story beats | `global` |
| "Each player's first biome entry" | `player` |
| "World-first achievement" | `global` |

---

## Global Events and Discord

`scope: global` entries pair naturally with `announce.discord` — you usually want to tell Discord who triggered the world event:

```yaml
- id: world_moder_fell
  scope: global
  trigger: { type: kill, creature: Dragon }
  display:
    mode: intro
    topic: "Moder Falls"
    text: "The winds of the Mountain still. A new power stirs."
  once: true
  announce:
    discord: "🐉 **{playerName}** has slain **Moder**! The Mountain yields."
```

---

## `requires` with Global Scope

`requires` always evaluates against the **local player's** history, regardless of scope. This means:

```yaml
- id: tell_player_about_swamp
  scope: player
  requires: [world_eikthyr_fell]   # global entry
  trigger: { type: first_login }
  display:
    mode: raven
    text: "With Eikthyr dead, the path to the Swamp opens."
```

This fires for each player on their next login **after** the world event has occurred. The `requires` check resolves because `world_eikthyr_fell` is stored in the world's global keys — all players see it as fired once it's been triggered by anyone.

---

## Admin Resets

`vsg_reset <id>` auto-detects scope:

- **Player-scope ID:** Clears from the local player's `m_customData`. Only affects the admin running the command.
- **Global-scope ID:** Requires the command to be run on the server or host. Removes the `VSG.<id>` global key from `ZoneSystem` — the entry can fire again for the whole world.

See [Admin Commands](Admin-Commands) for details.
