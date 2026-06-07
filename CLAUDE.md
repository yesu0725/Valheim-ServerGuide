# ValheimServerGuide — Project Reference

Server-authoritative, YAML-driven in-game guidance mod for Valheim.
Uses **vanilla assets only**. Built on BepInEx 5 + HarmonyX + Jötunn.

## Quick Facts

| Field | Value |
|---|---|
| GUID | `com.valheimserverguide` |
| Version | `0.1.0` |
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
│   ├── GuidanceConfig.cs            YAML data model (GuidanceEntry, TriggerSpec, DisplaySpec, …)
│   └── GuidanceConfigLoader.cs      FileSystemWatcher + debounce + starter YAML
├── State/
│   ├── SeenTracker.cs               Fire-state storage (player m_customData / world ZoneSystem keys)
│   └── SubmitState.cs               In-progress item collection counters for npc_item_submit (VSG.is.*)
├── Triggers/
│   ├── GuidanceDispatcher.cs        Match-and-fire logic; player vs global routing; FireEntry() single-entry path
│   ├── CraftTrigger.cs              Harmony patch: InventoryGui.DoCrafting
│   ├── KillTrigger.cs               Harmony patch: Character.OnDeath
│   └── NpcItemSubmitTrigger.cs      Harmony patch: Trader.UseItem + GetHoverText; count/consume/progress
├── Display/
│   ├── GuidanceDisplay.cs           All five display modes + Harmony patches for raven/intro/music
│   └── NpcConversationPanel.cs      Hold-E conversation panel with choice navigation (CRIT-17)
├── Rewards/
│   ├── RewardDispatcher.cs          Grant items / skill exp / buffs on entry completion (CRIT-18)
│   └── RewardNotification.cs        MessageHud summary after rewards are granted (CRIT-18)
├── Net/
│   └── GuidanceSync.cs              ZRoutedRpc RPCs; server↔client config & global-event sync
├── Commands/
│   └── AdminCommands.cs             vsg_reset / vsg_list Terminal.ConsoleCommand
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

## Key Invariants (never violate these)

1. **Vanilla assets only** — no custom textures, prefabs, or AssetBundles. See CRIT-14.
2. **Server is the authority** — clients never override server config. See CRIT-06.
3. **Discord URL stays server-side** — never sent over RPC to clients. See CRIT-08.
4. **Raven mode has its own toggle** — `RavenEnabled` BepInEx config, independent of the vanilla "Tutorials" game setting. See CRIT-11.
5. **YamlDotNet.dll is NOT deployed** — Jötunn's transitive dep provides it at runtime. See CRIT-10.
6. **RPC names are registered exactly once** — `_rpcsBound` guard in GuidanceSync; reset in ZNet.OnDestroy. See CRIT-06.
