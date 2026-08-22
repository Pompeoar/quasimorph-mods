using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    internal sealed class FetchOption
    {
        public string SpaceObjectId;
        public string OrbitName;
        public string CategoryId;
        public int TargetCount;          // how many of the wanted item come back
        public int ReturnValue;
        public int CargoValue;
        public int Profit;
        public int ItemsLoaded;
        public List<string> Sellers = new List<string>();
    }

    internal sealed class FetchResult
    {
        public string Failure;
        public string TargetItemId;
        public FetchOption Chosen;
        public List<FetchOption> Considered = new List<FetchOption>();
        public List<string> Skipped = new List<string>();
    }

    /// <summary>
    /// "Get me the weapons case." Finds the orbits that actually stock the wanted item, loads
    /// the shuttle with whatever each of those destinations will pay best for, simulates the
    /// run, and keeps the loadout that brings the most of the wanted item home.
    ///
    /// The search has to mutate the real hold, because the game's simulator reads
    /// TradeShuttleStorage directly and there is no way to hand it a hypothetical cargo. Every
    /// candidate is therefore loaded, simulated and unloaded again; the winner is reloaded at
    /// the end. RestoreAll in the finally block guarantees the hold never ends up holding a
    /// half-built trial loadout if anything throws.
    /// </summary>
    internal static class Fetch
    {
        private static readonly MethodInfo SimulateMethod = typeof(TradeSystem).GetMethod(
            "SimulateTradeShuttlePreviewExchange",
            BindingFlags.NonPublic | BindingFlags.Static);

        public static FetchResult Run(State state, string targetItemId)
        {
            var result = new FetchResult { TargetItemId = targetItemId };

            if (string.IsNullOrEmpty(targetItemId))
            {
                result.Failure = "No target item yet.\n\nOpen the stock market (H), go to the goods tab and click the item you want. Then come back here and press the hotkey.";
                return result;
            }

            var progression = state.Get<MagnumProgression>();
            var factions = state.Get<Factions>();
            var prices = state.Get<ItemsPrices>();
            var difficulty = state.Get<Difficulty>();
            var stations = state.Get<Stations>();
            var travel = state.Get<TravelMetadata>();
            var cargo = state.Get<MagnumCargo>();
            var spaceTime = state.Get<SpaceTime>();

            if (progression == null || factions == null || prices == null || difficulty == null ||
                stations == null || travel == null || cargo == null || spaceTime == null)
            {
                result.Failure = "Game state is not ready.";
                return result;
            }

            var dept = progression.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null)
            {
                result.Failure = "No trade shuttle on the ship yet.";
                return result;
            }
            if (dept.ShuttleInMove)
            {
                result.Failure = "The shuttle is already out.";
                return result;
            }

            var proxyFactionId = dept.Mode == TradeShuttleMode.Barter
                ? progression.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                : null;

            // Which orbits actually have it in stock right now, and will deal with us.
            var candidates = new Dictionary<string, List<Station>>();
            foreach (var station in stations.Values)
            {
                if (string.IsNullOrEmpty(station.SpaceObjectId)) { continue; }
                if (station.SpaceObjectId == travel.CurrentSpaceObject) { continue; }
                if (!candidates.TryGetValue(station.SpaceObjectId, out var list))
                {
                    list = new List<Station>();
                    candidates[station.SpaceObjectId] = list;
                }
                list.Add(station);
            }

            var withStock = new Dictionary<string, List<Station>>();
            foreach (var pair in candidates)
            {
                var stocked = pair.Value.Any(s =>
                {
                    var f = factions.Get(s.OwnerFactionId);
                    if (f == null || !Eligible(progression, f, s, proxyFactionId)) { return false; }
                    return s.InternalStorage.Items.Any(i => i.Id == targetItemId);
                });
                if (stocked) { withStock[pair.Key] = pair.Value; }
            }

            if (withStock.Count == 0)
            {
                result.Failure =
                    "No station that will trade with you is currently stocking " +
                    Names.Item(targetItemId) + ".\n\nThe stock market's manufacturer list shows who " +
                    "makes it, but the shuttle can only buy what a station is actually holding right now.";
                return result;
            }

            var categoryId = CategoryForItem(targetItemId);
            var savedCategory = dept.SelectedBarterCategoryId;
            var originalShuttle = new List<BasePickupItem>(dept.TradeShuttleStorage.Items);

            try
            {
                UnloadAll(dept, cargo, spaceTime);

                foreach (var pair in withStock)
                {
                    var option = Trial(progression, dept, factions, prices, difficulty, cargo,
                        spaceTime, pair.Key, pair.Value, targetItemId, categoryId, proxyFactionId);
                    if (option != null) { result.Considered.Add(option); }
                    UnloadAll(dept, cargo, spaceTime);
                }

                result.Considered = result.Considered
                    .OrderByDescending(o => o.TargetCount)
                    .ThenByDescending(o => o.Profit)
                    .ToList();

                result.Chosen = result.Considered.FirstOrDefault();

                if (result.Chosen != null)
                {
                    // Rebuild the winning loadout and leave it in the hold.
                    LoadFor(progression, dept, factions, prices, cargo, spaceTime,
                        withStock[result.Chosen.SpaceObjectId], proxyFactionId);
                    dept.SelectedBarterCategoryId = result.Chosen.CategoryId;
                }
                else
                {
                    dept.SelectedBarterCategoryId = savedCategory;
                    RestoreAll(dept, cargo, spaceTime, originalShuttle);
                    result.Failure = "Could not build a loadout that brings back " +
                                     Names.Item(targetItemId) + ".";
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] fetch failed: " + e);
                dept.SelectedBarterCategoryId = savedCategory;
                try { RestoreAll(dept, cargo, spaceTime, originalShuttle); } catch { }
                result.Failure = "Planning failed: " + e.Message + "\nYour cargo was put back.";
            }

            return result;
        }

        private static FetchOption Trial(
            MagnumProgression progression, TradeShuttleDepartment dept, Factions factions,
            ItemsPrices prices, Difficulty difficulty, MagnumCargo cargo, SpaceTime spaceTime,
            string spaceObjectId, List<Station> stations, string targetItemId,
            string categoryId, string proxyFactionId)
        {
            if (!TradeSystem.HasTradeShuttleAvailableStations(progression, factions, dept.Mode, stations))
            {
                return null;
            }

            var loaded = LoadFor(progression, dept, factions, prices, cargo, spaceTime, stations, proxyFactionId);
            if (loaded == 0) { return null; }

            dept.SelectedBarterCategoryId = categoryId;

            var cargoValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, dept.TradeShuttleStorage.Items);
            var returning = Simulate(progression, dept, factions, prices, difficulty, stations);
            if (returning == null) { return null; }

            var originals = new HashSet<BasePickupItem>(dept.TradeShuttleStorage.Items);
            var targetCount = returning
                .Where(i => i.Id == targetItemId && !originals.Contains(i))
                .Sum(i => (int)i.StackCount);

            var returnValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, returning);

            var sellers = stations
                .Where(s =>
                {
                    var f = factions.Get(s.OwnerFactionId);
                    return f != null && Eligible(progression, f, s, proxyFactionId)
                        && s.InternalStorage.Items.Any(i => i.Id == targetItemId);
                })
                .Select(s => Names.Faction(s.OwnerFactionId))
                .Distinct()
                .ToList();

            return new FetchOption
            {
                SpaceObjectId = spaceObjectId,
                OrbitName = Names.Orbit(spaceObjectId),
                CategoryId = categoryId,
                TargetCount = targetCount,
                ReturnValue = returnValue,
                CargoValue = cargoValue,
                Profit = cargoValue > 0 ? Mathf.RoundToInt(returnValue * 100f / cargoValue) : 0,
                ItemsLoaded = loaded,
                Sellers = sellers
            };
        }

        private static List<BasePickupItem> Simulate(
            MagnumProgression progression, TradeShuttleDepartment dept, Factions factions,
            ItemsPrices prices, Difficulty difficulty, List<Station> stations)
        {
            if (SimulateMethod == null) { return null; }
            try
            {
                return SimulateMethod.Invoke(null, new object[]
                {
                    progression, dept, factions, prices, difficulty, stations
                }) as List<BasePickupItem>;
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] simulate: " + (e.InnerException ?? e).Message);
                return null;
            }
        }

        /// <summary>
        /// Fills the hold from ship cargo with what this destination pays best for. Items the
        /// destination actually consumes are worth their full world price; everything else only
        /// liquidates at the junk rate, so it is ranked far lower and only used as filler.
        /// </summary>
        private static int LoadFor(
            MagnumProgression progression, TradeShuttleDepartment dept, Factions factions,
            ItemsPrices prices, MagnumCargo cargo, SpaceTime spaceTime,
            List<Station> stations, string proxyFactionId)
        {
            var junkRate = progression.TradeShuttleUnsupportedSellValuePercent;

            var eligible = stations
                .Select(s => new { s, f = factions.Get(s.OwnerFactionId) })
                .Where(x => x.f != null && Eligible(progression, x.f, x.s, proxyFactionId))
                .ToList();

            var scored = new List<(BasePickupItem item, float score)>();
            foreach (var storage in cargo.ShipCargo)
            {
                foreach (var item in storage.Items.ToList())
                {
                    if (ItemInteractionSystem.IsQuestItem(item)) { continue; }

                    var world = prices.GetPrice(item.Id) * item.StackCount;
                    var wanted = eligible.Any(x => TradeSystem.IsValidItem(x.f, x.s, item.Id));
                    var value = wanted ? world : world * junkRate;
                    var cells = Mathf.Max(1, item.InventoryWidthSize);
                    scored.Add((item, value / cells));
                }
            }

            var loaded = 0;
            foreach (var entry in scored.OrderByDescending(e => e.score))
            {
                if (entry.item.Storage == null) { continue; }
                if (ItemInteractionSystem.IsQuestItem(entry.item)) { continue; }
                if (dept.TradeShuttleStorage.TryPutItem(entry.item, CellPosition.Zero)) { loaded++; }
            }
            return loaded;
        }

        private static void UnloadAll(TradeShuttleDepartment dept, MagnumCargo cargo, SpaceTime spaceTime)
        {
            foreach (var item in new List<BasePickupItem>(dept.TradeShuttleStorage.Items))
            {
                item.Storage.Remove(item);
                item.Storage = null;
                MagnumCargoSystem.AddCargo(cargo, spaceTime, item, null, splittedItem: false, tabFilter: true);
            }
            dept.CheckShuttleArrived();
        }

        private static void RestoreAll(
            TradeShuttleDepartment dept, MagnumCargo cargo, SpaceTime spaceTime,
            List<BasePickupItem> original)
        {
            UnloadAll(dept, cargo, spaceTime);
            foreach (var item in original)
            {
                if (item.Storage == null) { continue; }
                dept.TradeShuttleStorage.TryPutItem(item, CellPosition.Zero);
            }
        }

        private static bool Eligible(
            MagnumProgression progression, Faction faction, Station station, string proxyFactionId)
        {
            if (!string.IsNullOrEmpty(proxyFactionId) && station.OwnerFactionId == proxyFactionId) { return false; }
            if (faction.Record == null || !faction.Record.CanBeTraded) { return false; }
            if (progression.TradeShuttleContraband) { return true; }
            return faction.PlayerReputation >= Data.Global.TradeMinReputationToExchange;
        }

        private static string CategoryForItem(string itemId)
        {
            var record = Data.Items.GetSimpleRecord<ItemRecord>(itemId);
            if (record == null) { return string.Empty; }
            foreach (var cat in Data.TradeShuttleBarterCategories.Records)
            {
                if (cat.ItemClasses != null && cat.ItemClasses.Contains(record.ItemClass)) { return cat.Id; }
            }
            return string.Empty;
        }
    }
}
