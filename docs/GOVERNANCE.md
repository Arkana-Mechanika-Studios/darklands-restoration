# How the project is run

This page holds the principles every change follows, the classes of change and the evidence each needs, and how ideas
become decisions. What is planned is in `docs/ROADMAP.md`; the practical side of contributing is in `CONTRIBUTING.md`.

## 1. Principles

1. **Classic stays the reference.** Tag v1.0 is the frozen faithful game. The Classic rule set is checked against the
   original program in every release. No change may make Classic less faithful; a change that would is a regression,
   whatever it adds.
2. **One codebase, switches and presets.** No long-lived forks. Every departure from the original sits behind a switch
   with an id, and the original path stays beside it. Switches act on three independent axes: **rules** (what the game
   decides), **presentation** (what the player sees and hears) and **interface** (how the player gives the same orders).
   Presets combine them: Classic, Fixed, Restored, Custom (`docs/ROADMAP.md`). The save records the rule set it was
   played with.
3. **Every change has a class, and the class decides its evidence and its switch** (section 2).
4. **Two kinds of fix are on in every preset:** bugs of the original that damage the player's data (lost items, broken
   saves, load crashes) and plain display glitches. Each is still reproduced exactly behind a switch, for the checks
   against the original, and documented.
5. **Saved games stay compatible both ways** where the rule set allows: a Classic or Fixed save loads in the original
   game. New things are saved through stand-ins the original understands.
6. **Data, not code, for mods.** Mods and packs are data only; code changes go through the repository and review.
7. **Neutral sources and clean rights.** Committed text cites the original program by its coordinates and describes it in
   our own words. The original game's files are never redistributed; tools read the player's own copy.
8. **Evidence over opinion.** Questions about what the original does are settled by running it; questions about what the
   designers meant are settled by sources, recorded with their level, not by votes.

## 2. Classes of change

| Class | Example | Where it lives | Evidence required |
|---|---|---|---|
| Faithfulness fix | the C# differs from the original program | Classic itself | the checks against the original agree afterwards |
| Data-safety fix | a save writer that skips slots | on in every preset | exact reproduction behind its switch, and a test |
| Display fix | stray boxes in a hint | on in every preset | the original's look documented, with a picture |
| Gameplay bug fix | an exploit, a wrong formula | rules: Fixed | a register id, the original path kept, tests of both |
| Restoration | unused content, the designers' intent | rules: Restored | sources at a stated level (section 3), and a test |
| Interface comfort | key rebinding, sorting a list | interface | it breaks down into orders the original accepts |
| New capability | a new screen that changes the game | rules: extended | a design note (section 4) and tests; never in Classic |
| Presentation | sharper pictures, new sounds | presentation | shows nothing the original hides; clicks land where they would in the original |
| Content mod | quests, items, art packs | the mod system | the mod checker passes |

## 3. Evidence for restorations

A restoration names its sources, at one of these levels (stronger first):

1. present in the program or its data and reachable, but unused (dormant code, unused records, unused pictures or texts);
2. present but incomplete (half-wired features, placeholder texts);
3. described in the manual, the hint book or the original documentation;
4. stated by a designer (posts, interviews, the mailing-list archive);
5. design documents and code held in archives;
6. historical research, where the game aims at history.

Research informs; it does not by itself change rules. A restoration ships as a switched rules change or, where it is
data, as a first-party mod. "Is this a bug or what the designers meant?" is decided with this evidence; quirks players
love stay optional.

## 4. From an idea to a decision

1. **An issue**, tagged with its track and its class. Small, obvious changes may go straight to a pull request.
2. **A design note** for anything that adds a switch, changes the saved game, adds a screen or touches the game's
   rules: a short text (the problem, the class, the evidence, the switch, what it does to saves, the tests). It is
   discussed in the issue or on Discord, then decided by a maintainer.
3. **The decision log:** decisions are recorded with who took them, when and why, so settled questions are not reopened
   without new evidence.
4. **Review:** a maintainer reviews every pull request against section 2. Changes to the game's rules need the checks
   against the original; Classic must stay as it was; the neutrality check must find nothing.

## 5. Registers

The registers in `docs/faithfulness` are the public record: differences from the original, bugs of the original,
unused code, unreachable code, readings still uncertain. Two more come with the work after 1.0: the **switch register**
(every switch, its class, its presets and its default) and the **restoration register** (every candidate, its evidence
level and its sources).

## 6. Roles

- **Maintainers** merge, run the checks against the original (they need the original program, so they cannot run in
  public CI), and make releases.
- **Track leads** guide one track: its issues, its design notes, its first review. Until more people join, the
  maintainer leads every track.
- **Contributors** send changes; **testers** play the builds and report.

Contributors are credited in the release notes and in a contributors file.

## 7. Releases

Patch releases (1.x.y) when fixes are ready. Minor releases (1.x) have a theme; what is not finished waits behind its
switch for the next one, so the main branch stays releasable. Each release lists the switches it adds and passes a
release check against the original.
