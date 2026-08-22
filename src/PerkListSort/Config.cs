namespace PerkListSort
{
    /// <summary>
    /// Tunables, kept together so behaviour can be changed without reading the patch.
    ///
    /// These are static readonly rather than const deliberately. As consts the compiler
    /// folds them into their call sites, so any guard written against a flag's default
    /// value becomes provably dead code and the build fills with CS0162 - which trains you
    /// to ignore warnings in a project where a warning is usually a renamed game member.
    /// </summary>
    public static class Config
    {
        /// <summary>
        /// Sort A to Z. Set false for Z to A.
        /// </summary>
        public static readonly bool Ascending = true;

        /// <summary>
        /// Sort by the localized name shown on the row rather than by the internal perk id.
        ///
        /// On by default because the point of the mod is that the list reads in the order
        /// your eye scans it, and the two disagree: "military_training_basic" displays as
        /// "Training", so sorting by id would file it under M in a list showing T.
        /// </summary>
        public static readonly bool SortByDisplayName = true;
    }
}
