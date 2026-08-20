using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QuickRecycle.Patches
{
    /// <summary>
    /// Ctrl+Shift on an item in a cargo hold sends it straight to the Recycling tab.
    ///
    /// Why this seam. DragController already owns a "modifier + item slot" gesture: on
    /// Ctrl it raises _controlClickCallback, and with the "Fast item transfer with CTRL"
    /// setting enabled it raises it on *hover*, every frame, with no click at all
    /// (DragController line 374). Hooking that callback means the sweep behaviour comes
    /// free and matches a control the player has already opted into, rather than inventing
    /// a second input path that behaves subtly differently.
    ///
    /// Ctrl alone is taken - every cargo screen binds it to moving items between the
    /// mercenary and the hold - so this requires Shift as well. Ctrl+Shift is genuinely
    /// unclaimed: DragController's Shift branches all test
    /// <c>LeftShift &amp;&amp; !LeftControl</c>, so holding both reaches nothing in vanilla.
    ///
    /// ArsenalScreen, FastTradeScreen and TradeShuttleScreen all override the callback, so
    /// patching only the base class would silently miss the screen this is actually for.
    /// Both the base and ArsenalScreen are patched below.
    /// </summary>
    public static class RecycleSweepPatches
    {
        private static float _lastDeniedSound;

        [HarmonyPatch(typeof(ScreenWithShipCargo), "DragControllerControlClickCallback")]
        public static class BasePatch
        {
            public static bool Prefix(ScreenWithShipCargo __instance, ItemSlot obj)
            {
                return !TrySweepToRecycler(__instance, obj);
            }
        }

        [HarmonyPatch(typeof(ArsenalScreen), "DragControllerControlClickCallback")]
        public static class ArsenalPatch
        {
            public static bool Prefix(ArsenalScreen __instance, ItemSlot obj)
            {
                return !TrySweepToRecycler(__instance, obj);
            }
        }

        /// <summary>
        /// Returns true when this gesture was handled here and vanilla should be skipped.
        /// Every rejection returns false so the original Ctrl behaviour still runs - a
        /// modifier that silently does nothing is worse than one that does the old thing.
        ///
        /// Shared by both entry points: the Ctrl hover/click callback and the BeginDrag
        /// interception in ClickSweepPatches. One set of rules, one set of refusals.
        /// </summary>
        internal static bool TrySweepToRecycler(ScreenWithShipCargo screen, ItemSlot slot)
        {
            Trace("gesture reached " + screen.GetType().Name);

            if (Config.RequireShift && !InputHelper.GetKey(KeyCode.LeftShift))
            {
                return Decline("Ctrl is held but Shift is not");
            }

            var cargo = MagnumCargoRef(screen);
            var ship = MagnumSpaceshipRef(screen);
            if (cargo == null || ship == null)
            {
                return Decline("screen has no cargo/spaceship reference");
            }

            // The same condition ScreenWithShipCargo.Configure uses to decide whether the
            // recycling tab exists at all. Without the department there is nowhere to send
            // anything, so leave Ctrl doing its normal job.
            if (!ship.HasStoreConstructorDepartment)
            {
                return Decline("no store constructor department, so no recycling tab");
            }

            var item = slot == null ? null : slot.Item;
            if (item == null || item.Storage == null)
            {
                return Decline("empty slot or item with no storage");
            }

            var target = cargo.RecyclingStorage;
            if (item.Storage == target)
            {
                return Decline("item is already in the recycler");
            }

            if (Config.CargoOnly && item.Storage.Source != ItemStorageSource.ShipCargo)
            {
                return Decline("item is in " + item.Storage.Source + ", not ShipCargo");
            }

            // The gotcha: while a batch is running the recycler is sealed. ItemTab.
            // DropItemInTab refuses outright for TabType.RecycleInProgress, and
            // MagnumCargoSystem.AddCargo quietly reroutes to ShipCargo[0] instead. Neither
            // is right for a sweep - one would drop items on the floor of the first hold
            // without saying so. Refuse audibly and let the player see the timer.
            if (cargo.RecyclingInProgress)
            {
                Trace("refused: a batch is already recycling");
                PlayDenied();
                return true;
            }

            // Vanilla's own rule for this storage, from ScreenWithShipCargo.
            // CanDropItemInStorage. Locked is the story/quest hold that
            // ItemInteractionSystem.Repair also refuses to touch.
            if (ItemInteractionSystem.IsQuestItem(item) || item.Locked)
            {
                Trace("refused: quest or locked item");
                PlayDenied();
                return true;
            }

            item.Storage.Remove(item);

            // Matches ScreenWithShipCargo.DropItemInTab. The fallback expands the grid by a
            // row rather than failing, so a full recycler is not a dead end mid-sweep.
            MagnumCargoSystem.PutItemWithFallback(cargo, target, item);

            SingletonMonoBehaviour<SoundController>.Instance.PlayUiSound(
                SingletonMonoBehaviour<SoundsStorage>.Instance.TakeItem, true);

            screen.RefreshView();
            Trace("swept " + item.Id + " to the recycler");
            return true;
        }

        /// <summary>
        /// Records a refusal and returns false. Deduplicated because the hover path calls
        /// this every frame the cursor sits on a slot; without that, one second of hovering
        /// buries the log in a thousand copies of the same line.
        /// </summary>
        private static bool Decline(string reason)
        {
            Trace("declined: " + reason);
            return false;
        }

        private static string _lastTrace;

        private static void Trace(string message)
        {
            if (!Config.DebugLog || message == _lastTrace)
            {
                return;
            }

            _lastTrace = message;
            ModEntry.Log(message);
        }

        /// <summary>
        /// Rate-limited: with fast transfer enabled the callback fires every frame the
        /// cursor is over a slot, so an unthrottled refusal is a buzzsaw.
        /// </summary>
        private static void PlayDenied()
        {
            if (Time.unscaledTime - _lastDeniedSound < Config.DeniedSoundCooldown)
            {
                return;
            }

            _lastDeniedSound = Time.unscaledTime;
            SingletonMonoBehaviour<SoundController>.Instance.PlayUiSound(
                SingletonMonoBehaviour<SoundsStorage>.Instance.EmptyAttack);
        }

        private static readonly AccessTools.FieldRef<ScreenWithShipCargo, MagnumCargo> MagnumCargoRef =
            AccessTools.FieldRefAccess<ScreenWithShipCargo, MagnumCargo>("_magnumCargo");

        private static readonly AccessTools.FieldRef<ScreenWithShipCargo, MagnumProgression> MagnumSpaceshipRef =
            AccessTools.FieldRefAccess<ScreenWithShipCargo, MagnumProgression>("_magnumSpaceship");
    }
}
