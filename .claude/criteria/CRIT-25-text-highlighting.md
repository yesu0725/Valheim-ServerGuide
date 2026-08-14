# CRIT-25 — Text Highlighting & No-Truncation Layout

**Files:** `src/Display/TextHighlighter.cs`, `src/Config/GuidanceConfig.cs` (`HighlightSpec`),
`src/Display/NpcConversationPanel.cs`, `src/Display/RunePanel.cs`, `src/Display/GuidanceDisplay.cs`

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
| `message` + `position: Center` | Vanilla's centre text is one non-wrapping line. `EnsureCenterMessageWraps` turns wrapping on and widens the rect to 70% of screen width. It only ever *grows* the rect, so vanilla's own short centre messages are unchanged. |
| `raven`, `intro` | `EnsureVanillaTextNotTruncated` forces wrap + Overflow on `TextViewer.m_text` / `m_introText` / `m_ravenText`. These are fixed-size vanilla art and do **not** grow — long text spills past the parchment rather than being cut. Authors wanting long text should use `rune` or `conversation`. |
| `bubble` | Explicit world rect (14 × 6) so wrapping has a sane measure; Overflow. |
| Codex | Detail-pane title wraps. Body and "Upcoming Steps" scroll. The guide sidebar is **paged** rather than scrolled (`[<] Page N / M [>]`), so no row is ever clipped or compressed. Sidebar rows keep `Ellipsis` **by design** — they are navigation, and the full title is shown in the detail pane. |

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

The Codex guide sidebar uses pagination: `PaginateItems` measures the list rect (falling back to
`FallbackListHeight` when the rect has not resolved yet — a freshly activated hierarchy reports
zero) and fills pages with `CatRowHeight`/`QuestRowHeight`/`RowSpacing` until the next row would
not fit. A category spanning a page break repeats its header on the following page. The row
metric constants must stay in sync with the `LayoutElement` heights the rows actually set, or a
page can overflow. A `RectMask2D` on the list clips as a last resort.

Explicit per-row heights also keep the `ContentSizeFitter` off `TMP.preferredHeight`, which is
the zero-size-row trap that made an earlier attempt at scrolling the sidebar fail.

Clearing a list also needs care: `Destroy()` is deferred to end-of-frame and a layout group counts
any child still `activeInHierarchy`, so a clear-then-repopulate in one frame lays out the old rows
plus the new ones. `SetActive(false)` before `Destroy()`.

### Panel activation ordering

TMP's `GetPreferredValues` and `LayoutRebuilder` return zeroes for an inactive hierarchy. Panels
must therefore be activated *before* measuring, but *after* fonts are assigned (the TMP null-font
Awake warning rule). Order: resolve font → assign to static texts → `SetActive(true)` → set text
→ measure/clamp.

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

- [ ] No body text anywhere uses `Ellipsis`/`Truncate` overflow; long text scrolls instead.
- [ ] The conversation panel sizes to its text, grows upward from a fixed baseline, and scrolls past 82% of screen height.
- [ ] Conversation choice buttons wrap onto multiple rows and grow to fit a wrapped label.
- [ ] The rune panel scrolls past 86% of screen height rather than growing off-screen.
- [ ] `message` with `position: Center` word-wraps and never widens the vanilla rect beyond need.
- [ ] Panels are activated before any `GetPreferredValues` / `LayoutRebuilder` call, and after fonts are assigned.
- [ ] Every row in a growable list sets `minHeight` as well as `preferredHeight`, so a full list cannot compress rows to invisibility.
- [ ] The Codex guide sidebar pages and the Upcoming Steps section scrolls; neither compresses when the entry count outgrows the panel.
- [ ] A page never exceeds the measured list height, and page arrows grey out at the first/last page.
- [ ] A category split across a page boundary repeats its header on the next page.
- [ ] List containers are cleared with `SetActive(false)` before `Destroy()` so a same-frame repopulate does not lay out stale rows.
- [ ] `highlight:` works at root, entry and step level; step beats entry beats root.
- [ ] Root-level `highlight:` lists from all YAML files are merged, not first-file-wins.
- [ ] Word-like phrases match on word boundaries automatically; punctuation-bounded phrases match literally.
- [ ] A highlighted span is never re-highlighted, and matching never occurs inside an existing tag.
- [ ] An invalid `color:` is dropped with a single warning, never emitted as a broken tag.
- [ ] Discord messages never contain highlight markup.
