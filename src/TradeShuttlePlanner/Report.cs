using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TradeShuttlePlanner
{
    internal static class Report
    {
        private static string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Full report, written to disk.</summary>
        public static string BuildFull(PlanResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TRADE SHUTTLE PLANNER");
            sb.AppendLine(new string('=', 78));
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            sb.AppendLine();

            if (r.Failure != null)
            {
                sb.AppendLine(r.Failure);
                return sb.ToString();
            }

            sb.AppendLine($"Hold        {N(r.CargoCells)} of {N(r.CargoCapacity)} cells used");
            sb.AppendLine($"World value {N(r.CargoValue)}");
            sb.AppendLine($"Mode        {r.Mode}" +
                (r.Mode == MGSC.TradeShuttleMode.Barter
                    ? string.Empty
                    : "  (the category dropdown only applies in Barter mode)"));
            sb.AppendLine($"Scanned     {N(r.OrbitsScanned)} orbits x {N(r.CombinationsEvaluated)} orbit/category combinations");
            if (!r.UsedItemLevelDetail)
            {
                sb.AppendLine("NOTE: item-level detail unavailable (game internals moved); showing value only.");
            }
            sb.AppendLine();

            sb.AppendLine("RANKED DESTINATIONS");
            sb.AppendLine("100% is break even. The game colours this green from 70%, which is still a loss.");
            sb.AppendLine();
            sb.AppendLine("  #  PROFIT  ORBIT                      CATEGORY      RETURNS");
            sb.AppendLine("  " + new string('-', 74));

            var n = 0;
            foreach (var plan in r.Ranked.Take(PlannerConfig.Current.TopOrbits))
            {
                n++;
                sb.AppendLine(string.Format(
                    "  {0,-2} {1,5}%  {2,-26} {3,-13} {4}",
                    n, plan.Profit, Trim(plan.OrbitName, 26), Trim(Names.Category(plan.CategoryId), 13),
                    Gains(plan, PlannerConfig.Current.MaxGainsShown)));
            }
            if (n == 0)
            {
                sb.AppendLine("  No orbit has a station that will trade with you.");
                sb.AppendLine("  Reputation below " + MGSC.Data.Global.TradeMinReputationToExchange +
                              " everywhere, or every faction is untradeable.");
            }
            sb.AppendLine();

            if (r.DeadWeight.Count > 0)
            {
                sb.AppendLine("DEAD WEIGHT AT THE TOP DESTINATION (" + r.DeadWeightOrbit + ")");
                sb.AppendLine("No station there consumes these, so they liquidate at the junk rate.");
                sb.AppendLine();
                foreach (var line in r.DeadWeight.Take(20))
                {
                    sb.AppendLine($"  {line.Count,4}x  {Trim(Names.Item(line.ItemId), 34),-34} world {N(line.Value)}");
                }
                sb.AppendLine();
            }

            if (r.WantedHits.Count > 0 || r.WantedMisses.Count > 0)
            {
                sb.AppendLine("WHERE TO BUY");
                sb.AppendLine("Rank is this item's place in the shuttle's value-for-money ordering within");
                sb.AppendLine("its category at that station. Rank 1 means the priority budget takes it first.");
                sb.AppendLine();
                sb.AppendLine("  ITEM                       ORBIT                 CATEGORY     STOCK  BUY  RANK");
                sb.AppendLine("  " + new string('-', 76));
                foreach (var hit in r.WantedHits)
                {
                    sb.AppendLine(string.Format(
                        "  {0,-26} {1,-21} {2,-12} {3,5} {4,4}  {5}{6}",
                        Trim(Names.Item(hit.ItemId), 26),
                        Trim(Names.Orbit(hit.SpaceObjectId), 21),
                        Trim(Names.Category(hit.CategoryId), 12),
                        hit.Stock,
                        hit.BuyPrice,
                        hit.RatioField > 0 ? $"{hit.RatioRank}/{hit.RatioField}" : "-",
                        hit.Eligible ? string.Empty : "  [REP TOO LOW]"));
                }
                foreach (var miss in r.WantedMisses)
                {
                    sb.AppendLine($"  \"{miss}\" is not in stock anywhere outside your current orbit.");
                }
                sb.AppendLine();
            }
            else if (PlannerConfig.Current.Wanted.Count == 0)
            {
                sb.AppendLine("TIP: list items in planner.cfg under 'wanted =' to get a where-to-buy section.");
                sb.AppendLine("     " + PlannerConfig.ConfigPath);
                sb.AppendLine();
            }

            sb.AppendLine(new string('=', 78));
            sb.AppendLine("Remember the category is a priority budget, not a filter: only 60% of the run's");
            sb.AppendLine("trade points chase it, and only from stock the destination already holds.");
            return sb.ToString();
        }

        /// <summary>Short version for the in-game popup, which is a single text label.</summary>
        public static string BuildDialog(PlanResult r)
        {
            if (r.Failure != null) { return "Trade Shuttle Planner\n\n" + r.Failure; }

            var sb = new StringBuilder();
            sb.AppendLine("Trade Shuttle Planner");
            sb.AppendLine();
            sb.AppendLine($"Hold {N(r.CargoCells)}/{N(r.CargoCapacity)} cells, world value {N(r.CargoValue)}.");
            sb.AppendLine($"Ranked {N(r.CombinationsEvaluated)} orbit/category options.");
            sb.AppendLine();

            var top = r.Ranked.Take(8).ToList();
            if (top.Count == 0)
            {
                sb.AppendLine("No destination will trade with you right now.");
            }
            else
            {
                foreach (var plan in top)
                {
                    sb.AppendLine($"{plan.Profit,5}%  {Trim(plan.OrbitName, 22),-22}  {Names.Category(plan.CategoryId)}");
                    var gains = Gains(plan, 2);
                    if (gains.Length > 0) { sb.AppendLine($"        {Trim(gains, 52)}"); }
                }
                sb.AppendLine();
                sb.AppendLine("100% is break even, not 70%.");
            }

            if (r.DeadWeight.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Dead weight at {Trim(r.DeadWeightOrbit, 24)}: " +
                              string.Join(", ", r.DeadWeight.Take(3).Select(d => d.Count + "x " + Names.Item(d.ItemId))));
            }

            sb.AppendLine();
            sb.AppendLine("Full report: " + Paths.ReportPath);
            return sb.ToString();
        }

        private static string Gains(OrbitPlan plan, int max)
        {
            if (plan.Gains == null || plan.Gains.Count == 0) { return string.Empty; }
            var parts = new List<string>();
            foreach (var line in plan.Gains.Take(max))
            {
                parts.Add(line.Count + "x " + Names.Item(line.ItemId));
            }
            if (plan.Gains.Count > max) { parts.Add("+" + (plan.Gains.Count - max) + " more"); }
            return string.Join(", ", parts);
        }

        private static string Trim(string s, int width)
        {
            if (string.IsNullOrEmpty(s)) { return string.Empty; }
            return s.Length <= width ? s : s.Substring(0, Math.Max(1, width - 1)) + ".";
        }
    }
}
