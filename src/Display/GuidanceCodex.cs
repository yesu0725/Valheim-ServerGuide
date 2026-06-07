using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimServerGuide.Config;
using ValheimServerGuide.State;

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

        // ── Right panel ───────────────────────────────────────────────────────────────────────
        private TMP_Text _titleText;
        private TMP_Text _badgeText;
        private TMP_Text _bodyText;
        private RectTransform _bodyContentRect;
        private Transform _upcomingContent;

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

            // Guide list container, filling the space below the header. A plain top-anchored
            // VerticalLayoutGroup (same pattern as the working header area) — no ScrollRect /
            // ContentSizeFitter, which previously produced zero-size rows. The list is short.
            var contentGo = new GameObject("VSG_LeftContent");
            contentGo.transform.SetParent(leftGo.transform, false);
            // Capture the RectTransform RETURNED by AddComponent. Adding a RectTransform to a GO
            // that has a plain Transform replaces the transform, so a reference cached from
            // `.transform` BEFORE this call would dangle (and `as RectTransform` would be null,
            // leaving the VerticalLayoutGroup unable to lay out children).
            var cRect = contentGo.AddComponent<RectTransform>();
            _leftContent = cRect;
            cRect.anchorMin = new Vector2(0f, 0f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.offsetMin = new Vector2(0f, 0f);
            cRect.offsetMax = new Vector2(0f, -26f); // below the 26px CATEGORIES header
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment         = TextAnchor.UpperLeft;
            vlg.spacing                = 1f;
            vlg.padding                = new RectOffset(4, 4, 4, 4);
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
            _titleText.enableWordWrapping = false;
            _titleText.overflowMode       = TextOverflowModes.Ellipsis;
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

            // ── Upcoming steps section (bottom 160px) ─────────────────────────────────────────
            BuildUpcomingSection(rightGo.transform);

            // ── Body text (scrollable, between title area and upcoming section) ────────────────
            BuildBodyScroll(rightGo.transform);
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
            scrollRect.offsetMax = new Vector2(-8f, -54f);
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

            // Content container for upcoming step rows.
            var contentGo = new GameObject("VSG_UpContent");
            contentGo.transform.SetParent(upGo.transform, false);
            // Capture the RETURNED RectTransform (see note in BuildLeftPanel) — caching
            // `.transform` before this call would leave the VLG without a usable RectTransform.
            var cRect = contentGo.AddComponent<RectTransform>();
            _upcomingContent = cRect;
            cRect.anchorMin = new Vector2(0f, 0f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.offsetMin = Vector2.zero;
            cRect.offsetMax = new Vector2(0f, -22f);
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childControlHeight     = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing                = 2f;
        }

        // ── Data population ───────────────────────────────────────────────────────────────────

        private void PopulatePanel()
        {
            EnsureFont();

            var player = Player.m_localPlayer;
            var config = Plugin.CurrentConfig;

            // Clear left panel.
            ClearChildren(_leftContent);
            _guideRows.Clear();
            _selected = null;

            if (config?.Guidances == null || player == null)
            {
                ShowEntry(null, null);
                return;
            }

            // Group visible guides by category, preserving YAML order.
            var catOrder = new List<string>();
            var byCategory = new Dictionary<string, List<GuidanceEntry>>();
            foreach (var entry in config.Guidances)
            {
                if (!IsVisible(entry, player)) continue;
                var cat = string.IsNullOrEmpty(entry.Category) ? "General" : entry.Category;
                if (!byCategory.ContainsKey(cat))
                {
                    byCategory[cat] = new List<GuidanceEntry>();
                    catOrder.Add(cat);
                }
                byCategory[cat].Add(entry);
            }

            // Build the rows with the container inactive so each TextMeshProUGUI's Awake (which
            // logs "LiberationSans SDF Font Asset was not found" when m_fontAsset is null) is
            // deferred until after ApplyFont has assigned the vanilla font. Reactivated below.
            _leftContent.gameObject.SetActive(false);

            foreach (var cat in catOrder)
            {
                // Category header row.
                var catGo = new GameObject("VSG_Cat_" + cat);
                catGo.transform.SetParent(_leftContent, false);
                var catLe = catGo.AddComponent<LayoutElement>();
                catLe.preferredHeight = 22f;
                var catTmp = catGo.AddComponent<TextMeshProUGUI>();
                ApplyFont(catTmp);
                catTmp.text          = cat.ToUpper();
                catTmp.fontStyle     = FontStyles.Bold;
                catTmp.fontSize      = 11f;
                catTmp.color         = ColGold;
                catTmp.alignment     = TextAlignmentOptions.Left;
                catTmp.raycastTarget = false;

                foreach (var entry in byCategory[cat])
                {
                    var complete   = IsEntryComplete(entry, player);
                    var entryTitle = string.IsNullOrEmpty(entry.Title) ? entry.Id : entry.Title;

                    var rowGo = new GameObject("VSG_Row_" + entry.Id);
                    rowGo.transform.SetParent(_leftContent, false);
                    var rowImg = rowGo.AddComponent<Image>();
                    rowImg.color = new Color(0f, 0f, 0f, 0f);
                    var rowLe = rowGo.AddComponent<LayoutElement>();
                    rowLe.preferredHeight = 20f;

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
                    labelTmp.text               = (complete ? "[+] " : "  ") + entryTitle;
                    labelTmp.fontStyle          = FontStyles.Normal;
                    labelTmp.fontSize           = 12f;
                    labelTmp.color              = complete ? ColGreen : ColText;
                    labelTmp.alignment          = TextAlignmentOptions.Left;
                    labelTmp.enableWordWrapping = false;
                    labelTmp.overflowMode       = TextOverflowModes.Ellipsis;
                    labelTmp.raycastTarget      = false;

                    _guideRows.Add((entry, rowGo, labelTmp));
                }
            }

            // Reactivate now that every row has its font — Awake fires here without warning.
            _leftContent.gameObject.SetActive(true);

            // Force the VLG/layout to compute row rects now so geometry is valid this frame.
            if (_leftContent is RectTransform lcRect)
                LayoutRebuilder.ForceRebuildLayoutImmediate(lcRect);

            // Auto-select first visible guide.
            if (_guideRows.Count > 0)
            {
                _selected = _guideRows[0].entry;
                HighlightRow(_selected);
                ShowEntry(_selected, player);
            }
            else
            {
                ShowEntry(null, null);
            }
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
                ClearUpcoming();
                return;
            }

            var title    = string.IsNullOrEmpty(entry.Title) ? entry.Id : entry.Title;
            var complete = IsEntryComplete(entry, player);

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
                // Non-chain entry. Multi-count item-submit entries show a collection counter;
                // everything else shows Complete or nothing.
                var goal = ItemSubmitGoal(entry);
                if (goal > 0 && !complete)
                {
                    var cur = SubmitState.Get(player, entry.Id);
                    _badgeText.text  = cur + " / " + goal;
                    _badgeText.color = ColText;
                }
                else
                {
                    _badgeText.text  = complete ? "[Complete]" : "";
                    _badgeText.color = ColGreen;
                }
            }

            // Body text: current step message (or last step for completed chains).
            if (entry.Steps != null && entry.Steps.Count > 0)
            {
                GuidanceStep displayStep;
                if (complete)
                {
                    displayStep = entry.Steps[entry.Steps.Count - 1];
                }
                else
                {
                    var idx = ChainState.GetStep(player, entry.Id);
                    displayStep = idx < entry.Steps.Count ? entry.Steps[idx] : null;
                }
                _bodyText.text = StepMessage(displayStep);
            }
            else
            {
                // Non-chain entry: show its message (item-submit entries get a progress line too).
                var body = !string.IsNullOrEmpty(entry.Message) ? entry.Message : entry.Display?.Text ?? "";
                var goal = ItemSubmitGoal(entry);
                if (goal > 0 && !complete)
                {
                    var cur = SubmitState.Get(player, entry.Id);
                    var item = entry.Trigger?.Item;
                    var label = string.IsNullOrEmpty(item) ? "items" : item;
                    body = $"Submitted {cur} / {goal} {label}." +
                           (string.IsNullOrEmpty(body) ? "" : "\n\n" + body);
                }
                _bodyText.text = body;
            }

            if (_bodyContentRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_bodyContentRect);

            PopulateUpcoming(entry, player);
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
                var label  = "  Step " + (i + 1) + ": " + TruncateStepTitle(StepMessage(step)) + "   (locked)";

                var rowGo = new GameObject("VSG_UpRow");
                rowGo.transform.SetParent(_upcomingContent, false);
                var rowLe = rowGo.AddComponent<LayoutElement>();
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
                Destroy(t.GetChild(i).gameObject);
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

            // Non-chain entry: visible once it has fired, OR while an item-submit goal is
            // actively in progress (collecting items toward a multi-count submission).
            if (HasItemSubmitProgress(entry, player)) return true;
            return SeenTracker.HasFired(player, entry.Id, entry.Scope ?? "player");
        }

        private static bool IsEntryComplete(GuidanceEntry entry, Player player)
        {
            if (entry.Steps != null && entry.Steps.Count > 0)
                return ChainState.IsComplete(player, entry.Id);
            return SeenTracker.HasFired(player, entry.Id, entry.Scope ?? "player");
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

        // ── Text helpers ──────────────────────────────────────────────────────────────────────

        private static string StepMessage(GuidanceStep step)
        {
            if (step == null) return "";
            if (!string.IsNullOrEmpty(step.Message)) return step.Message;
            return step.Display?.Text ?? "";
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
