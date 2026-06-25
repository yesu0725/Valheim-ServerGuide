# CRIT-22 — Conversation Sequencing / Multi-Node Dialogue Trees (Roadmap Phase 4)

**Status:** `done`

`npc_conversation` entries can define a graph of **nodes** instead of a single flat
text+choices block. Each node has its own text and choices; choices can jump to another node
(stays open), jump to a different entry (closes this one, fires the other), or end the
conversation. Per-choice gates can hide or grey out a choice. Progress (current node) persists
so a relog mid-tree resumes where the player left off.

See [`.claude/FEATURE_ROADMAP.md`](/.claude/FEATURE_ROADMAP.md) Phase 4.

---

## YAML Schema

```yaml
- id: haldor_bounty
  trigger:
    type: npc_conversation
    npc: Haldor
  display:
    mode: conversation
    topic: Haldor
  conversation:
    resume_on_return: true
    nodes:
      - id: intro
        text: "You look like someone who's been in the mires before."
        choices:
          - label: "I've fought worse."
            goto_node: fought_worse
          - label: "What's it to you?"
            goto_node: suspicious
          - label: "[Ask about bounty]"
            requires: ["bounty_board_read"]
            goto_node: bounty_talk
            hidden_when_locked: true
      - id: fought_worse
        text: "Good. I need someone fearless for a task..."
        choices:
          - label: "Tell me more."
            goto: bounty_start_entry
          - label: "Not interested."
      - id: suspicious
        text: "Fair enough. Move along then."
```

This is additive: an entry without `conversation.nodes` (or with an empty list) keeps the exact
Phase-2/3 flat behavior — `entry.Message`/`display.text` + `conversation.choices`. Both shapes
share the same trigger (`npc_conversation`) and display mode (`conversation`).

### `closes_conversation` is not a field — termination is implicit

The roadmap's draft schema shows `closes_conversation: true` on a leaf choice ("Not interested.")
and on a childless node ("suspicious"). Neither is implemented as a property: a choice with
**no `goto_node` and no `goto`** always ends the conversation (mirrors the existing flat
`ChoiceSpec` rule — "no goto = dismiss"), and a node with **no choices** always renders a default
"Dismiss" button that does the same. `closes_conversation` is therefore redundant in this design;
the YAML loader's `IgnoreUnmatchedProperties()` silently accepts it if present, so the roadmap's
exact example YAML still parses without error — it's just a no-op.

---

## Config (`GuidanceConfig.cs`) new types

```csharp
public class ConversationSpec
{
    public List<ChoiceSpec> Choices { get; set; } = new List<ChoiceSpec>();
    public List<ConversationNodeSpec> Nodes { get; set; }   // new
    public bool ResumeOnReturn { get; set; }                // new
}

public class ConversationNodeSpec
{
    public string Id { get; set; }
    public string Text { get; set; }
    public List<NodeChoiceSpec> Choices { get; set; } = new List<NodeChoiceSpec>();
}

public class NodeChoiceSpec
{
    public string Label { get; set; }
    public string GotoNode { get; set; }
    public string Goto { get; set; }
    public List<string> Requires { get; set; } = new List<string>();
    public bool HiddenWhenLocked { get; set; }
    public string LockedHint { get; set; }
}
```

---

## State: `ConversationNodeState` (new)

`VSG.cn.<entry_id>` = current node id. Distinct from `ChainState`'s `VSG.cp./cd.` buckets — this
tracks position in a dialogue tree, not chain-entry progression. Written on every node render
(regardless of `resume_on_return`), so progress is never lost to a relog; `resume_on_return`
only controls whether `Open()` reads it back on a fresh hold-E (`false`/absent = always restart
at `nodes[0]`). Cleared whenever the conversation ends (any terminal choice).

---

## Panel logic (`NpcConversationPanel.cs`)

- `Open(entry, renderedText)` checks `entry.Conversation?.Nodes`; non-empty routes to
  `OpenNodeConversation`, otherwise the existing Phase-2/3 flat path runs unchanged.
- `OpenNodeConversation` picks the starting node (saved node if `resume_on_return` and a save
  exists, else `nodes[0]`) and calls `RenderNode`.
- `RenderNode` sets header/body from the node, persists the node as current, and builds one
  button per choice:
  - Locked (`PrerequisiteChecker.AllSatisfied(choice.Requires, ...)` fails) + `hidden_when_locked: true`
    → button omitted entirely.
  - Locked + `hidden_when_locked: false` (default) → button rendered with `Button.interactable = false`,
    faded colors, and `choice.locked_hint` appended to the label if set (no separate tooltip
    system — vanilla UI only).
  - No choices at all → a single default "Dismiss" button, same convention as the flat path.
- Selecting a choice (`OnNodeChoiceSelected`):
  - `goto_node` set → `RenderNode` on the target node; panel stays open, no fire/mark yet.
  - else `goto` set → `OnNodeConversationEnd(entry, choice.Goto)`.
  - else → `OnNodeConversationEnd(entry, null)`.
- `OnNodeConversationEnd` closes the panel, marks the entry fired (`once`/`cooldown`/`max_fires`,
  identical to the flat `OnChoiceSelected` path), clears `ConversationNodeState` for this entry,
  and — if a cross-entry `goto` was given — calls `GuidanceDispatcher.FireById`.

### Button label wrap

`AddChoiceButton`'s label previously was single-line (`enableWordWrapping = false`), which let
longer labels — picker entry titles, node choice labels, the `locked_hint` suffix — overflow the
fixed-width button. The label now uses a **fixed small font** (`fontSize = 13`) with
`enableWordWrapping = true` and `overflowMode = Overflow`, so longer labels flow onto a second
line inside the 40px button instead of being clipped. Applies to every choice button — flat
(`ChoiceSpec`), multi-quest picker (`AddSelectionButton`), and node choices (`AddNodeChoiceButton`)
all share this one method.

**TMP auto-sizing was tried first and removed.** `enableAutoSizing` (min 9 / max 14) resolves the
*largest* font that fits the text on one line against the rect, then truncates the remainder — the
exact opposite of wrapping. The on-screen symptom was a larger font with the tail of the label cut
off (e.g. `[Trade rare goods] (Locked:` clipped mid-word). A fixed font with `Overflow` (never
`Truncate`) is what actually wraps. `FinalizeChoiceLayout()` — called right after
`_choiceContainer.SetActive(true)` in all three build paths (`Open`, `OpenSelection`,
`RenderNode`) — still forces `LayoutRebuilder.ForceRebuildLayoutImmediate` + `ForceMeshUpdate` so
the first wrap render runs against the settled button width rather than the stale
build-while-inactive rect.

---

## Files Changed

| File | Change |
|---|---|
| `src/Config/GuidanceConfig.cs` | Add `ConversationNodeSpec`, `NodeChoiceSpec`; add `Nodes`/`ResumeOnReturn` to `ConversationSpec` |
| `src/State/ConversationNodeState.cs` | New — `VSG.cn.*` current-node accumulator (Get/Set/Clear/ResetAll) |
| `src/Display/NpcConversationPanel.cs` | Add `OpenNodeConversation`, `RenderNode`, `FindNode`, `AddNodeChoiceButton`, `OnNodeChoiceSelected`, `OnNodeConversationEnd`; `AddChoiceButton` gains an `interactable` parameter for locked/disabled rendering |
| `src/Commands/AdminCommands.cs` | Reset `VSG.cn.*` (all + single id) |
| `src/Net/GuidanceSync.cs` | Reset `VSG.cn.*` (all + single id) |
| `.claude/criteria/CRIT-17-npc-conversation.md` | Cross-reference this file |

---

## Criteria

- [x] An entry with `conversation.nodes` opens at `nodes[0]` on first hold-E.
- [x] A `goto_node` choice renders the target node's text/choices without closing the panel.
- [x] A `goto_node` referencing a non-existent node id logs a warning and ends the conversation gracefully (no crash).
- [x] A choice with neither `goto_node` nor `goto` ends the conversation when selected (marks fired, closes panel).
- [x] A node with no choices shows a default "Dismiss" button that ends the conversation.
- [x] A `goto` (cross-entry) choice closes this conversation, marks it fired, and fires the target entry by ID.
- [x] A locked choice (`requires` not satisfied) with `hidden_when_locked: true` is omitted from the button row.
- [x] A locked choice with `hidden_when_locked: false` (default) is shown disabled/greyed, with `locked_hint` appended to its label if set.
- [x] Current node persists in `VSG.cn.<id>` on every node render, independent of `resume_on_return`.
- [x] `resume_on_return: true` + a relog mid-tree reopens at the last-rendered node, not `nodes[0]`.
- [x] `resume_on_return: false`/absent always restarts at `nodes[0]`, even with a saved node present.
- [x] Ending a multi-node conversation (any terminal choice) clears its `VSG.cn.<id>` save.
- [x] `vsg_reset <id>` and `vsg_reset all` clear `VSG.cn.*`; same for `vsg_reset_player`.
- [x] An entry without `conversation.nodes` is unaffected — exact Phase 2/3 flat behavior.
- [x] Build is clean: `Build succeeded. 0 Warning(s) 0 Error(s)`.
- [x] All choice button labels (flat choices, multi-quest picker, node choices) wrap and shrink-to-fit so text never overflows the button's width or height.
