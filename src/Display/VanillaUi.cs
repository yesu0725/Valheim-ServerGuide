using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimServerGuide.Display
{
    /// The vanilla player-inventory window's own look — sprites, font and colours — read out of
    /// the running game so a VSG panel can be drawn in Valheim's style without shipping a single
    /// asset (CRIT-14).
    ///
    /// Nothing here comes from a bundle. Each sprite is matched by its vanilla name against the
    /// `Image`s the live `InventoryGui` hierarchy already draws with, which also hands us the exact
    /// settings vanilla renders it with (`type`, material, pixels-per-unit) — so a panel styled
    /// from here is the same picture as the window it was copied from rather than an approximation.
    /// A sprite that is loaded but not currently drawn by the inventory is found among the loaded
    /// `Sprite` objects instead, and anything that cannot be found at all leaves the caller on its
    /// flat-colour fallback.
    internal static class VanillaUi
    {
        // ── Palette ───────────────────────────────────────────────────────────────────────────
        // Valheim's UI colours: warm orange headings and parchment body text over carved wood.

        /// Panel/window headings.
        internal static readonly Color Orange = new Color(1.00f, 0.63f, 0.24f);
        /// Body and label text.
        internal static readonly Color Beige = new Color(0.93f, 0.86f, 0.74f);
        /// Locked / disabled / hidden text.
        internal static readonly Color Dim = new Color(0.56f, 0.51f, 0.44f);
        /// Completed quests and "on" toggles.
        internal static readonly Color Green = new Color(0.55f, 0.87f, 0.45f);

        // Flat fills used only when the matching sprite could not be resolved. The panel still
        // reads as a Valheim panel, it just loses the carving.
        internal static readonly Color PanelFill  = new Color(0.09f, 0.07f, 0.05f, 0.96f);
        internal static readonly Color InsetFill  = new Color(0.04f, 0.035f, 0.03f, 0.90f);
        internal static readonly Color ButtonFill = new Color(0.20f, 0.16f, 0.10f, 0.95f);
        internal static readonly Color DividerCol = new Color(0.42f, 0.34f, 0.22f, 1.00f);
        internal static readonly Color RowSelFill = new Color(0.26f, 0.20f, 0.11f, 0.90f);

        // ── Resolved vanilla art ──────────────────────────────────────────────────────────────

        /// One sprite plus the `Image` settings vanilla draws it with.
        internal struct SpriteStyle
        {
            public Sprite Sprite;
            public Image.Type Type;
            public Material Material;
            public float PixelsPerUnit;

            public bool Valid => Sprite != null;

            /// True when the sprite carries 9-slice borders, i.e. it can be drawn at any size
            /// without distorting its carved edges. Valheim's per-window panel art (e.g.
            /// `woodpanel_playerinventory`) has none, so callers size themselves to its aspect.
            public bool Scalable => Sprite != null && Sprite.border != Vector4.zero;

            /// Paint `img` with this sprite, tinted. Falls back to a plain `tint` fill when the
            /// sprite was never resolved, so every caller has one code path.
            public void Apply(Image img, Color tint)
            {
                if (img == null) return;
                if (Sprite == null)
                {
                    img.sprite = null;
                    img.type   = Image.Type.Simple;
                    img.color  = tint;
                    return;
                }
                img.sprite = Sprite;
                img.type   = Type;
                if (Material != null) img.material = Material;
                if (PixelsPerUnit > 0f) img.pixelsPerUnitMultiplier = PixelsPerUnit;
                img.color = tint;
            }
        }

        /// `woodpanel_playerinventory` — the frame the player inventory itself is drawn in.
        internal static SpriteStyle Panel;
        /// Darker interior box: the inset a list or a description sits in.
        internal static SpriteStyle Inset;
        /// Standard vanilla button, with its hover/press/disabled swaps.
        internal static SpriteStyle Button;
        internal static Sprite ButtonHighlight;
        internal static Sprite ButtonPressed;
        internal static Sprite ButtonDisabled;

        /// The font the inventory window itself draws with.
        internal static TMP_FontAsset Font;

        private static bool _resolved;
        /// The inventory this art was read from. Every cached sprite and font belongs to that GUI
        /// scene, so a different instance (a new session) means the cache is stale and everything
        /// has to be read again — a sprite from a destroyed scene would otherwise sit here reading
        /// as "found but null" forever.
        private static InventoryGui _resolvedFor;

        /// True once the inventory UI has been found and its art cached. Callers that build their
        /// UI before the GUI scene is up simply re-apply after this flips.
        internal static bool Resolved => _resolved;

        // ── Resolution ────────────────────────────────────────────────────────────────────────

        /// Resolve every sprite and the font from the live inventory UI, once per GUI scene.
        /// Returns false while `InventoryGui.instance` does not exist yet — the caller should try
        /// again later.
        internal static bool TryResolve()
        {
            var gui = InventoryGui.instance;
            if (gui == null) return false;
            if (_resolved && gui == _resolvedFor) return true;
            Reset();
            _resolvedFor = gui;

            var images = gui.transform.GetComponentsInChildren<Image>(includeInactive: true);

            // Preference order per slot: the exact vanilla asset first, then near-identical
            // siblings, so a renamed or removed sprite degrades to the same visual family instead
            // of to a flat box.
            Panel = FindStyle(images, "woodpanel_playerinventory", "woodpanel_container",
                "woodpanel_trophys", "woodpanel_settings", "woodpanel_512x512", "woodpanel_large",
                "woodpanel_highres");
            Inset = FindStyle(images, "panel_interior_bkg_128", "panel_bkg_128", "panel_bkg",
                "item_background");
            Button = FindStyle(images, "button", "button_small");

            ButtonHighlight = FindSprite("button_highlight") ?? FindSprite("button_small_highlight");
            ButtonPressed   = FindSprite("button_pressed")   ?? FindSprite("button_small_pressed");
            ButtonDisabled  = FindSprite("button_disabled")  ?? FindSprite("button_small_disabled");

            Font = FindInventoryFont(gui.transform);

            // A completed pass counts as resolved even when nothing matched: every lookup is keyed
            // to this InventoryGui instance, so re-running it (the loose-sprite fallback walks every
            // sprite in memory) would find exactly the same nothing, once per Open().
            _resolved = true;
            if (Panel.Valid || Inset.Valid || Button.Valid)
                Plugin.Log.LogInfo("[vanilla-ui] panel=" + Name(Panel) + " inset=" + Name(Inset)
                    + " button=" + Name(Button)
                    + " font=" + (Font != null ? Font.name : "(none)"));
            else
                Plugin.Log.LogWarning("[vanilla-ui] no vanilla panel sprites found — VSG panels stay"
                    + " on their flat fills. font=" + (Font != null ? Font.name : "(none)"));
            return true;
        }

        private static string Name(SpriteStyle s)
            => s.Sprite != null ? s.Sprite.name + (s.Scalable ? "" : " (fixed-aspect)") : "(none)";

        /// Paint the interior box a list or a block of text sits in.
        ///
        /// Only a 9-sliced interior sprite is used: carrying slice borders is what makes a sprite a
        /// stretchable panel background in the first place, whereas bespoke art would be stretched
        /// to proportions (and a brightness) nobody authored it for. The flat dark fill it falls
        /// back to is what the vanilla trophy and skills windows show inside their frames anyway.
        internal static void ApplyInset(Image img)
        {
            if (Inset.Scalable) { Inset.Apply(img, Color.white); return; }
            if (img == null) return;
            img.sprite = null;
            img.type   = Image.Type.Simple;
            img.color  = InsetFill;
        }

        /// Style a button with the vanilla button sprite and vanilla's own hover/press swaps.
        /// Leaves the button on its flat fill (and no transition) when the sprite is missing.
        internal static void StyleButton(Button button, Image image)
        {
            if (button == null || image == null) return;
            button.targetGraphic = image;

            if (!Button.Valid)
            {
                image.sprite     = null;
                image.color      = ButtonFill;
                button.transition = Selectable.Transition.None;
                return;
            }

            Button.Apply(image, Color.white);
            button.transition  = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = ButtonHighlight,
                pressedSprite     = ButtonPressed,
                selectedSprite    = ButtonHighlight,
                disabledSprite    = ButtonDisabled,
            };
        }

        // ── Lookup helpers ────────────────────────────────────────────────────────────────────

        /// First of `names` that is drawn somewhere in the inventory hierarchy (which also gives us
        /// vanilla's draw settings for it), else the first that is merely loaded, else nothing.
        private static SpriteStyle FindStyle(Image[] images, params string[] names)
        {
            foreach (var name in names)
            {
                foreach (var img in images)
                {
                    var sp = img != null ? img.sprite : null;
                    if (sp == null) continue;
                    if (!string.Equals(sp.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                    return new SpriteStyle
                    {
                        Sprite        = sp,
                        Type          = img.type,
                        Material      = img.material,
                        PixelsPerUnit = img.pixelsPerUnitMultiplier,
                    };
                }

                var loose = FindSprite(name);
                if (loose != null)
                    return new SpriteStyle
                    {
                        Sprite = loose,
                        // Nothing told us how vanilla draws it, so slice it when it has borders
                        // and leave it alone when it does not.
                        Type          = loose.border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced,
                        PixelsPerUnit = 1f,
                    };
            }
            return default;
        }

        /// Loaded sprites, cached for the duration of one TryResolve pass — the scan walks every
        /// sprite the game has in memory, which is not something to repeat per lookup.
        private static Sprite[] _allSprites;

        private static Sprite FindSprite(string name)
        {
            if (_allSprites == null) _allSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var sp in _allSprites)
                if (sp != null && string.Equals(sp.name, name, StringComparison.OrdinalIgnoreCase))
                    return sp;
            return null;
        }

        /// The font the inventory draws with, decided by majority vote over its own labels rather
        /// than by name — if the game changes fonts, this follows. Outline variants are skipped
        /// unless they are the only thing there: they exist for text over the world, and their
        /// outline material reads as a heavy smear inside a panel.
        private static TMP_FontAsset FindInventoryFont(Transform root)
        {
            var counts = new Dictionary<TMP_FontAsset, int>();
            TMP_FontAsset outlineFallback = null;

            foreach (var t in root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                var f = t != null ? t.font : null;
                if (f == null) continue;
                if (f.name.IndexOf("outline", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (outlineFallback == null) outlineFallback = f;
                    continue;
                }
                counts.TryGetValue(f, out var n);
                counts[f] = n + 1;
            }

            TMP_FontAsset best = null;
            var bestCount = 0;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }

            return best ?? outlineFallback ?? GuidanceHudTracker.FindVanillaFontStatic();
        }

        /// Drop the cached art so the next TryResolve re-reads it from the scene.
        internal static void Reset()
        {
            _resolved       = false;
            _resolvedFor    = null;
            _allSprites     = null;
            Panel           = default;
            Inset           = default;
            Button          = default;
            ButtonHighlight = null;
            ButtonPressed   = null;
            ButtonDisabled  = null;
            Font            = null;
        }
    }
}
