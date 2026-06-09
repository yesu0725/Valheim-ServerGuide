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

---

## YAML `announce` Field

```yaml
announce:
  discord: null        # absent/null  → no Discord announcement
  discord: ""          # empty string → use DefaultTemplate from BepInEx config
  discord: "Custom: {playerName} killed {creatureName}!"   # literal template
```

Supported tokens in templates: `{playerName}`, `{id}`, `{topic}`, `{text}`.

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
