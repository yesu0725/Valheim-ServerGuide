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

        private static void Ensure(InventoryGui gui)
        {
            if (!(Plugin.CodexEnabled?.Value ?? true)) return;
            if (gui == null || gui.m_player == null) return;

            var source = gui.m_takeAllButton;
            if (source == null) return;

            // Parented to the screen root (the container the inventory panels sit in)
            // rather than to any one panel, so "centre" means the middle of the screen —
            // the same parent Lost Scrolls II's row uses, which is what lets us read its
            // anchoredPosition directly.
            var parent = (gui.m_player.parent as RectTransform) ?? gui.m_player as RectTransform;
            if (parent == null) return;

            var existing = parent.Find(ButtonName) as RectTransform;
            if (existing == null)
            {
                existing = Build(source, parent);
                if (existing == null) return;
                Plugin.Log.LogInfo($"[codex] inventory '{Label}' button built under '{parent.name}'.");
            }

            // Re-applied every Show, not just on build: our Postfix and Lost Scrolls II's
            // run in an undefined order, so on the first Show of a session the Tournaments
            // button may not exist yet and we would otherwise be stuck at the fallback
            // position for the rest of the world load.
            existing.anchoredPosition = ResolvePosition(parent);
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
                    // Close the inventory first: the codex is a full-screen panel that
                    // takes the cursor, and leaving the inventory open behind it makes
                    // cursor ownership ambiguous. Open() runs after Hide() so the codex's
                    // own cursor/camera handling is what sticks.
                    if (InventoryGui.instance != null) InventoryGui.instance.Hide();
                    GuidanceCodex.Instance?.Open();
                });
                btn.interactable = true;
            }

            clone.SetActive(true);
            return rt;
        }
    }
}
