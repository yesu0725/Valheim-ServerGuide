# Phase 08 — Discord Guide Events

**Status:** `done`
**Depends on:** Phase 02 (chain completion hook), CRIT-08 (existing Discord infrastructure)
**Blocks:** Phase 09 (content authors can enable Discord events per guide)

Extends `src/Discord/DiscordAnnouncer.cs` to POST a webhook message when a player
completes an entire guide chain. Server-side only — Discord URL never leaves the server.
See CRIT-08.

---

## YAML Field

```yaml
- id: companions_offline_chain
  title: "Offline Companions Guide"
  discord_on_complete: true          # POST webhook when last step fires
  steps: [...]
```

`discord_on_complete` is a `bool` field on `GuidanceEntry` added in Phase 06.

---

## Webhook Payload

Posted to the configured Discord webhook URL when a chain's final step fires:

```json
{
  "content": "**[PlayerName]** has completed the **Offline Companions Guide**! 🎉"
}
```

Optionally, embed format for richer output:

```json
{
  "embeds": [{
    "title": "Guide Complete",
    "description": "**[PlayerName]** finished **Offline Companions Guide**",
    "color": 3066993,
    "footer": { "text": "Hearthbound Valheim" }
  }]
}
```

Format (plain `content` vs. embed) is configurable via BepInEx config `DiscordGuideFormat`.

---

## Changes to `DiscordAnnouncer.cs`

Add a new public method:

```csharp
public static void AnnounceChainComplete(string playerName, string chainTitle)
```

Called from `GuidanceDispatcher` after a chain's final step fires and `discord_on_complete == true`.

Implementation reuses the existing `UnityWebRequest` POST pattern. Server-side only.

---

## Call Site in `GuidanceDispatcher.cs`

```csharp
// After marking a chain as complete:
if (entry.DiscordOnComplete && ZNet.instance.IsServer())
{
    DiscordAnnouncer.AnnounceChainComplete(player.GetPlayerName(), entry.Title);
}
```

---

## BepInEx Config Keys

| Key | Default | Description |
|---|---|---|
| `DiscordGuideEnabled` | `true` | Enable/disable guide completion webhooks (independent of kill/event webhooks) |
| `DiscordGuideFormat` | `plain` | `plain` (content string) or `embed` (rich embed) |

These are added under the existing `[Discord]` section in Plugin config.

---

## Criteria

- [x] `discord_on_complete: false` (or absent) never triggers a POST. See CRIT-08.
- [x] Webhook POST is only executed on the server (`ZNet.instance.IsServer()`). See CRIT-08.
- [x] The Discord URL is read from server-side BepInEx config only — never sent to clients. See CRIT-08.
- [x] `DiscordGuideEnabled = false` suppresses all guide-completion POSTs without affecting kill/event POSTs.
- [x] Plain and embed format both include the player name and chain title.
- [x] POST failures (no webhook URL configured, network error) are logged to BepInEx log and do not throw.
- [x] Single-entry guides (no `steps:`) with `discord_on_complete: true` POST when the entry fires.
