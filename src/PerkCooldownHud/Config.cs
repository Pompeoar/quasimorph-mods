namespace PerkCooldownHud
{
    /// <summary>
    /// Tunables. Kept in one place so the behaviour is easy to change without hunting
    /// through the patch classes.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Use the panel's yellow border sprite while the perk is cooling down. This is the
        /// primary signal: green/red already mean "buff"/"debuff", so a third colour reads
        /// as a distinct state instantly, where a dimmed green has to be compared against
        /// an undimmed one before it means anything.
        /// </summary>
        public const bool YellowBorderWhileCoolingDown = true;

        /// <summary>
        /// Alpha applied to a perk's HUD panel while it is cooling down. Secondary cue only,
        /// now that the border carries the state. Set to 1.0 to disable dimming entirely.
        /// </summary>
        public const float CooldownAlpha = 0.45f;

        /// <summary>Alpha applied while the perk is in its active phase (vanilla look).</summary>
        public const float ActiveAlpha = 1.0f;

        /// <summary>
        /// Flip the border to the red "bad effect" sprite during cooldown instead. Off, and
        /// superseded by the yellow border when both are enabled, since the yellow border is
        /// applied later. Red means "debuff" everywhere else in this HUD.
        /// </summary>
        public const bool RedBorderWhileCoolingDown = false;
    }
}
