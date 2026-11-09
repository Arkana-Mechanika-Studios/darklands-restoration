# Contributing to the Darklands Restoration Project

Thank you for wanting to help. This guide says what the project is, how to build it, which kind of work suits you,
how a change gets in, and the few rules that are not negotiable. Read the first section and the lane you want;
skim the rest.

Contents:

1. [What the project is](#1-what-the-project-is)
2. [Building and running](#2-building-and-running)
3. [The lanes](#3-the-lanes)
4. [How a change gets in](#4-how-a-change-gets-in)
5. [Faithfulness, in plain words](#5-faithfulness-in-plain-words)
6. [Rules for committed text, and the licence](#6-rules-for-committed-text-and-the-licence)
7. [Getting help](#7-getting-help)

## 1. What the project is

The Darklands Restoration Project is a C# reimplementation of *Darklands*, MicroProse's 1992 role-playing game,
version 483.07. It is not a remake and not an emulator: every rule of the original program was rewritten in C#
and is checked against the original program running.

The promise is simple:

* **Classic mode plays exactly like the original 483.07.** The same rules, the same random draws, the same saved
  games (saves are shared with the original in both directions).
* **Everything new is optional and behind a switch.** A mod, a picture or sound pack, a convenience or a new screen
  is something a player turns on. With nothing turned on, the game is the 1992 game.

Two rules follow from this and apply to every contribution:

* **You need your own copy of the game** (English 483.07, for example the GOG release). The program reads your
  installation.
* **Never commit game files, and never commit text or data derived from them** (card texts, item names, pictures,
  music, tables copied from the game's files). Nothing from the game is in this repository, and it stays that way.
  Test data that needs the game's files reads them from your installation at run time.

## 2. Building and running

You need the .NET SDK named in `global.json` (10.0.403, with the .NET 10.0.14 runtime; a later 10.0 feature band is accepted). The solution is
`Darklands.slnx`.

```
dotnet build Darklands.slnx
dotnet test Darklands.slnx
```

CI builds in Release (`dotnet build Darklands.slnx -c Release`, then `dotnet test Darklands.slnx -c Release --no-build`)
on Windows and on Ubuntu 24.04; Linux needs the system's SDL 2 (`libsdl2-2.0-0` on Debian and Ubuntu).

To play from the source tree, give the SDL host your game folder:

```
dotnet run --project src/Darklands.Host.Sdl -- "C:/GOG Games/Darklands"
```

Without a folder the host searches for your installation, and the launcher comes up if it cannot find one.
All options (settings file, command line, environment variables) are in `docs/SETTINGS.md`.

Release builds, if you work on packaging:

```
dotnet publish src/Darklands.Host.Sdl -p:PublishProfile=win-x64
dotnet publish src/Darklands.Host.Sdl -p:PublishProfile=linux-x64
```

### The test projects

| Project | What it checks |
|---|---|
| `tests/Darklands.Core.Tests` | the rules, the source map, the faithfulness coverage lists, the naming rule of section 6 |
| `tests/Darklands.Cards.Tests` | the card engine and the card scripts |
| `tests/Darklands.Battles.Tests` | battles |
| `tests/Darklands.Oracle.Tests`, `tests/Darklands.WorldCalls.Tests` | the rules and the world map against recorded runs of the original functions |
| `tests/Darklands.Persistence.Tests` | saved games |
| `tests/Darklands.Audio.Tests` | the sound drivers and sound packs |
| `tests/Darklands.Platform.Tests`, `tests/Darklands.Host.Sdl.Tests`, `tests/Darklands.Launcher.Tests`, `tests/Darklands.Rendering.Tests`, `tests/Darklands.DevConsole.Tests` | settings, hosts, launcher, HD packs, developer console |
| `tests/Darklands.Architecture.Tests` | the dependency rules of `docs/ARCHITECTURE-RULES.md` |

Tests that need the original game read your game folder from the environment variable `DARKLANDS_DIR` (or the
executable alone from `DARKLANDS_EXE`). **Without it those tests pass without checking anything.** So a green run
on a machine without the game proves less than a green run with it: say in your pull request which of the two you
had. CI has no game files, so CI only proves that the project builds and the game-independent tests pass.

## 3. The lanes

Work falls into six lanes. Pick the one that matches what you want to do and the skills you have. If your idea
touches two lanes, say so in the issue (section 4); the stricter lane's rules win.

| Lane | One line | Who |
|---|---|---|
| Faithful | the game logic, equal to the original | few, experienced contributors |
| Portable | more platforms and devices, no change to the game | anyone with platform skills |
| Moddable | mod and pack formats and their loaders | anyone with C# skills |
| Extendable | optional new features, screens, commands | anyone with C# skills |
| Playtesting and bug reports | finding and describing problems | everyone with the game |
| Content | packs and translations | artists, musicians, writers, translators |

### Foundation: read this before any code lane

Two documents describe the structure that every code change must respect.

**The architecture** (`ARCHITECTURE.md`). The code is in layers and dependencies point down: the domain and rules
(`Darklands.Core`, no input or output) at the bottom, then the game files (`Darklands.Content`), the card engine
(`Darklands.Cards`), the game loop (`Darklands.Simulation`), the screens (`Darklands.Presentation`) and finally the
hosts (`Darklands.Host.Sdl`, `Darklands.Host.Headless`). Saved-game layouts are known only to `Darklands.Persistence`.
Randomness comes only from an injected random source, never from the system clock.

**The command layer** (`docs/COMMANDS.md`). Screens read the player only through `GameUi`. A script or a
convenience feature plays the game by issuing *player commands* (`PressKey`, `ClickAt`, `PointAt`, `ChooseLine`),
which the screens carry out exactly as a typed key or a click. A command is never a shortcut into the game's
state: no direct call of a menu's choice, no state written by the caller.

**The architecture test** (`tests/Darklands.Architecture.Tests`, rules in `docs/ARCHITECTURE-RULES.md`) enforces
this on the compiled code. In short:

* each assembly references only the layers below it, and nothing but the hosts uses the console, the launcher or
  the HD backend;
* the rules (`Core`, `Cards`, `Simulation`) do no file or console I/O and read no wall clock;
* views only show and ask: a screen does not write the game model, and reads the host only through `GameUi`;
* saved games are used only where the game saves and loads.

Where code breaks a rule today, the file lists it as a known exception with its reason. The lists only shrink: do
not add an exception to get a change through, and if you remove the last break of one, take it off the list (the
test fails otherwise).

### Faithful lane

**Covers.** The game logic: `src/Darklands.Core` (rules, battles, generators), `src/Darklands.Cards` (the card
engine and every card script), and the parts of `Darklands.Simulation`, `Darklands.Persistence` and
`Darklands.Content` that carry original behaviour. The aim is that this code does what `DARKLAND.EXE` does,
including its oddities.

**What you need.** Reading skills for 16-bit x86 code and for C semantics (16-bit wrap-around, signed against
unsigned comparison, evaluation order of random draws), your own `DARKLAND.EXE`, Python for the tools, and patience.
This lane has few contributors on purpose; talk to the maintainers before you start (section 7).

**How faithfulness is checked, at a high level.** The checks run against the original program itself, never against
another reimplementation:

* *Traceability.* Every reconstructed operation carries a comment such as `// orig: R044+0x0123 (1838:0123) matched;
  tests: ...` that names the place in the original it reproduces. `docs/TRACEABILITY.md` explains the coordinates
  and the markers (`DORMANT`, `DIVERGENCE`, `UNCERTAIN`, `// C:`, `// BUG:`). A coordinate is a pointer, not a proof:
  only tests and oracles prove fidelity.
* *The oracle.* `tools/oracle` runs your original `DARKLAND.EXE` functions in an emulated processor and the C# on
  the same inputs, and compares the results; whole scripted game sessions and battles are replayed in both and
  compared after every step. Recorded corpora live in `tests/Darklands.Oracle.Tests/Corpus`. Start with
  `tools/oracle/README.md`.
* *The structure scan.* `tools/faithfulness/structure.py` compares the calls, constants, random draws and branches
  of each cited original function with the C# that implements it, from the executable's bytes.
* *Coverage.* `docs/faithfulness/BRANCHES.md` and `COVERAGE.md` say how much of the original has been exercised and
  list what has not.
* *The registers.* Every difference is written down, with a reason, in `docs/faithfulness`:

| Register | What it lists |
|---|---|
| `DIVERGENCES.md` / `divergences.csv` | places where the C# differs from the original on purpose (ids like `D-49817d`) |
| `ORIGINAL_BUGS.md` / `original_bugs.csv` | bugs of the original that the C# reproduces (ids like `B-126118`) |
| `UNCERTAIN.md` / `uncertain.csv` | readings of the original that are still guesses |
| `dormant.csv`, `unreachable.csv` | branches the original has that nothing reaches |

The registers are generated from the markers in the source (`python tools/faithfulness/registers.py`;
`--check` tells you whether the files on disk are out of date). If you add or change a marker, regenerate them and
commit the result. `docs/faithfulness/SUMMARY.md` is the published claim of where the project stands; it is kept
in step with the generated files.

**Where to start.** `docs/faithfulness/SUMMARY.md` (open items), `docs/faithfulness/UNCERTAIN.md` and `BRANCHES.md`
(what is not yet reached), `docs/TRACEABILITY.md` (how to cite), `docs/source-map/README.md` (the index of original
functions).

**How a change is checked.** A change must either leave every oracle and session replay agreeing with the original,
or be registered as a divergence or an original bug, with its reason, coordinates and a test. New behaviour is
proved with an oracle run or a direct comparison, not with "it looks right". Every branch of the original is
preserved, dormant ones too. Do not simplify silently: an omission is a `DIVERGENCE`, a guess is `UNCERTAIN`.

**What will not be accepted.**

* A "fix" or "improvement" of a rule, price, formula or quirk of the original in Classic play (section 5).
* A change that makes the oracles or session replays disagree without a registered reason.
* Behaviour with no citation of where in the original it comes from.
* References to other reverse-engineering projects (section 6).

### Portable lane

**Covers.** Running the game on more systems and devices: the SDL host (`src/Darklands.Host.Sdl`), the host
interfaces and settings (`src/Darklands.Platform`), game folder search (`GameFolderSearch.cs`,
`GameFolderSearch.Linux.cs`), packaging and publish profiles, the CI workflow `.github/workflows/build.yml`, and
input, audio and video hosts. Linux x64 (including the Steam Deck) is built today; other systems and controller
support are open ground.

**What you need.** .NET, SDL 2, and a machine of the kind you target. Skills in packaging or in an audio or video
API help.

**Where to start.** The README's technical section (build and publish), `docs/SETTINGS.md` (the settings and the
places the program looks), `src/Darklands.Platform/IHost.cs` (what a host must provide), and the headless host
`src/Darklands.Host.Headless` for scripted runs.

**How a change is checked.** It builds and the tests pass on the existing platforms in CI; you have run the game
on the new platform yourself and say what you tried. Host tests are in `tests/Darklands.Host.Sdl.Tests` and
`tests/Darklands.Platform.Tests`.

**What will not be accepted.** Anything that changes game logic, timing of the rules, or random draws; a host that
needs a different `Core`; a dependency from the game layers on a host or a platform library (the architecture test
fails it); files of the game copied into a package.

### Moddable lane

**Covers.** How mods and packs are described and loaded: card mods (`mod.json`, `docs/MODS.md`), picture and sound
packs (`docs/PACKS.md`, `src/Darklands.Rendering`, `src/Darklands.Audio`), the modded-save marking
(`docs/SAVES.md`), and future checking tools for mod authors.

**What you need.** C#, and an eye for data formats that stay readable for years. Reading a mod must never run code
from the mod's folder.

**Where to start.** `docs/MODS.md` and `docs/PACKS.md`, the examples `docs/mods/wayside-shrine` and
`docs/audio-pack-example`, the card engine in `src/Darklands.Cards`, and the card table
(`src/Darklands.Cards/CardTable.cs`).

**How a change is checked.** Existing mods and packs must still load and behave the same (the formats are
backward compatible: add, do not change the meaning of what exists). **With no mod loaded, Classic must be
untouched**: the oracle and session replays must still agree. A mod's saved games are marked modded in a companion
file and stay loadable by the original; keep that true. Add tests for the new field or rule, and update the guide.

**What will not be accepted.** Mods that execute code; a loader that changes anything when no mod is named; a mod
or pack format that cannot express the game's own data without changing it; shipping a mod or pack that contains
game files or text derived from them.

### Extendable lane

**Covers.** New, optional things the original never had: the enhanced mode, new screens (information such as a
journal, a quest log or tooltips), convenience features, new commands, new mechanics.

**What you need.** C# and the foundation above. Design sense for a game that must still feel like Darklands.

**The rules.**

* **Everything is behind a switch** that is off in Classic. Information features only read the game (a test must
  show that opening one changes nothing: not the state, not the random generator, not the clock). Convenience
  features may only issue player commands that a player could have issued, in a valid order, with no time skipped
  and no random draw added or left out. New mechanics go behind switches or mods, never into Classic, and games
  that use them are marked modded.
* **Classic stays exact.** The oracles and the session replay must agree as before when the switch is off.
* **The architecture test must pass.** Views read models and send commands: a screen never writes the game
  model, never reads the host except through `GameUi`, and never reaches into saved-game code. Put new rules in
  `Core` or `Simulation`, not in a screen.

**Where to start.** `docs/COMMANDS.md` (the screens already on the layer and the known gaps), `docs/SETTINGS.md`
(how a setting is defined and documented), `src/Darklands.Presentation/Commands`, and the developer console
(`src/Darklands.DevConsole`, opened with F11 in the game) for the shape of a small tool built on the game.

**How a change is checked.** Tests for the feature; a test that it is off by default and that Classic is
unchanged with it off; the architecture test; a short description of how to try it. Document the setting in
`docs/SETTINGS.md`.

**What will not be accepted.** A feature that is on by default; a shortcut that writes game state or skips time;
a new exception on the architecture test's lists; a feature that changes the original's saved-game bytes for a
Classic game.

### Playtesting and bug reports

You do not need to write code to help, and this lane matters for a project that claims fidelity.

**What you need.** Your own copy of the game and this program. If possible, DOSBox (or another way to run the
original) to compare.

**A good report contains:**

* the program version (`DarklandsRP --version`), your system, and your settings file if it is not the default;
* **a save file**: the `DKSAVEn.SAV` and its `DKSAVEn.BSV`, from as close before the problem as you can (saves are
  the original's own files, so the same save can be loaded in both programs);
* **the steps** from that save to the problem, as exact keys and clicks;
* what happened, and what you expected;
* **whether the original does the same.** Load the same save in the original under DOSBox and follow the same
  steps. If the original behaves differently, that is a fidelity bug and the most valuable kind of report. If
  it behaves the same, it may be an original bug, which Classic keeps on purpose (section 5), but we still want to
  know. State which you saw.
* a screenshot (F12 in the game saves one) when the problem is visual, and the console output of the program if
  it printed anything.

Because randomness is deterministic, a save plus steps usually lets a maintainer reproduce a problem exactly. Do
not attach game files other than your own saves.

**Where to start.** Open an issue (section 7). `docs/RC-CHECKLIST.md` is the maintainers'
own play-through check before a release.

### Content: packs and translations

**Covers.** Picture packs (PNG), sound packs (OGG, WAV, General MIDI), mods that add cards, and translations of the
user interface and texts.

**What you need.** Art, music or writing skills. No programming is needed for packs and mods, which are data.

**Where to start.**

* Packs: `docs/PACKS.md` (formats, names of all the game's music and sounds), with the example in
  `docs/audio-pack-example`.
* Mods: `docs/MODS.md`, with the example in `docs/mods/wayside-shrine`.
* The modding guides are being written under `docs/modding/`; use them when they are there.
* Translations: `docs/LOCALISATION.md`. Translations arrive with version 1.2; that guide says how to prepare. Until
  then, please discuss on Discord rather than sending finished translations.

**How a change is checked.** A pack changes how the game looks or sounds and never what it does. Your pack must
load without errors on the console and leave the game's decisions unchanged.

**What will not be accepted.** In this repository: the game's own pictures, music or text, or any copy or
conversion of them. Publish your own packs in your own place, with your own licence, and tell us about them.
Original artwork, music and writing that you made yourself are welcome as examples when you give them under the
project's licence.

## 4. How a change gets in

1. **Issue first for anything non-trivial.** Open an issue on GitHub saying what you want to change and which lane
   it is in, and wait for a reply before you spend days on it. A typo or a one-line obvious fix may go straight to
   a pull request.
2. **Branch.** Fork the repository or branch from `master`, one branch per change, with a short descriptive name.
3. **Small commits.** One idea per commit; the project builds and the tests pass at each one.
4. **Tests.** A behaviour change comes with a test. In the Faithful lane, with an oracle or comparison run too.
   Run `dotnet test Darklands.slnx` before you push, with `DARKLANDS_DIR` set if you have the game.
5. **Registers and docs.** If you touched a `DIVERGENCE`, `BUG`, `UNCERTAIN` or `DORMANT` marker, regenerate the
   registers (`python tools/faithfulness/registers.py`). If you changed a setting, the file formats or a command,
   update the matching document in `docs/`.
6. **Pull request.** Describe what changed and why, the lane, how you checked it, and whether you had the game
   files for the tests. CI (the `build` workflow: build and test on Windows and Linux) must pass.
7. **Review.** A maintainer reviews. Expect questions about faithfulness and about whether Classic is untouched.
   Keep the discussion on the change; push fixes as new commits.

### Commit messages

The log reads as a list of what the game does now. One line, starting with the area, then a colon, then what
changed, in plain words, no full stop. Say "as the original" when the point is that the behaviour now matches.
Real examples:

```
Battles: the seven-headed dragon's victory draws a frame, as the original
Card menu: Up / Down move the pointer onto the line they choose, as the original does
Sound packs: General MIDI (.mid) cues through an external SoundFont
Start: refuse a DARKLAND.EXE other than the English 483.07 with a message on the launcher
Travel map: the marker is clamped only on the axis and side it moves, on a pass that moves
Registers regenerated
```

Add a body when the reason is not obvious. Do not add credit lines for tools or other projects (section 6).

## 5. Faithfulness, in plain words

The original program has bugs: a wrong price formula, a check that is skipped, a card that misbehaves. Many players
know them, plan around them and some like them. They are part of Darklands. Classic mode exists so that anyone can
play the game as it was, and so that the project can be checked against the original at all: if Classic changes,
"it is the same game" can no longer be tested.

So in Classic mode:

* **A bug of the original is reproduced exactly.** A pull request that "fixes" a wrong formula, a missed check or
  a misbehaving card in the default game is not accepted, however clearly wrong the original is.
* **A real difference is a bug in our code**, and fixing it (making it match the original) is welcome.

What you can do about an original bug you want fixed:

1. **Register it.** The bug is marked in the code with a `// BUG:` comment at the place that reproduces it, with
   the original's coordinates; it appears in `docs/faithfulness/original_bugs.csv` under an id such as `B-126118`.
2. **Put the fix behind a switch.** The original's behaviour is a flag on `OriginalBehaviour`
   (`src/Darklands.Core/OriginalBehaviour.cs`); the flag's name starts with `Fix` (for example
   `FixCacheStoreLimit`). The faithful mode keeps the original behaviour, byte for byte. The fix itself is
   registered as a deliberate divergence (`divergences.csv`, an id such as `D-a25000`) that names the bug it
   fixes. The register's `fix_flag` column ties each original bug to the flag that fixes it. Add a test for both
   settings of the flag.
3. **Decide who gets the fix by default.** Only bugs that **damage the player's data** (saved games, the item
   stores in `CACHE.TMP`) are fixed in the player's default game, by listing the flag in
   `OriginalBehaviour.DataSafe`. Gameplay quirks stay as they are, with their fix flag off; the switches are not
   exposed in the settings file today. Talk to the maintainers before adding to `DataSafe`: it must be a data-damaging
   bug, and what the fixed game writes must still be readable by the original.

A fix of this kind is for the Faithful lane, because it needs the original's coordinates and a proof of what the
original does.

## 6. Rules for committed text, and the licence

This applies to code, comments, tests, documents, commit messages and issues.

* **Cite the original only by its own coordinates**, such as `R049+0x2E2C`, `root 0000:0CAA`, `thunk 09C0:190F`,
  `R136+0xD090` or `DS:E48A`, and describe what the code does **in your own words**. `docs/TRACEABILITY.md` lists
  the forms. The coordinates are places in `DARKLAND.EXE` that anyone can check against the game's bytes.
* **Do not copy names, comments or text from other reverse-engineering or decompilation projects,** and do not
  mention, name or credit such projects, their authors, files, function names or revisions. If you learned
  something from such a source, re-establish it from the original's own bytes and write it as your own finding with
  its coordinate. A test (`NoThirdPartyNamesTests`) checks the tracked files against a list kept by the maintainers;
  a pull request that trips it is sent back.
* **No game content**: no text, tables or pictures copied from the game's files (section 1).
* Names in the C# are readable ordinary descriptions. Mark guesses `UNCERTAIN`, not as facts.
* Write plainly: short sentences, no marketing.

**Licence.** The project is licensed under GPL-3.0-or-later (see `LICENSE`). By contributing you agree that your
contribution is licensed under the same terms. *Darklands* itself belongs to its owners and is not covered by that
licence; third-party components keep theirs (`THIRD_PARTY_NOTICES.md`). Do not contribute anything you do not have
the right to give under the GPL.

## 7. Getting help

* **Discord:** https://discord.gg/HjzWvmHhqZ for questions, ideas and discussion before an issue.
* **GitHub issues:** https://github.com/Arkana-Mechanika-Studios/darklands-restoration for bug reports, proposals
  and work in progress.

When you are unsure which lane something belongs to, or whether a behaviour is a bug or the original's way, ask
before you change it.
