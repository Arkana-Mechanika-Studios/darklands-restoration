# Modding Darklands Restoration

This is the friendly way in. It tells you what you can change, where the files go, how to switch things on and how to
find out why something does not work. The exact references are [PACKS.md](../PACKS.md) (pictures and sound) and
[MODS.md](../MODS.md) (data mods); this guide points to them whenever you need every detail.

* [GRAPHICS.md](GRAPHICS.md): for artists, replacing the game's pictures.
* [AUDIO.md](AUDIO.md): for musicians and sound designers, replacing music and battle sounds.
* [DATA-MODS.md](DATA-MODS.md): for writers, adding or changing cards (scenes with a text and choices).

## The three kinds of mod

| Kind | What it changes | Made of | Changes how the game plays? |
|---|---|---|---|
| Picture pack ("HD pack") | what the game looks like | PNG files | no |
| Sound pack ("audio pack") | the music and the battle sound effects | OGG, WAV or General MIDI files | no |
| Data mod | the content: new scenes, or a different scene in place of one of the game's | one `mod.json` file | yes |

Packs and mods are separate things. One folder can be both a picture pack and a sound pack (they share an optional
`pack.json`), but a data mod is always its own folder with its own `mod.json`.

## What "Classic" means

With no pack and no mod, the game is Classic: it plays exactly as the 1992 game does. Nothing in the program looks
for packs or mods on its own; they are used only when you choose them.

* **Packs only change presentation.** The game makes the same decisions, draws the same random numbers and keeps the same
  timing with or without a pack. A game played with packs is the same game, looking and sounding different. Your saved
  games are not marked in any way.
* **Data mods change the content.** A card a mod adds or replaces is new game, and it is no longer the 1992 game. Such a
  game is called modded. When you save a modded game, the program writes a small companion file beside the usual save
  (`DKSAVE<n>.MODS.JSON`, next to `DKSAVE<n>.SAV`) that records which mods were used. The `.SAV` itself stays in the
  original format, so the original game can still load it; loaded without its mods, a game carries on from the
  nearest ordinary card. See [MODS.md](../MODS.md#saved-games-of-a-modded-game).

You need your own copy of Darklands for any of this. Nothing of the game comes with the program, and a pack or mod
never needs to (see "Rights" below).

## Where the files go

The release comes with the program (`DarklandsRP.exe`), a `docs` folder (these guides), a `packs` folder with a small
example sound pack and a `mods` folder with a small example mod.

```text
DarklandsRP.exe
darklands.settings.json      (written on the first run)
packs/
  audio-pack-example/        (comes with the release)
  my-first-pack/             (yours)
mods/
  wayside-shrine/            (comes with the release)
SOUNDFONT/                   (optional: your own .sf2 file for MIDI music)
```

* **Packs:** one folder per pack inside `packs`. The launcher finds it there. A `packs` folder in the game folder, or next
  to it, is looked in too. A pack can also live anywhere else if you browse to it or name its folder explicitly.
* **Mods:** one folder per mod, with a `mod.json` inside, in a `mods` folder next to the program (or in or beside the game folder).
* **SoundFont:** a `.sf2` file in the `SOUNDFONT` folder, or chosen in the launcher ([AUDIO.md](AUDIO.md)).

Never put the game's own files in a pack or a mod.

## Switching things on

**In the launcher** (the settings screen shown before the game; hold Shift while starting, or start with `--setup`, to
see it again):

* the **HD pack** row: None, then each pack found in a `packs` folder; use the left and right arrows. Enter browses for a
  folder somewhere else, and you can also drop a pack's folder on the launcher window.
* the **Audio pack** row: the same, for sound packs.
* the **SoundFont** row: the `.sf2` that plays MIDI music.

The row shows the pack's name, marked with `(!)` if it cannot be read as a pack, and the help line under the rows says how many
pictures or sounds it replaces, or what is wrong. **Play** or **Save and quit** writes your choice into the settings file.

**In the settings file** (`darklands.settings.json`, commented template written on the first run; all the keys are in
[SETTINGS.md](../SETTINGS.md)):

```json
{
  "display": { "hdPack": "packs/my-first-pack" },
  "sound": { "audioPack": "packs/my-first-pack", "soundFont": "SOUNDFONT/gm.sf2" },
  "mods": ["wayside-shrine"]
}
```

**On the command line**, which wins over the settings file:

```text
DarklandsRP --hd-pack packs/my-first-pack --audio-pack packs/my-first-pack --soundfont SOUNDFONT/gm.sf2 --mod wayside-shrine
```

`--mod` can be given more than once; mods load in that order. There is also the environment variable
`DARKLANDS_AUDIO_PACK` for the sound pack. A choice given on the command line or in the environment shows in the launcher
but cannot be changed there.

Data mods have no launcher row yet; name them in the settings file or on the command line.

## Testing and troubleshooting

Whatever cannot be used is reported, and the game falls back to its own picture or sound. It is never a reason for the
game to stop.

* **Read the messages.** The program writes problems to the console (the error output) as it starts: a missing folder, a
  name it does not know, a file it cannot read, a mod it refuses and why. Start it from a terminal to see them. The
  developer console (F11 in the game) and the launcher's help line show some of them too.
* **Screenshots.** F12 saves a screenshot, which is the easiest way to compare before and after.
* **Pack not listed.** Is it inside a `packs` folder, with one folder per pack? Are the files inside the folder you
  chose, not one level deeper in a second folder left by unpacking an archive? Subfolders *inside* a pack are fine; the
  files are found anywhere under the pack folder.
* **A picture does not change.** The name must be the asset's name plus `.png` ([GRAPHICS.md](GRAPHICS.md)). Letter case
  does not matter, but a typo does. A name that matches nothing is simply never used.
* **A sound does not change.** The file name must be the cue's name (`inn-1.ogg`) or be named in `audio.json`
  ([AUDIO.md](AUDIO.md)). Is music on in the game's options (Alt-M)? Are effects on (Alt-F)?
* **MIDI is silent.** There is no SoundFont: put an `.sf2` in `SOUNDFONT` or choose one in the launcher. The game's own
  music plays meanwhile.
* **A mod is not played.** A mod is played whole or not at all; the console names the mod and the field that is wrong
  ([MODS.md](../MODS.md#when-a-mod-is-not-played)). Comments and trailing commas are allowed in `mod.json`, but a
  value of the wrong type (`"400"` instead of `400`) is not.
* **Back to Classic.** Set the rows to None, remove the `mods` entry and the game is exactly the original again.

## A first pack in ten minutes

You will make a sound pack that replaces the music of the map, and add a picture to it. All you need is a sound file of
your own (any short recording you made, or a tone from an audio editor) and, for the picture, a PNG you drew.

**1. Make the folder (1 minute).** Next to `DarklandsRP.exe`, inside `packs`, create `my-first-pack`.

**2. Add a sound (2 minutes).** The map's travel music is cue `travel-2` (the game plays one of two travel pieces,
`travel-1` and `travel-2`; `travel-2` is also heard in the character creation). Save your sound as Ogg Vorbis or WAV,
name it `travel-2.ogg` (or `travel-2.wav`) and put it in `my-first-pack`. To make the other travel piece play it too,
add this `audio.json` beside it:

```jsonc
{
  "cues": {
    "travel-1": { "file": "travel-2.ogg", "volume": 0.8 }
  }
}
```

**3. Add a picture (3 minutes).** Name a PNG after a game picture plus `.png`. The title screen's picture is
`PICS/STARTSCR.PIC`, so the file is `PICS/STARTSCR.PIC.png`: a `PICS` folder inside your pack, which puts the file at
`my-first-pack/PICS/STARTSCR.PIC.png`. Keep the proportions of the original (a whole multiple of its size, for
example four times). [GRAPHICS.md](GRAPHICS.md) shows how to check the size.

**4. Choose it (1 minute).** Start `DarklandsRP.exe` with Shift held, or `DarklandsRP --setup`. In the **HD pack** and
**Audio pack** rows press the right arrow until `my-first-pack` shows. Check the help line: it says how many pictures
and sounds the pack replaces. Press **Play**.

**5. Look and listen (3 minutes).** The title screen shows your picture. Create a party: the music of the creation
screen is yours. Press F12 for a screenshot if you want to compare.

If something is wrong, go through "Testing and troubleshooting" above. From here, the other guides take you the rest of the way.

## Rights

Only put into a pack or mod what you may share: your own work, or work whose licence allows it. The original game's
pictures, music and sounds are not free; do not copy them into a pack, and do not publish packs made of them. Free
General MIDI SoundFonts exist, but check each one's licence before you share it. A SoundFont is never part of the
download, and it is best left out of your pack too, unless its licence allows it. When you publish, say what your files
are and under which licence, so that others know what they may do with them.

## Going further

* The exact catalogues, manifests and rules: [PACKS.md](../PACKS.md) and [MODS.md](../MODS.md).
* All the settings: [SETTINGS.md](../SETTINGS.md).
* Cards written in C# for your own build: [MODS.md](../MODS.md#cards-in-c) and [CARD_SCRIPTS.md](../CARD_SCRIPTS.md).
