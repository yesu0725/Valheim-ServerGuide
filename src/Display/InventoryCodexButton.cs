using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimServerGuide.Display
{
    /// A "Quests" button on the player inventory screen that opens the Guide Codex.
    ///
    /// WHY THIS EXISTS. The codex was reachable only by hotkey (F3 by default). A key
    /// nobody told you about is not discoverable — a player who never reads the config
    /// or the wiki has no way to find out the codex exists. The inventory screen is
    /// where a player already goes looking for things, so that is where the entry point
    /// belongs.
    ///
    /// BUILT FROM VANILLA (CRIT-14). The button is a CLONE of the inventory's own
    /// "Take All" button, so it inherits vanilla styling with no authored assets.
    ///
    /// WHERE IT SITS. Directly beneath Lost Scrolls II's "Tournaments" button, which is
    /// the middle entry of that mod's inventory menu bar. The position is read off the
    /// live `LSII_TournamentButton` rect on every Show rather than recomputed from
    /// constants, so this button follows that row wherever the operator has nudged it
    /// with `Interface/MenuBarOffset`. With Lost Scrolls II absent, the same geometry is
    /// reproduced from the constants below — that row is centred, so "under Tournaments"
    /// degrades to "centred, one row down" and the button stays exactly where a player
    /// running both mods would have learned to look for it.
    ///
    /// MOVABLE, AND OPTIONAL. A player can click-and-drag the button anywhere on the inventory
    /// screen (QuestsButtonDrag); where they leave it is saved to the client config
    /// (`Codex → InventoryButtonPosition`) and survives relogging. `InventoryButtonEnabled =
    /// false` hides it — the Codex hotkey is unaffected.
    ///
    /// REBUILT PER WORLD LOAD. InventoryGui is destroyed and rebuilt on every world load,
    /// taking our clone with it. That is why the button is re-validated on every Show
    /// rather than created once and cached.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
    internal static class InventoryCodexButton
    {
        private const string ButtonName = "VSG_QuestsButton";
        private const string Label      = "Quests";

        /// The Lost Scrolls II menu-bar button this one hangs under, and the geometry of
        /// that row. Matched to `LostScrollsII.Companions.InventoryMenuBar` — the sizes
        /// must agree or the two rows will not line up.
        private const string AnchorButtonName = "LSII_TournamentButton";
        private const float TopEdgeInset = -18f;
        private const float ButtonWidth  = 132f;
        private const float ButtonHeight = 32f;
        private const float ButtonGap    = 6f;

        private static void Postfix(InventoryGui __instance)
        {
            try { Ensure(__instance); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[codex] inventory Quests button failed: {e.Message}");
            }
        }

        /// Re-apply visibility and position now — the settings changed while the inventory
        /// may be on screen, and waiting for the next Show would leave it stale.
        internal static void Refresh()
        {
            if (_saving) return; // our own write echoing back through SettingChanged
            try { Ensure(InventoryGui.instance); }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[codex] inventory Quests button refresh failed: {e.Message}");
            }
        }

        private static void Ensure(InventoryGui gui)
        {
            if (gui == null || gui.m_player == null) return;

            // Parented to the screen root (the container the inventory panels sit in)
            // rather than to any one panel, so "centre" means the middle of the screen —
            // the same parent Lost Scrolls II's row uses, which is what lets us read its
            // anchoredPosition directly.
            var parent = (gui.m_player.parent as RectTransform) ?? gui.m_player as RectTransform;
            if (parent == null) return;

            var existing = parent.Find(ButtonName) as RectTransform;
            var show = (Plugin.CodexEnabled?.Value ?? true) && (Plugin.InventoryButtonEnabled?.Value ?? true);
            if (!show)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            if (existing == null)
            {
                var source = gui.m_takeAllButton;
                if (source == null) return;
                existing = Build(source, parent);
                if (existing == null) return;
                Plugin.Log.LogInfo($"[codex] inventory '{Label}' button built under '{parent.name}'.");
            }

            // Re-applied every Show, not just on build: our Postfix and Lost Scrolls II's
            // run in an undefined order, so on the first Show of a session the Tournaments
            // button may not exist yet and we would otherwise be stuck at the fallback
            // position for the rest of the world load.
            existing.gameObject.SetActive(true);
            var drag = existing.GetComponent<QuestsButtonDrag>();
            if (drag != null && drag.Dragging) return; // never yank it out from under the cursor
            existing.anchoredPosition = TryLoadPosition(out var saved)
                ? Clamp(existing, saved)
                : ResolvePosition(parent);
        }

        /// One row below the Tournaments button when it is there, at the same x. Both
        /// rects are anchored and pivoted top-centre, so y is the top edge and "below"
        /// is simply a subtraction.
        private static Vector2 ResolvePosition(RectTransform parent)
        {
            var anchor = parent.Find(AnchorButtonName) as RectTransform;
            if (anchor == null)
                return new Vector2(0f, TopEdgeInset - ButtonHeight - ButtonGap);

            var pos = anchor.anchoredPosition;
            return new Vector2(pos.x, pos.y - anchor.rect.height - ButtonGap);
        }

        private static RectTransform Build(Button source, RectTransform parent)
        {
            var clone = Object.Instantiate(source.gameObject, parent);
            clone.name = ButtonName;

            // Opt out of layout. This container drives a layout group, which would
            // otherwise override anchoredPosition every frame — the position has to be
            // ours deliberately, not a side effect of that group happening to centre things.
            var layoutElement = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var rt = clone.GetComponent<RectTransform>();
            if (rt == null)
            {
                Object.Destroy(clone);
                return null;
            }

            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot      = new Vector2(0.5f, 1f);
            rt.sizeDelta  = new Vector2(ButtonWidth, ButtonHeight);
            rt.localScale = Vector3.one;

            var txt = clone.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.text = Label;
                // Autosizing on a cloned vanilla button fits the largest one-line size
                // rather than wrapping, which makes a short label balloon out of the row.
                txt.enableAutoSizing = false;
                txt.fontSize = 14f;
            }

            var btn = clone.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    // The release that ends a drag is also a click on the button; a move is not
                    // a request to open the Codex.
                    var dragger = clone.GetComponent<QuestsButtonDrag>();
                    if (dragger != null && dragger.SuppressClick) return;
                    // Close the inventory first: the codex is a full-screen panel that
                    // takes the cursor, and leaving the inventory open behind it makes
                    // cursor ownership ambiguous. Open() runs after Hide() so the codex's
                    // own cursor/camera handling is what sticks.
                    if (InventoryGui.instance != null) InventoryGui.instance.Hide();
                    GuidanceCodex.Instance?.Open();
                });
                btn.interactable = true;
            }

            clone.AddComponent<QuestsButtonDrag>();
            clone.SetActive(true);
            return rt;
        }

        // ── Saved position ────────────────────────────────────────────────────────────────────

        /// The player's dragged position, "x,y" in the parent's top-centre anchored space.
        /// Stored in the client config, not the server-side progress file: it is a per-machine
        /// layout preference that should follow the player across characters and servers, and
        /// it has to work on a server that does not run VSG at all.
        private static bool TryLoadPosition(out Vector2 pos)
        {
            pos = default;
            var raw = Plugin.InventoryButtonPosition?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var parts = raw.Split(',');
            if (parts.Length != 2) return false;
            const System.Globalization.NumberStyles Num = System.Globalization.NumberStyles.Float;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0].Trim(), Num, inv, out var x)) return false;
            if (!float.TryParse(parts[1].Trim(), Num, inv, out var y)) return false;
            pos = new Vector2(x, y);
            return true;
        }

        private static bool _saving;

        internal static void SavePosition(Vector2 pos)
        {
            if (Plugin.InventoryButtonPosition == null) return;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _saving = true;
            try { Plugin.InventoryButtonPosition.Value = pos.x.ToString("0.#", inv) + "," + pos.y.ToString("0.#", inv); }
            finally { _saving = false; }
            Plugin.Log.LogInfo($"[codex] inventory '{Label}' button moved to ({Plugin.InventoryButtonPosition.Value}).");
        }

        /// Keep the whole button inside its parent (the inventory screen), so a position saved
        /// at a bigger resolution or GUI scale can never strand it off-screen. Anchor and pivot
        /// are both top-centre: x runs either side of the centre line, y downward from the top.
        internal static Vector2 Clamp(RectTransform rt, Vector2 pos)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return pos;
            var area = parent.rect;
            var size = rt.rect.size;
            var halfX = Mathf.Max(0f, (area.width - size.x) * 0.5f);
            var maxDown = Mathf.Max(0f, area.height - size.y);
            return new Vector2(Mathf.Clamp(pos.x, -halfX, halfX), Mathf.Clamp(pos.y, -maxDown, 0f));
        }
    }

    /// Click-and-drag for the inventory Quests button.
    ///
    /// Polls the mouse rather than implementing the EventSystem drag interfaces — the same
    /// approach the HUD tracker's drag uses (GuidanceHudTracker.UpdateDrag), which is proven
    /// against Valheim's own input module. A press only becomes a drag once the cursor has
    /// travelled DragThresholdPx, so an ordinary click with a little hand jitter still opens the
    /// Codex; once it IS a drag, the click that the release produces is swallowed.
    internal sealed class QuestsButtonDrag : MonoBehaviour
    {
        private const float DragThresholdPx = 8f;

        private RectTransform _rt;
        private bool _pressed;
        private Vector2 _pressScreen;
        private Vector2 _pressLocal;
        private Vector2 _startPos;

        internal bool Dragging { get; private set; }
        /// True from the moment a press turns into a drag until the next press, so the click
        /// that ends the drag — whichever side of this Update the EventSystem delivers it on —
        /// is ignored.
        internal bool SuppressClick { get; private set; }

        private void Awake() => _rt = (RectTransform)transform;

        private void Update()
        {
            if (!_pressed)
            {
                if (!Input.GetMouseButtonDown(0)) return;
                SuppressClick = false;
                if (!InventoryGui.IsVisible()) return;
                var cam = EventCamera();
                if (!RectTransformUtility.RectangleContainsScreenPoint(_rt, Input.mousePosition, cam)) return;
                if (!ToParentLocal(Input.mousePosition, cam, out _pressLocal)) return;
                _pressed     = true;
                _pressScreen = Input.mousePosition;
                _startPos    = _rt.anchoredPosition;
                return;
            }

            if (Input.GetMouseButton(0))
            {
                if (!Dragging)
                {
                    if (((Vector2)Input.mousePosition - _pressScreen).magnitude < DragThresholdPx) return;
                    Dragging      = true;
                    SuppressClick = true;
                }
                // Parent-local space, not raw pixels: the delta is then right at any GUI scale.
                if (ToParentLocal(Input.mousePosition, EventCamera(), out var local))
                    _rt.anchoredPosition = InventoryCodexButton.Clamp(_rt, _startPos + (local - _pressLocal));
                return;
            }

            _pressed = false;
            if (!Dragging) return;
            Dragging = false;
            InventoryCodexButton.SavePosition(_rt.anchoredPosition);
        }

        /// The inventory closing mid-drag (Tab, Esc) still has to save where the button got to.
        private void OnDisable()
        {
            var wasDragging = Dragging;
            _pressed = false;
            Dragging = false;
            if (wasDragging) InventoryCodexButton.SavePosition(_rt.anchoredPosition);
        }

        private bool ToParentLocal(Vector2 screen, Camera cam, out Vector2 local)
        {
            local = default;
            var parent = _rt.parent as RectTransform;
            return parent != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out local);
        }

        /// Null for a screen-space-overlay canvas (what Valheim's HUD uses); the canvas camera
        /// otherwise, which the rect maths needs to be right.
        private Camera EventCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            canvas = canvas.rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }
    }
}
