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
    /// In-game Codex panel: lets players browse all guides they have triggered,
    /// re-read the current step message, and see upcoming locked steps.
    /// Read-only — cannot advance chains or re-fire triggers.
    /// Vanilla UI components only (no custom assets). See CRIT-14.
    public class GuidanceCodex : MonoBehaviour
    {
        public static GuidanceCodex Instance { get; internal set; }
        public bool IsOpen { get; private set; }

        // ── Scene roots ───────────────────────────────────────────────────────────────────────
        private GameObject _uiRoot;  // dedicated ScreenSpaceOverlay canvas
        private GameObject _panel;   // main centered panel

        // ── Panel geometry ────────────────────────────────────────────────────────────────────
        // The panel is drawn in the vanilla player-inventory frame (VanillaUi.Panel), whose art is
        // one fixed-aspect sprite rather than a 9-sliced one, so the panel sizes itself to that
        // aspect instead of to hard-coded numbers — see ResolvePanelSize.
        /// Thickness of the frame's carving at the default panel size. All content is inset by
        /// this much so nothing is drawn on top of the border; the live value (`_frameInset`) is
        /// scaled with the panel, since the frame art scales with it too.
        private const float BaseFrameInset = 22f;
        private const float HeaderHeight   = 48f;
        /// Gap between the two panes, and the width the vertical rule occupies inside it.
        private const float PaneGap      = 8f;
        /// Used until the vanilla sprite resolves, and whenever it turns out to be 9-sliceable
        /// (in which case any size draws correctly and there is no aspect to honour).
        private const float DefaultPanelWidth  = 880f;
        private const float DefaultPanelHeight = 560f;

        private float _panelWidth  = DefaultPanelWidth;
        private float _panelHeight = DefaultPanelHeight;
        private float _leftWidth   = 250f;
        private float _frameInset  = BaseFrameInset;

        // Rects re-derived whenever the panel is resized (ApplyGeometry).
        private RectTransform _panelRect;
        private RectTransform _headerRect;
        private RectTransform _headerSepRect;
        private RectTransform _contentRect;
        private RectTransform _leftRect;
        private RectTransform _vDivRect;
        private RectTransform _rightRect;

        // Images repainted once the vanilla sprites resolve (ApplyVanillaStyle).
        private Image _panelImg;
        private Image _leftImg;
        private Image _bodyBgImg;
        private Image _upcomingBgImg;
        /// Buttons restyled with the vanilla button sprite and its hover/press swaps.
        private readonly List<(Button button, Image image)> _buttons
            = new List<(Button, Image)>();
        private bool _styled;
        /// Screen size and GUI scale the panel was last fitted to, so either changing re-fits it.
        private Vector2 _fittedScreen;
        private float _fittedScale;

        // ── Left panel ────────────────────────────────────────────────────────────────────────
        private Transform _leftContent;  // VerticalLayoutGroup content for guide rows
        private readonly List<(GuidanceEntry entry, GameObject row, TMP_Text label)> _guideRows
            = new List<(GuidanceEntry, GameObject, TMP_Text)>();
        private GuidanceEntry _selected;

        // ── Left panel list ───────────────────────────────────────────────────────────────────
        // Row metrics. They must match the LayoutElement heights set in BuildQuestRow /
        // BuildCategoryRow, which is what the scroll content is sized from.
        private const float CatRowHeight   = 26f;
        private const float QuestRowHeight = 24f;
        private const float RowSpacing     = 1f;
        private const float FooterHeight   = 42f;
        /// Left-pane width minus the list padding (4+4), the row label's left inset (10) and the
        /// scrollbar gutter on the right (8). Quest titles wrap at this measure.
        private float QuestLabelWidth => _leftWidth - 8f - 10f - 8f;
        /// Width available to an "Upcoming Steps" row: the right pane, less the section's own 16px
        /// inset and the 8+8 padding inside its interior box.
        private float UpcomingRowWidth
            => _panelWidth - 2f * _frameInset - _leftWidth - PaneGap - 32f;

        // ── Font sizes ────────────────────────────────────────────────────────────────────────
        // Matched to the vanilla inventory window, which titles at ~20 and labels at ~16 in the
        // same serif face. The old sizes were tuned for a sans fallback and read a step too small
        // in the game's own font.
        private const float FsWindowTitle = 20f;
        private const float FsPaneHeader  = 15f;
        private const float FsCategory    = 14f;
        private const float FsQuestRow    = 14f;
        private const float FsEntryTitle  = 18f;
        private const float FsBadge       = 15f;
        private const float FsBody        = 16f;
        private const float FsButton      = 16f;
        private const float FsFooter      = 15f;
        private const float FsUpcoming    = 13f;

        /// Padding between a button's edge and its label, on both axes. Buttons are sized so this
        /// survives: the text never sits on the sprite's carved border.
        private const float ButtonPadX = 16f;
        private const float ButtonPadY = 4f;

        private TMP_Text _showHiddenText;
        private bool _showHidden;   // when true, hidden quests are listed (dimmed) so they can be unhidden
        private ScrollRect _listScroll;
        private readonly List<ListItem> _items = new List<ListItem>();

        /// One rendered row in the list: either a category header or a quest row.
        private struct ListItem
        {
            public bool Category;
            public string CategoryName;
            public GuidanceEntry Entry;
            public bool Hidden;
            /// Rendered label (already templated) and the height its wrapped form needs.
            /// Measured up front so the row's LayoutElement can pin a height that fits it.
            public string Label;
            public float Height;
        }

        /// Off-screen label used only to measure how tall a wrapped quest title will be. Sharing
        /// one instance keeps the measurement identical to what BuildQuestRow later renders.
        private TMP_Text _measureText;

        // ── Right panel ───────────────────────────────────────────────────────────────────────
        private TMP_Text _titleText;
        private TMP_Text _badgeText;
        private TMP_Text _bodyText;
        private RectTransform _bodyContentRect;
        // Rects that shift down when a long quest title needs a taller title band — see
        // LayoutTitleArea. Height below the band is fixed, so they all move together.
        private RectTransform _titleAreaRect;
        private RectTransform _titleDivRect;
        private RectTransform _bodyScrollRect;
        private const float TitleAreaBaseHeight = 54f;
        /// Height of the toggle-pill bar under the title.
        private const float ToggleBarHeight = 38f;
        /// Height reserved at the bottom of the right pane for the "Upcoming Steps" section while
        /// it has anything to show. The body scroll takes the space back when it does not.
        private const float UpcomingHeight = 150f;
        private ScrollRect _upcomingScroll;
        private Transform _upcomingContent;
        /// The whole "Upcoming Steps" section, hidden for entries that have none.
        private GameObject _upcomingGo;

        // ── Tracker pin toggle (in-progress quests only) ──────────────────────────────────────
        private GameObject _trackToggleGo;
        private TMP_Text _trackToggleText;

        // ── Hide-from-list toggle (any selected quest) ────────────────────────────────────────
        private GameObject _hideToggleGo;
        private TMP_Text _hideToggleText;

        // ── Font (lazy resolution, same pattern as GuidanceHudTracker) ───────────────────────
        private TMP_FontAsset _font;
        /// True once `_font` is the font the vanilla inventory window itself draws with, as opposed
        /// to a provisional fallback resolved before the GUI scene was available.
        private bool _fontFromInventory;

        // ── Palette ───────────────────────────────────────────────────────────────────────────
        // Taken from VanillaUi so the codex reads in the game's own colours: orange headings and
        // parchment body text, the same pairing the inventory and crafting windows use. The *Bg
        // entries are only the flat fallbacks for when a vanilla sprite cannot be resolved.
        private static readonly Color ColGold    = VanillaUi.Orange;
        private static readonly Color ColText    = VanillaUi.Beige;
        private static readonly Color ColLocked  = VanillaUi.Dim;
        private static readonly Color ColGreen   = VanillaUi.Green;
        private static readonly Color ColBg      = VanillaUi.PanelFill;
        private static readonly Color ColPanelBg = VanillaUi.InsetFill;
        private static readonly Color ColDivider = VanillaUi.DividerCol;
        private static readonly Color ColRowSel  = VanillaUi.RowSelFill;

        // ── Construction ─────────────────────────────────────────────────────────────────────

        /// Called once from HudAwakeCodexPatch after the component is added to the scene.
        public void BuildPanel()
        {
            // Dedicated root canvas — same pattern as GuidanceHudTracker.UiRoot().
            _uiRoot = new GameObject("VSG_CodexRoot");
            var canvas = _uiRoot.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiLayers.Codex;

            var srcScaler = Hud.instance != null
                ? Hud.instance.GetComponentInParent<Canvas>()?.GetComponent<CanvasScaler>()
                : null;
            var scaler = _uiRoot.AddComponent<CanvasScaler>();
            if (srcScaler != null)
            {
                scaler.uiScaleMode         = srcScaler.uiScaleMode;
                scaler.referenceResolution = srcScaler.referenceResolution;
                scaler.screenMatchMode     = srcScaler.screenMatchMode;
                scaler.matchWidthOrHeight  = srcScaler.matchWidthOrHeight;
            }
            _uiRoot.AddComponent<GraphicRaycaster>();

            // Build all children while the root is INACTIVE so each TextMeshProUGUI's Awake
            // (which warns on a null font) is deferred until Open(), where the font is applied
            // before reactivation. Also leaves the codex hidden/raycast-free until opened.
            _uiRoot.SetActive(false);

            // Semi-transparent backdrop — clicking it closes the codex.
            var backdrop = new GameObject("VSG_CodexBackdrop");
            backdrop.transform.SetParent(_uiRoot.transform, false);
            var bdRect = backdrop.AddComponent<RectTransform>();
            bdRect.anchorMin = Vector2.zero;
            bdRect.anchorMax = Vector2.one;
            bdRect.offsetMin = Vector2.zero;
            bdRect.offsetMax = Vector2.zero;
            var bdImg = backdrop.AddComponent<Image>();
            bdImg.color = new Color(0f, 0f, 0f, 0.55f);
            var bdBtn = backdrop.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(Close);

            // Main panel, centred. Drawn in the vanilla player-inventory frame; its size is
            // finalised by ApplyGeometry once that sprite's aspect is known.
            _panel = new GameObject("VSG_CodexPanel");
            _panel.transform.SetParent(_uiRoot.transform, false);
            var panelRect = _panel.AddComponent<RectTransform>();
            _panelRect                 = panelRect;
            panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRect.pivot            = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta        = new Vector2(_panelWidth, _panelHeight);
            panelRect.anchoredPosition = Vector2.zero;
            _panelImg = _panel.AddComponent<Image>();
            _panelImg.color = ColBg;

            BuildHeader(panelRect);
            BuildContentArea(panelRect);
            // The vanilla art and the panel's final size are applied from Open(): the inventory GUI
            // may not exist yet at Hud.Awake, and the fit needs a live canvas.
            // _uiRoot was already deactivated above; it stays hidden (and raycast-free, so it
            // doesn't block the game's inventory/crafting clicks) until Open().
        }

        /// Title band inside the panel frame: heading on the left, a vanilla Close button on the
        /// right, and a thin rule underneath — the same arrangement the vanilla inventory and
        /// crafting windows use. No filled bar; the carved panel is the background.
        private void BuildHeader(RectTransform panelRect)
        {
            var hdrGo = new GameObject("VSG_CodexHeader");
            hdrGo.transform.SetParent(panelRect, false);
            _headerRect = hdrGo.AddComponent<RectTransform>();
            _headerRect.anchorMin = new Vector2(0f, 1f);
            _headerRect.anchorMax = new Vector2(1f, 1f);
            _headerRect.offsetMin = new Vector2(_frameInset, -(_frameInset + HeaderHeight));
            _headerRect.offsetMax = new Vector2(-_frameInset, -_frameInset);

            // Window heading.
            var titleGo = new GameObject("VSG_CodexHdrTitle");
            titleGo.transform.SetParent(hdrGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.7f, 1f);
            titleRect.offsetMin = new Vector2(4f, 0f);
            titleRect.offsetMax = Vector2.zero;
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(titleTmp);
            titleTmp.text      = "Guide Codex";
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.fontSize  = FsWindowTitle;
            titleTmp.color     = ColGold;
            titleTmp.alignment = TextAlignmentOptions.Left;
            titleTmp.raycastTarget = false;

            // Close button, styled as a vanilla button (sprite + hover/press swaps).
            var closeGo = new GameObject("VSG_CodexClose");
            closeGo.transform.SetParent(hdrGo.transform, false);
            var closeRect = closeGo.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot     = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(132f, 34f);
            closeRect.anchoredPosition = new Vector2(-2f, 0f);
            var closeImg = closeGo.AddComponent<Image>();
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);
            RegisterButton(closeBtn, closeImg);
            BuildButtonLabel(closeGo.transform, "Close", FsButton, ColText);

            // Rule closing off the title band — the same plain thin line vanilla's own windows
            // separate their sections with.
            var sepGo = new GameObject("VSG_CodexHeaderSep");
            sepGo.transform.SetParent(panelRect, false);
            _headerSepRect = sepGo.AddComponent<RectTransform>();
            _headerSepRect.anchorMin = new Vector2(0f, 1f);
            _headerSepRect.anchorMax = new Vector2(1f, 1f);
            _headerSepRect.offsetMin = new Vector2(_frameInset, -(_frameInset + HeaderHeight));
            _headerSepRect.offsetMax = new Vector2(-_frameInset, -(_frameInset + HeaderHeight) + 2f);
            var sepImg = sepGo.AddComponent<Image>();
            sepImg.color = ColDivider;
            sepImg.raycastTarget = false;
        }

        /// Centred label inside a button. Buttons carry a sprite on their own Image, so the text
        /// has to live on a child rather than being the button's graphic.
        ///
        /// The inset is the button's padding: it keeps the label clear of the sprite's carved edge
        /// on both axes, which is what makes the text readable at these sizes rather than crammed
        /// against the border.
        private TMP_Text BuildButtonLabel(Transform buttonGo, string text, float fontSize,
            Color color)
        {
            var go = new GameObject("VSG_BtnLabel");
            go.transform.SetParent(buttonGo, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(ButtonPadX, ButtonPadY);
            rect.offsetMax = new Vector2(-ButtonPadX, -ButtonPadY);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            ApplyFont(tmp);
            tmp.text               = text;
            tmp.fontSize           = fontSize;
            tmp.color              = color;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Overflow;
            tmp.raycastTarget      = false;
            return tmp;
        }

        /// Queue a button for vanilla styling. Styling is applied immediately when the inventory
        /// art is already resolved and re-applied for the whole panel from ApplyVanillaStyle
        /// otherwise, so buttons built before the GUI scene exists are not left flat.
        private void RegisterButton(Button button, Image image)
        {
            _buttons.Add((button, image));
            if (VanillaUi.Resolved) VanillaUi.StyleButton(button, image);
            else
            {
                image.color       = VanillaUi.ButtonFill;
                button.transition = Selectable.Transition.None;
                button.targetGraphic = image;
            }
        }

        private void BuildContentArea(RectTransform panelRect)
        {
            // Content area: inside the frame carving, below the title band.
            var contentGo = new GameObject("VSG_CodexContent");
            contentGo.transform.SetParent(panelRect, false);
            _contentRect = contentGo.AddComponent<RectTransform>();
            _contentRect.anchorMin = new Vector2(0f, 0f);
            _contentRect.anchorMax = new Vector2(1f, 1f);
            _contentRect.offsetMin = new Vector2(_frameInset, _frameInset);
            _contentRect.offsetMax = new Vector2(-_frameInset, -(_frameInset + HeaderHeight + 6f));

            BuildLeftPanel(contentGo.transform);

            // Vertical divider between left and right panels.
            var divGo = new GameObject("VSG_CodexVDiv");
            divGo.transform.SetParent(contentGo.transform, false);
            _vDivRect = divGo.AddComponent<RectTransform>();
            _vDivRect.anchorMin = new Vector2(0f, 0f);
            _vDivRect.anchorMax = new Vector2(0f, 1f);
            _vDivRect.offsetMin = new Vector2(_leftWidth + 3f, 4f);
            _vDivRect.offsetMax = new Vector2(_leftWidth + 5f, -4f);
            var vDivImg = divGo.AddComponent<Image>();
            vDivImg.color = ColDivider;
            vDivImg.raycastTarget = false;

            BuildRightPanel(contentGo.transform);
        }

        private void BuildLeftPanel(Transform parent)
        {
            var leftGo = new GameObject("VSG_CodexLeft");
            leftGo.transform.SetParent(parent, false);
            _leftRect = leftGo.AddComponent<RectTransform>();
            _leftRect.anchorMin = new Vector2(0f, 0f);
            _leftRect.anchorMax = new Vector2(0f, 1f);
            _leftRect.offsetMin = Vector2.zero;
            _leftRect.offsetMax = new Vector2(_leftWidth, 0f);
            // Darker interior box, the same inset the vanilla windows put a list in.
            _leftImg = leftGo.AddComponent<Image>();
            _leftImg.color = ColPanelBg;

            // "CATEGORIES" header label.
            var catHdrGo = new GameObject("VSG_CodexCatHdr");
            catHdrGo.transform.SetParent(leftGo.transform, false);
            var catHdrRect = catHdrGo.AddComponent<RectTransform>();
            catHdrRect.anchorMin        = new Vector2(0f, 1f);
            catHdrRect.anchorMax        = new Vector2(1f, 1f);
            catHdrRect.pivot            = new Vector2(0.5f, 1f);
            catHdrRect.sizeDelta        = new Vector2(0f, 28f);
            catHdrRect.anchoredPosition = Vector2.zero;
            var catHdrTmp = catHdrGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(catHdrTmp);
            catHdrTmp.text             = "Categories";
            catHdrTmp.fontStyle        = FontStyles.Bold;
            catHdrTmp.fontSize         = FsPaneHeader;
            catHdrTmp.color            = ColGold;
            catHdrTmp.alignment        = TextAlignmentOptions.Center;
            catHdrTmp.raycastTarget    = false;

            // Footer: the "show hidden" toggle. Built before the list so the list can anchor
            // above it.
            BuildLeftFooter(leftGo.transform);

            // Guide list, between the CATEGORIES header and the footer. The whole list lives in
            // one scroll view: the content grows to whatever the rows need (ContentSizeFitter)
            // and the viewport clips + scrolls it.
            //
            // A VerticalLayoutGroup does not spill past a FIXED-height container, it *compresses*
            // its children toward LayoutElement.minHeight, and rows that leave min unset (i.e. 0)
            // collapse to invisible slivers while category headers hold their size. The fitter
            // removes that failure mode (the content is never shorter than its rows), and rows
            // still pin min AND preferred so nothing can squeeze them.
            var listGo = new GameObject("VSG_LeftList");
            listGo.transform.SetParent(leftGo.transform, false);
            var listRect = listGo.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.offsetMin = new Vector2(0f, FooterHeight);
            listRect.offsetMax = new Vector2(0f, -28f); // below the 28px Categories header
            listGo.AddComponent<RectMask2D>();

            _listScroll = listGo.AddComponent<ScrollRect>();
            _listScroll.horizontal   = false;
            _listScroll.vertical     = true;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("VSG_LeftVp");
            vpGo.transform.SetParent(listGo.transform, false);
            var vpRect = vpGo.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;

            var contentGo = new GameObject("VSG_LeftContent");
            contentGo.transform.SetParent(vpGo.transform, false);
            // Capture the RectTransform RETURNED by AddComponent. Adding a RectTransform to a GO
            // that has a plain Transform replaces the transform, so a reference cached from
            // `.transform` BEFORE this call would dangle (and `as RectTransform` would be null,
            // leaving the VerticalLayoutGroup unable to lay out children).
            var cRect = contentGo.AddComponent<RectTransform>();
            _leftContent = cRect;
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.pivot     = new Vector2(0.5f, 1f);
            cRect.offsetMin = Vector2.zero;
            cRect.offsetMax = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment         = TextAnchor.UpperLeft;
            vlg.spacing                = RowSpacing;
            vlg.padding                = new RectOffset(4, 4, 4, 4);

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _listScroll.viewport = vpRect;
            _listScroll.content  = cRect;
            // Fixed pixels per notch instead of ScrollRect's delta-scaled sensitivity, which
            // Valheim's input module reduces to a crawl. ~10 short rows per notch.
            WheelScroller.Attach(_listScroll, 200f);

            BuildLeftScrollbar(listGo.transform);
        }

        /// Slim vertical scrollbar down the right edge of the guide list. Auto-hidden while the
        /// list fits, so it doubles as the "there is more below" cue that the page counter used
        /// to give. Plain coloured Images only — no custom sprites (CRIT-14).
        private void BuildLeftScrollbar(Transform listGo)
        {
            var sbGo = new GameObject("VSG_LeftScrollbar");
            sbGo.transform.SetParent(listGo, false);
            var sbRect = sbGo.AddComponent<RectTransform>();
            sbRect.anchorMin        = new Vector2(1f, 0f);
            sbRect.anchorMax        = new Vector2(1f, 1f);
            sbRect.pivot            = new Vector2(1f, 0.5f);
            sbRect.sizeDelta        = new Vector2(6f, -4f);
            sbRect.anchoredPosition = new Vector2(-1f, 0f);
            sbGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var sb = sbGo.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.transition = Selectable.Transition.None;

            // Unity's Scrollbar drives handleRect's anchors, so the handle must sit inside a
            // full-size "sliding area" child rather than directly on the scrollbar.
            var slideGo = new GameObject("VSG_LeftScrollbarSlide");
            slideGo.transform.SetParent(sbGo.transform, false);
            var slideRect = slideGo.AddComponent<RectTransform>();
            slideRect.anchorMin = Vector2.zero;
            slideRect.anchorMax = Vector2.one;
            slideRect.offsetMin = Vector2.zero;
            slideRect.offsetMax = Vector2.zero;

            var handleGo = new GameObject("VSG_LeftScrollbarHandle");
            handleGo.transform.SetParent(slideGo.transform, false);
            var handleRect = handleGo.AddComponent<RectTransform>();
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = ColDivider;

            sb.handleRect    = handleRect;
            sb.targetGraphic = handleImg;

            _listScroll.verticalScrollbar           = sb;
            _listScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        /// The hidden-quest toggle, pinned to the bottom of the left pane.
        private void BuildLeftFooter(Transform leftGo)
        {
            var footerGo = new GameObject("VSG_LeftFooter");
            footerGo.transform.SetParent(leftGo, false);
            var footerRect = footerGo.AddComponent<RectTransform>();
            footerRect.anchorMin        = new Vector2(0f, 0f);
            footerRect.anchorMax        = new Vector2(1f, 0f);
            footerRect.pivot            = new Vector2(0.5f, 0f);
            footerRect.sizeDelta        = new Vector2(0f, FooterHeight);
            footerRect.anchoredPosition = Vector2.zero;

            // Divider along the top edge of the footer.
            var divGo = new GameObject("VSG_LeftFooterDiv");
            divGo.transform.SetParent(footerGo.transform, false);
            var divRect = divGo.AddComponent<RectTransform>();
            divRect.anchorMin        = new Vector2(0f, 1f);
            divRect.anchorMax        = new Vector2(1f, 1f);
            divRect.pivot            = new Vector2(0.5f, 1f);
            divRect.sizeDelta        = new Vector2(-8f, 1f);
            divRect.anchoredPosition = Vector2.zero;
            divGo.AddComponent<Image>().color = ColDivider;

            _showHiddenText = BuildFooterButton(footerGo.transform, "VSG_ShowHidden", "",
                new Vector2(4f, 4f), new Vector2(-4f, -6f), ToggleShowHidden);
        }

        /// Vanilla-styled button stretched across the footer. Returns the label so callers can
        /// restyle it later (relabelling / recolouring the toggle).
        private TMP_Text BuildFooterButton(Transform parent, string name, string label,
            Vector2 offsetMin, Vector2 offsetMax, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);
            RegisterButton(btn, img);
            return BuildButtonLabel(go.transform, label, FsFooter, ColText);
        }

        private void BuildRightPanel(Transform parent)
        {
            var rightGo = new GameObject("VSG_CodexRight");
            rightGo.transform.SetParent(parent, false);
            var rightRect = rightGo.AddComponent<RectTransform>();
            _rightRect = rightRect;
            rightRect.anchorMin = new Vector2(0f, 0f);
            rightRect.anchorMax = new Vector2(1f, 1f);
            rightRect.offsetMin = new Vector2(_leftWidth + PaneGap, 0f);
            rightRect.offsetMax = Vector2.zero;

            // ── Title + badge (top 50px) ──────────────────────────────────────────────────────
            var titleAreaGo = new GameObject("VSG_CodexTitleArea");
            titleAreaGo.transform.SetParent(rightGo.transform, false);
            var titleAreaRect = titleAreaGo.AddComponent<RectTransform>();
            titleAreaRect.anchorMin        = new Vector2(0f, 1f);
            titleAreaRect.anchorMax        = new Vector2(1f, 1f);
            titleAreaRect.pivot            = new Vector2(0.5f, 1f);
            titleAreaRect.sizeDelta        = new Vector2(0f, TitleAreaBaseHeight);
            titleAreaRect.anchoredPosition = Vector2.zero;
            _titleAreaRect = titleAreaRect;

            var titleGo = new GameObject("VSG_CodexEntryTitle");
            titleGo.transform.SetParent(titleAreaGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.70f, 1f);
            titleRect.offsetMin = new Vector2(8f, 0f);
            titleRect.offsetMax = new Vector2(0f, -10f); // breathing room under the header bar
            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_titleText);
            _titleText.text               = "Select a guide";
            _titleText.fontStyle          = FontStyles.Bold;
            _titleText.fontSize           = FsEntryTitle;
            _titleText.color              = ColGold;
            _titleText.alignment          = TextAlignmentOptions.TopLeft;
            // Wraps rather than ellipsising, and LayoutTitleArea grows the band (pushing the
            // divider, the toggle pills and the body scroll down) so even a three-line title is
            // fully readable instead of spilling over the rule below it.
            _titleText.enableWordWrapping = true;
            _titleText.overflowMode       = TextOverflowModes.Overflow;
            _titleText.raycastTarget      = false;

            var badgeGo = new GameObject("VSG_CodexBadge");
            badgeGo.transform.SetParent(titleAreaGo.transform, false);
            var badgeRect = badgeGo.AddComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0.70f, 0f);
            badgeRect.anchorMax = new Vector2(1f, 1f);
            badgeRect.offsetMin = Vector2.zero;
            badgeRect.offsetMax = new Vector2(-8f, 0f);
            _badgeText = badgeGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_badgeText);
            _badgeText.text               = "";
            _badgeText.fontStyle          = FontStyles.Bold;
            _badgeText.fontSize           = FsBadge;
            _badgeText.color              = ColGreen;
            _badgeText.alignment          = TextAlignmentOptions.Right;
            _badgeText.enableWordWrapping = false;
            _badgeText.raycastTarget      = false;

            // Horizontal divider below title area.
            var hDivGo = new GameObject("VSG_CodexHDiv");
            hDivGo.transform.SetParent(rightGo.transform, false);
            var hDivRect = hDivGo.AddComponent<RectTransform>();
            hDivRect.anchorMin        = new Vector2(0f, 1f);
            hDivRect.anchorMax        = new Vector2(1f, 1f);
            hDivRect.pivot            = new Vector2(0.5f, 1f);
            hDivRect.sizeDelta        = new Vector2(-16f, 1f);
            hDivRect.anchoredPosition = new Vector2(0f, -TitleAreaBaseHeight);
            var hDivImg = hDivGo.AddComponent<Image>();
            hDivImg.color = ColDivider;
            hDivImg.raycastTarget = false;
            _titleDivRect = hDivRect;

            // ── Tracker pin toggle (just below the title divider) ─────────────────────────────
            BuildTrackToggle(rightGo.transform);

            // ── Upcoming steps section (bottom 160px) ─────────────────────────────────────────
            BuildUpcomingSection(rightGo.transform);

            // ── Body text (scrollable, between toggle bar and upcoming section) ───────────────
            BuildBodyScroll(rightGo.transform);
        }

        /// A clickable "Pin to Tracker" pill shown only when the selected quest is in
        /// progress (finished quests are not pinnable). Toggles TrackedQuestState and tells the
        /// HUD tracker to repaint / unhide.
        private void BuildTrackToggle(Transform rightGo)
        {
            _trackToggleGo = new GameObject("VSG_CodexTrackToggle");
            _trackToggleGo.transform.SetParent(rightGo, false);
            var rect = _trackToggleGo.AddComponent<RectTransform>();
            // Left ~62% of the bar; the hide pill takes the right side.
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0.62f, 1f);
            rect.pivot     = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(8f, -(TitleAreaBaseHeight + 4f + ToggleBarHeight));
            rect.offsetMax = new Vector2(-3f, -(TitleAreaBaseHeight + 4f));

            var img = _trackToggleGo.AddComponent<Image>();
            var btn = _trackToggleGo.AddComponent<Button>();
            btn.onClick.AddListener(ToggleTrackSelected);
            RegisterButton(btn, img);

            _trackToggleText = BuildButtonLabel(_trackToggleGo.transform, "", FsButton, ColText);
            _trackToggleText.fontStyle = FontStyles.Bold;

            BuildHideToggle(rightGo);
        }

        /// "Hide from list" pill, sharing the toggle bar with the tracker pin. Always available
        /// (unlike the pin, which is in-progress only) — a finished guide is the most likely
        /// thing a player wants out of the list.
        private void BuildHideToggle(Transform rightGo)
        {
            _hideToggleGo = new GameObject("VSG_CodexHideToggle");
            _hideToggleGo.transform.SetParent(rightGo, false);
            var rect = _hideToggleGo.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.62f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot     = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(3f, -(TitleAreaBaseHeight + 4f + ToggleBarHeight));
            rect.offsetMax = new Vector2(-8f, -(TitleAreaBaseHeight + 4f));

            var img = _hideToggleGo.AddComponent<Image>();
            var btn = _hideToggleGo.AddComponent<Button>();
            btn.onClick.AddListener(ToggleHideSelected);
            RegisterButton(btn, img);

            _hideToggleText = BuildButtonLabel(_hideToggleGo.transform, "", FsButton, ColText);
            _hideToggleText.fontStyle = FontStyles.Bold;
        }

        private void BuildBodyScroll(Transform rightGo)
        {
            // Occupies the vertical space between the title+divider (54px from top)
            // and the upcoming section (160px from bottom).
            var scrollGo = new GameObject("VSG_CodexBodyScroll");
            scrollGo.transform.SetParent(rightGo, false);
            var scrollRect = scrollGo.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(8f, UpcomingHeight + 8f);
            // Leave room for the title band plus the toggle-pill bar below it. LayoutTitleArea
            // pushes this further down when a long title needs a taller band.
            scrollRect.offsetMax = new Vector2(-8f, -BodyTopOffset(TitleAreaBaseHeight));
            // Sits in the same darker interior box the vanilla windows read long text in.
            _bodyBgImg = scrollGo.AddComponent<Image>();
            _bodyBgImg.color = ColPanelBg;
            scrollGo.AddComponent<RectMask2D>();
            _bodyScrollRect = scrollRect;

            var sv = scrollGo.AddComponent<ScrollRect>();
            sv.horizontal   = false;
            sv.vertical     = true;
            sv.movementType = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("VSG_BodyVp");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = vpGo.AddComponent<RectTransform>();
            // Inset from the interior box so the text is not flush against its carved edge.
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = new Vector2(10f, 8f);
            vpRect.offsetMax = new Vector2(-10f, -8f);

            var contentGo = new GameObject("VSG_BodyContent");
            contentGo.transform.SetParent(vpGo.transform, false);
            _bodyContentRect = contentGo.AddComponent<RectTransform>();
            _bodyContentRect.anchorMin = new Vector2(0f, 1f);
            _bodyContentRect.anchorMax = new Vector2(1f, 1f);
            _bodyContentRect.pivot     = new Vector2(0.5f, 1f);
            _bodyContentRect.offsetMin = Vector2.zero;
            _bodyContentRect.offsetMax = Vector2.zero;

            _bodyText = contentGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_bodyText);
            _bodyText.text               = "";
            _bodyText.fontSize           = FsBody;
            _bodyText.color              = ColText;
            _bodyText.alignment          = TextAlignmentOptions.TopLeft;
            _bodyText.enableWordWrapping = true;
            _bodyText.raycastTarget      = false;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sv.viewport = vpRect;
            sv.content  = _bodyContentRect;
            WheelScroller.Attach(sv);
        }

        private void BuildUpcomingSection(Transform rightGo)
        {
            var upGo = new GameObject("VSG_CodexUpcoming");
            upGo.transform.SetParent(rightGo, false);
            _upcomingGo = upGo;
            var upRect = upGo.AddComponent<RectTransform>();
            upRect.anchorMin        = new Vector2(0f, 0f);
            upRect.anchorMax        = new Vector2(1f, 0f);
            upRect.pivot            = new Vector2(0.5f, 0f);
            upRect.sizeDelta        = new Vector2(-16f, UpcomingHeight - 8f);
            upRect.anchoredPosition = new Vector2(0f, 4f);

            // Header label.
            var hdrGo = new GameObject("VSG_UpHdr");
            hdrGo.transform.SetParent(upGo.transform, false);
            var hdrRect = hdrGo.AddComponent<RectTransform>();
            hdrRect.anchorMin        = new Vector2(0f, 1f);
            hdrRect.anchorMax        = new Vector2(1f, 1f);
            hdrRect.pivot            = new Vector2(0.5f, 1f);
            hdrRect.sizeDelta        = new Vector2(0f, 22f);
            hdrRect.anchoredPosition = Vector2.zero;
            var hdrTmp = hdrGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(hdrTmp);
            hdrTmp.text             = "Upcoming Steps";
            hdrTmp.fontStyle        = FontStyles.Bold;
            hdrTmp.fontSize         = FsPaneHeader;
            hdrTmp.color            = ColGold;
            hdrTmp.alignment        = TextAlignmentOptions.Left;
            hdrTmp.raycastTarget    = false;

            // Divider above header.
            var divGo = new GameObject("VSG_UpDiv");
            divGo.transform.SetParent(upGo.transform, false);
            var divRect = divGo.AddComponent<RectTransform>();
            divRect.anchorMin        = new Vector2(0f, 1f);
            divRect.anchorMax        = new Vector2(1f, 1f);
            divRect.pivot            = new Vector2(0.5f, 1f);
            divRect.sizeDelta        = new Vector2(0f, 1f);
            divRect.anchoredPosition = new Vector2(0f, -22f);
            var upDivImg = divGo.AddComponent<Image>();
            upDivImg.color = ColDivider;
            upDivImg.raycastTarget = false;

            // Scroll view for the upcoming step rows — a chain with more than ~7 remaining steps
            // does not fit the section, and the rows now hold their height instead of
            // compressing, so the overflow has to be scrollable and clipped.
            var scrollGo = new GameObject("VSG_UpScroll");
            scrollGo.transform.SetParent(upGo.transform, false);
            var scrollRect = scrollGo.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0f, -24f);
            _upcomingBgImg = scrollGo.AddComponent<Image>();
            _upcomingBgImg.color = ColPanelBg;
            scrollGo.AddComponent<RectMask2D>();

            _upcomingScroll = scrollGo.AddComponent<ScrollRect>();
            _upcomingScroll.horizontal   = false;
            _upcomingScroll.vertical     = true;
            _upcomingScroll.movementType = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("VSG_UpVp");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = vpGo.AddComponent<RectTransform>();
            // Inset from the interior box, matching the body scroll.
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = new Vector2(8f, 6f);
            vpRect.offsetMax = new Vector2(-8f, -6f);

            var contentGo = new GameObject("VSG_UpContent");
            contentGo.transform.SetParent(vpGo.transform, false);
            // Capture the RETURNED RectTransform (see note in BuildLeftPanel) — caching
            // `.transform` before this call would leave the VLG without a usable RectTransform.
            var cRect = contentGo.AddComponent<RectTransform>();
            _upcomingContent = cRect;
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.pivot     = new Vector2(0.5f, 1f);
            cRect.offsetMin = Vector2.zero;
            cRect.offsetMax = Vector2.zero;

            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing                = 2f;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _upcomingScroll.viewport = vpRect;
            _upcomingScroll.content  = cRect;
            // Smaller step than the guide list: this section is only ~130px tall, so a full-panel
            // step would jump it from top to bottom in one notch.
            WheelScroller.Attach(_upcomingScroll, 90f);
        }

        // ── Vanilla styling ───────────────────────────────────────────────────────────────────

        /// Paint the panel in the vanilla player-inventory window's own art — its carved frame, the
        /// interior boxes its lists sit in, its buttons — and fit it to the screen. Called from
        /// Open() with the canvas already live, because the fit is expressed in canvas units and the
        /// `CanvasScaler` only publishes its scale factor once enabled.
        ///
        /// The art is applied once; the fit re-runs whenever the screen size has changed since the
        /// last one. Until `InventoryGui` exists the flat-colour fallbacks stay in place.
        private void ApplyVanillaStyle()
        {
            var resolved = VanillaUi.TryResolve();

            // Size before art: the frame sprite is fixed-aspect, so the panel takes its aspect.
            // Re-fitted whenever the screen size OR the game's GUI scale has moved since the last
            // fit — both change how much canvas the panel has to work with.
            var screen = new Vector2(Screen.width, Screen.height);
            var scale  = UiScaleFactor();
            if (screen != _fittedScreen || !Mathf.Approximately(scale, _fittedScale))
            {
                _fittedScreen = screen;
                _fittedScale  = scale;
                ApplyGeometry();
            }

            if (!resolved || _styled) return;
            _styled = true;

            VanillaUi.Panel.Apply(_panelImg, Color.white);
            VanillaUi.ApplyInset(_leftImg);
            VanillaUi.ApplyInset(_bodyBgImg);
            VanillaUi.ApplyInset(_upcomingBgImg);

            foreach (var (button, image) in _buttons)
                VanillaUi.StyleButton(button, image);
        }

        /// Re-derive every size-dependent rect from `_panelWidth` / `_panelHeight` / `_leftWidth`.
        /// Everything else in the panel is anchored, so this is the whole of the resize.
        private void ApplyGeometry()
        {
            var size = ResolvePanelSize();
            _panelWidth  = size.x;
            _panelHeight = size.y;
            // The frame art scales with the panel, so its carving does too — a fixed inset would
            // leave content sitting on the border of a large panel.
            _frameInset = Mathf.Clamp(Mathf.Round(_panelWidth * 0.025f), 18f, 34f);
            // A wider panel gets a wider guide list, but the list is never allowed to crowd out
            // the detail pane it feeds.
            _leftWidth = Mathf.Clamp(Mathf.Round(_panelWidth * 0.28f), 210f, 320f);

            if (_panelRect != null) _panelRect.sizeDelta = size;
            if (_headerRect != null)
            {
                _headerRect.offsetMin = new Vector2(_frameInset, -(_frameInset + HeaderHeight));
                _headerRect.offsetMax = new Vector2(-_frameInset, -_frameInset);
            }
            if (_headerSepRect != null)
            {
                _headerSepRect.offsetMin = new Vector2(_frameInset, -(_frameInset + HeaderHeight));
                _headerSepRect.offsetMax =
                    new Vector2(-_frameInset, -(_frameInset + HeaderHeight) + 2f);
            }
            if (_contentRect != null)
            {
                _contentRect.offsetMin = new Vector2(_frameInset, _frameInset);
                _contentRect.offsetMax =
                    new Vector2(-_frameInset, -(_frameInset + HeaderHeight + 6f));
            }
            if (_leftRect  != null) _leftRect.offsetMax  = new Vector2(_leftWidth, 0f);
            if (_vDivRect  != null)
            {
                _vDivRect.offsetMin = new Vector2(_leftWidth + 3f, 4f);
                _vDivRect.offsetMax = new Vector2(_leftWidth + 5f, -4f);
            }
            if (_rightRect != null)
                _rightRect.offsetMin = new Vector2(_leftWidth + PaneGap, 0f);
        }

        /// The panel's size. Valheim's window art is one sprite per window rather than a 9-sliced
        /// tile, so `woodpanel_playerinventory` can only be drawn undistorted at its own aspect
        /// ratio — the panel is fitted to that, scaled to ~80% of the screen height and clamped so
        /// a very wide frame does not run off the sides. A sprite that *does* carry 9-slice borders
        /// (or no sprite at all, on the flat fallback) keeps the default proportions.
        private Vector2 ResolvePanelSize()
        {
            var aspect = DefaultPanelWidth / DefaultPanelHeight;
            var sprite = VanillaUi.Panel.Sprite;
            if (sprite != null && !VanillaUi.Panel.Scalable
                && sprite.rect.width > 1f && sprite.rect.height > 1f)
                aspect = sprite.rect.width / sprite.rect.height;

            var scale  = UiScaleFactor();
            var availW = Screen.width  / scale;
            var availH = Screen.height / scale;

            var height = Mathf.Clamp(availH * 0.80f, 420f, 900f);
            var width  = height * aspect;
            var maxW   = availW * 0.92f;
            if (width > maxW)
            {
                width  = maxW;
                height = Mathf.Max(380f, width / aspect);
            }
            return new Vector2(Mathf.Round(width), Mathf.Round(height));
        }

        /// Screen pixels per canvas unit for this panel.
        ///
        /// Read off the **vanilla HUD's** canvas rather than our own: a `CanvasScaler` applies
        /// itself from `Canvas.preWillRenderCanvases`, i.e. at render time, so on the frame our root
        /// is activated our canvas still reports the factor it was created with (1) — which fitted
        /// the panel to raw pixels and made it ~20% oversized on any display that is not exactly the
        /// reference resolution. The HUD's canvas has been rendering since the scene loaded and our
        /// scaler is a copy of its settings, so its factor is the one ours settles on.
        private float UiScaleFactor()
        {
            var hudCanvas = Hud.instance != null
                ? Hud.instance.GetComponentInParent<Canvas>() : null;
            if (hudCanvas != null && hudCanvas.scaleFactor > 0.01f) return hudCanvas.scaleFactor;
            var own = _uiRoot != null ? _uiRoot.GetComponent<Canvas>() : null;
            return own != null && own.scaleFactor > 0.01f ? own.scaleFactor : 1f;
        }

        /// Distance from the top of the right pane to the top of the body scroll, for a title band
        /// of `titleHeight`: the band itself, then the toggle-pill bar with a gap either side.
        private static float BodyTopOffset(float titleHeight)
            => titleHeight + 8f + ToggleBarHeight;

        // ── Data population ───────────────────────────────────────────────────────────────────

        /// Rebuild the list from the current config + player state and render it.
        /// `keepSelection` preserves the highlighted quest when it is still listed (used after
        /// hiding/unhiding or a config push) along with the scroll position; a fresh Open()
        /// drops both and starts at the top.
        private void PopulatePanel(bool keepSelection = false)
        {
            EnsureFont();

            var player = Player.m_localPlayer;
            var config = Plugin.CurrentConfig;

            var previous = keepSelection ? _selected : null;
            // Read before the rebuild: clearing the content resets the ScrollRect to the top.
            var scrollAt = keepSelection && _listScroll != null
                ? _listScroll.verticalNormalizedPosition : 1f;

            ClearChildren(_leftContent);
            _guideRows.Clear();
            _selected = null;
            _items.Clear();

            if (config?.Guidances == null || player == null)
            {
                UpdateFooter(player);
                ShowEntry(null, null);
                return;
            }

            // Group listable guides by category, preserving YAML order.
            var catOrder = new List<string>();
            var byCategory = new Dictionary<string, List<GuidanceEntry>>();
            foreach (var entry in config.Guidances)
            {
                if (!IsVisible(entry, player)) continue;
                // Hidden quests are dropped from the list entirely unless the player has flipped
                // the footer toggle, which brings them back (dimmed) so they can be unhidden.
                if (!_showHidden && HiddenQuestState.IsHidden(player, entry.Id)) continue;
                var cat = string.IsNullOrEmpty(entry.Category) ? "General" : entry.Category;
                if (!byCategory.ContainsKey(cat))
                {
                    byCategory[cat] = new List<GuidanceEntry>();
                    catOrder.Add(cat);
                }
                byCategory[cat].Add(entry);
            }

            BuildItems(catOrder, byCategory, player);

            RenderList(player);
            UpdateFooter(player);
            if (_listScroll != null) _listScroll.verticalNormalizedPosition = scrollAt;

            // Restore the previous selection when it survived the repopulate, otherwise fall back
            // to the first quest in the list.
            GuidanceEntry toSelect = null;
            if (previous != null)
                foreach (var (entry, _, _) in _guideRows)
                    if (entry == previous) { toSelect = entry; break; }
            if (toSelect == null && _guideRows.Count > 0) toSelect = _guideRows[0].entry;

            if (toSelect != null)
            {
                _selected = toSelect;
                HighlightRow(_selected);
                ShowEntry(_selected, player);
            }
            else
            {
                ShowEntry(null, null);
            }
        }

        /// Height a quest row needs for its title once wrapped in the left pane. Titles are never
        /// ellipsised, so a two-line title simply gets a two-line row (CRIT-25) and the row's
        /// LayoutElement is pinned to that height.
        private float MeasureQuestRow(string label)
            => MeasureRow(label, QuestLabelWidth, QuestRowHeight, FsQuestRow);

        /// Wrapped height of `label` at `width` and `fontSize`, never below `minHeight`. Falls back
        /// to `minHeight` whenever the measurement is unavailable (no font resolved yet), which is
        /// exactly the old fixed-height behaviour — a row is never sized to nothing.
        private float MeasureRow(string label, float width, float minHeight, float fontSize)
        {
            EnsureMeasureText();
            if (_measureText == null || string.IsNullOrEmpty(label)) return minHeight;
            // TMP metrics need an awoken component; it only awoke if the codex root was active
            // when the measurer was created (it always is — PopulatePanel runs while open).
            if (!_measureText.gameObject.activeInHierarchy) return minHeight;
            float h;
            // The measurer is shared between the list rows and the (smaller) upcoming rows, so the
            // size is set per call rather than baked in — measuring at the wrong size would leave
            // every upcoming row a few pixels taller than it needs to be.
            _measureText.fontSize = fontSize;
            try { h = _measureText.GetPreferredValues(label, width, 0f).y; }
            catch (System.Exception) { return minHeight; }
            return Mathf.Max(minHeight, Mathf.Ceil(h) + 4f);
        }

        /// Hidden TMP instance used only for text metrics. Built inactive so its Awake fires
        /// with the font already assigned (no LiberationSans warning), activated so TMP is
        /// initialised, then disabled as a component so it is never drawn — GetPreferredValues
        /// overwrites its text on every call.
        private void EnsureMeasureText()
        {
            if (_measureText != null) return;
            if (_font == null || _uiRoot == null) return;

            var go = new GameObject("VSG_CodexMeasure");
            go.transform.SetParent(_uiRoot.transform, false);
            go.SetActive(false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font               = _font;
            tmp.fontSize           = FsQuestRow;
            tmp.enableWordWrapping = true;
            tmp.overflowMode       = TextOverflowModes.Overflow;
            tmp.raycastTarget      = false;
            go.SetActive(true);
            tmp.enabled = false;
            _measureText = tmp;
        }

        /// Flatten the grouped entries into one scrollable row list: a header per category
        /// followed by its quests, each measured so its row can be sized to the wrapped title.
        private void BuildItems(List<string> catOrder,
            Dictionary<string, List<GuidanceEntry>> byCategory, Player player)
        {
            foreach (var cat in catOrder)
            {
                _items.Add(new ListItem { Category = true, CategoryName = cat });
                foreach (var entry in byCategory[cat])
                {
                    var hidden = HiddenQuestState.IsHidden(player, entry.Id);
                    var label  = QuestRowLabel(entry, player, hidden);
                    _items.Add(new ListItem
                    {
                        Category = false,
                        Entry    = entry,
                        Hidden   = hidden,
                        Label    = label,
                        Height   = MeasureQuestRow(label),
                    });
                }
            }
        }

        /// Instantiate every row into the scroll content.
        private void RenderList(Player player)
        {
            if (_items.Count == 0) return;

            // Build the rows with the container inactive so each TextMeshProUGUI's Awake (which
            // logs "LiberationSans SDF Font Asset was not found" when m_fontAsset is null) is
            // deferred until after ApplyFont has assigned the vanilla font. Reactivated below.
            _leftContent.gameObject.SetActive(false);

            foreach (var item in _items)
            {
                if (item.Category) BuildCategoryRow(item.CategoryName);
                else               BuildQuestRow(item, player);
            }

            // Reactivate now that every row has its font — Awake fires here without warning.
            _leftContent.gameObject.SetActive(true);

            // Force the VLG/layout to compute row rects now so geometry is valid this frame.
            if (_leftContent is RectTransform lcRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(lcRect);

            // A TMP built inside an inactive container caches an empty mesh; the layout rebuild
            // above fixes the rects but does NOT regenerate that geometry. Push a mesh update
            // on every label so each row draws with the width it was just given.
            ForceMeshUpdateIn(_leftContent);
        }

        /// Category header row. TMP lives on a child GO (not directly on catGo) so the VLG only
        /// sees LayoutElement when computing the row height — avoids TMP.preferredHeight (which
        /// can be 0 during ForceRebuildLayoutImmediate if the font geometry hasn't been computed
        /// yet) collapsing the row to zero height.
        private void BuildCategoryRow(string cat)
        {
            var catGo = new GameObject("VSG_Cat_" + cat);
            catGo.transform.SetParent(_leftContent, false);
            var catImg = catGo.AddComponent<Image>();
            catImg.color = new Color(0f, 0f, 0f, 0f);
            var catLe = catGo.AddComponent<LayoutElement>();
            catLe.minHeight       = CatRowHeight;
            catLe.preferredHeight = CatRowHeight;

            var catLabelGo = new GameObject("VSG_CatLabel");
            catLabelGo.transform.SetParent(catGo.transform, false);
            var catLabelRect = catLabelGo.AddComponent<RectTransform>();
            catLabelRect.anchorMin = Vector2.zero;
            catLabelRect.anchorMax = Vector2.one;
            catLabelRect.offsetMin = new Vector2(4f, 0f);
            catLabelRect.offsetMax = Vector2.zero;
            var catTmp = catLabelGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(catTmp);
            catTmp.text               = cat.ToUpper();
            catTmp.fontStyle          = FontStyles.Bold;
            catTmp.fontSize           = FsCategory;
            catTmp.color              = ColGold;
            catTmp.alignment          = TextAlignmentOptions.Left;
            catTmp.enableWordWrapping = false;
            catTmp.overflowMode       = TextOverflowModes.Overflow;
            catTmp.raycastTarget      = false;
        }

        /// The exact string a quest row renders: status prefix + templated title. Built here so
        /// pagination measures the same text the row will later show.
        private static string QuestRowLabel(GuidanceEntry entry, Player player, bool hidden)
        {
            var title  = string.IsNullOrEmpty(entry.Title) ? entry.Id : Template(entry.Title, entry);
            var prefix = hidden ? "[-] " : IsEntryComplete(entry, player) ? "[+] " : "  ";
            return prefix + title;
        }

        private void BuildQuestRow(ListItem item, Player player)
        {
            var entry    = item.Entry;
            var complete = IsEntryComplete(entry, player);
            var label    = string.IsNullOrEmpty(item.Label)
                ? QuestRowLabel(entry, player, item.Hidden) : item.Label;

            var rowGo = new GameObject("VSG_Row_" + entry.Id);
            rowGo.transform.SetParent(_leftContent, false);
            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = new Color(0f, 0f, 0f, 0f);
            var rowLe = rowGo.AddComponent<LayoutElement>();
            // minHeight matters as much as preferredHeight: a VerticalLayoutGroup that runs out
            // of room shrinks children down to their MIN, and an unset min (0) let quest rows
            // collapse to invisible slivers while the category headers (min 22) held their size.
            // Height comes from the wrapped measurement so a two-line title gets a two-line row.
            rowLe.minHeight       = Mathf.Max(QuestRowHeight, item.Height);
            rowLe.preferredHeight = rowLe.minHeight;

            var rowBtn = rowGo.AddComponent<Button>();
            rowBtn.transition    = Selectable.Transition.None;
            rowBtn.targetGraphic = rowImg;
            var captured = entry;
            rowBtn.onClick.AddListener(() => SelectGuide(captured));

            var labelGo = new GameObject("VSG_RowLabel");
            labelGo.transform.SetParent(rowGo.transform, false);
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f); // clear of the scrollbar gutter
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(labelTmp);
            labelTmp.text               = label;
            labelTmp.fontStyle          = item.Hidden ? FontStyles.Italic : FontStyles.Normal;
            labelTmp.fontSize           = FsQuestRow;
            labelTmp.color              = item.Hidden ? ColLocked : complete ? ColGreen : ColText;
            labelTmp.alignment          = TextAlignmentOptions.TopLeft;
            // Wrapped, never ellipsised — the row was sized for the wrapped title (CRIT-25).
            labelTmp.enableWordWrapping = true;
            labelTmp.overflowMode       = TextOverflowModes.Overflow;
            labelTmp.raycastTarget      = false;

            _guideRows.Add((entry, rowGo, labelTmp));
        }

        // ── Hidden-quest control ──────────────────────────────────────────────────────────────

        /// Refresh the footer's hidden-quest toggle label.
        private void UpdateFooter(Player player)
        {
            if (_showHiddenText != null)
            {
                var hiddenCount = HiddenQuestState.Count(player);
                _showHiddenText.text = _showHidden
                    ? "[x] Showing hidden (" + hiddenCount + ")"
                    : "[ ] Show hidden (" + hiddenCount + ")";
                _showHiddenText.color = _showHidden ? ColGreen
                    : hiddenCount > 0 ? ColText : ColLocked;
            }
        }

        private void ToggleShowHidden()
        {
            _showHidden = !_showHidden;
            PopulatePanel(keepSelection: true);
        }

        /// Click handler for the detail-pane "Hide from list" pill.
        private void ToggleHideSelected()
        {
            var player = Player.m_localPlayer;
            if (_selected == null || player == null) return;

            var nowHidden = !HiddenQuestState.IsHidden(player, _selected.Id);
            HiddenQuestState.SetHidden(player, _selected.Id, nowHidden);

            // Hiding the selected quest while hidden quests are not being shown drops it from the
            // list, so the selection cannot survive — PopulatePanel falls back to the first row.
            PopulatePanel(keepSelection: true);
        }

        private void SelectGuide(GuidanceEntry entry)
        {
            _selected = entry;
            HighlightRow(entry);
            ShowEntry(entry, Player.m_localPlayer);
        }

        private void HighlightRow(GuidanceEntry selected)
        {
            foreach (var (entry, row, _) in _guideRows)
                PaintRow(row.GetComponent<Image>(), entry == selected);
        }

        /// The selected row is filled in the panel's own warm highlight — the same treatment
        /// vanilla's recipe and server lists give a selected row, rather than the item-slot frame,
        /// which is art for a square slot. Unselected rows are invisible but still raycast: the row
        /// itself is the click target.
        private static void PaintRow(Image img, bool selected)
        {
            if (img == null) return;
            img.sprite = null;
            img.type   = Image.Type.Simple;
            img.color  = selected ? ColRowSel : new Color(0f, 0f, 0f, 0f);
        }

        private void ShowEntry(GuidanceEntry entry, Player player)
        {
            if (entry == null || player == null)
            {
                if (_titleText != null) _titleText.text = "Select a guide";
                if (_badgeText != null) _badgeText.text = "";
                if (_bodyText  != null) _bodyText.text  = "";
                LayoutTitleArea();
                UpdateTrackToggle(null, null, false);
                ClearUpcoming();
                return;
            }

            var title    = string.IsNullOrEmpty(entry.Title) ? entry.Id : Template(entry.Title, entry);
            var complete = IsEntryComplete(entry, player);

            UpdateTrackToggle(entry, player, complete);

            _titleText.text = title;
            LayoutTitleArea();

            // Badge: completed chains show "[Complete] N / N"; in-progress shows "N / N".
            if (entry.Steps != null && entry.Steps.Count > 0)
            {
                if (complete)
                {
                    _badgeText.text  = "[Complete]  " + entry.Steps.Count + " / " + entry.Steps.Count;
                    _badgeText.color = ColGreen;
                }
                else
                {
                    var stepIdx = ChainState.GetStep(player, entry.Id);
                    _badgeText.text  = stepIdx + " / " + entry.Steps.Count;
                    _badgeText.color = ColText;
                }
            }
            else
            {
                // Non-chain entry. Show progress counter for item-submit or item_acquired goals;
                // otherwise show Complete or nothing.
                var submitGoal = ItemSubmitGoal(entry);
                var itemGoals  = entry.Trigger != null
                    ? ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger) : null;
                if (submitGoal > 0 && !complete)
                {
                    var cur = SubmitState.Get(player, entry.Id);
                    _badgeText.text  = cur + " / " + submitGoal;
                    _badgeText.color = ColText;
                }
                else if (itemGoals != null && !complete)
                {
                    var done = 0;
                    foreach (var g in itemGoals)
                        if (ItemAcquiredTrigger.CountInInventory(player, g.Item) >= g.Count) done++;
                    _badgeText.text  = done + " / " + itemGoals.Count + " goals";
                    _badgeText.color = ColText;
                }
                else if (KillGoal(entry) > 0 && !complete)
                {
                    _badgeText.text  = KillCountState.Get(player, entry.Id) + " / " + KillGoal(entry);
                    _badgeText.color = ColText;
                }
                else
                {
                    _badgeText.text  = complete ? "[Complete]" : "";
                    _badgeText.color = ColGreen;
                }
            }

            // Body text: completed entries with a summary show that recap first.
            // In-progress chains show the current step description (what to DO).
            // Completed chains without a summary fall back to the last step's message.
            if (complete && !string.IsNullOrEmpty(entry.Summary))
            {
                _bodyText.text = "<color=#96F296><b>Quest Complete</b></color>\n\n" + Template(entry.Summary, entry);
            }
            else if (entry.Steps != null && entry.Steps.Count > 0)
            {
                GuidanceStep displayStep;
                if (complete)
                {
                    displayStep = entry.Steps[entry.Steps.Count - 1];
                    _bodyText.text = StepMessage(entry, displayStep);
                }
                else
                {
                    var idx = ChainState.GetStep(player, entry.Id);
                    displayStep = idx < entry.Steps.Count ? entry.Steps[idx] : null;
                    _bodyText.text = Template(displayStep?.Description ?? "", entry, displayStep);
                }
            }
            else
            {
                // Non-chain entry: show its message, optionally prepended with collection progress.
                var body = Template(!string.IsNullOrEmpty(entry.Message)
                    ? entry.Message : entry.Display?.Text ?? "", entry);

                var submitGoal = ItemSubmitGoal(entry);
                if (submitGoal > 0 && !complete)
                {
                    var cur = SubmitState.Get(player, entry.Id);
                    var item = entry.Trigger?.Item;
                    var label = string.IsNullOrEmpty(item) ? "items" : item;
                    body = $"Submitted {cur} / {submitGoal} {label}." +
                           (string.IsNullOrEmpty(body) ? "" : "\n\n" + body);
                }

                var itemGoals = entry.Trigger != null
                    ? ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger) : null;
                if (itemGoals != null && !complete)
                {
                    var progressText = ItemAcquiredTrigger.BuildGoalProgressText(player, itemGoals);
                    if (!string.IsNullOrEmpty(progressText))
                        body = progressText + (string.IsNullOrEmpty(body) ? "" : "\n\n" + body);
                }

                var killGoal = KillGoal(entry);
                if (killGoal > 0 && !complete)
                {
                    var creature = entry.Trigger?.Creature;
                    var label = string.IsNullOrEmpty(creature) ? "kills" : creature;
                    body = $"Defeated {KillCountState.Get(player, entry.Id)} / {killGoal} {label}." +
                           (string.IsNullOrEmpty(body) ? "" : "\n\n" + body);
                }

                _bodyText.text = body;
            }

            if (_bodyContentRect != null)
            {
                // Re-flow before measuring: the body keeps the wrap points from the previous
                // entry (and from the zero-width rect it had while the root was inactive) until
                // its geometry is regenerated, which would leave the ContentSizeFitter sizing the
                // scroll content to the wrong height and clip the tail of a long guide.
                _bodyText.SetAllDirty();
                _bodyText.ForceMeshUpdate();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyContentRect);
            }

            PopulateUpcoming(entry, player);
        }

        /// Grow the title band to fit a wrapped quest title and slide everything below it down:
        /// the rule, the two toggle pills, and the top of the body scroll. Without this a title
        /// that needs a third line paints straight over the divider and the pills.
        private void LayoutTitleArea()
        {
            if (_titleAreaRect == null || _titleText == null) return;

            // Real rect width once laid out; the 70% title column of the current panel otherwise.
            var width = _titleText.rectTransform.rect.width;
            if (width < 40f)
                width = (_panelWidth - 2f * _frameInset - _leftWidth - PaneGap) * 0.70f - 8f;

            var height = TitleAreaBaseHeight;
            if (!string.IsNullOrEmpty(_titleText.text) && _titleText.font != null
                && _titleText.gameObject.activeInHierarchy)
            {
                var textH = _titleText.GetPreferredValues(_titleText.text, width, 0f).y;
                height = Mathf.Max(TitleAreaBaseHeight, Mathf.Ceil(textH) + 20f);
            }

            _titleAreaRect.sizeDelta = new Vector2(_titleAreaRect.sizeDelta.x, height);

            if (_titleDivRect != null)
                _titleDivRect.anchoredPosition = new Vector2(0f, -height);

            SetToggleBarTop(_trackToggleGo, height);
            SetToggleBarTop(_hideToggleGo,  height);

            if (_bodyScrollRect != null)
                _bodyScrollRect.offsetMax =
                    new Vector2(_bodyScrollRect.offsetMax.x, -BodyTopOffset(height));
        }

        /// Re-pin a toggle pill to sit just under a title band of `titleHeight` (4px gap).
        private static void SetToggleBarTop(GameObject pill, float titleHeight)
        {
            var rect = pill != null ? pill.GetComponent<RectTransform>() : null;
            if (rect == null) return;
            rect.offsetMax = new Vector2(rect.offsetMax.x, -(titleHeight + 4f));
            rect.offsetMin = new Vector2(rect.offsetMin.x, -(titleHeight + 4f + ToggleBarHeight));
        }

        // ── Tracker pin toggle ────────────────────────────────────────────────────────────────

        /// Show/refresh the "Pin to Tracker" pill for the selected entry. Hidden for finished
        /// quests and for entries that never appear on the tracker (no title / non-progress type).
        private void UpdateTrackToggle(GuidanceEntry entry, Player player, bool complete)
        {
            UpdateHideToggle(entry, player);

            if (_trackToggleGo == null) return;
            if (entry == null || player == null || complete || !IsTrackable(entry, player))
            {
                _trackToggleGo.SetActive(false);
                return;
            }

            _trackToggleGo.SetActive(true);
            var on = TrackedQuestState.IsTracked(player, entry.Id);
            if (_trackToggleText != null)
            {
                // Short labels: these are vanilla buttons with hover/press states now, so the
                // "(click to pin)" hint is redundant — and dropping it keeps the label inside the
                // pill at the larger font even when the panel is at its narrowest.
                _trackToggleText.text  = on ? "[x] Pinned to Tracker" : "[ ] Pin to Tracker";
                _trackToggleText.color = on ? ColGreen : ColText;
            }
        }

        /// Show/refresh the "Hide from list" pill. Hidden only when there is nothing selected.
        private void UpdateHideToggle(GuidanceEntry entry, Player player)
        {
            if (_hideToggleGo == null) return;
            if (entry == null || player == null)
            {
                _hideToggleGo.SetActive(false);
                return;
            }

            _hideToggleGo.SetActive(true);
            var hidden = HiddenQuestState.IsHidden(player, entry.Id);
            if (_hideToggleText != null)
            {
                _hideToggleText.text  = hidden ? "[x] Hidden — unhide" : "[ ] Hide from list";
                _hideToggleText.color = hidden ? ColLocked : ColText;
            }
        }

        /// Click handler: flip the selected quest's tracker pin. Delegates to the HUD tracker so
        /// pinning also force-unhides the panel.
        private void ToggleTrackSelected()
        {
            var player = Player.m_localPlayer;
            if (_selected == null || player == null) return;
            if (IsEntryComplete(_selected, player) || !IsTrackable(_selected, player)) return;

            var now = !TrackedQuestState.IsTracked(player, _selected.Id);
            GuidanceHudTracker.Instance?.SetTracked(_selected.Id, now);
            UpdateTrackToggle(_selected, player, false);
        }

        /// True when an entry would actually render a row on the HUD tracker: an in-progress quest
        /// with a title that is a chain, a multi-count kill / item-submit, or an item_acquired goal.
        private static bool IsTrackable(GuidanceEntry entry, Player player)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Title)) return false;
            if (entry.Steps != null && entry.Steps.Count > 0) return true;
            if (entry.Trigger == null) return false;

            if (string.Equals(entry.Trigger.Type, "npc_item_submit",
                    System.StringComparison.OrdinalIgnoreCase))
                return (entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count) > 1;
            if (string.Equals(entry.Trigger.Type, "kill",
                    System.StringComparison.OrdinalIgnoreCase))
                return (entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count) > 1;
            if (string.Equals(entry.Trigger.Type, "item_acquired",
                    System.StringComparison.OrdinalIgnoreCase))
                return ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger) != null;
            return false;
        }

        private void PopulateUpcoming(GuidanceEntry entry, Player player)
        {
            ClearUpcoming();
            if (entry?.Steps == null || entry.Steps.Count == 0) return;

            var complete    = IsEntryComplete(entry, player);
            var currentStep = complete ? entry.Steps.Count : ChainState.GetStep(player, entry.Id);
            // Nothing left to preview (a finished chain, or one on its last step): leave the
            // section hidden and let the guide text have the space.
            if (currentStep + 1 >= entry.Steps.Count) return;
            ShowUpcomingSection(true);

            // Build inactive so each TMP's null-font Awake warning is deferred until after the
            // font is assigned (see note in PopulatePanel). Reactivated below.
            _upcomingContent.gameObject.SetActive(false);

            // Steps after the current one are "upcoming / locked".
            for (var i = currentStep + 1; i < entry.Steps.Count; i++)
            {
                var step   = entry.Steps[i];
                var label  = "  Step " + (i + 1) + ": " + StepTitle(StepMessage(entry, step)) + "   (locked)";

                var rowGo = new GameObject("VSG_UpRow");
                rowGo.transform.SetParent(_upcomingContent, false);
                var rowLe = rowGo.AddComponent<LayoutElement>();
                // Pin the min too — a long chain overflows the 155px section and would otherwise
                // compress every step row to nothing (same failure as the guide list). Height is
                // the wrapped measurement, so a long step title takes the lines it needs and the
                // section scrolls rather than clipping it.
                rowLe.minHeight       = MeasureRow(label, UpcomingRowWidth, 22f, FsUpcoming);
                rowLe.preferredHeight = rowLe.minHeight;
                var rowTmp = rowGo.AddComponent<TextMeshProUGUI>();
                ApplyFont(rowTmp);
                rowTmp.text               = label;
                rowTmp.fontStyle          = FontStyles.Italic;
                rowTmp.fontSize           = FsUpcoming;
                rowTmp.color              = ColLocked;
                rowTmp.alignment          = TextAlignmentOptions.TopLeft;
                rowTmp.enableWordWrapping = true;
                rowTmp.overflowMode       = TextOverflowModes.Overflow;
                rowTmp.raycastTarget      = false;
            }

            _upcomingContent.gameObject.SetActive(true);

            // Same inactive-build caveat as the guide list — regenerate the row meshes.
            if (_upcomingContent is RectTransform upRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(upRect);
            ForceMeshUpdateIn(_upcomingContent);
            if (_upcomingScroll != null) _upcomingScroll.verticalNormalizedPosition = 1f;
        }

        /// Regenerate the text geometry of every TMP under a container that was populated while
        /// inactive. Without this the label rects are correct but the mesh stays empty, which
        /// renders as an invisible row.
        private static void ForceMeshUpdateIn(Transform container)
        {
            if (container == null) return;
            foreach (var t in container.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                if (t == null) continue;
                t.SetAllDirty();
                t.ForceMeshUpdate();
            }
        }

        private void ClearUpcoming()
        {
            ClearChildren(_upcomingContent);
            ShowUpcomingSection(false);
        }

        /// Show or hide the whole "Upcoming Steps" section, handing the space it reserves at the
        /// bottom of the right pane to the guide text while it is hidden.
        private void ShowUpcomingSection(bool show)
        {
            if (_upcomingGo != null) _upcomingGo.SetActive(show);
            if (_bodyScrollRect != null)
                _bodyScrollRect.offsetMin =
                    new Vector2(_bodyScrollRect.offsetMin.x, show ? UpcomingHeight + 8f : 4f);
        }

        /// Destroys all children of a container. The `t == null` check uses Unity's overloaded
        /// equality, so it returns early for both a genuine null AND a destroyed ("fake-null")
        /// transform — iterating a destroyed transform throws NRE on get_childCount. Indexed
        /// backward loop avoids the foreach enumerator entirely.
        private void ClearChildren(Transform t)
        {
            if (t == null) return;
            for (var i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                // Destroy is deferred to end-of-frame, and a LayoutGroup counts any child that is
                // still activeInHierarchy. Repopulating in the same frame would otherwise lay out
                // the old rows plus the new ones. Deactivating first takes them out immediately.
                child.SetActive(false);
                Destroy(child);
            }
        }

        // ── Visibility helpers ────────────────────────────────────────────────────────────────

        private static bool IsVisible(GuidanceEntry entry, Player player)
        {
            // Entries locked by unmet prerequisites are completely hidden.
            if (!PrerequisiteChecker.AllSatisfied(entry.Requires, player, Plugin.CurrentConfig))
                return false;

            // Must have been triggered — same rules as the HUD tracker's "started" check.
            if (entry.Steps != null && entry.Steps.Count > 0)
            {
                if (ChainState.IsComplete(player, entry.Id)) return true;
                var stepIdx = ChainState.GetStep(player, entry.Id);
                if (stepIdx > 0) return true;
                // Counter step 0 activated (primary trigger fired but goal not yet reached).
                if (entry.Steps[0].ProgressGoal > 0
                    && ChainState.GetCounter(player, entry.Id, 0) >= 0) return true;
                return false;
            }

            // Non-chain entry: visible once a collection goal is in progress, all goals are
            // currently met, or the entry has fired at least once before.
            // HasFired is kept as a final fallback so the entry remains visible even after
            // its materials have been consumed (IsEntryComplete would then return false).
            if (HasItemSubmitProgress(entry, player)) return true;
            if (HasItemAcquiredGoalProgress(entry, player)) return true;
            if (HasKillProgress(entry, player)) return true;
            if (IsEntryComplete(entry, player)) return true;
            return SeenTracker.HasFired(player, entry.Id, entry.Scope ?? "player");
        }

        private static bool IsEntryComplete(GuidanceEntry entry, Player player)
        {
            if (entry.Steps != null && entry.Steps.Count > 0)
                return ChainState.IsComplete(player, entry.Id);

            // item_acquired multi-goal entries: live inventory check.
            // HasFired is only set at the moment of completion and does not update if items are
            // later consumed, so we re-verify the current inventory on every codex render.
            var goals = entry.Trigger != null
                ? ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger) : null;
            if (goals != null)
            {
                foreach (var g in goals)
                    if (ItemAcquiredTrigger.CountInInventory(player, g.Item) < g.Count)
                        return false;
                return true;
            }

            return SeenTracker.HasFired(player, entry.Id, entry.Scope ?? "player");
        }

        /// Effective required count for a multi-count `kill` entry, or 0 if the entry is not a
        /// multi-count kill entry.
        private static int KillGoal(GuidanceEntry entry)
        {
            if (entry?.Trigger == null) return 0;
            if (!string.Equals(entry.Trigger.Type, "kill",
                    System.StringComparison.OrdinalIgnoreCase)) return 0;
            var goal = entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count;
            return goal > 1 ? goal : 0;
        }

        /// True while a multi-count kill entry is mid-progress (0 < kills < goal). Kills can't be
        /// recounted from the inventory, so this reads the persistent KillCountState accumulator.
        private static bool HasKillProgress(GuidanceEntry entry, Player player)
        {
            var goal = KillGoal(entry);
            if (goal == 0) return false;
            var cur = KillCountState.Get(player, entry.Id);
            return cur > 0 && cur < goal;
        }

        /// Effective required count for a multi-count npc_item_submit entry, or 0 if the entry
        /// is not a multi-count item-submit entry.
        private static int ItemSubmitGoal(GuidanceEntry entry)
        {
            if (entry?.Trigger == null) return 0;
            if (!string.Equals(entry.Trigger.Type, "npc_item_submit",
                    System.StringComparison.OrdinalIgnoreCase)) return 0;
            var goal = entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count;
            return goal > 1 ? goal : 0;
        }

        /// True while a multi-count item-submit entry is mid-collection (0 < submitted < goal).
        private static bool HasItemSubmitProgress(GuidanceEntry entry, Player player)
        {
            var goal = ItemSubmitGoal(entry);
            if (goal == 0) return false;
            var cur = SubmitState.Get(player, entry.Id);
            return cur > 0 && cur < goal;
        }

        /// True while an item_acquired multi-goal entry is mid-collection: the player has
        /// collected toward ANY goal but has not yet finished them all. A goal that is already
        /// complete (or one sitting at 0) does not hide the entry — as long as some goal has
        /// progress, the quest is considered started and stays visible until every goal is met.
        private static bool HasItemAcquiredGoalProgress(GuidanceEntry entry, Player player)
        {
            if (entry?.Trigger == null) return false;
            if (!string.Equals(entry.Trigger.Type, "item_acquired",
                    System.StringComparison.OrdinalIgnoreCase)) return false;
            var goals = ItemAcquiredTrigger.GetEffectiveGoals(entry.Trigger);
            if (goals == null) return false;

            var anyProgress = false;
            var allComplete = true;
            foreach (var g in goals)
            {
                var cur = ItemAcquiredTrigger.CountInInventory(player, g.Item);
                if (cur > 0) anyProgress = true;
                if (cur < g.Count) allComplete = false;
            }
            // The latched "started" flag keeps the entry in progress even after every goal item
            // has been removed from the inventory, so it never disappears once collection began.
            var started = GoalStartedState.IsStarted(player, entry.Id);
            // Visible while started but not yet finished. When all goals are met this returns
            // false, but IsEntryComplete is then true and IsVisible keeps it shown.
            return (anyProgress || started) && !allComplete;
        }

        // ── Text helpers ──────────────────────────────────────────────────────────────────────

        private static string StepMessage(GuidanceEntry entry, GuidanceStep step)
        {
            if (step == null) return "";
            if (!string.IsNullOrEmpty(step.Message)) return Template(step.Message, entry, step);
            return Template(step.Display?.Text ?? "", entry, step);
        }

        /// Expand {playerName}/{player_name}/{biome}/… and apply the entry's `highlight:` rules
        /// to any config text the Codex renders.
        private static string Template(string text, GuidanceEntry entry, GuidanceStep step = null)
            => GuidanceDispatcher.RenderLocal(entry, text, step) ?? "";

        /// For the current (incomplete) step: prefer Description over Message so the Codex
        /// shows what the player needs to DO rather than the reward text that fires on completion.
        private static string StepDescription(GuidanceEntry entry, GuidanceStep step)
        {
            if (step == null) return "";
            if (!string.IsNullOrEmpty(step.Description)) return Template(step.Description, entry, step);
            return StepMessage(entry, step);
        }

        /// Flatten a step's message onto one logical line for the "Upcoming Steps" list. It is
        /// only newlines that are collapsed — the text itself is never cut short (the rows wrap
        /// and the section scrolls), so a locked step always reads in full.
        private static string StepTitle(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(step)";
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        // ── Font helpers ──────────────────────────────────────────────────────────────────────

        private void ApplyFont(TMP_Text t)
        {
            if (_font != null && t != null) t.font = _font;
        }

        private void ApplyFontToPanel()
        {
            if (_panel == null || _font == null) return;
            foreach (var t in _panel.GetComponentsInChildren<TMP_Text>(includeInactive: true))
                if (t != null) t.font = _font;
        }

        /// Resolve the font the vanilla inventory window uses and push it to all existing panel
        /// texts. Called from Open() while the root is still inactive so the static texts' Awake
        /// doesn't warn.
        ///
        /// The inventory's own font is the one we want, but it can only be read once the GUI scene
        /// is up — so a font resolved from anywhere else is treated as provisional and re-resolved
        /// on the next Open() rather than cached for the session.
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
            if (_font != null) ApplyFontToPanel();
        }

        // ── Open / Close ──────────────────────────────────────────────────────────────────────

        public void Open()
        {
            if (!(Plugin.CodexEnabled?.Value ?? true)) return;
            if (GuidanceDisplay.IntroLockActive) return;
            if (_uiRoot == null) return;
            IsOpen = true;
            // Apply the font BEFORE activating so the static panel texts' Awake (fired on
            // activation) sees a valid font and doesn't log the LiberationSans warning.
            EnsureFont();
            _uiRoot.SetActive(true);
            // The inventory GUI usually appears after Hud.Awake built this panel, so this is where
            // the vanilla frame/insets/buttons normally land — and where the panel takes its final
            // size, before PopulatePanel measures any row against it. After SetActive, so the
            // CanvasScaler has published the scale factor the fit is measured in.
            ApplyVanillaStyle();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            if (GameCamera.instance != null) GameCamera.instance.m_mouseCapture = false;
            PopulatePanel();
        }

        /// Repopulate the guide list and detail pane from the current config + player state,
        /// without tearing the panel down. No-op while closed (Open() populates anyway).
        /// Called by `vsg_refresh` and whenever a fresh config arrives from the server.
        public void RepopulateIfOpen()
        {
            if (!IsOpen || _uiRoot == null) return;
            // Keep whatever the player was reading — a background config push or an admin reset
            // should not yank the detail pane back to the first guide.
            PopulatePanel(keepSelection: true);
        }

        /// Destroy the whole codex UI and build it again from scratch, then reopen it if it was
        /// open. This is the recovery path for a panel whose geometry or fonts were resolved
        /// against an incomplete scene — e.g. the vanilla font was still unresolved at
        /// Hud.Awake, leaving rows as blank gaps, or a stale root survived a HUD reload.
        public void Rebuild()
        {
            var wasOpen = IsOpen;
            if (wasOpen) Close();

            if (_uiRoot != null)
            {
                // Deactivate first: Destroy is deferred to end-of-frame, and a live backdrop
                // would otherwise sit on top of the new root for the rest of this frame.
                _uiRoot.SetActive(false);
                Destroy(_uiRoot);
            }

            _uiRoot           = null;
            _panel            = null;
            _panelRect        = null;
            _headerRect       = null;
            _headerSepRect    = null;
            _contentRect      = null;
            _leftRect         = null;
            _vDivRect         = null;
            _rightRect        = null;
            _panelImg         = null;
            _leftImg          = null;
            _bodyBgImg        = null;
            _upcomingBgImg    = null;
            _upcomingGo       = null;
            _buttons.Clear();
            // Re-styled and re-fitted from scratch by the rebuilt panel; the sprites themselves are
            // still valid (VanillaUi caches per scene, not per panel).
            _styled           = false;
            _fittedScreen     = Vector2.zero;
            _fittedScale      = 0f;
            _leftContent      = null;
            _upcomingScroll   = null;
            _upcomingContent  = null;
            _titleText        = null;
            _badgeText        = null;
            _bodyText         = null;
            _bodyContentRect  = null;
            _titleAreaRect    = null;
            _titleDivRect     = null;
            _bodyScrollRect   = null;
            _measureText      = null;   // its GameObject lived under the destroyed _uiRoot
            _trackToggleGo    = null;
            _trackToggleText  = null;
            _hideToggleGo     = null;
            _hideToggleText   = null;
            _showHiddenText   = null;
            _listScroll       = null;
            _guideRows.Clear();
            _items.Clear();
            _selected = null;
            // Drop the cached font so it is re-resolved against the current scene — a null font
            // captured at build time is the usual cause of invisible rows.
            _font              = null;
            _fontFromInventory = false;

            BuildPanel();
            if (wasOpen) Open();
        }

        public void Close()
        {
            IsOpen = false;
            // Deactivate the whole root so the full-screen backdrop button stops intercepting
            // clicks meant for the game's inventory/crafting UI.
            if (_uiRoot != null) _uiRoot.SetActive(false);
            if (GameCamera.instance != null) GameCamera.instance.m_mouseCapture = true;
        }

        // ── Per-frame ─────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            // Intro cinematic: close immediately if open (CRIT-07).
            if (IsOpen && GuidanceDisplay.IntroLockActive)
            {
                Close();
                return;
            }

            var codexKey = ResolveCodexKey();
            if (codexKey != KeyCode.None && Input.GetKeyDown(codexKey))
            {
                if (IsOpen) Close();
                else Open();
            }

            // ESC closes the codex when it is the frontmost panel.
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
                Close();
        }

        private static KeyCode ResolveCodexKey()
        {
            var keyStr = Plugin.CodexKey?.Value ?? "F3";
            if (string.IsNullOrEmpty(keyStr)) return KeyCode.None;
            return System.Enum.TryParse<KeyCode>(keyStr, ignoreCase: true, out var kc) ? kc : KeyCode.None;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // _uiRoot is a scene-root object not under this transform — destroy it explicitly.
            if (_uiRoot != null) Destroy(_uiRoot);
        }
    }

    // ── Harmony Patches ───────────────────────────────────────────────────────────────────────

    /// Spawns the Codex component each time the Hud scene loads. Runs alongside HudAwakePatch
    /// (the tracker patch) — HarmonyX supports multiple Postfix patches on the same method.
    [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
    internal static class HudAwakeCodexPatch
    {
        private static void Postfix(Hud __instance)
        {
            if (!(Plugin.CodexEnabled?.Value ?? true)) return;
            // If Hud.Awake runs again (e.g. the game recreates the HUD), tear down the previous
            // codex first. Otherwise the stale instance's _uiRoot is orphaned and the new patch
            // would coexist with a half-destroyed one.
            if (GuidanceCodex.Instance != null)
                Object.Destroy(GuidanceCodex.Instance.gameObject);

            var go = new GameObject("VSG_Codex");
            go.transform.SetParent(__instance.transform, worldPositionStays: false);
            GuidanceCodex.Instance = go.AddComponent<GuidanceCodex>();
            GuidanceCodex.Instance.BuildPanel();
            Plugin.Log.LogInfo("[codex] Codex panel created.");
        }
    }

    /// Blocks Menu.Show (the ESC pause menu entry point) while the codex is open so that
    /// pressing ESC closes the codex rather than simultaneously opening the game menu.
    /// Our own Update() loop handles ESC → Close() directly.
    [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
    internal static class MenuShowCodexPatch
    {
        private static bool Prefix()
        {
            // Skip Menu.Show when codex is open (ESC is consumed by the codex's Update loop).
            if (GuidanceCodex.Instance != null && GuidanceCodex.Instance.IsOpen)
                return false;
            return true;
        }
    }

    /// While the codex is open the mouse wheel belongs to its scroll views, not to the game.
    /// `GameCamera.UpdateCamera` gates zooming on a hard-coded list of `!SomeGui.IsVisible()`
    /// checks that a modded panel can never join, so the wheel is hidden from ZInput instead for
    /// as long as the codex is up — that also stops the hotbar from cycling underneath it.
    /// Unity's UI reads the wheel through the EventSystem, not ZInput, so the codex's own
    /// scrolling is unaffected.
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInputScrollCodexPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (GuidanceCodex.Instance == null || !GuidanceCodex.Instance.IsOpen) return true;
            __result = 0f;
            return false;
        }
    }

    /// Prevents Game.Pause from triggering when the codex is the only panel open.
    /// In singleplayer Valheim pauses when ESC/Menu opens; the codex should not pause the game.
    [HarmonyPatch(typeof(Game), nameof(Game.Pause))]
    internal static class GamePauseCodexPatch
    {
        private static bool Prefix()
        {
            // Skip pause when codex is open (the game menu itself is not open).
            if (GuidanceCodex.Instance != null && GuidanceCodex.Instance.IsOpen)
                return false;
            return true;
        }
    }
}
