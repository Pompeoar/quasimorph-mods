using System.Collections.Generic;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace PerkCooldownHud.Patches
{
    /// <summary>
    /// Dims the HUD panel while the perk behind it is cooling down. A CanvasGroup is used
    /// rather than tinting each Image because the panel's own Update() drives the white
    /// flash overlay's colour, and a CanvasGroup composes with that instead of fighting it.
    /// </summary>
    [HarmonyPatch(typeof(CommonEffectPanel))]
    public static class CommonEffectPanelPatches
    {
        private static readonly Dictionary<int, CanvasGroup> _groups = new Dictionary<int, CanvasGroup>();

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

        private static void ApplyAlpha(CommonEffectPanel panel, IEffectWithView effect)
        {
            if (panel == null)
            {
                return;
            }

            // Panels are pooled and reused for unrelated effects, so the non-cooldown case
            // must explicitly restore full opacity rather than just skipping.
            var trigger = effect as PerkTrigger;
            var alpha = (trigger != null && !trigger.IsInActivePhase)
                ? Config.CooldownAlpha
                : Config.ActiveAlpha;

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
