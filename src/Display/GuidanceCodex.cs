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

        // ── Left panel ────────────────────────────────────────────────────────────────────────
        private Transform _leftContent;  // VerticalLayoutGroup content for guide rows
        private readonly List<(GuidanceEntry entry, GameObject row, TMP_Text label)> _guideRows
            = new List<(GuidanceEntry, GameObject, TMP_Text)>();
        private GuidanceEntry _selected;

        // ── Left panel paging ─────────────────────────────────────────────────────────────────
        // Row metrics; PaginateItems uses these to decide how much fits on one page. They must
        // match the LayoutElement heights set in PopulatePanel or a page can overflow.
        private const float CatRowHeight   = 22f;
        private const float QuestRowHeight = 20f;
        private const float RowSpacing     = 1f;
        private const float FooterHeight   = 48f;
        // Used only if the list rect has not resolved yet (panel height 520 - 36 header
        // - 26 CATEGORIES - 48 footer - 8 padding).
        private const float FallbackListHeight = 402f;

        private TMP_Text _pageLabelText;
        private TMP_Text _prevPageText;
        private TMP_Text _nextPageText;
        private TMP_Text _showHiddenText;
        private int  _page;         // current page index into _pages
        private bool _showHidden;   // when true, hidden quests are listed (dimmed) so they can be unhidden
        private readonly List<List<PageItem>> _pages = new List<List<PageItem>>();

        /// One rendered row on a page: either a category header or a quest row.
        private struct PageItem
        {
            public bool Category;
            public string CategoryName;
            public GuidanceEntry Entry;
            public bool Hidden;
        }

        // ── Right panel ───────────────────────────────────────────────────────────────────────
        private TMP_Text _titleText;
        private TMP_Text _badgeText;
        private TMP_Text _bodyText;
        private RectTransform _bodyContentRect;
        private ScrollRect _upcomingScroll;
        private Transform _upcomingContent;

        // ── Tracker pin toggle (in-progress quests only) ──────────────────────────────────────
        private GameObject _trackToggleGo;
        private TMP_Text _trackToggleText;

        // ── Hide-from-list toggle (any selected quest) ────────────────────────────────────────
        private GameObject _hideToggleGo;
        private TMP_Text _hideToggleText;

        // ── Font (lazy resolution, same pattern as GuidanceHudTracker) ───────────────────────
        private TMP_FontAsset _font;

        // ── Palette ───────────────────────────────────────────────────────────────────────────
        private static readonly Color ColGold    = new Color(1f, 0.82f, 0.42f);
        private static readonly Color ColText    = new Color(0.90f, 0.88f, 0.82f);
        private static readonly Color ColLocked  = new Color(0.50f, 0.50f, 0.50f);
        private static readonly Color ColGreen   = new Color(0.60f, 0.95f, 0.60f);
        private static readonly Color ColBg      = new Color(0.06f, 0.05f, 0.04f, 0.96f);
        private static readonly Color ColPanelBg = new Color(0.10f, 0.08f, 0.06f, 0.92f);
        private static readonly Color ColHdrBar  = new Color(0.12f, 0.10f, 0.08f, 1.00f);
        private static readonly Color ColDivider = new Color(0.30f, 0.25f, 0.20f, 1.00f);
        private static readonly Color ColRowSel  = new Color(0.22f, 0.17f, 0.10f, 0.90f);

        // ── Construction ─────────────────────────────────────────────────────────────────────

        /// Called once from HudAwakeCodexPatch after the component is added to the scene.
        public void BuildPanel()
        {
            // Dedicated root canvas — same pattern as GuidanceHudTracker.UiRoot().
            _uiRoot = new GameObject("VSG_CodexRoot");
            var canvas = _uiRoot.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1100; // above tracker (1000)

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
            bdImg.color = new Color(0f, 0f, 0f, 0.60f);
            var bdBtn = backdrop.AddComponent<Button>();
            bdBtn.transition = Selectable.Transition.None;
            bdBtn.onClick.AddListener(Close);

            // Main panel (centred, 780×520).
            _panel = new GameObject("VSG_CodexPanel");
            _panel.transform.SetParent(_uiRoot.transform, false);
            var panelRect = _panel.AddComponent<RectTransform>();
            panelRect.anchorMin        = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax        = new Vector2(0.5f, 0.5f);
            panelRect.pivot            = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta        = new Vector2(780f, 520f);
            panelRect.anchoredPosition = Vector2.zero;
            _panel.AddComponent<Image>().color = ColBg;

            BuildHeader(panelRect);
            BuildContentArea(panelRect);
            // _uiRoot was already deactivated above; it stays hidden (and raycast-free, so it
            // doesn't block the game's inventory/crafting clicks) until Open().
        }

        private void BuildHeader(RectTransform panelRect)
        {
            var hdrGo = new GameObject("VSG_CodexHeader");
            hdrGo.transform.SetParent(panelRect, false);
            var hdrRect = hdrGo.AddComponent<RectTransform>();
            hdrRect.anchorMin        = new Vector2(0f, 1f);
            hdrRect.anchorMax        = new Vector2(1f, 1f);
            hdrRect.pivot            = new Vector2(0.5f, 1f);
            hdrRect.sizeDelta        = new Vector2(0f, 36f);
            hdrRect.anchoredPosition = Vector2.zero;
            hdrGo.AddComponent<Image>().color = ColHdrBar;

            // "GUIDE CODEX" label
            var titleGo = new GameObject("VSG_CodexHdrTitle");
            titleGo.transform.SetParent(hdrGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.8f, 1f);
            titleRect.offsetMin = new Vector2(10f, 0f);
            titleRect.offsetMax = Vector2.zero;
            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(titleTmp);
            titleTmp.text      = "GUIDE CODEX";
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.fontSize  = 16f;
            titleTmp.color     = ColGold;
            titleTmp.alignment = TextAlignmentOptions.Left;
            titleTmp.raycastTarget = false;

            // "[X] Close" button
            var closeGo = new GameObject("VSG_CodexClose");
            closeGo.transform.SetParent(hdrGo.transform, false);
            var closeRect = closeGo.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(0.8f, 0f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.offsetMin = Vector2.zero;
            closeRect.offsetMax = new Vector2(-8f, 0f);
            var closeTmp = closeGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(closeTmp);
            closeTmp.text      = "[X] Close";
            closeTmp.fontStyle = FontStyles.Normal;
            closeTmp.fontSize  = 13f;
            closeTmp.color     = ColText;
            closeTmp.alignment = TextAlignmentOptions.Right;
            closeTmp.raycastTarget = true;
            var closeBtn = closeGo.AddComponent<Button>();
            closeBtn.transition    = Selectable.Transition.None;
            closeBtn.targetGraphic = closeTmp;
            closeBtn.onClick.AddListener(Close);
        }

        private void BuildContentArea(RectTransform panelRect)
        {
            // Content area below the 36px header.
            var contentGo = new GameObject("VSG_CodexContent");
            contentGo.transform.SetParent(panelRect, false);
            var contentRect = contentGo.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 0f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = new Vector2(0f, -36f);

            BuildLeftPanel(contentGo.transform);

            // Vertical divider between left and right panels.
            var divGo = new GameObject("VSG_CodexVDiv");
            divGo.transform.SetParent(contentGo.transform, false);
            var divRect = divGo.AddComponent<RectTransform>();
            divRect.anchorMin = new Vector2(0f, 0f);
            divRect.anchorMax = new Vector2(0f, 1f);
            divRect.offsetMin = new Vector2(200f, 4f);
            divRect.offsetMax = new Vector2(202f, -4f);
            divGo.AddComponent<Image>().color = ColDivider;

            BuildRightPanel(contentGo.transform);
        }

        private void BuildLeftPanel(Transform parent)
        {
            var leftGo = new GameObject("VSG_CodexLeft");
            leftGo.transform.SetParent(parent, false);
            var leftRect = leftGo.AddComponent<RectTransform>();
            leftRect.anchorMin = new Vector2(0f, 0f);
            leftRect.anchorMax = new Vector2(0f, 1f);
            leftRect.offsetMin = Vector2.zero;
            leftRect.offsetMax = new Vector2(200f, 0f);
            leftGo.AddComponent<Image>().color = ColPanelBg;

            // "CATEGORIES" header label.
            var catHdrGo = new GameObject("VSG_CodexCatHdr");
            catHdrGo.transform.SetParent(leftGo.transform, false);
            var catHdrRect = catHdrGo.AddComponent<RectTransform>();
            catHdrRect.anchorMin        = new Vector2(0f, 1f);
            catHdrRect.anchorMax        = new Vector2(1f, 1f);
            catHdrRect.pivot            = new Vector2(0.5f, 1f);
            catHdrRect.sizeDelta        = new Vector2(0f, 26f);
            catHdrRect.anchoredPosition = Vector2.zero;
            var catHdrTmp = catHdrGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(catHdrTmp);
            catHdrTmp.text             = "CATEGORIES";
            catHdrTmp.fontStyle        = FontStyles.Bold;
            catHdrTmp.fontSize         = 11f;
            catHdrTmp.color            = ColGold;
            catHdrTmp.alignment        = TextAlignmentOptions.Center;
            catHdrTmp.raycastTarget    = false;

            // Footer: page arrows + the "show hidden" toggle. Built before the list so the list
            // can anchor above it.
            BuildLeftFooter(leftGo.transform);

            // Guide list, between the CATEGORIES header and the footer. There is no scrolling
            // here by design — the list is PAGED, and PaginateItems only ever puts as many rows
            // on a page as the measured height can hold, so the container never overflows.
            //
            // That constraint matters: a VerticalLayoutGroup does not spill past a fixed-height
            // container, it *compresses* its children toward LayoutElement.minHeight. Rows that
            // leave min unset (i.e. 0) collapse to invisible slivers while category headers hold
            // their size — which is exactly how this list broke before. Rows now pin min AND
            // preferred, and pagination keeps the total under the container height. The
            // RectMask2D is belt-and-braces: if a page ever did overshoot, it clips instead of
            // painting over the footer.
            var listGo = new GameObject("VSG_LeftList");
            listGo.transform.SetParent(leftGo.transform, false);
            var listRect = listGo.AddComponent<RectTransform>();
            listRect.anchorMin = new Vector2(0f, 0f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.offsetMin = new Vector2(0f, FooterHeight);
            listRect.offsetMax = new Vector2(0f, -26f); // below the 26px CATEGORIES header
            listGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("VSG_LeftContent");
            contentGo.transform.SetParent(listGo.transform, false);
            // Capture the RectTransform RETURNED by AddComponent. Adding a RectTransform to a GO
            // that has a plain Transform replaces the transform, so a reference cached from
            // `.transform` BEFORE this call would dangle (and `as RectTransform` would be null,
            // leaving the VerticalLayoutGroup unable to lay out children).
            var cRect = contentGo.AddComponent<RectTransform>();
            _leftContent = cRect;
            cRect.anchorMin = Vector2.zero;
            cRect.anchorMax = Vector2.one;
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
        }

        /// Page navigation ("[<]  Page 1 / 4  [>]") plus the hidden-quest toggle, pinned to the
        /// bottom of the left pane.
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

            // ── Row 1: [<]  Page N / M  [>] ───────────────────────────────────────────────────
            _prevPageText = BuildFooterButton(footerGo.transform, "VSG_PrevPage", "[<]",
                new Vector2(0f, 1f), new Vector2(0.22f, 1f), new Vector2(4f, -24f),
                new Vector2(0f, -2f), TextAlignmentOptions.Left, () => ChangePage(-1));

            _nextPageText = BuildFooterButton(footerGo.transform, "VSG_NextPage", "[>]",
                new Vector2(0.78f, 1f), new Vector2(1f, 1f), new Vector2(0f, -24f),
                new Vector2(-4f, -2f), TextAlignmentOptions.Right, () => ChangePage(1));

            var pageGo = new GameObject("VSG_PageLabel");
            pageGo.transform.SetParent(footerGo.transform, false);
            var pageRect = pageGo.AddComponent<RectTransform>();
            pageRect.anchorMin = new Vector2(0.22f, 1f);
            pageRect.anchorMax = new Vector2(0.78f, 1f);
            pageRect.offsetMin = new Vector2(0f, -24f);
            pageRect.offsetMax = new Vector2(0f, -2f);
            _pageLabelText = pageGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_pageLabelText);
            _pageLabelText.text               = "";
            _pageLabelText.fontSize           = 11f;
            _pageLabelText.color              = ColText;
            _pageLabelText.alignment          = TextAlignmentOptions.Center;
            _pageLabelText.enableWordWrapping = false;
            _pageLabelText.overflowMode       = TextOverflowModes.Overflow;
            _pageLabelText.raycastTarget      = false;

            // ── Row 2: hidden-quest toggle ────────────────────────────────────────────────────
            _showHiddenText = BuildFooterButton(footerGo.transform, "VSG_ShowHidden", "",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(4f, -44f),
                new Vector2(-4f, -26f), TextAlignmentOptions.Center, ToggleShowHidden);
        }

        /// Small clickable TMP label used for the footer controls. Returns the text component so
        /// callers can restyle it later (dimming a dead arrow, relabelling the toggle).
        private TMP_Text BuildFooterButton(Transform parent, string name, string label,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
            TextAlignmentOptions align, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            ApplyFont(tmp);
            tmp.text               = label;
            tmp.fontStyle          = FontStyles.Bold;
            tmp.fontSize           = 11f;
            tmp.color              = ColText;
            tmp.alignment          = align;
            tmp.enableWordWrapping = false;
            tmp.overflowMode       = TextOverflowModes.Overflow;
            tmp.raycastTarget      = true;

            var btn = go.AddComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = tmp;
            btn.onClick.AddListener(onClick);
            return tmp;
        }

        private void BuildRightPanel(Transform parent)
        {
            var rightGo = new GameObject("VSG_CodexRight");
            rightGo.transform.SetParent(parent, false);
            var rightRect = rightGo.AddComponent<RectTransform>();
            rightRect.anchorMin = new Vector2(0f, 0f);
            rightRect.anchorMax = new Vector2(1f, 1f);
            rightRect.offsetMin = new Vector2(204f, 0f);
            rightRect.offsetMax = Vector2.zero;

            // ── Title + badge (top 50px) ──────────────────────────────────────────────────────
            var titleAreaGo = new GameObject("VSG_CodexTitleArea");
            titleAreaGo.transform.SetParent(rightGo.transform, false);
            var titleAreaRect = titleAreaGo.AddComponent<RectTransform>();
            titleAreaRect.anchorMin        = new Vector2(0f, 1f);
            titleAreaRect.anchorMax        = new Vector2(1f, 1f);
            titleAreaRect.pivot            = new Vector2(0.5f, 1f);
            titleAreaRect.sizeDelta        = new Vector2(0f, 50f);
            titleAreaRect.anchoredPosition = Vector2.zero;

            var titleGo = new GameObject("VSG_CodexEntryTitle");
            titleGo.transform.SetParent(titleAreaGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0.70f, 1f);
            titleRect.offsetMin = new Vector2(8f, 0f);
            titleRect.offsetMax = Vector2.zero;
            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_titleText);
            _titleText.text               = "Select a guide";
            _titleText.fontStyle          = FontStyles.Bold;
            _titleText.fontSize           = 15f;
            _titleText.color              = ColGold;
            _titleText.alignment          = TextAlignmentOptions.Left;
            // Wraps onto a second line inside the 50 px title band rather than ellipsising —
            // the detail pane is where the player reads the full quest name.
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
            _badgeText.fontSize           = 13f;
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
            hDivRect.anchoredPosition = new Vector2(0f, -50f);
            hDivGo.AddComponent<Image>().color = ColDivider;

            // ── Tracker pin toggle (just below the title divider) ─────────────────────────────
            BuildTrackToggle(rightGo.transform);

            // ── Upcoming steps section (bottom 160px) ─────────────────────────────────────────
            BuildUpcomingSection(rightGo.transform);

            // ── Body text (scrollable, between toggle bar and upcoming section) ───────────────
            BuildBodyScroll(rightGo.transform);
        }

        /// A clickable "Show on Tracker: ON/OFF" pill shown only when the selected quest is in
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
            rect.offsetMin = new Vector2(8f, -76f);
            rect.offsetMax = new Vector2(-3f, -54f);

            var img = _trackToggleGo.AddComponent<Image>();
            img.color = ColRowSel;

            var btn = _trackToggleGo.AddComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(ToggleTrackSelected);

            var labelGo = new GameObject("VSG_TrackToggleLabel");
            labelGo.transform.SetParent(_trackToggleGo.transform, false);
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            _trackToggleText = labelGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_trackToggleText);
            _trackToggleText.text               = "";
            _trackToggleText.fontStyle          = FontStyles.Bold;
            _trackToggleText.fontSize           = 12f;
            _trackToggleText.color              = ColText;
            _trackToggleText.alignment          = TextAlignmentOptions.Center;
            _trackToggleText.enableWordWrapping = false;
            _trackToggleText.raycastTarget      = false;

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
            rect.offsetMin = new Vector2(3f, -76f);
            rect.offsetMax = new Vector2(-8f, -54f);

            var img = _hideToggleGo.AddComponent<Image>();
            img.color = ColRowSel;

            var btn = _hideToggleGo.AddComponent<Button>();
            btn.transition    = Selectable.Transition.None;
            btn.targetGraphic = img;
            btn.onClick.AddListener(ToggleHideSelected);

            var labelGo = new GameObject("VSG_HideToggleLabel");
            labelGo.transform.SetParent(_hideToggleGo.transform, false);
            var labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(-6f, 0f);
            _hideToggleText = labelGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(_hideToggleText);
            _hideToggleText.text               = "";
            _hideToggleText.fontStyle          = FontStyles.Bold;
            _hideToggleText.fontSize           = 12f;
            _hideToggleText.color              = ColText;
            _hideToggleText.alignment          = TextAlignmentOptions.Center;
            _hideToggleText.enableWordWrapping = false;
            _hideToggleText.overflowMode       = TextOverflowModes.Overflow;
            _hideToggleText.raycastTarget      = false;
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
            scrollRect.offsetMin = new Vector2(8f, 160f);
            // Leave room for the title divider (54px) plus the tracker-pin toggle bar (~26px).
            scrollRect.offsetMax = new Vector2(-8f, -80f);
            scrollGo.AddComponent<RectMask2D>();

            var sv = scrollGo.AddComponent<ScrollRect>();
            sv.horizontal        = false;
            sv.vertical          = true;
            sv.scrollSensitivity = 20f;
            sv.movementType      = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("VSG_BodyVp");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = vpGo.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;

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
            _bodyText.fontSize           = 14f;
            _bodyText.color              = ColText;
            _bodyText.alignment          = TextAlignmentOptions.TopLeft;
            _bodyText.enableWordWrapping = true;
            _bodyText.raycastTarget      = false;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sv.viewport = vpRect;
            sv.content  = _bodyContentRect;
        }

        private void BuildUpcomingSection(Transform rightGo)
        {
            var upGo = new GameObject("VSG_CodexUpcoming");
            upGo.transform.SetParent(rightGo, false);
            var upRect = upGo.AddComponent<RectTransform>();
            upRect.anchorMin        = new Vector2(0f, 0f);
            upRect.anchorMax        = new Vector2(1f, 0f);
            upRect.pivot            = new Vector2(0.5f, 0f);
            upRect.sizeDelta        = new Vector2(-16f, 155f);
            upRect.anchoredPosition = new Vector2(0f, 4f);

            // Header label.
            var hdrGo = new GameObject("VSG_UpHdr");
            hdrGo.transform.SetParent(upGo.transform, false);
            var hdrRect = hdrGo.AddComponent<RectTransform>();
            hdrRect.anchorMin        = new Vector2(0f, 1f);
            hdrRect.anchorMax        = new Vector2(1f, 1f);
            hdrRect.pivot            = new Vector2(0.5f, 1f);
            hdrRect.sizeDelta        = new Vector2(0f, 20f);
            hdrRect.anchoredPosition = Vector2.zero;
            var hdrTmp = hdrGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(hdrTmp);
            hdrTmp.text             = "-- Upcoming Steps --";
            hdrTmp.fontStyle        = FontStyles.Italic;
            hdrTmp.fontSize         = 12f;
            hdrTmp.color            = new Color(0.65f, 0.65f, 0.65f);
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
            divRect.anchoredPosition = new Vector2(0f, -20f);
            divGo.AddComponent<Image>().color = ColDivider;

            // Scroll view for the upcoming step rows — a chain with more than ~7 remaining steps
            // does not fit the 155px section, and the rows now hold their height instead of
            // compressing, so the overflow has to be scrollable and clipped.
            var scrollGo = new GameObject("VSG_UpScroll");
            scrollGo.transform.SetParent(upGo.transform, false);
            var scrollRect = scrollGo.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0f, -22f);
            scrollGo.AddComponent<RectMask2D>();

            _upcomingScroll = scrollGo.AddComponent<ScrollRect>();
            _upcomingScroll.horizontal        = false;
            _upcomingScroll.vertical          = true;
            _upcomingScroll.scrollSensitivity = 20f;
            _upcomingScroll.movementType      = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("VSG_UpVp");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRect = vpGo.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = Vector2.zero;

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
        }

        // ── Data population ───────────────────────────────────────────────────────────────────

        /// Rebuild the page model from the current config + player state and render the current
        /// page. `keepSelection` preserves the highlighted quest when it is still listed (used
        /// after hiding/unhiding or a config push); a fresh Open() drops it.
        private void PopulatePanel(bool keepSelection = false)
        {
            EnsureFont();

            var player = Player.m_localPlayer;
            var config = Plugin.CurrentConfig;

            var previous = keepSelection ? _selected : null;

            ClearChildren(_leftContent);
            _guideRows.Clear();
            _selected = null;
            _pages.Clear();

            if (config?.Guidances == null || player == null)
            {
                _page = 0;
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

            PaginateItems(catOrder, byCategory, player);

            if (_page >= _pages.Count) _page = _pages.Count - 1;
            if (_page < 0) _page = 0;

            RenderPage(player);
            UpdateFooter(player);

            // Restore the previous selection when it survived the repopulate, otherwise fall back
            // to the first quest on this page.
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

        /// Split the grouped entries into pages that each fit the measured list height. A
        /// category spanning a page break repeats its header on the next page so no row is ever
        /// orphaned under the wrong heading.
        private void PaginateItems(List<string> catOrder,
            Dictionary<string, List<GuidanceEntry>> byCategory, Player player)
        {
            var capacity = FallbackListHeight;
            if (_leftContent is RectTransform lcRect)
            {
                // 8 = the VerticalLayoutGroup's 4px top + bottom padding.
                var measured = lcRect.rect.height - 8f;
                if (measured > CatRowHeight + QuestRowHeight) capacity = measured;
            }

            var current = new List<PageItem>();
            var used    = 0f;

            foreach (var cat in catOrder)
            {
                var headerOnPage = false;
                foreach (var entry in byCategory[cat])
                {
                    var headerCost = headerOnPage ? 0f : CatRowHeight + RowSpacing;
                    var rowCost    = QuestRowHeight + RowSpacing;

                    // Start a new page when this row (plus its header, if the category has not
                    // appeared on this page yet) no longer fits. Never emit an empty page.
                    if (used + headerCost + rowCost > capacity && current.Count > 0)
                    {
                        _pages.Add(current);
                        current      = new List<PageItem>();
                        used         = 0f;
                        headerOnPage = false;
                        headerCost   = CatRowHeight + RowSpacing;
                    }

                    if (!headerOnPage)
                    {
                        current.Add(new PageItem { Category = true, CategoryName = cat });
                        used += headerCost;
                        headerOnPage = true;
                    }

                    current.Add(new PageItem
                    {
                        Category = false,
                        Entry    = entry,
                        Hidden   = HiddenQuestState.IsHidden(player, entry.Id),
                    });
                    used += rowCost;
                }
            }

            if (current.Count > 0) _pages.Add(current);
        }

        /// Instantiate the rows for `_page`.
        private void RenderPage(Player player)
        {
            if (_pages.Count == 0) return;

            // Build the rows with the container inactive so each TextMeshProUGUI's Awake (which
            // logs "LiberationSans SDF Font Asset was not found" when m_fontAsset is null) is
            // deferred until after ApplyFont has assigned the vanilla font. Reactivated below.
            _leftContent.gameObject.SetActive(false);

            foreach (var item in _pages[_page])
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
            catTmp.fontSize           = 11f;
            catTmp.color              = ColGold;
            catTmp.alignment          = TextAlignmentOptions.Left;
            catTmp.enableWordWrapping = false;
            catTmp.overflowMode       = TextOverflowModes.Overflow;
            catTmp.raycastTarget      = false;
        }

        private void BuildQuestRow(PageItem item, Player player)
        {
            var entry      = item.Entry;
            var complete   = IsEntryComplete(entry, player);
            var entryTitle = string.IsNullOrEmpty(entry.Title) ? entry.Id : Template(entry.Title, entry);

            var rowGo = new GameObject("VSG_Row_" + entry.Id);
            rowGo.transform.SetParent(_leftContent, false);
            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = new Color(0f, 0f, 0f, 0f);
            var rowLe = rowGo.AddComponent<LayoutElement>();
            // minHeight matters as much as preferredHeight: a VerticalLayoutGroup that runs out
            // of room shrinks children down to their MIN, and an unset min (0) let quest rows
            // collapse to invisible slivers while the category headers (min 22) held their size.
            // Pagination keeps the page under capacity; these pin the row size regardless.
            rowLe.minHeight       = QuestRowHeight;
            rowLe.preferredHeight = QuestRowHeight;

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
            labelRect.offsetMax = Vector2.zero;
            var labelTmp = labelGo.AddComponent<TextMeshProUGUI>();
            ApplyFont(labelTmp);
            var prefix = item.Hidden ? "[-] " : complete ? "[+] " : "  ";
            labelTmp.text               = prefix + entryTitle;
            labelTmp.fontStyle          = item.Hidden ? FontStyles.Italic : FontStyles.Normal;
            labelTmp.fontSize           = 12f;
            labelTmp.color              = item.Hidden ? ColLocked : complete ? ColGreen : ColText;
            labelTmp.alignment          = TextAlignmentOptions.Left;
            labelTmp.enableWordWrapping = false;
            labelTmp.overflowMode       = TextOverflowModes.Ellipsis;
            labelTmp.raycastTarget      = false;

            _guideRows.Add((entry, rowGo, labelTmp));
        }

        // ── Paging + hidden-quest controls ────────────────────────────────────────────────────

        /// Refresh the footer: page counter, arrow dimming, and the hidden-quest toggle label.
        private void UpdateFooter(Player player)
        {
            var pageCount = _pages.Count;
            if (_pageLabelText != null)
                _pageLabelText.text = pageCount <= 0
                    ? "no guides"
                    : "Page " + (_page + 1) + " / " + pageCount;

            // Arrows stay clickable but grey out when they would do nothing, so the control
            // never appears to be broken.
            if (_prevPageText != null) _prevPageText.color = _page > 0 ? ColGold : ColLocked;
            if (_nextPageText != null) _nextPageText.color = _page < pageCount - 1 ? ColGold : ColLocked;

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

        private void ChangePage(int delta)
        {
            if (_pages.Count <= 1) return;
            var next = _page + delta;
            if (next < 0 || next >= _pages.Count) return;
            _page = next;

            var player = Player.m_localPlayer;
            ClearChildren(_leftContent);
            _guideRows.Clear();
            RenderPage(player);
            UpdateFooter(player);

            // Keep the detail pane in sync: if the selected quest is not on this page, move the
            // selection to the first quest that is.
            var stillListed = false;
            foreach (var (entry, _, _) in _guideRows)
                if (entry == _selected) { stillListed = true; break; }
            if (!stillListed && _guideRows.Count > 0) SelectGuide(_guideRows[0].entry);
            else HighlightRow(_selected);
        }

        private void ToggleShowHidden()
        {
            _showHidden = !_showHidden;
            // The listed set changes size, so page indices no longer mean the same thing.
            _page = 0;
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
            {
                var img = row.GetComponent<Image>();
                if (img != null)
                    img.color = entry == selected ? ColRowSel : new Color(0f, 0f, 0f, 0f);
            }
        }

        private void ShowEntry(GuidanceEntry entry, Player player)
        {
            if (entry == null || player == null)
            {
                if (_titleText != null) _titleText.text = "Select a guide";
                if (_badgeText != null) _badgeText.text = "";
                if (_bodyText  != null) _bodyText.text  = "";
                UpdateTrackToggle(null, null, false);
                ClearUpcoming();
                return;
            }

            var title    = string.IsNullOrEmpty(entry.Title) ? entry.Id : Template(entry.Title, entry);
            var complete = IsEntryComplete(entry, player);

            UpdateTrackToggle(entry, player, complete);

            _titleText.text = title;

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
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyContentRect);

            PopulateUpcoming(entry, player);
        }

        // ── Tracker pin toggle ────────────────────────────────────────────────────────────────

        /// Show/refresh the "Show on Tracker" pill for the selected entry. Hidden for finished
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
                _trackToggleText.text  = on ? "[x] Pinned to Tracker  (click to unpin)"
                                            : "[ ] Show on Tracker  (click to pin)";
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

            // Build inactive so each TMP's null-font Awake warning is deferred until after the
            // font is assigned (see note in PopulatePanel). Reactivated below.
            _upcomingContent.gameObject.SetActive(false);

            // Steps after the current one are "upcoming / locked".
            for (var i = currentStep + 1; i < entry.Steps.Count; i++)
            {
                var step   = entry.Steps[i];
                var label  = "  Step " + (i + 1) + ": " + TruncateStepTitle(StepMessage(entry, step)) + "   (locked)";

                var rowGo = new GameObject("VSG_UpRow");
                rowGo.transform.SetParent(_upcomingContent, false);
                var rowLe = rowGo.AddComponent<LayoutElement>();
                // Pin the min too — a long chain overflows the 155px section and would otherwise
                // compress every step row to nothing (same failure as the guide list).
                rowLe.minHeight       = 18f;
                rowLe.preferredHeight = 18f;
                var rowTmp = rowGo.AddComponent<TextMeshProUGUI>();
                ApplyFont(rowTmp);
                rowTmp.text               = label;
                rowTmp.fontStyle          = FontStyles.Italic;
                rowTmp.fontSize           = 12f;
                rowTmp.color              = ColLocked;
                rowTmp.alignment          = TextAlignmentOptions.Left;
                rowTmp.enableWordWrapping = false;
                rowTmp.overflowMode       = TextOverflowModes.Ellipsis;
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

        private static string TruncateStepTitle(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(step)";
            return text.Length > 40 ? text.Substring(0, 37) + "..." : text;
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

        /// Resolve the vanilla font (once) and push it to all existing panel texts. Called from
        /// Open() while the root is still inactive so the static texts' Awake doesn't warn.
        private void EnsureFont()
        {
            if (_font == null)
            {
                _font = (GuidanceHudTracker.Instance != null
                            ? GuidanceHudTracker.Instance.ResolvedFont : null)
                        ?? GuidanceHudTracker.FindVanillaFontStatic();
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
            _leftContent      = null;
            _upcomingScroll   = null;
            _upcomingContent  = null;
            _titleText        = null;
            _badgeText        = null;
            _bodyText         = null;
            _bodyContentRect  = null;
            _trackToggleGo    = null;
            _trackToggleText  = null;
            _hideToggleGo     = null;
            _hideToggleText   = null;
            _pageLabelText    = null;
            _prevPageText     = null;
            _nextPageText     = null;
            _showHiddenText   = null;
            _guideRows.Clear();
            _pages.Clear();
            _page = 0;
            _selected = null;
            // Drop the cached font so it is re-resolved against the current scene — a null font
            // captured at build time is the usual cause of invisible rows.
            _font = null;

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
