using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace TradeShuttlePlanner
{
    /// <summary>
    /// Plain key=value config, deliberately not JSON so the mod carries no serializer
    /// dependency into Unity's Mono runtime. Written on first run with the defaults.
    /// </summary>
    public sealed class PlannerConfig
    {
        public KeyCode Hotkey = KeyCode.F9;
        public int TopOrbits = 12;
        public int MaxGainsShown = 4;
        public bool ShowDialog = true;
        public List<string> Wanted = new List<string>();

        public static PlannerConfig Current { get; private set; } = new PlannerConfig();

        public static string ConfigPath =>
            Path.Combine(Paths.ModDataDir, "planner.cfg");

        public static void Load()
        {
            var cfg = new PlannerConfig();
            try
            {
                Directory.CreateDirectory(Paths.ModDataDir);
                if (!File.Exists(ConfigPath))
                {
                    File.WriteAllText(ConfigPath, DefaultFileText());
                }
                else
                {
                    foreach (var raw in File.ReadAllLines(ConfigPath))
                    {
                        var line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') { continue; }
                        var eq = line.IndexOf('=');
                        if (eq <= 0) { continue; }
                        var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        var val = line.Substring(eq + 1).Trim();
                        Apply(cfg, key, val);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("[TradeShuttlePlanner] config load failed, using defaults: " + e.Message);
            }
            Current = cfg;
        }

        private static void Apply(PlannerConfig cfg, string key, string val)
        {
            switch (key)
            {
                case "hotkey":
                    if (Enum.TryParse(val, true, out KeyCode k)) { cfg.Hotkey = k; }
                    break;
                case "toporbits":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t > 0)
                    {
                        cfg.TopOrbits = Math.Min(t, 40);
                    }
                    break;
                case "maxgainsshown":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) && g > 0)
                    {
                        cfg.MaxGainsShown = Math.Min(g, 20);
                    }
                    break;
                case "showdialog":
                    cfg.ShowDialog = !val.Equals("false", StringComparison.OrdinalIgnoreCase) && val != "0";
                    break;
                case "wanted":
                    cfg.Wanted = val.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToList();
                    break;
            }
        }

        private static string DefaultFileText()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "# Trade Shuttle Planner",
                "#",
                "# hotkey        any UnityEngine.KeyCode name. Polled on the space map.",
                "# topOrbits     how many destinations to rank in the report.",
                "# maxGainsShown how many returning item lines to preview per destination.",
                "# showDialog    false = write the report file only, no in-game popup.",
                "# wanted        comma separated item ids or display-name fragments. The report",
                "#               adds a 'where to buy' section for each one. Case insensitive.",
                "#               example: wanted = ore_cargo, Research Dump, Military Rations",
                "",
                "hotkey = F9",
                "topOrbits = 12",
                "maxGainsShown = 4",
                "showDialog = true",
                "wanted = ",
                ""
            });
        }
    }

    internal static class Paths
    {
        public static string ModDataDir =>
            Path.Combine(Application.persistentDataPath, "TradeShuttlePlanner");

        public static string ReportPath =>
            Path.Combine(ModDataDir, "last_plan.txt");
    }
}
