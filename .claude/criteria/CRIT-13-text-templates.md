# CRIT-13 — Text Templates

**File:** `src/Triggers/GuidanceDispatcher.cs` (`TemplateText`)

---

## Overview

`display.text`, `announce.discord` (when a literal template), and — since the reward `expand`
callback (CRIT-23) — reward `message` fields (`chat_message`, `discord`) all support token
substitution. Tokens are replaced at fire time with contextual values from the triggering event
and player.

---

## Supported Tokens

| Token | Replaced with | Available when |
|---|---|---|
| `{playerName}` / `{player_name}` | The local player's name (`Player.GetPlayerName()`) — both spellings are aliases for the same value | Always |
| `{itemName}` | `evt.DisplayName` ?? `evt.Subject` | `craft`, `pickup`, `equip` triggers |
| `{creatureName}` | `evt.DisplayName` ?? `evt.Subject` | `kill` trigger |
| `{biome}` | The local player's current biome (`Player.GetCurrentBiome()`); falls back to `evt.Subject` if the player is unavailable | Always (biome is read live, not just on the `biome` trigger) |
| `{skill}` | The skill name part of `"SkillName:level"` | `skill_level` trigger |
| `{level}` | The level part of `"SkillName:level"` | `skill_level` trigger |
| `{step}` | 1-based current chain step number | Chain steps only (passed explicitly by the caller; `-1` elsewhere means the token is left unexpanded) |
| `{total}` | Total step count in the chain | Chain steps only |
| `{caste}` | `evt.Subject` | `dvergr_*` triggers (caste name, or `"party"` for tournament party entries) |
| `{rank}` | `Extra["rank"]` | `dvergr_rank_changed`, `dvergr_rank_first`, `dvergr_party_rank_changed`, `dvergr_party_rank_first` |
| `{rating}` | `Extra["rating"]` | Same rank/ladder triggers above |
| `{companionName}` | `Extra["companionName"]` | `dvergr_duel_won`, rank triggers |
| `{ownerName}` | `Extra["ownerName"]` | `dvergr_duel_won`, rank triggers, `dvergr_party_duel_won` |
| `{partyName}` | `Extra["partyName"]` (falls back to the owner's name if the party is unnamed) | `dvergr_party_duel_won`, party rank triggers |
| `{winSize}` | `Extra["winSize"]` | `dvergr_party_duel_won` |
| `{opponentOwner}` | `Extra["opponentOwner"]` | `dvergr_party_duel_won` |
| `{mvpCaste}` | `Extra["mvpCaste"]` | `dvergr_party_duel_won` |
| `{opponent}` | `Extra["opponent"]` | `dvergr_duel_won`, `dvergr_tournament_match` |
| `{round}` | `Extra["round"]` | `dvergr_tournament_match` |
| `{mode}` | `Extra["mode"]` | `dvergr_tournament_won` |
| `{bracketSize}` | `Extra["bracketSize"]` | `dvergr_tournament_won` |

All `Extra`-backed tokens resolve to `""` (not an error) when the key is absent from
`evt.Extra` or `evt` itself is `null` — safe to use in any template regardless of trigger type.

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
internal static string TemplateText(string template, TriggerEvent evt, string playerName,
    int step = -1, int total = -1)
{
    if (string.IsNullOrEmpty(template)) return template;

    var biomeName = Player.m_localPlayer != null
        ? Player.m_localPlayer.GetCurrentBiome().ToString() : "";

    // skill_level: Subject = "SkillName:level".
    var skillName = ""; var levelStr = "";
    if (evt != null && evt.Type == "skill_level" && !string.IsNullOrEmpty(evt.Subject))
    {
        var sep = evt.Subject.IndexOf(':');
        if (sep >= 0) { skillName = evt.Subject.Substring(0, sep); levelStr = evt.Subject.Substring(sep + 1); }
    }

    string Extra(string key) =>
        evt?.Extra != null && evt.Extra.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    var result = template
        .Replace("{playerName}", playerName ?? "")
        .Replace("{player_name}", playerName ?? "")
        .Replace("{itemName}", evt?.DisplayName ?? evt?.Subject ?? "")
        .Replace("{creatureName}", evt?.DisplayName ?? evt?.Subject ?? "")
        .Replace("{biome}", string.IsNullOrEmpty(biomeName) ? (evt?.Subject ?? "") : biomeName)
        .Replace("{skill}", skillName)
        .Replace("{level}", levelStr)
        .Replace("{rank}", Extra("rank"))
        .Replace("{rating}", Extra("rating"))
        .Replace("{companionName}", Extra("companionName"))
        .Replace("{ownerName}", Extra("ownerName"))
        .Replace("{partyName}", Extra("partyName"))
        .Replace("{winSize}", Extra("winSize"))
        .Replace("{opponentOwner}", Extra("opponentOwner"))
        .Replace("{mvpCaste}", Extra("mvpCaste"))
        .Replace("{round}", Extra("round"))
        .Replace("{opponent}", Extra("opponent"))
        .Replace("{mode}", Extra("mode"))
        .Replace("{bracketSize}", Extra("bracketSize"))
        .Replace("{caste}", evt?.Subject ?? "");

    if (step >= 0)  result = result.Replace("{step}",  step.ToString());
    if (total >= 0) result = result.Replace("{total}", total.ToString());
    return result;
}
```

Note: `{itemName}` and `{creatureName}` resolve to the same value (`DisplayName ?? Subject`). They are aliases — the correct one to use depends on the trigger type, but either works. `{step}`/`{total}` are only substituted when the caller passes non-negative values (chain step firing); everywhere else they pass through unexpanded.

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
- [ ] Reward `chat_message`/`discord` messages receive the full token set (not just `{player_name}`) when granted through `RewardDispatcher.Grant`'s `expand` callback (see CRIT-23).
- [ ] All `Extra`-backed tokens (`{rank}`, `{rating}`, `{companionName}`, etc.) resolve to `""` — never throw — when `evt.Extra` is null or the key is missing.
