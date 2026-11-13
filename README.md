# No-Fuss Locks

A BepInEx 6 mod for **Obenseuer** that replaces the lockpicking, safe and keypad minigames with a simple Yes/No prompt.

Download: [No-Fuss Locks on Nexus Mods](https://www.nexusmods.com/obeseuer/mods/92)

- **Locks** (doors, containers): "Pick this lock?" On Yes you pick the lock for a moment (1 second by default) and it always opens. Witnesses can still see you and report a burglary, just like during the normal minigame.
- **Safes** (combination dials): "Crack this safe?" On Yes the safe opens without entering the combination.
- **Keypads** (number pads): "Bypass this keypad?" On Yes the keypad opens without entering the code.

For locks you still need a lockpick in your hand for locks, and lockpicks never break (as you never fail). Locks that can't be picked, safes marked "Blocked" and keypads without power behave as in the normal game. Each kind of prompt can be switched off on its own.

## Controls

| | Keyboard and mouse | Controller |
|---|---|---|
| Yes | <kbd>Y</kbd> or <kbd>Enter</kbd>, or click the button | the game's "Move Item" button |
| No / Stop | <kbd>N</kbd> or <kbd>Esc</kbd>, or click the button | the game's "Menu" button |

## Settings

The settings file is `BepInEx/config/obenseuer.maddog2050.nofusslocks.cfg`. It is created the first time the game
runs with the mod. BepInEx reads it when the game starts, so restart the game after editing it (or change the
settings in-game with a config manager mod).

| Section | Setting | Default | What it does |
|---|---|---|---|
| Lockpicking | `Enabled` | `true` | Prompt instead of the lockpicking minigame. Off = the normal minigame. |
| Lockpicking | `BypassWitnesses` | `false` | On = picking a lock is never seen and never counts as a burglary. |
| Lockpicking | `PickTime` | `1` | Seconds you stay at the lock after Yes (0–10). Witnesses are checked during this time. 0 = instant, but then you are hardly ever caught. |
| Lockpicking | `GainSkill` | `true` | A pick raises your Lockpicking skill like a successful minigame. |
| Lockpicking | `MaxSkillGain` | `0.15` | Most Lockpicking skill one pick can give (0.01–1). The game gives up to 1.0 for very hard locks; since every pick succeeds, it is capped. 1 = no cap. |
| Safe | `Enabled` | `true` | Prompt instead of the combination dial. |
| Keypad | `Enabled` | `true` | Prompt instead of the number pad. |

## Requirements

- Obenseuer for Windows (Steam). Built and checked against the game's code of early November 2026.
- [BepInEx 6](https://builds.bepinex.dev/projects/bepinex_be) for Unity Mono (built against 6.0.0-be.790).

## Installation

**With a mod manager:** install the release zip (`NoFussLocks_<version>.zip`) like any other BepInEx mod.

**By hand:** unpack the zip into the game folder, so that you end up with
`<Obenseuer>/BepInEx/plugins/NoFussLocks/NoFussLocks.dll`.

To check that it loaded, `BepInEx/LogOutput.log` should contain `No-Fuss Locks <version> loaded`.

**Removing it** is safe: the mod changes no saved game data. Delete the `NoFussLocks` folder (or disable the mod in
your mod manager) and the minigames are back.

## Building from source

You need the .NET SDK (8 or newer), your own copy of the game and BepInEx 6 for Unity Mono. No game or BepInEx files
are included in this repository or in the release zip.

1. Copy `local.props.example` to `local.props` and set `GameDir` to your Obenseuer folder. If BepInEx is not inside
   the game folder (for example when a mod manager keeps it elsewhere), also set `BepInExCore` to its `core` folder.
2. Build:

   ```sh
   dotnet build NoFussLocks
   ```

   The dll is written to `NoFussLocks/bin/Debug/NoFussLocks.dll`.
3. Optional: `dotnet build NoFussLocks -p:Deploy=true` also copies it to `DeployDir` (default
   `<GameDir>/BepInEx/plugins/NoFussLocks`). Close the game first.

The version is set in one place, `<Version>` in `NoFussLocks/NoFussLocks.csproj`, and is managed by release-please
(see below).

### Releases and changelog

[CHANGELOG.md](CHANGELOG.md) and the version number are maintained by
[release-please](https://github.com/googleapis/release-please) from the commit messages, so commits on `main` use
[Conventional Commits](https://www.conventionalcommits.org/):

- `fix: ...` for a bug fix (raises the patch version, 0.1.0 → 0.1.1),
- `feat: ...` for a new feature (raises the minor version, 0.1.0 → 0.2.0),
- `feat!: ...` or a `BREAKING CHANGE:` footer for a breaking change (also the minor version while below 1.0),
- `docs:`, `chore:`, `refactor:` and so on for changes that don't show up in the changelog.

On every push to `main`, a GitHub Action opens or updates a release PR with the next version and changelog entry.
Merging it tags the release and creates the GitHub release as a **draft**. The same workflow then builds the mod from
that tag, checks the zip, attaches `NoFussLocks_<version>.zip` and only then publishes the release, so a failed build
leaves an unpublished draft instead of a public release without a download. After that it uploads the same zip to
Nexus Mods. If the Nexus upload fails, the GitHub release is already public: re-run the failed job, or run the
workflow by hand with the tag (Actions → release-please → Run workflow), which also finishes a release left as a
draft. The workflow file explains the recovery options and lists the secrets and variables it needs
([.github/workflows/release-please.yml](.github/workflows/release-please.yml)).

The build on GitHub uses **stripped** copies of the game's dlls (names and signatures only, every method body
removed) from a private repository; no game file is ever part of this repository or of a release. After a game
update, refresh them with the `update.sh` script in that repository (its README has the steps), then commit and push
it. If the mod starts referencing another game or Unity dll, add it to that script's list too.

### Project layout

| File | What it does |
|---|---|
| `NoFussLocks/Plugin.cs` | Plugin entry point: settings, patching, unloading. |
| `NoFussLocks/PromptUI.cs` | The Yes/No prompt: input, the picking phase, witness checks, restoring controls. |
| `NoFussLocks/Patches/LockpickingPatch.cs` | Harmony patch on `PlayerLockpicking.ToggleLockpicking`: shows the prompt instead of the lockpicking minigame. |
| `NoFussLocks/Patches/KeypadPatch.cs` | Harmony patch on `Keypad.Use`: shows the prompt instead of the safe dial or number pad. |
| `.github/workflows/release-please.yml` | Changelog, version, release, build and Nexus upload. |

## How it was made

Much of the code was written with the help of an AI coding assistant (Claude).

## Licence

Copyright (C) 2026 Maddog2050

This program is free software; you can redistribute it and/or modify it under the terms of the GNU General Public
License version 2 as published by the Free Software Foundation.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied
warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [LICENSE](LICENSE) file for the full text.

Obenseuer and its code belong to their owners; this mod includes none of the game's files.
