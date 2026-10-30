namespace Darklands.Core.Economy;

/// <summary>
/// An amount of money in florins, groschen and pfennigs (1 florin = 20 groschen,
/// 1 groschen = 12 pfennigs). Components are 16-bit like the original purse, and every
/// operation follows the original carry and borrow rules, including their wrap-around.
/// </summary>
// orig: purse-duplicate R029+0x50DE (1367:01CE) matched: d->pf = s->pf, d->gr = s->gr, d->fl = s->fl;
//   copying a purse is the assignment of this value type (no normalisation, as in the original).
public readonly record struct Money(short Florins, short Groschen, short Pfennigs) : IComparable<Money>
{
    public const int GroschenPerFlorin = 20;
    public const int PfennigsPerGroschen = 12;

    public static Money Zero => default;

    /// <summary>Builds a normalised amount, carrying pfennigs and groschen upwards.</summary>
    // orig: purse-assign R029+0x4FB4 (1367:00A4) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x4FB4); CoreValueRulesTests
    // C: gr and pf are unsigned parameters (a negative amount carries as 65536 - x); fl += gr / 20 stays signed.
    public static Money From(int florins, int groschen, int pfennigs)
    {
        // Groschen and pfennigs are treated as unsigned, as in the original.
        var pf = (ushort)pfennigs;
        var gr = (ushort)groschen;
        var fl = (short)florins;
        if (pf >= PfennigsPerGroschen)
        {
            gr = (ushort)(gr + pf / PfennigsPerGroschen);
            pf = (ushort)(pf % PfennigsPerGroschen);
        }
        if (gr >= GroschenPerFlorin)
        {
            fl = (short)(fl + gr / GroschenPerFlorin);
            gr = (ushort)(gr % GroschenPerFlorin);
        }
        return new Money(fl, (short)gr, (short)pf);
    }

    public static Money FromPfennigs(int pfennigs) => From(0, 0, pfennigs);

    /// <summary>Total value in pfennigs.</summary>
    // orig: purse-in-pfennigs R029+0x5002 (1367:00F2) matched; tests: CoreValueRulesTests
    public long TotalPfennigs => ((long)Florins * GroschenPerFlorin + Groschen) * PfennigsPerGroschen + Pfennigs;

    // orig: purse-add-parts R029+0x5040 (1367:0130) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x5040)
    // C: pf / 12 is a signed division, the groschen carry divides (unsigned)gr / 20.
    public Money Add(int florins, int groschen, int pfennigs)
    {
        var pf = (short)(Pfennigs + pfennigs);
        var gr = Groschen;
        var fl = Florins;
        if (pf >= PfennigsPerGroschen)
        {
            gr = (short)(gr + pf / PfennigsPerGroschen);
            pf = (short)(pf % PfennigsPerGroschen);
        }
        gr = (short)(gr + groschen);
        if (gr >= GroschenPerFlorin)
        {
            fl = (short)(fl + (ushort)gr / GroschenPerFlorin);
            gr = (short)(gr % GroschenPerFlorin);
        }
        return new Money((short)(fl + florins), gr, pf);
    }

    // orig: purse-sum R029+0x50FC (1367:01EC) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x50FC)
    // C: unlike purse-add-parts both carries divide signed (a->gr / 20).
    public Money Add(Money other)
    {
        var pf = (short)(Pfennigs + other.Pfennigs);
        var gr = Groschen;
        var fl = Florins;
        if (pf >= PfennigsPerGroschen)
        {
            gr = (short)(gr + pf / PfennigsPerGroschen);
            pf = (short)(pf % PfennigsPerGroschen);
        }
        gr = (short)(gr + other.Groschen);
        if (gr >= GroschenPerFlorin)
        {
            fl = (short)(fl + gr / GroschenPerFlorin);
            gr = (short)(gr % GroschenPerFlorin);
        }
        return new Money((short)(fl + other.Florins), gr, pf);
    }

    /// <summary>Subtracts, borrowing from the next larger unit; the florins may go negative.</summary>
    // orig: purse-subtract-parts R029+0x5090 (1367:0180) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x5090)
    // C: the borrow q = (unsigned)(11 - pf) / 12 is unsigned; the stores wrap at 16 bits.
    public Money Subtract(int florins, int groschen, int pfennigs)
    {
        var pf = (short)(Pfennigs - pfennigs);
        var gr = Groschen;
        var fl = Florins;
        if (pf < 0)
        {
            var borrow = (ushort)(11 - pf) / PfennigsPerGroschen;
            gr = (short)(gr - borrow);
            pf = (short)(pf + borrow * PfennigsPerGroschen);
        }
        gr = (short)(gr - groschen);
        if (gr < 0)
        {
            var borrow = (ushort)(19 - gr) / GroschenPerFlorin;
            fl = (short)(fl - borrow);
            gr = (short)(gr + borrow * GroschenPerFlorin);
        }
        return new Money((short)(fl - florins), gr, pf);
    }

    // orig: purse-difference R029+0x514E (1367:023E) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x514E)
    public Money Subtract(Money other) => Subtract(other.Florins, other.Groschen, other.Pfennigs);

    /// <summary>The amount added to itself <paramref name="count"/> times (0 for count &lt; 1).</summary>
    // orig: purse-scale R029+0x51A0 (1367:0290) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x51A0)
    public Money Times(int count)
    {
        var total = Zero;
        for (var i = 1; i <= count; i++)
        {
            total = total.Add(this);
        }
        return total;
    }

    /// <summary>Compares the pfennig totals as unsigned 32-bit values, like the original.</summary>
    // orig: purse-compare R029+0x51E4 (1367:02D4) matched; tests: CoreValuesOracleTests (Oracle:core.json R029+0x51E4)
    // C: both totals are long products stored in unsigned long: a negative purse compares as a huge one.
    public int CompareTo(Money other)
    {
        var a = unchecked((uint)TotalPfennigs);
        var b = unchecked((uint)other.TotalPfennigs);
        return a > b ? 1 : a < b ? -1 : 0;
    }

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    /// <summary>"3 florins, 2 groschen and 1 pfennig".</summary>
    // orig: purse-to-text R029+0x5286 (1367:0376) matched; number-with-separators root 0265:09B2 (0265:09B2) matched; tests: CoreValueRulesTests
    // C: in `if (fl == 1) if (m->pf) ", " else " and "` the else belongs to the inner if: after florins ", " when pfennigs follow, else " and ".
    // C: number-with-separators (root 0265:09B2, assembly only) groups thousands with ',': the asm writes 0x2C before the third digit from the right; agrees with the original in the oracles (9 of 10 blocks hit).
    public override string ToString()
    {
        var text = new System.Text.StringBuilder();
        var hasFlorins = false;
        var hasGroschen = false;
        if (Florins > 0)
        {
            text.Append(Florins == 1 ? "1 florin" : Commas(Florins) + " florins");
            hasFlorins = true;
        }
        if (Groschen > 0)
        {
            if (hasFlorins)
            {
                text.Append(Pfennigs != 0 ? ", " : " and ");
            }
            text.Append(Commas(Groschen) + " groschen");
            hasGroschen = true;
        }
        if (Pfennigs > 0)
        {
            if (hasFlorins || hasGroschen)
            {
                text.Append(" and ");
            }
            text.Append(Pfennigs == 1 ? "1 pfennig" : Commas(Pfennigs) + " pfennigs");
        }
        return text.ToString();
    }

    private static string Commas(short value) =>
        value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
