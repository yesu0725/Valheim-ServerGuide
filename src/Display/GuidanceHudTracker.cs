using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimServerGuide.Config;
using ValheimServerGuide.State;
using ValheimServerGuide.Triggers;

namespace ValheimServerGuide.Display
{
    /// Persistent HUD widget that shows the player's active guide chains and their progress.
    /// Uses vanilla UI components only (Image, TextMeshProUGUI, LayoutGroup) and the game's own
    /// TMP font asset (AveriaSansLibre). No custom assets. See CRIT-14.
    ///
    /// Layout is live-tunable from guidance.yaml via the `tracker:` section (anchor, offsets,
    /// width, font size). ApplyLayout() re-applies those on every YAML reload, so the box can
    /// be repositioned in-game without a restart.
    ///
    /// Lifecycle:
    ///   • BuildPanel()  — called once from HudAwakePatch.Postfix to create the main tracker.
    ///   • BuildBadge()  — called once from HudAwakePatch.Postfix to create the hint badge.
    ///   • ApplyLayout() — positions/sizes/fonts both panel and badge from current config (live).
    ///   • Refresh()     — repaints rows after every chain state change.
    ///   • Update()      — per-frame: intro-cinematic hide; hotkey toggle poll.
    public class GuidanceHudTracker : MonoBehaviour
    {
        public static GuidanceHudTracker Instance { get; internal set; }

        // ASCII-only markers — Valheim's font lacks the ▸/▌ geometric glyphs (they render as □).
        private const string RowPrefix = "> ";
        /// Gap between the bottom of the badge and the top of the panel, in pixels.
        private const float BadgeGap = 6f;
        /// Left indent (TMP margin) on a description line, so it reads as belonging to the row above.
        private const float DescIndent = 14f;

        /// How much of the tracker is on screen. F10 cycles forward through these and wraps.
        public enum TrackerView
        {
            /// Badge only — the panel is down.
            Collapsed = 0,
            /// Badge + one row per pinned quest (title and progress bar).
            Titles = 1,
            /// Titles plus each quest's description underneath, so the objective is readable
            /// without hovering a row with a freed cursor.
            Full = 2,
        }

        // ── Main tracker panel ────────────────────────────────────────────────────────────────
        private GameObject _panel;
        private RectTransform _panelRect;
        private TMP_Text _headerText;
        private readonly List<TMP_Text> _rowTexts = new List<TMP_Text>();
        /// One per row in _rowTexts, built immediately after it so the layout group interleaves
        /// them (row, description, row, description…). Only shown in TrackerView.Full.
        private readonly List<TMP_Text> _rowDescTexts = new List<TMP_Text>();
        private TMP_Text _overflowText;
        private TMP_FontAsset _font;
        private int _builtMaxVisible;

        // ── Hotkey cycle + badge ──────────────────────────────────────────────────────────────
        // The panel shows the set of quests the player has pinned from the Guide Codex
        // (TrackedQuestState). It starts Collapsed each session; F10 cycles
        // Collapsed → Titles → Full → Collapsed, and pinning a quest in the Codex opens it to at
        // least Titles. The panel never captures input or the cursor.
        private TrackerView _view = TrackerView.Collapsed;
        private GameObject _badgePanel;
        private TMP_Text _badgeText;
        /// Second badge line: the drag-to-move hint.
        private TMP_Text _badgeHintText;

        // ── Drag-to-move ──────────────────────────────────────────────────────────────────────
        // The panel can be dragged anywhere, but only while the cursor is free (inventory or the
        // ESC menu open). Once moved, the custom position is persisted and overrides the config anchor.
        private bool _dragging;
        private Vector2 _dragMouseStart;
        private Vector2 _dragPanelStart;
        private bool _hasCustomPos;
        private Vector2 _customPos;

        // ── Row highlights ────────────────────────────────────────────────────────────────────
        private CanvasGroup _panelGroup;
        private float[] _rowHighlightTimers;

        // ── Phase 04d: hover tooltips ─────────────────────────────────────────────────────────
        private GameObject _tooltipPanel;
        private TMP_Text _tooltipText;
        private RectTransform _tooltipRect;
        private int _hoveredRowIndex = -1;
        private readonly List<string> _rowDescriptions = new List<string>();

        // ── Phase 04e: completion flash & progress bars ───────────────────────────────────────
        private readonly Dictionary<string, float> _completingRows = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _completingRowIdx = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _completingRowText = new Dictionary<string, string>();
        private readonly List<string> _rowChainIds = new List<string>();

        // Dedicated root canvas for all tracker UI. A nested canvas under Hud cannot draw above
        // Valheim's inventory/crafting panels (they share Hud's root canvas plane). Our own root
        // canvas at a high sortingOrder renders globally on top of them.
        private GameObject _uiRoot;

        // ── Construction ──────────────────────────────────────────────────────────────────────

        /// Lazily creates (and returns) the dedicated root canvas all tracker UI parents to.
        /// Sits above the inventory/crafting UI via a high sortingOrder. Copies Hud's CanvasScaler
        /// so our pixel-anchored offsets line up with the rest of the HUD at any resolution.
        private Transform UiRoot()
        {
            if (_uiRoot != null) return _uiRoot.transform;

            _uiRoot = new GameObject("VSG_TrackerRoot");
            var canvas = _uiRoot.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiLayers.Tracker;

            var srcScaler = Hud.instance != null
                ? Hud.instance.GetComponentInParent<Canvas>()?.GetComponent<CanvasScaler>()
                : null;
            var scaler = _uiRoot.AddComponent<CanvasScaler>();
            if (srcScaler != null)
            {
                scaler.uiScaleMode            = srcScaler.uiScaleMode;
                scaler.referenceResolution    = srcScaler.referenceResolution;
                scaler.screenMatchMode        = srcScaler.screenMatchMode;
                scaler.matchWidthOrHeight     = srcScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = srcScaler.referencePixelsPerUnit;
                scaler.scaleFactor            = srcScaler.scaleFactor;
            }

            // Needed so the click-outside overlay's Button receives pointer events on this canvas.
            _uiRoot.AddComponent<GraphicRaycaster>();
            return _uiRoot.transform;
        }

        /// Called from HudAwakePatch immediately after the tracker GameObject is added to the scene.
        public void BuildPanel()
        {
            // _font intentionally left null here — TMP fonts are not loaded during Hud.Awake.
            // Lazy resolution happens in Refresh() once assets are available. Assigning a null
            // font now would trigger a "LiberationSans SDF Font Asset was not found" warning for
            // every text row created.
            _builtMaxVisible = Plugin.TrackerMaxVisible?.Value ?? 3;

            _panel = new GameObject("VSG_TrackerPanel");
            _panel.transform.SetParent(UiRoot(), worldPositionStays: false);

            _panelRect = _panel.AddComponent<RectTransform>();

            var bg = _panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            var layout = _panel.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth      = true;
            layout.childForceExpandWidth  = true;
            layout.childControlHeight     = true;
            layout.childForceExpandHeight = false;
            layout.padding  = new RectOffset(8, 8, 4, 4);
            layout.spacing  = 1f;

            var fitter = _panel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            _panelGroup = _panel.AddComponent<CanvasGroup>();

            // No nested Canvas here: the panel renders directly on VSG_TrackerRoot (sortingOrder
            // 1000), which already sits above the inventory/crafting UI. A nested canvas with
            // overrideSorting would sort globally by its own (lower) order and hide the panel
            // behind other HUD layers.

            // Deactivate BEFORE creating text children — while the panel is inactive,
            // child TextMeshProUGUI components never run OnEnable, so TMP does not attempt
            // a mesh render against the (still-null) font → LiberationSans warning avoided.
            _panel.SetActive(false);

            _headerText = MakeText("GUIDES", style: FontStyles.Bold,
                color: new Color(1f, 0.82f, 0.42f), rowHeight: 15f);

            // Row and its description are created as a pair so the VerticalLayoutGroup renders
            // them adjacent — the description belongs to the row directly above it.
            _rowTexts.Clear();
            _rowDescTexts.Clear();
            for (var i = 0; i < _builtMaxVisible; i++)
            {
                _rowTexts.Add(MakeText("", style: FontStyles.Normal, color: Color.white, rowHeight: 14f));

                var desc = MakeText("", style: FontStyles.Normal,
                    color: new Color(0.74f, 0.72f, 0.66f), rowHeight: 12f);
                desc.margin = new Vector4(DescIndent, 0f, 0f, 2f);
                desc.gameObject.SetActive(false); // Titles view hides these
                _rowDescTexts.Add(desc);
            }
            _rowHighlightTimers = new float[_builtMaxVisible];

            _overflowText = MakeText("", style: FontStyles.Italic,
                color: new Color(0.75f, 0.75f, 0.75f), rowHeight: 13f);

            ApplyLayout();
        }

        /// Called from HudAwakePatch after BuildPanel. Creates the always-visible badge that sits
        /// directly above the panel: line 1 says what F10 will do next (and, while collapsed, how
        /// many quests are pinned), line 2 is the drag-to-move hint. It is the widget's title bar
        /// and its only affordance while the panel is collapsed.
        public void BuildBadge()
        {
            _badgePanel = new GameObject("VSG_TrackerBadge");
            _badgePanel.transform.SetParent(UiRoot(), worldPositionStays: false);
            _badgePanel.AddComponent<RectTransform>();

            var bg = _badgePanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;

            // Vertical, not horizontal: the badge carries two stacked lines — the label telling
            // the player what F10 will do NEXT, and the drag-to-move hint beneath it.
            var layout = _badgePanel.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth      = true;
            layout.childForceExpandWidth  = false;
            layout.childControlHeight     = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment         = TextAnchor.UpperLeft;
            layout.spacing                = 1f;
            layout.padding = new RectOffset(6, 6, 3, 3);

            var fitter = _badgePanel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // Start hidden — same null-font guard as the main panel.
            _badgePanel.SetActive(false);

            _badgeText = MakeBadgeLine("VSG_BadgeText", "Show Quests [F10]", 10f,
                new Color(0.85f, 0.85f, 0.85f, 1f), FontStyles.Normal);
            _badgeHintText = MakeBadgeLine("VSG_BadgeHint", DragHintText, 9f,
                new Color(0.62f, 0.60f, 0.56f, 1f), FontStyles.Italic);

            ApplyBadgeLayout();
        }

        /// The hint under the badge label. Names the modifier that actually makes dragging
        /// possible — the cursor is captured for mouse-look otherwise, so "just drag it" would
        /// be advice the player cannot follow.
        private const string DragHintText = "drag to move (with inventory open)";

        private TMP_Text MakeBadgeLine(string name, string content, float size, Color color, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_badgePanel.transform, worldPositionStays: false);
            go.AddComponent<LayoutElement>();

            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text               = content;
            t.fontStyle          = style;
            t.color              = color;
            t.fontSize           = size;
            t.alignment          = TextAlignmentOptions.Left;
            t.enableWordWrapping = false;
            t.overflowMode       = TextOverflowModes.Overflow;
            t.raycastTarget      = false;
            return t;
        }

        /// Builds the floating tooltip panel shown when hovering a chain row that has a description.
        /// Parented to UiRoot() so it renders on the same dedicated overlay canvas as the tracker.
        public void BuildTooltip()
        {
            _tooltipPanel = new GameObject("VSG_TrackerTooltip");
            _tooltipPanel.transform.SetParent(UiRoot(), worldPositionStays: false);

            _tooltipRect = _tooltipPanel.AddComponent<RectTransform>();

            var bg = _tooltipPanel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.08f, 0.06f, 0.88f);
            bg.raycastTarget = false;

            var layout = _tooltipPanel.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth      = true;
            layout.childForceExpandWidth  = true;
            layout.childControlHeight     = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(6, 6, 4, 4);

            var fitter = _tooltipPanel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            _tooltipPanel.SetActive(false);

            var textGo = new GameObject("VSG_TooltipText");
            textGo.transform.SetParent(_tooltipPanel.transform, worldPositionStays: false);

            var le = textGo.AddComponent<LayoutElement>();
            le.preferredWidth = 280f;
            le.flexibleWidth  = 0f;

            _tooltipText = textGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) _tooltipText.font = _font;
            _tooltipText.fontSize           = 13f;
            _tooltipText.color              = new Color(0.9f, 0.88f, 0.82f);
            _tooltipText.alignment          = TextAlignmentOptions.TopLeft;
            _tooltipText.enableWordWrapping = true;
            // No line cap and no truncation: the tooltip panel is content-sized on both axes, so
            // a long step description grows the box instead of losing its tail (CRIT-25).
            _tooltipText.overflowMode       = TextOverflowModes.Overflow;
            _tooltipText.raycastTarget      = false;
        }

        /// Locate Valheim's main UI font (AveriaSansLibre/AveriaSerifLibre) so the tracker
        /// matches the game. A blind GetComponentInChildren can grab a hidden element using an
        /// unrelated fallback font, so we explicitly scan loaded TMP_FontAssets for Averia first.
        internal static TMP_FontAsset FindVanillaFontStatic() => FindVanillaFont();

        /// The vanilla font this tracker resolved during its first successful Refresh.
        /// Null until the tracker has run at least one Refresh with assets loaded.
        /// The codex reuses this so it doesn't have to re-resolve (and risk a null) on its own.
        internal TMP_FontAsset ResolvedFont => _font;

        private static TMP_FontAsset FindVanillaFont()
        {
            TMP_FontAsset fallback = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (f == null) continue;
                if (fallback == null) fallback = f;
                if (f.name.IndexOf("Averia", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return f;
            }
            if (Hud.instance != null)
            {
                var existing = Hud.instance.GetComponentInChildren<TMP_Text>(includeInactive: true);
                if (existing != null && existing.font != null) return existing.font;
            }
            return fallback ?? TMP_Settings.defaultFontAsset;
        }

        private void ApplyFontToAll(TMP_FontAsset font)
        {
            if (_headerText != null) _headerText.font = font;
            foreach (var t in _rowTexts) if (t != null) t.font = font;
            foreach (var t in _rowDescTexts) if (t != null) t.font = font;
            if (_overflowText != null) _overflowText.font = font;
            if (_badgeText != null) _badgeText.font = font;
            if (_badgeHintText != null) _badgeHintText.font = font;
            if (_tooltipText != null) _tooltipText.font = font;
        }

        /// Expand {playerName}/{player_name}/{biome}/… and apply `highlight:` rules to the
        /// titles and step descriptions the tracker renders — these come straight from YAML and
        /// are never run through the dispatcher's fire-path templating.
        private static string Template(string text, GuidanceEntry entry, GuidanceStep step = null)
            => GuidanceDispatcher.RenderLocal(entry, text, step) ?? "";

        /// The objective line for a row: the current step's `description:` when the entry is a
        /// chain, else the entry-level one. Non-chain quests (kill counts, item submits) have no
        /// step to carry it, so without the entry-level fallback they would show a title and a
        /// bar and never say what the player is meant to do.
        private static string RowDescription(GuidanceEntry entry, GuidanceStep step)
        {
            var text = step?.Description;
            if (string.IsNullOrEmpty(text)) text = entry?.Description;
            if (string.IsNullOrEmpty(text)) return null;
            // Flattened to one logical line: the row wraps on its own, and a YAML block scalar's
            // hard newlines would otherwise punch odd gaps into the panel.
            return Template(text, entry, step).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        /// Fixed-width "ghost bar" progress indicator using TMP rich-text color tags so the
        /// bracket width never changes as the counter advances (plain space-padding looks
        /// uneven in a proportional font). Bright filled segments, dark-gray ghost segments.
        private static string ProgressBar(int cur, int goal)
        {
            if (goal <= 0) return cur + "/" + goal;
            var width = Mathf.Clamp(goal, 1, 12);
            var filled = Mathf.Clamp(Mathf.RoundToInt((float)cur / goal * width), 0, width);
            // <nobr> keeps the bar and its counter together: rows wrap now, and a line break in
            // the middle of the bar would read as two broken half-bars.
            return "<nobr>[<color=#FFE6A8>" + new string('=', filled) +
                   "</color><color=#555555>" + new string('=', width - filled) +
                   "</color>] " + cur + "/" + goal + "</nobr>";
        }

        private TMP_Text MakeText(string content, FontStyles style, Color color, float rowHeight)
        {
            var go = new GameObject("VSG_T");
            go.transform.SetParent(_panel.transform, worldPositionStays: false);

            var le = go.AddComponent<LayoutElement>();
            le.minHeight       = rowHeight;
            le.preferredHeight = rowHeight;
            le.flexibleWidth   = 1f;

            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text               = content;
            t.fontStyle          = style;
            t.color              = color;
            t.alignment          = TextAlignmentOptions.TopLeft;
            // Long quest titles wrap onto as many lines as they need and the panel grows to fit
            // (SizeRow measures each row). Ellipsis is banned here — no VSG surface truncates
            // authored text (CRIT-25).
            t.enableWordWrapping = true;
            t.overflowMode       = TextOverflowModes.Overflow;
            t.raycastTarget      = false;
            return t;
        }

        /// Panel width available to a row: the configured width minus the layout group's
        /// left + right padding. Rows wrap at this measure.
        private static float RowInnerWidth() => Mathf.Max(40f, Mathf.Max(60f, EffectiveSpec().Width) - 16f);

        /// Pin a row's LayoutElement to the height its text actually needs once wrapped at the
        /// panel width. Rows word-wrap, so the fixed one-line height SetRow seeds would clip
        /// every wrapped line after the first (a LayoutElement outranks TMP's own preferred
        /// height, so the VerticalLayoutGroup would never grow the row on its own).
        private static void SizeRow(TMP_Text t)
        {
            if (t == null) return;
            var le = t.GetComponent<LayoutElement>();
            if (le == null) return;

            var oneLine = Mathf.Ceil(t.fontSize * 1.45f);
            var height  = oneLine;
            // Only measure a live label: TMP's metrics need the component to have awoken, which
            // it has not while the panel is still being built. Refresh() re-measures every row it
            // shows, so a row that skips the measurement here gets its real height before it is
            // ever visible. Description rows carry a left margin, which eats into the width the
            // text has to wrap in — measure against what is actually left.
            if (!string.IsNullOrEmpty(t.text) && t.font != null && t.gameObject.activeInHierarchy)
            {
                var width = Mathf.Max(40f, RowInnerWidth() - t.margin.x - t.margin.z);
                height = Mathf.Ceil(t.GetPreferredValues(t.text, width, 0f).y) + 2f;
            }

            le.minHeight       = Mathf.Max(oneLine, height);
            le.preferredHeight = le.minHeight;
        }

        // ── Live layout ───────────────────────────────────────────────────────────────────────

        /// Re-apply position, width, and font size from the current config. Safe to call any
        /// time (e.g. from Plugin.OnConfigChanged on YAML reload). Falls back to BepInEx config
        /// when the YAML `tracker:` section is absent.
        public void ApplyLayout()
        {
            if (_panelRect == null) return;

            var spec = EffectiveSpec();

            _panelRect.sizeDelta = new Vector2(Mathf.Max(60f, spec.Width), 0f);
            if (_hasCustomPos)
                ApplyCustomPos();
            else
                ApplyAnchor(_panelRect, spec.Anchor, spec.OffsetX, spec.OffsetY);

            var fs = Mathf.Max(6f, spec.FontSize);
            // Font size AND row height scale together. The LayoutElement.preferredHeight set in
            // MakeText is just a seed — here we recompute each row's height from the live font
            // size so larger fonts (e.g. font_size: 16) are not vertically clipped by a fixed
            // row height. A ~1.45x line box leaves room for ascenders/descenders.
            if (_headerText != null)   SetRow(_headerText,   fs + 1f);
            foreach (var t in _rowTexts) if (t != null) SetRow(t, fs);
            foreach (var t in _rowDescTexts) if (t != null) SetRow(t, Mathf.Max(6f, fs - 2f));
            if (_overflowText != null) SetRow(_overflowText, fs - 1f);

            ApplyBadgeLayout();
        }

        /// Apply a font size to a text row and resize its LayoutElement height to match, so the
        /// glyphs are never clipped vertically by a stale fixed row height. SizeRow then grows
        /// the row further when the text wraps onto more than one line.
        private static void SetRow(TMP_Text t, float fontSize)
        {
            t.fontSize = fontSize;
            var le = t.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minHeight       = Mathf.Ceil(fontSize * 1.45f);
                le.preferredHeight = le.minHeight;
            }
            SizeRow(t);
        }

        private void ApplyBadgeLayout()
        {
            if (_badgePanel == null) return;

            var spec = EffectiveSpec();
            if (_badgeText != null)
                _badgeText.fontSize = Mathf.Max(6f, spec.FontSize - 1f);
            if (_badgeHintText != null)
                _badgeHintText.fontSize = Mathf.Max(5f, spec.FontSize - 4f);

            PositionBadge();
        }

        /// Park the badge directly above the panel, whatever the panel's anchor or dragged
        /// position is. The badge used to be anchored independently (same corner, OffsetY − 40),
        /// which meant dragging the panel left it stranded at the corner — unacceptable now that
        /// the badge is the widget's title bar and advertises "drag to move".
        ///
        /// It borrows the panel's anchor and pivot, so the maths is pivot-agnostic and the two
        /// boxes stay edge-aligned (right edges for a right anchor, left for a left one).
        private void PositionBadge()
        {
            if (_badgePanel == null || _panelRect == null) return;
            var badgeRect = _badgePanel.GetComponent<RectTransform>();
            if (badgeRect == null) return;

            badgeRect.anchorMin = _panelRect.anchorMin;
            badgeRect.anchorMax = _panelRect.anchorMax;
            badgeRect.pivot     = _panelRect.pivot;

            // A hidden panel keeps its last measured height; treat it as zero so the badge sits
            // where the panel's top edge would be, instead of floating above empty space.
            var panelH = _panel != null && _panel.activeSelf ? _panelRect.rect.height : 0f;
            var badgeH = badgeRect.rect.height;
            var pivotY = _panelRect.pivot.y;

            // Panel top edge, then place the badge's bottom edge BadgeGap above it.
            var panelTop = _panelRect.anchoredPosition.y + (1f - pivotY) * panelH;
            badgeRect.anchoredPosition = new Vector2(
                _panelRect.anchoredPosition.x,
                panelTop + BadgeGap + pivotY * badgeH);
        }

        /// Resolve effective layout settings: YAML `tracker:` section wins; otherwise derive
        /// from the BepInEx config, keeping TrackerSpec defaults for layout fields.
        private static TrackerSpec EffectiveSpec()
        {
            var t = Plugin.CurrentConfig?.Tracker;
            if (t != null) return t;
            return new TrackerSpec
            {
                Enabled = Plugin.TrackerEnabled?.Value ?? true,
                Anchor  = Plugin.TrackerPosition?.Value ?? "TopRight",
            };
        }

        private static bool EffectiveBadgeEnabled()
        {
            var yamlTracker = Plugin.CurrentConfig?.Tracker;
            if (yamlTracker != null) return yamlTracker.BadgeEnabled;
            return Plugin.TrackerBadgeEnabled?.Value ?? true;
        }

        private static void ApplyAnchor(RectTransform rect, string pos, float offX, float offY)
        {
            switch ((pos ?? "topright").ToLowerInvariant())
            {
                case "topleft":
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(offX, -offY);
                    break;
                case "bottomright":
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
                    rect.anchoredPosition = new Vector2(-offX, offY);
                    break;
                case "bottomleft":
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0f);
                    rect.anchoredPosition = new Vector2(offX, offY);
                    break;
                default: // TopRight
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                    rect.anchoredPosition = new Vector2(-offX, -offY);
                    break;
            }
        }

        /// Pin the panel to a player-chosen position. The ANCHOR is canvas-centre so the stored
        /// position is resolution-independent (and survives the CanvasScaler) the same way the
        /// drag math computes it; the PIVOT is the top edge so the panel grows downward from
        /// where the player dropped it. A centre pivot would make it expand in both directions as
        /// rows appear, dragging the badge above it up and down with every progress tick.
        private void ApplyCustomPos()
        {
            if (_panelRect == null) return;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot     = new Vector2(0.5f, 1f);
            _panelRect.anchoredPosition = _customPos;
            PositionBadge();
        }

        // ── Tracked-quest pins (Guide Codex toggle) ───────────────────────────────────────────

        /// Pin (tracked) or unpin a quest from the progress panel. Called by the Guide Codex
        /// toggle. Pinning force-unhides the panel (per spec); unpinning leaves the hidden state
        /// as-is. Persists via TrackedQuestState and repaints immediately.
        public void SetTracked(string entryId, bool tracked)
        {
            var player = Player.m_localPlayer;
            if (player == null || string.IsNullOrEmpty(entryId)) return;
            TrackedQuestState.SetTracked(player, entryId, tracked);
            // Pinning opens the panel far enough to see the quest land. Already-open views are
            // left alone — a player reading descriptions should not be dropped back to titles.
            if (tracked && _view == TrackerView.Collapsed) _view = TrackerView.Titles;
            Refresh();
        }

        /// True when the given quest is currently pinned to the progress panel.
        public static bool IsTracked(string entryId)
        {
            var player = Player.m_localPlayer;
            return player != null && TrackedQuestState.IsTracked(player, entryId);
        }

        // ── Refresh ───────────────────────────────────────────────────────────────────────────

        /// Rebuild the visible rows from the current config and player chain state.
        /// Only quests the player has pinned from the Guide Codex appear. Visibility is controlled
        /// by the player (F10 cycles _view); fromProgress only highlights changed rows.
        /// Safe to call at any time; exits early when the HUD is not ready.
        public void Refresh(bool fromProgress = false)
        {
            if (_panel == null) return;

            // Lazy font resolution — TMP fonts are not available during Hud.Awake, so we defer
            // until the first Refresh() call where they are guaranteed to be loaded.
            if (_font == null)
            {
                _font = FindVanillaFont();
                if (_font != null)
                {
                    ApplyFontToAll(_font);
                    Plugin.Log.LogInfo("[tracker] Font resolved: " + _font.name);
                }
                else
                {
                    // No font yet — keep everything hidden rather than activating text rows with
                    // a null font (which would log the LiberationSans warning).
                    _panel.SetActive(false);
                    HideTooltip();
                    RefreshBadge(0);
                    return;
                }
            }

            // Hide everything during intro cinematic (CRIT-07).
            if (GuidanceDisplay.IntroLockActive)
            {
                _panel.SetActive(false);
                HideTooltip();
                RefreshBadge(0);
                return;
            }

            // Enabled gate (YAML section wins, else BepInEx).
            if (!EffectiveSpec().Enabled)
            {
                _panel.SetActive(false);
                HideTooltip();
                RefreshBadge(0);
                return;
            }

            var player = Player.m_localPlayer;
            var config = Plugin.CurrentConfig;
            if (player == null || config?.Guidances == null)
            {
                _panel.SetActive(false);
                HideTooltip();
                RefreshBadge(0);
                return;
            }

            LoadCustomPos(player);

            // Build the row list: active (not complete) chains that have a title.
            // Only quests the player has pinned from the Guide Codex are shown.
            var rows = new List<string>();
            var descs = new List<string>();
            var rowChainIds = new List<string>();
            foreach (var entry in config.Guidances)
            {
                if (entry.Steps == null || entry.Steps.Count == 0) continue;
                if (string.IsNullOrEmpty(entry.Title)) continue;
                if (!TrackedQuestState.IsTracked(player, entry.Id)) continue;
                if (ChainState.IsComplete(player, entry.Id)) continue;

                var stepIdx = ChainState.GetStep(player, entry.Id);
                if (stepIdx >= entry.Steps.Count) continue;

                var step = entry.Steps[stepIdx];

                // Hide chains that have not actually started yet. GetStep() returns 0 both for a
                // brand-new/reset chain (step 0 pending, nothing fired) and for a chain genuinely
                // working on step 0. A chain is "started" only when its first step has fired and
                // advanced (stepIdx > 0), or when a counter step 0 has been activated
                // (GetCounter >= 0). Otherwise it is not yet in progress and must not appear.
                if (stepIdx == 0)
                {
                    var counterActivated = step != null && step.ProgressGoal > 0
                        && ChainState.GetCounter(player, entry.Id, 0) >= 0;
                    if (!counterActivated) continue;
                }

                string progress;
                if (step != null && step.ProgressGoal > 0)
                {
                    var raw = ChainState.GetCounter(player, entry.Id, stepIdx);
                    var cnt = raw < 0 ? 0 : raw;
                    progress = ProgressBar(cnt, step.ProgressGoal);
                }
                else
                {
                    // stepIdx is the count of completed steps (chains only appear after their
                    // first step fires, so this is >= 1 when visible). Shows "1/3" after the
                    // first step, matching the "Step 1/3" message wording.
                    progress = ProgressBar(stepIdx, entry.Steps.Count);
                }

                rows.Add(RowPrefix + Template(entry.Title, entry) + "   " + progress);
                descs.Add(RowDescription(entry, step));
                rowChainIds.Add(entry.Id);
            }

            // Multi-count npc_item_submit entries: show a progress bar while the player is
            // still collecting (submitted > 0 and < goal). Single-count entries have no bar.
            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "npc_item_submit",
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.Title)) continue;
                if (!TrackedQuestState.IsTracked(player, entry.Id)) continue;

                var goal = entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count;
                if (goal <= 1) continue;

                var cur = SubmitState.Get(player, entry.Id);
                if (cur <= 0 || cur >= goal) continue; // only while actively in progress

                rows.Add(RowPrefix + Template(entry.Title, entry) + "   " + ProgressBar(cur, goal));
                descs.Add(RowDescription(entry, null));
                rowChainIds.Add(entry.Id);
            }

            // Multi-count kill entries: show X/Y progress while the player is still accumulating
            // kills (counted > 0 and < goal). Single-kill entries have no bar.
            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "kill",
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.Title)) continue;
                if (!TrackedQuestState.IsTracked(player, entry.Id)) continue;

                var goal = entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count;
                if (goal <= 1) continue;
                if (SeenTracker.HasFired(player, entry.Id, entry.Scope)) continue;

                var cur = KillCountState.Get(player, entry.Id);
                if (cur <= 0 || cur >= goal) continue; // only while actively in progress

                rows.Add(RowPrefix + Template(entry.Title, entry) + "   " + ProgressBar(cur, goal));
                descs.Add(RowDescription(entry, null));
                rowChainIds.Add(entry.Id);
            }

            // item_acquired count-goal entries: show X/Y progress while collecting.
            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "item_acquired",
                        System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(entry.Title)) continue;
                if (!TrackedQuestState.IsTracked(player, entry.Id)) continue;
                if (SeenTracker.HasFired(player, entry.Id, entry.Scope)) continue;

                var goals = ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger);
                if (goals == null) continue;

                // A latched "started" flag keeps the row visible after the player has begun
                // collecting, even if every goal item is later removed from the inventory.
                var started = GoalStartedState.IsStarted(player, entry.Id);

                string progress;
                if (goals.Count == 1)
                {
                    var cur = ItemAcquiredTrigger.CountInInventory(player, goals[0].Item);
                    if (cur >= goals[0].Count) continue;   // complete — should have fired
                    if (cur <= 0 && !started) continue;     // not started yet
                    progress = ProgressBar(cur, goals[0].Count);
                }
                else
                {
                    var completedGoals = 0;
                    var totalProgress = 0;
                    foreach (var g in goals)
                    {
                        var cur = ItemAcquiredTrigger.CountInInventory(player, g.Item);
                        if (cur >= g.Count) completedGoals++;
                        else totalProgress += cur;
                    }
                    if (completedGoals >= goals.Count) continue; // all done, should have fired
                    if (completedGoals == 0 && totalProgress == 0 && !started) continue; // not started
                    progress = ProgressBar(completedGoals, goals.Count) + " goals";
                }

                rows.Add(RowPrefix + Template(entry.Title, entry) + "   " + progress);
                // The per-item breakdown IS the objective here; fall back to the authored
                // description only when there is nothing to break down.
                descs.Add(ItemAcquiredTrigger.BuildGoalProgressText(player, goals)
                          ?? RowDescription(entry, null));
                rowChainIds.Add(entry.Id);
            }

            // Cache descriptions for visible rows so the hover-tooltip logic in Update() can
            // look up the current step's description without re-scanning the config.
            var maxVis = Plugin.TrackerMaxVisible?.Value ?? 3;
            _rowDescriptions.Clear();
            _rowChainIds.Clear();
            for (var i = 0; i < System.Math.Min(descs.Count, maxVis); i++)
            {
                _rowDescriptions.Add(descs[i]);
                _rowChainIds.Add(rowChainIds[i]);
            }

            // Visibility is the player's call: F10 cycles Collapsed → Titles → Full. While
            // collapsed the panel stays down even though its pinned quests are still "in" it (the
            // badge keeps showing the count so the player knows there's something to re-open).
            if (_view == TrackerView.Collapsed)
            {
                _panel.SetActive(false);
                HideTooltip();
                RefreshBadge(rows.Count);
                return;
            }

            _panel.SetActive(true);
            if (_panelGroup != null) _panelGroup.alpha = 1f;

            if (rows.Count == 0)
            {
                // Empty state — panel shown (F10) but no pinned quests are active.
                if (_rowTexts.Count > 0)
                {
                    var codexKey = Plugin.CodexKey?.Value ?? "F3";
                    _rowTexts[0].text = "  No pinned quests — pin from [" + codexKey + "] Codex";
                    _rowTexts[0].gameObject.SetActive(true);
                }
                for (var i = 1; i < _rowTexts.Count; i++)
                    _rowTexts[i].gameObject.SetActive(false);
                foreach (var d in _rowDescTexts) if (d != null) d.gameObject.SetActive(false);
                if (_overflowText != null) _overflowText.gameObject.SetActive(false);
            }
            else
            {
                var max     = Plugin.TrackerMaxVisible?.Value ?? 3;
                var visible = System.Math.Min(rows.Count, System.Math.Min(max, _rowTexts.Count));
                var highlightDuration = fromProgress ? EffectiveSpec().HighlightDuration : 0f;

                for (var i = 0; i < _rowTexts.Count; i++)
                {
                    if (i < visible)
                    {
                        var newText = rows[i];
                        // Highlight rows whose content changed when called from a progress event.
                        if (fromProgress && _rowHighlightTimers != null
                            && i < _rowHighlightTimers.Length && newText != _rowTexts[i].text)
                        {
                            _rowTexts[i].color = new Color(1f, 0.95f, 0.5f);
                            _rowHighlightTimers[i] = highlightDuration;
                        }
                        _rowTexts[i].text = newText;
                        _rowTexts[i].gameObject.SetActive(true);
                    }
                    else
                    {
                        _rowTexts[i].gameObject.SetActive(false);
                    }

                    // Description under the row — only in Full view, and only when the quest
                    // actually has one. A row with no description simply has no second line.
                    if (i < _rowDescTexts.Count && _rowDescTexts[i] != null)
                    {
                        var desc = _view == TrackerView.Full && i < visible && i < descs.Count
                            ? descs[i] : null;
                        _rowDescTexts[i].text = desc ?? "";
                        _rowDescTexts[i].gameObject.SetActive(!string.IsNullOrEmpty(desc));
                    }
                }

                var overflow = rows.Count - max;
                if (_overflowText != null)
                {
                    if (overflow > 0)
                    {
                        var codexKey = Plugin.CodexKey?.Value ?? "F3";
                        _overflowText.text = "+ " + overflow + " more — press [" + codexKey + "] for Codex";
                        _overflowText.gameObject.SetActive(true);
                    }
                    else
                    {
                        _overflowText.gameObject.SetActive(false);
                    }
                }
            }

            RefreshBadge(rows.Count);

            // Re-show rows that are mid-completion flash — Refresh hides them because IsComplete
            // is already true, but the animation has not finished yet.
            foreach (var kv in _completingRowIdx)
            {
                var cIdx = kv.Value;
                if (cIdx >= 0 && cIdx < _rowTexts.Count && _rowTexts[cIdx] != null)
                {
                    if (_completingRowText.TryGetValue(kv.Key, out var ct)) _rowTexts[cIdx].text = ct;
                    _rowTexts[cIdx].gameObject.SetActive(true);
                }
            }

            // Re-measure every visible row against the panel width now that its final text is in
            // place, so a wrapped title (or a description spanning three lines) gets the extra
            // height it needs.
            if (_headerText != null) SizeRow(_headerText);
            foreach (var t in _rowTexts) if (t != null && t.gameObject.activeSelf) SizeRow(t);
            foreach (var t in _rowDescTexts) if (t != null && t.gameObject.activeSelf) SizeRow(t);
            if (_overflowText != null && _overflowText.gameObject.activeSelf) SizeRow(_overflowText);

            // Force an immediate layout pass so ContentSizeFitter recalculates the panel height
            // and TMP regenerates each row's geometry against its now-correct width.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
            // The panel's height just changed, and the badge rides directly above it.
            PositionBadge();
        }

        public void FlashCompletion(string chainId)
        {
            var idx = _rowChainIds.IndexOf(chainId);
            if (idx < 0 || idx >= _rowTexts.Count) return;
            _completingRows[chainId] = 0f;
            _completingRowIdx[chainId] = idx;
            _completingRowText[chainId] = _rowTexts[idx].text;
            _rowTexts[idx].color = new Color(1f, 1f, 0.7f);
            SpawnCompletionVfx();
        }

        private void SpawnCompletionVfx()
        {
            var spec = EffectiveSpec();
            if (!spec.CompletionVfxEnabled) return;
            var player = Player.m_localPlayer;
            if (player == null) return;

            // Reuse the game's own skill level-up EffectList, serialized on the Player prefab — the
            // exact vanilla VFX/SFX the game plays on a skill-up. Guaranteed valid; no prefab-name
            // guessing or ZNetScene registration dependency.
            player.m_skillLevelupEffects?.Create(player.transform.position, player.transform.rotation);
        }

        private void RefreshBadge(int activeCount)
        {
            if (_badgePanel == null) return;
            // Don't activate badge until font resolves — same null-font guard as the main panel.
            if (_font == null) { _badgePanel.SetActive(false); return; }
            if (GuidanceDisplay.IntroLockActive) { _badgePanel.SetActive(false); return; }
            if (!EffectiveBadgeEnabled()) { _badgePanel.SetActive(false); return; }

            // Resolve hotkey label: YAML tracker.hotkey wins; fall back to BepInEx.
            var yamlTracker = Plugin.CurrentConfig?.Tracker;
            var keyStr = yamlTracker != null
                ? yamlTracker.Hotkey
                : Plugin.TrackerHotkey?.Value ?? "F10";
            if (string.IsNullOrEmpty(keyStr)) keyStr = "F10";

            // The badge advertises what the key does NEXT, not what is on screen now — a player
            // who has never pressed F10 can read "Show Quests [F10]" and know both that the key
            // does something and what it will do. The pinned count rides along only while the
            // panel is down, where it is the one thing the badge cannot otherwise convey.
            switch (_view)
            {
                case TrackerView.Collapsed:
                    _badgeText.text = activeCount > 0
                        ? "Show Quests (" + activeCount + ") [" + keyStr + "]"
                        : "Show Quests [" + keyStr + "]";
                    break;
                case TrackerView.Titles:
                    _badgeText.text = "Show Desc [" + keyStr + "]";
                    break;
                default: // Full — next press closes it
                    _badgeText.text = "Hide Quests [" + keyStr + "]";
                    break;
            }

            _badgePanel.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_badgePanel.transform);
            PositionBadge();
        }

        // ── Hotkey toggle ─────────────────────────────────────────────────────────────────────

        /// F10 handler: advance to the next view and wrap — Collapsed → Titles → Full →
        /// Collapsed. Cycling (rather than a two-state toggle) is what lets the player read
        /// objectives without freeing the cursor to hover a row. The panel still never captures
        /// the cursor or freezes the player; it just shows more or less over normal gameplay.
        private void CycleView()
        {
            _view = _view == TrackerView.Collapsed ? TrackerView.Titles
                  : _view == TrackerView.Titles    ? TrackerView.Full
                  : TrackerView.Collapsed;

            // Full view prints every description inline, so the hover tooltip would only repeat
            // what is already on screen; collapsed has nothing to hover.
            if (_view != TrackerView.Titles) HideTooltip();
            Refresh();
        }

        private KeyCode ResolveHotkey()
        {
            var yamlTracker = Plugin.CurrentConfig?.Tracker;
            var keyStr = yamlTracker != null
                ? yamlTracker.Hotkey
                : Plugin.TrackerHotkey?.Value ?? "F10";
            if (string.IsNullOrEmpty(keyStr)) return KeyCode.None;
            return System.Enum.TryParse<KeyCode>(keyStr, ignoreCase: true, out var kc)
                ? kc : KeyCode.None;
        }

        // ── Drag-to-move ──────────────────────────────────────────────────────────────────────

        /// Load the player's saved panel position once it is available. Marks _hasCustomPos so
        /// ApplyLayout pins the panel there instead of at the configured corner.
        private void LoadCustomPos(Player player)
        {
            if (_hasCustomPos || player == null) return;
            var saved = TrackedQuestState.GetPosition(player);
            if (saved.HasValue)
            {
                _customPos    = saved.Value;
                _hasCustomPos = true;
                ApplyCustomPos();
            }
        }

        /// True when the cursor is free for UI interaction — i.e. the inventory or the ESC menu is
        /// open. The panel can only be dragged in these states (otherwise the cursor is captured
        /// for mouse-look). Guarded so a missing/!ready InventoryGui never throws.
        private static bool CursorFreeForDrag()
        {
            bool inv  = InventoryGui.instance != null && InventoryGui.IsVisible();
            bool menu = Menu.IsVisible();
            return inv || menu;
        }

        /// Per-frame drag handling. Press-and-hold left mouse over the panel (while the cursor is
        /// free) to move it; the position is persisted on release.
        private void UpdateDrag()
        {
            // Either box is a drag handle. The badge matters most: while collapsed it is the only
            // thing on screen, and it is where the "drag to move" hint is written.
            var panelUp = _panel != null && _panel.activeSelf;
            var badgeUp = _badgePanel != null && _badgePanel.activeSelf;
            if (!panelUp && !badgeUp) { _dragging = false; return; }

            if (!_dragging)
            {
                if (!CursorFreeForDrag()) return;
                if (Input.GetMouseButtonDown(0) && _panelRect != null && DragHandleHit())
                {
                    _dragging       = true;
                    _dragMouseStart = Input.mousePosition;
                    // Snap to a centre anchor so the live drag math is in canvas-centre space.
                    if (!_hasCustomPos)
                    {
                        _customPos    = CornerPosToCenter();
                        _hasCustomPos = true;
                        ApplyCustomPos();
                    }
                    _dragPanelStart = _panelRect.anchoredPosition;
                }
                return;
            }

            // Dragging in progress.
            if (Input.GetMouseButton(0))
            {
                var scale = _uiRoot != null
                    ? Mathf.Max(0.0001f, _uiRoot.GetComponent<Canvas>().scaleFactor) : 1f;
                var deltaScreen = (Vector2)Input.mousePosition - _dragMouseStart;
                _customPos = _dragPanelStart + deltaScreen / scale;
                ApplyCustomPos();
            }
            else // button released
            {
                _dragging = false;
                var player = Player.m_localPlayer;
                if (player != null) TrackedQuestState.SetPosition(player, _customPos);
            }
        }

        /// True when the cursor is over either drag handle — the panel or the badge above it.
        private bool DragHandleHit()
        {
            var mouse = Input.mousePosition;
            if (_panel != null && _panel.activeSelf && _panelRect != null &&
                RectTransformUtility.RectangleContainsScreenPoint(_panelRect, mouse, null))
                return true;
            if (_badgePanel != null && _badgePanel.activeSelf &&
                RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)_badgePanel.transform, mouse, null))
                return true;
            return false;
        }

        /// Convert the panel's current corner-anchored rect into the centre-anchored
        /// anchoredPosition ApplyCustomPos expects, so dragging starts exactly where the panel is
        /// currently drawn (no visual jump). Takes the TOP-centre point, matching the custom
        /// pivot (0.5, 1).
        private Vector2 CornerPosToCenter()
        {
            if (_panelRect == null || _uiRoot == null) return Vector2.zero;
            var rootRect = (RectTransform)_uiRoot.transform;
            var r        = _panelRect.rect;
            var topCentre = _panelRect.TransformPoint(new Vector3(r.center.x, r.yMax, 0f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect, RectTransformUtility.WorldToScreenPoint(null, topCentre), null, out var local);
            return local;
        }

        /// The root canvas is a scene-root object (no parent), so it is not destroyed automatically
        /// when this tracker's GameObject is. Tear it down explicitly to avoid orphaned canvases if
        /// the Hud is recreated.
        private void OnDestroy()
        {
            if (_uiRoot != null) Destroy(_uiRoot);
        }

        // ── Hover tooltip helpers ─────────────────────────────────────────────────────────────

        private void HideTooltip()
        {
            if (_tooltipPanel != null) _tooltipPanel.SetActive(false);
            _hoveredRowIndex = -1;
        }

        private void PositionTooltip(int rowIndex)
        {
            if (_tooltipRect == null || _uiRoot == null || _panelRect == null) return;
            if (rowIndex >= _rowTexts.Count || _rowTexts[rowIndex] == null) return;

            var rowRect  = _rowTexts[rowIndex].rectTransform;
            var rootRect = (RectTransform)_uiRoot.transform;

            // Always place the tooltip to the left of the tracker panel, vertically
            // aligned with the top of the hovered row.
            var panelCorners = new Vector3[4];
            _panelRect.GetWorldCorners(panelCorners);
            // corners: [0]=BL [1]=TL [2]=TR [3]=BR (screen pixels in ScreenSpaceOverlay)
            var rowCorners = new Vector3[4];
            rowRect.GetWorldCorners(rowCorners);

            // Anchor the tooltip's top-right corner just left of the panel's left edge.
            var screenX = panelCorners[0].x - 8f; // panel left edge − gap
            var screenY = rowCorners[1].y;          // row top

            // ScreenPointToLocalPointInRectangle returns coordinates in rootRect LOCAL space,
            // whose origin is at the canvas CENTER (pivot 0.5,0.5 on a root ScreenSpaceOverlay
            // canvas). Setting anchorMin/Max to (0.5,0.5) makes anchoredPosition equal to that
            // local-space value directly, so the pivot lands exactly on the target screen point.
            _tooltipRect.pivot     = new Vector2(1f, 1f); // top-right of tooltip touches target
            _tooltipRect.anchorMin = new Vector2(0.5f, 0.5f);
            _tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect, new Vector2(screenX, screenY), null, out var localPos);
            _tooltipRect.anchoredPosition = localPos;
        }

        private void UpdateTooltip()
        {
            if (_tooltipPanel == null) return;

            // Titles view only: in Full the description is already printed under every row, so a
            // tooltip would just repeat it on top of the panel.
            bool panelOpen = _panel != null && _panel.activeSelf
                             && _view == TrackerView.Titles && !GuidanceDisplay.IntroLockActive;
            if (!panelOpen)
            {
                HideTooltip();
                return;
            }

            var mousePos  = Input.mousePosition;
            var newHovered = -1;

            for (var i = 0; i < _rowTexts.Count; i++)
            {
                if (!_rowTexts[i].gameObject.activeSelf) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(
                        _rowTexts[i].rectTransform, mousePos, null))
                {
                    newHovered = i;
                    break;
                }
            }

            // Only rows with a non-empty description show a tooltip.
            string desc = null;
            if (newHovered >= 0 && newHovered < _rowDescriptions.Count)
                desc = _rowDescriptions[newHovered];
            if (string.IsNullOrEmpty(desc)) newHovered = -1;

            if (newHovered == _hoveredRowIndex) return;

            _hoveredRowIndex = newHovered;

            if (newHovered < 0)
            {
                HideTooltip();
                return;
            }

            _tooltipText.text = desc;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tooltipRect);
            PositionTooltip(newHovered);
            _tooltipPanel.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_tooltipRect);
        }

        // ── Per-frame ─────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // Cinematic hide — applies regardless of manual open state.
            if (_panel != null && _panel.activeSelf && GuidanceDisplay.IntroLockActive)
                _panel.SetActive(false);

            // Hotkey toggle — fires only on the initial KeyDown frame, no repeat.
            var hotkey = ResolveHotkey();
            if (hotkey != KeyCode.None && Input.GetKeyDown(hotkey))
                CycleView();

            // Drag-to-move (only while the cursor is free — inventory or ESC menu open).
            UpdateDrag();

            // Per-row highlight countdown — decrement non-zero timers and reset to white on expiry.
            if (_rowHighlightTimers != null)
            {
                for (var i = 0; i < _rowHighlightTimers.Length; i++)
                {
                    if (_rowHighlightTimers[i] <= 0f) continue;
                    _rowHighlightTimers[i] -= Time.deltaTime;
                    if (_rowHighlightTimers[i] <= 0f && i < _rowTexts.Count && _rowTexts[i] != null)
                        _rowTexts[i].color = Color.white;
                }
            }

            // Phase 04e: drive completion flash (0.4 s white-gold) then fade (0.6 s) per row.
            // Runs after highlight timers so the flash color always wins.
            if (_completingRows.Count > 0)
            {
                var completionDone = new List<string>();
                foreach (var chainId in new List<string>(_completingRows.Keys))
                {
                    var elapsed = _completingRows[chainId] + Time.deltaTime;
                    _completingRows[chainId] = elapsed;

                    if (!_completingRowIdx.TryGetValue(chainId, out var idx)) continue;
                    if (idx < 0 || idx >= _rowTexts.Count || _rowTexts[idx] == null) continue;

                    var row = _rowTexts[idx];
                    if (_completingRowText.TryGetValue(chainId, out var rowText)) row.text = rowText;
                    row.gameObject.SetActive(true);

                    if (elapsed < 0.4f)
                    {
                        row.color = new Color(1f, 1f, 0.7f);
                    }
                    else if (elapsed < 1.0f)
                    {
                        var t = (elapsed - 0.4f) / 0.6f;
                        row.color = new Color(1f, 1f, 0.7f, 1f - t);
                    }
                    else
                    {
                        row.color = Color.white;
                        row.gameObject.SetActive(false);
                        completionDone.Add(chainId);
                    }
                }
                if (completionDone.Count > 0)
                {
                    foreach (var id in completionDone)
                    {
                        _completingRows.Remove(id);
                        _completingRowIdx.Remove(id);
                        _completingRowText.Remove(id);
                    }
                    Refresh();
                }
            }

            UpdateTooltip();
        }
    }

    /// Spawns and initialises the tracker each time the Hud scene is loaded.
    /// A fresh Instance overwrites the stale one from the previous session automatically.
    [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
    internal static class HudAwakePatch
    {
        private static void Postfix(Hud __instance)
        {
            var go = new GameObject("VSG_Tracker");
            go.transform.SetParent(__instance.transform, worldPositionStays: false);
            GuidanceHudTracker.Instance = go.AddComponent<GuidanceHudTracker>();
            GuidanceHudTracker.Instance.BuildPanel();
            GuidanceHudTracker.Instance.BuildBadge();
            GuidanceHudTracker.Instance.BuildTooltip();
            GuidanceHudTracker.Instance.Refresh();
            Plugin.Log.LogInfo("[tracker] HUD tracker panel created.");
        }
    }

    /// Refreshes the tracker after the local player object is fully initialised. On a host this
    /// already sees the loaded progress store; on a client the store arrives moments later and
    /// PlayerProgress repaints again. Either way in-progress chains that survived a session
    /// reload appear on login without requiring any action.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    internal static class PlayerOnSpawnedTrackerPatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance != Player.m_localPlayer) return;
            GuidanceHudTracker.Instance?.Refresh();
        }
    }

    /// Suppress player attack/use/interact input while the Codex is open (it's a modal panel).
    /// The progress tracker no longer captures input — it shows over normal gameplay.
    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    internal static class PlayerTakeInputTrackerPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (GuidanceCodex.Instance != null && GuidanceCodex.Instance.IsOpen) __result = false;
        }
    }

    /// The actual mouse-LOOK and movement are gated by PlayerController's own private TakeInput
    /// (not Player.TakeInput). Freezing it while the Codex is open stops the camera rotating with
    /// the mouse — the tracker no longer participates here.
    [HarmonyPatch(typeof(PlayerController), "TakeInput", new[] { typeof(bool) })]
    internal static class PlayerControllerTakeInputTrackerPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (GuidanceCodex.Instance != null && GuidanceCodex.Instance.IsOpen) __result = false;
        }
    }
}
