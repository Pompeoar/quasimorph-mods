namespace QuickRecycle
{
    /// <summary>
    /// Tunables, kept together so behaviour can be changed without reading the patch.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Require Shift in addition to Ctrl.
        ///
        /// Ctrl alone is already spoken for: DragController invokes the control-click
        /// callback on Ctrl, and every cargo screen binds that to moving items between the
        /// mercenary's inventory and the hold. Stealing it would break a useful vanilla
        /// action to add one. Shift is free here because DragController's own Shift paths
        /// all test <c>LeftShift &amp;&amp; !LeftControl</c>, so Ctrl+Shift reaches nothing
        /// in the base game.
        /// </summary>
        public const bool RequireShift = true;

        /// <summary>
        /// Only sweep items that are sitting in a ship cargo hold. Equipped armour and
        /// anything in the mercenary's inventory falls through to vanilla Ctrl behaviour.
        /// The whole point of the sweep is speed, and speed plus "recycles what you are
        /// wearing" is how someone loses a suit of Vulture armour.
        /// </summary>
        public const bool CargoOnly = true;

        /// <summary>Seconds between repeats of the refusal sound, so a sweep cannot machine-gun it.</summary>
        public const float DeniedSoundCooldown = 0.35f;

        /// <summary>
        /// Log why a sweep was refused. The sweep has eight separate reasons to decline and
        /// all of them look identical from the player's chair: nothing happens. Guessing
        /// between them from a description of the symptom is slow and unreliable, so the
        /// mod can name the one it took instead.
        ///
        /// On while the gesture is still being shaken out; turn off once it is trusted, as
        /// this fires from a hover path that runs every frame.
        /// </summary>
        public const bool DebugLog = true;
    }
}
