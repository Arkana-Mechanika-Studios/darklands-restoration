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
    // UNCERTAIN: one tick after the last cel of OPENING2 (the capture's tick after it came in the same frame, before the tune).
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
