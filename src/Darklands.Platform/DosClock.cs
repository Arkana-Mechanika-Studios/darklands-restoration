namespace Darklands.Platform;

/// <summary>
/// The C library's clock() as the original reads it: milliseconds from DOS's time of day, which DOS derives from the BIOS tick count
/// (18.2 ticks a second), so the value moves only at a BIOS tick, by 50 or 60.
/// </summary>
// orig: root 06A1:2682 (clock: ftime root 06A1:22A6, `(time - start) * 1000 + millitm - start millitm`), ftime's millitm from INT 21h
//   function 2Ch (hundredths of a second, computed by DOS from the tick count 0040:006C); the game's timer interrupt (root 047C:01EA)
//   passes a tick on to the BIOS every 65536 timer counts (its sum DS:07DC/07DE), so the BIOS count keeps its 18.2 Hz
public static class DosClock
{
    /// <summary>The BIOS ticks in <paramref name="milliseconds"/> of real time (1193182 / 65536 a second).</summary>
    public static long BiosTicks(long milliseconds) => milliseconds * 1_193_182 / 65_536_000;

    /// <summary>
    /// The BIOS tick count's low word (0040:006C) at <paramref name="timeOfDay"/>: the count runs from midnight (the BIOS sets it from the
    /// real-time clock at start-up and wraps it at 0x1800B0 a day), 1193182 / 65536 a second.
    /// </summary>
    // orig: R037+0x9DB2 cases 0x2E43 / 0x2E63 (C) and 0x1051 / 0x1071 (Q): party-random-seed (DS:9C4A) = the word at 0040:006C, read when the key is taken
    public static ushort TickWordOfDay(TimeSpan timeOfDay) => (ushort)BiosTicks((long)timeOfDay.TotalMilliseconds);

    /// <summary>clock() after <paramref name="milliseconds"/> of real time: ten times DOS's hundredths of a second at the last BIOS tick.</summary>
    public static long Milliseconds(long milliseconds) => BiosTicks(milliseconds) * 6_553_600 / 1_193_180 * 10;

    /// <summary>The real time at which the BIOS tick after the one of <paramref name="milliseconds"/> comes.</summary>
    public static long NextTickAt(long milliseconds) => ((BiosTicks(milliseconds) + 1) * 65_536_000 + 1_193_181) / 1_193_182;
}
