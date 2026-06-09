# Firing Semantics

Firing semantics control **when** a guidance entry is allowed to fire. Every time a trigger event occurs, the dispatcher checks these rules before showing anything to the player.

---

## `once`

The most common control. When `once: true` (the default), the entry fires exactly once and is then permanently suppressed for that character.

```yaml
- id: first_bronze_sword
  trigger: { type: craft, item: SwordBronze }
  display:
    mode: raven
    topic: "Bronze Edge"
    text: "Forged in fire, this blade cleaves the Black Forest's beasts."
  once: true      # default — this line is optional
```

For **global scope** entries, `once: true` means once per world (the first player to trigger it fires it for everyone, and it never fires again for that world).

Set `once: false` to allow the entry to fire repeatedly (combined with `cooldown` to throttle it).

---

## `cooldown`

Instead of firing once and stopping, the entry can fire repeatedly on a throttle.

```yaml
- id: swamp_tip
  trigger: { type: biome, biome: Swamp }
  display:
    mode: message
    text: "Bring an antidote potion into the swamp."
  once: false
  cooldown: 900     # 15 minutes between fires, per character
```

`cooldown` is measured in seconds. After the entry fires, it is suppressed for that many seconds. When the cooldown expires, the entry is eligible again.

`cooldown` and `once: true` are mutually exclusive — if `once: true`, the entry never fires a second time regardless of cooldown.

---

## `requires`

An entry with a `requires` list only fires after all listed entry IDs have already fired for this character.

```yaml
- id: swamp_crypt_tip
  trigger: { type: biome, biome: Swamp }
  requires: [first_bronze_sword]      # only show once the player has made a bronze sword
  display:
    mode: raven
    topic: "Sunken Crypts"
    text: "Seek iron in the bog. Bring a key."
```

This allows you to sequence guidance — don't tell a player about swamp crypts until they've progressed enough to be ready for them.

**Important:** If any ID in `requires` does not exist in the YAML, the entry is **permanently blocked** (treated as an unsatisfied dependency). Double-check that all referenced IDs are valid.

Multiple requirements all must be satisfied:

```yaml
requires: [entered_swamp, has_bronze_gear]
```

---

## `stop_when`

An entry stops firing once **any** ID in the `stop_when` list has fired. This is useful for "show a recurring reminder *until* the player has done X".

```yaml
- id: troll_armor_reminder
  trigger: { type: kill, creature: Troll }
  display:
    mode: message
    text: "Troll hide makes fine armor. Craft it at a workbench."
  once: false
  cooldown: 600
  stop_when: [crafted_troll_armor]    # stop nagging once they've made the set
```

`stop_when` is checked **before** `cooldown`, so once any stop condition fires, the entry is suppressed immediately regardless of whether the cooldown has reset.

---

## `requires` + `stop_when` Together

Combine both to create narrow windows:

```yaml
- id: plains_entry_warning
  trigger: { type: biome, biome: Plains }
  requires: [reached_iron_age]         # don't warn until they've progressed
  stop_when: [defeated_yagluth]        # stop once they've beaten the plains boss
  cooldown: 1800
  display:
    mode: message
    text: "The plains are deadly. Bring padded armor and a good shield."
```

---

## `max_fires`

Cap how many times an entry fires per character without using `once: true`.

```yaml
- id: companions_combat_tip
  trigger:
    type: player_death
    max_fires: 2          # show at most twice, then stop
  display:
    mode: raven
    topic: "Companions"
  message: "Dying alone is rough. Hire a companion from Haldor..."
```

Unlike `once: true`, the entry is NOT added to the "Fired" list in `vsg_list`. Instead, `vsg_list` shows it as `[fired 1/2]` so you can see the counter.

After the cap is reached, `vsg_reset <id>` or `vsg_reset all` will clear the counter so the entry can fire again. If the counter is not cleared, the entry stays permanently blocked — a full reset always handles this.

---

## `version`

If you update the text of an entry that players have already seen, bump `version` to re-deliver it.

```yaml
- id: forge_tip
  version: 2           # players who saw version 1 will see this again
  trigger: { type: craft, item: Forge }
  display:
    mode: raven
    topic: "The Forge"
    text: "Updated guidance: the forge also lets you upgrade existing items."
  once: true
```

The version number is stored alongside the fired state. Raising it resets the "already fired" check for that entry.

---

## Evaluation Order

When a trigger event occurs, the dispatcher checks in this order:

1. **Scope** — Is this a global entry? If so, has the world already marked it fired?
2. **`requires`** — Have all prerequisite IDs fired for this character?
3. **`stop_when`** — Has any stop condition fired?
4. **`once`** — Has this entry already fired for this character (if `once: true`)?
5. **`cooldown`** — Is the entry still on cooldown?
6. **`max_fires`** — Has the per-character fire count reached the cap?
7. **Fire** — All checks passed; show the display.

The entry fires if and only if all checks pass.

---

## Fire Modes Reference

| Setting | Behaviour | Stored in |
|---|---|---|
| `once: true` (default) | Fire once, ever, per character (or per world for global scope). | `VSG.fired` |
| `once: false` (no cooldown) | Fire every time the trigger condition is met. | — |
| `once: false` + `cooldown: N` | Fire at most once every N seconds. | in-memory |
| `trigger.max_fires: N` | Fire at most N times total per character. | `VSG.fc.<id>` |
| `stop_when: [id]` | Stop firing permanently once `id` has fired. | — |
| `requires: [id]` | Don't fire at all until `id` has fired. | — |
