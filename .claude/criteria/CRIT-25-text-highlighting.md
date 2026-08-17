# CRIT-25 — Text Highlighting & No-Truncation Layout

**Files:** `src/Display/TextHighlighter.cs`, `src/Config/GuidanceConfig.cs` (`HighlightSpec`),
`src/Display/NpcConversationPanel.cs`, `src/Display/RunePanel.cs`, `src/Display/GuidanceDisplay.cs`,
`src/Display/GuidanceHudTracker.cs`, `src/Display/GuidanceCodex.cs`

Two rules that belong together: authored text is never cut off, and the words that matter
inside it can be given their own colour.

---

## Part 1 — No display surface truncates authored text

`TextOverflowModes.Ellipsis` / `Truncate` are **banned on body text**. A guide author writing a
long paragraph must never discover that the last third of it silently vanished in game.

The pattern for a VSG-owned panel:

1. A `VerticalLayoutGroup` + `ContentSizeFitter (verticalFit = PreferredSize)` on the panel so
   its height follows its content, with the width fixed.
2. Body content inside a `ScrollRect` whose viewport carries a `LayoutElement`. Its
   `preferredHeight` is set per-message to `min(contentHeight, maxHeight)`.
3. `maxHeight` derived from `Screen.height * MaxHeightFraction` minus the chrome, so the panel
   grows to fit and only starts scrolling when it would outgrow the screen.
4. `overflowMode = Overflow` everywhere. Scrolling is how overflow is handled, not clipping.

| Surface | Behaviour |
|---|---|
| `conversation` | Bottom-pivot anchored above the lower edge, grows upward; body scrolls past 82% screen height. Header wraps. Choice buttons wrap onto multiple rows (3 across, or 2 across when there are more than 3) and each button grows to fit a wrapped label. |
| `rune` | Body + bullet list share one scroll viewport, capped at 86% screen height. |
| `message` + `position: Center` | Vanilla's centre text is one non-wrapping line. `EnsureCenterMessageWraps` turns wrapping on and widens the rect to 70% of screen width. It only ever *grows* the rect, so vanilla's own short centre messages are unchanged. The reward summary ("Received: …") goes through the same call before showing. |
| `message` + `position: TopLeft` | `EnsureTopLeftMessageWraps` turns wrap + Overflow on `MessageHud.m_messageText` (rect untouched) so a long line stacks instead of running off the edge. Also applied to the version-bump re-delivery in `GuidanceDispatcher`. |
| `raven`, `intro` | `EnsureVanillaTextNotTruncated` forces wrap + Overflow on `TextViewer.m_text` / `m_introText` / `m_ravenText`. These are fixed-size vanilla art and do **not** grow — long text spills past the parchment rather than being cut. Authors wanting long text should use `rune` or `conversation`. |
| `bubble` | Explicit world rect (14 × 6) so wrapping has a sane measure; Overflow. |
| Codex | Detail-pane title wraps, and `LayoutTitleArea` grows the title band (pushing the rule, both toggle pills and the body scroll down) so a multi-line title never paints over them. Body and "Upcoming Steps" scroll; upcoming rows wrap and show the step message in full (newlines flattened, nothing cut). The guide sidebar scrolls as one list; its rows wrap too: `MeasureQuestRow` measures each title at the 174px label width and the row's `LayoutElement` is pinned to that height, so a two-line title gets a two-line row. |
| Tracker (F10) | Rows wrap at the panel width and `SizeRow` re-measures each visible row on every `Refresh`, so the panel grows instead of ellipsising a long quest title. Description rows (Expand Full view) wrap too — `SizeRow` subtracts their TMP left margin from the wrap width so an indented line is measured against the width it actually has. Progress bars are wrapped in `<nobr>` so a bar never breaks across lines. The hover tooltip is content-sized with no line cap. |

### Fixed-height lists compress; they do not overflow

A `VerticalLayoutGroup` in a fixed-height container splits the available space between its
children rather than spilling past the bottom. With `childControlHeight = true` each child is
allocated between `LayoutElement.minHeight` and `preferredHeight`, and once the total preferred
exceeds the container **everything shrinks toward min**. A `LayoutElement` that sets only
`preferredHeight` has an implicit min of 0, so those rows collapse to invisible slivers while
siblings that set `minHeight` hold their size.

This is how the Codex sidebar broke: category headers (`minHeight = 22`) stayed put while quest
rows (`preferredHeight = 20`, min unset) squeezed to nothing, rendering as ragged blank gaps
between category names. It surfaced only once a player had enough visible quests to overflow the
panel, and it logs nothing — the layout is behaving as designed.

Any VSG list that can grow past its container therefore needs **both**:

1. Explicit `minHeight` *and* `preferredHeight` on every row's `LayoutElement`.
2. Either a `ScrollRect` + viewport (`RectMask2D`) + `ContentSizeFitter (verticalFit =
   PreferredSize)` with content top-anchored at pivot `(0.5, 1)`, **or** pagination that caps
   how much goes into the container in the first place.

The Codex guide sidebar takes option 2's first branch: `BuildItems` flattens every category into
one `ListItem` list and `RenderList` drops the lot into a `ScrollRect` whose content is fitted to
its rows. Quest rows wrap, so each row's height is **measured** (`MeasureQuestRow`, floor
`QuestRowHeight`) and `ListItem` carries that height through to the `LayoutElement` the row builds
— the two can therefore never drift apart. Category headers still use the fixed `CatRowHeight`. A
`RectMask2D` on the list clips; an auto-hiding 6px scrollbar down the right edge signals overflow.
(Before 0.12 this pane was paged instead — `[<] Page 2 / 4 [>]` — which is why `QuestLabelWidth`
and the per-row measurement exist.)

The viewport of every Codex scroll view carries an **invisible full-size `Image`**
(`raycastTarget = true`, alpha 0). A `ScrollRect` only receives the wheel when the pointer hits a
`Graphic` *inside* it, because the event bubbles up from the hit object; body text and row labels
are all `raycastTarget = false`, so without that catcher the wheel falls through to the panel
background and the view never scrolls.

Explicit per-row heights also keep the `ContentSizeFitter` off `TMP.preferredHeight`, which is
the zero-size-row trap that made an earlier attempt at scrolling the sidebar fail.

Clearing a list also needs care: `Destroy()` is deferred to end-of-frame and a layout group counts
any child still `activeInHierarchy`, so a clear-then-repopulate in one frame lays out the old rows
plus the new ones. `SetActive(false)` before `Destroy()`.

### A stretched child needs its offsets zeroed

A `RectTransform` added in code starts at **100 × 100**, and with a horizontal stretch anchor the
width is `parent width + sizeDelta.x`. A ScrollRect content rect that sets its anchors but not its
offsets is therefore 100px wider than its viewport and centred on it, so the body wraps at that
oversized width and the viewport's `RectMask2D` clips it symmetrically on **both** sides — text
that starts mid-word on the left and ends mid-word on the right. This is exactly how the rune
panel broke. Always set `offsetMin`/`offsetMax` (or `sizeDelta`) explicitly on a stretched rect;
never rely on the default.

Layout-group children are safe — the group drives their anchors and size — but anything the
layout does not own (scroll content, manually anchored rects) is not.

### Panel activation ordering

TMP's `GetPreferredValues` and `LayoutRebuilder` return zeroes for an inactive hierarchy. Panels
must therefore be activated *before* measuring, but *after* fonts are assigned (the TMP null-font
Awake warning rule). Order: resolve font → assign to static texts → `SetActive(true)` → set text
→ measure/clamp. Every measuring call site guards on `activeInHierarchy` and falls back to a
one-line height, so a measurement taken too early degrades instead of collapsing a row.

A layout rebuild fixes rects but does **not** re-flow glyphs: text assigned while the hierarchy was
inactive keeps the wrap points it computed against the old width. After activating, call
`SetAllDirty()` + `ForceMeshUpdate()` on the labels (`RunePanel.RefreshTextGeometry`,
`GuidanceCodex.ForceMeshUpdateIn`, the codex body in `ShowEntry`) and rebuild again.

A `LayoutElement` outranks TMP's own `preferredHeight` (priority 1 vs 0), so a wrapping row inside
a layout group only gets its extra lines if something writes the measured height back onto the
`LayoutElement` — that is what `SizeRow` (tracker) and `MeasureRow` (codex) are for.

---

## Part 2 — `highlight:`

```yaml
highlight:
  - text: "Communion Totem"     # the phrase; use this or `any`
    any: ["[F7]", "[E]"]        # several phrases sharing one style
    color: "#FFCC55"            # #RRGGBB or #RRGGBBAA, leading '#' optional
    style: "Bold Italic"        # Bold | Italic | Underline | Strikethrough, combinable
    size_percent: 120           # font size for the span, % of surrounding text. 0 = unchanged
    first: true                 # only the first occurrence (default: every one)
    match_case: true            # default false
    whole_word: false           # default: auto (see below)
```

Emitted as TMP rich text, so one implementation covers every in-game surface.

### Scope and precedence

| Level | Applies to |
|---|---|
| Root of any YAML file (`GuidanceConfig.Highlight`) | Every entry, server-wide. **Lists from every loaded file are concatenated** — unlike `tracker:`, where the first file wins. |
| `GuidanceEntry.Highlight` | That entry's text |
| `GuidanceStep.Highlight` | That step's text |

Rules are collected step → entry → global, so the most specific wins any overlap.

### Matching

- **Auto whole-word**: a phrase that starts *and* ends with a letter or digit matches on word
  boundaries, so `Guard` never lights up inside `Guardian`. Anything else — `[F7]`, `(Locked`,
  `—` — matches as a plain substring. `whole_word` overrides either way.
- **Case-insensitive** unless `match_case: true`.
- Order matters: list a longer phrase before a shorter one it contains.

### Two invariants that keep the output renderable

1. **Matching never looks inside an existing rich-text tag.** The input is split into plain and
   markup segments first, so an author's own `<color>` (or one produced by a token) is never
   matched into and never corrupted. Text *between* tags is still matchable.
2. **A highlighted span is locked.** Later rules skip it, so tags never nest or interleave into
   something TMP cannot parse. This is also why the first matching rule wins.

A `<` that is not followed by a `>` before the next `<` is treated as literal text, so
`5 < 10` survives.

### Discord

`TextHighlighter` runs on **display paths only**. `announce.discord`, `discord_on_complete`, and
the `discord` reward all keep calling `TemplateText` directly — a webhook post carrying
`<color=#FFCC55>` would show the literal markup. The `chat_message` reward is the one reward that
does get highlighting, via `RewardDispatcher.Grant`'s separate optional `highlight` callback.

### Render pipeline

```csharp
// Template first, highlight second: rules can match text a token produced
// (a player name, a creature name) and never see the tokens themselves.
GuidanceDispatcher.RenderDisplay(entry, step, template, evt, playerName, stepNum, total)
GuidanceDispatcher.RenderLocal(entry, template, step)   // non-fire-path surfaces, CRIT-13
```

### Performance

Compiled open/close tag strings are cached per `HighlightSpec` instance in a
`ConditionalWeakTable`. `Apply` runs on every render — a Codex selection, a tracker refresh, each
conversation node — so without the cache a malformed `color:` would re-log its warning forever
instead of once per config load. The table drops its entries when a YAML reload replaces the rule
objects.

---

## Criteria

- [ ] No text on any VSG surface uses `Ellipsis`/`Truncate` overflow — including tracker rows, Codex sidebar rows, Upcoming Steps rows and the tracker tooltip; long text wraps and the surface grows or scrolls.
- [ ] Every stretched (non-layout-owned) rect sets its offsets explicitly, so no body wraps at a width wider than its mask.
- [ ] Wrapping rows inside a layout group get their measured height written back to the `LayoutElement`.
- [ ] The conversation panel sizes to its text, grows upward from a fixed baseline, and scrolls past 82% of screen height.
- [ ] Conversation choice buttons wrap onto multiple rows and grow to fit a wrapped label.
- [ ] The rune panel scrolls past 86% of screen height rather than growing off-screen.
- [ ] `message` with `position: Center` word-wraps and never widens the vanilla rect beyond need.
- [ ] Panels are activated before any `GetPreferredValues` / `LayoutRebuilder` call, and after fonts are assigned.
- [ ] Every row in a growable list sets `minHeight` as well as `preferredHeight`, so a full list cannot compress rows to invisibility.
- [ ] The Codex guide sidebar and the Upcoming Steps section scroll; neither compresses when the entry count outgrows the panel.
- [ ] The Codex title band grows for a multi-line title and the divider, toggle pills and body scroll move down with it.
- [ ] The guide sidebar's scrollbar appears only when the list overflows, and the wheel scrolls the list wherever the pointer sits inside it (including the gaps between rows).
- [ ] The mouse wheel does not zoom the game camera or cycle the hotbar while the Codex is open.
- [ ] List containers are cleared with `SetActive(false)` before `Destroy()` so a same-frame repopulate does not lay out stale rows.
- [ ] `highlight:` works at root, entry and step level; step beats entry beats root.
- [ ] Root-level `highlight:` lists from all YAML files are merged, not first-file-wins.
- [ ] Word-like phrases match on word boundaries automatically; punctuation-bounded phrases match literally.
- [ ] A highlighted span is never re-highlighted, and matching never occurs inside an existing tag.
- [ ] An invalid `color:` is dropped with a single warning, never emitted as a broken tag.
- [ ] Discord messages never contain highlight markup.
