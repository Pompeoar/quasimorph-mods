using System;
using MGSC;
using UnityEngine;

namespace EnableConsole
{
    /// <summary>
    /// Local development helper. NOT a Workshop mod - it lives in dev\ and is never staged
    /// to dist\.
    ///
    /// config_globals.txt ships with "Console false", and GameModeStateMachine.LateUpdate
    /// gates the backquote toggle on Data.Global.Console. Without this, the console cannot
    /// be opened, and the console is the only way to reach mod_createworkshopitem and
    /// mod_updateworkshopitem - so publishing anything from this repo depends on it.
    ///
    /// AfterConfigsLoaded is the right hook: Bootstrap runs it immediately after
    /// Data.Load(), so the value set here survives config loading instead of being
    /// overwritten by it.
    /// </summary>
    public static class ModEntry
    {
        public const string ModId = "EnableConsole";

        [Hook(ModHookType.AfterConfigsLoaded)]
        public static void OnAfterConfigsLoaded(IModContext context)
        {
            try
            {
                if (Data.Global == null)
                {
                    Debug.LogWarning("[" + ModId + "] Data.Global was null; console left disabled.");
                    return;
                }

                if (Data.Global.Console)
                {
                    return;
                }

                Data.Global.Console = true;
                Debug.Log("[" + ModId + "] dev console enabled - press ` (backquote) to toggle.");
            }
            catch (Exception e)
            {
                Debug.LogError("[" + ModId + "] failed to enable the console: " + e);
            }
        }
    }
}
