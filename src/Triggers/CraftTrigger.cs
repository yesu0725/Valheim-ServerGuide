using HarmonyLib;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player finishes crafting a recipe.
    /// InventoryGui.DoCrafting is the method invoked once the craft timer completes.
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
    internal static class CraftTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(InventoryGui __instance, Player player)
        {
            if (player != Player.m_localPlayer)
            {
                Plugin.Log.LogDebug("[craft] postfix fired for non-local player; ignoring.");
                return;
            }

            var recipe = __instance.m_craftRecipe;
            var itemPrefab = recipe?.m_item?.gameObject;
            if (itemPrefab == null)
            {
                Plugin.Log.LogWarning("[craft] DoCrafting completed but m_craftRecipe/m_item was null.");
                return;
            }

            var subject = itemPrefab.name;
            Plugin.Log.LogInfo($"[craft] subject='{subject}' (display='{recipe.m_item.m_itemData?.m_shared?.m_name}')");

            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "craft",
                Subject = subject,
                DisplayName = recipe.m_item.m_itemData?.m_shared?.m_name,
            });
        }
    }
}
