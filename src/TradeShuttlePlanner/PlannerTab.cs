using System;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// Injects a third "Planner" tab button into the vanilla Trade Shuttle screen. Unlike the old
    /// build, the tab no longer renders an inline page; clicking it opens the full-screen
    /// <see cref="PlannerWindow"/> overlay. The button is a clone of the last-trip-report tab so it
    /// inherits the native tab sprite; its glyph is sanitised on the clone.
    ///
    /// Everything is wrapped so a null field or a UI exception logs once and no-ops rather than
    /// throwing, which on a screen built once at startup would otherwise disable the tab forever
    /// with a wall of errors.
    /// </summary>
    internal static class PlannerTab
    {
        internal static PlannerWindow Window;
        private static IconTabButton _tabButton;
        private static bool _buildFailedLogged;

        [HarmonyPatch(typeof(TradeShuttleScreen), "Awake")]
        internal static class AwakePatch
        {
            private static void Postfix(TradeShuttleScreen __instance)
            {
                try
                {
                    // Screens are instantiated once at startup, so build the tab once.
                    if (_tabButton != null) { return; }
                    Build(__instance);
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

        private static void Build(TradeShuttleScreen screen)
        {
            var t = Traverse.Create(screen);
            var reportButton = t.Field("_lastTripReportPageButton").GetValue<IconTabButton>();
            var unloadButton = t.Field("_unloadAllButton").GetValue<CommonButton>();
            if (reportButton == null || unloadButton == null)
            {
                Debug.LogError("[TradeShuttlePlanner] required screen field null; planner tab not built.");
                return;
            }

            // Clone the report tab button for a native-looking third tab. Clones lose event
            // subscriptions, so its OnClick starts empty and is ours to own.
            var tabGo = UnityEngine.Object.Instantiate(reportButton.gameObject, reportButton.transform.parent, false);
            tabGo.name = "TradeShuttlePlanner_TabButton";
            tabGo.transform.SetAsLastSibling();
            Widgets.Sanitize(tabGo);   // strip any stray hotkey glyph on the clone
            _tabButton = tabGo.GetComponent<IconTabButton>();
            if (_tabButton == null)
            {
                Debug.LogError("[TradeShuttlePlanner] cloned tab lost its IconTabButton; not built.");
                UnityEngine.Object.Destroy(tabGo);
                return;
            }

            _tabButton.SetState(IconTabButtonState.Inactive);
            _tabButton.OnClick += (b, c) => OnTabClicked(screen, unloadButton);
            Debug.Log("[TradeShuttlePlanner] planner tab injected.");
        }

        private static void OnTabClicked(TradeShuttleScreen screen, CommonButton buttonTemplate)
        {
            try
            {
                if (Window == null || !Window)
                {
                    Window = PlannerWindow.Create(screen, buttonTemplate);
                }
                Window?.Open();
                // The overlay owns focus; keep our tab visually a momentary button, not a stuck tab.
                _tabButton?.SetState(IconTabButtonState.Inactive);
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] open planner window: " + e);
            }
        }
    }
}
