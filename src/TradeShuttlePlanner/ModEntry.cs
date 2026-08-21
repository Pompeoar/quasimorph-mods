using System;
using System.IO;
using MGSC;
using UnityEngine;

namespace TradeShuttlePlanner
{
    public static class ModEntry
    {
        private static bool _initialised;
        private static int _lastHandledFrame = -1;

        // UserModSystem.GrabMethods registers a hook twice when the hook key already exists
        // (it adds inside the ContainsKey branch, then again after TryGetValue). Every hook
        // here therefore has to be safe to run twice in the same frame.

        [Hook(ModHookType.AfterBootstrap)]
        public static void AfterBootstrap(IModContext context)
        {
            if (_initialised) { return; }
            _initialised = true;
            try
            {
                PlannerConfig.Load();
                Debug.Log("[TradeShuttlePlanner] ready. Hotkey " + PlannerConfig.Current.Hotkey +
                          " on the space map. Config: " + PlannerConfig.ConfigPath);
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
                Execute(context.State);
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] " + e);
                Notify("Trade Shuttle Planner failed: " + e.Message);
            }
        }

        private static void Execute(State state)
        {
            var result = Planner.Run(state);

            try
            {
                Directory.CreateDirectory(Paths.ModDataDir);
                File.WriteAllText(Paths.ReportPath, Report.BuildFull(result));
            }
            catch (Exception e)
            {
                Debug.LogError("[TradeShuttlePlanner] could not write report: " + e.Message);
            }

            if (PlannerConfig.Current.ShowDialog)
            {
                var text = Report.BuildDialog(result);
                UI.Chain<AlertDialogWindow>().Invoke(delegate (AlertDialogWindow v)
                {
                    v.Configure(text);
                }).Show();
            }
            else
            {
                Notify(result.Failure ?? ("Trade plan written: " + Paths.ReportPath));
            }
        }

        private static void Notify(string message)
        {
            try { UI.Staff.NotificationPanel.AddNotification(message); }
            catch { Debug.Log("[TradeShuttlePlanner] " + message); }
        }
    }
}
