using System.Collections.Generic;
using HarmonyLib;
using MGSC;

namespace BaneCounter.Patches
{
    /// <summary>
    /// Adds the numbers behind the HUD icon to the tooltip that already exists.
    ///
    /// Vanilla's Bane tooltip lists each active curse and how strong it currently is, but
    /// never states the level those curses are derived from, so there is no way to see how
    /// close the next one is.
    ///
    /// This is a postfix on InitTooltip rather than a replacement so the vanilla panels are
    /// left exactly as they are and these rows land underneath them.
    /// </summary>
    [HarmonyPatch(typeof(CommonEffectPanel))]
    public static class CurseTooltipPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch("InitTooltip")]
        public static void InitTooltipPostfix(CommonEffectPanel __instance)
        {
            if (!Config.ShowTooltipDetail || __instance == null)
            {
                return;
            }

            // Vanilla sets this only on the branches that actually built a tooltip. Without
            // the check we would append rows onto whatever tooltip happens to be on screen.
            if (!CreatedTooltipField(__instance))
            {
                return;
            }

            var effects = EffectsField(__instance);
            if (effects == null || effects.Count == 0)
            {
                return;
            }

            var effect = effects[0] as CurseEffect;
            if (effect == null)
            {
                return;
            }

            BaneInfo.AddTooltipRows(
                SingletonMonoBehaviour<TooltipFactory>.Instance,
                CurseEffectPatches.CurseOf(effect));
        }

        private static readonly AccessTools.FieldRef<CommonEffectPanel, List<IEffectWithView>> EffectsField =
            AccessTools.FieldRefAccess<CommonEffectPanel, List<IEffectWithView>>("_effectWithViews");

        private static readonly AccessTools.FieldRef<CommonEffectPanel, bool> CreatedTooltipField =
            AccessTools.FieldRefAccess<CommonEffectPanel, bool>("_createdTooltip");
    }
}
