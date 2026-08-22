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

        /// <summary>Never loaded into the shuttle. Item id or display-name fragment, case insensitive.</summary>
        public List<string> Keep = new List<string>();

        /// <summary>Hard ceiling on loaded cargo world value. 0 = derive it from the target's price.</summary>
        public int MaxCargoValue = 0;

        /// <summary>When MaxCargoValue is 0, load at most this many times the target's world price.</summary>
        public float CargoValueMultiplier = 6f;

        /// <summary>Items at or below this unit world price are treated as barter junk and spent first.</summary>
        public int JunkCeiling = 400;

        /// <summary>
        /// Allow loading cargo the destination does not consume. Such cargo liquidates at only a
        /// fifth of its worth while still eating a return cell, so this is off by default.
        /// </summary>
        public bool AllowUnwantedFiller = false;

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
                case "keep":
                    cfg.Keep = val.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 0)
                        .ToList();
                    break;
                case "maxcargovalue":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mc) && mc >= 0)
                    {
                        cfg.MaxCargoValue = mc;
                    }
                    break;
                case "cargovaluemultiplier":
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var cm) && cm > 0f)
                    {
                        cfg.CargoValueMultiplier = Math.Min(cm, 100f);
                    }
                    break;
                case "junkceiling":
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var jc) && jc >= 0)
                    {
                        cfg.JunkCeiling = jc;
                    }
                    break;
                case "allowunwantedfiller":
                    cfg.AllowUnwantedFiller =
                        val.Equals("true", StringComparison.OrdinalIgnoreCase) || val == "1";
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
                "#",
                "# keep          comma separated item ids or display-name fragments the shuttle must",
                "#               never carry. Use this for gear you are saving for upgrades.",
                "#               example: keep = Reactor Core, plasma, Advanced Toolkit",
                "# junkCeiling   unit world price at or below which an item counts as barter junk.",
                "#               Junk is loaded first, so your good stock stays home.",
                "# allowUnwantedFiller  true = also load cargo the destination does not consume.",
                "#               That cargo only liquidates at a fifth of its worth while still",
                "#               eating a return cell, so leaving this false is usually right.",
                "# maxCargoValue hard ceiling on loaded cargo world value. 0 = derive it.",
                "# cargoValueMultiplier  when maxCargoValue is 0, load at most this many times the",
                "#               wanted item's world price. Stops the hold being emptied for a",
                "#               cheap purchase.",
                "",
                "hotkey = F9",
                "topOrbits = 12",
                "maxGainsShown = 4",
                "showDialog = true",
                "wanted = ",
                "keep = ",
                "junkCeiling = 400",
                "allowUnwantedFiller = false",
                "maxCargoValue = 0",
                "cargoValueMultiplier = 6",
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
