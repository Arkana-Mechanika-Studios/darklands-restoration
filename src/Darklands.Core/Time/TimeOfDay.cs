namespace Darklands.Core.Time;

/// <summary>Hour-of-day rules: daylight and the canonical-hour bands used by texts and cards.</summary>
public static class TimeOfDay
{
    /// <summary>Daylight lasts from 05:00 to 18:59.</summary>
    // orig: daylight-test R029+0x563A (1367:072A) matched; tests: CoreValueRulesTests
    public static bool IsDaylight(int hour) => hour >= 5 && hour < 19;

    /// <summary>
    /// Band of an hour: 0–2 → 7 (Matins), 3–5 → 0, 6–8 → 1, 9–11 → 2, 12–14 → 3, 15–17 → 4,
    /// 18–20 → 5, 21–23 → 6.
    /// </summary>
    // orig: time-of-day-band R029+0x5654 (1367:0744) matched; time-band-now R029+0x56A6 (1367:0796) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x5654); CoreValueRulesTests
    public static int Band(int hour) => hour < 3 ? 7 : hour < 21 ? hour / 3 - 1 : 6;

    /// <summary>The same band counted from 1 (0–2 → 1 … 18–20 → 7, 21–23 → 0).</summary>
    // orig: time-band-now-first R029+0x56FE (1367:07EE) matched; tests: CoreValueRulesTests
    public static int BandFromOne(int hour) => (Band(hour) + 2) % 8;

    /// <summary>Hours from <paramref name="now"/> until <paramref name="target"/> (0..23).</summary>
    // orig: hours-remaining-until R029+0x577A (1367:086A) matched
    public static int HoursUntil(int now, int target) => now <= target ? target - now : target - now + 24;
}
