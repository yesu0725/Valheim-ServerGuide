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
    /// Conversation panel opened when the player holds E near a trader NPC.
    ///
    /// Layout — 750 px wide, height driven entirely by the content:
    ///   • Anchored above the bottom edge with a bottom pivot, so the panel grows UPWARD as
    ///     the text gets longer and never slides off the bottom of the screen.
    ///   • Header (NPC name / topic) at the top in gold, wrapping if it is long.
    ///   • Body text below, inside a scroll view.
    ///   • Choice buttons at the bottom, wrapped onto as many rows as they need.
    ///
    /// Text is never truncated. The body grows to fit whatever the guide author wrote; only
    /// once the panel would exceed MaxPanelHeightFraction of the screen does the body stop
    /// growing and start scrolling instead, so the remaining text is a wheel-scroll away
    /// rather than silently cut off.
    ///
    /// Vanilla Unity UI only — no custom textures. Font resolved lazily from loaded
    /// TMP assets the same way GuidanceHudTracker does.
    public class NpcConversationPanel : MonoBehaviour
    {
        public static NpcConversationPanel Instance { get; private set; }
        internal static bool IsOpen => Instance != null && Instance._isOpen;

        private const float PanelWidth = 750f;
        /// Ceiling for the whole panel, as a fraction of screen height. Beyond this the body
        /// scrolls rather than the panel continuing to grow.
        private const float MaxPanelHeightFraction = 0.82f;
        /// Height the chrome (header, rule, padding, choice rows) needs outside the body.
        /// Measured after layout; this is only the starting estimate for the first frame.
        private const float MinBodyHeight = 40f;

        private bool _isOpen;
        private GuidanceEntry _currentEntry;
        private List<ConversationNodeSpec> _nodes; // Phase 4 multi-node conversations

        private TMP_FontAsset _font;
        private RectTransform _bgRect;
        private TMP_Text _headerText;
        private TMP_Text _bodyText;
        private RectTransform _bodyContentRect;
        private LayoutElement _bodyViewportLe;
        private ScrollRect _bodyScroll;
        private GameObject _choiceContainer;
        private readonly List<TMP_Text> _choiceLabels = new List<TMP_Text>();
        private readonly List<LayoutElement> _choiceButtonLayouts = new List<LayoutElement>();
        // Buttons are packed into rows so a long list wraps instead of squeezing each button
        // down to an unreadable sliver. Rebuilt on every Open/RenderNode.
        private Transform _currentChoiceRow;
        private int _rowCapacity = 3;
        private int _rowCount;

        // ── Factory ────────────────────────────────────────────────────────────

        public static NpcConversationPanel Get()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("VSG_ConversationPanel");
            go.SetActive(false);          // inactive during build → TMP Awake suppressed
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiLayers.Conversation;
            go.AddComponent<GraphicRaycaster>();

            Instance = go.AddComponent<NpcConversationPanel>();
            Instance.BuildPanel();
            return Instance;
        }

        // ── Panel construction ─────────────────────────────────────────────────

        private void BuildPanel()
        {
            // ── Background ───────────────────────────────────────────────────────
            // Bottom pivot anchored above the lower edge: a VerticalLayoutGroup +
            // ContentSizeFitter set the height from the content, and the panel expands
            // upward from a fixed baseline instead of overflowing off-screen.
            var bg = new GameObject("BG");
            bg.transform.SetParent(transform, false);
            _bgRect = bg.AddComponent<RectTransform>();
            _bgRect.anchorMin        = new Vector2(0.5f, 0f);
            _bgRect.anchorMax        = new Vector2(0.5f, 0f);
            _bgRect.pivot            = new Vector2(0.5f, 0f);
            _bgRect.sizeDelta        = new Vector2(PanelWidth, 185f);
            _bgRect.anchoredPosition = new Vector2(0f, 110f);
            var bgImg = bg.AddComponent<Image>();
            // Darker, more opaque fill so the white body text reads clearly.
            bgImg.color = new Color(0.02f, 0.02f, 0.02f, 0.97f);

            var vlg = bg.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth   = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment         = TextAnchor.UpperLeft;
            vlg.spacing                = 8f;
            vlg.padding                = new RectOffset(12, 12, 8, 10);

            var fitter = bg.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width is fixed
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // ── Header ───────────────────────────────────────────────────────────
            var headerGo = new GameObject("Header");
            headerGo.transform.SetParent(bg.transform, false);
            headerGo.AddComponent<RectTransform>();
            _headerText = headerGo.AddComponent<TextMeshProUGUI>();
            _headerText.fontSize  = 20f;
            _headerText.fontStyle = FontStyles.Bold;
            _headerText.alignment = TextAlignmentOptions.Left;
            _headerText.color     = new Color(0.88f, 0.75f, 0.47f); // gold
            // A long NPC name or topic wraps onto a second line rather than being clipped.
            _headerText.enableWordWrapping = true;
            _headerText.overflowMode       = TextOverflowModes.Overflow;

            // ── Divider rule ─────────────────────────────────────────────────────
            var rule = new GameObject("Rule");
            rule.transform.SetParent(bg.transform, false);
            rule.AddComponent<RectTransform>();
            var ruleImg = rule.AddComponent<Image>();
            ruleImg.color         = new Color(0.88f, 0.75f, 0.47f, 0.30f);
            ruleImg.raycastTarget = false;
            var ruleLe = rule.AddComponent<LayoutElement>();
            ruleLe.minHeight       = 1f;
            ruleLe.preferredHeight = 1f;

            // ── Body (scrollable, never truncated) ───────────────────────────────
            // The viewport's preferred height is set per-message in SetBody: it matches the
            // text exactly until the panel would outgrow the screen, and only then clamps and
            // lets the ScrollRect take over. overflowMode stays Overflow — nothing is dropped.
            var viewportGo = new GameObject("BodyViewport");
            viewportGo.transform.SetParent(bg.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.pivot = new Vector2(0.5f, 1f);
            viewportGo.AddComponent<RectMask2D>();
            _bodyViewportLe = viewportGo.AddComponent<LayoutElement>();
            _bodyViewportLe.minHeight       = MinBodyHeight;
            _bodyViewportLe.preferredHeight = MinBodyHeight;

            _bodyScroll = viewportGo.AddComponent<ScrollRect>();
            _bodyScroll.horizontal   = false;
            _bodyScroll.vertical     = true;
            _bodyScroll.movementType = ScrollRect.MovementType.Clamped;
            _bodyScroll.viewport     = viewportRt;

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(viewportGo.transform, false);
            _bodyContentRect = bodyGo.AddComponent<RectTransform>();
            _bodyContentRect.anchorMin = new Vector2(0f, 1f);
            _bodyContentRect.anchorMax = new Vector2(1f, 1f);
            _bodyContentRect.pivot     = new Vector2(0.5f, 1f);
            _bodyContentRect.offsetMin = Vector2.zero;
            _bodyContentRect.offsetMax = Vector2.zero;
            _bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
            _bodyText.fontSize           = 15f;
            _bodyText.alignment          = TextAlignmentOptions.TopLeft;
            _bodyText.color              = Color.white;
            _bodyText.enableWordWrapping = true;
            _bodyText.overflowMode       = TextOverflowModes.Overflow;
            _bodyScroll.content = _bodyContentRect;
            // Fixed pixels per notch — see WheelScroller for why sensitivity alone does not work.
            WheelScroller.Attach(_bodyScroll);

            // ── Choice rows ──────────────────────────────────────────────────────
            // A vertical stack of horizontal rows. AddChoiceButton opens a new row once the
            // current one is full, so five choices become three readable rows instead of five
            // slivers. Row height follows each button's wrapped label.
            _choiceContainer = new GameObject("Choices");
            _choiceContainer.transform.SetParent(bg.transform, false);
            _choiceContainer.AddComponent<RectTransform>();
            var rowsLayout = _choiceContainer.AddComponent<VerticalLayoutGroup>();
            rowsLayout.spacing                = 6f;
            rowsLayout.childControlWidth      = true;
            rowsLayout.childControlHeight     = true;
            rowsLayout.childForceExpandWidth  = true;
            rowsLayout.childForceExpandHeight = false;
            rowsLayout.padding = new RectOffset(0, 0, 2, 0);
        }

        /// Sets the body text and sizes the scroll viewport to it. Grows with the content up to
        /// the panel's screen-height ceiling, then clamps so the rest scrolls into view.
        private void SetBody(string text)
        {
            _bodyText.text = text ?? "";

            // The text needs a resolved width before its height means anything.
            var innerWidth = PanelWidth - 24f; // VerticalLayoutGroup left+right padding
            _bodyContentRect.sizeDelta = new Vector2(0f, _bodyContentRect.sizeDelta.y);
            _bodyText.ForceMeshUpdate();

            var needed = _bodyText.GetPreferredValues(_bodyText.text, innerWidth, 0f).y;

            // Everything except the body: padding, header, rule, spacing, choice rows.
            var chrome = 26f + Mathf.Max(_headerText.preferredHeight, 24f) + 1f + 24f
                       + EstimateChoiceHeight();
            var maxBody = Mathf.Max(MinBodyHeight, Screen.height * MaxPanelHeightFraction - chrome);

            _bodyViewportLe.preferredHeight = Mathf.Clamp(needed, MinBodyHeight, maxBody);
            _bodyContentRect.sizeDelta      = new Vector2(0f, needed);

            // Scrolling only matters when the text actually overflows; park it at the top so a
            // re-used panel never opens halfway down the previous message.
            _bodyScroll.enabled           = needed > _bodyViewportLe.preferredHeight + 0.5f;
            _bodyScroll.verticalNormalizedPosition = 1f;
        }

        /// Rough height of the choice block, used to budget the body's share of the screen.
        /// Exact values are not needed — it only decides when scrolling kicks in.
        private float EstimateChoiceHeight()
            => _rowCount <= 0 ? 46f : _rowCount * 46f + (_rowCount - 1) * 6f;

        // ── Public API ─────────────────────────────────────────────────────────

        public void Open(GuidanceEntry entry, string renderedText)
        {
            var nodes = entry?.Conversation?.Nodes;
            if (nodes != null && nodes.Count > 0)
            {
                OpenNodeConversation(entry, nodes);
                return;
            }

            _currentEntry = entry;

            // Lazily resolve vanilla font (same logic as GuidanceHudTracker).
            if (_font == null) _font = GuidanceHudTracker.FindVanillaFontStatic();
            if (_font != null)
            {
                if (_headerText != null) _headerText.font = _font;
                if (_bodyText   != null) _bodyText.font   = _font;
            }

            // Activate before laying anything out. TMP's GetPreferredValues and
            // LayoutRebuilder both return zeroes for an inactive hierarchy, which would leave
            // the body sized to its minimum. Fonts are already assigned above, so no TMP Awake
            // fires without one.
            _isOpen = true;
            gameObject.SetActive(true);

            _headerText.text = Template(entry.Display?.Topic ?? entry.Title ?? "", entry);

            // Choices are built BEFORE the body so SetBody can budget the body's share of the
            // screen against the real number of button rows.
            var choices = entry.Conversation?.Choices;
            BeginChoices(choices?.Count ?? 1);
            if (choices != null && choices.Count > 0)
            {
                foreach (var c in choices) AddChoiceButton(c);
            }
            else
            {
                AddChoiceButton(new ChoiceSpec { Text = "Dismiss" });
            }
            EndChoices();

            SetBody(renderedText ?? "");
            FreeCursor();
        }

        /// Shows the "what would you like to discuss?" picker when 2+ npc_conversation
        /// entries are eligible for the same NPC. Selecting an entry fires it via
        /// GuidanceDispatcher.FireEntry, which opens that entry's own conversation normally
        /// through the regular GuidanceDisplay.Show -> Open() path.
        public void OpenSelection(string npcDisplayName, List<GuidanceEntry> entries, string npcSubject)
        {
            _currentEntry = null;

            if (_font == null) _font = GuidanceHudTracker.FindVanillaFontStatic();
            if (_font != null)
            {
                if (_headerText != null) _headerText.font = _font;
                if (_bodyText   != null) _bodyText.font   = _font;
            }

            _isOpen = true;
            gameObject.SetActive(true);

            // Trader.m_name is a localization token ("$npc_haldor"), not a name — the picker
            // header printed it raw before this.
            _headerText.text = Triggers.TriggerUtils.LocalizeName(npcDisplayName ?? "");

            BeginChoices(entries?.Count ?? 0);
            foreach (var entry in entries) AddSelectionButton(entry, npcSubject);
            EndChoices();

            SetBody("What would you like to discuss?");
            FreeCursor();
        }

        // ── Multi-node dialogue trees (Phase 4) ──────────────────────────────────

        /// Opens a multi-node conversation entry. Starts at the saved node when
        /// `resume_on_return: true` and a save exists, otherwise at nodes[0].
        private void OpenNodeConversation(GuidanceEntry entry, List<ConversationNodeSpec> nodes)
        {
            _currentEntry = entry;
            _nodes = nodes;

            ConversationNodeSpec startNode = null;
            if (entry.Conversation.ResumeOnReturn)
            {
                var savedId = ConversationNodeState.GetCurrentNode(Player.m_localPlayer, entry.Id);
                if (!string.IsNullOrEmpty(savedId)) startNode = FindNode(savedId);
            }
            startNode ??= _nodes[0];

            // Active before RenderNode, for the same measuring reason as Open().
            _isOpen = true;
            gameObject.SetActive(true);

            RenderNode(entry, startNode);
            FreeCursor();
        }

        private ConversationNodeSpec FindNode(string nodeId)
            => _nodes?.Find(n => string.Equals(n.Id, nodeId, System.StringComparison.OrdinalIgnoreCase));

        /// Renders one node's text + choices into the existing panel chrome, persisting
        /// the visited node immediately so a relog mid-tree resumes here (when
        /// resume_on_return reads it back) instead of always restarting at nodes[0].
        private void RenderNode(GuidanceEntry entry, ConversationNodeSpec node)
        {
            var player = Player.m_localPlayer;

            if (_font == null) _font = GuidanceHudTracker.FindVanillaFontStatic();
            if (_font != null)
            {
                if (_headerText != null) _headerText.font = _font;
                if (_bodyText   != null) _bodyText.font   = _font;
            }

            _headerText.text = Template(entry.Display?.Topic ?? entry.Title ?? "", entry);

            if (player != null) ConversationNodeState.SetCurrentNode(player, entry.Id, node.Id);

            BeginChoices(node.Choices?.Count ?? 1);
            var anyButton = false;
            if (node.Choices != null)
            {
                foreach (var choice in node.Choices)
                {
                    var locked = !PrerequisiteChecker.AllSatisfied(choice.Requires, player, Plugin.CurrentConfig);
                    if (locked && choice.HiddenWhenLocked) continue;
                    AddNodeChoiceButton(entry, choice, locked);
                    anyButton = true;
                }
            }
            if (!anyButton)
                AddChoiceButton(new ChoiceSpec { Text = "Dismiss" }, () => OnNodeConversationEnd(entry, fireGoto: null));
            EndChoices();

            SetBody(GuidanceDispatcher.RenderLocal(entry, node.Text) ?? "");
        }

        /// Clears the previous choice rows and picks how many buttons share a row. Three across
        /// stays readable at 750 px; past that, two per row keeps each label wide enough to read
        /// instead of squeezing every option into a sliver.
        ///
        /// The container stays inactive while rows are created so each TMP label Awakes only when
        /// EndChoices reactivates it, by which point the font is assigned (TMP Awake font rule).
        private void BeginChoices(int expectedCount)
        {
            _choiceContainer.SetActive(false);
            _choiceLabels.Clear();
            _choiceButtonLayouts.Clear();
            foreach (Transform child in _choiceContainer.transform)
                Destroy(child.gameObject);

            _currentChoiceRow = null;
            _rowCount    = 0;
            _rowCapacity = expectedCount <= 3 ? Mathf.Max(1, expectedCount) : 2;
        }

        /// Reactivates the rows, settles the layout, then grows any button whose label wrapped
        /// onto extra lines so no choice text is ever clipped by its own button.
        private void EndChoices()
        {
            _choiceContainer.SetActive(true);

            var containerRect = (RectTransform)_choiceContainer.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);

            // Labels are built against a stale rect width (the container was inactive), so the
            // first wrap pass can break at the wrong column — redo it now that widths are real.
            for (var i = 0; i < _choiceLabels.Count; i++)
            {
                var label = _choiceLabels[i];
                if (label == null) continue;
                label.ForceMeshUpdate(true, true);

                if (i >= _choiceButtonLayouts.Count) continue;
                var le = _choiceButtonLayouts[i];
                if (le == null) continue;
                // 8 px of vertical breathing room around the wrapped label.
                le.preferredHeight = Mathf.Max(40f, label.preferredHeight + 8f);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
        }

        /// Returns the row a new button should join, opening another one when the current row
        /// has reached _rowCapacity.
        private Transform NextChoiceRow()
        {
            if (_currentChoiceRow != null && _currentChoiceRow.childCount < _rowCapacity)
                return _currentChoiceRow;

            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_choiceContainer.transform, false);
            rowGo.AddComponent<RectTransform>();
            var h = rowGo.AddComponent<HorizontalLayoutGroup>();
            h.spacing                = 8f;
            h.childControlWidth      = true;
            h.childControlHeight     = true;
            h.childForceExpandWidth  = true;
            h.childForceExpandHeight = true;
            h.padding = new RectOffset(0, 0, 0, 0);

            _currentChoiceRow = rowGo.transform;
            _rowCount++;
            return _currentChoiceRow;
        }

        private void AddNodeChoiceButton(GuidanceEntry entry, NodeChoiceSpec choice, bool locked)
        {
            var label = choice.Label ?? "";
            if (locked && !string.IsNullOrEmpty(choice.LockedHint))
                label += $"  (Locked: {choice.LockedHint})";

            AddChoiceButton(new ChoiceSpec { Text = label },
                () => OnNodeChoiceSelected(entry, choice), interactable: !locked);
        }

        private void OnNodeChoiceSelected(GuidanceEntry entry, NodeChoiceSpec choice)
        {
            if (!string.IsNullOrEmpty(choice.GotoNode))
            {
                var next = FindNode(choice.GotoNode);
                if (next == null)
                {
                    Plugin.Log.LogWarning($"[conversation] '{entry.Id}' goto_node '{choice.GotoNode}' not found.");
                    OnNodeConversationEnd(entry, fireGoto: null);
                    return;
                }
                RenderNode(entry, next);
                return;
            }

            if (!string.IsNullOrEmpty(choice.Goto))
            {
                OnNodeConversationEnd(entry, choice.Goto);
                return;
            }

            // No goto_node and no goto — this choice ends the conversation (same
            // "no goto = dismiss" rule as the flat ChoiceSpec path).
            OnNodeConversationEnd(entry, fireGoto: null);
        }

        /// Closes the panel, marks the entry fired (once/cooldown/max_fires), clears its
        /// saved node so a future re-entry starts fresh at nodes[0], and optionally fires
        /// a cross-entry goto.
        private void OnNodeConversationEnd(GuidanceEntry entry, string fireGoto)
        {
            Close();

            var player = Player.m_localPlayer;
            if (player != null && entry != null)
            {
                if (entry.Once) SeenTracker.MarkFired(player, entry.Id, entry.Scope);
                var maxFires = entry.Trigger?.MaxFires ?? 0;
                if (maxFires > 0) SeenTracker.IncrementFireCount(player, entry.Id);
                SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);
                ConversationNodeState.Clear(player, entry.Id);
            }

            if (!string.IsNullOrEmpty(fireGoto))
                GuidanceDispatcher.FireById(fireGoto);
        }

        public void Close()
        {
            _isOpen        = false;
            _currentEntry  = null;
            gameObject.SetActive(false);
            CaptureCursor();
        }

        /// While the panel is open, keep the OS cursor free every frame. Valheim's
        /// GameCamera re-captures the mouse on its own update, so a one-shot unlock
        /// in Open() is not enough — we re-assert it here.
        private void Update()
        {
            if (!_isOpen) return;
            if (GameCamera.instance != null) GameCamera.instance.m_mouseCapture = false;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        /// Release the cursor: GameCamera.m_mouseCapture=false makes UpdateMouseCapture
        /// take its "no capture" branch. The TakeInput patches (below) stop the player
        /// from moving / looking / acting while the panel is up.
        private static void FreeCursor()
        {
            if (GameCamera.instance != null) GameCamera.instance.m_mouseCapture = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        /// Restore normal mouse capture when the panel closes.
        private static void CaptureCursor()
        {
            if (GameCamera.instance != null) GameCamera.instance.m_mouseCapture = true;
        }

        // ── Private helpers ────────────────────────────────────────────────────

        /// Expand {playerName}/{player_name}/… and apply the entry's `highlight:` rules to the
        /// panel chrome (header topic/title and choice labels). Body text is rendered by its own
        /// call site. `entry` is null for the multi-quest picker, which has no single entry.
        private static string Template(string text, GuidanceEntry entry)
            => GuidanceDispatcher.RenderLocal(entry, text) ?? "";

        private void AddChoiceButton(ChoiceSpec choice, System.Action onClick = null, bool interactable = true)
        {
            var btnGo = new GameObject("Btn");
            btnGo.transform.SetParent(NextChoiceRow(), false);

            // The row's HorizontalLayoutGroup controls width; the LayoutElement below carries
            // the height, which EndChoices raises to fit a label that wrapped onto extra lines.
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(0f, 40f);

            var bg = btnGo.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.07f);

            var btn = btnGo.AddComponent<Button>();
            btn.interactable = interactable;
            var cols = btn.colors;
            cols.normalColor      = Color.white;
            cols.highlightedColor = new Color(0.88f, 0.75f, 0.47f, 0.28f);
            cols.pressedColor     = new Color(0.70f, 0.60f, 0.35f, 0.50f);
            cols.disabledColor    = new Color(1f, 1f, 1f, 0.25f);
            btn.colors        = cols;
            btn.targetGraphic = bg;

            var le = btnGo.AddComponent<LayoutElement>();
            le.flexibleWidth   = 1f;
            le.minWidth        = 60f;
            le.minHeight       = 40f;
            le.preferredHeight = 40f;
            _choiceButtonLayouts.Add(le);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-6f, -2f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.text             = Template(choice.Text ?? "", _currentEntry);
            label.fontSize         = 13f;
            label.alignment        = TextAlignmentOptions.Center;
            label.color            = interactable ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            // Fixed small font + word wrap so longer labels (picker entry titles, locked-hint
            // suffixes) flow onto a second line inside the button instead of being clipped.
            // NOT auto-sizing: TMP auto-size resolves the largest font that fits on ONE line
            // against the rect and then truncates, which is the opposite of what we want here.
            // Overflow (not Truncate) so a wrapped second line is never cut off.
            label.enableWordWrapping = true;
            label.overflowMode      = TextOverflowModes.Overflow;
            label.enableAutoSizing  = false;
            _choiceLabels.Add(label);

            var capturedChoice = choice;
            btn.onClick.AddListener(() => (onClick ?? (() => OnChoiceSelected(capturedChoice)))());
        }

        /// One row in the multi-quest picker — label is the entry's title (falls back to id).
        private void AddSelectionButton(GuidanceEntry entry, string npcSubject)
        {
            AddChoiceButton(new ChoiceSpec { Text = entry.Title ?? entry.Id },
                () => OnEntrySelected(entry, npcSubject));
        }

        private void OnEntrySelected(GuidanceEntry entry, string npcSubject)
        {
            Close();
            if (entry == null) return;
            GuidanceDispatcher.FireEntry(entry,
                new TriggerEvent { Type = "npc_conversation", Subject = npcSubject });
        }

        private void OnChoiceSelected(ChoiceSpec choice)
        {
            var entry = _currentEntry;
            Close();

            if (entry != null)
            {
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    if (entry.Once) SeenTracker.MarkFired(player, entry.Id, entry.Scope);
                    var maxFires = entry.Trigger?.MaxFires ?? 0;
                    if (maxFires > 0) SeenTracker.IncrementFireCount(player, entry.Id);
                    SeenTracker.MarkCooldown(entry.Id, entry.Cooldown, Time.time);

                    if (choice?.Rewards != null && choice.Rewards.Count > 0)
                        Rewards.RewardDispatcher.Grant(choice.Rewards, player);
                }
            }

            if (!string.IsNullOrEmpty(choice?.Goto))
                GuidanceDispatcher.FireById(choice.Goto);
        }
    }

    // ── Input lock patches ───────────────────────────────────────────────────────
    // While the conversation panel is open the player must have exactly one option:
    // move the mouse and click a choice. These gate every other action.

    /// Gates the action block in Player.Update — movement, attack, interact (E),
    /// item use, and the inventory-open check all live inside `if (TakeInput())`,
    /// so returning false here disables all of them (just like an open map does).
    [HarmonyPatch(typeof(Player), nameof(Player.TakeInput))]
    internal static class NpcConvPlayerTakeInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (NpcConversationPanel.IsOpen) __result = false;
        }
    }

    /// Gates mouse-LOOK + WASD, which run through PlayerController's own private
    /// TakeInput (separate from Player.TakeInput). Without this the camera would
    /// still rotate with the freed cursor.
    [HarmonyPatch(typeof(PlayerController), "TakeInput", new[] { typeof(bool) })]
    internal static class NpcConvPlayerControllerTakeInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (NpcConversationPanel.IsOpen) __result = false;
        }
    }

    /// Block the ESC pause/options menu while the panel is open, so the only way
    /// out is to click a choice. (Choices always include at least a Dismiss button.)
    [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
    internal static class NpcConvMenuShowPatch
    {
        private static bool Prefix() => !NpcConversationPanel.IsOpen;
    }

    /// Block the inventory screen from opening while the panel is up. The inventory
    /// toggle lives in InventoryGui.Update (gated only by Menu/Console visibility),
    /// not inside Player.TakeInput, so the TakeInput patch above does not catch it —
    /// we suppress InventoryGui.Show directly.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class NpcConvInventoryShowPatch
    {
        private static bool Prefix() => !NpcConversationPanel.IsOpen;
    }
}
