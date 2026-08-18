using System;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace PerkCooldownHud
{
    /// <summary>
    /// Entry point. Quasimorph's loader reflects over public static methods looking for
    /// [Hook(...)] attributes and invokes them.
    /// </summary>
    public static class ModEntry
    {
        public const string ModId = "PerkCooldownHud";

        private static bool _applied;

        [Hook(ModHookType.BeforeBootstrap)]
        public static void OnBeforeBootstrap(IModContext context)
        {
            // UserModSystem.GrabMethods can register the same hook method twice when the
            // hook-type key already exists, so this must be safe to call more than once.
            if (_applied)
            {
                return;
            }

            _applied = true;

            try
            {
                var harmony = new Harmony(ModId);
                harmony.PatchAll(typeof(ModEntry).Assembly);
                Log("patches applied.");
            }
            catch (Exception e)
            {
                Debug.LogError("[" + ModId + "] failed to apply patches: " + e);
            }
        }

        public static void Log(string message)
        {
            Debug.Log("[" + ModId + "] " + message);
        }
    }
}
