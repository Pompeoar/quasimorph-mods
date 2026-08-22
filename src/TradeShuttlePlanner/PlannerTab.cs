using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// Injects a third "Planner" tab into the vanilla Trade Shuttle screen and keeps it mutually
    /// exclusive with the two stock tabs.
    ///
    /// The screen has no tab-group component; the vanilla ShowPage toggles the two page roots and
    /// the two IconTabButton states by hand. We mirror that: our tab button turns our page root on
    /// and both stock roots off, and a postfix on ShowPage turns our page off again whenever the
    /// player picks either stock tab. Everything is wrapped so a null field or a UI exception logs
    /// once and no-ops rather than throwing every frame, which would make the game unplayable.
    /// </summary>
    internal static class PlannerTab
    {
        internal static PlannerPanel Instance;
        private static bool _buildFailedLogged;

        [HarmonyPatch(typeof(TradeShuttleScreen), "Awake")]
        internal static class AwakePatch
        {
            private static void Postfix(TradeShuttleScreen __instance)
            {
                try
                {
                    // Screens are instantiated once at startup, so build the tab once.
                    if (Instance != null && Instance) { return; }
                    Instance = PlannerPanel.Build(__instance);
                }
                catch (Exception e)
                {
                    if (!_buildFailedLogged)
                    {
                        _buildFailedLogged = true;
                        Debug.LogError("[TradeShuttlePlanner] planner tab build failed, tab disabled: " + e);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TradeShuttleScreen), "ShowPage")]
        internal static class ShowPagePatch
        {
            // Selecting either stock tab must hide our page and reset our tab button.
            private static void Postfix()
            {
                try
                {
                    if (Instance != null && Instance) { Instance.HideForVanillaPage(); }
                }
                catch (Exception e)
                {
                    Debug.LogError("[TradeShuttlePlanner] ShowPage hook: " + e.Message);
                }
            }
        }
    }

    /// <summary>Marks a pooled station row and remembers which orbit it selects.</summary>
    internal sealed class PlannerRow : MonoBehaviour
    {
        public string SpaceObjectId;
    }

    /// <summary>
    /// The Planner page. Lives as a MonoBehaviour on the cloned page-root GameObject, so its Update
    /// only ticks while the tab is open, and OnEnable gives a free full refresh when the player
    /// switches to it. The whole hierarchy is built by cloning existing screen widgets: the report
    /// page for the container, its caption for the text blocks, and the unload-all button for every
    /// clickable control. Nothing is built from raw Unity primitives and no AssetBundle is loaded.
    /// </summary>
    internal sealed class PlannerPanel : MonoBehaviour
    {
        private TradeShuttleScreen _screen;

        private IconTabButton _plannerButton;
        private IconTabButton _cargoButton;
        private IconTabButton _lastButton;
        private GameObject _cargoRoot;
        private GameObject _lastRoot;
        private GameObject _pageRoot;

        private CommonButton _buttonTemplate;
        private TextMeshProUGUI _headerText;
        private TextMeshProUGUI _previewText;
        private Transform _listRoot;
        private CommonButton _loadInDemandButton;
        private CommonButton _loadBestButton;
        private readonly List<CommonButton> _rowPool = new List<CommonButton>();

        private string _selectedSpaceObjectId;
        private string _lastTarget = "\0";
        private int _lastHoldHash;
        private int _updateFailures;

        // Reputation-independent accent colours, kept as literals so the panel binds no more of
        // Colors than it must. Faction rows use the game's own reputation mapping instead.
        private const string TargetColor = "#F2C14E";
        private const string GoodColor = "#7CFF7C";
        private const string BadColor = "#FF7C7C";

        public static PlannerPanel Build(TradeShuttleScreen screen)
        {
            if (screen == null) { return null; }

            var t = Traverse.Create(screen);
            var lastButton = t.Field("_lastTripReportPageButton").GetValue<IconTabButton>();
            var cargoButton = t.Field("_cargoPageButton").GetValue<IconTabButton>();
            var lastRoot = t.Field("_lastTripReportPageRoot").GetValue<GameObject>();
            var cargoRoot = t.Field("_cargoPageRoot").GetValue<GameObject>();
            var unloadButton = t.Field("_unloadAllButton").GetValue<CommonButton>();

            if (lastButton == null || cargoButton == null || lastRoot == null ||
                cargoRoot == null || unloadButton == null)
            {
                Debug.LogError("[TradeShuttlePlanner] a required screen field was null; planner tab not built.");
                return null;
            }

            // Clone the last-trip page to get a ready-made container: a caption plus a laid-out
            // report root. Its own controller would fight us, so switch it off but still read the
            // child references out of it by reflection.
            var pageRoot = UnityEngine.Object.Instantiate(lastRoot, lastRoot.transform.parent, false);
            pageRoot.name = "TradeShuttlePlanner_PageRoot";
            pageRoot.SetActive(false);

            var reportPage = pageRoot.GetComponent<TradeShuttleLastReportPage>();
            TextMeshProUGUI caption = null;
            Transform listRoot = null;
            if (reportPage != null)
            {
                reportPage.enabled = false;
                var rt = Traverse.Create(reportPage);
                caption = rt.Field("_tripRootCaption").GetValue<TextMeshProUGUI>();
                listRoot = rt.Field("_reportRoot").GetValue<RectTransform>();
                var emptyState = rt.Field("_emptyState").GetValue<GameObject>();
                if (emptyState != null) { emptyState.SetActive(false); }
            }
            if (caption == null || listRoot == null)
            {
                Debug.LogError("[TradeShuttlePlanner] report page layout not as expected; planner tab not built.");
                UnityEngine.Object.Destroy(pageRoot);
                return null;
            }

            // Clone the tab button. Clones do not carry over event subscriptions, so its OnClick
            // starts empty and we own it.
            var tabGo = UnityEngine.Object.Instantiate(lastButton.gameObject, lastButton.transform.parent, false);
            tabGo.name = "TradeShuttlePlanner_TabButton";
            tabGo.transform.SetAsLastSibling();
            var plannerButton = tabGo.GetComponent<IconTabButton>();
            if (plannerButton == null)
            {
                Debug.LogError("[TradeShuttlePlanner] cloned tab lost its IconTabButton; planner tab not built.");
                UnityEngine.Object.Destroy(tabGo);
                UnityEngine.Object.Destroy(pageRoot);
                return null;
            }

            var panel = pageRoot.AddComponent<PlannerPanel>();
            panel._screen = screen;
            panel._plannerButton = plannerButton;
            panel._cargoButton = cargoButton;
            panel._lastButton = lastButton;
            panel._cargoRoot = cargoRoot;
            panel._lastRoot = lastRoot;
            panel._pageRoot = pageRoot;
            panel._buttonTemplate = unloadButton;
            panel._headerText = caption;
            panel._listRoot = listRoot;

            panel.BuildContent();

            plannerButton.SetState(IconTabButtonState.Inactive);
            plannerButton.OnClick += panel.OnPlannerTabClicked;

            Debug.Log("[TradeShuttlePlanner] planner tab injected.");
            return panel;
        }

        private void BuildContent()
        {
            // A second caption clone for the live preview text.
            var previewGo = UnityEngine.Object.Instantiate(_headerText.gameObject, _listRoot, false);
            previewGo.name = "TradeShuttlePlanner_Preview";
            _previewText = previewGo.GetComponent<TextMeshProUGUI>();
            if (_previewText != null)
            {
                _previewText.alignment = TextAlignmentOptions.TopLeft;
                _previewText.transform.SetAsFirstSibling();
            }

            _loadInDemandButton = CloneButton("TradeShuttlePlanner_LoadInDemand", "LOAD IN-DEMAND",
                (b, c) => LoadItems(inDemand: true));
            _loadBestButton = CloneButton("TradeShuttlePlanner_LoadBest", "LOAD BEST",
                (b, c) => LoadItems(inDemand: false));
        }

        private CommonButton CloneButton(string name, string caption, Action<CommonButton, int> onClick)
        {
            var go = UnityEngine.Object.Instantiate(_buttonTemplate.gameObject, _listRoot, false);
            go.name = name;
            go.transform.SetAsLastSibling();
            var button = go.GetComponent<CommonButton>();
            if (button == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            SetCaption(button, caption);
            button.OnClick += (b, c) => Safe(() => onClick(b, c), "button click");
            return button;
        }

        private void OnPlannerTabClicked(IconTabButton button, int clickCount)
        {
            Safe(ShowPlanner, "tab click");
        }

        private void ShowPlanner()
        {
            if (_cargoRoot != null) { _cargoRoot.SetActive(false); }
            if (_lastRoot != null) { _lastRoot.SetActive(false); }
            if (_cargoButton != null) { _cargoButton.SetState(IconTabButtonState.Inactive); }
            if (_lastButton != null) { _lastButton.SetState(IconTabButtonState.Inactive); }
            if (_plannerButton != null) { _plannerButton.SetState(IconTabButtonState.Active); }
            if (_pageRoot != null) { _pageRoot.SetActive(true); }   // OnEnable does the refresh
        }

        public void HideForVanillaPage()
        {
            if (_pageRoot != null && _pageRoot.activeSelf) { _pageRoot.SetActive(false); }
            if (_plannerButton != null) { _plannerButton.SetState(IconTabButtonState.Inactive); }
        }

        private void OnEnable()
        {
            _lastTarget = "\0";
            _lastHoldHash = 0;
            Safe(RefreshAll, "refresh on enable");
        }

        private void Update()
        {
            if (_screen == null) { return; }

            // Update runs every frame the tab is open. A recurring throw here would spam the log
            // and cost real framerate, so give up on the panel rather than keep failing.
            try
            {
                Tick();
                _updateFailures = 0;
            }
            catch (Exception e)
            {
                if (++_updateFailures >= 5)
                {
                    Debug.LogError("[TradeShuttlePlanner] planner tab disabled after repeated update failures: " + e);
                    enabled = false;
                }
            }
        }

        private void Tick()
        {
            var target = ShoppingList.TargetItemId ?? string.Empty;
            if (target != _lastTarget)
            {
                _lastTarget = target;
                Safe(() =>
                {
                    RefreshHeader();
                    RefreshStations();
                    RefreshPreview();
                }, "target change");
                return;
            }

            var hash = HoldHash();
            if (hash != _lastHoldHash)
            {
                _lastHoldHash = hash;
                Safe(RefreshPreview, "hold change");
            }
        }

        private void RefreshAll()
        {
            _lastTarget = ShoppingList.TargetItemId ?? string.Empty;
            RefreshHeader();
            RefreshStations();
            RefreshPreview();
            _lastHoldHash = HoldHash();
        }

        private void RefreshHeader()
        {
            if (_headerText == null) { return; }
            var target = ShoppingList.TargetItemId;
            if (string.IsNullOrEmpty(target))
            {
                _headerText.text = "PLANNER\nNo target good yet.\nOpen the stock market and click the good you want.";
            }
            else
            {
                _headerText.text = "PLANNER\nTarget: " + Tint(TargetColor, Names.Item(target)) +
                                   "\nPick a different good in the stock market to change it.";
            }
        }

        private void RefreshStations()
        {
            if (_listRoot == null) { return; }
            foreach (var row in _rowPool) { if (row != null) { row.gameObject.SetActive(false); } }

            var target = ShoppingList.TargetItemId;
            if (string.IsNullOrEmpty(target)) { return; }
            if (!Resolve(out var prog, out var fac, out var prices, out _, out var stations, out var travel, out _, out _))
            {
                return;
            }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept == null) { return; }

            var proxy = dept.Mode == TradeShuttleMode.Barter
                ? prog.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                : null;
            var categoryId = Fetch.CategoryForItem(target);
            var allowed = BuyOrder.ClassesForCategory(categoryId);

            var rows = new List<StationRow>();
            foreach (var station in stations.Values)
            {
                if (string.IsNullOrEmpty(station.SpaceObjectId)) { continue; }
                if (station.SpaceObjectId == travel.CurrentSpaceObject) { continue; }
                var faction = fac.Get(station.OwnerFactionId);
                if (faction == null || !Fetch.Eligible(prog, faction, station, proxy)) { continue; }

                var stock = station.InternalStorage.Items.Where(i => i.Id == target).Sum(i => (int)i.StackCount);
                if (stock <= 0) { continue; }

                var buyPrice = TradeSystem.GetItemBuyPrice(prog, faction, station, prices, target);
                var ordered = BuyOrder.Candidates(prog, faction, station, prices, allowed);
                BuyOrder.Rank(ordered, target, out var rank, out var field, out _);

                rows.Add(new StationRow
                {
                    SpaceObjectId = station.SpaceObjectId,
                    Orbit = Names.Orbit(station.SpaceObjectId),
                    Faction = Names.Faction(station.OwnerFactionId),
                    Stock = stock,
                    BuyPrice = buyPrice,
                    Rank = rank,
                    Field = field,
                    Reputation = faction.PlayerReputation
                });
            }

            rows = rows.OrderBy(r => r.BuyPrice == 0 ? int.MaxValue : r.BuyPrice)
                       .ThenBy(r => r.Rank == 0 ? int.MaxValue : r.Rank)
                       .ToList();

            var index = 0;
            foreach (var data in rows)
            {
                var button = GetRow(index++);
                if (button == null) { continue; }
                var marker = data.SpaceObjectId == _selectedSpaceObjectId ? "> " : "  ";
                var rankText = data.Rank > 0 ? "#" + data.Rank + "/" + data.Field : "#-";
                var line = marker + data.Orbit + "  |  " + data.Faction +
                           "   x" + data.Stock + "   buy " + data.BuyPrice.ToString("N0") +
                           "   " + rankText;
                SetCaption(button, Tint(RepHex(data.Reputation), line));

                var pr = button.GetComponent<PlannerRow>();
                if (pr != null) { pr.SpaceObjectId = data.SpaceObjectId; }
                button.gameObject.SetActive(true);
            }

            if (rows.Count == 0)
            {
                var button = GetRow(index);
                if (button != null)
                {
                    var pr = button.GetComponent<PlannerRow>();
                    if (pr != null) { pr.SpaceObjectId = null; }
                    SetCaption(button, "No reachable station is stocking " + Names.Item(target) + " right now.");
                    button.gameObject.SetActive(true);
                }
            }
        }

        private CommonButton GetRow(int index)
        {
            if (index < _rowPool.Count) { return _rowPool[index]; }
            if (_buttonTemplate == null || _listRoot == null) { return null; }

            var go = UnityEngine.Object.Instantiate(_buttonTemplate.gameObject, _listRoot, false);
            go.name = "TradeShuttlePlanner_Row";
            go.transform.SetAsLastSibling();
            var button = go.GetComponent<CommonButton>();
            if (button == null) { UnityEngine.Object.Destroy(go); return null; }

            var pr = go.AddComponent<PlannerRow>();
            // Subscribe once per pooled button so rebuilds never stack handlers.
            button.OnClick += (b, c) => Safe(() => OnRowClicked(pr), "row click");
            _rowPool.Add(button);
            return button;
        }

        private void OnRowClicked(PlannerRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.SpaceObjectId)) { return; }
            _selectedSpaceObjectId = row.SpaceObjectId;
            RefreshStations();
            RefreshPreview();
            _loadBestButton?.SetInteractable(true);
        }

        private void RefreshPreview()
        {
            if (_previewText == null) { return; }
            if (string.IsNullOrEmpty(_selectedSpaceObjectId))
            {
                _previewText.text = "Select a destination below to preview the return.";
                _loadInDemandButton?.SetInteractable(false);
                _loadBestButton?.SetInteractable(false);
                return;
            }
            if (!Resolve(out var prog, out var fac, out var prices, out var diff, out var stations, out _, out _, out _))
            {
                _previewText.text = "Game state not ready.";
                return;
            }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null)
            {
                _previewText.text = "No trade shuttle on the ship yet.";
                return;
            }

            var target = ShoppingList.TargetItemId;
            var destStations = stations.Values.Where(s => s.SpaceObjectId == _selectedSpaceObjectId).ToList();
            var originals = new HashSet<BasePickupItem>(dept.TradeShuttleStorage.Items);

            var savedCategory = dept.SelectedBarterCategoryId;
            var category = Fetch.CategoryForItem(target);
            if (dept.Mode == TradeShuttleMode.Barter && !string.IsNullOrEmpty(category))
            {
                dept.SelectedBarterCategoryId = category;
            }
            List<BasePickupItem> returning;
            try { returning = Fetch.Simulate(prog, dept, fac, prices, diff, destStations); }
            finally { dept.SelectedBarterCategoryId = savedCategory; }

            if (returning == null)
            {
                _previewText.text = "Return simulation is unavailable in this build.";
                return;
            }

            var cargoValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, dept.TradeShuttleStorage.Items);
            var returnValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, returning);
            var profit = cargoValue > 0 ? Mathf.RoundToInt(returnValue * 100f / cargoValue) : 0;
            var targetCount = string.IsNullOrEmpty(target)
                ? 0
                : returning.Where(i => i.Id == target && !originals.Contains(i)).Sum(i => (int)i.StackCount);

            var gained = new Dictionary<string, int>();
            foreach (var item in returning)
            {
                if (originals.Contains(item)) { continue; }
                gained.TryGetValue(item.Id, out var n);
                gained[item.Id] = n + item.StackCount;
            }

            var sb = new StringBuilder();
            sb.AppendLine(Names.Orbit(_selectedSpaceObjectId));
            if (!string.IsNullOrEmpty(target))
            {
                sb.AppendLine("Brings back " + Tint(targetCount > 0 ? GoodColor : BadColor,
                    targetCount + "x " + Names.Item(target)));
            }
            sb.AppendLine("Return value " + returnValue.ToString("N0") +
                          "   Profit " + Tint(profit >= 100 ? GoodColor : BadColor, profit + "%"));
            if (gained.Count > 0)
            {
                sb.AppendLine("Coming home:");
                foreach (var kv in gained.OrderByDescending(k => k.Value).Take(8))
                {
                    sb.AppendLine("  " + kv.Value + "x " + Names.Item(kv.Key));
                }
            }
            else
            {
                sb.AppendLine(Tint(BadColor, "Nothing comes back with this hold."));
            }
            _previewText.text = sb.ToString();

            var canLoad = destStations.Count > 0 && !dept.ShuttleInMove;
            _loadBestButton?.SetInteractable(canLoad);
            _loadInDemandButton?.SetInteractable(canLoad && !string.IsNullOrEmpty(target));
        }

        private void LoadItems(bool inDemand)
        {
            if (string.IsNullOrEmpty(_selectedSpaceObjectId))
            {
                if (_previewText != null) { _previewText.text = "Select a destination first."; }
                return;
            }
            if (!Resolve(out var prog, out var fac, out var prices, out var diff, out var stations, out _, out var cargo, out _))
            {
                return;
            }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null || dept.ShuttleInMove) { return; }

            var target = ShoppingList.TargetItemId;
            if (inDemand && string.IsNullOrEmpty(target))
            {
                if (_previewText != null) { _previewText.text = "Pick a target good before loading in-demand cargo."; }
                return;
            }

            var proxy = dept.Mode == TradeShuttleMode.Barter
                ? prog.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                : null;
            var destStations = stations.Values.Where(s => s.SpaceObjectId == _selectedSpaceObjectId).ToList();
            var eligible = destStations
                .Select(s => new { S = s, F = fac.Get(s.OwnerFactionId) })
                .Where(x => x.F != null && Fetch.Eligible(prog, x.F, x.S, proxy))
                .ToList();
            if (eligible.Count == 0) { return; }

            var cfg = PlannerConfig.Current;

            // Only ever load cargo the destination actually consumes, never quest or kept items.
            var candidates = new List<CargoPick>();
            foreach (var storage in cargo.ShipCargo)
            {
                foreach (var item in storage.Items.ToList())
                {
                    if (ItemInteractionSystem.IsQuestItem(item)) { continue; }
                    if (Fetch.IsKept(item, cfg)) { continue; }
                    if (!eligible.Any(x => TradeSystem.IsValidItem(x.F, x.S, item.Id))) { continue; }
                    candidates.Add(new CargoPick { Item = item, Unit = prices.GetPrice(item.Id) });
                }
            }

            var ordered = inDemand
                ? candidates.OrderBy(c => c.Unit).ToList()   // cheapest first, spend junk before gear
                : candidates.OrderByDescending(c => c.Unit).ToList();

            var savedCategory = dept.SelectedBarterCategoryId;
            var category = Fetch.CategoryForItem(target);
            if (dept.Mode == TradeShuttleMode.Barter && !string.IsNullOrEmpty(category))
            {
                dept.SelectedBarterCategoryId = category;
            }

            var originals = new HashSet<BasePickupItem>(dept.TradeShuttleStorage.Items);
            var loaded = 0;
            foreach (var pick in ordered)
            {
                if (pick.Item.Storage == null) { continue; }
                if (!dept.TradeShuttleStorage.TryPutItem(pick.Item, CellPosition.Zero)) { continue; }
                loaded++;

                if (inDemand && loaded % 3 == 0)
                {
                    var trial = Fetch.Simulate(prog, dept, fac, prices, diff, destStations);
                    if (trial != null &&
                        trial.Where(i => i.Id == target && !originals.Contains(i)).Sum(i => (int)i.StackCount) >= 1)
                    {
                        break;
                    }
                }
            }

            // Barter keeps the target's category selected so the real send chases it; other modes
            // must leave the save field exactly as found.
            if (dept.Mode != TradeShuttleMode.Barter) { dept.SelectedBarterCategoryId = savedCategory; }

            try { _screen.RefreshView(); } catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] refresh after load: " + e.Message); }
            _lastHoldHash = HoldHash();
            RefreshPreview();
        }

        private int HoldHash()
        {
            var prog = SafeResolve<MagnumProgression>();
            var dept = prog?.GetDepartment<TradeShuttleDepartment>();
            var storage = dept?.TradeShuttleStorage;
            if (storage == null) { return 0; }
            var h = 17 + storage.Items.Count;
            foreach (var item in storage.Items)
            {
                h = h * 31 + (item.Id?.GetHashCode() ?? 0);
                h = h * 31 + item.StackCount;
            }
            return h;
        }

        private bool Resolve(
            out MagnumProgression prog, out Factions fac, out ItemsPrices prices, out Difficulty diff,
            out Stations stations, out TravelMetadata travel, out MagnumCargo cargo, out SpaceTime spaceTime)
        {
            prog = SafeResolve<MagnumProgression>();
            fac = SafeResolve<Factions>();
            prices = SafeResolve<ItemsPrices>();
            diff = SafeResolve<Difficulty>();
            stations = SafeResolve<Stations>();
            travel = SafeResolve<TravelMetadata>();
            cargo = SafeResolve<MagnumCargo>();
            spaceTime = SafeResolve<SpaceTime>();
            return prog != null && fac != null && prices != null && diff != null &&
                   stations != null && travel != null && cargo != null && spaceTime != null;
        }

        private static T SafeResolve<T>() where T : class
        {
            try { return UI.Resolve<T>(); }
            catch { return null; }
        }

        private static string RepHex(float reputation)
        {
            try { return "#" + ColorUtility.ToHtmlStringRGB(Colors.GetFactionColorByReputation(reputation)); }
            catch { return "#FFFFFF"; }
        }

        private static string Tint(string hex, string text) => "<color=" + hex + ">" + text + "</color>";

        private static void SetCaption(CommonButton button, string text)
        {
            if (button == null) { return; }
            try { button.SetRawCaption(text); } catch { }
            if (button.captionText != null) { button.captionText.text = text; }
        }

        private static void Safe(Action action, string what)
        {
            try { action(); }
            catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] " + what + ": " + e.Message); }
        }

        private sealed class StationRow
        {
            public string SpaceObjectId;
            public string Orbit;
            public string Faction;
            public int Stock;
            public int BuyPrice;
            public int Rank;
            public int Field;
            public float Reputation;
        }

        private sealed class CargoPick
        {
            public BasePickupItem Item;
            public float Unit;
        }
    }
}
