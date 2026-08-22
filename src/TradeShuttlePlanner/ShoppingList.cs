using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// Remembers the item the player last inspected in the stock market.
    ///
    /// This is the whole reason the mod needs no UI of its own. Opening Weapons Case in the
    /// stock market already IS the act of saying "this is what I want", so the target is
    /// captured from TradeWindow.Configure rather than asked for a second time.
    /// </summary>
    public static class ShoppingList
    {
        public static string TargetItemId { get; private set; }
        public static DateTime PickedAt { get; private set; }

        public static void Set(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) { return; }
            TargetItemId = itemId;
            PickedAt = DateTime.Now;
            Debug.Log("[TradeShuttlePlanner] shopping target: " + itemId);
        }

        public static void Clear() => TargetItemId = null;
    }

    [HarmonyPatch(typeof(TradeWindow), nameof(TradeWindow.Configure))]
    internal static class TradeWindowConfigurePatch
    {
        // Postfix, not prefix: if vanilla throws we do not want a stale target recorded.
        private static void Postfix(string itemId) => ShoppingList.Set(itemId);
    }
}
