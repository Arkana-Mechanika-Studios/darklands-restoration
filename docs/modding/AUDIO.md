# Making a sound pack

For musicians and sound designers. A sound pack ("audio pack") replaces the game's pieces of music and its battle sound
effects with files of your own: OGG, WAV, or General MIDI played through a SoundFont you supply. It changes only what you hear.
The game decides and times everything exactly as before, with or without a pack.

The exact catalogue and rules are in [PACKS.md](../PACKS.md#sound-packs). The ten-minute walkthrough in
[README.md](README.md#a-first-pack-in-ten-minutes) shows the whole loop.

## How a replacement works

The game still runs its original sound drivers underneath, because their timer is also the game's timer (the opening's
animation and the battle's pace follow it). A pack never replaces the drivers. While one of your files plays, the
drivers keep going, muted, and your file is mixed on top. So:

* Your piece starts when the original piece would start, and stays until the game changes or stops the music.
* A piece you did not replace plays as usual, so a pack can replace one cue or all of them.
* Music off in the game's options (Alt-M) means no piece starts; effects off (Alt-F) means no effect. The game's own
  choices stay.
* Pause (Alt-P) holds your sound and resume carries on. Stop fades the music over about a second.
* Entering a battle ends the music.
* The sound card chosen in the launcher still plays everything your pack does not replace.

## File formats

| Format | Notes |
|---|---|
| `.ogg` | Ogg Vorbis. The best choice for music: small and good |
| `.wav` | PCM 8, 16, 24 or 32 bit, or 32-bit float. Good for short effects |
| `.mid`, `.midi` | General MIDI, played through a SoundFont (below) |

Any sample rate (it is converted to the output's), mono or stereo (of more channels, the first two are used). If a cue
has both a recording and a `.mid`, the recording is used. Files are decoded when the game starts, in the background.

## Folder layout

Name each file after its cue, and put it anywhere inside the pack folder. All of these work:

```text
my-sound-pack/
  inn-1.ogg
  music/travel-2.ogg
  effects/battle-death.wav
  audio.json            (optional)
  pack.json             (optional: SoundFont, music per card)
```

The example in [`docs/audio-pack-example`](../audio-pack-example) (in a release: `packs/audio-pack-example` next to the
program) is a working pack of two generated test tones. Its `music/travel-2.ogg` replaces a travel piece, and its
`effects/battle-swing.wav` replaces the sound of a missed swing. It also shows the `audio.json` form. Choose it in the
launcher's "Audio pack" row, create a party and listen to the creation screen's music; in a battle, a missed blow chirps.

## What the cues are called

Each cue is a number the game sends to its driver, and has a name that does not change. Highlights, by what you hear
(the full table with every number is in [PACKS.md](../PACKS.md#catalogue)):

* **Towns:** `church-district`, `inn-1`, `inn-2` (also used when camping in a town), `marketplace`, `crafts-quarter`,
  `city-square-day`, `university`, `shell-game`, `fortress-day`, `city-dungeon`, `city-walls-day`, `city-walls-night`,
  `hamlet-square`, `saint-prayer`.
* **The road and the wild:** `travel-1`, `travel-2` (the game picks one of the two at random; `travel-2` is also the
  character creation), `travel-notice`, `camp-wilderness`, `grove-day`, `grove-night`, `ambush`, `wild-hunt`.
* **Scenes:** `party-death`, `robbers-city`, `holzfrau`, `schrat-fruit`, `dream-1`, `dream-2`, `astrologer`,
  `templars-burning`.
* **Opening and ending:** `opening-1` to `opening-7`, `ending-prelude`, `ending-1` to `ending-6`.
* **Battle effects:** `battle-death`, `battle-missile-heavy`, `battle-missile-light`, `battle-cloud`, `battle-blast`,
  `battle-ambush-seen`, `battle-knockback`, `battle-swing`, `battle-refused`.

A few pieces the game is not known to play can still be given by number: `music-XX` or `effect-XX` with two hex digits,
for instance `music-19.ogg`. The driver commands (0 to 7: reset, stop, pause, resume and so on) cannot be replaced.

### Which cues repeat

Town and map pieces, camp and so on repeat by default. Scene cues (the opening, the ending, an encounter) and all
effects play once. The catalogue's "Loops" column says which is which. You can change it for any cue in `audio.json`
(`"loop": true` or `false`).

## `audio.json`

Optional. It names files that are not called after their cue, sets a cue's `loop` and `volume`, and sets a volume for the
whole pack. Comments and trailing commas are allowed; paths are relative to the pack folder.

```jsonc
{
  // every replacement at 80 %
  "volume": 0.8,
  "cues": {
    // a file that is not called after its cue
    "inn-1": "tavern.ogg",
    // file, loop and volume
    "travel-1": { "file": "music/road.ogg", "loop": true, "volume": 0.6 },
    // a cue found by its name already; only the volume is set
    "battle-death": { "volume": 1.5 }
  }
}
```

`volume` is a number from 0 to 4; 1 is the file's own level. Entries here win over the file names.

## Music per card

You can also give music to a **card**, a scene of the game (an inn, a city gate, the map, a street), rather than to one of
the driver's numbered pieces. In `pack.json`, write `card:` and the card's number in hexadecimal, then name a file; the
file can be called anything:

```json
{
  "card:29": "music/party-at-the-inn.ogg",
  "card:0C": { "file": "map.mid", "volume": 0.8 },
  "soundfont": "fonts/gm.sf2"
}
```

(`card:29` is the party at the inn, shown when a new game starts; `card:0C` is the map.) The value is a file, or an
object with `file`, `loop` and `volume`; a card's music repeats unless you write `"loop": false`. The card numbers are
those of `src/Darklands.Cards/CardTable.cs` (0 to 365).

The rules:

* The file starts when the card is shown and plays in place of the game's piece.
* The next card with the same file keeps it playing, without a restart. A card with another file changes to it.
* A card with no music of yours brings the game's own piece back.
* A battle ends it, as it ends the game's music.
* With music off in the game's options, none plays. Pause holds it.
* Loading a saved game shows the card again, so its music starts again.

**Ten cards silence the music as they open**, so music given to them stops at once and there is no point in giving them
any (the numbers are hexadecimal, as in `card:`): 03 (city gate at night), 06 and 08 (main street, by day and by night), 09 and 0A (side street, by day and by night),
16 (market at night), 19 (city square at night), 73 (before the city walls), 92 (the robber knight's men) and 115 (approaching
a walled city).

A [data mod](DATA-MODS.md) can name music for its own cards too (`"music"` in the card); where a pack and a mod name the
same card, the pack wins.

## General MIDI and the SoundFont

General MIDI is a fixed list of 128 instruments that every General MIDI file asks for by number, so any General MIDI
SoundFont plays any General MIDI file. The program renders each `.mid` through a SoundFont (`.sf2`) into a clip, which then
plays like an OGG (loop, volume, fade and pause as for any cue).

**No SoundFont comes with the program.** It is a separate file that you, and each of your players, put in place. The program
uses the first it finds of:

1. the pack's own, named in `pack.json` (`"soundfont": "fonts/gm.sf2"`, relative to the pack folder);
2. the one the player chose: the launcher's **SoundFont** row (Enter chooses a file, or drop an `.sf2` on the launcher
   window; the arrow keys go back to the default), `--soundfont <file>`, or `sound.soundFont` in the settings file;
3. the first `.sf2` (by name) in a `SOUNDFONT` folder next to the program.

A named file that does not exist is reported, and the next choice is tried. With no SoundFont, the `.mid` cues are skipped and
the game's own music plays; one message in the log (and the developer console, F11) says where to put one. A `.mid` is rendered
the first time its cue is needed, so a long piece can take a moment; the game's own piece is muted until it is ready (unless no SoundFont was found). A
file longer than ten minutes is refused.

Free General MIDI SoundFonts exist, such as FluidR3_GM (MIT licence) and GeneralUser GS (read its terms before you use or
share it). The project does not include or redistribute them. Download one yourself, and check its licence before
you put one inside a pack you publish.

## MT-32 notes

By default the game plays on the sound card named in your copy's own setup, usually a Roland MT-32. For that the player puts
MT-32 or CM-32L ROM files in the `MT32_ROMS` folder; without them an FM sound card plays instead.

* Pack files are mixed on top of the drivers, so they sound the same on every card. The card affects only what you did not replace.
* **A `.mid` written for the MT-32 sounds wrong through a General MIDI SoundFont.** The MT-32 has its own instruments and
  its own drum map. Write or convert your files for General MIDI: instrument numbers from the General MIDI list, and
  drums on channel 10.
* If you want the MT-32's own sound, you do not need a pack at all: leave the music alone and it is played by the
  game's drivers.

## Advice on loudness and loops

* **Loops.** A cue that repeats starts again from the beginning of the file: there are no loop points. Cut your file at a beat
  where the end leads into the start, remove silence at both ends, and avoid a reverb tail that is cut off at the loop.
* **Loudness.** The file is played at its own level times `volume`; nothing is normalised for you. Set your pieces to
  a similar level, and compare with the game's own sounds that you did not replace. Leave headroom
  (peaks below about -1 dBFS); use `"volume"` to trim, and remember that values above 1 add gain and can clip.
* **Effects are short.** Cut the silence off the start of an effect, so that it lands with the action.
* **Scenes are timed by the game.** The opening's and ending's pictures follow the game's timer, not your piece, so a
  replaced opening piece does not set their pace. Listen to the original scene and time yours to fit it.
* **Start and stop.** Your piece starts within a few milliseconds of the original, not on its exact sample. A piece that
  takes a long time to build is fine; one that depends on an instant start is not.

## Try it without playing

The headless host can render a scripted run and write what was heard, which is handy for testing a pack on a machine with no audio:

```text
Darklands.Host.Headless <game folder> <output folder> sound:sb audiopack:<pack folder> wav:heard.wav trace:heard.trace key:c wait:600
```

It is a tool you build from the source tree; it is not part of the release. Add `soundfont:<file>` to play `.mid` cues.

## Limits

* No checker for packs yet: the console lists the problems at the start.
* Only the pieces of music and the battle effects have cues; nothing else of the game's sound can be named in a pack.
* The driver commands cannot be replaced.

## Rights

Only put into a pack what you may share: your own music and sounds, or works whose licence allows it. The game's own music
and sounds are not free; do not record, convert or re-export them into a pack. The example pack's two files are generated test tones.
Say in your pack's description which licence its files are under.
