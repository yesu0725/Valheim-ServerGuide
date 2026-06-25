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

        // ── Main tracker panel ────────────────────────────────────────────────────────────────
        private GameObject _panel;
        private RectTransform _panelRect;
        private TMP_Text _headerText;
        private readonly List<TMP_Text> _rowTexts = new List<TMP_Text>();
        private TMP_Text _overflowText;
        private TMP_FontAsset _font;
        private int _builtMaxVisible;

        // ── Hotkey toggle + badge ─────────────────────────────────────────────────────────────
        // The panel shows the set of quests the player has pinned from the Guide Codex
        // (TrackedQuestState). It starts hidden each session; F10 toggles _userHidden, and pinning
        // a quest in the Codex force-unhides it. The panel no longer captures input or the cursor.
        private bool _userHidden = true;   // true = player has hidden the panel (default: hidden)
        private GameObject _badgePanel;
        private TMP_Text _badgeText;

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
            canvas.sortingOrder = 1000; // above InventoryGui/crafting, below nothing we care about

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

            _rowTexts.Clear();
            for (var i = 0; i < _builtMaxVisible; i++)
                _rowTexts.Add(MakeText("", style: FontStyles.Normal, color: Color.white, rowHeight: 14f));
            _rowHighlightTimers = new float[_builtMaxVisible];

            _overflowText = MakeText("", style: FontStyles.Italic,
                color: new Color(0.75f, 0.75f, 0.75f), rowHeight: 13f);

            ApplyLayout();
        }

        /// Called from HudAwakePatch after BuildPanel. Creates the persistent corner hint badge
        /// that shows the hotkey label and active quest count even when the main panel is hidden.
        public void BuildBadge()
        {
            _badgePanel = new GameObject("VSG_TrackerBadge");
            _badgePanel.transform.SetParent(UiRoot(), worldPositionStays: false);
            _badgePanel.AddComponent<RectTransform>();

            var bg = _badgePanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);
            bg.raycastTarget = false;

            var layout = _badgePanel.AddComponent<HorizontalLayoutGroup>();
            layout.childControlWidth      = true;
            layout.childForceExpandWidth  = false;
            layout.childControlHeight     = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(6, 6, 3, 3);

            var fitter = _badgePanel.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // Start hidden — same null-font guard as the main panel.
            _badgePanel.SetActive(false);

            var go = new GameObject("VSG_BadgeText");
            go.transform.SetParent(_badgePanel.transform, worldPositionStays: false);
            go.AddComponent<LayoutElement>();

            _badgeText = go.AddComponent<TextMeshProUGUI>();
            _badgeText.text               = "[F10] Quests";
            _badgeText.fontStyle          = FontStyles.Normal;
            _badgeText.color              = new Color(0.85f, 0.85f, 0.85f, 1f);
            _badgeText.fontSize           = 10f;
            _badgeText.alignment          = TextAlignmentOptions.Left;
            _badgeText.enableWordWrapping = false;
            _badgeText.raycastTarget      = false;

            ApplyBadgeLayout();
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
            _tooltipText.maxVisibleLines    = 6;
            _tooltipText.overflowMode       = TextOverflowModes.Truncate;
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
            if (_overflowText != null) _overflowText.font = font;
            if (_badgeText != null) _badgeText.font = font;
            if (_tooltipText != null) _tooltipText.font = font;
        }

        /// Fixed-width "ghost bar" progress indicator using TMP rich-text color tags so the
        /// bracket width never changes as the counter advances (plain space-padding looks
        /// uneven in a proportional font). Bright filled segments, dark-gray ghost segments.
        private static string ProgressBar(int cur, int goal)
        {
            if (goal <= 0) return cur + "/" + goal;
            var width = Mathf.Clamp(goal, 1, 12);
            var filled = Mathf.Clamp(Mathf.RoundToInt((float)cur / goal * width), 0, width);
            return "[<color=#FFE6A8>" + new string('=', filled) +
                   "</color><color=#555555>" + new string('=', width - filled) +
                   "</color>] " + cur + "/" + goal;
        }

        private TMP_Text MakeText(string content, FontStyles style, Color color, float rowHeight)
        {
            var go = new GameObject("VSG_T");
            go.transform.SetParent(_panel.transform, worldPositionStays: false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = rowHeight;
            le.flexibleWidth   = 1f;

            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text               = content;
            t.fontStyle          = style;
            t.color              = color;
            t.alignment          = TextAlignmentOptions.Left;
            t.enableWordWrapping = false;
            // Clamp to the row width with a trailing ellipsis so long titles never spill
            // off the right edge of the screen.
            t.overflowMode       = TextOverflowModes.Ellipsis;
            t.raycastTarget      = false;
            return t;
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
            if (_overflowText != null) SetRow(_overflowText, fs - 1f);

            ApplyBadgeLayout();
        }

        /// Apply a font size to a text row and resize its LayoutElement height to match, so the
        /// glyphs are never clipped vertically by a stale fixed row height.
        private static void SetRow(TMP_Text t, float fontSize)
        {
            t.fontSize = fontSize;
            var le = t.GetComponent<LayoutElement>();
            if (le != null) le.preferredHeight = Mathf.Ceil(fontSize * 1.45f);
        }

        private void ApplyBadgeLayout()
        {
            if (_badgePanel == null) return;
            var badgeRect = _badgePanel.GetComponent<RectTransform>();
            if (badgeRect == null) return;

            var spec = EffectiveSpec();
            // Badge sits clearly above the main tracker panel: same anchor and OffsetX, but a
            // smaller OffsetY (closer to the screen edge). The 40px gap is enough to clear the
            // badge's own height plus margin so the two boxes never overlap.
            var badgeOffsetY = Mathf.Max(0f, spec.OffsetY - 40f);
            ApplyAnchor(badgeRect, spec.Anchor, spec.OffsetX, badgeOffsetY);

            if (_badgeText != null)
                _badgeText.fontSize = Mathf.Max(6f, spec.FontSize - 1f);
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

        /// Pin the panel to a player-chosen position. Uses a centre anchor/pivot so the stored
        /// anchoredPosition is canvas-centre-relative (resolution-independent) and survives the
        /// CanvasScaler the same way the drag math computes it.
        private void ApplyCustomPos()
        {
            if (_panelRect == null) return;
            _panelRect.anchorMin = _panelRect.anchorMax = _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = _customPos;
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
            if (tracked) _userHidden = false; // turning a switch on unhides the panel
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
        /// by the player (F10 toggles _userHidden); fromProgress only highlights changed rows.
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

                rows.Add(RowPrefix + entry.Title + "   " + progress);
                descs.Add(step?.Description);
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

                rows.Add(RowPrefix + entry.Title + "   " + ProgressBar(cur, goal));
                descs.Add(null);
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

                rows.Add(RowPrefix + entry.Title + "   " + ProgressBar(cur, goal));
                descs.Add(null);
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

                rows.Add(RowPrefix + entry.Title + "   " + progress);
                descs.Add(ItemAcquiredTrigger.BuildGoalProgressText(player, goals));
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

            // Visibility is the player's call: F10 toggles _userHidden. While hidden, the panel
            // stays down even though its pinned quests are still "in" it (the badge keeps showing
            // the count so the player knows there's something to re-open).
            if (_userHidden)
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

            // Force an immediate layout pass so ContentSizeFitter recalculates the panel height
            // and TMP regenerates each row's geometry against its now-correct width.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
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

            _badgeText.text = activeCount > 0
                ? "[" + keyStr + "] Quests (" + activeCount + ")"
                : "[" + keyStr + "] Quests";

            _badgePanel.SetActive(true);
        }

        // ── Hotkey toggle ─────────────────────────────────────────────────────────────────────

        /// F10 handler: flip the player's hidden/shown preference. The panel no longer captures
        /// the cursor or freezes the player — it just shows or hides over normal gameplay.
        private void ToggleManual()
        {
            _userHidden = !_userHidden;
            if (_userHidden)
                HideTooltip();
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
            if (_panel == null || !_panel.activeSelf) { _dragging = false; return; }

            if (!_dragging)
            {
                if (!CursorFreeForDrag()) return;
                if (Input.GetMouseButtonDown(0) && _panelRect != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(_panelRect, Input.mousePosition, null))
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

        /// Convert the panel's current corner-anchored rect into a centre-anchored anchoredPosition
        /// so dragging starts exactly where the panel is currently drawn (no visual jump).
        private Vector2 CornerPosToCenter()
        {
            if (_panelRect == null || _uiRoot == null) return Vector2.zero;
            var rootRect = (RectTransform)_uiRoot.transform;
            var center   = _panelRect.TransformPoint(_panelRect.rect.center);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect, RectTransformUtility.WorldToScreenPoint(null, center), null, out var local);
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

            bool panelOpen = _panel != null && _panel.activeSelf && !GuidanceDisplay.IntroLockActive;
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
                ToggleManual();

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

    /// Refreshes the tracker after the local player object is fully initialised and
    /// m_customData is populated from the save. This ensures in-progress chains that
    /// survived a session reload appear immediately on login without requiring any action.
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
