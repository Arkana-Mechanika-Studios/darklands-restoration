# Settings file

The SDL host (and, on request, the headless host) reads its options from a JSON file,
`darklands.settings.json`. Every key is optional; the commented template written on the first run
documents each one with its default and the equivalent command-line option.

## Format

Plain JSON with `//` and `/* */` comments and trailing commas allowed. Unknown keys and invalid
values are reported on the console (stderr) with the file name and the key, then ignored; a broken
file never stops the program (an unparsable file is reported and every setting ignored). The template
shows each default; `""` (empty text) means the default for text and paths, and a key left out takes its
default (`null`, found in files written by earlier versions, means the same). Relative paths are taken relative to the settings file's folder, and `%VARIABLES%`
in paths are expanded.

## Where it is looked for

First found wins:

1. `--settings <file>` (an explicit file that cannot be read is reported and the defaults are used);
2. `darklands.settings.json` next to the executable;
3. `%APPDATA%\DarklandsRestoration\darklands.settings.json`.

With none of them, the first run writes the commented template next to the executable if that
folder is writable, else to `%APPDATA%\DarklandsRestoration`, and says where. `--write-settings <file>`
writes the template to a place of your choice (it never overwrites an existing file).

The headless host (a test and tooling program) reads only an explicit `--settings <file>`, so that
scripted runs never depend on a file found by chance; it never writes a template.

## Precedence

Command line > environment variables > settings file > defaults. A `--scale` or `--resolution` on the
command line replaces both display keys of the file; `--` followed by words replaces `startup.commandLine`.
The developer-only `local/paths.json` (`game`, `sdl`, `mt32roms`, `textfont`) is consulted only when nothing else names a path, and is
not meant for players.

## Keys

| Key | Default | Command line / environment | Meaning |
|---|---|---|---|
| `gameFolder` | searched (below) | first argument; `DARKLANDS_DIR` | folder with `DARKLAND.EXE` and the game data; the GOG or Steam install folder is fine (its `DARKLAND` subfolder is taken) |
| `savesFolder` | the game folder's `SAVES` (shared with the original); `%APPDATA%\DarklandsRestoration\SAVES` when that cannot be written | `--saves`, `DARKLANDS_SAVES` | saved games; set a folder to keep this program's saves elsewhere |
| `display.scale` | 1 | `--scale N` | window = N x 720x400 |
| `display.resolution` | | `--resolution WxH` | window size, e.g. `"1440x800"`; when set it replaces `scale` |
| `display.fullscreen` | false | `--fullscreen` | |
| `display.hdPack` | none | `--hd-pack <folder>` | a folder of replacement pictures (an HD pack, below); not part of the original, the game plays the same |
| `display.cleanDrawing` | false | `--clean-drawing` | false = the screens are drawn as in the 1992 game, including its drawing oddities; true = the cleaner drawing of 0.9.5 (the game plays the same) |
| `display.filter` | `"sharp"` | `--filter <name>` | how the 320x200 picture is drawn on a larger window: `sharp`, `smooth` or `crt` (below); only the window's picture changes (SDL host) |
| `display.crtCurvature` | false | | true: the `crt` filter's picture slightly curved, as a tube's (below) |
| `sound.card` | `"config"` | `--sound`, `--no-audio` (= `none`) | `config` (the card of CONFIG.DRK, as the original), `adlib`, `sb`, `sbpro2`, `mt32`, `cm32l`, `speaker` (not emulated: silent), `none` |
| `sound.mt32Roms` | `MT32_ROMS` next to the program | `--mt32-roms`; `DARKLANDS_MT32_ROMS` | MT-32 / CM-32L ROMs; without them the OPL3 FM drivers play instead, with a warning |
| `sound.audioPack` | none | `--audio-pack <folder>`; `DARKLANDS_AUDIO_PACK` | a folder of replacement music and battle effects (an audio pack, [PACKS.md](PACKS.md)); not part of the original, the game plays the same |
| `sound.soundFont` | `SOUNDFONT` folder next to the program | `--soundfont <file>` | the SoundFont (`.sf2`) that plays the audio pack's `.mid` files ([PACKS.md](PACKS.md)); a SoundFont named in the pack wins; none found: those cues are skipped |
| `sound.configDrk` | absent (commented out in the template) | | replaces CONFIG.DRK's words (below) |
| `startup.launcher` | false | `--setup` (once), `--no-launcher` | the launcher at every start; false: only when needed (below) |
| `startup.banner` | true | `--no-banner` | the BANNER.DAT banner and the start-up checks |
| `startup.intro` | true | `--no-intro` | the opening animation |
| `startup.commandLine` | `[]` | `-- <words>` | the game's own DOS command line (`-Q`, `-O`, `-A<letter>`), a list of words or one string |
| `textFont` | built-in | `--text-font`; `DARKLANDS_TEXT_FONT` | 4096-byte VGA BIOS 8x16 font dump, or its base64 |
| `screenshotsFolder` | current folder | `--screenshots` | |
| `mods` | `[]` | `--mod <folder or name>` (once per mod; replaces the file's list) | mods to play with, in load order: a mod's folder, or the name of a folder in a `mods` folder next to the program, in the game folder or next to it ([MODS.md](MODS.md)); not part of the original: a game with a mod plays differently and its saves get a companion file |
| `input.mouseWheel` | true | `--no-mouse-wheel` | the mouse wheel gives the cursor keys ↑ / ↓ where they move a selection (below); not part of the original, the game plays the same (SDL host) |
| `sdl.library` | the `SDL3.dll` next to the program (shipped on Windows), else the system's (`libSDL3.so.0`) | `--sdl` | the SDL3 library to load (SDL host only) |
| `console.enabled` | true | `--no-console` | the developer console (SDL host; below) |
| `console.font` | the bundled DejaVu Sans Mono | | a TrueType (.ttf) file for the console, e.g. `"C:/Windows/Fonts/consola.ttf"` |
| `console.fontSize` | 14 | | the console's text height in pixels at the 720x400 window (6..64); it grows with the window |

### `display.hdPack`

A folder with one PNG per picture of the game it replaces, named after the picture's asset name (the scheme of
`IndexedImage.AssetName`) plus `.png`, `\` and `/` as folders and `:` as `_`, in any letter case:
`PICS/STARTSCR.PIC.png`, `COMMONSP.IMG#12.png`, `F01C.CAT/F01WKSW.IMC#3.5.png`, `dropcap_D.png`. An optional
`pack.json` names other files: `{ "assets": { "PICS\\STARTSCR.PIC": "title.png" } }` (paths relative to the pack).
A picture is stretched over the original's rectangle, so any size works (a multiple of the original keeps its
proportions); it shows only where the original picture is still seen, so menus, text and anything drawn over it stay as
they are. A replaced picture fades, greys or lightens with the original (the card's option fade); colour cycling and
recoloured figures do not change a replacement yet. Problems (a
missing folder, a broken PNG) are reported on the console and the original pictures are kept. The headless host takes
the same key from `--settings`, or the step `hdpack:<folder>`, and then also saves `<name>.hd.png` for each shot
(and `<name>.assets.txt`, the asset names on that screen). The guide for pack makers: [PACKS.md](PACKS.md).

### `display.filter`

How the SDL host draws the game's 320x200 picture on the window; the picture keeps its place and shape (the window size,
fullscreen and the black borders are as without a filter). In the game **Ctrl+F11** goes to the next filter for the
rest of the run (the setting in the file stays; the original never reads F11, so the game loses no key).

| Name | Picture |
|---|---|
| `sharp` (default) | each window pixel takes the nearest game pixel: hard pixels, as always; at sizes that are not whole multiples of 320x200 some columns and rows are one window pixel wider than others |
| `smooth` | "sharp bilinear": the picture is enlarged by whole numbers with hard pixels, then smoothly to the window, so every game pixel is the same size and only the edges between pixels blend over one window pixel; at whole multiples it is the same as `sharp` |
| `crt` | as `smooth`, with scanlines between the picture's 200 lines and a light red/green/blue aperture mask, as on a VGA monitor, and part of the lost brightness given back. The scanlines fade out where a line is under three window pixels tall. With `display.crtCurvature` true the picture is slightly curved, as a tube's (its corners black) |

Only the picture shown in the window changes: the game, its timing, the screenshots (F12), the problem reports and
the frames the headless host and the oracles record are made before the filter and are the same with every filter. The
text screen of the program start keeps its sharp look. With an HD pack, the game's own pixels (the frame and the
overlay drawn over the replacements) go through `smooth`'s steps, the replacement pictures are drawn smoothly as before,
and the CRT's mask lies over everything, replacements included.

How it is drawn: when `smooth` or `crt` is set as the program starts, the window opens on SDL's GPU renderer, and a pixel
shader of this project (`src/Darklands.Host.Sdl/Shaders/picture.frag.hlsl`, compiled by `tools/shaders/build_shaders.py`
to DXIL for Direct3D 12 on Windows and to SPIR-V for Vulkan on Linux and the Steam Deck) draws the filter in one pass. As the
window opens, one colour is drawn through the shader and read back; where that renderer cannot start or the shader does not
draw (no Direct3D 12 or Vulkan, a driver that refuses it), or when a filter is chosen in the game with Ctrl+F11 on another
renderer, the filters are drawn with render targets and blend modes instead: the same picture, without the curvature; on
SDL's software renderer the CRT is a little darker there (it cannot give the brightness back). The log says which way is
used ("Picture filters: shaders on SDL's gpu renderer (vulkan)"). Both ways are tested against a reference drawn on the
CPU (`PictureFilterReference`), the shader on Direct3D 12 and on Vulkan.

### `sound.audioPack`

A folder of `.ogg` or `.wav` files named after the game's pieces of music and battle effects (`travel-1.ogg`,
`battle-death.wav`, the catalogue in [PACKS.md](PACKS.md)), with an optional `audio.json`. The game's own sound drivers
keep running underneath, muted while a replacement plays, so the game's timing does not change. Problems (a missing
folder, an unknown name, a file that cannot be read) are reported on the console and the game's own sound plays there.
The headless host takes the same key from `--settings`, the environment variable, or the step `audiopack:<folder>`.

### `sound.configDrk`

An object `{ "card", "port", "irq", "dma" }`, each field optional. The four values stand for the four
16-bit words of CONFIG.DRK and are applied as if the file held them: a field left out keeps the real
file's word (without a CONFIG.DRK: card 0, port 220h, IRQ 7, DMA 1). The game's start-up, the choice of
driver files and the sound resources all read these words, so the override has exactly the effect of
editing CONFIG.DRK, without touching the game folder.

`card` is the install program's number or a name:

| Number | Name | Driver letter | Card |
|---|---|---|---|
| 0 | `adlib` | a | AdLib |
| 1 | `covox` | a | Covox Sound Master |
| 2 | `sb` | a | Sound Blaster |
| 3 | `sbpro` | a | Sound Blaster Pro |
| 4 | `sbpro2` | p | Sound Blaster Pro 2 |
| 5, 6 | `pas`, `pas16` | p | Pro Audio Spectrum (16) |
| 7 | `roland`, `mt32`, `cm32l` | r | Roland MT-32 / LAPC-I |
| 127 | `speaker` | i | PC speaker |
| other | `none` (65535) | n | no sound driver |

`port`, `irq` and `dma` are numbers, or text such as `"0x220"` / `"220h"`, from 0 to 65535. The driver
files are `<letter>SOUND.DLC` / `.DLB` in the game folder. `sound.card` (when not `config`) then picks the
emulation among them, taking port, IRQ and DMA from these words.

### Sound diagnostics (SDL host, environment only)

* `DARKLANDS_AUDIO_TRACE=<file>`: each call of the game to the sound (`call music 0E at <tick> …`), each
  number the drivers carry out (`request <tick> <frame> <number>`) and every MIDI byte the Roland driver
  sends (`midi …`), in the headless host's trace format.
* `DARKLANDS_AUDIO_RECORD=<file>`: exactly what the audio device is given, raw 16-bit little-endian
  stereo at 44,100 Hz.

Music is played only where the original plays it: a card starts its piece when it is shown (the
districts, the inn, the city outskirts …). The main street fades the music out (piece 6), and loading a
game plays nothing until such a card comes.

## Finding the game (SDL host)

When no game folder is set, the program looks, in this order, without scanning any disk: its own folder and the folder
above it; GOG's registration of Darklands (product 1207658972); Steam's libraries (app 327930); Windows' list of
installed programs (an entry named Darklands); then `C:\GOG Games\Darklands`, GOG Galaxy's and Steam's default folders, and
`DARKLAND`, `Darklands`, `Games\Darklands`, `GOG Games\Darklands` at the root of each fixed drive. A folder counts when it,
or its `DARKLAND` subfolder, holds `DARKLAND.EXE`. The folder found is written into the settings file's `gameFolder`, so
the next start goes straight there and the choice can be seen and changed.

When nothing is found, or the folder named does not hold the game, the launcher comes up (below) to ask for it. With
`--no-launcher` the program ends instead, with a message on the console.

## Launcher (SDL host)

The launcher is the settings screen shown before the game: the game folder, the window size, fullscreen, the picture
filter (its "Picture" row beside Fullscreen: Sharp, Smooth, CRT; ←/→ or Enter), the HD pack,
the audio pack (both: None, the packs in a `packs` folder, or a folder browsed for or dropped; [PACKS.md](PACKS.md)),
the sound card and the Roland ROM folder (MT-32 and/or CM-32L; with a sound test: ten seconds of the game's music through the drivers the game
will use) and the start-up screen and opening animation. Its settings are on two tabs: **General** (the game folder, the
display, the sound card with its sound test, the packs) and **Advanced** (the Roland ROM folder, the SoundFont, the
start-up); a click on a tab or Ctrl+Tab opens the other, and Tab or the arrows go on from one tab's rows into the other's.
Its window has no title bar: the painting fills it, a drag on the painting (or on the panels' bare parchment) moves it, its
edges resize it, and its own buttons at the top-right corner minimise and close it (Alt+F4 and Esc close it too; no
minimise button when the window fills the screen, as in the Steam Deck's Game Mode). The game's window, after Play, has
its frame again. Beside them: the release notes (`RELEASE_NOTES.md` next to the
program, copied there by the build from `releases/<version>/release-notes.md` when that is there), a link to the version's
page on GitHub (the only place the notes are when the file is missing), and a word on the project. It comes up:

* on the first run (no settings file was found);
* when the game cannot be found;
* when the game could not start (a damaged HD pack, a missing file …), with the reason;
* when started through `DarklandsRP Launcher.exe` (Linux: `DarklandsRP-Launcher`), the small program beside `DarklandsRP.exe`
  that starts it with `--setup`; its Play starts the game;
* with `--setup`, or while **Shift** is held as the program starts;
* at every start when `startup.launcher` is true (the launcher's "Show launcher at every start").

`--no-launcher` turns it off. The game folder can be chosen with **Change…** (Windows' folder dialog; on other systems
Change… lets you type it), typed or pasted (Ctrl+V), dropped on the window, or chosen among the folders found with ←/→.
The Roland ROM folder is chosen the same way on its row on Advanced (Enter or its Change… opens the dialog) or by dropping the folder
with the ROM files on the window. The HD pack, the audio pack and the SoundFont rows: ←/→ (or a click on their ‹ ›) go
through the choices, Enter or the row's **Browse…** opens the system's dialog, and a dropped folder or `.sf2` file is
taken; a click on the row itself only chooses it. Under each folder and pack a line says what was found or what is wrong;
the pointer resting on a row, its line or a button (or the keyboard choosing it) shows a tooltip with the problem in full and
what it does. A notice over the buttons shows only while there is something to tell: why the game could not start, what
an action did. Rows and buttons light up under the pointer, and softly when chosen with the keyboard. The window can be made larger or
smaller, down to 960 x 540. **Play** and **Save and quit** write
the changed keys into the settings file, keeping its comments and everything else; closing the window or Esc saves as
well. A setting the command line or an environment variable overrides is shown but cannot be changed there (its
explanation says what sets it).

The launcher is its own project, `Darklands.Launcher`, which references `Darklands.Platform` only and draws into its own
pixels; the SDL host gives it the window (`SdlLauncherSurface`) and its services (`LauncherHost`). Another launcher fits
in by implementing `ILauncher`.

## Mouse (SDL host)

The system's mouse pointer is hidden over the window: the game draws its own (the gauntlet, the battle's hand) at the game
pixel under it, in any window size or fullscreen (`MouseMappingTests`). The game's pointer starts at the middle of the screen,
where the original's mouse reset puts it, until the mouse first moves over the window. When the game moves the pointer
itself (the cursor keys, a menu's default line) the system's pointer is moved with it.

While the mouse is outside the window the game's pointer is not drawn, and it comes back where the mouse enters the window
again (a host addition, not the original's: under DOS the mouse never leaves the screen). Only the picture changes: the game
keeps reading the mouse where it last was over the window, so nothing in the game and none of its random numbers depend on
it. With a button held the window still receives the mouse's moves outside it; the pointer stays hidden there too. The
headless host and the oracle tools always draw the pointer.

### The mouse wheel

With `input.mouseWheel` on (the default) the wheel moves the selection as ↑ / ↓ do, as DOSBox-X maps it: a notch away from you is
one press of ↑, towards you one of ↓. It does so only where these keys move a selection: the card menus and their item and saint
boxes, the character sheet's open lists and pack, the shop, barter and loot lists, the load list, the camp, the inn's roster, the
alchemy list, the character creation menus and the menu bar's pull-downs in its keyboard mode. Everywhere else (the map, a battle, a
card's text, a message box, the character sheet with no list open, where ↑ / ↓ move the frame between the boxes) the wheel does
nothing. The game reads each notch as the key itself, a press and release a player could have made; nothing else changes. The
screens say whether a selection is up (`GameUi.SelectionList`), the window turns the wheel into the keys (`IInput.SelectionListActive`).
The launcher shows it on its Advanced tab, under Start-up: "Mouse wheel in menus"; `--no-mouse-wheel` turns it off for one run.

### Keys on other layouts

The game takes the characters your keyboard layout prints, as DOS did with its keyboard driver: on AZERTY Shift with the `=` key is
`+`, the `6` key is `-` and Shift with the top row gives the digits; on QWERTZ `+` is its own key. The keypad's `+` and `-` work on
every layout. In the character generator a right click on an attribute or skill row lowers it, as a left click raises it (the first
click selects the row); the 1992 game had no right click there.

## Developer console (SDL host)

**F11** opens and closes it (Esc also closes it). F11 is free: the original reads the keyboard through
the old BIOS call, which never reports F11 or F12, so the game loses no key. While the console is open
the game receives no keys or clicks; it is drawn over the window, outside the game's 320x200 picture,
in a TrueType font at the window's own resolution. Typing follows the keyboard layout (QWERTY, AZERTY ...).

Keys: Enter runs the line, Up / Down recall earlier commands, Left / Right / Home / End / Backspace / Delete
edit, PgUp / PgDn or the mouse wheel scroll.

Commands (`help` lists them, `help <command>` shows one):

| Command | What it does |
|---|---|
| `show time\|place\|party\|money\|state` | the date, the place and its reputation, the members' attributes, the purse |
| `time set <hour>` / `time advance <hours>` | sets the hour, or lets hours pass by the game's own time rules |
| `date set <month 1..12> <day> [year]` | sets the date |
| `money add\|set <florins> [groschen] [pfennigs]` | changes the purse |
| `reputation set <value>` | the party's reputation in the current place |
| `character <slot\|all> <attribute\|skill> <value>` | sets an attribute (raising its maximum if needed) or a skill |
| `heal [slot\|all]` | endurance and strength back to their maximums |
| `saint grant\|remove <slot\|all> <number>` | a saint known or forgotten |
| `item find <text>` / `item grant <slot\|all> <code> [quality] [count]` | finds item codes; puts items in packs |
| `music on\|off` | the game's music option (as Alt-M) |
| `report [text]` | makes a problem report (a zip) and opens its folder and the issue form; nothing is sent ([REPORTING.md](REPORTING.md)) |
| `clear` | empties the console |

The commands change the game being played between two frames; a screen shows a change the next time it
draws. The headless host runs the same commands with the step `console:<command>`.

## Logs and reports folders

Beside the settings file (or, when that folder cannot be written, in `%APPDATA%\DarklandsRestoration`) the program keeps a `logs`
folder (one `darklands-<date>-<time>.log` per run, the newest 5) and a `reports` folder (the report zips, the newest 10);
`logs\session` holds the helper files of the reports (the last frame, the copy of the saved game last loaded). Nothing in them
is sent anywhere; see [REPORTING.md](REPORTING.md).

## Example

```jsonc
{
  "gameFolder": "C:/GOG Games/Darklands",
  "display": { "scale": 2 },
  "sound": {
    "card": "config",
    "configDrk": { "card": "sbpro2", "port": "0x220", "irq": 7, "dma": 1 },
  },
  "startup": { "intro": false, "commandLine": ["-Q"] },
}
```
