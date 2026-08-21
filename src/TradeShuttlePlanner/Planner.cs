using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    internal sealed class OrbitPlan
    {
        public string SpaceObjectId;
        public string OrbitName;
        public string CategoryId;
        public int StationCount;
        public int ReturnValue;
        public int Profit;                       // percent, same formula as the starmap
        public List<ItemLine> Gains = new List<ItemLine>();
    }

    internal sealed class ItemLine
    {
        public string ItemId;
        public int Count;
        public int Value;
    }

    internal sealed class SourceHit
    {
        public string SpaceObjectId;
        public string StationId;
        public string FactionId;
        public string ItemId;
        public int Stock;
        public int BuyPrice;
        public int WorldPrice;
        public bool Eligible;                    // passes the shuttle's reputation/faction gate
        public string CategoryId;                // which dropdown option would prioritise it
        public int RatioRank;                    // 1 = the shuttle buys this first in that class
        public int RatioField;                   // how many items it competes with
    }

    internal sealed class PlanResult
    {
        public string Failure;
        public TradeShuttleMode Mode;
        public int CargoValue;
        public int CargoCells;
        public int CargoCapacity;
        public int OrbitsScanned;
        public int CombinationsEvaluated;
        public List<OrbitPlan> Ranked = new List<OrbitPlan>();
        public List<ItemLine> DeadWeight = new List<ItemLine>();
        public string DeadWeightOrbit;
        public List<SourceHit> WantedHits = new List<SourceHit>();
        public List<string> WantedMisses = new List<string>();
        public bool UsedItemLevelDetail;
    }

    internal static class Planner
    {
        // TradeSystem.SimulateTradeShuttlePreviewExchange is private, but it is the only thing
        // that returns the actual resulting cargo rather than just its total value. The public
        // GetTradeShuttleExchangePreview wraps it and throws the item list away. Reflect for the
        // detail, and degrade to the public wrapper if the signature ever moves.
        private static readonly MethodInfo SimulateMethod = typeof(TradeSystem).GetMethod(
            "SimulateTradeShuttlePreviewExchange",
            BindingFlags.NonPublic | BindingFlags.Static);

        public static PlanResult Run(State state)
        {
            var result = new PlanResult();

            var progression = state.Get<MagnumProgression>();
            var factions = state.Get<Factions>();
            var prices = state.Get<ItemsPrices>();
            var difficulty = state.Get<Difficulty>();
            var stations = state.Get<Stations>();
            var travel = state.Get<TravelMetadata>();

            if (progression == null || factions == null || prices == null ||
                difficulty == null || stations == null || travel == null)
            {
                result.Failure = "Game state is not ready.";
                return result;
            }

            var dept = progression.GetDepartment<TradeShuttleDepartment>();
            if (dept == null)
            {
                result.Failure = "No trade shuttle department on the ship yet.";
                return result;
            }
            if (dept.ShuttleInMove)
            {
                result.Failure = "The shuttle is already out. Nothing to plan until it returns.";
                return result;
            }

            var storage = dept.TradeShuttleStorage;
            var cargo = storage?.Items;
            if (cargo == null || cargo.Count == 0)
            {
                result.Failure = "The shuttle hold is empty. Load it first, then press the hotkey.";
                return result;
            }

            result.CargoValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, cargo);
            result.CargoCells = cargo.Sum(i => i.InventoryWidthSize);
            result.CargoCapacity = storage.Width * storage.Height;

            // Barter mode refuses to deal with your own proxy corporation's stations, so they
            // must not count when deciding whether an item has a buyer. Other modes do use them.
            var proxyFactionId = dept.Mode == TradeShuttleMode.Barter
                ? progression.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                : null;

            var byOrbit = new Dictionary<string, List<Station>>();
            foreach (var station in stations.Values)
            {
                if (string.IsNullOrEmpty(station.SpaceObjectId)) { continue; }
                if (station.SpaceObjectId == travel.CurrentSpaceObject) { continue; }
                if (!byOrbit.TryGetValue(station.SpaceObjectId, out var list))
                {
                    list = new List<Station>();
                    byOrbit[station.SpaceObjectId] = list;
                }
                list.Add(station);
            }
            result.OrbitsScanned = byOrbit.Count;

            var categoryIds = new List<string> { string.Empty };
            if (dept.Mode == TradeShuttleMode.Barter)
            {
                categoryIds.AddRange(Data.TradeShuttleBarterCategories.Records.Select(r => r.Id));
            }
            result.Mode = dept.Mode;

            var originals = new HashSet<BasePickupItem>(cargo);

            // SelectedBarterCategoryId is a [Save] field, so it must come back exactly as we
            // found it even if a simulation throws.
            var savedCategory = dept.SelectedBarterCategoryId;
            try
            {
                foreach (var pair in byOrbit)
                {
                    if (!TradeSystem.HasTradeShuttleAvailableStations(
                            progression, factions, dept.Mode, pair.Value))
                    {
                        continue;
                    }

                    OrbitPlan best = null;
                    foreach (var categoryId in categoryIds)
                    {
                        dept.SelectedBarterCategoryId = categoryId;
                        var plan = Evaluate(progression, dept, factions, prices, difficulty,
                            pair.Key, pair.Value, originals, result);
                        result.CombinationsEvaluated++;
                        if (plan == null) { continue; }
                        plan.CategoryId = categoryId;
                        if (best == null || plan.ReturnValue > best.ReturnValue) { best = plan; }
                    }
                    if (best != null) { result.Ranked.Add(best); }
                }
            }
            finally
            {
                dept.SelectedBarterCategoryId = savedCategory;
            }

            result.Ranked = result.Ranked
                .OrderByDescending(p => p.Profit)
                .ThenByDescending(p => p.ReturnValue)
                .ToList();

            var top = result.Ranked.FirstOrDefault();
            if (top != null)
            {
                result.DeadWeightOrbit = top.OrbitName;
                result.DeadWeight = FindDeadWeight(progression, factions, prices, cargo,
                    byOrbit[top.SpaceObjectId], proxyFactionId);
            }

            FindWanted(progression, factions, prices, byOrbit, proxyFactionId, result);
            return result;
        }

        private static OrbitPlan Evaluate(
            MagnumProgression progression, TradeShuttleDepartment dept, Factions factions,
            ItemsPrices prices, Difficulty difficulty, string spaceObjectId,
            List<Station> stations, HashSet<BasePickupItem> originals, PlanResult result)
        {
            List<BasePickupItem> returning = null;
            if (SimulateMethod != null)
            {
                try
                {
                    returning = SimulateMethod.Invoke(null, new object[]
                    {
                        progression, dept, factions, prices, difficulty, stations
                    }) as List<BasePickupItem>;
                    result.UsedItemLevelDetail = true;
                }
                catch (Exception e)
                {
                    Debug.Log("[TradeShuttlePlanner] simulate failed: " + (e.InnerException ?? e).Message);
                }
            }

            var plan = new OrbitPlan
            {
                SpaceObjectId = spaceObjectId,
                OrbitName = Names.Orbit(spaceObjectId),
                StationCount = stations.Count
            };

            if (returning != null)
            {
                plan.ReturnValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, returning);
                var gained = new Dictionary<string, ItemLine>();
                foreach (var item in returning)
                {
                    if (originals.Contains(item)) { continue; }   // unsold cargo coming home again
                    if (!gained.TryGetValue(item.Id, out var line))
                    {
                        line = new ItemLine { ItemId = item.Id };
                        gained[item.Id] = line;
                    }
                    line.Count += item.StackCount;
                    line.Value += Mathf.RoundToInt(prices.GetPrice(item.Id)) * item.StackCount;
                }
                plan.Gains = gained.Values.OrderByDescending(l => l.Value).ToList();
            }
            else
            {
                var preview = TradeSystem.GetTradeShuttleExchangePreview(
                    progression, factions, prices, difficulty, dept, stations);
                if (preview == null) { return null; }
                plan.ReturnValue = preview.ExpectedCargoWorldPrice;
            }

            var cargoValue = result.CargoValue;
            plan.Profit = cargoValue > 0
                ? Mathf.RoundToInt(plan.ReturnValue * 100f / cargoValue)
                : 0;
            return plan;
        }

        /// <summary>
        /// Cargo that no eligible station in the destination consumes. IsValidItem is the same
        /// gate the shuttle uses, so anything failing it everywhere gets liquidated at the junk
        /// rate rather than sold.
        /// </summary>
        private static List<ItemLine> FindDeadWeight(
            MagnumProgression progression, Factions factions, ItemsPrices prices,
            List<BasePickupItem> cargo, List<Station> stations, string proxyFactionId)
        {
            var dead = new Dictionary<string, ItemLine>();
            foreach (var item in cargo)
            {
                var wanted = false;
                foreach (var station in stations)
                {
                    var faction = factions.Get(station.OwnerFactionId);
                    if (faction == null) { continue; }
                    if (!IsShuttleEligible(progression, faction, station, proxyFactionId)) { continue; }
                    if (TradeSystem.IsValidItem(faction, station, item.Id)) { wanted = true; break; }
                }
                if (wanted) { continue; }
                if (!dead.TryGetValue(item.Id, out var line))
                {
                    line = new ItemLine { ItemId = item.Id };
                    dead[item.Id] = line;
                }
                line.Count += item.StackCount;
                line.Value += Mathf.RoundToInt(prices.GetPrice(item.Id)) * item.StackCount;
            }
            return dead.Values.OrderByDescending(l => l.Value).ToList();
        }

        private static bool IsShuttleEligible(
            MagnumProgression progression, Faction faction, Station station, string proxyFactionId)
        {
            if (!string.IsNullOrEmpty(proxyFactionId) && station.OwnerFactionId == proxyFactionId) { return false; }
            if (faction.Record == null || !faction.Record.CanBeTraded) { return false; }
            if (progression.TradeShuttleContraband) { return true; }
            return faction.PlayerReputation >= Data.Global.TradeMinReputationToExchange;
        }

        /// <summary>
        /// "Where do I buy X." For each wanted term, sweep every station's sale stock and report
        /// the buy price at the local reputation, plus where the item sits in the shuttle's own
        /// value-for-money ordering inside its barter category. Rank 1 means the priority budget
        /// buys it before anything else in that class.
        /// </summary>
        private static void FindWanted(
            MagnumProgression progression, Factions factions, ItemsPrices prices,
            Dictionary<string, List<Station>> byOrbit, string proxyFactionId, PlanResult result)
        {
            var terms = PlannerConfig.Current.Wanted;
            if (terms == null || terms.Count == 0) { return; }

            foreach (var term in terms)
            {
                var hits = new List<SourceHit>();
                foreach (var pair in byOrbit)
                {
                    foreach (var station in pair.Value)
                    {
                        var faction = factions.Get(station.OwnerFactionId);
                        if (faction == null) { continue; }

                        var counts = new Dictionary<string, int>();
                        foreach (var item in station.InternalStorage.Items)
                        {
                            if (!Matches(item.Id, term)) { continue; }
                            counts.TryGetValue(item.Id, out var n);
                            counts[item.Id] = n + item.StackCount;
                        }
                        foreach (var kv in counts)
                        {
                            var categoryId = CategoryForItem(kv.Key);
                            RankInCategory(progression, faction, station, prices, kv.Key, categoryId,
                                out var rank, out var field);
                            hits.Add(new SourceHit
                            {
                                SpaceObjectId = pair.Key,
                                StationId = station.Id,
                                FactionId = station.OwnerFactionId,
                                ItemId = kv.Key,
                                Stock = kv.Value,
                                BuyPrice = TradeSystem.GetItemBuyPrice(progression, faction, station, prices, kv.Key),
                                WorldPrice = Mathf.RoundToInt(prices.GetPrice(kv.Key)),
                                Eligible = IsShuttleEligible(progression, faction, station, proxyFactionId),
                                CategoryId = categoryId,
                                RatioRank = rank,
                                RatioField = field
                            });
                        }
                    }
                }

                if (hits.Count == 0) { result.WantedMisses.Add(term); continue; }
                result.WantedHits.AddRange(hits
                    .OrderByDescending(h => h.Eligible)
                    .ThenBy(h => h.RatioRank)
                    .ThenBy(h => h.BuyPrice)
                    .Take(8));
            }
        }

        private static bool Matches(string itemId, string term)
        {
            if (itemId.Equals(term, StringComparison.OrdinalIgnoreCase)) { return true; }
            if (itemId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            return Names.Item(itemId).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ItemClass ClassOf(string itemId)
        {
            var record = Data.Items.GetSimpleRecord<ItemRecord>(itemId);
            return record?.ItemClass ?? ItemClass.None;
        }

        private static string CategoryForItem(string itemId)
        {
            var cls = ClassOf(itemId);
            if (cls == ItemClass.None) { return string.Empty; }
            foreach (var record in Data.TradeShuttleBarterCategories.Records)
            {
                if (record.ItemClasses != null && record.ItemClasses.Contains(cls)) { return record.Id; }
            }
            return string.Empty;
        }

        /// <summary>
        /// Reproduces the leading terms of IsTradeShuttleBuyCandidateBetter -- a zero buy price
        /// wins outright, then the worldPrice/buyPrice ratio compared as cross-multiplied longs.
        /// That is enough to say whether the shuttle reaches this item before its rivals.
        /// </summary>
        private static void RankInCategory(
            MagnumProgression progression, Faction faction, Station station, ItemsPrices prices,
            string itemId, string categoryId, out int rank, out int field)
        {
            rank = 0;
            field = 0;
            if (string.IsNullOrEmpty(categoryId)) { return; }

            var record = Data.TradeShuttleBarterCategories.GetRecord(categoryId);
            if (record?.ItemClasses == null) { return; }
            var allowed = new HashSet<ItemClass>(record.ItemClasses);

            var seen = new HashSet<string>();
            var rivals = new List<(string id, int world, int buy, int cells)>();
            foreach (var item in station.InternalStorage.Items)
            {
                if (!seen.Add(item.Id)) { continue; }
                if (!allowed.Contains(ClassOf(item.Id))) { continue; }
                rivals.Add((
                    item.Id,
                    Mathf.RoundToInt(prices.GetPrice(item.Id)),
                    TradeSystem.GetItemBuyPrice(progression, faction, station, prices, item.Id),
                    item.InventoryWidthSize));
            }

            field = rivals.Count;
            if (field == 0) { return; }

            // Mirrors IsTradeShuttleBuyCandidateBetter. That predicate is a strict total order,
            // so sorting by it reproduces the game's repeated-argmax selection exactly; see
            // TradeShuttlePlannerChecks in tools\Verify.
            rivals.Sort((a, b) =>
            {
                if (a.buy == 0 || b.buy == 0)
                {
                    if (a.buy == 0 && b.buy != 0) { return -1; }
                    if (a.buy != 0 && b.buy == 0) { return 1; }
                }
                else
                {
                    var left = (long)a.world * b.buy;
                    var right = (long)b.world * a.buy;
                    if (left != right) { return left > right ? -1 : 1; }
                }
                var margin = (b.world - b.buy).CompareTo(a.world - a.buy);
                if (margin != 0) { return margin; }
                if (a.cells != b.cells) { return a.cells < b.cells ? -1 : 1; }
                if (a.world != b.world) { return a.world > b.world ? -1 : 1; }
                if (a.buy != b.buy) { return a.buy < b.buy ? -1 : 1; }
                return string.CompareOrdinal(a.id, b.id);
            });

            rank = rivals.FindIndex(r => r.id == itemId) + 1;
        }
    }
}
