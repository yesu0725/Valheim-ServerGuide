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
    /// Layout (750 × 185 px):
    ///   • Top edge is anchored to screen centre — the box occupies the lower half.
    ///   • Header (NPC name / topic) at the top in gold.
    ///   • Word-wrapped body text below.
    ///   • Choice buttons arranged in a single horizontal row at the bottom.
    ///
    /// Vanilla Unity UI only — no custom textures. Font resolved lazily from loaded
    /// TMP assets the same way GuidanceHudTracker does.
    public class NpcConversationPanel : MonoBehaviour
    {
        public static NpcConversationPanel Instance { get; private set; }
        internal static bool IsOpen => Instance != null && Instance._isOpen;

        private bool _isOpen;
        private GuidanceEntry _currentEntry;
        private List<ConversationNodeSpec> _nodes; // Phase 4 multi-node conversations

        private TMP_FontAsset _font;
        private TMP_Text _headerText;
        private TMP_Text _bodyText;
        private GameObject _choiceContainer;
        private readonly List<TMP_Text> _choiceLabels = new List<TMP_Text>();

        // ── Factory ────────────────────────────────────────────────────────────

        public static NpcConversationPanel Get()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("VSG_ConversationPanel");
            go.SetActive(false);          // inactive during build → TMP Awake suppressed
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            go.AddComponent<GraphicRaycaster>();

            Instance = go.AddComponent<NpcConversationPanel>();
            Instance.BuildPanel();
            return Instance;
        }

        // ── Panel construction ─────────────────────────────────────────────────

        private void BuildPanel()
        {
            // ── Background ───────────────────────────────────────────────────────
            // Anchor Y = 0.25 sits a quarter of the way up from the bottom — i.e.
            // midway between screen centre and the lower edge. pivot (0.5, 0.5)
            // centres the box on that point, so it occupies the lower-middle band.
            var bg = new GameObject("BG");
            bg.transform.SetParent(transform, false);
            var bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin        = new Vector2(0.5f, 0.25f);
            bgRt.anchorMax        = new Vector2(0.5f, 0.25f);
            bgRt.pivot            = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta        = new Vector2(750f, 185f);
            bgRt.anchoredPosition = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            // Darker, more opaque fill so the white body text reads clearly.
            bgImg.color = new Color(0.02f, 0.02f, 0.02f, 0.97f);

            // ── Header ───────────────────────────────────────────────────────────
            // Spans full width minus 24 px margins; 36 px tall; 10 px from top.
            var headerGo = new GameObject("Header");
            headerGo.transform.SetParent(bg.transform, false);
            var headerRt = headerGo.AddComponent<RectTransform>();
            headerRt.anchorMin        = new Vector2(0f, 1f);
            headerRt.anchorMax        = new Vector2(1f, 1f);
            headerRt.pivot            = new Vector2(0.5f, 1f);
            headerRt.sizeDelta        = new Vector2(-24f, 36f);
            headerRt.anchoredPosition = new Vector2(0f, -8f);
            _headerText = headerGo.AddComponent<TextMeshProUGUI>();
            _headerText.fontSize  = 20f;
            _headerText.fontStyle = FontStyles.Bold;
            _headerText.alignment = TextAlignmentOptions.Left;
            _headerText.color     = new Color(0.88f, 0.75f, 0.47f); // gold

            // ── Divider rule ─────────────────────────────────────────────────────
            var rule = new GameObject("Rule");
            rule.transform.SetParent(bg.transform, false);
            var ruleRt = rule.AddComponent<RectTransform>();
            ruleRt.anchorMin        = new Vector2(0f, 1f);
            ruleRt.anchorMax        = new Vector2(1f, 1f);
            ruleRt.pivot            = new Vector2(0.5f, 1f);
            ruleRt.sizeDelta        = new Vector2(-16f, 1f);
            ruleRt.anchoredPosition = new Vector2(0f, -48f);
            var ruleImg = rule.AddComponent<Image>();
            ruleImg.color         = new Color(0.88f, 0.75f, 0.47f, 0.30f);
            ruleImg.raycastTarget = false;

            // ── Body text (word-wrapped) ─────────────────────────────────────────
            // Height = 82 px; sits just below the divider. Overflow is clipped with
            // ellipsis to guard against runaway text pushing into the button row.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(bg.transform, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.anchorMin        = new Vector2(0f, 1f);
            bodyRt.anchorMax        = new Vector2(1f, 1f);
            bodyRt.pivot            = new Vector2(0.5f, 1f);
            bodyRt.sizeDelta        = new Vector2(-24f, 82f);
            bodyRt.anchoredPosition = new Vector2(0f, -52f);
            _bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
            _bodyText.fontSize          = 15f;
            _bodyText.alignment         = TextAlignmentOptions.TopLeft;
            _bodyText.color             = Color.white;
            _bodyText.enableWordWrapping = true;
            _bodyText.overflowMode      = TextOverflowModes.Ellipsis;

            // ── Choice container (horizontal) ────────────────────────────────────
            // Fixed 40 px strip anchored 10 px above the bottom edge of the panel.
            // HorizontalLayoutGroup distributes buttons equally across full width.
            _choiceContainer = new GameObject("Choices");
            _choiceContainer.transform.SetParent(bg.transform, false);
            var choiceRt = _choiceContainer.AddComponent<RectTransform>();
            choiceRt.anchorMin        = new Vector2(0f, 0f);
            choiceRt.anchorMax        = new Vector2(1f, 0f);
            choiceRt.pivot            = new Vector2(0.5f, 0f);
            choiceRt.sizeDelta        = new Vector2(-24f, 40f);
            choiceRt.anchoredPosition = new Vector2(0f, 10f);
            var hLayout = _choiceContainer.AddComponent<HorizontalLayoutGroup>();
            hLayout.spacing              = 8f;
            hLayout.childControlWidth    = true;
            hLayout.childControlHeight   = true;
            hLayout.childForceExpandWidth  = true;
            hLayout.childForceExpandHeight = true;
            hLayout.padding = new RectOffset(0, 0, 0, 0);
        }

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

            _headerText.text = entry.Display?.Topic ?? entry.Title ?? "";
            _bodyText.text   = renderedText ?? "";

            // Rebuild choice buttons — container stays inactive while rows are created
            // so each TMP label Awakes only when _choiceContainer.SetActive(true) runs,
            // at which point the font is already assigned (per TMP Awake memory rule).
            _choiceContainer.SetActive(false);
            _choiceLabels.Clear();
            foreach (Transform child in _choiceContainer.transform)
                Destroy(child.gameObject);

            var choices = entry.Conversation?.Choices;
            if (choices != null && choices.Count > 0)
            {
                foreach (var c in choices) AddChoiceButton(c);
            }
            else
            {
                AddChoiceButton(new ChoiceSpec { Text = "Dismiss" });
            }
            _choiceContainer.SetActive(true);
            FinalizeChoiceLayout();

            _isOpen = true;
            gameObject.SetActive(true);
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

            _headerText.text = npcDisplayName ?? "";
            _bodyText.text   = "What would you like to discuss?";

            _choiceContainer.SetActive(false);
            _choiceLabels.Clear();
            foreach (Transform child in _choiceContainer.transform)
                Destroy(child.gameObject);

            foreach (var entry in entries) AddSelectionButton(entry, npcSubject);
            _choiceContainer.SetActive(true);
            FinalizeChoiceLayout();

            _isOpen = true;
            gameObject.SetActive(true);
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

            RenderNode(entry, startNode);

            _isOpen = true;
            gameObject.SetActive(true);
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

            _headerText.text = entry.Display?.Topic ?? entry.Title ?? "";
            _bodyText.text   = GuidanceDispatcher.TemplateText(node.Text, null, player?.GetPlayerName()) ?? "";

            if (player != null) ConversationNodeState.SetCurrentNode(player, entry.Id, node.Id);

            _choiceContainer.SetActive(false);
            _choiceLabels.Clear();
            foreach (Transform child in _choiceContainer.transform)
                Destroy(child.gameObject);

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

            _choiceContainer.SetActive(true);
            FinalizeChoiceLayout();
        }

        /// Forces the choice row layout to settle (HorizontalLayoutGroup/LayoutElement widths)
        /// and then forces each label to redo its word-wrap pass against the now-correct rect.
        /// The labels are built while _choiceContainer is inactive (per the TMP Awake font rule),
        /// so the button widths aren't assigned yet at build time; without this rebuild the first
        /// wrap render can run against a stale rect width and wrap at the wrong column.
        private void FinalizeChoiceLayout()
        {
            if (_choiceContainer == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_choiceContainer.transform);
            foreach (var label in _choiceLabels)
            {
                if (label == null) continue;
                label.ForceMeshUpdate(true, true);
            }
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

        private void AddChoiceButton(ChoiceSpec choice, System.Action onClick = null, bool interactable = true)
        {
            var btnGo = new GameObject("Btn");
            btnGo.transform.SetParent(_choiceContainer.transform, false);

            // HorizontalLayoutGroup controls width; sizeDelta.y is the preferred height hint.
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
            le.flexibleWidth = 1f;
            le.minWidth      = 60f;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(btnGo.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-6f, -2f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.text             = choice.Text ?? "";
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
