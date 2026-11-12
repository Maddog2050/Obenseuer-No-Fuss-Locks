# No-Fuss Locks — design notes

Artwork for the Nexus Mods page and mock-ups of the in-game prompt. The artwork is original SVG drawn by the build
scripts, with no game art or logos; the only game images are the in-game screenshots in `screenshots/`.

## Files

| File | What it is |
|---|---|
| `banner.png` / `banner.svg` | Nexus page header banner, 1300 × 372. |
| `build-banner.py` | Builds the banner. |
| `prompt-game.png` / `.svg` | Prompt mock-up in the game's tab colours (the preferred design), 1920 × 1080 frame. |
| `prompt-ochre.png` / `.svg` | Same prompt with a brighter ochre Yes button, 1920 × 1080 frame. |
| `build-prompts.py` | Builds both prompt mock-ups. |
| `screenshots/` | In-game screenshots at 1080p for the Nexus page: `mod-*` (the prompt) and `vanilla-*` (the minigames it replaces) for locks, safes and keypads. |

## Rebuilding

Both scripts render with `rsvg-convert` (librsvg). The prompt text uses the game's UI font, which is not in this
repository, so point `GAME_FONT_DIR` at a folder holding `ErikaOrmig.ttf`:

```sh
GAME_FONT_DIR=/path/to/font-folder python3 media/build-banner.py
GAME_FONT_DIR=/path/to/font-folder python3 media/build-prompts.py
```

`build-banner.py` stops if the font can't be found; `build-prompts.py` falls back to another font.

## Fonts

- **Erika Ormig** (Peter Wiegel): the game's own UI font, used for all prompt text. The game ships it inside
  `Obenseuer_Data/sharedassets0.assets` (an embedded TrueType font, with TextMeshPro "Erika Ormig SDF" assets built
  from it). It is only used locally while rendering: it is not committed, not installed system-wide and not embedded
  in the SVGs. Its metadata says "Beta version, for evaluation purpose only", so don't redistribute it. The font
  draws `[` and `]` almost exactly like `(` and `)`, so bracketed text such as `[Y]` reads as "(Y)".
- **Fira Sans Condensed** (SIL OFL 1.1): banner title and strap line.
- **Liberation Mono** (SIL OFL 1.1) and **DejaVu Sans Mono** (DejaVu licence): small banner labels.

## Banner

Dark green-grey industrial wall panels with seams, rivets, faint rain and wear, and a faded safe dial in the lower
left. On the left: an ochre dash and "FOR OBENSEUER", the title **No-Fuss Locks**, the strap line "One click. No
minigame." on one line ("One click." bold, in the title ochre `#d4ad5c`) and "LOCKS / SAFES / KEYPADS". On the right:
a large open padlock (whole shackle, including the lifted free end, kept clear of the cards) partly behind a fan of
three prompt cards, "Crack this safe?", "Bypass this keypad?" and "Pick this lock?" in front. The cards use the
`prompt-game` design, all three the same size, slightly rotated, with the fan scaled to 86 %. Only the titles of the
two back cards show, so the keypad card is drawn without its description line, which would otherwise peek out under
its title. Text is kept clear of the outer ~40 px, which Nexus may crop.

## In-game prompt

Reference: the official Steam store screenshots of Obenseuer (app 951240,
https://store.steampowered.com/app/951240/), mainly the character/skills and tenement screens. The game's menus use
nearly black panels inside thick charcoal frames, off-white typewriter text, and ochre tabs: the selected tab is
about `#825112`, the others about `#53380d`, both with cream text.

The prompt design (a mix of earlier "inset charcoal" and "corner toast" mock-ups) is centred on screen, 540 × 224:

| Element | Colour |
|---|---|
| Outer frame | `#090b0a` fill, `#383b34` 2 px stroke, 5 px around the panel |
| Panel | `#121313` fill, `#53564c` 1 px stroke |
| Accent rail | 4 px on the left edge, same colour as Yes |
| Title | `#e2ded0`, 26 px |
| Content lines and key hint | `#c8c5b8`; content 17 px, lines 23 px apart; hint 16 px |
| Yes button | `prompt-game`: `#825112`, cream text; `prompt-ochre`: `#ad8433`, dark `#131714` text |
| No / Stop button | `#53380d`, cream text, inner line `#6b4a16` (`prompt-ochre`: `#5e430f`) |
| Button border | `#68675f`, plus a thin inset line 3 px inside |

Layout: title, two content lines, Yes and No side by side (Stop takes No's place while picking, with a progress bar
in the accent colour under the content), and the key hint centred at the bottom. The hint follows the game's own
key hints (e.g. `F)BUY/SELL   E/Q)CATEGORY   Tab)EXIT` in the shop): `Y/Enter)YES   N/ESC)NO`, and `N/ESC)STOP`
while picking. That is the game's format: the key, `)`, the action in capitals with no space, `ESC` as the game
writes it, and three spaces between entries.

Checked in-game at 1080p: Unity draws Erika Ormig thinner than librsvg, so an earlier, darker grey (`#aaa99c`, 15–16
px) faded into the panel; the values above are the readable in-game ones, used by the mod and the mock-ups alike. The
mod draws each content line as its own label and wraps lines at words to the panel width. The safe and keypad lines
are "The safe opens without the combination." and "The keypad opens without the code.", short enough for one line.

IMGUI notes: everything is flat rectangles, borders and labels, drawn with one cached 1 × 1 texture and `GUIStyle`s.
The background blur is the game's own (`EffectsController.EnableBlur`, as behind its menus, keypad and safe), on while
the prompt is up, including while picking. The game's `Font` asset is looked up at runtime rather than shipped.
