using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QuickRecycle.Patches
{
    /// <summary>
    /// Makes Ctrl+Shift+<i>click</i> sweep as well as Ctrl+Shift+hover.
    ///
    /// Without this, the gesture behaves differently depending on a setting the player set
    /// months ago and has forgotten about. DragController.Update, with fast transfer
    /// enabled, routes a click like this:
    ///
    ///   if (GetMouseButtonUp(0))
    ///       if (!FastToss &amp;&amp; Ctrl)  -> control-click callback   // false when FastToss is on
    ///       else                          -> BeginDrag()               // item lands on the cursor
    ///   else if (FastToss &amp;&amp; no button &amp;&amp; !IsDragging &amp;&amp; Ctrl)
    ///                                     -> control-click callback   // hover only
    ///
    /// So with the setting ON a click can never reach the transfer path - it always picks
    /// the item up - and with it OFF a click is the *only* way to reach it. Hooking
    /// BeginDrag closes that gap, so Ctrl+Shift means the same thing in both modes.
    ///
    /// BeginDrag is global (it lives on the UI.Drag singleton) and knows nothing about
    /// which screen is open, hence the screen tracking below.
    /// </summary>
    public static class ClickSweepPatches
    {
        [HarmonyPatch(typeof(DragController), "BeginDrag")]
        public static class BeginDragPatch
        {
            public static bool Prefix(ItemSlot draggableSlot)
            {
                var screen = CargoScreenTracker.Current;
                if (screen == null || draggableSlot == null)
                {
                    return true;
                }

                // Same helper the hover path uses, so both gestures share one set of rules
                // and one set of refusals rather than drifting apart.
                return !RecycleSweepPatches.TrySweepToRecycler(screen, draggableSlot);
            }
        }
    }

    /// <summary>
    /// Remembers which cargo screen is open, because DragController.BeginDrag has no way to
    /// ask. Configure runs on every screen that shows cargo, and ArsenalScreen calls it too,
    /// so one patch covers them all without touching each subclass.
    /// </summary>
    public static class CargoScreenTracker
    {
        public static ScreenWithShipCargo Current { get; private set; }

        [HarmonyPatch(typeof(ScreenWithShipCargo), "Configure")]
        public static class ConfigurePatch
        {
            public static void Postfix(ScreenWithShipCargo __instance)
            {
                Current = __instance;
            }
        }

        /// <summary>
        /// ArsenalScreen overrides OnDisable but calls base.OnDisable() first, so patching
        /// the base still fires. Clearing matters: a stale reference to a disabled screen
        /// would let a click on some unrelated window try to move items into the recycler.
        /// </summary>
        [HarmonyPatch(typeof(ScreenWithShipCargo), "OnDisable")]
        public static class OnDisablePatch
        {
            public static void Postfix(ScreenWithShipCargo __instance)
            {
                if (Current == __instance)
                {
                    Current = null;
                }
            }
        }
    }
}
