using Darklands.Content.Formats.Graphics;
using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// The full-screen animations (.PAN files): the opening, the party's death and the ending.
/// Each animation comes with a music cue; a key press skips the rest of the sequence, as in
/// the original (its player stops reading when a key is waiting, and the key stays waiting).
/// Everything is timed on the game timer (<see cref="IGameTimer"/>), the music's clock: the tune
/// is started at a tick, the cels follow it at the PAN player's rate, and the samples the sound
/// driver starts fall where they did in the original.
/// </summary>
// orig: the PAN player root 030B:0004 matched (root 030B:001A allocates the ring buffer, root 030B:0150 reads the file into it,
//   root 030B:00FE waits until every cel was shown); the timer routine root 030B:0508 shows a cel when its delay has run out and
//   root 030B:05CA decodes one ('Z' cel: rate and palette, 'B' cel: a compressed screen); callers: the opening in root 03A1:000A matched, the ending in the last card R133+0x186C
//   (1971:04DC) matched, the death animation in the battle branch of root 03A1:000A. tests: OpeningTimingTests
// DIVERGENCE: the original streams the file through a ring buffer while a timer interrupt shows the cels; here the decoded frames are
//   shown one by one at their ticks. A key press stops the READING in the original (kbhit() in the loop of 0150) while the cels already
//   read still play out, and the key stays in the buffer so the next animation's reading stops at once too; a skip here cuts the current
//   animation short immediately (the one difference) and the later ones follow the original: they are not shown, but their music cues
//   still play.
// DORMANT: the player's status line on the monochrome screen (root 030B:07C2: "time: cels: full: head: tail: ..." written to B000:0000
//   when DS:A748 is set; a debugging aid of the original developers).
// orig: the status line's text helpers root 030B:06AC (clear), root 030B:06B4 (put a character), root 030B:06CE (put a string),
// orig: root 030B:06FA (pad), root 030B:0728 (int), root 030B:075A (long), root 030B:078E (aligned number) - dormant with it.
public sealed class Cinematics(GameUi ui)
{
    /// <summary>False with the command line's Q: the death animation is not shown (its tune still plays), the ending is skipped.</summary>
    // orig: DS:0BF2 (set by Q in R000+0x0114): `if (!DS:0BF2)` around the opening and the death animation in root 03A1:000A and around
    //   the ending in R133+0x186C
    public bool Animations { get; init; } = true;

    /// <summary>Ticks from a tune to the first cel of its file where nothing was measured: the next tick (the file is read at once here).</summary>
    // DIVERGENCE: the original reads the whole file (or a full ring buffer) before the first cel; its time depends on the machine. The
    //   opening uses the measured times (OpeningTiming).
    private const int ReadTicks = 1;

    /// <summary>Requests the next tune this many ticks ahead of its tick at most (the sound's output latency is less).</summary>
    private const int CueLookahead = 12;

    // orig: R043+0x09AE (the tune: plays it if music is on, else stops the music) with 2Fh + root 030B:0004("opening2"), 30h "opening3",
    //   31h "opening4", 32h "openin6z", 33h "opening7", 34h "opening8", 35h "opening9" in root 03A1:000A
    // C: "openin6z" is the file name as written in the program (opening 5/6 and the z).
    private static readonly (int Cue, string? Animation)[] OpeningScenes =
    [
        (0x2F, "opening2"), (0x30, "opening3"), (0x31, "opening4"), (0x32, "openin6z"),
        (0x33, "opening7"), (0x34, "opening8"), (0x35, "opening9"),
    ];

    // orig: R133+0x186C (the last card): tune 2Dh, then 36h "fin0a", 37h "fin1a", 38h "fin2a", [the digital sample enddark2.dgt starts
    //   here], 39h "fin3a", 3Ah "fin4a", 3Bh "fin5a"; all inside `if (!DS:0BF2)` (the no-intro flag of the command line)
    private static readonly (int Cue, string? Animation)[] EndingScenes =
    [
        (0x36, "fin0a"), (0x37, "fin1a"), (0x38, "fin2a"), (0x39, "fin3a"), (0x3A, "fin4a"), (0x3B, "fin5a"),
    ];

    /// <summary>Music cue played with the death animation.</summary>
    // orig: the tune 12h then, unless DS:0BF2, root 030B:0004("death") and `if (kbhit()) getch();` in the "everybody is down" branch of
    //   root 03A1:000A (the cue plays even when the animations are switched off)
    private const int DeathCue = 0x12;

    /// <summary>The opening, before the start screen.</summary>
    // orig: the first `if (!DS:0BF2)` block of root 03A1:000A: [opendark.dgt sample for sound drivers 'a' and 'p'], the seven scenes, then
    //   `if (kbhit()) { tune 0; getch(); tune 7; }`: a key that stopped the reading is eaten and the music is set to tune 0 then tune 7
    //   (the start screen's)
    // C: the digitised sample opendark.dgt (R043+0x09C8 + the driver call root 047C:0783) is loaded by the audio itself with the first
    //   opening tune (SynthAudio); a missing file is checked by StartUp.CheckOpeningSamples.
    public void Opening() => PlayScenes(OpeningScenes, afterSkip: [0, 7], measured: true);

    /// <summary>The party has perished (after a battle).</summary>
    // orig: see DeathCue; the same in battle-driver R029+0x2278 (music-start(0x12), pointer-hide, then the film unless DS:0BF2)
    public void Death() => PlayScenes([(DeathCue, Animations ? "death" : null)]);

    /// <summary>The party has perished while time passed: the death tune, the card engine's stop, then the film.</summary>
    // orig: hours-elapse R029+0x255A (0E76:255A) draft (bytes match): pointer-hide, music-start(0x12), cards-teardown-overlay-entry (R043+0x054E ends with
    //   sound-start(1), root 047C:078A), then root 030B:0004("death") unless DS:0BF2; tests: MusicTests
    public void DeathAfterShutdown()
    {
        var pointer = ui.PointerVisible;
        ui.PointerVisible = false;
        ui.PlayMusic(DeathCue);
        ui.Audio.PlayMusic(Darklands.Audio.GameSounds.Stop);
        ui.PointerVisible = pointer;
        if (Animations)
        {
            PlayScenes([(NoCue, "death")]);
        }
    }

    /// <summary>A scene without a tune of its own.</summary>
    private const int NoCue = -1;

    /// <summary>
    /// A film a card plays (card 359's ending): each step's tune, then its animation if it has
    /// one. The speech samples (ENDDARK1/2.DGT) are started by the sound driver with their tunes.
    /// </summary>
    // orig: R133+0x186C (1971:04DC) matched - tune / root 030B:0004 pairs
    // orig: R133+0x186C: inside `if (!DS:0BF2)`, cards-teardown R043+0x054E first, which ends with sound-start(1) (root 047C:078A)
    // DIVERGENCE: cards-teardown's files (charactr.tmp, locs.tmp, events.tmp) and frees are not needed; only its sound-start(1) is played.
    public bool Film(IReadOnlyList<Cards.FilmStep> steps)
    {
        if (!Animations)
        {
            return true;
        }
        ui.Audio.PlayMusic(Darklands.Audio.GameSounds.Stop);
        PlayScenes(steps.Select(step => (step.Tune, step.Animation)));
        return true;
    }

    /// <summary>Baphomet is defeated.</summary>
    // orig: R133+0x186C (the last card); the card script that calls this is not part of this branch (it opens the ending through its own screens)
    public void Ending()
    {
        if (!Animations)
        {
            return;
        }
        ui.PlayMusic(0x2D);
        PlayScenes(EndingScenes);
    }

    private void PlayScenes(IEnumerable<(int Cue, string? Animation)> scenes, int[]? afterSkip = null, bool measured = false)
    {
        var timer = ui.Timer;
        var list = scenes.ToList();
        var pointer = ui.PointerVisible;
        ui.PointerVisible = false;
        var skipped = false;
        try
        {
            // the first tune starts as soon as the sound can carry it out; with the opening the timer follows the original's ticks from there
            var cueTick = timer.RequestTick;
            if (measured)
            {
                timer.Follow(OpeningTiming.Track, cueTick);
            }
            var requested = false;
            for (var n = 0; n < list.Count; n++)
            {
                var (cue, animation) = list[n];
                if (skipped)
                {
                    if (!requested && cue != NoCue)
                    {
                        ui.PlayMusic(cue);
                    }
                    requested = false;
                    continue;
                }
                if (!requested && cue != NoCue)
                {
                    ui.PlayMusicAt(cue, n == 0 ? cueTick : Math.Max(cueTick, timer.RequestTick));
                }
                ui.Trace?.Invoke($"cue {cueTick} {cue:X2}");
                var pan = animation is null ? null : Load(animation);
                requested = false;
                if (pan is null)
                {
                    // C: no file, nothing shown: the next tune follows at once
                    cueTick = Math.Max(cueTick, timer.RequestTick);
                    continue;
                }
                var first = cueTick + (measured ? OpeningTiming.Files[n].FirstCelAfterTune : ReadTicks);
                var last = first + (long)CelTicks(pan) * (pan.Frames.Count - 1);
                var next = n + 1 < list.Count ? list[n + 1] : ((int, string?)?)null;
                var nextTick = last + (measured && n + 1 < OpeningTiming.Files.Count ? OpeningTiming.Files[n + 1].TuneAfterLastCel : 0);
                if (!Play(animation!, pan, first, () =>
                    {
                        // the next tune goes to the sound ahead of time, for the tick right after this file's last cel
                        if (next is { } upcoming && !requested && timer.RequestTick >= nextTick - CueLookahead)
                        {
                            ui.PlayMusicAt(upcoming.Item1, Math.Max(nextTick, timer.RequestTick));
                            requested = true;
                        }
                    }))
                {
                    skipped = true;
                    timer.Follow(null, 0);
                    continue;
                }
                cueTick = nextTick;
            }
            if (skipped && afterSkip is not null)
            {
                foreach (var tune in afterSkip)
                {
                    ui.PlayMusic(tune);
                }
            }
        }
        finally
        {
            ui.PointerVisible = pointer;
            ui.Canvas.Clear(0);
            ui.Present();
        }
    }

    /// <summary>Ticks between two cels: the 'Z' cel's rate divided into 60.</summary>
    // orig: root 030B:05CA: the 'Z' cel sets DS:83BA = 60 / p[1] (p[1]: the rate byte after the 'Z'); root 030B:0508 counts DS:83B8 down
    //   by one per tick and shows the next cel at zero
    // C: an int division (rate 10: 6 ticks, 100 ms on the 60 Hz tick); the decoder reads the 'Z' and the rate as the header's first word.
    private static int CelTicks(Pan pan) => 60 / Math.Max(1, pan.Header.Magic >> 8);

    // orig: root 030B:0004(name): the name gets ".PAN" (the "." and the strcat in 0150), picture-file-open; a missing file makes the open fail and
    //   nothing is shown (`if (picture-file-open(buf))`)
    private Pan? Load(string animation)
    {
        var file = animation.ToUpperInvariant() + ".PAN";
        return ui.Assets.Files.Exists(file) ? new PanDecoder().Decode(animation, ui.Assets.Files.Read(file)) : null;
    }

    /// <summary>Shows one animation from tick <paramref name="first"/>, a cel every <see cref="CelTicks"/>; false when the player pressed a key to skip.</summary>
    // orig: root 030B:0508 / 05CA - the cels in file order at their rate; root 030B:00FE returns once the last one was shown
    private bool Play(string animation, Pan pan, long first, Action waiting)
    {
        var timer = ui.Timer;
        var step = CelTicks(pan);
        for (var j = 0; j < pan.Frames.Count; j++)
        {
            var tick = first + (long)step * j;
            while (timer.Ticks < tick)
            {
                waiting();
                if (ui.WaitForKey(1))
                {
                    return false;
                }
            }
            var frame = pan.Frames[j];
            ui.Canvas.UsePalette(frame.Palette);
            ui.Canvas.Draw(frame.Image, 0, 0);
            ui.Present();
            ui.Trace?.Invoke($"cel {tick} {animation} {j}");
        }
        waiting();
        return true;
    }
}
