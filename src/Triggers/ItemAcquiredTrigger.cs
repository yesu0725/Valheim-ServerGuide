using HarmonyLib;
using UnityEngine;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player picks up an item.
    /// YAML `trigger.item` supports a trailing wildcard: `"Trophy_*"` matches any trophy.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class ItemAcquiredTrigger
    {
        [HarmonyPostfix]
        private static void Postfix(Humanoid __instance, GameObject go)
        {
            if (__instance != Player.m_localPlayer) return;
            if (go == null) return;

            var subject = TriggerUtils.NormalizePrefabName(go.name);
            if (string.IsNullOrEmpty(subject)) return;

            var drop = go.GetComponent<ItemDrop>();
            GuidanceDispatcher.Raise(new TriggerEvent
            {
                Type = "item_acquired",
                Subject = subject,
                DisplayName = drop?.m_itemData?.m_shared?.m_name,
            });
        }
    }
}
