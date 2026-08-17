using HarmonyLib;

namespace ValheimServerGuide.Display
{
    /// Canvas `sortingOrder` for every VSG screen-space surface, in one place.
    ///
    /// Valheim sets its UI sorting in scene data rather than in code, and some HUD elements —
    /// the health/stamina bars and the crosshair among them — sit on canvases that outrank a
    /// "comfortably above the inventory" value like 1000, so they painted over the conversation
    /// panel. Rather than chase vanilla's numbers, every VSG surface lives at the top of Unity's
    /// sortingOrder range (it is serialised as a short, so 32767 is the ceiling); only the order
    /// *among ourselves* carries meaning.
    ///
    /// Consequence worth knowing: these panels also draw over the pause menu and the loading
    /// screen. Each surface closes itself in those situations instead of relying on sorting.
    internal static class UiLayers
    {
        /// Persistent HUD-adjacent quest tracker — sits under every VSG panel.
        public const int Tracker = 32700;

        /// NPC dialogue panel.
        public const int Conversation = 32710;

        /// Rune reading. Can be opened from a conversation, so it must cover one.
        public const int Rune = 32720;

        /// Guide Codex — covers everything the player can open from the HUD.
        public const int Codex = 32730;

        /// Intro cinematic blackout. Above all of the above by design.
        public const int Intro = 32760;
    }

    /// True while a VSG surface is covering the middle of the screen. Used to suppress the
    /// vanilla crosshair, which is drawn dead-centre and reads as a smudge over panel text.
    internal static class ModalSurface
    {
        public static bool CoversScreen =>
            GuidanceDisplay.IntroLockActive
            || (GuidanceCodex.Instance != null && GuidanceCodex.Instance.IsOpen)
            || NpcConversationPanel.IsOpen
            || (RunePanel.Instance != null && RunePanel.Instance.IsOpen);
    }

    /// Hides the vanilla crosshair (and the hover name that shares its spot) while a VSG panel is
    /// up. Raising our canvases above vanilla's already stops the crosshair drawing *over* a
    /// panel, but it still shows through the translucent ones — and the player is using a free
    /// cursor at that point, so a centre-screen aiming reticle is just noise.
    ///
    /// Deactivating is safe to do every frame and needs no restore: vanilla's own
    /// `UpdateCrosshair` re-activates the crosshair as soon as this stops firing.
    [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
    internal static class HudCrosshairModalPatch
    {
        private static void Postfix(Hud __instance)
        {
            if (!ModalSurface.CoversScreen) return;
            if (__instance.m_crosshair != null && __instance.m_crosshair.gameObject.activeSelf)
                __instance.m_crosshair.gameObject.SetActive(false);
            if (__instance.m_hoverName != null) __instance.m_hoverName.text = "";
        }
    }
}
