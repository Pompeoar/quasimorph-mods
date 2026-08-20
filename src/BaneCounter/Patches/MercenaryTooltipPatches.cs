using HarmonyLib;
using MGSC;

namespace BaneCounter.Patches
{
    /// <summary>
    /// Adds Bane to the operator tooltip on the Manage Operators roster.
    ///
    /// The roster is where the decision actually gets made - you are picking who to send,
    /// and Bane is a property of the clone you are picking. Vanilla's tooltip already
    /// covers health, damage, accuracy, dodge, starvation, sight and pain, but says nothing
    /// about Bane, so the only way to find it was to open the Skull Project screen one
    /// operator at a time.
    ///
    /// Appended in a postfix so vanilla's rows are untouched and these land at the bottom.
    /// </summary>
    [HarmonyPatch(typeof(TooltipFactory))]
    public static class MercenaryTooltipPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(TooltipFactory.BuildMercenaryTooltip))]
        public static void BuildMercenaryTooltipPostfix(TooltipFactory __instance, Mercenary mercenary)
        {
            if (!Config.ShowTooltipDetail || __instance == null || mercenary == null)
            {
                return;
            }

            BaneInfo.AddTooltipRows(__instance, mercenary.CurseData);
        }
    }
}
