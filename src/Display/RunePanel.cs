using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimServerGuide.Config;
using ValheimServerGuide.Triggers;

namespace ValheimServerGuide.Display
{
    /// Custom themed panel for the `rune` display mode. Replaces the vanilla
    /// TextViewer.Rune reading with a Valheim-styled card that supports per-entry
    /// customization: a styled header, a word-wrapped body/description, an optional
    /// bullet list, and configurable fonts/colors/alignment (see RuneStyleSpec).
    ///
    /// Vanilla assets only — the game font (resolved lazily the same way the Codex and
    /// Conversation panels do) plus Image color fills. No custom textures.
    ///
    /// Behaviour matches the old rune mode: the screen darkens, ghost mode is engaged
    /// (invulnerable + undetected) while the reading is up, and there is NO input lock —
    /// the player dismisses it at will by pressing Use (E) / Escape, or clicking.
    public class RunePanel : MonoBehaviour
    {
        public static RunePanel Instance { get; private set; }
        public bool IsOpen { get; private set; }

        // ── Themed defaults ──────────────────────────────────────────────────────
        // Taken from VanillaUi, so the reading is drawn in the player inventory's own palette —
        // orange headings over parchment body text — exactly like the Codex. The two fill colours
        // are only the fallbacks for when the vanilla sprites cannot be resolved.
        private static readonly Color DefBackground = VanillaUi.PanelFill;
        private static readonly Color DefAccent     = VanillaUi.DividerCol;
        private static readonly Color DefHeader     = VanillaUi.Orange;
        private static readonly Color DefBody       = VanillaUi.Beige;
        private static readonly Color DefItem       = VanillaUi.Beige;
        private static readonly Color DefFooter     = VanillaUi.Dim;

        // ── Font sizes ───────────────────────────────────────────────────────────
        // Matched to the vanilla inventory window (and therefore the Codex), which titles at ~20
        // and sets body text at 16 in the game's own serif face. The previous numbers were tuned
        // for a sans fallback and read a step off in that font.
        private const float FsHeader = 22f;
        private const float FsBody   = 16f;
        private const float FsItem   = 15f;
        private const float FsFooter = 13f;

        /// Ceiling for the whole panel as a fraction of screen height. Past this the body/list
        /// area scrolls instead of the panel continuing to grow off the top and bottom edges.
        private const float MaxHeightFraction = 0.86f;

        // ── Scene objects ────────────────────────────────────────────────────────
        private GameObject _uiRoot;
        private Image _backdrop;
        private Image _panelBg;
        private Image _contentBg;
        private RectTransform _panelRect;
        private TMP_Text _headerText;
        private Image _divider;
        private TMP_Text _bodyText;
        private Transform _listContent;
        private TMP_Text _footerText;
        private RectTransform _contentRect;
        private LayoutElement _viewportLe;
        private ScrollRect _contentScroll;

        private TMP_FontAsset _font;
        /// True once `_font` is the inventory window's own font rather than a provisional fallback
        /// resolved before the GUI scene existed.
        private bool _fontFromInventory;
        private float _openTime;

        // ── Fade ─────────────────────────────────────────────────────────────────
        private CanvasGroup _canvasGroup;
        private Coroutine _fadeRoutine;
        private float _fadeInDuration = 0.35f;
        private float _fadeOutDuration = 0.35f;

        // ── Factory ───────────────────────────────────────────────────────────────

        public static RunePanel Get()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("VSG_RunePanel");
            go.SetActive(false); // inactive during build → TMP Awake suppressed until font set
            Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiLayers.Rune;
            go.AddComponent<GraphicRaycaster>();

            Instance = go.AddComponent<RunePanel>();
            Instance.BuildPanel();
            return Instance;
        }

        // ── Construction ────────────────────────────────────────────────────────────

        private void BuildPanel()
        {
            // Dedicated root canvas kept on this GameObject.
            _uiRoot = gameObject;

            // CanvasGroup on the root fades the backdrop + panel together as one unit.
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

            // Full-screen darkening backdrop — click to dismiss (when the cursor is free).
            var backdropGo = new GameObject("Backdrop");
            backdropGo.transform.SetParent(transform, false);
            var bdRect = backdropGo.AddComponent<RectTransform>();
            bdRect.anchorMin = Vector2.zero;
            bdRect.anchorMax = Vector2.one;
            bdRect.offsetMin = Vector2.zero;
            bdRect.offsetMax = Vector2.zero;
            _backdrop = backdropGo.AddComponent<Image>();
            _backdrop.color = new Color(0f, 0f, 0f, 0.72f);
            var bdBtn = backdropGo.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(Close);

            // Centered panel. A VerticalLayoutGroup stacks header / divider / body / list /
            // footer; a ContentSizeFitter grows the panel height to fit the content while the
            // width stays fixed (set per-entry from RuneStyleSpec.Width).
            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            _panelRect = panelGo.AddComponent<RectTransform>();
            _panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
            _panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
            _panelRect.pivot            = new Vector2(0.5f, 0.5f);
            _panelRect.sizeDelta        = new Vector2(620f, 200f);
            _panelRect.anchoredPosition = Vector2.zero;

            // The frame art lives on its own stretched child rather than on the panel root, and is
            // kept out of the vertical stack with ignoreLayout. `Image` is itself an ILayoutElement
            // that reports its sprite's NATIVE pixel height, and the root's ContentSizeFitter takes
            // the largest preferred height it finds on the GameObject — so a carved wood sprite on
            // the root would size the card to the texture instead of to its text.
            var bgGo = new GameObject("PanelBg");
            bgGo.transform.SetParent(panelGo.transform, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bgGo.AddComponent<LayoutElement>().ignoreLayout = true;
            _panelBg = bgGo.AddComponent<Image>();
            _panelBg.color = DefBackground;

            var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment         = TextAnchor.UpperCenter;
            vlg.spacing                = 12f;
            vlg.padding                = new RectOffset(28, 28, 22, 20);

            var fitter = panelGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; // width is fixed
            fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            // Header.
            _headerText = MakeText(panelGo.transform, "Header", FsHeader, FontStyles.Bold,
                DefHeader, TextAlignmentOptions.Center);

            // Divider rule.
            var divGo = new GameObject("Divider");
            divGo.transform.SetParent(panelGo.transform, false);
            divGo.AddComponent<RectTransform>();
            _divider = divGo.AddComponent<Image>();
            _divider.color = DefAccent;
            var divLe = divGo.AddComponent<LayoutElement>();
            divLe.minHeight       = 2f;
            divLe.preferredHeight = 2f;

            // Scrollable content area holding the body and the bullet list. The viewport's
            // preferred height is set per-reading in ClampContentHeight: it matches the content
            // exactly until the panel would outgrow the screen, and only then clamps so the rest
            // scrolls. Nothing is ever truncated.
            var viewportGo = new GameObject("ContentViewport");
            viewportGo.transform.SetParent(panelGo.transform, false);
            var viewportRt = viewportGo.AddComponent<RectTransform>();
            viewportRt.pivot = new Vector2(0.5f, 1f);
            viewportGo.AddComponent<RectMask2D>();
            // The darker interior box the vanilla windows read long text in. Added before
            // WheelScroller.Attach so it doubles as the viewport's wheel catcher rather than
            // having a second transparent Image stacked on top of it.
            _contentBg = viewportGo.AddComponent<Image>();
            _contentBg.color = VanillaUi.InsetFill;
            _viewportLe = viewportGo.AddComponent<LayoutElement>();
            // Pinned so the Image above (an ILayoutElement reporting its sprite's native size)
            // cannot drive the viewport height on the layout passes that run before
            // ClampContentHeight sets the real one.
            _viewportLe.minHeight       = 0f;
            _viewportLe.preferredHeight = 0f;

            _contentScroll = viewportGo.AddComponent<ScrollRect>();
            _contentScroll.horizontal   = false;
            _contentScroll.vertical     = true;
            _contentScroll.movementType = ScrollRect.MovementType.Clamped;
            _contentScroll.viewport     = viewportRt;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            _contentRect = contentGo.AddComponent<RectTransform>();
            _contentRect.anchorMin = new Vector2(0f, 1f);
            _contentRect.anchorMax = new Vector2(1f, 1f);
            _contentRect.pivot     = new Vector2(0.5f, 1f);
            // Zero the offsets explicitly. With a horizontal stretch anchor the rect width is
            // (viewport width + sizeDelta.x), and a freshly added RectTransform does not start at
            // zero — leaving it made the content wider than the viewport, so the body wrapped at
            // that oversized width and got clipped on BOTH sides by the RectMask2D below.
            _contentRect.offsetMin = Vector2.zero;
            _contentRect.offsetMax = Vector2.zero;
            var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVlg.childControlWidth      = true;
            contentVlg.childForceExpandWidth  = true;
            contentVlg.childControlHeight     = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childAlignment         = TextAnchor.UpperLeft;
            contentVlg.spacing                = 12f;
            // Keeps the text off the interior box's carved edge. Inside the content (not the
            // viewport) so ClampContentHeight's measure of _contentRect already accounts for it.
            contentVlg.padding                = new RectOffset(12, 12, 10, 10);
            var contentFitter = contentGo.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            _contentScroll.content = _contentRect;
            // Fixed pixels per notch — see WheelScroller for why sensitivity alone does not work.
            // Also installs the viewport's wheel catcher, which this panel needs: every text here
            // is raycastTarget = false, so nothing inside the scroll view could receive the event.
            WheelScroller.Attach(_contentScroll);

            // Body / description.
            _bodyText = MakeText(contentGo.transform, "Body", FsBody, FontStyles.Normal,
                DefBody, TextAlignmentOptions.TopLeft);
            _bodyText.enableWordWrapping = true;
            _bodyText.overflowMode       = TextOverflowModes.Overflow;

            // List container (one styled row per item).
            var listGo = new GameObject("List");
            listGo.transform.SetParent(contentGo.transform, false);
            _listContent = listGo.AddComponent<RectTransform>();
            var listVlg = listGo.AddComponent<VerticalLayoutGroup>();
            listVlg.childControlWidth      = true;
            listVlg.childForceExpandWidth  = true;
            listVlg.childControlHeight     = true;
            listVlg.childForceExpandHeight = false;
            listVlg.childAlignment         = TextAnchor.UpperLeft;
            listVlg.spacing                = 4f;

            // Footer hint.
            _footerText = MakeText(panelGo.transform, "Footer", FsFooter, FontStyles.Italic,
                DefFooter, TextAlignmentOptions.Center);
            _footerText.text = "Press [E] to continue";
        }

        /// Create a child TextMeshProUGUI with a LayoutElement-friendly rect (the parent
        /// VerticalLayoutGroup controls its size). Font is applied later in EnsureFont.
        ///
        /// Every label wraps and overflows: the panel grows (and past the screen cap, scrolls) to
        /// fit the text, so no authored line is ever clipped or ellipsised. See CRIT-25.
        private TMP_Text MakeText(Transform parent, string name, float size,
            FontStyles style, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // Start from a zero-size top-left rect. The parent layout group overwrites this on
            // the first pass, but an un-initialised RectTransform can carry a stray size into the
            // TMP wrap measure before that happens.
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot     = new Vector2(0f, 1f);
            rt.sizeDelta = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize       = size;
            tmp.fontStyle      = style;
            tmp.color          = color;
            tmp.alignment      = align;
            tmp.raycastTarget  = false;
            tmp.enableWordWrapping = true;
            tmp.overflowMode       = TextOverflowModes.Overflow;
            return tmp;
        }

        // ── Public API ──────────────────────────────────────────────────────────────

        /// Show a rune reading. `renderedText` is already template-expanded (the body).
        public void Open(GuidanceEntry entry, string renderedText)
        {
            if (_uiRoot == null) return;

            EnsureFont();

            var style      = entry.Display?.Rune;
            var playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "";

            // ── Header ──
            var headerRaw = !string.IsNullOrEmpty(style?.Header)
                ? style.Header
                : entry.Display?.Topic ?? "";
            var header = TextHighlighter.Apply(
                GuidanceDispatcher.TemplateText(headerRaw, null, playerName), entry) ?? "";
            _headerText.text        = header;
            _headerText.gameObject.SetActive(!string.IsNullOrEmpty(header));
            _headerText.fontSize    = style?.HeaderFontSize ?? FsHeader;
            _headerText.fontStyle   = ParseStyle(style?.HeaderStyle, FontStyles.Bold);
            _headerText.color       = ParseColor(style?.HeaderColor, DefHeader);
            _headerText.alignment   = ParseAlign(style?.HeaderAlignment, TextAlignmentOptions.Center);

            // Only show the divider when there is a header to separate.
            _divider.gameObject.SetActive(!string.IsNullOrEmpty(header));
            _divider.color = ParseColor(style?.AccentColor, DefAccent);

            // ── Body ──
            _bodyText.text        = renderedText ?? "";
            _bodyText.gameObject.SetActive(!string.IsNullOrEmpty(renderedText));
            _bodyText.fontSize    = style?.BodyFontSize ?? FsBody;
            _bodyText.fontStyle   = ParseStyle(style?.BodyStyle, FontStyles.Normal);
            _bodyText.color       = ParseColor(style?.BodyColor, DefBody);
            _bodyText.alignment   = ParseAlign(style?.BodyAlignment, TextAlignmentOptions.TopLeft);

            // ── Panel geometry / fill ──
            // Never wider than the screen (minus a margin): the body wraps to the panel, so an
            // oversized `width:` would push the text off both edges instead of stacking lines.
            var maxWidth = Mathf.Max(240f, CanvasSize().x - 40f);
            var width = Mathf.Clamp(style?.Width ?? 620f, 240f, Mathf.Min(1200f, maxWidth));
            _panelRect.sizeDelta = new Vector2(width, _panelRect.sizeDelta.y);
            ApplyVanillaStyle(style);

            // ── List ──
            BuildList(style, playerName, entry);

            // ── Fade ──
            _fadeInDuration  = Mathf.Max(0f, style?.FadeIn ?? 0.35f);
            _fadeOutDuration = Mathf.Max(0f, style?.FadeOut ?? 0.35f);

            IsOpen    = true;
            _openTime = Time.unscaledTime;
            GuidanceDisplay.BeginRuneGhost();

            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            // Start from transparent unless we're re-triggering mid fade-out (in which case
            // fading in from the current alpha reads as a smooth crossfade, not a flash).
            if (!_uiRoot.activeSelf) _canvasGroup.alpha = 0f;
            _uiRoot.SetActive(true);

            // Content was populated while children were (re)built; force a layout pass so the
            // VerticalLayoutGroup + ContentSizeFitter compute the final panel height this frame.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
            // Labels whose text was assigned while the root was inactive carry a mesh (and a
            // cached wrap width) from the previous rect. Regenerate them now that the widths are
            // real, or the first frame re-uses the old line breaks.
            RefreshTextGeometry();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);
            ClampContentHeight();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_panelRect);

            if (_fadeInDuration > 0f)
                _fadeRoutine = StartCoroutine(FadeRoutine(_canvasGroup.alpha, 1f, _fadeInDuration, null));
            else
                _canvasGroup.alpha = 1f;
        }

        /// Draw the card in the vanilla player-inventory window's art: its carved wood for the
        /// frame, the darker interior box for the reading itself. Re-run on every Open, because
        /// the GUI scene the art is read from may not have existed when the panel was built.
        ///
        /// An authored `background_color:` wins over the frame — asking for a specific fill and
        /// getting carved wood tinted with it is not what the author wrote.
        private void ApplyVanillaStyle(RuneStyleSpec style)
        {
            VanillaUi.TryResolve();

            if (string.IsNullOrWhiteSpace(style?.BackgroundColor))
            {
                VanillaUi.ApplyPanelFlex(_panelBg);
            }
            else
            {
                _panelBg.sprite = null;
                _panelBg.type   = Image.Type.Simple;
                _panelBg.color  = ParseColor(style.BackgroundColor, DefBackground);
            }

            VanillaUi.ApplyInset(_contentBg);
        }

        /// Sizes the scroll viewport to the body + list. The reading grows to fit whatever the
        /// author wrote; only once the whole panel would exceed MaxHeightFraction of the screen
        /// does the content area stop growing and start scrolling, so a very long reading is a
        /// wheel-scroll away instead of being cut off (or pushed off-screen).
        private void ClampContentHeight()
        {
            if (_viewportLe == null || _contentRect == null) return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRect);
            var needed = LayoutUtility.GetPreferredHeight(_contentRect);

            // Everything outside the content area: padding, header, divider, footer, spacing.
            var headerH = _headerText.gameObject.activeSelf ? _headerText.preferredHeight : 0f;
            var chrome = 42f + headerH + 2f + 24f + _footerText.preferredHeight + 24f;
            var maxContent = Mathf.Max(60f, CanvasSize().y * MaxHeightFraction - chrome);

            _viewportLe.minHeight       = Mathf.Min(needed, maxContent);
            _viewportLe.preferredHeight = Mathf.Min(needed, maxContent);

            _contentScroll.enabled = needed > maxContent + 0.5f;
            _contentScroll.verticalNormalizedPosition = 1f;
        }

        /// Canvas dimensions in the same units the panel is laid out in.
        ///
        /// Derived from the screen and the canvas's own `scaleFactor` (1 while this canvas has no
        /// CanvasScaler, and correct if one is ever added) rather than read off the canvas
        /// RectTransform: that rect is only filled in once Unity has laid the canvas out, and
        /// `Open()` sizes the panel while the root is still INACTIVE — where the rect is still the
        /// default 100×100 a fresh RectTransform carries. Reading it there made `maxWidth` collapse
        /// to the 240 floor, so the first reading of a session came out as a narrow column no matter
        /// what `width:` the author asked for.
        private Vector2 CanvasSize()
        {
            var canvas = GetComponent<Canvas>();
            var scale  = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            return new Vector2(Screen.width / scale, Screen.height / scale);
        }

        /// Regenerate every label's geometry against its current rect. Text assigned while the
        /// root was inactive keeps the wrap points it computed against the old (or zero) width,
        /// and a layout rebuild alone fixes the rects without re-flowing the glyphs.
        private void RefreshTextGeometry()
        {
            foreach (var t in GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (t == null) continue;
                t.SetAllDirty();
                t.ForceMeshUpdate();
            }
        }

        /// Rebuild the bullet list from RuneStyleSpec.Items. The container is toggled inactive
        /// while rows are created so each new TMP label's Awake fires only after its font is set
        /// (TMP null-font Awake warning rule).
        private void BuildList(RuneStyleSpec style, string playerName, GuidanceEntry entry)
        {
            for (var i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            var items = style?.Items;
            if (items == null || items.Count == 0)
            {
                _listContent.gameObject.SetActive(false);
                return;
            }

            _listContent.gameObject.SetActive(false);

            var bullet    = style.Bullet ?? "•";
            var prefix    = string.IsNullOrEmpty(bullet) ? "" : bullet + "  ";
            var itemColor = ParseColor(style.ItemColor, DefItem);
            var itemStyle = ParseStyle(style.ItemStyle, FontStyles.Normal);
            var itemSize  = style.ItemFontSize <= 0f ? FsItem : style.ItemFontSize;

            foreach (var raw in items)
            {
                var text = TextHighlighter.Apply(
                    GuidanceDispatcher.TemplateText(raw ?? "", null, playerName), entry) ?? "";
                var row  = MakeText(_listContent, "Item", itemSize, itemStyle, itemColor,
                    TextAlignmentOptions.TopLeft);
                ApplyFont(row);
                row.text                = prefix + text;
                row.enableWordWrapping  = true;
                row.overflowMode        = TextOverflowModes.Overflow;
            }

            _listContent.gameObject.SetActive(true);
        }

        /// Dismiss with a fade-out (duration from RuneStyleSpec.FadeOut).
        public void Close() => CloseInternal(immediate: false);

        /// Dismiss instantly, skipping any fade-out. Used when an intro cinematic
        /// preempts the reading and on session teardown, where a lingering fade
        /// coroutine would otherwise carry state across the transition.
        public void CloseImmediate() => CloseInternal(immediate: true);

        private void CloseInternal(bool immediate)
        {
            if (!IsOpen) return;
            IsOpen = false;

            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }

            // If an intro cinematic has taken over it owns the ghost-mode lifetime (it engaged
            // ghost mode too, and releasing here would strip the intro's invulnerability). Mirror
            // TextViewerHidePatch's "intro owns its own release" rule.
            if (immediate || _fadeOutDuration <= 0f)
            {
                if (_uiRoot != null) _uiRoot.SetActive(false);
                if (!GuidanceDisplay.IntroLockActive) GuidanceDisplay.ReleaseGhostMode();
                return;
            }

            _fadeRoutine = StartCoroutine(FadeRoutine(_canvasGroup.alpha, 0f, _fadeOutDuration, () =>
            {
                if (_uiRoot != null) _uiRoot.SetActive(false);
                if (!GuidanceDisplay.IntroLockActive) GuidanceDisplay.ReleaseGhostMode();
                _fadeRoutine = null;
            }));
        }

        private IEnumerator FadeRoutine(float from, float to, float duration, System.Action onComplete)
        {
            var t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            _canvasGroup.alpha = to;
            onComplete?.Invoke();
        }

        // ── Per-frame ───────────────────────────────────────────────────────────────

        private void Update()
        {
            if (!IsOpen) return;

            // Intro cinematic takes over — close instantly, no fade.
            if (GuidanceDisplay.IntroLockActive) { CloseImmediate(); return; }

            // Dismiss on Use (E) / Escape, matching the vanilla rune reading. A short grace
            // window prevents a key the player was already holding from skipping instantly.
            if (Time.unscaledTime - _openTime < 0.3f) return;
            if (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")
                || Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        // ── Font ──────────────────────────────────────────────────────────────────

        private void ApplyFont(TMP_Text t)
        {
            if (_font != null && t != null) t.font = _font;
        }

        /// Resolve the font the vanilla inventory window itself draws with and push it to the
        /// card's labels.
        ///
        /// That font can only be read once the GUI scene is up, so anything resolved before then
        /// is provisional and re-resolved on the next reading rather than cached for the session —
        /// the same rule the Codex follows, which is what keeps the two panels in one typeface.
        private void EnsureFont()
        {
            if (_font == null || !_fontFromInventory)
            {
                if (VanillaUi.TryResolve() && VanillaUi.Font != null)
                {
                    _font              = VanillaUi.Font;
                    _fontFromInventory = true;
                }
                else if (_font == null)
                {
                    _font = (GuidanceHudTracker.Instance != null
                                ? GuidanceHudTracker.Instance.ResolvedFont : null)
                            ?? GuidanceHudTracker.FindVanillaFontStatic();
                }
            }
            if (_font == null) return;
            ApplyFont(_headerText);
            ApplyFont(_bodyText);
            ApplyFont(_footerText);
            // List rows are rebuilt per reading, but a font that only arrived now has to reach the
            // ones already on screen too.
            foreach (Transform child in _listContent)
            {
                var t = child.GetComponent<TMP_Text>();
                if (t != null) ApplyFont(t);
            }
        }

        // ── Parse helpers ───────────────────────────────────────────────────────────

        /// Parse "#RRGGBB" / "#RRGGBBAA" (leading '#' optional). Returns fallback on empty/invalid.
        private static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        /// Parse a space/comma-separated font style list (Normal | Bold | Italic | Underline).
        private static FontStyles ParseStyle(string s, FontStyles fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            var result = FontStyles.Normal;
            var any = false;
            foreach (var tok in s.Split(' ', ',', '|', '+'))
            {
                switch (tok.Trim().ToLowerInvariant())
                {
                    case "":         break;
                    case "normal":   any = true; break;
                    case "bold":     result |= FontStyles.Bold; any = true; break;
                    case "italic":   result |= FontStyles.Italic; any = true; break;
                    case "underline":result |= FontStyles.Underline; any = true; break;
                    case "uppercase":result |= FontStyles.UpperCase; any = true; break;
                    case "strikethrough": result |= FontStyles.Strikethrough; any = true; break;
                }
            }
            return any ? result : fallback;
        }

        /// Parse Left | Center | Right into a top-aligned TMP alignment.
        private static TextAlignmentOptions ParseAlign(string s, TextAlignmentOptions fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            switch (s.Trim().ToLowerInvariant())
            {
                case "left":   return TextAlignmentOptions.TopLeft;
                case "center": return TextAlignmentOptions.Center;
                case "right":  return TextAlignmentOptions.TopRight;
                default:       return fallback;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
