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
        public int Rank;                 // target's position in the station's buy order, 1-based
        public int Field;                // how many distinct items share that category there
        public int CellsAhead;           // cells the shuttle spends on better items before it
        public int TargetBuyPrice;       // reputation-scaled price the best seller here quotes
        public int Reputation;           // our standing with that seller
        public List<BasePickupItem> Load = new List<BasePickupItem>();
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

                // Anything that actually delivers wins. Past that, prefer the seller that quotes
                // the lowest reputation-scaled price and has the least better-ratio stock queued
                // ahead of the item, because those are the two things that decide whether a
                // bigger cargo would ever have helped. Profit is only a final tie-break.
                result.Considered = result.Considered
                    .OrderByDescending(o => o.TargetCount)
                    .ThenBy(o => o.TargetBuyPrice == 0 ? int.MaxValue : o.TargetBuyPrice)
                    .ThenBy(o => o.CellsAhead < 0 ? int.MaxValue : o.CellsAhead)
                    .ThenByDescending(o => o.Profit)
                    .ToList();

                result.Chosen = result.Considered.FirstOrDefault();

                if (result.Chosen != null)
                {
                    // Rebuild the winning loadout and leave it in the hold.
                    foreach (var item in result.Chosen.Load)
                    {
                        if (item.Storage == null)
                        {
                            dept.TradeShuttleStorage.TryPutItem(item, CellPosition.Zero);
                        }
                    }
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

            dept.SelectedBarterCategoryId = categoryId;

            var eligible = stations
                .Select(s => new StationView { Station = s, Faction = factions.Get(s.OwnerFactionId) })
                .Where(x => x.Faction != null && Eligible(progression, x.Faction, x.Station, proxyFactionId))
                .ToList();

            var sellers = eligible
                .Where(x => x.Station.InternalStorage.Items.Any(i => i.Id == targetItemId))
                .ToList();
            if (sellers.Count == 0) { return null; }

            // Where the wanted item sits in the shuttle's buy order here, and what it costs us.
            // The shuttle always takes the best world/buy ratio it can still afford and still fit,
            // so an item with a lot of better-ratio stock ahead of it never comes home however
            // much cargo you send. Buy price is reputation-scaled, which is why a disliked faction
            // can quote double what a friendly one does for the identical item.
            var allowed = BuyOrder.ClassesForCategory(categoryId);
            var rank = 0;
            var field = 0;
            var cellsAhead = int.MaxValue;
            var buyPrice = 0;
            var reputation = 0;

            foreach (var view in sellers)
            {
                var ordered = BuyOrder.Candidates(progression, view.Faction, view.Station, prices, allowed);
                BuyOrder.Rank(ordered, targetItemId, out var r, out var f, out var ca);
                if (r <= 0) { continue; }

                var price = TradeSystem.GetItemBuyPrice(
                    progression, view.Faction, view.Station, prices, targetItemId);

                if (buyPrice == 0 || price < buyPrice)
                {
                    buyPrice = price;
                    reputation = Mathf.RoundToInt(view.Faction.PlayerReputation);
                }
                if (ca < cellsAhead) { cellsAhead = ca; rank = r; field = f; }
            }
            if (cellsAhead == int.MaxValue) { cellsAhead = -1; }

            var cap = CargoCap(prices, targetItemId);
            var queue = OrderedCargo(progression, prices, cargo, eligible);

            FetchOption best = null;
            var loaded = 0;
            var loadedValue = 0f;
            var lastSim = 0f;
            var step = Mathf.Max(1f, cap / 12f);

            foreach (var entry in queue)
            {
                if (loadedValue >= cap) { break; }
                if (entry.Item.Storage == null) { continue; }
                if (!dept.TradeShuttleStorage.TryPutItem(entry.Item, CellPosition.Zero)) { continue; }

                loaded++;
                loadedValue += entry.World;

                if (loadedValue - lastSim < step && loadedValue < cap) { continue; }
                lastSim = loadedValue;

                var option = Evaluate(progression, dept, factions, prices, difficulty, stations,
                    spaceObjectId, categoryId, targetItemId, loaded);
                if (option == null) { continue; }

                option.Rank = rank;
                option.Field = field;
                option.CellsAhead = cellsAhead;
                option.TargetBuyPrice = buyPrice;
                option.Reputation = reputation;
                option.Sellers = sellers
                    .Select(v => Names.Faction(v.Station.OwnerFactionId)).Distinct().ToList();
                option.Load = new List<BasePickupItem>(dept.TradeShuttleStorage.Items);
                best = option;

                if (option.TargetCount >= 1) { break; }
            }

            return best;
        }

        private static FetchOption Evaluate(
            MagnumProgression progression, TradeShuttleDepartment dept, Factions factions,
            ItemsPrices prices, Difficulty difficulty, List<Station> stations,
            string spaceObjectId, string categoryId, string targetItemId, int loaded)
        {
            var cargoValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, dept.TradeShuttleStorage.Items);
            var originals = new HashSet<BasePickupItem>(dept.TradeShuttleStorage.Items);

            var returning = Simulate(progression, dept, factions, prices, difficulty, stations);
            if (returning == null) { return null; }

            var targetCount = returning
                .Where(i => i.Id == targetItemId && !originals.Contains(i))
                .Sum(i => (int)i.StackCount);

            var returnValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, returning);

            return new FetchOption
            {
                SpaceObjectId = spaceObjectId,
                OrbitName = Names.Orbit(spaceObjectId),
                CategoryId = categoryId,
                TargetCount = targetCount,
                ReturnValue = returnValue,
                CargoValue = cargoValue,
                Profit = cargoValue > 0 ? Mathf.RoundToInt(returnValue * 100f / cargoValue) : 0,
                ItemsLoaded = loaded
            };
        }

        /// <summary>
        /// How much cargo world value we are willing to spend. Sending 100k of gear to fetch a 5k
        /// item is exactly the hold-emptying behaviour that made the first build unusable, so the
        /// default ceiling is a multiple of what the wanted item is worth.
        /// </summary>
        private static float CargoCap(ItemsPrices prices, string targetItemId)
        {
            var cfg = PlannerConfig.Current;
            if (cfg.MaxCargoValue > 0) { return cfg.MaxCargoValue; }
            var world = prices.GetPrice(targetItemId);
            return Mathf.Max(2000f, world * cfg.CargoValueMultiplier);
        }

        private sealed class StationView
        {
            public Station Station;
            public Faction Faction;
        }

        private sealed class CargoCandidate
        {
            public BasePickupItem Item;
            public float World;
            public float Unit;
            public int Tier;
        }

        /// <summary>
        /// Decides what the shuttle is allowed to spend, cheapest first.
        ///
        /// The first build ranked by value per cell and loaded everything that fit, which sent the
        /// player's good gear away. Barter junk goes first now: cheap stock the destination
        /// actually consumes, then cheap stock it does not, and only then anything valuable. The
        /// keep list is absolute.
        /// </summary>
        private static List<CargoCandidate> OrderedCargo(
            MagnumProgression progression, ItemsPrices prices, MagnumCargo cargo,
            List<StationView> eligible)
        {
            var cfg = PlannerConfig.Current;
            var ceiling = cfg.JunkCeiling;
            var list = new List<CargoCandidate>();

            foreach (var storage in cargo.ShipCargo)
            {
                foreach (var item in storage.Items.ToList())
                {
                    if (ItemInteractionSystem.IsQuestItem(item)) { continue; }
                    if (IsKept(item, cfg)) { continue; }

                    var unit = prices.GetPrice(item.Id);
                    var world = unit * item.StackCount;
                    var consumed = eligible.Any(x => TradeSystem.IsValidItem(x.Faction, x.Station, item.Id));

                    // Anything the destination does not consume is dumped at
                    // TradeShuttleUnsupportedSellValuePercent - a fifth of its worth - while still
                    // eating a return cell. Sending it is how a six-figure hold came back at 39%.
                    if (!consumed && !cfg.AllowUnwantedFiller) { continue; }

                    var tier = consumed ? (unit <= ceiling ? 0 : 1) : 2;

                    list.Add(new CargoCandidate { Item = item, World = world, Unit = unit, Tier = tier });
                }
            }

            return list.OrderBy(c => c.Tier).ThenBy(c => c.Unit).ToList();
        }

        private static bool IsKept(BasePickupItem item, PlannerConfig cfg)
        {
            if (cfg.Keep == null || cfg.Keep.Count == 0) { return false; }
            var display = Names.Item(item.Id) ?? string.Empty;
            foreach (var fragment in cfg.Keep)
            {
                if (item.Id.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
                if (display.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) { return true; }
            }
            return false;
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
