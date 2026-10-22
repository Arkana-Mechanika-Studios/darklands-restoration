# Darklands Restoration Project — architecture

A clean, modern C# (.NET 10) implementation of Darklands 483.07. The game's semantics are
established from the verified reference decompilation (compiles back to the original
bytes under MSC 6.00A, checked in a machine-local reliability report) and checked against
the original program with original-byte oracles. No decompiled C and no game bytes are copied
into this repository; the player supplies their own copy of Darklands.

Every reconstructed operation cites the original by its coordinates in `DARKLAND.EXE` only (RTLink
record and payload offset, `R044+0x0123 (1838:0123)`; `root SSSS:OOOO`; `DS:XXXX`), never by the
reference decompilation's names, files or revision: `docs/TRACEABILITY.md` describes the convention,
`docs/source-map/original-functions.csv` indexes the coordinates, and the mapping back to the reference
is kept machine-local (`local/reference-map.csv`, `tools/source-map/resolve.py`).

## Principles

* **Domain first.** Game concepts (characters, money, calendar, items, locations, events,
  cards, battles) are typed C# models with named fields. No DOS addresses, segments or raw
  record offsets outside the persistence layer.
* **KISS.** The simplest structure that expresses the rule. Plain classes and records, no
  frameworks, no service locators.
* **DRY.** One implementation per rule. Shared mechanisms are extracted only when the original
  provably uses the same mechanism.
* **Single responsibility.** A type owns one concept; rules are small pure functions over state.
* **Deterministic.** All randomness comes from an injected `IRandomSource` (the original MSC
  generator by default); wall-clock reseeding is an explicit injected policy.
* **Faithful by default, fixable by choice.** Original bugs are reproduced in `Faithful` mode
  and listed in one place (`OriginalBehaviour`), each with an optional fix.

## Layers (dependencies point down)

```
Hosts                    Darklands.Host.Sdl (SDL2 window, audio device, developer console, launcher)
                         Darklands.Host.Headless (scripted play, screenshots, traces)
        │
Darklands.Presentation   screens and the game loop's presentation (platform-agnostic: draws into a
        │                320x200 indexed screen, reads input through Darklands.Platform)
Darklands.Simulation     new game, card dispatch, overland travel
        │
Darklands.Cards          the card engine and the card scripts
        │
Darklands.Content        the game files: pictures, fonts, MSG texts, items, saints, cities, battle files
        │
Darklands.Core           domain model and rules (no I/O), battles included
        ▲
Darklands.Persistence    saved games (DKSAVEn.SAV / .BSV, docs/SAVES.md) and the .tmp records: the only
                         place that knows the original record layouts

Darklands.Platform       host interfaces (video, input, audio, clock, game timer), settings
Darklands.Audio          the player's sound drivers in an emulated PC; Darklands.Audio.Mt32 adds mt32emu
Darklands.Rendering      replacement graphics (HD packs) over the 320x200 screen, for the hosts (needs Platform only)
Darklands.DevConsole     the developer console (references Core only; no game project references it)
Darklands.Launcher       the settings screen before the game (references Platform only; draws into its own pixels;
                         only the SDL host references it, through ILauncher)
```

## Core domain (Darklands.Core)

| Area | Types | Notes |
| --- | --- | --- |
| Money | `Money` (florins, groschen, pfennigs) | 1 fl = 20 gr, 1 gr = 12 pf; original carry/borrow rules |
| Time | `GameDate`, `Calendar`, `TimeOfDay` | 365-day years, 1-based-overflow quirk, bands, daylight 05–18 |
| Random | `IRandomSource`, `MscRandom` | exact MSC 6 `rand()`; `Next(n)` = rand % max(n, 2), `Scaled(n)` |
| Characters | `Character`, `Attribute`, `Skill`, `CharacterStatus`, `Pack` | 7 attributes, 19 skills, current/maximum; 64 stacks per member |
| Party | `Party` | up to 5 members, leader, marching order |
| Items | `ItemStack`, `ItemDefinition` | |
| World | `Location`, `CityRecord` | |
| Events | `EventLog` (300 slots) | kinds, start/end dates, expiry handlers |
| Knowledge | `KnownBits` | the saints and formulas a member knows |
| Battles | `Battles/` (`TacticalBattle`, `BattlefieldBuilder` and the generators) | docs/BATTLES.md |
| Rules | `Trading`, `CampStay`, `SaintBook`, `SaintPrayer`, `Brewing`, `Practice`, `TimePassage`, `LocationRules` ... | small functions over the game state |

## Audio (Darklands.Audio)

The game's music and effects come from the player's own sound drivers, run unchanged in a small
emulated PC (`Machine`: an 8086 interpreter, the FM ports, a Sound Blaster DSP with its DMA channel,
an MPU-401 in UART mode, the timer's counter and the interrupt mask) and called through their slot
table as the game's thunks at root 047C:0783.. did (`Driver/SoundDriver`), on the game's timer
(`Driver/GameTimer`). `SoundSetup` chooses the drivers as the game does: CONFIG.DRK's card, its letter
(a/p/r/i/n) + SOUND.DLC/.DLB; the driver's name tells what it drives (FM, Roland, speaker, none); a
host may override the card (adlib, sb, sbpro2, mt32, cm32l). FM drivers play through `Synthesis/Opl3`;
the Roland driver's MPU bytes are grouped into MIDI messages (`MidiStream`) for an `IMidiSynth`.
`Darklands.Audio.Mt32` provides that synthesiser with Munt's mt32emu (LGPL, a separate NuGet library)
and the player's MT-32 / CM-32L ROMs, and `SoundSelection`, the host-independent choice (with the FM
drivers when the ROMs are missing). `SynthAudio` implements `IAudio` and renders 16-bit stereo for any
`IPcmOutput` (the SDL host's `SdlAudioOutput`, the headless host's .wav, `tools/tune`). The emulated
processor runs at 3000 instructions per ms (DOSBox's default, the reference capture's): a sample starts
as late into the driver's tick as its work takes.

## Timing (one clock)

`Darklands.Platform.IGameTimer` is the game's timer: the VGA's 70.086 Hz frames, a tick on six frames of
seven (`FrameTicker`), the 60 Hz clock both the sound driver and the PAN player count. With sound it is
`SynthAudio` (the ticks the player hears; a request is carried out at the tick it names, no sooner than
the output's latency); without, the host's clock (`ClockGameTimer`). `Cinematics` shows the cels at their
ticks (60 / rate ticks apart) and starts each tune at the tick after the previous file's last cel. For
the opening the timer follows a measured track (`OpeningTiming`, `Resources/OpeningTimer.txt`): the
original's interrupts were held up while the PAN files were read, and its music and pictures slowed down
together. `tools/intro/opening_timer.py` derives the track from the original's shared-clock capture and
compares a headless run with it (identical MPU bytes, every cel on the original's frame, samples within
a frame); `OpeningTimingTests` checks the same.

## Hosts

`Darklands.Host.Sdl`: a 720x400 window (the text screen at its own resolution, the 320x200 screen doubled
to 640x400 and centred, `DisplayLayout`), `--scale N`, `--resolution WxH`, `--fullscreen`, `--sound`,
`--mt32-roms`; the options also come from a settings file (`darklands.settings.json`, `Darklands.Platform.SettingsFile`, docs/SETTINGS.md): command line > environment > file > defaults. `Darklands.Host.Headless`: scripted play, screenshots, and with `sound:` the drivers in step
with the script's clock (`wav:`, `trace:`).

`Darklands.DevConsole`: the developer console, a host feature outside the game. It references only
`Darklands.Core`; no game project references it. Its commands read and change the game being played
(`DarklandsGame.Playing`, the game layer's only door for it) through the game's own types and rules. The
SDL host opens it with F11 (a key the original never reads) and draws it over the window in a TrueType
font (StbTrueTypeSharp, DejaVu Sans Mono), outside the game's picture; the headless host runs its commands
with `console:` steps (docs/SETTINGS.md).

## Rendering

Presentation draws every frame into `Darklands.Platform.Screen`: 320x200 palette indices and the 256
colours, exactly as the original. Hosts only adapt devices (show that frame, scaled; read input; play
sound) and make no rendering decisions.

For replacement graphics (HD packs, a later host-independent rendering library) the screen also keeps
`Screen.Owners`, a modern addition outside the game like the developer console: per pixel the id of the
asset that drew it (`ScreenOwners`, interned asset names, 0 = none). `Canvas` records it for every copy of
an image's pixels (only the pixels written), clears it for fills, outlines and single pixels, keeps it
through recolours, and saved regions and screens carry it back. Assets are named by one scheme
(`IndexedImage.AssetName`: `PICS\STARTSCR.PIC`, `COMMONSP.IMG#12`, `F01C.CAT/F01WKSW.IMC#3.5`,
`dropcap:D`); images built at run time may stay unnamed. A pixel a screen writes directly keeps a stale or
zero owner, and a replacement shows the original pixel there. The owners never change `Screen.Pixels`
or `Screen.Palette` (`CanvasOwnershipTests` compares the canvas with the drawing from before).
Beside each owner, `Screen.Texels` records which of the asset's own pixels it is (and whether it was drawn turned
over), and `ScreenOwners.SizeOf` the asset's size, so a drawing's place is known even when it is clipped, mirrored,
drawn twice or partly covered.

`src/Darklands.Rendering` (it depends on Platform only: no game code, no host) turns a screen and an HD pack
(`HdPack`: a folder of PNGs named after the assets, optional `pack.json`; settings key `display.hdPack`) into draw
items (`HdRenderer` → `HdFrame`): the frame itself scaled to the output rectangle, then per drawing of a replaced asset
its picture, turned as drawn, and last the frame again, transparent where a replacement shows (so the pointer, text or
a box over a picture changes only that small layer, never the picture's texture). Without a pack the frame is the
base item alone, exactly the picture shown before. The SDL host puts the items on SDL textures; the headless host
composes them in software (`FrameCompositor`) for `<shot>.hd.png`. A picture's own colours are recorded when it is
drawn (`ScreenOwners.SetOwnColors`); where the screen now shows other colours (a palette fade, the card's grey remap)
the replacement goes through the same change (`PaletteEffect`), and text drawn by and/or/xor recolours the picture's
pixels instead of owning them, so the card's hidden option lines stay hidden. Sprites' recolours and colour cycling are
not followed yet.

## Verification

* `tests/Darklands.Core.Tests`: unit tests per rule, the faithfulness registers and the source map.
* `tests/Darklands.Oracle.Tests`: original-byte oracles. Each rule runs both in C# and as the original
  function from the player's `DARKLAND.EXE` (Unicorn, see `tools/oracle`), over generated inputs, and
  the results must agree.
* `tests/Darklands.Cards.Tests`: every card, the golden card traces, the card oracle's results.
* `tests/Darklands.Battles.Tests`, `Darklands.Persistence.Tests`, `Darklands.Audio.Tests`,
  `Darklands.Platform.Tests`, `Darklands.DevConsole.Tests`, `Darklands.Host.Sdl.Tests`,
  `Darklands.Rendering.Tests`: the screens and battles, saved games, the sound drivers, settings, the console,
  the HD backend.

Tests that need the player's files read the game folder from `DARKLANDS_DIR` (or `DARKLANDS_EXE` for
the EXE alone); without them those tests pass without checking anything.
