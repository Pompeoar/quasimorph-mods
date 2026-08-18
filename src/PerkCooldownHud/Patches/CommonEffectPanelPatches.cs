using System.Collections.Generic;
using HarmonyLib;
using MGSC;
using UnityEngine;
using UnityEngine.UI;

namespace PerkCooldownHud.Patches
{
    /// <summary>
    /// Restyles the HUD panel while the perk behind it is cooling down.
    ///
    /// The border carries the signal: the panel already has a yellow sprite (vanilla uses
    /// it for hover), so cooling down reads as a third state at a glance rather than as a
    /// dimmer shade of "active". Dimming is kept as a secondary cue.
    ///
    /// Alpha is driven by a CanvasGroup rather than by tinting each Image because the
    /// panel's own Update() drives the white flash overlay's colour, and a CanvasGroup
    /// composes with that instead of fighting it.
    /// </summary>
    [HarmonyPatch(typeof(CommonEffectPanel))]
    public static class CommonEffectPanelPatches
    {
        private static readonly Dictionary<int, CanvasGroup> _groups = new Dictionary<int, CanvasGroup>();

        private static readonly AccessTools.FieldRef<CommonEffectPanel, Image> BgField =
            AccessTools.FieldRefAccess<CommonEffectPanel, Image>("_bg");

        private static readonly AccessTools.FieldRef<CommonEffectPanel, Sprite> YellowBorderField =
            AccessTools.FieldRefAccess<CommonEffectPanel, Sprite>("_yellowBorder");

        private static readonly AccessTools.FieldRef<CommonEffectPanel, Sprite> OriginalBgSpriteField =
            AccessTools.FieldRefAccess<CommonEffectPanel, Sprite>("_originalBgSprite");

        private static readonly AccessTools.FieldRef<CommonEffectPanel, List<IEffectWithView>> EffectsField =
            AccessTools.FieldRefAccess<CommonEffectPanel, List<IEffectWithView>>("_effectWithViews");

        [HarmonyPostfix]
        [HarmonyPatch("Initialize", typeof(Creatures), typeof(IEffectWithView), typeof(Sprite))]
        public static void InitializePostfix(CommonEffectPanel __instance, IEffectWithView effectWithView)
        {
            ApplyAlpha(__instance, effectWithView);
        }

        [HarmonyPostfix]
        [HarmonyPatch("RefreshValue", typeof(List<IEffectWithView>))]
        public static void RefreshValuePostfix(CommonEffectPanel __instance, List<IEffectWithView> effects)
        {
            ApplyAlpha(__instance, (effects != null && effects.Count > 0) ? effects[0] : null);
        }

        /// <summary>
        /// Vanilla InitializeBackground picks red or green from IsRedView. It runs at the end
        /// of both Initialize and RefreshValue, after _effectWithViews has been populated, so
        /// it is the one place that needs to know about the third state.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("InitializeBackground")]
        public static void InitializeBackgroundPostfix(CommonEffectPanel __instance)
        {
            if (!Config.YellowBorderWhileCoolingDown || __instance == null)
            {
                return;
            }

            if (!IsCoolingDown(FirstEffect(__instance)))
            {
                return;
            }

            var yellow = YellowBorderField(__instance);
            if (yellow == null)
            {
                return;
            }

            var bg = BgField(__instance);
            if (bg != null)
            {
                bg.sprite = yellow;
            }

            // OnPointerExit restores _originalBgSprite, so this has to move too or hovering
            // a cooling panel would drop it back to the green border.
            OriginalBgSpriteField(__instance) = yellow;
        }

        private static IEffectWithView FirstEffect(CommonEffectPanel panel)
        {
            var effects = EffectsField(panel);
            return (effects != null && effects.Count > 0) ? effects[0] : null;
        }

        private static bool IsCoolingDown(IEffectWithView effect)
        {
            var trigger = effect as PerkTrigger;
            return trigger != null && !trigger.IsInActivePhase;
        }

        private static void ApplyAlpha(CommonEffectPanel panel, IEffectWithView effect)
        {
            if (panel == null)
            {
                return;
            }

            // Panels are pooled and reused for unrelated effects, so the non-cooldown case
            // must explicitly restore full opacity rather than just skipping.
            var alpha = IsCoolingDown(effect) ? Config.CooldownAlpha : Config.ActiveAlpha;

            var group = GetGroup(panel);
            if (group != null && !Mathf.Approximately(group.alpha, alpha))
            {
                group.alpha = alpha;
            }
        }

        private static CanvasGroup GetGroup(CommonEffectPanel panel)
        {
            var key = panel.GetInstanceID();

            CanvasGroup group;
            if (_groups.TryGetValue(key, out group) && group != null)
            {
                return group;
            }

            group = panel.gameObject.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = panel.gameObject.AddComponent<CanvasGroup>();
            }

            _groups[key] = group;
            return group;
        }
    }
}
