# CRIT-13 — Text Templates

**File:** `src/Triggers/GuidanceDispatcher.cs` (`TemplateText`)

---

## Overview

`display.text` and `announce.discord` (when a literal template) support token substitution. Tokens are replaced at fire time with contextual values from the triggering event and player.

---

## Supported Tokens

| Token | Replaced with | Available when |
|---|---|---|
| `{playerName}` | The local player's name (`Player.GetPlayerName()`) | Always |
| `{itemName}` | `evt.DisplayName` ?? `evt.Subject` | `craft`, `pickup`, `equip` triggers |
| `{creatureName}` | `evt.DisplayName` ?? `evt.Subject` | `kill` trigger |
| `{biome}` | `evt.Subject` (biome name string) | `biome` trigger |

`{id}` and `{topic}` are available in Discord templates only (see CRIT-08); they are not substituted in `display.text`.

---

## Global-Scope Events

For global-scope entries, `PlayGlobalReceived` is called on every client with the original triggerer's player name. The `TriggerEvent` is not available at this point (only the entry ID and player name were transmitted). Template resolution:

```csharp
var rendered = TemplateText(entry.Display?.Text, evt: null, playerName: sourcePlayerName);
```

When `evt` is `null`:
- `{playerName}` is still substituted with `sourcePlayerName`.
- `{itemName}`, `{creatureName}`, `{biome}` resolve to `""` (empty string, not an error).

This means global-scope entries should avoid `{itemName}`/`{creatureName}` tokens if the subject matters, or use them knowing they'll be empty.

---

## `DisplayName` vs `Subject`

`TriggerEvent.DisplayName` is the human-readable / localized name when available. `Subject` is always the raw prefab name.

Examples:
- `kill` with `Eikthyr`: Subject=`"Eikthyr"`, DisplayName might be `"Eikthyr"` (same in this case, but creatures with localization keys would differ).
- `craft` with `SwordBronze`: Subject=`"SwordBronze"`, DisplayName might be `"Bronze Sword"` (if the trigger source provides it).

If `DisplayName` is null/empty, the token falls back to `Subject`.

---

## `TemplateText` Implementation

```csharp
internal static string TemplateText(string template, TriggerEvent evt, string playerName)
{
    if (string.IsNullOrEmpty(template)) return template;
    return template
        .Replace("{playerName}", playerName ?? "")
        .Replace("{itemName}",    evt?.DisplayName ?? evt?.Subject ?? "")
        .Replace("{creatureName}", evt?.DisplayName ?? evt?.Subject ?? "")
        .Replace("{biome}",       evt?.Subject ?? "");
}
```

Note: `{itemName}` and `{creatureName}` resolve to the same value (`DisplayName ?? Subject`). They are aliases — the correct one to use depends on the trigger type, but either works.

---

## Discord Template Tokens

In addition to the above, Discord templates (in `announce.discord` or `DiscordDefaultTemplate`) support:

| Token | Value |
|---|---|
| `{playerName}` | Player name |
| `{id}` | Entry ID from config |
| `{topic}` | `display.topic` from config |
| `{text}` | The already-rendered `display.text` (after token substitution) |

Discord token substitution is handled inside `DiscordAnnouncer.Announce`, not in `TemplateText`.

---

## YAML Example

```yaml
- id: eikthyr_kill
  trigger:
    type: kill
    creature: Eikthyr
  display:
    mode: intro
    topic: "The First Sacrifice"
    text: "You have slain {creatureName}, {playerName}. The first forsaken falls."
  scope: global
  announce:
    discord: "**{playerName}** has defeated **{creatureName}** on this world!"
```

---

## Criteria

- [ ] All tokens are replaced at fire time, not at config load time.
- [ ] Unknown tokens (e.g., `{unknownToken}`) are left as-is in the output (no error, no substitution).
- [ ] When `evt` is `null` (global-scope display on non-triggering clients), `{itemName}`, `{creatureName}`, `{biome}` resolve to empty string, not an exception.
- [ ] `{playerName}` is always substituted, even when `evt` is null (uses `sourcePlayerName` from the RPC).
- [ ] `{itemName}` and `{creatureName}` are functionally identical — both resolve to `DisplayName ?? Subject`. Use whichever reads more naturally for the trigger type.
- [ ] Discord template substitution is separate from display text substitution (handled in `DiscordAnnouncer`, not `TemplateText`).
- [ ] A null or empty `display.text` passes through unchanged (no null reference exception).
