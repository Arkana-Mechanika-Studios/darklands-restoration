namespace Darklands.Core.Time;

/// <summary>
/// A point on the game calendar: hour 0–23, day of month, month 0–11, year. The calendar has
/// fixed 365-day years. As in the original, a day only rolls over when it exceeds the month
/// length, so day == days-in-month is a valid date. Fields are 16-bit like the original.
/// </summary>
// orig: date_copy R029+0x579A (1367:088A) matched: hour, day, month, year copied;
//   copying a date is the assignment of this value type.
public readonly record struct GameDate(short Hour, short Day, short Month, short Year)
{
    public const int HoursPerDay = 24;
    public const int MonthsPerYear = 12;
    public const int DaysPerYear = 365;

    private static readonly short[] DaysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    // 1400 * 365 + 0x33E8 == 8 * 65536, so day numbers are counted from 1 January 1400.
    private const int DayNumberBias = 0x33E8;

    public static int DaysInMonth(int month) => DaysPerMonth[Mod12(month)];

    /// <summary>A date normalised the way <c>date_set</c> does: hours, then days, then months.</summary>
    // orig: date_set R029+0x57BE (1367:08AE) matched; tests: CoreValuesOracleTests (Oracle:core.json date_set)
    // C: hours and months roll with (unsigned)h / 24 and (unsigned)m / 12, once; days with the month loop.
    public static GameDate Create(int year, int month, int day, int hour)
    {
        var d = (short)day;
        var m = (short)month;
        var y = (short)year;
        var h = (short)hour;
        if (h > 23)
        {
            var days = (ushort)h / HoursPerDay;
            d = (short)(d + days);
            h = (short)(h - days * HoursPerDay);
        }
        (d, m) = RollDays(d, m);
        (m, y) = RollMonthsOnce(m, y);
        return new GameDate(h, d, m, y);
    }

    /// <summary>The date moved forward by <paramref name="hours"/>, normalised (<c>date_add_hours</c>).</summary>
    // orig: date_add_hours R029+0x58FA (1367:09EA) matched; tests: CoreValuesOracleTests (Oracle:core.json date_add_hours)
    // C: a negative hour count leaves a negative hour (no borrow), as in the original.
    public GameDate AddHours(int hours)
    {
        var h = (short)(Hour + hours);
        var d = Day;
        while (h > 23)
        {
            d++;
            h = (short)(h - HoursPerDay);
        }
        var (day, month) = RollDays(d, Month);
        var (m, y) = RollMonthsOnce(month, Year);
        return new GameDate(h, day, m, y);
    }

    /// <summary>Day of the year (sum of the previous months plus the day).</summary>
    // orig: yday_now R029+0x58DC (1367:09CC) matched
    public int DayOfYear
    {
        get
        {
            var sum = 0;
            for (var i = 0; i < Month; i++)
            {
                sum += DaysPerMonth[i];
            }
            return sum + Day;
        }
    }

    /// <summary>Absolute day number (16-bit, as in the original).</summary>
    // orig: day_number_now R029+0x597C (1367:0A6C) matched
    // C: year * 365 + 0x33E8 wraps in 16 bits; the month loop index is unsigned.
    public short DayNumber
    {
        get
        {
            var n = (ushort)(Year * DaysPerYear + DayNumberBias);
            for (ushort i = 0; i < (ushort)Month; i++)
            {
                n = (ushort)(n + DaysPerMonth[i]);
            }
            return (short)(n + Day);
        }
    }

    /// <summary>Days from this date to <paramref name="later"/>, 0 if earlier.</summary>
    // orig: days_until R029+0x5A2C (1367:0B1C) matched; days_since R029+0x59A6 (1367:0A96) matched; tests: EventLogTests
    // C: then - now in a 16-bit int, 0 when negative (days_since is the same test the other way round).
    public int DaysUntil(GameDate later)
    {
        var difference = (short)(later.DayNumber - DayNumber);
        return difference < 0 ? 0 : difference;
    }

    /// <summary>Days from <paramref name="earlier"/> to this date, limited to 0..32000 (<c>days_between</c>).</summary>
    // orig: days_between R029+0x5AB4 (1367:0BA4) matched; tests: CoreValuesOracleTests (Oracle:core.json days_between)
    // C: unsigned day numbers; the sign test is (int)(tb - ta) and the 32000 limit a long difference.
    public int DaysSince(GameDate earlier)
    {
        var to = (ushort)DayNumber;
        var from = (ushort)earlier.DayNumber;
        if ((short)(to - from) < 0)
        {
            return 0;
        }
        return (long)to - from > 32000 ? 32000 : (ushort)(to - from);
    }

    public bool IsDaylight => TimeOfDay.IsDaylight(Hour);

    // orig: date_set R029+0x57BE (1367:08AE) matched; date_add_hours R029+0x58FA (1367:09EA) matched
    // C: a day equal to the month length stays in the month (`dm < day`).
    internal static (short Day, short Month) RollDays(short day, short month)
    {
        while (DaysPerMonth[Mod12(month)] < day)
        {
            day = (short)(day - DaysPerMonth[Mod12(month)]);
            month++;
        }
        return (day, month);
    }

    private static (short Month, short Year) RollMonthsOnce(short month, short year)
    {
        if (month > 11)
        {
            var years = (ushort)month / MonthsPerYear;
            year = (short)(year + years);
            month = (short)(month - years * MonthsPerYear);
        }
        return (month, year);
    }

    public static int Mod12(int month) => month % MonthsPerYear;
}
