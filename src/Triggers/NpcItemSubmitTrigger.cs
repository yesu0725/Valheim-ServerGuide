using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimServerGuide.Config;
using ValheimServerGuide.Display;
using ValheimServerGuide.State;

namespace ValheimServerGuide.Triggers
{
    /// Fires when the local player submits a hotbar item (keys 1-8) to a trader NPC.
    ///
    /// Patch target: Trader.UseItem(Humanoid user, ItemDrop.ItemData item) → bool — this is
    /// the Interactable hook the game calls when a hotbar item is used on a Trader. Vanilla:
    ///   • If the NPC has m_useItems (Hildir): matches by m_shared.m_name and ALWAYS returns
    ///     true — either accepts the quest item or says m_randomGiveItemNo ("I don't need that").
    ///   • If m_useItems is empty (Haldor, BogWitch): returns false → the caller then shows the
    ///     "$msg_cantuseon" ("You can't use X on Y") center message.
    ///
    /// Returning true from UseItem = "item consumed" → no "can't use" message. We use this to
    /// suppress vanilla cleanly via the prefix's __result.
    ///
    /// Priority:
    ///   1. Item is a vanilla quest item (in m_useItems, matched by token) → run vanilla
    ///      (Hildir's quest stays intact). Our trigger does NOT fire.
    ///   2. Item matches a configured npc_item_submit entry (specific, then catch-all) → fire
    ///      our trigger, consume (__result = true), suppress vanilla.
    ///   3. No configured match:
    ///        • Hildir (m_useItems > 0) → run vanilla (her own rejection line plays).
    ///        • Haldor/BogWitch with configured entries → consume silently (no ugly "can't use").
    ///        • Otherwise → run vanilla.
    [HarmonyPatch(typeof(Trader), nameof(Trader.UseItem))]
    internal static class NpcItemSubmitTrigger
    {
        [HarmonyPrefix]
        private static bool Prefix(Trader __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (item == null) return true;
            if (user == null || user != Player.m_localPlayer) return true;

            var npcSubject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (string.IsNullOrEmpty(npcSubject)) return true;

            var itemPrefabName = ResolveItemName(item);
            Plugin.Log.LogInfo($"[item_submit] '{user.GetType().Name}' used '{itemPrefabName}' " +
                               $"(token '{item.m_shared?.m_name}') on '{npcSubject}'.");

            // Rule 1: vanilla quest item (Hildir) — let vanilla handle completely.
            if (IsVanillaUseItem(__instance, item))
            {
                Plugin.Log.LogInfo($"[item_submit] '{itemPrefabName}' is a vanilla quest item — deferring to vanilla.");
                return true;
            }

            var player = user as Player;
            if (player == null) return true;

            // Rule 2: matches a configured npc_item_submit entry → handle submission, suppress vanilla.
            // FindEntry already picked exactly ONE entry (specific item match preferred over a
            // catch-all), so we fire that single entry via FireEntry — NOT GuidanceDispatcher.Raise,
            // which would also fire the catch-all entry on top (its empty trigger.item matches any).
            var entry = FindEntry(npcSubject, itemPrefabName, player);
            if (entry != null)
            {
                HandleSubmission(__instance, player, item, itemPrefabName, npcSubject, entry);
                __result = true;     // consumed → no "$msg_cantuseon"
                return false;        // skip vanilla
            }

            // Rule 3: no configured match.
            var hasVanillaUseItems = __instance.m_useItems != null && __instance.m_useItems.Count > 0;
            if (hasVanillaUseItems)
            {
                // Hildir — let vanilla play her own "I don't need that" rejection line.
                Plugin.Log.LogInfo($"[item_submit] no entry; NPC has vanilla useItems — deferring to vanilla rejection.");
                return true;
            }
            if (NpcHasConfiguredEntries(npcSubject))
            {
                // Haldor/BogWitch we "own" — consume silently so the ugly "$msg_cantuseon"
                // message does not appear. (A catch-all entry, if present, will have matched
                // in Rule 2 and shown a friendlier rejection.)
                Plugin.Log.LogInfo($"[item_submit] no entry; suppressing vanilla 'can't use' on owned NPC '{npcSubject}'.");
                __result = true;
                return false;
            }

            // Not our NPC at all — full vanilla path.
            return true;
        }

        // ── Submission handling ──────────────────────────────────────────────────────

        /// Processes one item submission for a matched entry. Handles the required count
        /// (trigger.count), partial-stack consumption (trigger.consume), and progress vs.
        /// completion: while the goal is not yet met it consumes what it can, advances the
        /// progress counter, and refreshes the tracker; on reaching the goal it clears progress
        /// and fires the entry's configured display/reward via FireEntry.
        private static void HandleSubmission(Trader trader, Player player, ItemDrop.ItemData item,
            string itemPrefabName, string npcSubject, GuidanceEntry entry)
        {
            var goal     = entry.Trigger.Count <= 0 ? 1 : entry.Trigger.Count;
            var consume  = entry.Trigger.Consume;
            var localized = Localized(item);

            var evt = new TriggerEvent
            {
                Type        = "npc_item_submit",
                Subject     = npcSubject,
                DisplayName = localized,
                Extra       = new Dictionary<string, object> { { "item", itemPrefabName } },
            };

            // ── Single-submission entry (count <= 1): original behavior + optional consume. ──
            if (goal <= 1)
            {
                if (consume) ConsumeItems(player, item, 1);
                Plugin.Log.LogInfo($"[item_submit] firing entry '{entry.Id}' (single) for '{itemPrefabName}' -> '{npcSubject}'.");
                GuidanceDispatcher.FireEntry(entry, evt);
                return;
            }

            // ── Multi-submission entry (count > 1): accumulate progress. ──
            var current   = SubmitState.Get(player, entry.Id);
            var remaining = goal - current;
            if (remaining <= 0) { remaining = goal; current = 0; } // safety: stale state

            // Consume only what is still required — never more than the submitted stack holds,
            // and never the whole stack if fewer are needed.
            var available = System.Math.Max(1, item.m_stack);
            var take      = System.Math.Min(remaining, available);
            if (consume) ConsumeItems(player, item, take);

            var newCount = current + take;
            Plugin.Log.LogInfo($"[item_submit] '{entry.Id}' progress {newCount}/{goal} " +
                               $"(+{take} {itemPrefabName}, consume={consume}).");

            if (newCount >= goal)
            {
                // Goal reached — clear progress and fire the entry's configured display/reward.
                SubmitState.Clear(player, entry.Id);
                Plugin.Log.LogInfo($"[item_submit] '{entry.Id}' complete ({goal}/{goal}) — firing.");
                GuidanceDispatcher.FireEntry(entry, evt);
                GuidanceHudTracker.Instance?.FlashCompletion(entry.Id);
            }
            else
            {
                // Still collecting — persist progress, show feedback, refresh the tracker.
                SubmitState.Set(player, entry.Id, newCount);
                var title = !string.IsNullOrEmpty(entry.Title) ? entry.Title : localized;
                player.Message(MessageHud.MessageType.Center,
                    $"{title}: {newCount}/{goal} {localized}");
                GuidanceHudTracker.Instance?.Refresh(fromProgress: true);
            }
        }

        /// Removes `amount` of the submitted item from the player's inventory and shows the
        /// vanilla "removed item" message. RemoveItem reduces a stack in place or removes the
        /// item entirely when amount == stack — so a larger stack keeps its remainder.
        private static void ConsumeItems(Player player, ItemDrop.ItemData item, int amount)
        {
            if (amount <= 0) return;
            var inv = player.GetInventory();
            if (inv == null) return;
            inv.RemoveItem(item, amount);
            player.ShowRemovedMessage(item, amount);
        }

        /// Localized display name for an item ("$item_wood" → "Wood").
        private static string Localized(ItemDrop.ItemData item)
        {
            var token = item.m_shared?.m_name ?? "";
            return Localization.instance != null ? Localization.instance.Localize(token) : token;
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

        /// Best-effort prefab name for a hotbar item ("Wood", "Stone", …), matching the
        /// convention used by craft / item_acquired triggers. Falls back to the shared name.
        private static string ResolveItemName(ItemDrop.ItemData item)
        {
            var n = item.m_dropPrefab?.name;
            if (!string.IsNullOrEmpty(n)) return TriggerUtils.NormalizePrefabName(n);
            return TriggerUtils.NormalizePrefabName(item.m_shared?.m_name ?? "");
        }

        /// True if the item matches any entry in the trader's vanilla m_useItems list, using
        /// the SAME comparison vanilla uses: item.m_shared.m_name == useItem.m_prefab.m_itemData.m_shared.m_name.
        /// Prevents overriding Hildir's quest-item submissions.
        private static bool IsVanillaUseItem(Trader trader, ItemDrop.ItemData item)
        {
            if (trader.m_useItems == null || trader.m_useItems.Count == 0) return false;
            var token = item.m_shared?.m_name;
            if (string.IsNullOrEmpty(token)) return false;

            foreach (var ui in trader.m_useItems)
            {
                var uiToken = ui?.m_prefab?.m_itemData?.m_shared?.m_name;
                if (!string.IsNullOrEmpty(uiToken) &&
                    string.Equals(token, uiToken, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// Finds the first eligible npc_item_submit entry for (npcSubject, itemPrefabName) whose
        /// gates pass. An entry with no trigger.item matches ANY item (catch-all). Specific
        /// item matches are preferred over catch-alls regardless of YAML order.
        internal static GuidanceEntry FindEntry(string npcSubject, string itemPrefabName, Player player)
        {
            if (string.IsNullOrEmpty(npcSubject) || player == null) return null;
            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return null;

            GuidanceEntry catchAll = null;
            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "npc_item_submit",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(entry.Trigger.Npc, npcSubject,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (!GuidanceDispatcher.CheckGates(entry, player)) continue;

                if (string.IsNullOrEmpty(entry.Trigger.Item))
                {
                    if (catchAll == null) catchAll = entry;
                    continue;
                }
                if (string.Equals(entry.Trigger.Item, itemPrefabName, StringComparison.OrdinalIgnoreCase))
                    return entry; // specific match wins
            }
            return catchAll;
        }

        /// True if any configured npc_item_submit entry targets npcSubject.
        internal static bool NpcHasConfiguredEntries(string npcSubject)
        {
            var config = Plugin.CurrentConfig;
            if (config?.Guidances == null) return false;
            foreach (var entry in config.Guidances)
            {
                if (entry.Trigger == null) continue;
                if (!string.Equals(entry.Trigger.Type, "npc_item_submit",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(entry.Trigger.Npc, npcSubject,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    /// Adds the vanilla "[1-8] Give item" prompt to the trader hover text for NPCs that have
    /// configured npc_item_submit entries but no vanilla m_useItems (Haldor, BogWitch). Vanilla
    /// Trader.GetHoverText only adds this line when m_useItems.Count > 0 (i.e., Hildir), so we
    /// mirror the exact same localized string so the UX is identical across NPCs.
    [HarmonyPatch(typeof(Trader), nameof(Trader.GetHoverText))]
    internal static class NpcItemSubmitHoverPatch
    {
        private const string GiveItemLine = "\n[<color=yellow><b>1-8</b></color>] $npc_giveitem";

        [HarmonyPostfix]
        private static void Postfix(Trader __instance, ref string __result)
        {
            if (Player.m_localPlayer == null) return;

            // Vanilla already added the line when the NPC has its own use-items (Hildir).
            if (__instance.m_useItems != null && __instance.m_useItems.Count > 0) return;

            var npcSubject = TriggerUtils.NormalizePrefabName(__instance.gameObject?.name);
            if (!NpcItemSubmitTrigger.NpcHasConfiguredEntries(npcSubject)) return;

            var line = Localization.instance != null
                ? Localization.instance.Localize(GiveItemLine)
                : GiveItemLine;
            __result += line;
        }
    }
}
