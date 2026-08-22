using System;
using System.Collections.Generic;
using HarmonyLib;
using MGSC;

namespace PerkListSort.Patches
{
    /// <summary>
    /// Sorts the perk / talent picker that opens from a class project.
    ///
    /// Vanilla builds the list in MagnumProjectSelectAbilityWindow.Show by walking
    /// Data.MercenaryClasses.Records in config order, and each class's PerkIds in config
    /// order, skipping classes that are not unlocked and perks already added. That order is
    /// then turned upside down, because AddPanel ends with SetAsFirstSibling: every new row
    /// is inserted above the last, so the list renders as the exact reverse of the order it
    /// was built in. The result is neither alphabetical nor unlock order - it is the config
    /// file, backwards. Scouts of Hades is first in config, so its perks are always at the
    /// bottom.
    /// </summary>
    [HarmonyPatch(typeof(MagnumProjectSelectAbilityWindow))]
    public static class SelectAbilityWindowPatches
    {
        private static readonly AccessTools.FieldRef<MagnumProjectSelectAbilityWindow, List<MagnumProjectAbilityPanel>> PanelsRef =
            AccessTools.FieldRefAccess<MagnumProjectSelectAbilityWindow, List<MagnumProjectAbilityPanel>>("_panels");

        private static readonly AccessTools.FieldRef<MagnumProjectAbilityPanel, string> PerkIdRef =
            AccessTools.FieldRefAccess<MagnumProjectAbilityPanel, string>("_perkId");

        private static bool _failed;

        [HarmonyPostfix]
        [HarmonyPatch("Show")]
        public static void ShowPostfix(MagnumProjectSelectAbilityWindow __instance)
        {
            if (_failed)
            {
                return;
            }

            try
            {
                Sort(__instance);
            }
            catch (Exception e)
            {
                // One failure disables the feature rather than throwing on every open. A
                // broken sort must not be able to make the picker unusable - an ugly list
                // beats no list.
                _failed = true;
                ModEntry.Log("sorting failed, leaving the list in vanilla order: " + e);
            }
        }

        private static void Sort(MagnumProjectSelectAbilityWindow window)
        {
            var panels = PanelsRef(window);
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            // Sorted on a copy: _panels is the window's own bookkeeping, and FreePanels
            // iterates it to return rows to the pool. Reordering it in place would work
            // today but couples this mod to an implementation detail it has no need to
            // touch. Only sibling order decides what the player sees.
            var sorted = new List<MagnumProjectAbilityPanel>(panels);
            sorted.Sort(Compare);

            // Assigning 0..n-1 in order is safe: moving a row to index i only shifts rows
            // already at i or beyond, so rows placed earlier in the loop stay put.
            //
            // It also cleans up after the pool. MagnumProjectSelectAbilityWindow.FreePanels
            // calls Pool.Put without setParent, and Put only reparents when asked, so rows
            // from previous openings are still children of the list root - just deactivated.
            // Packing the live rows into the first n slots pushes those leftovers below all
            // of them. Anything that walked the root's children instead of this list would
            // be sorting those ghosts in among the real rows.
            for (var i = 0; i < sorted.Count; i++)
            {
                var panel = sorted[i];
                if (panel != null)
                {
                    panel.transform.SetSiblingIndex(i);
                }
            }
        }

        private static int Compare(MagnumProjectAbilityPanel a, MagnumProjectAbilityPanel b)
        {
            var result = string.Compare(KeyOf(a), KeyOf(b), StringComparison.CurrentCultureIgnoreCase);
            return Config.Ascending ? result : -result;
        }

        /// <summary>
        /// The string the row is sorted on.
        ///
        /// Deliberately rebuilt from the perk id rather than read off the row's label. The
        /// label has already been through ColorFirstLetter, which wraps the first character
        /// in a rich-text colour tag, so every caption starts with the same markup and the
        /// first character that actually differs sits several characters in. Comparing those
        /// strings would appear to work and would quietly mis-sort the moment the markup
        /// changed. This resolves the same localization key the row does, without the
        /// decoration.
        /// </summary>
        private static string KeyOf(MagnumProjectAbilityPanel panel)
        {
            if (panel == null)
            {
                return string.Empty;
            }

            var perkId = PerkIdRef(panel);
            if (string.IsNullOrEmpty(perkId))
            {
                return string.Empty;
            }

            if (!Config.SortByDisplayName)
            {
                return perkId;
            }

            var name = Localization.Get("perk." + FormatHelper.ClearPerkGrades(perkId) + ".name");
            return string.IsNullOrEmpty(name) ? perkId : name;
        }
    }
}
