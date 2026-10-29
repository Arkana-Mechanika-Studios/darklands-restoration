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
| `sound.card` | `"config"` | `--sound`, `--no-audio` (= `none`) | `config` (the card of CONFIG.DRK, as the original), `adlib`, `sb`, `sbpro2`, `mt32`, `cm32l`, `speaker` (not emulated: silent), `none` |
| `sound.mt32Roms` | `MT32_ROMS` next to the program | `--mt32-roms`; `DARKLANDS_MT32_ROMS` | MT-32 / CM-32L ROMs; without them the OPL3 FM drivers play instead, with a warning |
| `sound.audioPack` | none | `--audio-pack <folder>`; `DARKLANDS_AUDIO_PACK` | a folder of replacement music and battle effects (an audio pack, [PACKS.md](PACKS.md)); not part of the original, the game plays the same |
| `sound.configDrk` | absent (commented out in the template) | | replaces CONFIG.DRK's words (below) |
| `startup.launcher` | false | `--setup` (once), `--no-launcher` | the launcher at every start; false: only when needed (below) |
| `startup.banner` | true | `--no-banner` | the BANNER.DAT banner and the start-up checks |
| `startup.intro` | true | `--no-intro` | the opening animation |
| `startup.commandLine` | `[]` | `-- <words>` | the game's own DOS command line (`-Q`, `-O`, `-A<letter>`), a list of words or one string |
| `textFont` | built-in | `--text-font`; `DARKLANDS_TEXT_FONT` | 4096-byte VGA BIOS 8x16 font dump, or its base64 |
| `screenshotsFolder` | current folder | `--screenshots` | |
| `sdl.library` | the `SDL2.dll` next to the program (shipped on Windows), else the system's | `--sdl` | the SDL2 library to load (SDL host only) |
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

The launcher is the settings screen shown before the game: the game folder, the window size, fullscreen, the HD pack,
the audio pack (both: None, the packs in a `packs` folder, or a folder browsed for or dropped; [PACKS.md](PACKS.md)),
the sound card and the Roland ROM folder (MT-32 and/or CM-32L; with a sound test: ten seconds of the game's music through the drivers the game
will use) and the start-up screen and opening animation. Beside them: the release notes (`RELEASE_NOTES.md` next to the
program, copied there by the build from `releases/<version>/release-notes.md` when that is there), a link to the version's
page on GitHub (the only place the notes are when the file is missing), and a word on the project. It comes up:

* on the first run (no settings file was found);
* when the game cannot be found;
* when the game could not start (a damaged HD pack, a missing file …), with the reason;
* with `--setup`, or while **Shift** is held as the program starts;
* at every start when `startup.launcher` is true (the launcher's "Show launcher at every start").

`--no-launcher` turns it off. The game folder can be chosen with **Change…** (Windows' folder dialog; on other systems
Change… lets you type it), typed or pasted (Ctrl+V), dropped on the window, or chosen among the folders found with ←/→.
The Roland ROM folder is chosen the same way on its row (Enter or a click opens the dialog) or by dropping the folder with
the ROM files on the window; an HD pack is chosen by dropping its folder on the window. **Play** and **Save and quit** write
the changed keys into the settings file, keeping its comments and everything else; closing the window or Esc saves as
well. A setting the command line or an environment variable overrides is shown but cannot be changed there (its
explanation says what sets it).

The launcher is its own project, `Darklands.Launcher`, which references `Darklands.Platform` only and draws into its own
pixels; the SDL host gives it the window (`SdlLauncherSurface`) and its services (`LauncherHost`). Another launcher fits
in by implementing `ILauncher`.

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
| `clear` | empties the console |

The commands change the game being played between two frames; a screen shows a change the next time it
draws. The headless host runs the same commands with the step `console:<command>`.

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
