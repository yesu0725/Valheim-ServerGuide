# ValheimServerGuide — Project Reference

Server-authoritative, YAML-driven in-game guidance mod for Valheim.
Uses **vanilla assets only**. Built on BepInEx 5 + HarmonyX + Jötunn.

## Quick Facts

| Field | Value |
|---|---|
| GUID | `com.valheimserverguide` |
| Version | `0.12.0` |
| Model | `claude-sonnet-4-6` |
| Framework | net48 |
| BepInEx dep | `5.x` (HarmonyX included) |
| Jötunn dep | `[BepInDependency(Jotunn.Main.ModGuid)]` |
| YAML lib | YamlDotNet 16.3.0 (UnderscoredNamingConvention) |
| Publicizer | BepInEx.AssemblyPublicizer.MSBuild 0.4.2 (Publicize=true on assembly_valheim) |

## Source Layout

```
src/
├── Plugin.cs                        BepInEx entry, config, loader lifecycle
├── Config/
│   ├── GuidanceConfig.cs            YAML data model (GuidanceEntry, TriggerSpec, DisplaySpec, RewardSpec, HoverTextSpec, HighlightSpec, …)
│   └── GuidanceConfigLoader.cs      FileSystemWatcher + debounce + starter YAML; recursively merges every *.yaml/*.yml under the config folder (all subfolders)
├── State/                           Progress buckets (one class per VSG.* prefix) — see CRIT-26 for where they persist
│   ├── PlayerProgress.cs            THE accessor every bucket goes through; Local/Remote/Legacy modes (CRIT-26)
│   ├── PlayerProgressStore.cs       Server-side per-character YAML files + one-time migration (CRIT-26)
│   ├── SeenTracker.cs               Fire state + cooldown + max_fires (VSG.fired / VSG.fc.*)
│   ├── SubmitState.cs               npc_item_submit in-progress counters (VSG.is.*)
│   ├── ChainState.cs                Chain step/counter/complete/version (VSG.cp./cd./cc./cv.)
│   ├── KillCountState.cs            kill-count accumulator (VSG.kc.*)
│   ├── GoalStartedState.cs          item_acquired "started" latch (VSG.ig.*)
│   ├── ConversationNodeState.cs     Multi-node dialogue current node (VSG.cn.*)
│   ├── TrackedQuestState.cs         HUD tracker pins + custom panel position (VSG.trk / VSG.tpos)
│   ├── HiddenQuestState.cs          Codex "hide from list" preference (VSG.hid); display-only
│   ├── QuestStartLogState.cs        Once-per-quest "start already logged to Discord" latch (VSG.qs.*)
│   ├── PrerequisiteChecker.cs       requires/stop_when satisfaction logic
│   └── DebugFireLog.cs              Session-only last-10-fired ring buffer (vsg_debug; not persisted)
├── Triggers/                        One Harmony-patch file per trigger type; see CRIT-02
│   ├── GuidanceDispatcher.cs        Match-and-fire core; Raise / FireEntry / FireById / CheckGates
│   ├── TriggerUtils.cs              NormalizePrefabName + shared helpers
│   ├── CraftTrigger.cs · KillTrigger.cs · ItemAcquiredTrigger.cs · BiomeTrigger.cs · …
│   ├── TimeTrigger.cs               Poll coroutine: time_of_day / day_number / real_world_time / day_of_week
│   └── NpcConversationTrigger.cs    Hold-E detect, multi-quest picker, hover_text override
├── Display/
│   ├── GuidanceDisplay.cs           Mode dispatch (raven/message/chat/rune/intro/conversation/bubble) + patches
│   ├── CenterToast.cs               Sole writer of the vanilla centre message; merges a frame's lines into one multi-line toast (CRIT-03)
│   ├── RunePanel.cs                 Custom themed rune-reading panel (header/body/list, configurable fonts/colors; CRIT-03)
│   ├── GuidanceHudTracker.cs        Progress panel: F10 cycles Collapsed→Titles→Full (descriptions inline); Codex-pinned quests only, drag-to-move, no input lock
│   ├── GuidanceCodex.cs             In-game Guide Codex panel (F3); drawn in the vanilla inventory window's art (VanillaUi); scrolling guide list, "Pin to Tracker" + "Hide from list" toggles; suppresses ZInput's wheel while open
│   ├── NpcConversationPanel.cs      Hold-E conversation panel; content-sized + scrolling, wrapped choice rows (CRIT-17/22/25)
│   ├── NpcChatBubble.cs             World-space NPC bubble + vanilla-bubble suppression (CRIT-24)
│   ├── UiLayers.cs                  Canvas sortingOrder for every VSG surface + crosshair suppression (CRIT-03)
│   ├── VanillaUi.cs                 Vanilla inventory-window sprites/font/palette read off the live InventoryGui (CRIT-14)
│   ├── WheelScroller.cs             Fixed-pixels-per-notch wheel scrolling for every VSG ScrollRect (CRIT-03)
│   └── TextHighlighter.cs           `highlight:` rules -> TMP rich text; never applied to Discord (CRIT-25)
├── Rewards/
│   ├── RewardDispatcher.cs          17 reward types (CRIT-18 base + CRIT-23 enhanced)
│   └── RewardNotification.cs        MessageHud "Received: …" summary
├── Net/
│   └── GuidanceSync.cs              ZRoutedRpc RPCs; config sync, progress request/push/delta, global events, admin, reward-discord, kill-share, quest-start log
├── Commands/
│   └── AdminCommands.cs             vsg_reset / vsg_list / vsg_list_player / vsg_reset_player / vsg_debug / vsg_refresh (public)
└── Discord/
    └── DiscordAnnouncer.cs          Server-side webhook POST via UnityWebRequest; announce/complete/reward posts + separate quest-start debug log (own webhook)
```

## Development Workflow

Every phase follows the build → test → debug → update cycle defined in
[`.claude/PHASE_WORKFLOW.md`](/.claude/PHASE_WORKFLOW.md). Read it before starting any phase.

## Criteria Reference

Each feature area has its own detailed spec in `.claude/criteria/`.
**In every session, load only the criteria files relevant to the current task.**

| File | Topic |
|---|---|
| [CRIT-01](/.claude/criteria/CRIT-01-yaml-config.md) | YAML Config Schema |
| [CRIT-02](/.claude/criteria/CRIT-02-triggers.md) | Trigger Types |
| [CRIT-03](/.claude/criteria/CRIT-03-display-modes.md) | Display Modes |
| [CRIT-04](/.claude/criteria/CRIT-04-firing-semantics.md) | Firing Semantics |
| [CRIT-05](/.claude/criteria/CRIT-05-scope.md) | Player vs Global Scope |
| [CRIT-06](/.claude/criteria/CRIT-06-server-authority.md) | Server Authority & RPC Sync |
| [CRIT-07](/.claude/criteria/CRIT-07-intro-cinematic.md) | Intro Cinematic |
| [CRIT-08](/.claude/criteria/CRIT-08-discord.md) | Discord Webhooks |
| [CRIT-09](/.claude/criteria/CRIT-09-admin-commands.md) | Admin Commands |
| [CRIT-10](/.claude/criteria/CRIT-10-build-deploy.md) | Build & Deploy Targets |
| [CRIT-11](/.claude/criteria/CRIT-11-raven-bypass.md) | Raven Vanilla-Gate Bypass |
| [CRIT-12](/.claude/criteria/CRIT-12-state-persistence.md) | State Persistence |
| [CRIT-13](/.claude/criteria/CRIT-13-text-templates.md) | Text Templates |
| [CRIT-14](/.claude/criteria/CRIT-14-vanilla-assets-only.md) | Vanilla Assets Only |
| [CRIT-15](/.claude/criteria/CRIT-15-hearthbound-guide-plan.md) | Hearthbound Modpack Guide Plan |
| [CRIT-16](/.claude/criteria/CRIT-16-entry-finished-trigger.md) | `entry_finished` Trigger |
| [CRIT-17](/.claude/criteria/CRIT-17-npc-conversation.md) | NPC Conversation System |
| [CRIT-18](/.claude/criteria/CRIT-18-reward-system.md) | Reward System |
| [CRIT-19](/.claude/criteria/CRIT-19-phase1-triggers.md) | Phase 1 — Kill-count + 8 interaction triggers |
| [CRIT-20](/.claude/criteria/CRIT-20-phase2-time-day-triggers.md) | Phase 2 — Time & day triggers |
| [CRIT-21](/.claude/criteria/CRIT-21-phase3-multi-quest-picker.md) | Phase 3 — Multi-quest NPC picker |
| [CRIT-22](/.claude/criteria/CRIT-22-phase4-conversation-sequencing.md) | Phase 4 — Multi-node dialogue trees |
| [CRIT-23](/.claude/criteria/CRIT-23-phase5-enhanced-rewards.md) | Phase 5 — Enhanced reward types |
| [CRIT-24](/.claude/criteria/CRIT-24-phase6-system-polish.md) | Phase 6 — System polish (bubble, vsg_debug, hover_text, kill-share) |
| [CRIT-25](/.claude/criteria/CRIT-25-text-highlighting.md) | Text highlighting + no-truncation layout rules |
| [CRIT-26](/.claude/criteria/CRIT-26-server-side-progress.md) | Server-side player progress files + one-time migration |
| [phase-04f](/.claude/criteria/hearthbound/phase-04f-tracker-view-cycle.md) | F10 three-state view cycle (Collapsed / Titles / Full) — supersedes the 04b toggle |

The full multi-phase plan lives in [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) (Phases 1–6 all `done`).

## Key Invariants (never violate these)

1. **Vanilla assets only** — no custom textures, prefabs, or AssetBundles. See CRIT-14.
2. **Server is the authority** — clients never override server config. See CRIT-06.
3. **Discord URL stays server-side** — never sent over RPC to clients. See CRIT-08.
4. **Raven mode has its own toggle** — `RavenEnabled` BepInEx config, independent of the vanilla "Tutorials" game setting. See CRIT-11.
5. **YamlDotNet.dll is NOT deployed** — Jötunn's transitive dep provides it at runtime. See CRIT-10.
6. **RPC names are registered exactly once** — `_rpcsBound` guard in GuidanceSync; reset in ZNet.OnDestroy. See CRIT-06.
7. **No display surface truncates authored text** — panels size to their content and scroll past a screen-height cap; `Ellipsis`/`Truncate` overflow is banned on body text. See CRIT-25.
8. **Highlight markup never reaches Discord** — `TextHighlighter` runs on display paths only; webhook templating stays on the raw text. See CRIT-08/CRIT-25.
9. **Nothing writes the centre message directly** — `MessageHud`'s centre slot holds one string, so two writes in a frame lose the first. Queue via `CenterToast.Queue`; `Plugin.Update` flushes them as one multi-line toast. See CRIT-03.
10. **Game-supplied names are localization tokens** — `Trader.m_name` is `$npc_haldor`, not "Haldor". Run anything the game hands you through `TriggerUtils.LocalizeName` before showing it. `TemplateText` already does this for `{creatureName}`/`{itemName}`. See CRIT-13.
11. **A client only ever learns the config from `GuidanceSync.OnReceive`** — `Plugin.OnConfigChanged` returns early on non-authoritative processes. Anything clients must do on config load has to be wired into both. See CRIT-02 (`timed`) / CRIT-06.
12. **Never touch `Player.m_customData` for quest state** — progress lives in a server-owned per-character file. Go through `PlayerProgress` (`TryGet`/`Set`/`Remove`/`KeysWithPrefix`/`RemoveWithPrefix`); `m_customData` is now a read-only pre-migration backup. See CRIT-26.
13. **Canvas `sortingOrder` comes from `UiLayers`, and mouse wheels come from `WheelScroller`** — never hard-code a sorting number "just above" the vanilla element you collided with (vanilla's orders live in scene data and are not uniform), and never rely on `ScrollRect.scrollSensitivity` (Valheim's input module normalises the wheel delta to a fraction of a unit). See CRIT-03.
14. **Vanilla art is read off the live game, never loaded** — `VanillaUi` takes the inventory window's sprites and font from `InventoryGui`'s own components (Jötunn's `GUIManager` woodpanel is a *bundled* copy and is off-limits). Every slot degrades to a flat fill, and fixed-aspect panel art is fitted to its own aspect rather than stretched. See CRIT-14.
15. **Nothing may fire while `PlayerProgress.IsReady` is false** — an unbound store reads as "this player has done nothing", so every `once` quest would re-run *and persist the duplicate fires to the server*. Any new trigger that marks a guard key before dispatching must check `IsReady` itself, and any new spawn-time work belongs in `PlayerProgress.OnBecameReady()`. See CRIT-26.
