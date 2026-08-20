namespace BaneCounter
{
    /// <summary>
    /// Tunables, kept together so behaviour can be changed without reading the patches.
    ///
    /// These are static readonly rather than const deliberately. As consts the compiler
    /// folds them into their call sites, so any guard written against a flag's default
    /// value becomes provably dead code and the build fills with CS0162 - which trains you
    /// to ignore warnings in a project where a warning is usually a renamed game member.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Show the raw Bane level on the HUD icon instead of the number of active curses.
        ///
        /// Turning this off leaves only the tooltip additions, which is a reasonable way to
        /// run the mod if the two-to-four digit number is visually too loud.
        /// </summary>
        public static readonly bool ShowLevelOnIcon = true;

        /// <summary>
        /// Add the current level and the distance to the next curse to the icon's tooltip.
        /// </summary>
        public static readonly bool ShowTooltipDetail = true;

        /// <summary>
        /// Keep vanilla's flash-and-chime when the displayed number changes.
        ///
        /// This matters more than it looks. Vanilla's number is the count of active curses,
        /// which changes maybe five times in a run, so the blink is rare by construction.
        /// Showing the level makes it change on every single Pact cast. That is the point -
        /// it is the feedback that was missing - but it is also a sound and a flash every
        /// cast, so it gets a switch.
        /// </summary>
        public static readonly bool BlinkOnAccrual = true;

        /// <summary>
        /// Add a Bane diamond to each row of the Manage Operators roster.
        ///
        /// The icon is a clone of the implants diamond, so it inherits the row's layout
        /// rather than guessing at the game's UI metrics.
        /// </summary>
        public static readonly bool ShowRosterIcon = true;

        /// <summary>
        /// Show the roster icon on operators whose Bane is 0. Off by default: a clone that
        /// has never spent Bane is the uninteresting case, and hiding it keeps the row
        /// scannable in exactly the way the healing icon does.
        /// </summary>
        public static readonly bool ShowRosterIconAtZero = false;

        /// <summary>Size of the roster number relative to the row's class label.</summary>
        public static readonly float RosterLabelScale = 0.9f;

        /// <summary>
        /// Log the roster row's layout once per session.
        ///
        /// The icon row is prefab-authored, so its container, layout group and sizing are
        /// serialized data that cannot be read from the decompiled source. Guessing at it
        /// costs a full build-deploy-restart cycle per guess; having the game describe it
        /// once costs one.
        /// </summary>
        public static readonly bool DebugLayout = true;
    }
}
