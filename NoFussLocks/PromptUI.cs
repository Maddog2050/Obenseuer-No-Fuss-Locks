using System;
using System.Collections.Generic;
using UnityEngine;

namespace NoFussLocks
{
    // The Yes/No box shown instead of the lockpicking, safe and keypad minigames, drawn with IMGUI (no game UI).
    // While it is up, the player's controls and the game menu are disabled; they come back after it closes, and only
    // then does the Yes action run. Every frame it checks that its target still makes sense (lock still locked,
    // keypad still there, powered and closed) and closes if not.
    //
    // Two modes:
    // - OpenSimple (safes, keypads): Yes runs the given action (Keypad.TriggerSuccess); No does nothing.
    // - Open (locks): Yes starts a short "Picking the lock..." phase of PickTime seconds, during which witnesses are
    //   checked like in the vanilla minigame, then the lock opens with vanilla skill gain (capped) and achievements.
    //   No or Stop ends the attempt like cancelling the minigame.
    internal static class PromptUI
    {
        private enum State { Closed, Asking, Picking }
        private enum Mode { Lock, Simple } // Open (lockpicking) or OpenSimple (safe/keypad)
        private static Mode mode;

        private static State state = State.Closed;
        private static Action<bool> callback;
        private static Lock activeLock;
        private static float difficulty;
        private static int stateFrame;
        private static float pickStart;
        private static float pickDuration;
        private static bool caught;
        private static float lastWitnessCheck;
        private static string title = "";
        private static string content = "";
        private static string confirmText = "";
        private static Action simpleYes; // set for OpenSimple prompts; null for lockpicking
        private static Func<bool> simpleValid; // OpenSimple: is the keypad/safe still there, powered and closed?
        private static Action afterRestore; // the Yes action, run right after controls/menu are restored (RestoreControls)
        private static bool witnessErrorLogged;
        // The controllers whose controls/menu were disabled on open, so Close re-enables exactly those.
        private static GameController controlsOwner;
        private static GameUIController menuOwner;
        private static bool menuWasDisabled; // someone else had disabled the game menu before the prompt opened
        private enum Click { None, Yes, No }
        private static Click pendingClick; // IMGUI button clicks, acted on in LateUpdate (see OnGUI)
        private static int restoreFrame = -1; // frame on which Close ran; controls/menu come back on the next frame

        private static GUIStyle titleStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle hintStyle;
        private static GUIStyle contentStyle;
        private static Texture2D barTexture;

        internal static bool IsOpen => state != State.Closed;

        internal static void Open(Action<bool> callbackSuccess, float lockDifficulty, Lock lockToLockpick)
        {
            if (IsOpen)
                return;
            RestoreControls(); // finish the previous prompt (restore + its Yes action) before any field is set

            // The game's own label, e.g. "(Lockpick — Easy)", so the thresholds can't drift from vanilla.
            // It reads PlayerStats.instance, so skip it if that isn't there (e.g. mid scene change).
            string text = PlayerStats.instance != null
                ? PlayerLockpicking.ConstructLockpickMessage(lockDifficulty, lockToLockpick).Trim('(', ')') + "."
                : "";
            if (!Plugin.BypassWitnesses.Value)
                text += (text.Length > 0 ? " " : "") + "Anyone who sees you will report a burglary.";

            // Assigned only once nothing above can throw, so a failed open leaves no stale lock behind.
            mode = Mode.Lock;
            callback = callbackSuccess;
            activeLock = lockToLockpick;
            difficulty = lockDifficulty;
            simpleYes = null;
            simpleValid = null;
            Show("Pick this lock?", text, "Pick lock");
        }

        internal static bool IsActiveLock(Lock l) => l != null && activeLock == l; // activeLock is cleared on Close

        // Lock is a plain C# class, so "lock == null" never sees its door/container being destroyed. Lock.Use stores
        // the owner GameObject in _parent for the attempt; Unity's null check on that tells whether the owner (and
        // the Relay components the lock fires) still exist.
        private static bool LockAlive(Lock l) => l != null && l._parent != null;

        // stillValid: whether Yes still makes sense (keypad not destroyed, powered, not opened meanwhile); checked every
        // frame, and the prompt closes as soon as it returns false.
        internal static void OpenSimple(Func<bool> stillValid, string promptTitle, string promptContent, string promptConfirm, Action onYes)
        {
            if (IsOpen)
                return;
            RestoreControls(); // finish the previous prompt (restore + its Yes action) before any field is set

            mode = Mode.Simple;
            callback = null;
            activeLock = null;
            simpleYes = onYes;
            simpleValid = stillValid;
            Show(promptTitle, promptContent, promptConfirm);
        }

        // The prompt only counts as open once the menu and controls are disabled. If disabling throws, whatever was
        // disabled is restored, the fields are cleared and the exception goes to the patch, which leaves the use to
        // vanilla or ends the lock attempt.
        private static void Show(string promptTitle, string promptContent, string promptConfirm)
        {
            title = promptTitle;
            content = promptContent;
            confirmText = promptConfirm;
            pendingClick = Click.None;
            hint = null; // key bindings may have changed since the last prompt

            try
            {
                // Each owner is remembered only after its disable call returned, so the restore never re-enables
                // something this prompt didn't disable.
                var menu = GameUIController.instance;
                if (menu != null)
                {
                    menuWasDisabled = menu.gameMenuDisabled;
                    menu.DisableGameMenu();
                    menuOwner = menu;
                }
                var controls = GameController.instance;
                if (controls != null && Plugin.ControlsKey != null)
                {
                    controls.ControlsDisabled(Plugin.ControlsKey, ShowCursor: true);
                    controlsOwner = controls;
                }
            }
            catch
            {
                callback = null;
                activeLock = null;
                simpleYes = null;
                simpleValid = null;
                restoreFrame = Time.frameCount;
                RestoreControls();
                throw;
            }

            SetState(State.Asking);
        }

        private static void SetState(State s)
        {
            state = s;
            stateFrame = Time.frameCount;
        }

        // Closes the prompt. The player's controls and the menu come back on the next frame (RestoreControls), so no
        // game script that reads the same key press later in this frame sees them already enabled.
        // Returns false if it was not open.
        private static bool Close()
        {
            if (!IsOpen)
                return false;
            SetState(State.Closed);
            callback = null;
            activeLock = null;
            simpleYes = null;
            simpleValid = null;
            restoreFrame = Time.frameCount;
            return true;
        }

        private static void RestoreControls()
        {
            if (restoreFrame < 0)
                return;
            restoreFrame = -1;
            if (controlsOwner != null && Plugin.ControlsKey != null)
                controlsOwner.ControlsEnabled(Plugin.ControlsKey);
            if (menuOwner != null && !menuWasDisabled)
                menuOwner.EnableGameMenu();
            controlsOwner = null;
            menuOwner = null;

            // The Yes action runs after the menu is enabled again, so if it (or a relay it fires) starts something
            // that disables the menu, that disable is the one that stays.
            var action = afterRestore;
            afterRestore = null;
            if (action != null)
            {
                try { action(); }
                catch (Exception e) { Plugin.Log.LogError($"Yes action failed: {e}"); }
            }
        }

        private static void StartPicking()
        {
            if (state != State.Asking) // LateUpdate has just checked EndIfLockChanged
                return;
            if (mode == Mode.Simple)
            {
                afterRestore = simpleYes;
                Close();
                return;
            }

            caught = false;
            witnessErrorLogged = false;
            lastWitnessCheck = float.NegativeInfinity;
            pickStart = Time.time;
            pickDuration = Plugin.PickTime.Value; // kept in 0..10 by its AcceptableValueRange
            SetState(State.Picking);
            CheckWitnesses();
            if (pickDuration <= 0f)
                Finish();
        }

        // Same rules as PlayerLockpicking.LockpickingProcess: at most one burglary per attempt, and at most one check
        // per 0.01 s (its WaitForSeconds(0.01f)), so the odds don't grow with the frame rate (cameras have no cooldown).
        private static void CheckWitnesses()
        {
            if (caught || Plugin.BypassWitnesses.Value || Time.time - lastWitnessCheck < 0.01f)
                return;
            lastWitnessCheck = Time.time;
            try
            {
                if (Witness.IsWitnessed() && Crime.instance != null)
                {
                    caught = true;
                    Crime.instance.CommitCrime(null, (int)(PlayerLockpicking.Severity * difficulty), Crime.CrimeType.Burglary);
                }
            }
            catch (Exception e)
            {
                // A failing witness (e.g. an NPC being destroyed) must not stop the pick from finishing; keep checking
                // the others, but log only once per attempt.
                if (!witnessErrorLogged)
                    Plugin.Log.LogWarning($"Witness check failed: {e}");
                witnessErrorLogged = true;
            }
        }

        private static void Finish()
        {
            var cb = callback;
            var lk = activeLock;
            var diff = difficulty;
            if (Close())
                afterRestore = () => Succeed(cb, lk, diff);
        }

        private static void Succeed(Action<bool> cb, Lock lk, float diff)
        {
            // The lock's door/container was destroyed since: end the attempt as aborted.
            if (!LockAlive(lk) || cb == null)
            {
                EndAttempt(lk, fail: true);
                return;
            }
            // Opened some other way in the frame since Finish: no skill/achievements, no second unlock.
            if (!lk.Locked)
            {
                EndAttempt(lk, fail: false);
                return;
            }

            // Skill and achievements are extras: if they fail, the lock still gets its success callback.
            try
            {
                if (Plugin.GainSkill.Value && PlayerStats.instance != null)
                {
                    // Same formula as PlayerLockpicking.LockpickingProcess on success (inline there; the game has no helper to call).
                    float chance = PlayerLockpicking.LockpickChance(diff);
                    float value = chance > 0f ? 1f / (chance * chance + 35f * chance) : 1f;
                    // Capped, because every pick succeeds here: uncapped, a hard lock pays the maximum 1.0 on every pick.
                    float max = Plugin.MaxSkillGain.Value; // 0.01..1 by its AcceptableValueRange
                    PlayerStats.instance.IncreaseSkill(PlayerStats.PlayerSkillType.Lockpicking, Mathf.Clamp(value, 0.01f, max));
                }

                // Vanilla PlayerLockpicking.LockpickingProcess triggers all three (+1 each) on every success, whatever the
                // lock's difficulty: they are counters toward different totals, not difficulty tiers.
                if (Achievements.instance != null)
                {
                    Achievements.instance.TriggerAchievement("BURGLAR_LOW", 1);
                    Achievements.instance.TriggerAchievement("BURGLAR_MED", 1);
                    Achievements.instance.TriggerAchievement("BURGLAR_HIGH", 1);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Skill/achievement update failed: {e}");
            }

            cb(true); // Lock.LockpickSuccessCallback: unlocks, fires onLockpickingEnd
        }

        // If the lock was opened some other way (key, script) or its door/container destroyed since the prompt opened,
        // or a keypad prompt's keypad is gone, unpowered or opened, end the prompt and return true. No skill, achievement or burglary then.
        private static bool EndIfLockChanged()
        {
            if (mode == Mode.Simple)
            {
                if (simpleValid != null && simpleValid())
                    return false;
                Close();
                return true;
            }
            var lk = activeLock;
            if (LockAlive(lk) && lk.Locked)
                return false;
            Close();
            EndAttempt(lk, fail: !LockAlive(lk)); // destroyed: aborted; opened elsewhere: no failure relay
            return true;
        }

        // Ends the attempt Lock.Use started (it fired onLockpickingStart): like Lock.AbortLockpicking when fail is true,
        // otherwise only onLockpickingEnd. Unlike the game's own "?." calls, the relays get Unity's null check, because
        // they may have been destroyed with their scene.
        internal static void EndAttempt(Lock lk, bool fail)
        {
            if (lk == null)
                return;
            lk._parent = null;
            lk._CallbackUnlock = null;
            // A relay can still exist while its targets are being torn down (scene change): don't let that throw
            // into the caller (a SceneManager event, a Harmony prefix).
            try
            {
                if (fail && lk.onLockpickingFail != null)
                    lk.onLockpickingFail.triggerOutputs();
                if (lk.onLockpickingEnd != null)
                    lk.onLockpickingEnd.triggerOutputs();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Lock relay failed: {e}");
            }
        }

        internal static void Cancel()
        {
            var lk = activeLock;
            if (Close())
                EndAttempt(lk, fail: true);
        }

        private static bool YesPressed()
        {
            return Input.GetKeyDown(KeyCode.Y) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        }

        // Only on a controller: on keyboard and mouse the "Move Item" binding may be a key or button the player
        // presses for other reasons.
        private static bool ControllerConfirmPressed()
        {
            return UsingController() && InputManager.instance.GetKeyDown("Move Item");
        }

        private static bool UsingController()
        {
            var input = InputManager.instance;
            return input != null && input.inputType == InputManager.InputType.Controller;
        }

        // OnGUI runs several times per frame, so the hint is only rebuilt when the state or the input type changes.
        private static string hint;
        private static State hintState;
        private static bool hintController;

        // Same format as the game's key hints (InventoryNavigationHandler.KeyCodeInfoToString, e.g. the shop's
        // "F)BUY/SELL   E/Q)CATEGORY   Tab)EXIT"): key, ")", the action in capitals, entries three spaces apart.
        private static string HintText()
        {
            bool controller = UsingController();
            if (hint == null || hintState != state || hintController != controller)
            {
                hintState = state;
                hintController = controller;
                string yes = controller ? BindingText("Move Item") : "Y/Enter";
                string no = controller ? BindingText("Menu", "Pause Menu") : "N/ESC";
                hint = state == State.Picking
                    ? HintEntry(no, "STOP")
                    : JoinHints(HintEntry(yes, "YES"), HintEntry(no, "NO"));
            }
            return hint;
        }

        private static string HintEntry(string keys, string action)
        {
            return string.IsNullOrEmpty(keys) ? "" : keys + ")" + action;
        }

        private static string JoinHints(string a, string b)
        {
            return a.Length == 0 ? b : b.Length == 0 ? a : a + "   " + b;
        }

        // The player's current bindings for these actions as the game displays them ("A", "Menu", ...), joined
        // with "/" and without repeats; empty when none is bound.
        private static string BindingText(params string[] actions)
        {
            var input = InputManager.instance;
            var shown = new List<KeyCode>();
            foreach (var action in actions)
            {
                var code = input.GetKeyCode(action);
                if (code != KeyCode.None && !shown.Contains(code))
                    shown.Add(code);
            }
            return string.Join("/", shown.ConvertAll(input.GetKeyCodeDisplayText));
        }

        // Y/N/Enter/Esc are safe on keyboard: the game's own key handling is disabled while the prompt is up.
        // The game's "Menu"/"Pause Menu" bindings count only on a controller; on keyboard they may be keys like
        // the inventory key that the player presses out of habit.
        private static bool NoPressed()
        {
            return Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.Escape)
                || (UsingController() && (InputManager.instance.GetKeyDown("Menu") || InputManager.instance.GetKeyDown("Pause Menu")));
        }

        // Called from the plugin's LateUpdate, after the game's Update, so a key that closes the prompt (Esc)
        // is not also read by the game in the same frame.
        internal static void LateUpdate()
        {
            if (restoreFrame >= 0 && Time.frameCount > restoreFrame)
                RestoreControls();
            // Skip the frame the state changed on, so the key press that caused it isn't read again.
            if (state == State.Closed || Time.frameCount <= stateFrame)
                return;

            var click = pendingClick;
            pendingClick = Click.None;

            if (EndIfLockChanged())
                return;

            if (state == State.Asking)
            {
                if (click == Click.Yes || YesPressed() || ControllerConfirmPressed())
                    StartPicking();
                else if (click == Click.No || NoPressed())
                    Cancel();
                return;
            }

            // Picking. (Vanilla's "keep looking at the lock" rule isn't needed: the camera is locked while the
            // prompt is up.)
            if (click == Click.No || NoPressed())
            {
                Cancel();
                return;
            }
            CheckWitnesses();
            if (Time.time - pickStart >= pickDuration)
                Finish();
        }

        internal static void OnGUI()
        {
            if (state == State.Closed)
                return;

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = Color.white;
                contentStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                contentStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18 };
                hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                hintStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            }
            if (barTexture == null)
            {
                barTexture = new Texture2D(1, 1);
                barTexture.SetPixel(0, 0, new Color(0.85f, 0.75f, 0.35f));
                barTexture.Apply();
            }

            const float w = 380f, h = 190f;
            var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(rect, GUIContent.none);
            GUI.Box(rect, GUIContent.none); // twice for a darker backdrop

            if (state == State.Asking)
            {
                GUI.Label(new Rect(rect.x, rect.y + 12f, w, 32f), title, titleStyle);
                GUI.Label(new Rect(rect.x + 15f, rect.y + 46f, w - 30f, 44f), content, contentStyle);

                const float bw = 140f, bh = 40f, gap = 20f;
                float bx = rect.x + (w - 2f * bw - gap) / 2f;
                float by = rect.y + 100f;
                // Only record the click: the lock/keypad callbacks then run from LateUpdate, the same as for keys,
                // not in the middle of IMGUI event processing.
                if (GUI.Button(new Rect(bx, by, bw, bh), confirmText, buttonStyle))
                    pendingClick = Click.Yes;
                else if (GUI.Button(new Rect(bx + bw + gap, by, bw, bh), "Cancel", buttonStyle))
                    pendingClick = Click.No;

                GUI.Label(new Rect(rect.x, rect.y + h - 34f, w, 24f), HintText(), hintStyle);
            }
            else
            {
                GUI.Label(new Rect(rect.x, rect.y + 12f, w, 32f), "Picking the lock...", titleStyle);

                float progress = pickDuration > 0f ? Mathf.Clamp01((Time.time - pickStart) / pickDuration) : 1f;
                var bar = new Rect(rect.x + 30f, rect.y + 58f, w - 60f, 20f);
                GUI.Box(bar, GUIContent.none);
                GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * progress, bar.height - 4f), barTexture);

                // Where the Cancel button was, not where "Pick lock" was, so a double click on "Pick lock" can't hit Stop.
                const float bw = 140f, bh = 40f, gap = 20f;
                float bx = rect.x + (w - 2f * bw - gap) / 2f + bw + gap;
                if (GUI.Button(new Rect(bx, rect.y + 100f, bw, bh), "Stop", buttonStyle))
                    pendingClick = Click.No;

                GUI.Label(new Rect(rect.x, rect.y + h - 34f, w, 24f), HintText(), hintStyle);
            }
        }

        internal static void Dispose()
        {
            Cancel();
            afterRestore = null; // unloading/quitting: don't run a pending Yes into objects being destroyed
            RestoreControls();
            if (barTexture != null)
                UnityEngine.Object.Destroy(barTexture);
            barTexture = null;
            titleStyle = contentStyle = buttonStyle = hintStyle = null;
        }
    }
}
