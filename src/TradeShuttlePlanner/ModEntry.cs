using System;
using System.IO;
using System.Text;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    public static class ModEntry
    {
        private static bool _initialised;
        private static int _lastHandledFrame = -1;

        // UserModSystem.GrabMethods registers a hook twice when the hook-type key already
        // exists, so every hook here must be safe to run more than once.

        [Hook(ModHookType.BeforeBootstrap)]
        public static void BeforeBootstrap(IModContext context)
        {
            if (_initialised) { return; }
            _initialised = true;
            try
            {
                PlannerConfig.Load();
                new Harmony("TradeShuttlePlanner").PatchAll(typeof(ModEntry).Assembly);
                Debug.Log("[TradeShuttlePlanner] ready. Open an item in the stock market, then press " +
                          PlannerConfig.Current.Hotkey + " on the trade shuttle screen.");
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] init failed: " + e);
            }
        }

        [Hook(ModHookType.SpaceUpdateBeforeGameLoop)]
        public static void SpaceUpdate(IModContext context)
        {
            if (Time.frameCount == _lastHandledFrame) { return; }
            try
            {
                if (!Input.GetKeyDown(PlannerConfig.Current.Hotkey)) { return; }
                _lastHandledFrame = Time.frameCount;

                if (UI.IsShowing<TradeShuttleScreen>())
                {
                    DoFetch(context.State);
                }
                else
                {
                    // Away from the loading screen the hotkey keeps its original meaning:
                    // rank the hold you have already built.
                    DoSurvey(context.State);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] " + e);
                Alert("Trade Shuttle Planner failed:\n" + e.Message);
            }
        }

        private static void DoFetch(State state)
        {
            var result = Fetch.Run(state, ShoppingList.TargetItemId);

            if (result.Failure == null && result.Chosen != null)
            {
                // The hold changed underneath the open screen; make it show that.
                try { UI.Get<TradeShuttleScreen>()?.RefreshView(); }
                catch (Exception e) { Debug.LogError("[TradeShuttlePlanner] refresh: " + e.Message); }
            }

            WriteReport(FetchReport.BuildFull(result));
            Alert(FetchReport.BuildDialog(result));
        }

        private static void DoSurvey(State state)
        {
            var result = Planner.Run(state);
            WriteReport(Report.BuildFull(result));
            Alert(Report.BuildDialog(result));
        }

        private static void WriteReport(string text)
        {
            try
            {
                Directory.CreateDirectory(Paths.ModDataDir);
                File.WriteAllText(Paths.ReportPath, text);
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] could not write report: " + e.Message);
            }
        }

        private static void Alert(string text)
        {
            if (!PlannerConfig.Current.ShowDialog)
            {
                Debug.Log("[TradeShuttlePlanner] " + text);
                return;
            }
            UI.Chain<AlertDialogWindow>().Invoke(delegate (AlertDialogWindow v)
            {
                v.Configure(text);
            }).Show();
        }
    }
}
