# Phase 04f — Three-State F10 View Cycle

**Status:** `complete`
**Supersedes:** the two-state open/close toggle in [phase-04b](phase-04b-hotkey-toggle.md) and the
badge anchoring described there.

F10 no longer toggles; it **cycles** through three views and wraps:

```
Collapsed  ──F10──▶  Titles  ──F10──▶  Full  ──F10──▶  Collapsed
```

| View | On screen |
|---|---|
| `Collapsed` | Badge only. |
| `Titles` | Badge + `GUIDES` + one row per pinned quest (title and progress bar). The previous "expanded" state. |
| `Full` | Titles plus each quest's description under its row. |

```
Collapsed                          Titles                             Full
─────────────────────────          ─────────────────────────          ─────────────────────────
Show Quests (2) [F10]              Show Desc [F10]                    Hide Quests [F10]
drag to move (…)                   drag to move (…)                   drag to move (…)
                                   GUIDES                             GUIDES
                                   > Quest 1  [====] 1/4              > Quest 1  [====] 1/4
                                   > Quest 2  [==] 1/2                  Do this and go there.
                                                                      > Quest 2  [==] 1/2
                                                                        Go here and do that.
```

`Full` exists so the objective is readable **without** freeing the cursor to hover a row. The
hover tooltip therefore only runs in `Titles`: in `Full` it would repeat what is already printed,
and `Collapsed` has nothing to hover.

The view resets to `Collapsed` each session (as the old hidden flag did). Pinning a quest in the
Codex opens the panel to at least `Titles`, but never pulls a player who is reading descriptions
back down to titles.

---

## Badge: says what the key does NEXT

The badge label is an instruction, not a status. A player who has never pressed F10 reads
`Show Quests [F10]` and learns both that the key does something and what it will do.

| View | Label |
|---|---|
| `Collapsed` | `Show Quests [F10]` — or `Show Quests (N) [F10]` while N quests are pinned |
| `Titles` | `Show Desc [F10]` |
| `Full` | `Hide Quests [F10]` |

The pinned count appears **only** while collapsed: that is the one state where the badge cannot
otherwise show that there is something worth opening.

A second, dimmer line under the label carries the drag hint —
`drag to move (with inventory open)`. It names the modifier deliberately: dragging requires a free
cursor (`CursorFreeForDrag` — inventory or ESC menu open), so "drag to move" alone would be advice
the player cannot act on.

---

## The badge now rides on the panel

The badge used to be anchored independently (same corner, `OffsetY − 40`), so dragging the panel
left it stranded at the screen corner. That was survivable when it was a corner hint; it is not,
now that the badge is the widget's title bar and the thing that says "drag to move".

`PositionBadge()` borrows the panel's anchor **and pivot**, then places the badge's bottom edge
`BadgeGap` above the panel's top edge:

```csharp
var panelTop = panelRect.anchoredPosition.y + (1f - pivotY) * panelH;
badgeRect.anchoredPosition = new Vector2(panelRect.anchoredPosition.x,
                                         panelTop + BadgeGap + pivotY * badgeH);
```

Borrowing the pivot makes the maths pivot-agnostic (it is correct for a top, bottom, or centre
pivot) and keeps the two boxes edge-aligned — right edges for a right anchor, left for a left one.
A hidden panel is treated as zero-height so the badge sits where the panel's top edge would be
rather than floating above empty space. Called after every layout rebuild, and from
`ApplyCustomPos` so the badge tracks a live drag frame by frame.

**Custom-position pivot changed** from centre `(0.5, 0.5)` to top-centre `(0.5, 1)`, so a dragged
panel grows downward from where it was dropped instead of expanding in both directions and
see-sawing the badge with every progress tick. `CornerPosToCenter` now converts the panel's
top-centre point to match. A position saved by an older build therefore lands about half a panel
height lower on first load; the next drag re-saves it correctly.

Either box is a drag handle (`DragHandleHit`) — necessary because in `Collapsed` the badge is the
only thing on screen.

---

## Description rows

`BuildPanel` creates a description label immediately after each quest row, so the
`VerticalLayoutGroup` interleaves them (row, description, row, description…). Description rows:

- are hidden outside `Full`, and hidden in `Full` for any quest that has no description;
- render at `font_size − 2`, dimmer, with a `DescIndent` (14px) TMP left margin so they read as
  belonging to the row above;
- wrap and are measured by `SizeRow`, which subtracts the margin from the wrap width (CRIT-25).

### Where the text comes from — `RowDescription(entry, step)`

1. `step.description` (chain entries — the current step's objective), else
2. `entry.description` (**new field**, see CRIT-01).

Newlines are flattened to spaces: the row wraps on its own, and a YAML block scalar's hard breaks
would otherwise punch odd gaps into the panel.

`item_acquired` goal entries keep using `BuildGoalProgressText` (the per-item breakdown *is* the
objective there) and fall back to the description only when there is nothing to break down.

> **`entry.description` was already being written and silently dropped.** `GuidanceEntry` had no
> `Description` property, and the loader runs `IgnoreUnmatchedProperties()`, so the 25 entry-level
> `description:` fields across `guidance.valcoin-quests.yaml` and `hearthbound_guides.yaml` parsed
> to nothing. Adding the property makes that authored content live. Without it, non-chain quests
> (kill counts, item submits) would show a title and a bar in `Full` view and never say what to do.

---

## Files Changed

| File | Change |
|---|---|
| `src/Display/GuidanceHudTracker.cs` | `TrackerView` enum replacing `_userHidden`; `CycleView()`; `_rowDescTexts` interleaved rows; `RowDescription()`; next-action badge label + `_badgeHintText` drag line; `PositionBadge()`; `DragHandleHit()`; top pivot in `ApplyCustomPos`; tooltip limited to `Titles` |
| `src/Config/GuidanceConfig.cs` | `GuidanceEntry.Description` |

---

## Criteria

- [ ] F10 cycles Collapsed → Titles → Full → Collapsed and wraps.
- [ ] The badge label names the NEXT action in every view (`Show Quests` / `Show Desc` / `Hide Quests`).
- [ ] The pinned count shows only in the Collapsed label, and only when at least one quest is pinned.
- [ ] A dimmer second badge line states that the panel can be dragged, and names the inventory-open requirement.
- [ ] Full view prints each pinned quest's description under its row, wrapped and indented, never truncated.
- [ ] A quest with no description shows no second line (no blank gap).
- [ ] `entry.description` is honoured for non-chain quests; `step.description` still wins for chains.
- [ ] The hover tooltip appears in Titles only.
- [ ] The badge stays glued above the panel at every anchor, in every view, and while dragging.
- [ ] Dragging works by grabbing either the panel or the badge, and only with the cursor free.
- [ ] Pinning from the Codex opens Collapsed → Titles, and leaves Full alone.
- [ ] View resets to Collapsed on a new session.
