using System.Collections.Generic;

namespace ValheimServerGuide.Config
{
    public class GuidanceConfig
    {
        public static readonly GuidanceConfig Empty = new GuidanceConfig();

        public List<GuidanceEntry> Guidances { get; set; } = new List<GuidanceEntry>();

        /// Optional HUD tracker layout overrides. When present, these win over the BepInEx
        /// config and are applied live on YAML reload (host / single-player). See Phase 04.
        public TrackerSpec Tracker { get; set; }
    }

    /// Live-tunable layout for the on-screen objective tracker widget (Phase 04).
    /// Edit these in guidance.yaml and save — the widget repositions without a restart.
    /// Defaults are chosen to sit just below the top-right minimap, left-aligned to it.
    public class TrackerSpec
    {
        /// Master on/off. Overrides the BepInEx TrackerEnabled toggle when the section is present.
        public bool Enabled { get; set; } = true;
        /// KeyCode name for the toggle hotkey (e.g. F9, F10, H). See UnityEngine.KeyCode.
        public string Hotkey { get; set; } = "F10";
        /// Show the persistent corner hint badge. Independent of the main panel visibility.
        public bool BadgeEnabled { get; set; } = true;
        /// TopRight | TopLeft | BottomRight | BottomLeft.
        public string Anchor { get; set; } = "TopRight";
        /// Pixels in from the anchored corner, horizontally. Larger = further from that edge.
        public float OffsetX { get; set; } = 46f;
        /// Pixels in from the anchored corner, vertically. Larger = further from that edge.
        public float OffsetY { get; set; } = 320f;
        /// Panel width in pixels.
        public float Width { get; set; } = 210f;
        /// Base row font size. Header is +1, the "+N more" label is -1.
        public float FontSize { get; set; } = 15f;
        /// Seconds of no new progress before the auto-fade begins. 0 = disabled.
        public float AutoHideDelay { get; set; } = 5f;
        /// Duration of the alpha 1→0 fade-out in seconds.
        public float FadeDuration { get; set; } = 1f;
        /// Seconds an updated row stays gold before returning to white.
        public float HighlightDuration { get; set; } = 3f;
        /// Spawn the vanilla level-up VFX on the player when a chain completes.
        public bool CompletionVfxEnabled { get; set; } = true;
    }

    public class GuidanceEntry
    {
        public string Id { get; set; }
        /// Human-readable title shown in the Codex UI and HUD tracker.
        public string Title { get; set; }
        /// Mod/group label for Codex left-panel grouping.
        public string Category { get; set; }
        public TriggerSpec Trigger { get; set; }
        public DisplaySpec Display { get; set; }
        /// Top-level message for single-entry (non-chain) guidance.
        public string Message { get; set; }
        /// once_per_player | once_global | always | once_per_session
        public string FireMode { get; set; }
        public bool Once { get; set; } = true;
        public float Cooldown { get; set; } = 0f;
        /// Optional vanilla SFX prefab name played when the entry fires.
        public string Sound { get; set; }
        /// Guide content version. Bump when step messages change meaningfully.
        /// Players who completed this entry at a lower version will receive the
        /// updated messages as a notification on next login. Defaults to 1.
        public int Version { get; set; } = 1;
        /// When true, POST the Discord webhook when the chain completes (Phase 08).
        public bool DiscordOnComplete { get; set; }
        public List<string> Requires { get; set; } = new List<string>();
        /// If any of these IDs has fired for this character, the entry stops firing.
        /// Useful for "show this hint on a cooldown UNTIL the player has done X".
        public List<string> StopWhen { get; set; } = new List<string>();
        /// "player" (default) -- each player has their own fire state in m_customData.
        /// "global"           -- world-wide. First player to trigger broadcasts the display
        ///                       to every connected player; the fired state is stored in
        ///                       ZoneSystem global keys ("VSG.<id>") and persists with the
        ///                       world save.
        public string Scope { get; set; } = "player";
        public AnnounceSpec Announce { get; set; }
        /// Ordered steps. When present, this is a chain entry and Trigger/Display/Once
        /// on the parent are ignored — each step governs its own trigger and display.
        public List<GuidanceStep> Steps { get; set; }
        /// NPC conversation data: the choices shown when the conversation panel opens.
        public ConversationSpec Conversation { get; set; }
        public List<RewardSpec> Rewards { get; set; } = new List<RewardSpec>();
        /// Short recap shown in the Codex body when the entry is complete.
        /// Reminds the player what the quest was about without re-reading every step.
        public string Summary { get; set; }
        /// Overrides the vanilla NPC interact hover tooltip ("Hold E to interact") for
        /// the entry's trigger.npc, keyed by whether the entry has fired yet. Trader-bound
        /// NPCs only (Phase 6).
        public HoverTextSpec HoverText { get; set; }
    }

    /// Per-entry NPC hover tooltip override (Phase 6). Null/absent = vanilla behavior
    /// (plus the existing "[Hold E] Quest" hint when a conversation is available).
    public class HoverTextSpec
    {
        /// Shown while the entry is still eligible to fire (gates passing, not yet fired).
        public string Default { get; set; }
        /// Shown once the entry has fired (only meaningful when `once: true`).
        public string AfterFire { get; set; }
    }

    /// One step in a multi-step chain. Step N fires only after Step N-1 has fired.
    public class GuidanceStep
    {
        public TriggerSpec Trigger { get; set; }
        public DisplaySpec Display { get; set; }
        /// Text for this step. Overrides Display.Text when set.
        public string Message { get; set; }
        /// Optional tooltip body shown when hovering the tracker row for this step.
        /// Null/absent = no tooltip. Multi-line friendly (YAML block scalar).
        public string Description { get; set; }
        /// When > 0 this is a counter step.
        /// The step's Trigger activates the counter; each ProgressTrigger event increments it.
        /// The step's Message / display fires only when the counter reaches ProgressGoal.
        public int ProgressGoal { get; set; }
        /// The trigger that is counted toward ProgressGoal. Required when ProgressGoal > 0.
        public TriggerSpec ProgressTrigger { get; set; }
        /// HUD label for the counter, e.g. "Trophies".
        public string ProgressLabel { get; set; }
    }

    public class AnnounceSpec
    {
        /// Discord webhook message template. null/absent = no announcement.
        /// Empty string = use the default template from BepInEx config.
        /// Any other value = literal template, supports {playerName}, {id}, {topic}, {text}.
        public string Discord { get; set; }
    }

    /// One item/count pair used in a multi-goal item_acquired trigger.
    public class ItemGoalSpec
    {
        public string Item { get; set; }
        public int Count { get; set; } = 1;
    }

    public class TriggerSpec
    {
        public string Type { get; set; }
        public string Item { get; set; }
        public string Creature { get; set; }
        public string Piece { get; set; }
        public string Biome { get; set; }
        public string Location { get; set; }
        public float Radius { get; set; }
        public string Skill { get; set; }
        public int Level { get; set; }
        public string DamageType { get; set; }
        public string Npc { get; set; }
        public string Interval { get; set; }
        /// crafting_table_used / cooking_used: optional station prefab filter. Empty = any station.
        public string Station { get; set; }
        /// portal_used: optional portal tag filter. Empty = any portal.
        public string Tag { get; set; }
        /// time_of_day: target EnvMan.GetDayFraction() (0.0 = midnight, 0.5 = noon).
        public float GameTimeFraction { get; set; }
        /// time_of_day: +/- tolerance around GameTimeFraction, as a fraction of a full day.
        public float Window { get; set; } = 0.02f;
        /// day_number: target EnvMan.GetDay() value (parsed as int).
        /// day_of_week: target weekday name, e.g. "Saturday" (matched as string).
        public string Day { get; set; }
        /// real_world_time: target UTC hour (0-23) / minute (0-59).
        public int UtcHour { get; set; }
        public int UtcMinute { get; set; }
        public string Id { get; set; }
        public int MaxFires { get; set; }
        /// entry_finished: the ID of the entry whose completion triggers this one.
        public string Entry { get; set; }
        /// item_acquired: list of item/count goals — all must be reached before the entry fires.
        /// When present, takes precedence over the single-item Item/Count fields.
        public List<ItemGoalSpec> Goals { get; set; }
        /// npc_item_submit / single-item item_acquired: total count required.
        /// <= 1 means a single acquisition/submission completes immediately.
        public int Count { get; set; } = 1;
        /// npc_item_submit: whether submitted items are removed from the player's inventory.
        /// Default true (the NPC "takes" the item, like Hildir's quest turn-ins). When a stack
        /// is submitted, only the number still required is consumed — never the whole stack.
        public bool Consume { get; set; } = true;
        /// kill (with count > 1): when true, each kill broadcasts the increment to nearby
        /// players (see ShareProgressRadius in KillTrigger.cs) so the whole party's counter
        /// for this entry advances together, not just the player who landed the kill.
        public bool ShareProgress { get; set; }
    }

    public class ConversationSpec
    {
        public List<ChoiceSpec> Choices { get; set; } = new List<ChoiceSpec>();
        /// Multi-node dialogue tree (Phase 4). When present (non-empty), the panel renders
        /// the current node's text/choices instead of the flat Choices list above.
        public List<ConversationNodeSpec> Nodes { get; set; }
        /// When true, reopening this conversation resumes at the last-visited node instead
        /// of restarting at Nodes[0]. Progress is always persisted regardless of this flag —
        /// it only controls whether a fresh Open() reads that saved position back.
        public bool ResumeOnReturn { get; set; }
    }

    public class ChoiceSpec
    {
        /// Button label shown to the player.
        public string Text { get; set; }
        /// Optional entry ID to fire when this choice is selected. Null/absent = dismiss only.
        public string Goto { get; set; }
        /// Rewards granted to the player when this choice is confirmed.
        public List<RewardSpec> Rewards { get; set; } = new List<RewardSpec>();
    }

    /// One node in a multi-node conversation tree (Phase 4).
    public class ConversationNodeSpec
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public List<NodeChoiceSpec> Choices { get; set; } = new List<NodeChoiceSpec>();
    }

    /// One choice within a conversation node. A choice with neither GotoNode nor Goto set
    /// ends the conversation when selected (same "no goto = dismiss" rule as ChoiceSpec).
    public class NodeChoiceSpec
    {
        public string Label { get; set; }
        /// Jump to another node in the same conversation; the panel stays open.
        public string GotoNode { get; set; }
        /// Cross-entry goto — closes this conversation and fires another entry by ID,
        /// identical to ChoiceSpec.Goto.
        public string Goto { get; set; }
        /// Per-choice gate — entry IDs that must already be satisfied (same semantics as
        /// GuidanceEntry.Requires, checked via PrerequisiteChecker).
        public List<string> Requires { get; set; } = new List<string>();
        /// true = omit the button entirely while locked; false (default) = show it
        /// disabled/greyed out.
        public bool HiddenWhenLocked { get; set; }
        /// Optional hint appended to a locked, visible button's label.
        public string LockedHint { get; set; }
    }

    public class RewardSpec
    {
        /// item | skill_exp | skill_level | buff | map_pin | location_pin | unlock_recipe |
        /// spawn_creature | set_global_key | remove_global_key | set_player_key |
        /// remove_player_key | weather | chat_message | teleport | rename_player | discord
        public string Type { get; set; }

        // item fields
        public string Item { get; set; }
        public int Amount { get; set; } = 1;
        public int Quality { get; set; } = 1;

        // skill fields
        public string Skill { get; set; }
        public float SkillExp { get; set; }
        public int Level { get; set; }

        // buff fields
        public string Effect { get; set; }
        public float? DurationOverride { get; set; }

        // map_pin / location_pin / teleport fields
        public string Name { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
        public string Icon { get; set; }
        public string Location { get; set; }
        public string PinName { get; set; }
        public bool AllowlistOnly { get; set; }

        // unlock_recipe field
        public string Recipe { get; set; }

        // spawn_creature fields
        public string Prefab { get; set; }
        public bool Tamed { get; set; }
        public int Count { get; set; } = 1;

        // set_global_key / remove_global_key / set_player_key / remove_player_key field
        public string Key { get; set; }

        // weather fields
        public string Preset { get; set; }
        public float Duration { get; set; } = 60f;

        // chat_message / discord fields. Supports {player_name}.
        public string Message { get; set; }

        // rename_player field
        public string Suffix { get; set; }
    }

    public class DisplaySpec
    {
        public string Mode { get; set; } = "raven";   // raven | message | chat | rune | intro | conversation | bubble
        public string Topic { get; set; }
        public string Text { get; set; }
        public string Position { get; set; } = "TopLeft"; // for message mode
        /// bubble mode: prefab name of the NPC to float the text above (matched the same way
        /// trigger.npc is — TriggerUtils.NormalizePrefabName against nearby Characters).
        public string NpcName { get; set; }
        /// bubble mode: seconds the bubble stays visible before fading out. Default 6.
        public float Duration { get; set; } = 6f;
    }
}
