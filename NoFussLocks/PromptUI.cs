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

        // Drawing. Sizes are in pixels at 1080p and scaled with the screen height (styles rebuilt when it changes).
        private static GUIStyle titleStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle hintStyle;
        private static GUIStyle contentStyle;
        private static float styleScale;
        private static Texture2D white; // 1x1, tinted with GUI.color for every filled rectangle
        private static Font gameFont;
        private static bool gameFontSearched;
        private static bool fontMissingLogged;

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
                text += (text.Length > 0 ? "\n" : "") + "Anyone who sees you will report a burglary.";

            // Assigned only once nothing above can throw, so a failed open leaves no stale lock behind.
            mode = Mode.Lock;
            callback = callbackSuccess;
            activeLock = lockToLockpick;
            difficulty = lockDifficulty;
            simpleYes = null;
            simpleValid = null;
            Show("Pick this lock?", text);
        }

        internal static bool IsActiveLock(Lock l) => l != null && activeLock == l; // activeLock is cleared on Close

        // Lock is a plain C# class, so "lock == null" never sees its door/container being destroyed. Lock.Use stores
        // the owner GameObject in _parent for the attempt; Unity's null check on that tells whether the owner (and
        // the Relay components the lock fires) still exist.
        private static bool LockAlive(Lock l) => l != null && l._parent != null;

        // stillValid: whether Yes still makes sense (keypad not destroyed, powered, not opened meanwhile); checked every
        // frame, and the prompt closes as soon as it returns false.
        internal static void OpenSimple(Func<bool> stillValid, string promptTitle, string promptContent, Action onYes)
        {
            if (IsOpen)
                return;
            RestoreControls(); // finish the previous prompt (restore + its Yes action) before any field is set

            mode = Mode.Simple;
            callback = null;
            activeLock = null;
            simpleYes = onYes;
            simpleValid = stillValid;
            Show(promptTitle, promptContent);
        }

        // The prompt only counts as open once the menu and controls are disabled. If disabling throws, whatever was
        // disabled is restored, the fields are cleared and the exception goes to the patch, which leaves the use to
        // vanilla or ends the lock attempt.
        private static void Show(string promptTitle, string promptContent)
        {
            title = promptTitle;
            content = promptContent;
            pendingClick = Click.None;
            hint = null; // key bindings may have changed since the last prompt
            if (gameFont == null) // not loaded yet last time, or unloaded since (Unity null): look again, rebuild styles
            {
                gameFontSearched = false;
                titleStyle = null;
            }

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
            blurErrorLogged = false;
            EnsureBlur();
        }

        // The game's menu blur: the Beautify "blur" flag on the cameras EffectsController keeps (what its EnableBlur
        // and DisableBlur set, for the game menu, pause menu and start selections). On while the prompt is up, picking
        // included. The flag is set per camera here rather than through EnableBlur/DisableBlur, so a stale entry can't
        // stop the rest, and only cameras this prompt blurred are cleared again.
        private static readonly List<BeautifyEffect.Beautify> blurredByPrompt = new List<BeautifyEffect.Beautify>();
        private static bool blurErrorLogged;

        // Blurs any camera that isn't blurred yet. Called on open and every frame while open, so a DisableBlur from
        // elsewhere during the prompt (e.g. a menu closing) doesn't leave the prompt unblurred.
        private static void EnsureBlur()
        {
            var fx = EffectsController.instance;
            if (fx == null)
                return;
            foreach (var entry in fx.beautifyList)
            {
                try
                {
                    var b = entry?.beautify;
                    if (b == null || b.blur)
                        continue;
                    b.blur = true;
                    blurredByPrompt.Add(b);
                }
                catch (Exception e)
                {
                    if (!blurErrorLogged)
                        Plugin.Log.LogWarning($"Background blur failed: {e}");
                    blurErrorLogged = true;
                }
            }
        }

        // Clears the blur this prompt added, unless something that needs it has opened meanwhile (the pause or death
        // menu, the game menu, the start selections), which would otherwise lose its background blur.
        private static void ReleaseBlur()
        {
            if (blurredByPrompt.Count == 0)
                return;
            bool neededElsewhere = false;
            try
            {
                neededElsewhere = (GameController.instance != null && GameController.instance.GameIsPaused)
                    || (GameUIController.instance != null && GameUIController.instance.IsMenuVisible)
                    || (StartSelectionsUI.instance != null && StartSelectionsUI.instance.menu != null
                        && StartSelectionsUI.instance.menu.activeSelf);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Blur owner check failed: {e}");
            }
            if (!neededElsewhere)
            {
                foreach (var b in blurredByPrompt)
                {
                    try
                    {
                        if (b != null)
                            b.blur = false;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Removing the background blur failed: {e}");
                    }
                }
            }
            blurredByPrompt.Clear();
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
            ReleaseBlur();
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
            if (state == State.Closed)
                return;
            EnsureBlur();
            // Skip the frame the state changed on, so the key press that caused it isn't read again.
            if (Time.frameCount <= stateFrame)
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

        // Colours of the prompt-game design (media/design-notes.md), taken from the game's menus: near-black panels in
        // charcoal frames, cream text, and the tab colours for the buttons (selected tab #825112, other tabs #53380d).
        private static readonly Color FrameFill = Hex(0x090b0a), FrameLine = Hex(0x383b34);
        private static readonly Color PanelFill = Hex(0x121313), PanelLine = Hex(0x53564c);
        private static readonly Color Accent = Hex(0x825112), Secondary = Hex(0x53380d);
        private static readonly Color AccentInset = new Color(0.85f, 0.80f, 0.63f, 0.27f), SecondaryInset = Hex(0x6b4a16);
        private static readonly Color ButtonLine = Hex(0x68675f);
        // Muted is lighter than the mock-ups' #aaa99c: Unity draws Erika Ormig thinner than the SVG renderer, and the
        // darker grey faded into the panel in-game.
        private static readonly Color Ink = Hex(0xe2ded0), Muted = Hex(0xc8c5b8);
        private static readonly Color BarFill = Hex(0x070d0b), BarLine = Hex(0x575e4b);

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f);

        // The game's UI font. It ships inside the game's assets (used by its TextMeshPro text), so it is borrowed at
        // runtime, never bundled. Searched for once per prompt until found (Show resets the search while it is missing
        // or has been unloaded); without it the prompt uses Unity's default font.
        private static Font GameFont()
        {
            if (!gameFontSearched)
            {
                gameFontSearched = true;
                foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                {
                    if (f != null && f.name.IndexOf("Erika", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        gameFont = f;
                        break;
                    }
                }
                if (gameFont == null && !fontMissingLogged)
                {
                    fontMissingLogged = true;
                    Plugin.Log.LogInfo("The game's Erika Ormig font wasn't found; the prompt uses the default font.");
                }
            }
            return gameFont;
        }

        private static GUIStyle Style(float size, Color color, TextAnchor anchor, float scale)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                font = GameFont(),
                fontSize = Mathf.Max(8, Mathf.RoundToInt(size * scale)),
                alignment = anchor,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            style.normal.textColor = color;
            return style;
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        // A 1 px (scaled) outline inside r.
        private static void Outline(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        // Draws a button and returns whether it was clicked. Lighter while the mouse is over it. px is the line width:
        // the scale rounded to whole pixels, so lines stay sharp (they step 1 -> 2 px at 1.5x, e.g. 1620p).
        private static bool Button(Rect r, string label, bool primary, float px)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color fill = primary ? Accent : Secondary;
            if (hover)
                fill = Color.Lerp(fill, Color.white, 0.12f);
            Fill(r, fill);
            Outline(r, ButtonLine, px);
            Outline(new Rect(r.x + 3f * px, r.y + 3f * px, r.width - 6f * px, r.height - 6f * px), primary ? AccentInset : SecondaryInset, px);
            GUI.Label(r, label, buttonStyle);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // The content, one label per line 23 px apart, as in the mock-ups (IMGUI's own line spacing is tighter).
        // Lines are split at "\n" and wrapped at words to the panel width with the real font, so they never clip. Two
        // lines fit above the buttons; the panel grows for more (OnGUI), so nothing is dropped. Cached until the text
        // or the scale changes.
        private static string wrappedFor;
        private static float wrappedScale;
        private static readonly List<string> wrapped = new List<string>();

        private static List<string> Wrap(string text, float width, float k)
        {
            if (wrappedFor != text || wrappedScale != k)
            {
                wrappedFor = text;
                wrappedScale = k;
                wrapped.Clear();
                foreach (var para in text.Split('\n'))
                {
                    string line = "";
                    foreach (var word in para.Split(' '))
                    {
                        string next = line.Length == 0 ? word : line + " " + word;
                        if (line.Length > 0 && contentStyle.CalcSize(new GUIContent(next)).x > width)
                        {
                            wrapped.Add(line);
                            line = word;
                        }
                        else
                            line = next;
                    }
                    wrapped.Add(line);
                }
            }
            return wrapped;
        }

        private static void DrawLines(List<string> lines, float left, float top, float width, float k)
        {
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(left, top + (60f + 23f * i) * k, width, 24f * k), lines[i], contentStyle);
        }

        internal static void OnGUI()
        {
            if (state == State.Closed)
                return;

            // Laid out at 1080p and scaled with the screen height; on a very narrow screen the 540 px panel (plus a
            // margin) is fitted to the width instead.
            float k = Mathf.Min(Screen.height / 1080f, Screen.width / 600f);
            if (k <= 0f)
                return;
            if (white == null)
            {
                white = new Texture2D(1, 1);
                white.SetPixel(0, 0, Color.white);
                white.Apply();
            }
            if (titleStyle == null || styleScale != k)
            {
                styleScale = k;
                titleStyle = Style(26f, Ink, TextAnchor.MiddleLeft, k);
                contentStyle = Style(17f, Muted, TextAnchor.MiddleLeft, k);
                buttonStyle = Style(20f, Ink, TextAnchor.MiddleCenter, k);
                hintStyle = Style(16f, Muted, TextAnchor.MiddleCenter, k);
            }

            float w = 540f * k;
            float textWidth = w - 52f * k;
            var lines = Wrap(state == State.Asking ? content : (Plugin.BypassWitnesses.Value ? "" : "Keep out of sight."), textWidth, k);
            // Room for two content lines (and, while picking, one line plus the bar); 23 px more per extra line.
            float extra = 23f * k * Mathf.Max(0, lines.Count - (state == State.Asking ? 2 : 1));
            float h = 224f * k + extra;
            float x = Mathf.Round((Screen.width - w) / 2f), y = Mathf.Round((Screen.height - h) / 2f);
            float px = Mathf.Max(1f, Mathf.Round(k)); // line thickness

            // Outer frame, panel, accent rail on the left.
            var frame = new Rect(x - 5f * k, y - 5f * k, w + 10f * k, h + 10f * k);
            Fill(frame, FrameFill);
            Outline(frame, FrameLine, 2f * px);
            var panel = new Rect(x, y, w, h);
            Fill(panel, PanelFill);
            Outline(panel, PanelLine, px);
            Fill(new Rect(x, y, 4f * k, h), Accent);

            float left = x + 26f * k;
            float bw = 236f * k, bh = 40f * k, by = y + 140f * k + extra;
            var yesRect = new Rect(left, by, bw, bh);
            var noRect = new Rect(x + 278f * k, by, bw, bh);

            if (state == State.Asking)
            {
                GUI.Label(new Rect(left, y + 20f * k, textWidth, 34f * k), title, titleStyle);
                DrawLines(lines, left, y, textWidth, k);
                // Only record the click: the lock/keypad callbacks then run from LateUpdate, the same as for keys,
                // not in the middle of IMGUI event processing.
                if (Button(yesRect, "Yes", true, px))
                    pendingClick = Click.Yes;
                else if (Button(noRect, "No", false, px))
                    pendingClick = Click.No;
            }
            else
            {
                GUI.Label(new Rect(left, y + 20f * k, textWidth, 34f * k), "Picking the lock...", titleStyle);
                DrawLines(lines, left, y, textWidth, k);

                float progress = pickDuration > 0f ? Mathf.Clamp01((Time.time - pickStart) / pickDuration) : 1f;
                var bar = new Rect(left, y + 88f * k + extra, textWidth, 14f * k);
                Fill(bar, BarFill);
                Outline(bar, BarLine, px);
                Fill(new Rect(bar.x + 3f * k, bar.y + 3f * k, (bar.width - 6f * k) * progress, bar.height - 6f * k), Accent);

                // Where No was, not where Yes was, so a double click on Yes can't hit Stop.
                if (Button(noRect, "Stop", false, px))
                    pendingClick = Click.No;
            }
            GUI.Label(new Rect(x, y + h - 32f * k, w, 24f * k), HintText(), hintStyle);
        }

        internal static void Dispose()
        {
            Cancel();
            afterRestore = null; // unloading/quitting: don't run a pending Yes into objects being destroyed
            RestoreControls();
            if (white != null)
                UnityEngine.Object.Destroy(white);
            white = null;
            titleStyle = contentStyle = buttonStyle = hintStyle = null;
            wrappedFor = null;
            gameFont = null; // borrowed from the game, never destroyed here
            gameFontSearched = false;
        }
    }
}
