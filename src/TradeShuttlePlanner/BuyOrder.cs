using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    internal sealed class BuyCandidate
    {
        public string Id;
        public int World;
        public int Buy;
        public int Cells;
        public int Stock;
    }

    /// <summary>
    /// Models the order in which the shuttle buys.
    ///
    /// GetBestTradeShuttleItemsFromStation repeatedly takes the best world/buy ratio item it can
    /// still afford and still fit, so a specific item only ever comes home if it sits near the
    /// top of that ordering within its barter category at that station. Everything above it gets
    /// bought first and fills the hold. That is why "just load more cargo" does not work: trade
    /// points are not the binding constraint, return cells are.
    /// </summary>
    internal static class BuyOrder
    {
        /// <summary>Transcribed from TradeSystem.IsTradeShuttleBuyCandidateBetter.</summary>
        public static int Compare(BuyCandidate a, BuyCandidate b)
        {
            if (a.Buy == 0 || b.Buy == 0)
            {
                if (a.Buy == 0 && b.Buy != 0) { return -1; }
                if (a.Buy != 0 && b.Buy == 0) { return 1; }
            }
            else
            {
                var left = (long)a.World * b.Buy;
                var right = (long)b.World * a.Buy;
                if (left != right) { return left > right ? -1 : 1; }
            }

            var margin = (b.World - b.Buy).CompareTo(a.World - a.Buy);
            if (margin != 0) { return margin; }
            if (a.Cells != b.Cells) { return a.Cells < b.Cells ? -1 : 1; }
            if (a.World != b.World) { return a.World > b.World ? -1 : 1; }
            if (a.Buy != b.Buy) { return a.Buy < b.Buy ? -1 : 1; }
            return string.CompareOrdinal(a.Id, b.Id);
        }

        public static List<BuyCandidate> Candidates(
            MagnumProgression progression, Faction faction, Station station, ItemsPrices prices,
            HashSet<ItemClass> allowedClasses)
        {
            var byId = new Dictionary<string, BuyCandidate>();
            foreach (var item in station.InternalStorage.Items)
            {
                if (allowedClasses != null && allowedClasses.Count > 0)
                {
                    var record = Data.Items.GetSimpleRecord<ItemRecord>(item.Id);
                    if (record == null || !allowedClasses.Contains(record.ItemClass)) { continue; }
                }

                if (!byId.TryGetValue(item.Id, out var c))
                {
                    c = new BuyCandidate
                    {
                        Id = item.Id,
                        World = Mathf.RoundToInt(prices.GetPrice(item.Id)),
                        Buy = TradeSystem.GetItemBuyPrice(progression, faction, station, prices, item.Id),
                        Cells = Mathf.Max(1, item.InventoryWidthSize)
                    };
                    byId[item.Id] = c;
                }
                c.Stock += item.StackCount;
            }

            var list = byId.Values.ToList();
            list.Sort(Compare);
            return list;
        }

        public static HashSet<ItemClass> ClassesForCategory(string categoryId)
        {
            if (string.IsNullOrEmpty(categoryId)) { return null; }
            var record = Data.TradeShuttleBarterCategories.GetRecord(categoryId);
            if (record?.ItemClasses == null) { return null; }
            return new HashSet<ItemClass>(record.ItemClasses);
        }

        /// <summary>
        /// 1-based position of <paramref name="itemId"/> in the buy order, and how many cells the
        /// shuttle would spend on strictly better items before reaching it. The second number is
        /// the one that matters: if it already exceeds the hold, the item is unreachable no
        /// matter how much cargo you send.
        /// </summary>
        public static void Rank(
            List<BuyCandidate> ordered, string itemId, out int rank, out int field, out int cellsAhead)
        {
            rank = 0;
            field = ordered.Count;
            cellsAhead = 0;

            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Id == itemId) { rank = i + 1; return; }
                cellsAhead += ordered[i].Cells * Math.Max(1, ordered[i].Stock);
            }
        }

        /// <summary>
        /// Trade points that are certainly enough to reach <paramref name="itemId"/>: the cost of
        /// clearing out every strictly better-ranked item's entire stock, plus the item itself.
        ///
        /// This is an upper bound, not the minimum. The real loop skips a better item once it can
        /// no longer afford it, so the target is often bought with far less - which is exactly why
        /// "you need a bigger cargo" is usually true and the earlier flat refusal was wrong. It is
        /// only genuinely hopeless when the better stock would fill every return cell first.
        /// </summary>
        public static int PointsToReach(List<BuyCandidate> ordered, string itemId)
        {
            long total = 0;
            foreach (var c in ordered)
            {
                if (c.Id == itemId) { return (int)Math.Min(int.MaxValue, total + c.Buy); }
                total += (long)c.Buy * Math.Max(1, c.Stock);
            }
            return 0;
        }
    }
}
