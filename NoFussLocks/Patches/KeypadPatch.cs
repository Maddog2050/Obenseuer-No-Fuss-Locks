using System;
using HarmonyLib;

namespace NoFussLocks.Patches
{
    // Keypad.Use opens the keypad / rotary-dial menu when the keypad is powered and closed.
    // Show the yes/no prompt instead; Yes = Keypad.TriggerSuccess, which is what KeypadUI.SuccessDelay
    // calls after a correct code. Safes (RotaryLock) and number keypads have separate switches.
    [HarmonyPatch(typeof(Keypad), nameof(Keypad.Use))]
    internal static class KeypadPatch
    {
        private static bool Prefix(Keypad __instance)
        {
            // A prompt is up (even if this feature was switched off meanwhile): don't open the keypad menu over it.
            if (PromptUI.IsOpen)
                return false;

            bool safe = __instance.type == Keypad.KeypadType.RotaryLock;
            if (safe ? !Plugin.SafePrompt.Value : !Plugin.KeypadPrompt.Value)
                return true;

            // Same branches as vanilla: unpowered/blocked and open keypads keep their normal behaviour.
            if (__instance.noPower || __instance.open)
                return true;

            // Vanilla opens the keypad through GameUIController.ShowGameMenu, which refuses during a dialogue, while
            // another menu is visible or while the game menu is disabled. Leave those cases to vanilla too.
            var ui = GameUIController.instance;
            if (ui == null || ui.gameMenuDisabled || ui.IsMenuVisible
                || (DialogueController.instance != null && DialogueController.instance.DialogueActive))
                return true;

            var (title, content, confirm) = safe
                ? ("Crack this safe?", "The safe opens without entering the combination.", "Crack safe")
                : ("Bypass this keypad?", "The keypad opens without entering the code.", "Bypass keypad");
            Keypad keypad = __instance;
            bool StillValid() => keypad != null && !keypad.noPower && !keypad.open;
            try
            {
                PromptUI.OpenSimple(StillValid, title, content, confirm, () =>
                {
                    // Checked again: Yes runs one frame after the prompt closes (after controls come back).
                    if (StillValid())
                        keypad.TriggerSuccess();
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Couldn't open the keypad prompt, using the normal keypad: {e}");
                return true;
            }
            return false;
        }
    }
}
