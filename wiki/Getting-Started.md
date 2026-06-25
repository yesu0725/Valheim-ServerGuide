# Getting Started

## Requirements

- Valheim (Steam)
- [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.x
- [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) (any recent version)

ValheimServerGuide depends on Jötunn for RPC syncing and on BepInEx for the plugin framework. Both are pulled in automatically if you install via r2modman.

---

## Installation

### Via r2modman (recommended)

1. Open r2modman and select your Valheim profile.
2. Search for **ValheimServerGuide** in the online packages list.
3. Install it. r2modman handles BepInEx and Jötunn automatically.
4. Install the same mod on your **dedicated server** (or on the host machine if you use Host & Play).

### Manual install

1. Drop `ValheimServerGuide.dll` into `BepInEx/plugins/ValheimServerGuide/`.
2. Make sure BepInEx and Jötunn are already installed.

---

## Who installs what?

| Where | Install? | Why |
|---|---|---|
| Dedicated server | **Yes** | The server reads the YAML and pushes it to clients. |
| Host (Host & Play) | **Yes** | The host acts as the server. |
| Every player | **Yes** | Without the mod, players don't receive any guidance popups. |

Players who don't have the mod installed simply won't see any guidance messages. The server and other players are unaffected.

---

## First Launch

When the server (or host) starts for the first time with the mod installed, it creates:

```
BepInEx/config/ValheimServerGuide/guidance.yaml
```

This is the file you edit to write your guidance content. It comes pre-populated with a commented example covering all trigger types.

The mod watches this file for changes. Save the file and every connected client receives the updated config within seconds — **no server restart required**.

---

## Your First Entry

Open `guidance.yaml` and add (or replace the example with):

```yaml
guidances:
  - id: welcome_message
    trigger:
      type: first_login
    display:
      mode: raven
      topic: "Welcome!"
      text: "Welcome to the server! Check the Codex (F3) for guides."
    once: true
```

Save the file. The next player to log in for the first time will see Hugin deliver the message.

---

## The Codex

Players can open the in-game guide browser at any time by pressing **F3** (default). It shows all guidance entries grouped by category, with their title, description, and completion status. Press **F3** again (or Escape) to close it.

---

## The Progress Panel

The progress panel (top-right by default) shows the quests a player has **pinned**, with a `current/goal` count for counter steps. It is **hidden by default** — players pin an in-progress quest from the Codex (F3 → **Show on Tracker**), which unhides the panel. Press **F10** (default) to show/hide it. It doesn't lock movement or show the cursor, and players can drag it anywhere while the inventory or ESC menu is open. See [HUD Tracker & Codex](HUD-Tracker-and-Codex) for details.

---

## BepInEx Config

The mod's BepInEx config file is at:

```
BepInEx/config/com.valheimserverguide.cfg
```

This file lets players (and the server admin) adjust display settings, hotkeys, and Discord integration. See the [YAML Configuration](YAML-Configuration) wiki page for fields that can also be set in `guidance.yaml`.

---

## Next Steps

- [YAML Configuration](YAML-Configuration) — Learn the full schema.
- [Trigger Types](Trigger-Types) — See all the ways you can trigger guidance.
- [Display Modes](Display-Modes) — Choose how messages appear.
