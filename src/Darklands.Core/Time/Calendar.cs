namespace Darklands.Core.Time;

/// <summary>
/// Moves the game clock. Reports every month boundary crossed so the simulation can run the
/// monthly update with the same month number the original passes (which may be 12 before the
/// year rolls over).
/// </summary>
public static class Calendar
{
    /// <summary>The clock advanced by <paramref name="hours"/> (<c>advance_hours</c>, clock part).</summary>
    // orig: advance_hours R029+0x54D8 (1367:05C8) matched; tests: CoreValueRulesTests
    // C: the clock part of advance_hours (TimePassage.Advance runs the rest): the month loop calls the monthly update with the incremented cur_month.
    public static GameDate AdvanceHours(GameDate now, int hours, Action<int>? monthStarted = null)
    {
        var hour = (short)(now.Hour + hours);
        var day = now.Day;
        while (hour > 23)
        {
            day++;
            hour = (short)(hour - GameDate.HoursPerDay);
        }
        return Finish(new GameDate(hour, day, now.Month, now.Year), monthStarted);
    }

    /// <summary>The clock advanced by <paramref name="days"/> (<c>advance_days</c>, clock part).</summary>
    // orig: advance_days R029+0x5590 (1367:0680) matched; tests: CoreValueRulesTests
    public static GameDate AdvanceDays(GameDate now, int days, Action<int>? monthStarted = null) =>
        Finish(now with { Day = (short)(now.Day + days) }, monthStarted);

    // orig: advance_hours R029+0x54D8 (1367:05C8) matched; advance_days R029+0x5590 (1367:0680) matched
    // C: days_per_month[cur_month % 12] with a signed %; the years roll one at a time after all the months.
    private static GameDate Finish(GameDate date, Action<int>? monthStarted)
    {
        var day = date.Day;
        var month = date.Month;
        while (GameDate.DaysInMonth(month) < day)
        {
            day = (short)(day - GameDate.DaysInMonth(month));
            month++;
            monthStarted?.Invoke(month);
        }
        var year = date.Year;
        while (month > 11)
        {
            year++;
            month = (short)(month - GameDate.MonthsPerYear);
        }
        return new GameDate(date.Hour, day, month, year);
    }
}
