# Discord Integration

ValheimServerGuide can post messages to a Discord channel via a webhook when guidance events occur. The webhook URL lives **only on the server** — it is never transmitted to clients, so players cannot see or abuse it.

---

## Setup

1. In Discord, go to your channel's **Settings → Integrations → Webhooks → New Webhook**.
2. Copy the webhook URL.
3. Open `BepInEx/config/com.valheimserverguide.cfg` on your **server**.
4. Paste the URL into `WebhookUrl`:

```ini
[Discord]
WebhookUrl = https://discord.com/api/webhooks/...
```

5. Optionally set the bot username:

```ini
BotUsername = ValheimServerGuide
```

Leave `WebhookUrl` empty to disable all Discord posting.

---

## Per-Entry Announcements

Add an `announce.discord` field to any entry to post when it fires:

```yaml
- id: world_eikthyr_fell
  scope: global
  trigger: { type: kill, creature: Eikthyr }
  display:
    mode: intro
    topic: "The Stag-King Falls"
    text: "Eikthyr is slain."
  announce:
    discord: "⚔️ **{playerName}** has slain **Eikthyr** — the first boss has fallen!"
```

### Message Templates

The `discord` value is a message template. Supported tokens:

| Token | Replaced with |
|---|---|
| `{playerName}` | The triggering player's display name |
| `{id}` | The entry's `id` field |
| `{topic}` | The entry's `display.topic` |
| `{text}` | The entry's `display.text` or `message` |

Set `discord: ""` (empty string) to use the **default template** from the BepInEx config:

```yaml
announce:
  discord: ""    # uses DefaultTemplate from BepInEx config
```

Default template (configurable in `com.valheimserverguide.cfg`):
```
**{playerName}** triggered **{topic}**
```

---

## Chain Completion Webhooks

Set `discord_on_complete: true` on a chain entry to post when the final step completes. This fires in addition to (or instead of) per-step `announce.discord`.

```yaml
- id: boss_prep_chain
  title: "Prepare for Eikthyr"
  discord_on_complete: true
  announce:
    discord: "**{playerName}** has completed the Eikthyr preparation quest!"
  steps:
    - trigger: { type: craft, item: ShieldWood }
      message: "Craft a shield."
    - trigger: { type: craft, item: ArmorLeatherChest }
      message: "Craft leather armor."
```

Configure the format in the BepInEx config:

```ini
[Discord]
DiscordGuideEnabled = true
DiscordGuideFormat = plain       # plain | embed
```

`DiscordGuideFormat = embed` sends a rich embed instead of a plain text message.

---

## Privacy

The Discord webhook URL is read **only on the server**. It is:

- Never included in config synced to clients.
- Never logged to the game console.
- Never sent over any RPC channel.

Players connecting to your server cannot discover the webhook URL.

---

## Example Discord Messages

With the example entries above, your Discord channel would receive:

```
⚔️ MagnusThor has slain Eikthyr — the first boss has fallen!
```

```
MagnusThor has completed the Eikthyr preparation quest!
```

---

## Troubleshooting

**No messages appearing in Discord:**
- Confirm `WebhookUrl` is set in the BepInEx config on the **server** (not the client).
- Verify the webhook URL is valid by testing it in a Discord webhook tester.
- Check the server's BepInEx console log for `[VSG] Discord POST` lines to confirm the mod is attempting to send.

**Messages appear but `{playerName}` shows as blank:**
- The entry may be triggering on the server without an associated player. Global-scope entries require a player to trigger them.
