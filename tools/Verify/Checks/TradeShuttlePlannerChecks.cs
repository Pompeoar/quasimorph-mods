namespace Verify.Checks;

/// <summary>
/// TradeShuttlePlanner calls the game's own exchange simulation for its headline numbers, so
/// most of it needs no behavioural check. The exception is the where-to-buy ranking.
///
/// The game picks purchases by repeated argmax over a boolean predicate,
/// IsTradeShuttleBuyCandidateBetter. The mod instead <em>sorts</em> the candidates once and
/// reads off positions. Those two agree only if the predicate is a strict total order - if it
/// were merely a partial order, or intransitive, sorting would silently produce a different
/// ordering than the game does, and the mod would confidently print a wrong rank.
///
/// So this check proves three things over randomised candidate sets:
///   1. the game's predicate is irreflexive, antisymmetric and transitive;
///   2. sorting by it reproduces repeated-argmax exactly;
///   3. the mod's comparator agrees with the predicate on every pair.
///
/// The predicate below is transcribed from TradeSystem.IsTradeShuttleBuyCandidateBetter, and
/// the comparator from Planner.RankInCategory. Keeping both transcriptions here, rather than
/// referencing the mod, is deliberate: the check has to be able to disagree with the mod.
/// </summary>
public sealed class TradeShuttlePlannerChecks : IModChecks
{
    public string ModName => "TradeShuttlePlanner";

    private readonly record struct Candidate(string Id, int World, int Buy, int Cells);

    /// <summary>Transcribed from TradeSystem.IsTradeShuttleBuyCandidateBetter.</summary>
    private static bool IsBetter(Candidate c, Candidate best)
    {
        if (c.Buy == 0 || best.Buy == 0)
        {
            if (c.Buy == 0 && best.Buy != 0) { return true; }
            if (c.Buy != 0 && best.Buy == 0) { return false; }
        }
        else
        {
            long a = (long)c.World * best.Buy;
            long b = (long)best.World * c.Buy;
            if (a != b) { return a > b; }
        }

        int cm = c.World - c.Buy;
        int bm = best.World - best.Buy;
        if (cm != bm) { return cm > bm; }
        if (c.Cells != best.Cells) { return c.Cells < best.Cells; }
        if (c.World != best.World) { return c.World > best.World; }
        if (c.Buy != best.Buy) { return c.Buy < best.Buy; }
        return string.CompareOrdinal(c.Id, best.Id) < 0;
    }

    /// <summary>Transcribed from Planner.RankInCategory's sort comparator.</summary>
    private static int ModCompare(Candidate a, Candidate b)
    {
        if (a.Buy == 0 || b.Buy == 0)
        {
            if (a.Buy == 0 && b.Buy != 0) { return -1; }
            if (a.Buy != 0 && b.Buy == 0) { return 1; }
        }
        else
        {
            long left = (long)a.World * b.Buy;
            long right = (long)b.World * a.Buy;
            if (left != right) { return left > right ? -1 : 1; }
        }

        int margin = (b.World - b.Buy).CompareTo(a.World - a.Buy);
        if (margin != 0) { return margin; }
        if (a.Cells != b.Cells) { return a.Cells < b.Cells ? -1 : 1; }
        if (a.World != b.World) { return a.World > b.World ? -1 : 1; }
        if (a.Buy != b.Buy) { return a.Buy < b.Buy ? -1 : 1; }
        return string.CompareOrdinal(a.Id, b.Id);
    }

    public void Run(Reporter reporter)
    {
        // Fixed seed: a check that fails only sometimes is worse than no check.
        var rng = new Random(20240614);

        var orderViolations = 0;
        var agreementViolations = 0;
        var selectionMismatches = 0;
        var totalPairs = 0;
        var sawZeroBuy = false;
        var sawRatioTie = false;

        for (var trial = 0; trial < 400; trial++)
        {
            var n = rng.Next(2, 9);
            var set = new List<Candidate>();
            for (var i = 0; i < n; i++)
            {
                // Small price ranges on purpose, so ratio ties and equal margins - the cases
                // where the deeper tie-breaks actually decide - occur often rather than never.
                set.Add(new Candidate(
                    Id: "item_" + rng.Next(0, 20).ToString("00"),
                    World: rng.Next(1, 13),
                    Buy: rng.Next(0, 13),
                    Cells: rng.Next(1, 4)));
            }
            set = set.GroupBy(c => c.Id).Select(g => g.First()).ToList();
            if (set.Count < 2) { continue; }

            if (set.Any(c => c.Buy == 0)) { sawZeroBuy = true; }

            foreach (var a in set)
            {
                if (IsBetter(a, a)) { orderViolations++; }        // irreflexive

                foreach (var b in set)
                {
                    if (a.Id == b.Id) { continue; }
                    totalPairs++;

                    var ab = IsBetter(a, b);
                    var ba = IsBetter(b, a);
                    if (ab == ba) { orderViolations++; }          // antisymmetric + total

                    if (a.Buy != 0 && b.Buy != 0 &&
                        (long)a.World * b.Buy == (long)b.World * a.Buy)
                    {
                        sawRatioTie = true;
                    }

                    var cmp = ModCompare(a, b);
                    if (cmp == 0 || (cmp < 0) != ab) { agreementViolations++; }
                }
            }

            // Transitivity.
            foreach (var a in set)
            {
                foreach (var b in set)
                {
                    if (a.Id == b.Id) { continue; }
                    if (!IsBetter(a, b)) { continue; }
                    foreach (var c in set)
                    {
                        if (c.Id == a.Id || c.Id == b.Id) { continue; }
                        if (IsBetter(b, c) && !IsBetter(a, c)) { orderViolations++; }
                    }
                }
            }

            // Repeated argmax, exactly as the game consumes the predicate.
            var remaining = new List<Candidate>(set);
            var greedy = new List<string>();
            while (remaining.Count > 0)
            {
                var best = remaining[0];
                for (var i = 1; i < remaining.Count; i++)
                {
                    if (IsBetter(remaining[i], best)) { best = remaining[i]; }
                }
                greedy.Add(best.Id);
                remaining.RemoveAll(c => c.Id == best.Id);
            }

            var sorted = new List<Candidate>(set);
            sorted.Sort(ModCompare);

            if (!greedy.SequenceEqual(sorted.Select(c => c.Id)))
            {
                selectionMismatches++;
            }
        }

        reporter.Assert(
            orderViolations == 0,
            $"[TradeShuttlePlanner] IsTradeShuttleBuyCandidateBetter is not a strict total order ({orderViolations} violations); the mod's sort-based ranking would not match the game's selection");

        reporter.Assert(
            agreementViolations == 0,
            $"[TradeShuttlePlanner] the mod's rank comparator disagrees with the game's predicate on {agreementViolations} pair(s) of {totalPairs}");

        reporter.Assert(
            selectionMismatches == 0,
            $"[TradeShuttlePlanner] sorting disagreed with repeated-argmax selection in {selectionMismatches} trial(s)");

        // Guard against the test data being so tame it never exercises the tie-breaks. A pass
        // on inputs that never tie would prove almost nothing.
        reporter.Assert(sawZeroBuy,
            "[TradeShuttlePlanner] no zero-buy-price candidate was generated; the free-item branch went untested");
        reporter.Assert(sawRatioTie,
            "[TradeShuttlePlanner] no ratio tie was generated; every tie-break past the ratio went untested");
    }
}
