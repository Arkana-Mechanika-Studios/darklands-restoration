namespace Darklands.Core.Time;

/// <summary>
/// Moves the game clock. Reports every month boundary crossed so the simulation can run the
/// monthly update with the same month number the original passes (which may be 12 before the
/// year rolls over).
/// </summary>
public static class Calendar
{
    /// <summary>The clock advanced by <paramref name="hours"/> (<c>pass-hours</c>, clock part).</summary>
    // orig: pass-hours R029+0x54D8 (1367:05C8) matched; tests: CoreValueRulesTests
    // C: the clock part of pass-hours (TimePassage.Advance runs the rest): the month loop calls the monthly update with the incremented month-now.
    public static GameDate AdvanceHours(GameDate now, int hours, Action<int>? monthStarted = null) =>
        AdvanceHours(now, hours, monthStarted is null ? null : (_, month) => monthStarted(month));

    /// <summary>As above, the callback also gets the clock as the globals hold it at that moment (hour, day and month already moved).</summary>
    public static GameDate AdvanceHours(GameDate now, int hours, Action<GameDate, int>? monthStarted)
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

    /// <summary>The clock advanced by <paramref name="days"/> (<c>pass-days</c>, clock part).</summary>
    // orig: pass-days R029+0x5590 (1367:0680) matched; tests: CoreValueRulesTests
    public static GameDate AdvanceDays(GameDate now, int days, Action<int>? monthStarted = null) =>
        AdvanceDays(now, days, monthStarted is null ? null : (_, month) => monthStarted(month));

    /// <summary>As above, the callback also gets the clock as the globals hold it at that moment.</summary>
    public static GameDate AdvanceDays(GameDate now, int days, Action<GameDate, int>? monthStarted) =>
        Finish(now with { Day = (short)(now.Day + days) }, monthStarted);

    // orig: pass-hours R029+0x54D8 (1367:05C8) matched; pass-days R029+0x5590 (1367:0680) matched
    // C: month-length-table[month-now % 12] with a signed %; the years roll one at a time after all the months.
    // orig: day-now and month-now are the globals themselves: the monthly update runs with day-now already reduced and month-now incremented (the year not yet rolled);
    //   the events it creates are dated by that clock (lockstep camp-inn-broke: a dragon quest on 1 June, 05:00, not 30 May)
    private static GameDate Finish(GameDate date, Action<GameDate, int>? monthStarted)
    {
        var day = date.Day;
        var month = date.Month;
        while (GameDate.DaysInMonth(month) < day)
        {
            day = (short)(day - GameDate.DaysInMonth(month));
            month++;
            monthStarted?.Invoke(new GameDate(date.Hour, day, month, date.Year), month);
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
