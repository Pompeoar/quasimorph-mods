namespace PerkCooldownHud
{
    /// <summary>
    /// Tunables. Kept in one place so the behaviour is easy to change without hunting
    /// through the patch classes.
    /// </summary>
    public static class Config
    {
        /// <summary>Alpha applied to a perk's HUD panel while it is cooling down.</summary>
        public const float CooldownAlpha = 0.45f;

        /// <summary>Alpha applied while the perk is in its active phase (vanilla look).</summary>
        public const float ActiveAlpha = 1.0f;

        /// <summary>
        /// When true the panel border also flips to the red "bad effect" sprite during
        /// cooldown. Off by default: the dimming already reads as unavailable, and red
        /// means "debuff" everywhere else in this HUD.
        /// </summary>
        public const bool RedBorderWhileCoolingDown = false;
    }
}
