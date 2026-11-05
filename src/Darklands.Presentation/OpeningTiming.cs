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
    // DIVERGENCE: host timing: whether the tick after a file's last cel comes before its next tune (1 here) or after it (0) is the
    //   original's CPU time against the timer, so it varies with the machine; the values are the measured capture's (OPENING3 1, the others 0).
    //   After the last cel (root 030B:0565) the player's wait root 030B:00FE..0x012C runs until the reader has caught up, returns, and
    //   root 03A1 starts the next tune (thunk 09C0:18F1); the next 60 Hz tick (root 030B:0508) lands in that same stretch. A second
    //   shared-clock capture (DOSBox-X at fixed 3000 cycles/ms, exec traces at those coordinates and the timer interrupt, one emulated
    //   clock; 2026-11-05; two runs alike) had the tick first at each of the five boundaries it reached (OPENING2..OPENING7), 0.30 to
    //   0.39 ms before the tune's start (2% of a tick), where the first capture had the tune first at five of six; its cels lie up to
    //   57 ms off the first capture's. The order follows the CPU's speed against the timer, not the program.
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
