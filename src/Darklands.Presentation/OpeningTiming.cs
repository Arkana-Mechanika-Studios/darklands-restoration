using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>
/// When the original's opening does what, on its game timer (60 Hz ticks: <see cref="IGameTimer"/>).
/// The PAN player shows a cel every 60 / rate ticks (the files' rate is 10: six ticks); between two
/// files it reads the next one before its first cel, which took the original the measured number of
/// ticks; the next tune is started right after the last cel of a file. The ticks themselves came
/// irregularly while the files were read (<see cref="Track"/>), and the music, the samples and the cels
/// all followed them. Measured: the shared-clock capture of the original (DOSBox-X at 3000 cycles/ms,
/// the Roland driver with a Sound Blaster), see tools/intro/opening_timer.py; with this timing the
/// emulated drivers reproduce its 13 364 MPU bytes and the cels fall on its 1 171 frame times.
/// </summary>
// orig: root 03A1:000A matched - the tune 2Fh + n (R043+0x09AE) then the PAN player root 030B:0004 for each file;
//   root 030B:0508 (the tick: the next cel when DS:83B8 has counted DS:83BA = 60 / rate ticks; nothing while DS:A73A is set, i.e. until
//   the reader has filled the ring buffer: the measured FirstCel) and root 030B:00FE (back to the caller once every cel was shown).
public static class OpeningTiming
{
    /// <summary>Per file: ticks from the last cel of the previous file to its tune (the first: 0), and from the tune to its first cel.</summary>
    // DIVERGENCE: host timing: whether the tick after a file's last cel comes before its next tune (1 here) or after it (0) is
    //   the original's CPU time against the timer, so it varies with the machine; the values are the measured capture's (OPENING3 1, the others 0).
    //   Decided from the code (2026-11-06): the cel is shown inside the timer interrupt (root 047C:01EA -> 047C:000A -> 030B:0508 -> cel
    //   030B:05CA), which waits for the vertical retrace after the decode (R043+0x3B71, 3DA bit 3) and sends its EOI only at its end
    //   (047C:0236): a timer interrupt due meanwhile waits and runs, with its 60 Hz tick, before the main code's next instruction. The timer is
    //   locked to the retrace (047C:0257: the divisor is the frame measured over ten retraces, rounded down; the PIT is restarted at a retrace
    //   after 70 interrupts that land in the displayed lines), so on a VGA the interrupt comes at most the vertical blanking before the
    //   retrace (0 to about 0.4 ms) ahead of it. If the cel's work before its retrace wait ends within that lead, the cel ends at that retrace
    //   and the main code has nearly a frame for the ~1,100 instructions to the tune (030B:00FE's end, the file closed, 03A1's tune call):
    //   tune first. Otherwise it ends at the next retrace, after the next interrupt: tick first. At 3,000 instructions per ms that work took
    //   more than 1.6 ms for every cel (the second capture: each cel ends 1.60 to 2.08 ms after an interrupt, at the frame's retrace, one or
    //   two frames on): so the tick comes first on the machines of the game's time (a 386, a 486 at least up to four times that speed), the
    //   tune first only on a faster one; the first capture (host clock, a fast emulated CPU) is such a machine, the second (fixed 3,000
    //   cycles, all five boundaries tick first) the slow one, whose emulator also held its interrupt 1.6 to 2.1 ms ahead of the retrace with no
    //   restart. Neither is the program's own order: the values stay the first capture's, the only complete one (13 364 MPU bytes, 1 171 cels,
    //   32 samples; the second ends at OPENING7), whose file reads also set FirstCelAfterTune; matching a slow machine would take that
    //   machine's file reads as well.
    public static readonly IReadOnlyList<(int TuneAfterLastCel, int FirstCelAfterTune)> Files =
    [
        (0, 32), (1, 43), (0, 42), (0, 43), (0, 40), (0, 40), (0, 33),
    ];

    /// <summary>The ticks per frame of the original's timer from the opening's first tune to its last cel.</summary>
    public static TimerTrack Track => _track ??= Load();

    private static TimerTrack? _track;

    private static TimerTrack Load()
    {
        using var stream = typeof(OpeningTiming).Assembly.GetManifestResourceStream("Darklands.Presentation.Resources.OpeningTimer.txt")
            ?? throw new InvalidOperationException("the opening's timer track is missing from the assembly");
        using var reader = new StreamReader(stream);
        var digits = string.Concat(reader.ReadToEnd().Split('\n').Where(line => !line.StartsWith('#')));
        return TimerTrack.FromDigits("opening", digits);
    }
}
