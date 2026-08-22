using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TradeShuttlePlanner
{
    internal static class FetchReport
    {
        private static string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);

        public static string BuildDialog(FetchResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Trade Shuttle Planner");
            sb.AppendLine();

            if (r.Failure != null)
            {
                sb.AppendLine(r.Failure);
                return sb.ToString();
            }

            var c = r.Chosen;
            sb.AppendLine("Shopping for: " + Names.Item(r.TargetItemId));
            sb.AppendLine();
            sb.AppendLine("SHUTTLE LOADED - " + N(c.ItemsLoaded) + " stacks, worth " + N(c.CargoValue));
            sb.AppendLine();
            sb.AppendLine("  SEND TO   " + c.OrbitName);
            sb.AppendLine("  CATEGORY  " + Names.Category(c.CategoryId));
            sb.AppendLine();
            sb.AppendLine("  Expect back: " + c.TargetCount + "x " + Names.Item(r.TargetItemId));
            sb.AppendLine("  Return value " + N(c.ReturnValue) + "  (profit " + c.Profit + "%)");

            if (c.TargetBuyPrice > 0)
            {
                sb.AppendLine("  It quotes " + N(c.TargetBuyPrice) + " there (rep " + c.Reputation + ")");
            }

            if (c.TargetCount == 0)
            {
                sb.AppendLine();
                if (c.Rank > 1)
                {
                    // The shuttle buys strictly best-ratio first, so position in that queue is
                    // the real gate - not how much cargo you send.
                    sb.AppendLine("WHY 0x: it is #" + c.Rank + " of " + c.Field + " in the buy order");
                    sb.AppendLine("there. The shuttle spends everything on the " + (c.Rank - 1) + " better");
                    sb.AppendLine("value items first and fills the hold. More cargo will not");
                    sb.AppendLine("help; you need a seller where it ranks higher, or better rep");
                    sb.AppendLine("so its price drops.");
                }
                else
                {
                    sb.AppendLine("WARNING: this loadout does not bring one back. Raise the");
                    sb.AppendLine("cargo cap (cargoValueMultiplier in planner.cfg) or improve");
                    sb.AppendLine("reputation with the faction selling it.");
                }
            }

            if (r.Considered.Count > 1)
            {
                sb.AppendLine();
                sb.AppendLine("Other options:");
                foreach (var o in r.Considered.Skip(1).Take(4))
                {
                    var price = o.TargetBuyPrice > 0 ? ", costs " + N(o.TargetBuyPrice) : string.Empty;
                    sb.AppendLine("  " + o.OrbitName + " - " + o.TargetCount + "x, profit "
                                  + o.Profit + "%" + price);
                }
            }

            sb.AppendLine();
            sb.AppendLine("100% is break even, not 70%.");
            return sb.ToString();
        }

        public static string BuildFull(FetchResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("TRADE SHUTTLE PLANNER - FETCH");
            sb.AppendLine(new string('=', 78));
            sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            sb.AppendLine();

            if (r.TargetItemId != null)
            {
                sb.AppendLine("Target: " + Names.Item(r.TargetItemId) + "  (" + r.TargetItemId + ")");
                sb.AppendLine();
            }

            if (r.Failure != null)
            {
                sb.AppendLine(r.Failure);
                return sb.ToString();
            }

            sb.AppendLine("  ORBIT                      GET  PROFIT   CARGO   RETURN  SOLD BY");
            sb.AppendLine("  " + new string('-', 74));
            foreach (var o in r.Considered)
            {
                sb.AppendLine(string.Format(
                    "  {0,-26} {1,3}  {2,5}%  {3,7}  {4,7}  {5}",
                    o.OrbitName.Length > 26 ? o.OrbitName.Substring(0, 25) + "." : o.OrbitName,
                    o.TargetCount, o.Profit, N(o.CargoValue), N(o.ReturnValue),
                    string.Join(", ", o.Sellers)));
            }
            sb.AppendLine();

            if (r.Chosen != null)
            {
                sb.AppendLine("CHOSEN: " + r.Chosen.OrbitName);
                sb.AppendLine("The hold has been loaded for this destination and the barter category");
                sb.AppendLine("set to " + Names.Category(r.Chosen.CategoryId) + ". Press SEND and pick " + r.Chosen.OrbitName + ".");
                sb.AppendLine();
            }

            sb.AppendLine(new string('=', 78));
            sb.AppendLine("The category is a priority budget, not a filter: only 60% of the run's trade");
            sb.AppendLine("points chase it, and only from stock the destination already holds.");
            sb.AppendLine("These figures also ignore banked faction trade points, so they are a floor.");
            return sb.ToString();
        }
    }
}
