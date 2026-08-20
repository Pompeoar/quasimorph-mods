using MGSC;

namespace BaneCounter
{
    /// <summary>
    /// The Bane arithmetic, in one place, so the three surfaces that display it - the HUD
    /// icon, the HUD tooltip and the operator tooltip - cannot disagree with each other.
    /// </summary>
    public static class BaneInfo
    {
        /// <summary>
        /// The lowest activation threshold above the current level, or -1 when every curse
        /// for this patron is already active.
        ///
        /// Filtered by CurrentBramfatura because the table holds a separate ladder per
        /// patron and only the signed one applies. In the shipped data every ladder is the
        /// same five steps - 1, 200, 400, 700, 1000 - but that is data, not a guarantee, so
        /// it is read rather than assumed.
        /// </summary>
        public static int NextActivationLevel(CurseData curse)
        {
            if (curse == null || string.IsNullOrEmpty(curse.CurrentBramfatura))
            {
                return -1;
            }

            var best = -1;
            foreach (var record in Data.Curses.Records)
            {
                if (record.BramfaturaId != curse.CurrentBramfatura)
                {
                    continue;
                }

                if (record.ActivationLevel <= curse.CurseLevel)
                {
                    continue;
                }

                if (best < 0 || record.ActivationLevel < best)
                {
                    best = record.ActivationLevel;
                }
            }

            return best;
        }

        /// <summary>
        /// Appends the level and the distance to the next curse to whichever tooltip is
        /// currently being built.
        ///
        /// Why the distance is worth showing at all: between two thresholds a curse's power
        /// is an InverseLerp across the gap (CurseSystem.RefreshCursesPower), so Bane short
        /// of the next step is not idle - it is continuously strengthening what is already
        /// on you. The gap is the only number that makes that legible.
        /// </summary>
        public static void AddTooltipRows(TooltipFactory factory, CurseData curse)
        {
            if (factory == null || curse == null || string.IsNullOrEmpty(curse.CurrentBramfatura))
            {
                return;
            }

            // "Bane level" - the one string here the game already translates.
            factory.AddPanelToTooltip()
                .SetIcon(IconTag)
                .LocalizeName("tooltip.DecreaseCurseLevel")
                .SetValue(curse.CurseLevel);

            var next = NextActivationLevel(curse);
            if (next < 0)
            {
                factory.AddPanelToTooltip()
                    .SetIcon(IconTag)
                    .SetName("All curses active")
                    .SetNameColor(Colors.LightRed);
                return;
            }

            factory.AddPanelToTooltip()
                .SetIcon(IconTag)
                .SetName("Next curse at")
                .SetValue(next);

            factory.AddPanelToTooltip()
                .SetIcon(IconTag)
                .SetName("Bane to go")
                .SetValue(next - curse.CurseLevel)
                .SetValueColor(Colors.Yellow);
        }

        /// <summary>The tooltip-atlas tag vanilla uses for curse rows, resolved by Data.TooltipIcons.</summary>
        public const string IconTag = "common_curse_red";
    }
}
