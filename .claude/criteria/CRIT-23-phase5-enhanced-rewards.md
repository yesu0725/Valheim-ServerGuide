# CRIT-23 — Enhanced Reward Types (Roadmap Phase 5)

**Status:** `done`

Thirteen new `rewards[].type` values beyond the original item/skill_exp/skill_level/buff set
(CRIT-18). All run on whichever client raised the triggering event — same execution model as
the existing reward types — except `discord`, which proxies through a server RPC because the
webhook URL is a server-only secret (CRIT-08).

See [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) Phase 5.

---

## Reward Types

| Type | Fields | Implementation |
|---|---|---|
| `map_pin` | `name`, `x`, `z`, `icon` | `Minimap.instance.AddPin(pos, PinType, name, save:true, isChecked:false)` |
| `location_pin` | `location`, `pin_name`, `icon` | `ZoneSystem.instance.FindClosestLocation(location, playerPos, out closest)` then `AddPin` at `closest.m_position`. Skipped with a warning if the location hasn't been discovered/generated nearby. |
| `unlock_recipe` | `recipe` (Recipe asset name, e.g. `Recipe_FishingRod`) | `ObjectDB.instance.m_recipes.Find(r => r.name == recipe)` then `player.AddKnownRecipe(recipe)` — that method is `private` in vanilla but callable directly because `assembly_valheim` is publicized at build time (see CLAUDE.md). Reuses vanilla's own "$msg_newrecipe" unlock toast. |
| `spawn_creature` | `prefab`, `tamed`, `count` | Instantiates `count` copies of the prefab in a ring 2.5m around the player; calls `Character.SetTamed(true)` per spawn when `tamed: true`. |
| `set_global_key` / `remove_global_key` | `key` | `ZoneSystem.instance.SetGlobalKey(key)` / `RemoveGlobalKey(key)` — world-wide, persists with the save. |
| `set_player_key` / `remove_player_key` | `key` | `player.AddUniqueKey(key)` / `RemoveUniqueKey(key)` — same storage other mods read via `Player.HaveUniqueKey()`. |
| `weather` | `preset`, `duration` (default 60s) | `EnvMan.instance.SetForceEnvironment(preset)`; a coroutine clears it back to `""` after `duration`, but only if nothing else has since forced a *different* environment in the meantime. Logs a warning (but still forces it) if `preset` doesn't match any `EnvSetup.m_name` in `EnvMan.m_environments`. |
| `chat_message` | `message` (supports `{player_name}`) | `Chat.instance.AddString(text)` — local-only, same call `display.mode: chat` already uses. |
| `teleport` | `x`, `z`, `allowlist_only` | `player.TeleportTo(new Vector3(x, player.y, z), player.rotation, true)`. See note below on `allowlist_only`. |
| `rename_player` | `suffix` | Appends to the player's networked name: `zdo.Set(ZDOVars.s_playerName, baseName + " " + suffix)` via `player.GetComponent<ZNetView>().GetZDO()`. Visible to everyone (chat, name tags) since it's the actual replicated player name field, not a local-only cosmetic. |
| `discord` | `message` (supports `{player_name}`) | Client sends the already-expanded text over a new RPC (`VSG_RewardDiscord`) to the server; `GuidanceSync.OnRewardDiscord` calls `DiscordAnnouncer.AnnounceRaw(message)`, which posts with the server's own `DiscordWebhookUrl`/`DiscordBotUsername` config — mirrors the existing `RpcAnnounce` pattern used for `announce.discord`. |

### `allowlist_only` is documentation-only

The roadmap's draft schema implies a separate server-side allowlist check for `teleport`
destinations. There isn't one, because there's no separate trust boundary to enforce: the
coordinates already come from the server-authoritative YAML (CRIT-06) — the same config a
malicious client cannot edit to add an unauthorized teleport reward in the first place. The
field is accepted (and the loader's `IgnoreUnmatchedProperties()` means it always was, even
before this phase) but does not gate anything at grant time.

---

## RewardSpec (GuidanceConfig.cs additions)

```csharp
// map_pin / location_pin / teleport
public string Name { get; set; }
public float X { get; set; }
public float Z { get; set; }
public string Icon { get; set; }
public string Location { get; set; }
public string PinName { get; set; }
public bool AllowlistOnly { get; set; }

public string Recipe { get; set; }            // unlock_recipe

public string Prefab { get; set; }             // spawn_creature
public bool Tamed { get; set; }
public int Count { get; set; } = 1;

public string Key { get; set; }                // *_global_key / *_player_key

public string Preset { get; set; }             // weather
public float Duration { get; set; } = 60f;

public string Message { get; set; }            // chat_message / discord

public string Suffix { get; set; }             // rename_player
```

`icon` parses against `Minimap.PinType` (`System.Enum.TryParse`, case-insensitive); an unknown
or absent value falls back to `Minimap.PinType.Icon3`.

---

## Notification summaries (`RewardNotification.cs`)

Player-visible "Received: ..." line additions: `map_pin`/`location_pin` → "Map pin: {name}",
`unlock_recipe` → "Recipe: {name}", `spawn_creature` → "{prefab} x{count}", `weather` →
"Weather: {preset}", `teleport` → "Teleported", `rename_player` → "Title: {suffix}". The four
key-toggle types and `chat_message`/`discord` are intentionally silent in this summary — the
keys are invisible bookkeeping and the other two already deliver their own visible
message (chat line / Discord post).

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add 13 new fields to `RewardSpec` |
| `src/Rewards/RewardDispatcher.cs` | Add `Grant*`/`Validate` handling for all 13 new types |
| `src/Rewards/RewardNotification.cs` | Add `Describe` cases for the new types |
| `src/Discord/DiscordAnnouncer.cs` | New `AnnounceRaw(message)` — server-side post with no entry/template context |
| `src/Net/GuidanceSync.cs` | New RPC `VSG_RewardDiscord` (client → server) + `SendRewardDiscord`/`OnRewardDiscord` |
| `src/ValheimServerGuide.csproj` | Add `Splatform.dll` reference — required by `Minimap.AddPin`'s optional `PlatformUserID` parameter even when not explicitly passed |

---

## Criteria

- [x] `map_pin` adds a pin at the exact `x`/`z` with the requested icon and name.
- [x] `location_pin` finds the nearest discovered instance of `location` and pins it; logs a warning and skips (no crash) if none has been discovered yet.
- [x] `unlock_recipe` adds the named recipe to the player's known list and triggers vanilla's own unlock notification.
- [x] `spawn_creature` spawns exactly `count` instances near the player, tamed when `tamed: true`.
- [x] `set_global_key` / `remove_global_key` set/clear a world-wide `ZoneSystem` global key.
- [x] `set_player_key` / `remove_player_key` set/clear a key visible to `Player.HaveUniqueKey()`.
- [x] `weather` forces the named environment immediately and reverts automatically after `duration` seconds.
- [x] `chat_message` posts the (token-expanded) text to local chat.
- [x] `teleport` warps the player to the exact `x`/`z` (keeping current height).
- [x] `rename_player` appends `suffix` to the player's name, visible to other connected players.
- [x] `discord` posts to the server's configured webhook without exposing the URL to any client.
- [x] An unknown/malformed field on any new type logs a warning and skips that single reward; the rest of the list still executes (existing CRIT-18 invariant, unchanged).
- [x] Build is clean: `Build succeeded. 0 Warning(s) 0 Error(s)`.
