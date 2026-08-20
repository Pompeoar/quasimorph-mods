using HarmonyLib;
using MGSC;

namespace BaneCounter.Patches
{
    /// <summary>
    /// Bane is called Curse everywhere in the code - nothing in the assembly says "Bane",
    /// only localization does (ui.label.curse). The number itself is CurseData.CurseLevel,
    /// a plain int that rises in Mercenary.ResetPact() by the cast Pact's CurseValue times
    /// (1 - FPactDebuff), and falls by the ship's MorphanalPactRecovery after each mission
    /// in MissionSystem. Costs run from 7 to 175 per cast across the 142 Pacts.
    ///
    /// The HUD already has an icon for this and it already prints a number. The problem is
    /// which number: vanilla ViewValue returns CursesPower.Count, the count of curse tiers
    /// currently active, which is 1 to 5. So the player sees "2" when what they want to
    /// know is "247". Everything else about the panel - the red border, the flash on
    /// change, the tooltip - already works; only the value is wrong for the question being
    /// asked.
    /// </summary>
    [HarmonyPatch(typeof(CurseEffect))]
    public static class CurseEffectPatches
    {
        /// <summary>
        /// Report the Bane level rather than the number of active curses.
        ///
        /// CommonEffectPanel renders this with EffectViewShowValueFormat.Raw, which is
        /// float.ToString(), so an integral value prints as "247" and not "247.0". No
        /// formatting work is needed.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(CurseEffect.ViewValue), MethodType.Getter)]
        public static void ViewValuePostfix(CurseEffect __instance, ref float __result)
        {
            if (!Config.ShowLevelOnIcon)
            {
                return;
            }

            var curse = CurseOf(__instance);
            if (curse != null)
            {
                __result = curse.CurseLevel;
            }
        }

        /// <summary>
        /// Vanilla blinks and plays the "effect received" chime whenever the number changes.
        /// That was cheap when the number was a 1-to-5 tier count; it now changes on every
        /// Pact cast.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(CurseEffect.BlinkOnChange), MethodType.Getter)]
        public static void BlinkOnChangePostfix(ref bool __result)
        {
            if (Config.ShowLevelOnIcon && !Config.BlinkOnAccrual)
            {
                __result = false;
            }
        }

        /// <summary>
        /// Reads the same field vanilla's ViewValue reads, via the protected _creature on
        /// BaseEffect. Null-tolerant: CurseData is created lazily in Mercenary.OnAfterLoad,
        /// and the effect exists on non-player creatures too.
        /// </summary>
        internal static CurseData CurseOf(CurseEffect effect)
        {
            if (effect == null)
            {
                return null;
            }

            var player = CreatureField(effect) as Player;
            if (player == null || player.Mercenary == null)
            {
                return null;
            }

            return player.Mercenary.CurseData;
        }

        private static readonly AccessTools.FieldRef<BaseEffect, Creature> CreatureField =
            AccessTools.FieldRefAccess<BaseEffect, Creature>("_creature");
    }
}
