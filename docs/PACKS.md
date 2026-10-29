# Picture and sound packs

A pack changes what the game looks or sounds like, never what it does: the game makes the same decisions, draws
the same random numbers and keeps the same timing with or without one. There are two kinds, and one folder can hold
both:

* a **picture pack** (HD pack): PNG files that replace the game's pictures and sprites;
* a **sound pack** (audio pack): OGG or WAV files that replace pieces of music and battle effects.

Packs are not part of the original game. With no pack set, the game looks and sounds exactly as before.

| | Picture pack | Sound pack |
|---|---|---|
| settings key ([SETTINGS.md](SETTINGS.md)) | `display.hdPack` | `sound.audioPack` |
| command line | `--hd-pack <folder>` | `--audio-pack <folder>` |
| environment | | `DARKLANDS_AUDIO_PACK` |
| launcher | "HD pack" row | "Audio pack" row |
| files | `*.png`, named after the asset | `*.ogg`, `*.wav`, named after the cue (catalogue below) |
| manifest (optional) | `pack.json` | `audio.json` |
| headless step | `hdpack:<folder>` | `audiopack:<folder>` |

Problems (a missing folder, an unknown name, a broken file) are written to the console at the start; whatever cannot
be used keeps the game's own picture or sound.

### Choosing a pack in the launcher

Put each pack in a folder of its own inside a `packs` folder: next to the program (the release's example is there),
in the game folder, or next to the game folder. The launcher's "HD pack" and "Audio pack" rows then go through None and
the packs found with ← and →; Enter (or a click) browses for a folder anywhere else, and a pack's folder dropped on the
launcher window is taken as the pack of each kind it holds. The row shows the pack's name, marked (!) when it cannot be
read as one; with the row chosen, the help line says how many pictures or sounds it replaces and whether its `audio.json` was read,
or what is wrong with it. None turns the pack off. The choice is written into the settings file (`display.hdPack`,
`sound.audioPack`); a pack given on the command line or by `DARKLANDS_AUDIO_PACK` is shown but cannot be changed there.
The launcher's "how to make a pack" link opens this guide.

## Picture packs

### Names

A replacement is found by the asset's name plus `.png`, with `\` and `/` as folders and `:` written as `_`, in any
letter case:

* `PICS/STARTSCR.PIC.png`: a whole picture file;
* `COMMONSP.IMG#12.png`: image 12 of a bank;
* `F01C.CAT/F01WKSW.IMC#3.5.png`: a frame of a battle animation (frame, facing);
* `BC/CAVE.FLC@1A40.png`: a battlefield sprite (member and offset);
* `dropcap_D.png`: a drop capital of the card text.

To find the names on a screen, take a shot with the headless host and any pack (an empty folder is enough):

```text
Darklands.Host.Headless <game folder> <output folder> hdpack:<pack folder> wait:200 shot:start
```

Besides `start.png` it writes `start.hd.png` (the frame with the replacements, `hdscale:<n>` times 320x200, default 4)
and `start.assets.txt`, one line per asset on the screen: its name, its own size, the pixels it shows and `replaced`
when the pack has it.

### Pictures

A picture replaces the whole asset: it is stretched over the asset's rectangle, so any size works, and a whole
multiple of the original size (2x, 4x ...) keeps its proportions. It shows only where the original asset is still seen,
so text, menus, the pointer and anything drawn over it stay on top.

A replaced picture follows the palette: it fades, greys or lightens with the original (the card option fade). Not
yet: colour cycling, and sprites drawn in other colours (recoloured figures, markers) keep the replacement's own
colours.

### `pack.json`

Optional; it names files that are not called after their asset, and its entries win over the file names:

```json
{ "assets": { "PICS\\STARTSCR.PIC": "title.png", "COMMONSP.IMG#12": "icons/sword.png" } }
```

Comments and trailing commas are allowed; paths are relative to the pack folder.

## Sound packs

### How it works

The game sends numbers to its two sound drivers: pieces of music to the towns-and-map driver and effects to the battle
driver. The restoration runs the original drivers in an emulated PC, and their timer is the game's timer (the music,
the opening's animation and the battle's pace follow it). A sound pack therefore never replaces the drivers: they
keep running underneath, muted while a replacement plays, and every call the game makes reaches them unchanged. The
pack's files are mixed on top. What you hear changes; what the game does and when does not.

* A replaced **piece** starts when the drivers would start the original and owns the music until the next piece:
  the drivers stay muted also while their piece fades out after a stop or the game asks for silence.
  An unreplaced piece is heard as usual.
* A replaced **effect** mutes the drivers while it plays (an unreplaced effect sounding at the same moment is muted
  with it).
* Stop (the music fades over a second), silence (the city menus) and reset end the replacement; the game's pause
  (Alt-P) holds it and resume goes on. Entering a battle ends the music, as the drivers are swapped.
* The game's own choices stay: with music off in the game's options, no piece starts; with effects off, no effect
  (except the refused order, which the game always plays); the sound card chosen still plays everything the pack
  does not replace.

### Files

Name each file after its cue (the catalogue below) and put it anywhere in the pack folder: `inn-1.ogg`,
`music/travel-2.ogg`, `effects/battle-death.wav`. Formats:

* `.ogg`: Ogg Vorbis (decoded with NVorbis);
* `.wav`: PCM 8, 16, 24 or 32-bit, or 32-bit float.

Any sample rate (converted to the output's), mono or stereo (of more channels the first two are used). Pieces repeat
by default when the original's place does (a town, the map, camp: "loops" in the catalogue); cues of a scene (the
opening, the ending, an encounter) and effects play once. Files are decoded at the start, in the background.

Every number of a driver can also be named generically, `music-XX` or `effect-XX` with two hex digits: `music-19.ogg`
replaces piece 19h, which the game is not known to play. The driver commands (0 to 7: reset, stop, pause, resume,
current piece, silence, ready) cannot be replaced.

### `audio.json`

Optional. It names files that are not called after their cue, and sets a cue's `loop` and `volume`; its entries
win over the file names. `volume` at the top scales every cue (0 to 4, 1 is the file's own level).

```jsonc
{
  "volume": 0.8,
  "cues": {
    "inn-1": "tavern.ogg",
    "travel-1": { "file": "music/road.ogg", "loop": true, "volume": 0.6 },
    "battle-death": { "volume": 1.5 }
  }
}
```

Comments and trailing commas are allowed; paths are relative to the pack folder.

### The example pack

[`docs/audio-pack-example`](audio-pack-example) (in a release: `packs/audio-pack-example` next to the program)
replaces the map's travel pieces (also heard while creating the party)
with a soft generated drone and the battle's missed swing with a short generated chirp. It holds no music of the
game: both files are test tones made with ffmpeg from a formula. Choose it in the launcher's "Audio pack" row (or
start with `--audio-pack packs/audio-pack-example`, or set `sound.audioPack`) and create a party: the creation
screen's music is the drone; in a battle, a missed blow chirps.

### Trying a pack without playing

The headless host renders the drivers in step with a script and can write what was heard:

```text
Darklands.Host.Headless <game folder> <output folder> sound:sb audiopack:<pack folder> wav:heard.wav trace:heard.trace key:c wait:600
```

`heard.wav` has the replacements mixed in; `heard.trace` lists the drivers' ticks and the numbers they carried out,
the same with or without the pack.

### Catalogue

The names are part of the pack format and do not change. "Where" says where the game plays the number (with the
original's code coordinates for those who look them up).

| Name | Kind | Number | Loops | Where |
|---|---|---|---|---|
| `church-district` | music | 08h | yes | the church district (cards R048+0x2504) |
| `inn-1` | music | 09h | yes | an inn at night, by day one of two at random; camp in a town, one of two |
| `inn-2` | music | 0Ah | yes | an inn by day, the other of two; camp in a town, the other of two |
| `marketplace` | music | 0Bh | yes | the marketplace by day (R049+0x05B2) |
| `crafts-quarter` | music | 0Ch | yes | the business district, the crafts quarter and the military crafts quarter |
| `saint-prayer` | music | 0Dh | yes | praying to a saint (the previous piece comes back afterwards) |
| `city-square-day` | music | 0Eh | yes | the city square by day (R048+0x1B18) |
| `university` | music | 0Fh | yes | the university (R056+0x088C) |
| `shell-game` | music | 10h | yes | the shell-game swindler in the market (R088+0x0B20) |
| `fortress-day` | music | 11h | yes | the city lord's fortress by day (R050+0x1CEE) |
| `party-death` | music | 12h | no | the whole party dies (R029+0x2278) |
| `travel-1` | music | 13h | yes | travelling on the map, one of two at random (R030+0x00A8) |
| `travel-2` | music | 14h | yes | travelling on the map, the other of two; also the character creation (R037+0x0000) |
| `camp-wilderness` | music | 15h | yes | camping in the wilderness (R036+0x0082) |
| `city-dungeon` | music | 16h | yes | the city dungeon (R047+0x01A6) |
| `grove-day` | music | 17h | yes | a grove outside the city by day (R052+0x104C) |
| `travel-notice` | music | 18h | no | the cue when the map travel notices a place |
| `city-walls-day` | music | 1Bh | yes | before the city walls by day (R045+0x04C0) |
| `city-walls-night` | music | 1Ch | yes | before the city walls at night (R045+0x04C0) |
| `grove-night` | music | 1Dh | yes | a grove outside the city at night (R052+0x164E) |
| `robbers-city` | music | 1Fh | no | robbers in a city street (R053+0x0000) |
| `ambush` | music | 20h | no | robbers or bandits on the road, a spider nest, schrats in the forest |
| `wild-hunt` | music | 23h | no | the Wild Hunt rides at night |
| `holzfrau` | music | 24h | no | a holzfrau's advice about the saints (R086+0x0C40) |
| `schrat-fruit` | music | 25h | no | a schrat's magic fruit; the Wild Hunt's quarry (R087+0x14EE, R086+0x084E) |
| `dream-1` | music | 26h | no | called in a dream, first part (R117+0x0896) |
| `dream-2` | music | 27h | no | called in a dream, second part (R117+0x0896) |
| `astrologer` | music | 28h | no | the astrologer's manor (R117+0x03AA) |
| `hamlet-square` | music | 29h | yes | a hamlet's square (R077+0x1320) |
| `templars-burning` | music | 2Ah | no | Templars burning a friar (R117+0x1152) |
| `ending-prelude` | music | 2Dh | no | the start of the ending (R133+0x186C; plays ENDDARK1.DGT's speech) |
| `opening-1` | music | 2Fh | no | the opening, first scene (root 03A1:000A; plays OPENDARK.DGT's clips) |
| `opening-2` | music | 30h | no | the opening, second scene |
| `opening-3` | music | 31h | no | the opening, third scene |
| `opening-4` | music | 32h | no | the opening, fourth scene |
| `opening-5` | music | 33h | no | the opening, fifth scene |
| `opening-6` | music | 34h | no | the opening, sixth scene |
| `opening-7` | music | 35h | no | the opening, seventh scene |
| `ending-1` | music | 36h | no | the ending, first scene (R133+0x186C) |
| `ending-2` | music | 37h | no | the ending, second scene |
| `ending-3` | music | 38h | no | the ending, third scene |
| `ending-4` | music | 39h | no | the ending, fourth scene (ENDDARK2.DGT's speech from here) |
| `ending-5` | music | 3Ah | no | the ending, fifth scene |
| `ending-6` | music | 3Bh | no | the ending, sixth scene |
| `battle-death` | effect | 0Ah | no | a combatant dies (R014+0x2324) |
| `battle-missile-heavy` | effect | 0Bh | no | a heavy missile is loosed (R018+0x0000) |
| `battle-cloud` | effect | 0Ch | no | a cloud or slowing spell or potion lands (R019+0x0000) |
| `battle-blast` | effect | 0Dh | no | a flash or blast lands (R019+0x0000) |
| `battle-ambush-seen` | effect | 0Eh | no | hidden enemies are spotted (R014+0x4442) |
| `battle-missile-light` | effect | 0Fh | no | an arrow or bolt is loosed (R018+0x0000) |
| `battle-knockback` | effect | 10h | no | a hit knocks the target back |
| `battle-swing` | effect | 11h | no | a melee swing that does not hit |
| `battle-refused` | effect | 12h | no | an order that cannot be carried out (R014+0x4694; plays even with effects off) |

Pieces the game is not known to play (19h, 1Ah, 1Eh, 21h, 22h, 2Bh, 2Ch, 2Eh) and the battle numbers 7 to 9 (allowed by
the game's sound options but not sent by the battle) can be given as `music-XX` / `effect-XX`.

### Limits (for now)

* A replacement starts within one audio block (a few milliseconds) of the original, not on its exact sample.
* The opening's and ending's pictures follow the drivers' timer, so a replaced opening piece does not set their pace.
* No checker for packs yet (the console lists the problems at the start); no text packs.

## Rights

Only put into a pack what you may share. The original game's music, sounds and pictures are not free; a pack of
your own work (or of works whose licence allows it) is. The example pack's two files are generated test tones and
part of this repository.
