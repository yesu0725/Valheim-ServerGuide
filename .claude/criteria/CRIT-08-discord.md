# CRIT-08 — Discord Webhooks

**File:** `src/Discord/DiscordAnnouncer.cs`

---

## Overview

When an entry fires and has `announce.discord` configured, the server posts a message to a Discord webhook. This is **server-side only** — the webhook URL is never sent to clients.

---

## Config Entries (BepInEx `Discord` section)

| Key | Default | Description |
|---|---|---|
| `WebhookUrl` | `""` | Discord webhook URL. Empty = disabled. Server only. |
| `DefaultTemplate` | `"**{playerName}** triggered **{topic}**"` | Template used when `announce.discord: ""` |
| `BotUsername` | `"ValheimServerGuide"` | Display name for the webhook message in Discord |
| `DiscordGuideEnabled` | `true` | Master toggle for `discord_on_complete` chain-completion posts |
| `DiscordGuideFormat` | `"plain"` | `plain` \| `embed` — format for chain-completion posts |
| `QuestStartLogEnabled` | `true` | Master toggle for the quest-start debug log (see below) |
| `QuestStartWebhookUrl` | `""` | **Separate** webhook for the quest-start log. Empty = disabled. Server only. |

---

## YAML `announce` Field

```yaml
announce:
  discord: null        # absent/null  → no Discord announcement
  discord: ""          # empty string → use DefaultTemplate from BepInEx config
  discord: "Custom: {playerName} killed {creatureName}!"   # literal template
```

Supported tokens in templates: `{playerName}`, `{id}`, `{topic}`, `{text}`.

**Never highlighted.** Discord templating runs on the raw text: `TextHighlighter` (CRIT-25) is
applied on display paths only, because a webhook post carrying `<color=#FFCC55>` would show the
literal markup. This includes `{text}`, which is the templated — not highlighted — display text.
The `chat_message` reward is the sole exception, since it renders in the in-game chat.

---

## Two Announcement Paths

### Global-scope entries
Server handles it directly in `OnTriggerGlobal`:
```csharp
if (entry.Announce?.Discord != null)
    DiscordAnnouncer.Announce(entry, playerName);
```
No RPC needed — the server already processes the global trigger.

### Player-scope entries
Client sends `VSG_AnnounceRequest(entryId, playerName)` RPC to the server.
Server's `OnAnnounceRequest` verifies the entry exists and has discord configured, then posts.
The client never posts directly — it never has the webhook URL.

---

## Quest-Start Debug Log (v0.8.0)

A **separate** announcement path from `announce.discord` / `discord_on_complete`, intended for
verifying that quests fire as intended. It posts to its own webhook (`QuestStartWebhookUrl`) — never
the main `WebhookUrl` — so monitoring logs stay isolated from player-facing announcements.

- **When:** the first time a player *begins* a quest — a chain's first step firing, or the first
  fire of any single (non-chain) entry — for player- and global-scope entries.
- **Once per character per quest:** latched in `QuestStartLogState` (`VSG.qs.<id>` in `m_customData`).
  Repeat fires, cooldown re-fires, and later chain steps do **not** re-log. Cleared by
  `vsg_reset` / `vsg_reset_player` (both `all` and single-id) so a quest can be re-tested.
- **Transport:** the webhook URL is server-side (CRIT-08 security). The triggering client sends a
  `VSG_QuestStartLog` RPC to the server carrying `entryId`, `playerName`, `biome`, and `position`
  (packed into one unit-separator-delimited string). The server resolves the entry's base info
  (title / category / trigger type or chain step-count) from its own config and posts.
- **Payload:** a Discord **embed** with fields for Quest (title + id), Category, Trigger, Player,
  and Location (biome + rounded X/Y/Z), plus an ISO-8601 UTC `timestamp` stamped server-side on
  RPC receipt (within milliseconds of the trigger).
- **Dispatcher hooks** (`GuidanceDispatcher.MaybeLogQuestStart`): single-entry player + global paths
  in `Raise`, `FireEntry`, `FireById`, and chain step-0 in both normal and counter steps.

---

## HTTP Request Details

- Uses `UnityWebRequest` (coroutine on `Plugin.Instance`).
- Method: `POST`.
- Content-Type: `application/json`.
- Body (JSON):
  ```json
  {
    "content": "<rendered message, max 1900 chars>",
    "username": "<BotUsername config>",
    "allowed_mentions": { "parse": [] }
  }
  ```
- `allowed_mentions: { "parse": [] }` prevents `@everyone` / `@here` / role pings even if the template text contains them.
- Message body is capped at **1900 characters** (Discord limit is 2000; 100 chars reserved for safety).
- Special characters in the message are JSON-escaped manually.

---

## Success / Error Handling

- Uses `req.result != UnityWebRequest.Result.Success` (not deprecated `isNetworkError`/`isHttpError`).
- On failure: logs `LogError` with the HTTP error string.
- On success: logs `LogInfo` with the entry ID and player name.
- Announcement failures are non-fatal — the guidance display is never blocked by a webhook failure.

---

## Security Rules

- The webhook URL is read from BepInEx config only on the server process.
- The URL is **never** included in `VSG_SyncConfig`, `VSG_PlayGlobal`, or any other RPC payload.
- `DiscordAnnouncer.Announce` should only be called from server-side code paths.

---

## Criteria

- [ ] Discord URL is read only from the server's BepInEx config; never transmitted to clients.
- [ ] `announce.discord: null` (absent) means no announcement — no RPC is sent, no POST is made.
- [ ] `announce.discord: ""` uses `DiscordDefaultTemplate` from BepInEx config.
- [ ] `announce.discord: "custom text"` uses the literal string as a template.
- [ ] All template tokens (`{playerName}`, `{id}`, `{topic}`, `{text}`) are replaced before posting.
- [ ] The message body is capped at 1900 characters.
- [ ] `allowed_mentions: { parse: [] }` is always included to prevent ping abuse.
- [ ] A failed POST logs an error but does NOT crash or block the guidance display.
- [ ] The HTTP request runs in a coroutine so it doesn't block the game loop.
- [ ] Player-scope announcements are routed to the server via `VSG_AnnounceRequest` RPC; the client never posts directly.
- [ ] The quest-start log posts only to `QuestStartWebhookUrl` (never the main `WebhookUrl`); empty URL or `QuestStartLogEnabled = false` disables it.
- [ ] The quest-start log fires once per character per quest and is cleared by `vsg_reset` / `vsg_reset_player`.
- [ ] The quest-start webhook URL is server-side only; the client sends a `VSG_QuestStartLog` RPC and never posts directly.
