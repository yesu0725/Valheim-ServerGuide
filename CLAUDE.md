# ValheimServerGuide — Project Reference

Server-authoritative, YAML-driven in-game guidance mod for Valheim.
Uses **vanilla assets only**. Built on BepInEx 5 + HarmonyX + Jötunn.

## Quick Facts

| Field | Value |
|---|---|
| GUID | `com.valheimserverguide` |
| Version | `0.6.0` |
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
│   ├── GuidanceConfig.cs            YAML data model (GuidanceEntry, TriggerSpec, DisplaySpec, RewardSpec, HoverTextSpec, …)
│   └── GuidanceConfigLoader.cs      FileSystemWatcher + debounce + starter YAML
├── State/                           m_customData buckets (one class per VSG.* prefix)
│   ├── SeenTracker.cs               Fire state + cooldown + max_fires (VSG.fired / VSG.fc.*)
│   ├── SubmitState.cs               npc_item_submit in-progress counters (VSG.is.*)
│   ├── ChainState.cs                Chain step/counter/complete/version (VSG.cp./cd./cc./cv.)
│   ├── KillCountState.cs            kill-count accumulator (VSG.kc.*)
│   ├── GoalStartedState.cs          item_acquired "started" latch (VSG.ig.*)
│   ├── ConversationNodeState.cs     Multi-node dialogue current node (VSG.cn.*)
│   ├── TrackedQuestState.cs         HUD tracker pins + custom panel position (VSG.trk / VSG.tpos)
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
│   ├── GuidanceHudTracker.cs        Progress panel (F10): Codex-pinned quests only, drag-to-move, no input lock
│   ├── GuidanceCodex.cs             In-game Guide Codex panel (F3); per-quest "Show on Tracker" pin toggle
│   ├── NpcConversationPanel.cs      Hold-E conversation panel; multi-node trees (CRIT-17/22)
│   └── NpcChatBubble.cs             World-space NPC bubble + vanilla-bubble suppression (CRIT-24)
├── Rewards/
│   ├── RewardDispatcher.cs          17 reward types (CRIT-18 base + CRIT-23 enhanced)
│   └── RewardNotification.cs        MessageHud "Received: …" summary
├── Net/
│   └── GuidanceSync.cs              ZRoutedRpc RPCs; config sync, global events, admin, reward-discord, kill-share
├── Commands/
│   └── AdminCommands.cs             vsg_reset / vsg_list / vsg_list_player / vsg_reset_player / vsg_debug
└── Discord/
    └── DiscordAnnouncer.cs          Server-side webhook POST via UnityWebRequest
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

The full multi-phase plan lives in [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) (Phases 1–6 all `done`).

## Key Invariants (never violate these)

1. **Vanilla assets only** — no custom textures, prefabs, or AssetBundles. See CRIT-14.
2. **Server is the authority** — clients never override server config. See CRIT-06.
3. **Discord URL stays server-side** — never sent over RPC to clients. See CRIT-08.
4. **Raven mode has its own toggle** — `RavenEnabled` BepInEx config, independent of the vanilla "Tutorials" game setting. See CRIT-11.
5. **YamlDotNet.dll is NOT deployed** — Jötunn's transitive dep provides it at runtime. See CRIT-10.
6. **RPC names are registered exactly once** — `_rpcsBound` guard in GuidanceSync; reset in ZNet.OnDestroy. See CRIT-06.
