using HarmonyLib;
using MGSC;

namespace PerkCooldownHud.Patches
{
    /// <summary>
    /// A PerkTrigger effect exists on the creature from the moment a triggered perk fires
    /// until it is ready again (PerkSystem.ApplyPerkTrigger only runs when GetTrigger
    /// returns null, and the effect is removed when Duration reaches 0). Its single
    /// Duration counts down the active phase first and the cooldown afterwards.
    ///
    /// Vanilla hides the HUD panel for the whole cooldown half because Show is hardcoded
    /// to IsInActivePhase. These patches surface it instead.
    /// </summary>
    [HarmonyPatch(typeof(PerkTrigger))]
    public static class PerkTriggerPatches
    {
        /// <summary>Keep the panel on screen through the cooldown, not just the active phase.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PerkTrigger.Show), MethodType.Getter)]
        public static void ShowPostfix(ref bool __result)
        {
            __result = true;
        }

        /// <summary>
        /// Vanilla ViewValue counts down the remaining active turns and then runs negative
        /// once the active phase ends. During cooldown, report Duration instead - which is
        /// what MercenaryClassScreen already treats as "turns of cooldown remaining".
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PerkTrigger.ViewValue), MethodType.Getter)]
        public static void ViewValuePostfix(PerkTrigger __instance, ref float __result)
        {
            if (!__instance.IsInActivePhase)
            {
                __result = __instance.Duration;
            }
        }

        /// <summary>
        /// CommonEffectPanel flashes white and plays the "effect received" sound whenever the
        /// displayed value changes. The cooldown counter changes every turn, so leaving this
        /// alone would ping the player once per turn for the entire cooldown.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PerkTrigger.BlinkOnChange), MethodType.Getter)]
        public static void BlinkOnChangePostfix(PerkTrigger __instance, ref bool __result)
        {
            __result = __instance.IsInActivePhase;
        }

        /// <summary>Optional: flip the panel border to the red "bad effect" sprite while cooling.</summary>
        [HarmonyPostfix]
        [HarmonyPatch(nameof(PerkTrigger.IsRedView), MethodType.Getter)]
        public static void IsRedViewPostfix(PerkTrigger __instance, ref bool __result)
        {
            if (Config.RedBorderWhileCoolingDown && !__instance.IsInActivePhase)
            {
                __result = true;
            }
        }
    }
}
