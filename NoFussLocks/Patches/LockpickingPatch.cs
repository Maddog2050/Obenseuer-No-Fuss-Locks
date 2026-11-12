using System;
using HarmonyLib;

namespace NoFussLocks.Patches
{
    // Lock.Use calls PlayerLockpicking.ToggleLockpicking when the player wields a lockpick on a pickable locked lock.
    // Instead of starting the minigame coroutine, show the yes/no prompt.
    [HarmonyPatch(typeof(PlayerLockpicking), nameof(PlayerLockpicking.ToggleLockpicking))]
    internal static class LockpickingPatch
    {
        private static bool Prefix(Action<bool> callbackSuccess, float difficulty, Lock lockToLockpick)
        {
            // A prompt is already up (checked before the on/off switch, so switching the feature off mid-prompt
            // can't start the vanilla minigame on top of it).
            if (PromptUI.IsOpen)
            {
                if (PromptUI.IsActiveLock(lockToLockpick))
                    PromptUI.Cancel(); // same lock: a second toggle stops the attempt, like vanilla (aborts once)
                else
                    PromptUI.EndAttempt(lockToLockpick, fail: true); // other lock: Lock.Use fired onLockpickingStart, so end it as cancelled
                return false;
            }

            if (!Plugin.LockpickingPrompt.Value)
                return true;

            // A vanilla minigame is running (the mod was switched on mid-pick): let the original toggle it off.
            if (PlayerLockpicking.ActiveLockpicking != null)
                return true;

            if (lockToLockpick == null || callbackSuccess == null)
                return true;

            try
            {
                PromptUI.Open(callbackSuccess, difficulty, lockToLockpick);
            }
            catch (Exception e)
            {
                // Lock.Use already fired onLockpickingStart: end the attempt as cancelled instead of leaving it open.
                Plugin.Log.LogError($"Couldn't open the lockpick prompt: {e}");
                if (PromptUI.IsOpen)
                    PromptUI.Cancel(); // aborts the lock once
                else
                    PromptUI.EndAttempt(lockToLockpick, fail: true);
            }
            return false;
        }
    }
}
