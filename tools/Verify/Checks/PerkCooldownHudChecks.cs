namespace Verify.Checks;

/// <summary>
/// Independent replay of the countdown the player actually sees, reimplemented from
/// PerkTrigger's own arithmetic rather than from the patch, so an off-by-one shows up here
/// instead of in game.
/// </summary>
public sealed class PerkCooldownHudChecks : IModChecks
{
    public string ModName => "PerkCooldownHud";

    public void Run(Reporter reporter)
    {
        // Unsaturated case: active 3, cooldown 5, so every number in the sequence is distinct
        // and a shift by one is unambiguous.
        var (active, cooling) = Replay(activeTurns: 3, cooldownTurns: 5);

        reporter.AssertEqual("active phase readout", string.Join(",", active), "3,2,1");
        reporter.AssertEqual("cooldown readout", string.Join(",", cooling), "5,4,3,2,1");

        reporter.Assert(
            cooling.Count == 5,
            $"cooldown shown for {cooling.Count} turns, config says 5");

        // A negative or zero readout is the exact bug the ViewValue patch exists to prevent:
        // vanilla ViewValue runs negative once the active phase ends.
        reporter.Assert(
            !active.Concat(cooling).Any(v => v.StartsWith("-") || v == "0"),
            "a non-positive value would be displayed");

        // ICDRecovery ticks cooldowns down faster than one per turn. Because the readout is
        // Duration itself rather than a count of elapsed turns, it stays truthful for free.
        var (_, fastCooling) = Replay(activeTurns: 2, cooldownTurns: 6, cdRecoveryPerTurn: 1);
        reporter.AssertEqual("cooldown readout with ICDRecovery=1", string.Join(",", fastCooling), "6,4,2");
    }

    private static (List<string> Active, List<string> Cooling) Replay(
        int activeTurns,
        int cooldownTurns,
        int cdRecoveryPerTurn = 0)
    {
        var activePhaseDuration = activeTurns;
        var duration = activeTurns + cooldownTurns;
        var originalDuration = duration;

        var shownActive = new List<string>();
        var shownCooling = new List<string>();

        // The effect is removed - and the perk usable again - when Duration reaches 0.
        while (duration > 0)
        {
            var isInActivePhase = originalDuration - duration <= activePhaseDuration - 1;

            // Vanilla ViewValue, then the patch's override.
            var view = (float)(activePhaseDuration - Math.Abs(duration - originalDuration));
            if (!isInActivePhase)
            {
                view = duration;
            }

            if (isInActivePhase)
            {
                shownActive.Add(view.ToString());
            }
            else
            {
                shownCooling.Add(view.ToString());
            }

            duration -= 1 + (isInActivePhase ? 0 : cdRecoveryPerTurn);
        }

        return (shownActive, shownCooling);
    }
}
