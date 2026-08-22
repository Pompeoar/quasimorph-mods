using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// The full-screen Planner window. It is a self-owned GameObject parented under
    /// <see cref="UI.ScreenRoot"/> (a mod cannot register a real [UIView], so we mirror where the
    /// game keeps its views and toggle ours with SetActive), drawn above the trade shuttle screen
    /// by its own high-sorting Canvas.
    ///
    /// It walks the three-step flow the player asked for: pick a good from an icon grid filtered by
    /// barter category, pick which orbit to buy it from, then load and review the hold with a live
    /// expected-return readout. Every widget is cloned from an existing game widget so it inherits
    /// the native sprites, fonts and materials rather than being drawn from raw primitives.
    ///
    /// Nothing here is allowed to throw into a per-frame Update: Tick is wrapped and a repeated
    /// failure disables the window rather than spamming the log and killing the framerate.
    /// </summary>
    internal sealed class PlannerWindow : MonoBehaviour
    {
        private enum Step { Goods, Stations, Load }

        private TradeShuttleScreen _screen;
        private CommonButton _buttonTemplate;
        private TMP_FontAsset _font;

        private Step _step = Step.Goods;
        private int _updateFailures;
        private int _lastHoldHash;

        // Step 1 state
        private string _categoryFilter = "\0ALL";   // sentinel meaning "no category filter"
        private int _goodsPage;
        private const int GoodsPerPage = 40;
        private const int GoodsColumns = 8;
        private List<GoodEntry> _goodsCache;
        private string _highlightItemId;

        // Step 2/3 state
        private string _selectedItemId;
        private string _selectedSpaceObjectId;

        // Widget roots
        private GameObject _goodsRoot;
        private GameObject _stationsRoot;
        private GameObject _loadRoot;
        private TextMeshProUGUI _header;

        private Transform _categoryBar;
        private Transform _goodsGrid;
        private TextMeshProUGUI _goodsStatus;
        private CommonButton _prevButton;
        private CommonButton _nextButton;
        private readonly List<Cell> _goodsCells = new List<Cell>();
        private readonly List<CommonButton> _categoryTabs = new List<CommonButton>();

        private TextMeshProUGUI _stationsHeader;
        private Transform _stationsList;
        private readonly List<CommonButton> _stationRows = new List<CommonButton>();

        private Transform _holdGrid;
        private Transform _availGrid;
        private TextMeshProUGUI _loadStatus;
        private TextMeshProUGUI _preview;
        private CommonButton _loadInDemandButton;
        private CommonButton _loadBestButton;
        private readonly List<Cell> _holdCells = new List<Cell>();
        private readonly List<Cell> _availCells = new List<Cell>();
        private readonly List<BasePickupItem> _availItems = new List<BasePickupItem>();

        private const string Teal = "#5AD9D9";
        private const string Good = "#7CFF7C";
        private const string Bad = "#FF7C7C";
        private const string Dim = "#9AA6A6";

        public static PlannerWindow Create(TradeShuttleScreen screen, CommonButton buttonTemplate)
        {
            if (screen == null || buttonTemplate == null) { return null; }
            RectTransform parent;
            try { parent = UI.ScreenRoot; }
            catch { parent = null; }
            if (parent == null)
            {
                Debug.LogError("[TradeShuttlePlanner] UI.ScreenRoot unavailable; window not created.");
                return null;
            }

            var root = new GameObject("TradeShuttlePlanner_Window", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();
            Stretch(rt);
            var canvas = root.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;

            var window = root.AddComponent<PlannerWindow>();
            window._screen = screen;
            window._buttonTemplate = buttonTemplate;
            window._font = buttonTemplate.captionText != null ? buttonTemplate.captionText.font : null;
            window.BuildChrome(root.transform);
            root.SetActive(false);
            return window;
        }

        public void Open()
        {
            Safe(() =>
            {
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                _step = Step.Goods;
                _selectedItemId = null;
                _selectedSpaceObjectId = null;
                _highlightItemId = null;
                _goodsCache = null;
                _goodsPage = 0;
                if (!string.IsNullOrEmpty(ShoppingList.TargetItemId))
                {
                    // Honour a good already chosen via the stock-market / F9 flow.
                    _selectedItemId = ShoppingList.TargetItemId;
                    _step = Step.Stations;
                }
                RefreshAll();
            }, "open");
        }

        public void Close()
        {
            Safe(() => gameObject.SetActive(false), "close");
        }

        // ---- chrome ---------------------------------------------------------------------------

        private void BuildChrome(Transform root)
        {
            var backdrop = NewImage(root, "Backdrop", new Color(0f, 0f, 0f, 0.78f));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            // Teal border, dark fill: two nested images approximate the game's panel chrome without
            // needing to guess at a serialized sprite.
            var border = NewImage(root, "PanelBorder", new Color(0.22f, 0.62f, 0.62f, 1f));
            var brt = border.rectTransform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(900f, 600f);
            brt.anchoredPosition = Vector2.zero;

            var fill = NewImage(border.transform, "PanelFill", new Color(0.06f, 0.09f, 0.10f, 0.99f));
            var frt = fill.rectTransform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(3f, 3f); frt.offsetMax = new Vector2(-3f, -3f);

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(fill.transform, false);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(14f, 14f); content.offsetMax = new Vector2(-14f, -14f);

            _header = NewLabel(content, "Header", "PLANNER", 22f, Colors.White, TextAlignmentOptions.TopLeft);
            var hrt = _header.rectTransform;
            hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
            hrt.pivot = new Vector2(0f, 1f);
            hrt.anchoredPosition = new Vector2(0f, 0f);
            hrt.sizeDelta = new Vector2(0f, 30f);

            var closeButton = Widgets.CloneButton(_buttonTemplate, content, "Close");
            if (closeButton != null)
            {
                Widgets.SetCaption(closeButton, "CLOSE");
                var crt = closeButton.transform as RectTransform;
                crt.anchorMin = new Vector2(1f, 1f); crt.anchorMax = new Vector2(1f, 1f);
                crt.pivot = new Vector2(1f, 1f);
                crt.anchoredPosition = new Vector2(0f, 2f);
                crt.sizeDelta = new Vector2(90f, 26f);
                closeButton.OnClick += (b, c) => Safe(Close, "close click");
            }

            // Body area sits below the header.
            var body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>();
            body.SetParent(content, false);
            body.anchorMin = Vector2.zero; body.anchorMax = Vector2.one;
            body.offsetMin = Vector2.zero; body.offsetMax = new Vector2(0f, -38f);

            BuildGoodsStep(body);
            BuildStationsStep(body);
            BuildLoadStep(body);
        }

        private void BuildGoodsStep(Transform body)
        {
            _goodsRoot = NewPanel(body, "GoodsStep");

            _categoryBar = NewRow(_goodsRoot.transform, "Categories", 1f, 26f, 6f);

            _goodsGrid = NewGrid(_goodsRoot.transform, "GoodsGrid").transform;

            _goodsStatus = NewLabel(_goodsRoot.transform, "GoodsStatus", "", 15f, Colors.White, TextAlignmentOptions.Left);
            Anchor(_goodsStatus.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0f, 30f), new Vector2(0f, 54f));

            var nav = NewRow(_goodsRoot.transform, "GoodsNav", 0f, 26f, 8f);
            _prevButton = Widgets.CloneButton(_buttonTemplate, nav, "Prev");
            if (_prevButton != null) { Widgets.SetCaption(_prevButton, "< PREV"); FixWidth(_prevButton, 110f); _prevButton.OnClick += (b, c) => Safe(() => { _goodsPage--; RefreshGoods(); }, "prev"); }
            _nextButton = Widgets.CloneButton(_buttonTemplate, nav, "Next");
            if (_nextButton != null) { Widgets.SetCaption(_nextButton, "NEXT >"); FixWidth(_nextButton, 110f); _nextButton.OnClick += (b, c) => Safe(() => { _goodsPage++; RefreshGoods(); }, "next"); }
            Anchor((RectTransform)nav, 0f, 0f, 1f, 0f, new Vector2(0f, 0f), new Vector2(0f, 26f));
        }

        private void BuildStationsStep(Transform body)
        {
            _stationsRoot = NewPanel(body, "StationsStep");

            _stationsHeader = NewLabel(_stationsRoot.transform, "StationsHeader", "", 16f, Colors.White, TextAlignmentOptions.Left);
            Anchor(_stationsHeader.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0f, -22f), new Vector2(0f, 0f));

            var listRoot = new GameObject("StationsList", typeof(RectTransform)).GetComponent<RectTransform>();
            listRoot.SetParent(_stationsRoot.transform, false);
            listRoot.anchorMin = Vector2.zero; listRoot.anchorMax = Vector2.one;
            listRoot.offsetMin = new Vector2(0f, 32f); listRoot.offsetMax = new Vector2(0f, -28f);
            var vlg = listRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f; vlg.childForceExpandHeight = false; vlg.childControlHeight = true; vlg.childControlWidth = true; vlg.childForceExpandWidth = true;
            _stationsList = listRoot;

            var back = Widgets.CloneButton(_buttonTemplate, _stationsRoot.transform, "StationsBack");
            if (back != null)
            {
                Widgets.SetCaption(back, "< BACK");
                Anchor((RectTransform)back.transform, 0f, 0f, 0f, 0f, new Vector2(0f, 0f), new Vector2(120f, 26f));
                back.OnClick += (b, c) => Safe(() => { _step = Step.Goods; RefreshAll(); }, "stations back");
            }
        }

        private void BuildLoadStep(Transform body)
        {
            _loadRoot = NewPanel(body, "LoadStep");

            var top = NewRow(_loadRoot.transform, "LoadButtons", 0f, 28f, 8f);
            Anchor((RectTransform)top, 0f, 1f, 1f, 1f, new Vector2(0f, -28f), new Vector2(0f, 0f));
            var back = Widgets.CloneButton(_buttonTemplate, top, "LoadBack");
            if (back != null) { Widgets.SetCaption(back, "< BACK"); FixWidth(back, 110f); back.OnClick += (b, c) => Safe(() => { _step = Step.Stations; RefreshAll(); }, "load back"); }
            _loadInDemandButton = Widgets.CloneButton(_buttonTemplate, top, "LoadInDemand");
            if (_loadInDemandButton != null) { Widgets.SetCaption(_loadInDemandButton, "LOAD IN-DEMAND"); FixWidth(_loadInDemandButton, 180f); _loadInDemandButton.OnClick += (b, c) => Safe(() => LoadItems(true), "load in-demand"); }
            _loadBestButton = Widgets.CloneButton(_buttonTemplate, top, "LoadBest");
            if (_loadBestButton != null) { Widgets.SetCaption(_loadBestButton, "LOAD BEST"); FixWidth(_loadBestButton, 150f); _loadBestButton.OnClick += (b, c) => Safe(() => LoadItems(false), "load best"); }

            var holdLabel = NewLabel(_loadRoot.transform, "HoldLabel", "Shuttle hold  (click to unload)", 14f, HexColor(Teal), TextAlignmentOptions.Left);
            Anchor(holdLabel.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0f, -50f), new Vector2(0f, -32f));
            _holdGrid = NewGridAt(_loadRoot.transform, "HoldGrid", 1f, -160f, 1f, -54f).transform;

            var availLabel = NewLabel(_loadRoot.transform, "AvailLabel", "Ship cargo this station wants  (click to load)", 14f, HexColor(Teal), TextAlignmentOptions.Left);
            Anchor(availLabel.rectTransform, 0f, 1f, 1f, 1f, new Vector2(0f, -180f), new Vector2(0f, -162f));
            _availGrid = NewGridAt(_loadRoot.transform, "AvailGrid", 1f, -290f, 1f, -184f).transform;

            _preview = NewLabel(_loadRoot.transform, "Preview", "", 14f, Colors.White, TextAlignmentOptions.TopLeft);
            _preview.enableWordWrapping = true;
            Anchor(_preview.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0f, 0f), new Vector2(0f, 108f));

            _loadStatus = NewLabel(_loadRoot.transform, "LoadStatus", "", 13f, HexColor(Dim), TextAlignmentOptions.Left);
            Anchor(_loadStatus.rectTransform, 0f, 0f, 1f, 0f, new Vector2(0f, 108f), new Vector2(0f, 126f));
        }

        // ---- lifecycle ------------------------------------------------------------------------

        private void Update()
        {
            if (!gameObject.activeSelf) { return; }
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                if (_step == Step.Load)
                {
                    var hash = HoldHash();
                    if (hash != _lastHoldHash) { _lastHoldHash = hash; RefreshPreview(); }
                }
                _updateFailures = 0;
            }
            catch (Exception e)
            {
                if (++_updateFailures >= 5)
                {
                    Debug.LogError("[TradeShuttlePlanner] window disabled after repeated update failures: " + e);
                    enabled = false;
                }
            }
        }

        private void RefreshAll()
        {
            RefreshHeader();
            if (_goodsRoot != null) { _goodsRoot.SetActive(_step == Step.Goods); }
            if (_stationsRoot != null) { _stationsRoot.SetActive(_step == Step.Stations); }
            if (_loadRoot != null) { _loadRoot.SetActive(_step == Step.Load); }

            switch (_step)
            {
                case Step.Goods: RefreshCategories(); RefreshGoods(); break;
                case Step.Stations: RefreshStations(); break;
                case Step.Load: RefreshLoad(); break;
            }
        }

        private void RefreshHeader()
        {
            if (_header == null) { return; }
            switch (_step)
            {
                case Step.Goods:
                    _header.text = "PLANNER  " + Tint(Dim, "/  1. choose the good");
                    break;
                case Step.Stations:
                    _header.text = "PLANNER  " + Tint(Dim, "/  2. choose where to buy  ") + Tint(Teal, Names.Item(_selectedItemId));
                    break;
                case Step.Load:
                    _header.text = "PLANNER  " + Tint(Dim, "/  3. load and review  ") + Tint(Teal, Names.Orbit(_selectedSpaceObjectId));
                    break;
            }
        }

        // ---- step 1: goods --------------------------------------------------------------------

        private void RefreshCategories()
        {
            if (_categoryBar == null) { return; }

            var cats = new List<(string Id, string Label)> { ("\0ALL", "All") };
            try
            {
                foreach (var rec in Data.TradeShuttleBarterCategories.Records)
                {
                    if (rec == null || string.IsNullOrEmpty(rec.Id)) { continue; }
                    cats.Add((rec.Id, Names.Category(rec.Id)));
                }
            }
            catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] categories: " + e.Message); }

            for (var i = 0; i < cats.Count; i++)
            {
                var tab = i < _categoryTabs.Count ? _categoryTabs[i] : null;
                if (tab == null)
                {
                    tab = Widgets.CloneButton(_buttonTemplate, _categoryBar, "Cat" + i);
                    if (tab == null) { continue; }
                    _categoryTabs.Add(tab);
                }
                var cat = cats[i];
                var selected = cat.Id == _categoryFilter;
                Widgets.SetCaption(tab, selected ? Tint(Teal, cat.Label.ToUpperInvariant()) : cat.Label.ToUpperInvariant());
                tab.gameObject.SetActive(true);
                var captured = cat.Id;
                Widgets.SetClick(tab, () => Safe(() => { _categoryFilter = captured; _goodsPage = 0; RefreshGoods(); RefreshCategories(); }, "category"));
            }
            for (var i = cats.Count; i < _categoryTabs.Count; i++)
            {
                if (_categoryTabs[i] != null) { _categoryTabs[i].gameObject.SetActive(false); }
            }
        }

        private void RefreshGoods()
        {
            if (_goodsGrid == null) { return; }
            if (_goodsCache == null) { _goodsCache = BuildGoods(); }

            var filtered = _categoryFilter == "\0ALL"
                ? _goodsCache
                : _goodsCache.Where(g => g.CategoryId == _categoryFilter).ToList();

            var pageCount = Math.Max(1, (filtered.Count + GoodsPerPage - 1) / GoodsPerPage);
            if (_goodsPage < 0) { _goodsPage = 0; }
            if (_goodsPage >= pageCount) { _goodsPage = pageCount - 1; }

            var start = _goodsPage * GoodsPerPage;
            var shown = 0;
            for (var i = start; i < filtered.Count && shown < GoodsPerPage; i++, shown++)
            {
                var g = filtered[i];
                var cell = GetGoodsCell(shown);
                if (cell == null) { shown--; continue; }
                cell.Icon.sprite = g.Icon;
                cell.Icon.enabled = g.Icon != null;
                cell.Count.text = g.TotalStock > 1 ? g.TotalStock.ToString() : string.Empty;
                if (cell.Highlight != null) { cell.Highlight.enabled = g.ItemId == _highlightItemId; }
                var itemId = g.ItemId;
                var name = g.Name;
                cell.Input.OnEnter = () => { if (_goodsStatus != null) { _goodsStatus.text = name + "   x" + g.TotalStock + " in " + g.OrbitCount + " orbit" + (g.OrbitCount == 1 ? "" : "s"); } };
                cell.Input.OnClick = () => Safe(() => SelectGood(itemId), "select good");
                cell.SetActive(true);
            }
            for (var i = shown; i < _goodsCells.Count; i++) { _goodsCells[i].SetActive(false); }

            if (_goodsStatus != null)
            {
                _goodsStatus.text = filtered.Count == 0
                    ? "No reachable station is stocking anything in this category right now."
                    : "Hover a good for its name, click to choose it.   Page " + (_goodsPage + 1) + "/" + pageCount + "   (" + filtered.Count + " goods)";
            }
            _prevButton?.SetInteractable(_goodsPage > 0);
            _nextButton?.SetInteractable(_goodsPage < pageCount - 1);
        }

        private Cell GetGoodsCell(int index)
        {
            if (index < _goodsCells.Count) { return _goodsCells[index]; }
            var cell = Widgets.CreateCell(_goodsGrid, "Good" + index, 46f);
            if (cell != null) { _goodsCells.Add(cell); }
            return cell;
        }

        private void SelectGood(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) { return; }
            _selectedItemId = itemId;
            _highlightItemId = itemId;
            ShoppingList.Set(itemId);   // keep the F9 flow and the window pointed at the same good
            _selectedSpaceObjectId = null;
            _step = Step.Stations;
            RefreshAll();
        }

        private List<GoodEntry> BuildGoods()
        {
            var result = new List<GoodEntry>();
            if (!Resolve(out var prog, out var fac, out var prices, out _, out var stations, out var travel, out _, out _)) { return result; }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            var proxy = dept != null && dept.Mode == TradeShuttleMode.Barter
                ? prog.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                : null;

            var byId = new Dictionary<string, GoodEntry>();
            foreach (var station in stations.Values)
            {
                if (string.IsNullOrEmpty(station.SpaceObjectId)) { continue; }
                if (station.SpaceObjectId == travel.CurrentSpaceObject) { continue; }
                if (station.InternalStorage == null) { continue; }
                var faction = fac.Get(station.OwnerFactionId);
                if (faction == null || !Fetch.Eligible(prog, faction, station, proxy)) { continue; }

                foreach (var item in station.InternalStorage.Items)
                {
                    if (string.IsNullOrEmpty(item.Id)) { continue; }
                    if (!byId.TryGetValue(item.Id, out var entry))
                    {
                        entry = new GoodEntry
                        {
                            ItemId = item.Id,
                            Name = Names.Item(item.Id),
                            CategoryId = Fetch.CategoryForItem(item.Id),
                            Icon = Widgets.ResolveIcon(item)
                        };
                        byId[item.Id] = entry;
                    }
                    entry.TotalStock += item.StackCount;
                    entry.OrbitSet.Add(station.SpaceObjectId);
                }
            }

            foreach (var entry in byId.Values)
            {
                entry.OrbitCount = entry.OrbitSet.Count;
                result.Add(entry);
            }
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // ---- step 2: stations -----------------------------------------------------------------

        private void RefreshStations()
        {
            if (_stationsList == null) { return; }
            foreach (var row in _stationRows) { if (row != null) { row.gameObject.SetActive(false); } }

            if (_stationsHeader != null)
            {
                _stationsHeader.text = "Where to buy " + Tint(Teal, Names.Item(_selectedItemId)) +
                    "   " + Tint(Dim, "(sorted by stock, coloured by your reputation)");
            }

            if (string.IsNullOrEmpty(_selectedItemId)) { return; }
            if (!Resolve(out var prog, out var fac, out var prices, out _, out var stations, out var travel, out _, out _)) { return; }

            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            var proxy = dept != null && dept.Mode == TradeShuttleMode.Barter
                ? prog.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                : null;
            var allowed = BuyOrder.ClassesForCategory(Fetch.CategoryForItem(_selectedItemId));

            var rows = new List<StationRow>();
            foreach (var station in stations.Values)
            {
                if (string.IsNullOrEmpty(station.SpaceObjectId)) { continue; }
                if (station.SpaceObjectId == travel.CurrentSpaceObject) { continue; }
                if (station.InternalStorage == null) { continue; }
                var faction = fac.Get(station.OwnerFactionId);
                if (faction == null || !Fetch.Eligible(prog, faction, station, proxy)) { continue; }
                var stock = station.InternalStorage.Items.Where(i => i.Id == _selectedItemId).Sum(i => (int)i.StackCount);
                if (stock <= 0) { continue; }

                var buyPrice = TradeSystem.GetItemBuyPrice(prog, faction, station, prices, _selectedItemId);
                var ordered = BuyOrder.Candidates(prog, faction, station, prices, allowed);
                BuyOrder.Rank(ordered, _selectedItemId, out var rank, out var field, out _);
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

            rows = rows.OrderByDescending(r => r.Stock).ThenBy(r => r.BuyPrice == 0 ? int.MaxValue : r.BuyPrice).ToList();

            var i2 = 0;
            foreach (var data in rows)
            {
                var button = GetStationRow(i2++);
                if (button == null) { i2--; continue; }
                var rankText = data.Rank > 0 ? "#" + data.Rank + "/" + data.Field : "#-";
                var line = data.Orbit + "   " + data.Faction + "    x" + data.Stock +
                           "    buy " + data.BuyPrice.ToString("N0") + "    rank " + rankText;
                Widgets.SetCaption(button, Tint(RepHex(data.Reputation), line));
                var sid = data.SpaceObjectId;
                Widgets.SetClick(button, () => Safe(() => { _selectedSpaceObjectId = sid; _step = Step.Load; RefreshAll(); }, "pick station"));
                button.gameObject.SetActive(true);
            }

            if (rows.Count == 0)
            {
                var button = GetStationRow(0);
                if (button != null)
                {
                    Widgets.SetCaption(button, "No reachable station is stocking " + Names.Item(_selectedItemId) + " right now.");
                    Widgets.SetClick(button, null);
                    button.gameObject.SetActive(true);
                }
            }
        }

        private CommonButton GetStationRow(int index)
        {
            if (index < _stationRows.Count) { return _stationRows[index]; }
            var button = Widgets.CloneButton(_buttonTemplate, _stationsList, "Station" + index);
            if (button == null) { return null; }
            var le = button.gameObject.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24f; le.preferredHeight = 24f;
            _stationRows.Add(button);
            return button;
        }

        // ---- step 3: load ---------------------------------------------------------------------

        private void RefreshLoad()
        {
            RefreshHoldGrid();
            RefreshAvailGrid();
            RefreshPreview();
            _lastHoldHash = HoldHash();
        }

        private void RefreshHoldGrid()
        {
            if (_holdGrid == null) { return; }
            var prog = SafeResolve<MagnumProgression>();
            var dept = prog?.GetDepartment<TradeShuttleDepartment>();
            var storage = dept?.TradeShuttleStorage;
            var items = storage != null ? storage.Items.ToList() : new List<BasePickupItem>();

            var shown = 0;
            foreach (var item in items)
            {
                var cell = GetCell(_holdCells, _holdGrid, "Hold", shown);
                if (cell == null) { continue; }
                BindItemCell(cell, item, () => UnloadOne(item));
                shown++;
            }
            for (var i = shown; i < _holdCells.Count; i++) { _holdCells[i].SetActive(false); }
        }

        private void RefreshAvailGrid()
        {
            if (_availGrid == null) { return; }
            _availItems.Clear();

            if (Resolve(out var prog, out var fac, out var prices, out _, out var stations, out _, out var cargo, out _))
            {
                var dept = prog.GetDepartment<TradeShuttleDepartment>();
                var proxy = dept != null && dept.Mode == TradeShuttleMode.Barter
                    ? prog.GetDepartment<ProxyCorpDepartment>()?.ProxyFactionId
                    : null;
                var destStations = stations.Values.Where(s => s.SpaceObjectId == _selectedSpaceObjectId).ToList();
                var eligible = destStations
                    .Select(s => new { S = s, F = fac.Get(s.OwnerFactionId) })
                    .Where(x => x.F != null && Fetch.Eligible(prog, x.F, x.S, proxy))
                    .ToList();
                var cfg = PlannerConfig.Current;

                if (cargo != null)
                {
                    foreach (var s in cargo.ShipCargo)
                    {
                        foreach (var item in s.Items)
                        {
                            if (ItemInteractionSystem.IsQuestItem(item)) { continue; }
                            if (Fetch.IsKept(item, cfg)) { continue; }
                            if (!eligible.Any(x => TradeSystem.IsValidItem(x.F, x.S, item.Id))) { continue; }
                            _availItems.Add(item);
                        }
                    }
                }
                _availItems.Sort((a, b) => prices.GetPrice(a.Id).CompareTo(prices.GetPrice(b.Id)));
            }

            var shown = 0;
            foreach (var item in _availItems)
            {
                var cell = GetCell(_availCells, _availGrid, "Avail", shown);
                if (cell == null) { continue; }
                BindItemCell(cell, item, () => LoadOne(item));
                shown++;
            }
            for (var i = shown; i < _availCells.Count; i++) { _availCells[i].SetActive(false); }
        }

        private void BindItemCell(Cell cell, BasePickupItem item, Action onClick)
        {
            cell.Icon.sprite = Widgets.ResolveIcon(item);
            cell.Icon.enabled = cell.Icon.sprite != null;
            cell.Count.text = item.StackCount > 1 ? item.StackCount.ToString() : string.Empty;
            var name = Names.Item(item.Id);
            cell.Input.OnEnter = () => { if (_loadStatus != null) { _loadStatus.text = name; } };
            cell.Input.OnClick = () => Safe(onClick, "cell action");
            cell.SetActive(true);
        }

        private Cell GetCell(List<Cell> pool, Transform parent, string prefix, int index)
        {
            if (index < pool.Count) { return pool[index]; }
            var cell = Widgets.CreateCell(parent, prefix + index, 40f);
            if (cell != null) { pool.Add(cell); }
            return cell;
        }

        private void UnloadOne(BasePickupItem item)
        {
            if (item == null || ItemInteractionSystem.IsQuestItem(item)) { return; }
            if (!Resolve(out var prog, out _, out _, out _, out _, out _, out var cargo, out var spaceTime)) { return; }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null || dept.ShuttleInMove) { return; }
            if (item.Storage == null) { return; }
            item.Storage.Remove(item);
            item.Storage = null;
            MagnumCargoSystem.AddCargo(cargo, spaceTime, item, null, splittedItem: false, tabFilter: true);
            dept.CheckShuttleArrived();
            AfterHoldChange();
        }

        private void LoadOne(BasePickupItem item)
        {
            if (item == null || ItemInteractionSystem.IsQuestItem(item)) { return; }
            if (!Resolve(out var prog, out _, out _, out _, out _, out _, out _, out _)) { return; }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null || dept.ShuttleInMove) { return; }
            if (item.Storage == null) { return; }
            if (!dept.TradeShuttleStorage.TryPutItem(item, CellPosition.Zero)) { return; }
            AfterHoldChange();
        }

        private void LoadItems(bool inDemand)
        {
            if (string.IsNullOrEmpty(_selectedSpaceObjectId)) { return; }
            if (!Resolve(out var prog, out var fac, out var prices, out var diff, out var stations, out _, out var cargo, out _)) { return; }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null || dept.ShuttleInMove) { return; }

            var target = _selectedItemId;
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
                ? candidates.OrderBy(c => c.Unit).ToList()
                : candidates.OrderByDescending(c => c.Unit).ToList();

            var savedCategory = dept.SelectedBarterCategoryId;
            var category = Fetch.CategoryForItem(target);
            if (dept.Mode == TradeShuttleMode.Barter && !string.IsNullOrEmpty(category)) { dept.SelectedBarterCategoryId = category; }

            var originals = new HashSet<BasePickupItem>(dept.TradeShuttleStorage.Items);
            var loaded = 0;
            foreach (var pick in ordered)
            {
                if (pick.Item.Storage == null) { continue; }
                if (!dept.TradeShuttleStorage.TryPutItem(pick.Item, CellPosition.Zero)) { continue; }
                loaded++;
                if (inDemand && !string.IsNullOrEmpty(target) && loaded % 3 == 0)
                {
                    var trial = Fetch.Simulate(prog, dept, fac, prices, diff, destStations);
                    if (trial != null && trial.Where(i => i.Id == target && !originals.Contains(i)).Sum(i => (int)i.StackCount) >= 1) { break; }
                }
            }

            if (dept.Mode != TradeShuttleMode.Barter) { dept.SelectedBarterCategoryId = savedCategory; }
            AfterHoldChange();
        }

        private void AfterHoldChange()
        {
            try { _screen.RefreshView(); } catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] refresh screen: " + e.Message); }
            RefreshHoldGrid();
            RefreshAvailGrid();
            RefreshPreview();
            _lastHoldHash = HoldHash();
        }

        private void RefreshPreview()
        {
            if (_preview == null) { return; }
            if (string.IsNullOrEmpty(_selectedSpaceObjectId)) { _preview.text = ""; return; }
            if (!Resolve(out var prog, out var fac, out var prices, out var diff, out var stations, out _, out _, out _)) { _preview.text = "Game state not ready."; return; }
            var dept = prog.GetDepartment<TradeShuttleDepartment>();
            if (dept?.TradeShuttleStorage == null) { _preview.text = "No trade shuttle on the ship yet."; return; }

            var target = _selectedItemId;
            var destStations = stations.Values.Where(s => s.SpaceObjectId == _selectedSpaceObjectId).ToList();
            var originals = new HashSet<BasePickupItem>(dept.TradeShuttleStorage.Items);

            var savedCategory = dept.SelectedBarterCategoryId;
            var category = Fetch.CategoryForItem(target);
            if (dept.Mode == TradeShuttleMode.Barter && !string.IsNullOrEmpty(category)) { dept.SelectedBarterCategoryId = category; }
            List<BasePickupItem> returning;
            try { returning = Fetch.Simulate(prog, dept, fac, prices, diff, destStations); }
            finally { dept.SelectedBarterCategoryId = savedCategory; }

            if (returning == null) { _preview.text = "Return simulation is unavailable in this build."; return; }

            var cargoValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, dept.TradeShuttleStorage.Items);
            var returnValue = TradeSystem.GetTradeShuttleCargoWorldPrice(prices, returning);
            var profit = cargoValue > 0 ? Mathf.RoundToInt(returnValue * 100f / cargoValue) : 0;
            var targetCount = string.IsNullOrEmpty(target) ? 0 : returning.Where(i => i.Id == target && !originals.Contains(i)).Sum(i => (int)i.StackCount);

            var gained = new Dictionary<string, int>();
            foreach (var item in returning)
            {
                if (originals.Contains(item)) { continue; }
                gained.TryGetValue(item.Id, out var n);
                gained[item.Id] = n + item.StackCount;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(target))
            {
                sb.AppendLine("Brings back " + Tint(targetCount > 0 ? Good : Bad, targetCount + "x " + Names.Item(target)));
            }
            sb.AppendLine("Return value " + returnValue.ToString("N0") + "    Profit " + Tint(profit >= 100 ? Good : Bad, profit + "%") +
                          "    " + Tint(Dim, "(100% is break-even)"));
            if (gained.Count > 0)
            {
                sb.Append("Coming home: ");
                sb.Append(string.Join(",  ", gained.OrderByDescending(k => k.Value).Take(8).Select(kv => kv.Value + "x " + Names.Item(kv.Key))));
            }
            else
            {
                sb.Append(Tint(Bad, "Nothing comes back with this hold."));
            }
            _preview.text = sb.ToString();

            var canLoad = destStations.Count > 0 && !dept.ShuttleInMove;
            _loadBestButton?.SetInteractable(canLoad);
            _loadInDemandButton?.SetInteractable(canLoad && !string.IsNullOrEmpty(target));
        }

        private int HoldHash()
        {
            var prog = SafeResolve<MagnumProgression>();
            var storage = prog?.GetDepartment<TradeShuttleDepartment>()?.TradeShuttleStorage;
            if (storage == null) { return 0; }
            var h = 17 + storage.Items.Count;
            foreach (var item in storage.Items)
            {
                h = h * 31 + (item.Id?.GetHashCode() ?? 0);
                h = h * 31 + item.StackCount;
            }
            return h;
        }

        // ---- state helpers --------------------------------------------------------------------

        private bool Resolve(out MagnumProgression prog, out Factions fac, out ItemsPrices prices, out Difficulty diff,
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

        private static Color HexColor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }

        private static string Tint(string hex, string text) => "<color=" + hex + ">" + text + "</color>";

        private void Safe(Action action, string what)
        {
            try { action(); }
            catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] " + what + ": " + e.Message); }
        }

        // ---- tiny UI factory ------------------------------------------------------------------

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        private static void Anchor(RectTransform rt, float axMin, float ayMin, float axMax, float ayMax, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = new Vector2(axMin, ayMin);
            rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
        }

        private GameObject NewPanel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            Stretch(rt);
            return go;
        }

        private Image NewImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private TextMeshProUGUI NewLabel(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) { tmp.font = _font; }
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.richText = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        private RectTransform NewRow(Transform parent, string name, float pivotY, float height, float spacing)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = spacing;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            return rt;
        }

        private GameObject NewGrid(Transform parent, string name)
        {
            return NewGridAt(parent, name, 1f, -170f, 1f, -32f);
        }

        private GameObject NewGridAt(Transform parent, string name, float axMax, float offMinY, float ayMax, float offMaxY)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(0f, offMinY);
            rt.offsetMax = new Vector2(0f, offMaxY);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(46f, 46f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GoodsColumns;
            return go;
        }

        private static void FixWidth(CommonButton button, float width)
        {
            if (button == null) { return; }
            var le = button.gameObject.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width; le.preferredWidth = width; le.minHeight = 26f; le.preferredHeight = 26f;
        }

        private sealed class GoodEntry
        {
            public string ItemId;
            public string Name;
            public string CategoryId;
            public Sprite Icon;
            public int TotalStock;
            public int OrbitCount;
            public readonly HashSet<string> OrbitSet = new HashSet<string>();
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
