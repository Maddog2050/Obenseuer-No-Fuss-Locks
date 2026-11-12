using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoFussLocks
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "obenseuer.maddog2050.nofusslocks";
        public const string Name = "No-Fuss Locks";
        public const string Version = PluginVersion.Value; // generated from <Version> in the csproj

        internal static ManualLogSource Log;
        // Key object for GameController.ControlsDisabled/ControlsEnabled. Not this plugin's gameObject: in BepInEx 6
        // every plugin sits on the shared BepInEx_Manager object, so another mod using that key would clear ours.
        internal static GameObject ControlsKey;

        // One section per minigame, each with its own on/off switch. Off = that minigame is vanilla.
        internal static ConfigEntry<bool> LockpickingPrompt;
        internal static ConfigEntry<bool> BypassWitnesses;
        internal static ConfigEntry<float> PickTime;
        internal static ConfigEntry<bool> GainSkill;
        internal static ConfigEntry<float> MaxSkillGain;
        internal static ConfigEntry<bool> SafePrompt;
        internal static ConfigEntry<bool> KeypadPrompt;

        private Harmony harmony;

        private void Awake()
        {
            Log = Logger;
            ControlsKey = new GameObject("NoFussLocks Controls Key") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(ControlsKey);

            LockpickingPrompt = Config.Bind("Lockpicking", "Enabled", true,
                "On = locked doors and containers ask \"Pick this lock?\" and always open on Yes instead of the lockpicking minigame. " +
                "Off = the normal minigame.");
            BypassWitnesses = Config.Bind("Lockpicking", "BypassWitnesses", false,
                "On = picking a lock is never seen by witnesses and never adds a burglary crime. " +
                "Off = like the game: if anyone sees you while you pick the lock, a burglary is recorded.");
            PickTime = Config.Bind("Lockpicking", "PickTime", 1f,
                new ConfigDescription("Seconds you stay at the lock after choosing Yes; witnesses are checked every frame during this time, " +
                    "like during the minigame. 0 = instant, but then one quick check rarely catches you.",
                    new AcceptableValueRange<float>(0f, 10f)));
            GainSkill = Config.Bind("Lockpicking", "GainSkill", true,
                "On = a pick raises your Lockpicking skill like a successful minigame would. Off = no skill gain.");
            MaxSkillGain = Config.Bind("Lockpicking", "MaxSkillGain", 0.15f,
                new ConfigDescription("Most Lockpicking skill one pick can give (1 = one level). The game gives more for harder locks, " +
                    "up to 1.0 for very hard ones; since every pick succeeds here, that is capped. 0.15 is about a Moderate lock; 1 = no cap.",
                    new AcceptableValueRange<float>(0.01f, 1f)));
            SafePrompt = Config.Bind("Safe", "Enabled", true,
                "On = rotary-dial safes ask \"Crack this safe?\" and open on Yes instead of showing the dial. " +
                "Off = the normal dial. (The game has no witness check for safes, so none is added.)");
            KeypadPrompt = Config.Bind("Keypad", "Enabled", true,
                "On = number keypads ask \"Bypass this keypad?\" and open on Yes instead of asking for the code. " +
                "Off = the normal keypad. Keypads without power still don't work.");

            SceneManager.activeSceneChanged += OnActiveSceneChanged;

            harmony = new Harmony(Guid);
            // Patch class by class, so one broken target (after a game update) doesn't disable the others.
            foreach (var patch in new[] { typeof(Patches.LockpickingPatch), typeof(Patches.KeypadPatch) })
            {
                try
                {
                    harmony.PatchAll(patch);
                }
                catch (Exception e)
                {
                    Logger.LogError($"{patch.Name} failed, that part of the mod is inactive: {e}");
                }
            }
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        // LateUpdate, after the game's Update: on the frame a key closes the prompt, the game's Update has already run
        // with its keys disabled (GameController.keysDisabled), so it ignored that key. Controls only come back on the
        // next frame (PromptUI.RestoreControls), so later LateUpdates this frame don't see the key either.
        private void LateUpdate() => PromptUI.LateUpdate();

        private void OnGUI() => PromptUI.OnGUI();

        private static void OnActiveSceneChanged(Scene from, Scene to) => PromptUI.Cancel();

        private void OnDestroy()
        {
            PromptUI.Dispose();
            if (ControlsKey != null)
                Destroy(ControlsKey);
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            harmony?.UnpatchSelf();
        }
    }
}
