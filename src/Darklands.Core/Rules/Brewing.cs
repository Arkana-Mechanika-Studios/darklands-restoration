using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>A row of the alchemy screen: a formula the alchemist knows and how many batches of it to make.</summary>
public sealed class Batch(Formula formula)
{
    public Formula Formula { get; } = formula;

    public int Count { get; set; }
}

// orig: R033+0x0658 (1462:0658) draft (register-level diffs only); tests: CampAndSheetRulesTests
/// <summary>
/// The alchemist's bench (the alchemy screen's state): the formulas the member knows, the
/// party's ingredients still free, and brewing. Brewing records a <see cref="EventKinds.Brew"/>
/// event that ends when time next passes; its expiry handler makes the potions
/// (<see cref="EventExpiry.AlchemyResult"/>).
/// </summary>
public sealed class BrewingBench
{
    /// <summary>The ingredient items (0x48 Pure Gold … 0x5E Melancholic Base).</summary>
    public const int FirstIngredient = 0x48;
    public const int LastIngredient = 0x5E;

    private readonly GameState _s;
    private readonly int[] _stock;

    // orig: R033+0x0658: batch rows from formulas_known (3 bits per potion, in formula order), then R033+0x14E6, then the ingredient count
    /// <summary>
    /// Opens the bench: lists the member's formulas, takes back the member's unfinished brews
    /// (their batches are shown again and their ingredients return to the alchemist) and counts
    /// the ingredients the present members carry.
    /// </summary>
    public BrewingBench(GameState s, IReadOnlyList<Formula> formulas, int alchemist)
    {
        _s = s;
        Alchemist = alchemist;
        var known = s.Party.Slot(alchemist).Formulas;
        Batches = formulas.Where(f => known.Contains(f.Index)).OrderBy(f => f.Index).Select(f => new Batch(f)).ToList();
        TakeBackUnfinishedBrews();
        _stock = Brewing.IngredientsCarried(s);
    }

    public int Alchemist { get; }

    public IReadOnlyList<Batch> Batches { get; }

    /// <summary>How many of an ingredient are free (carried and not set aside for batches).</summary>
    public int Stock(int item) => item is >= 0 and < 256 ? _stock[item] : 0;

    // orig: R033+0x1334 (1462:1334) draft (bytes match)
    /// <summary>The party has the ingredients for one more batch.</summary>
    public bool HasIngredients(Formula f) => f.Ingredients.All(i => Stock(i.Item) >= i.Count);

    // orig: R033+0x125C (1462:125C) draft (bytes match)
    // C: the check stops at the first empty slot, the subtraction runs over all five (an empty slot subtracts 0 from item 0's count).
    /// <summary>Sets aside the ingredients of one batch; false (nothing set aside) when some are missing.</summary>
    public bool Reserve(Formula f)
    {
        if (!HasIngredients(f))
        {
            return false;
        }
        foreach (var i in f.Slots)
        {
            _stock[i.Item] -= i.Count;
        }
        return true;
    }

    // orig: R033+0x12EC (1462:12EC) draft (bytes match)
    /// <summary>Frees the ingredients of one batch again.</summary>
    public void Release(Formula f)
    {
        foreach (var i in f.Ingredients)
        {
            _stock[i.Item] += i.Count;
        }
    }

    // orig: the chance of R033+0x094A and R033+0x1390 (both draft, register-level diffs)
    /// <summary>The chance of success of a batch row (<see cref="Brewing.SuccessChance"/>).</summary>
    public int Chance(Batch b)
    {
        var m = _s.Party.Slot(Alchemist);
        return Brewing.SuccessChance(m[Attribute.Intelligence], m[Skill.Alchemy], b.Formula.MysticNumber, _s.Party.PhilosophersStone, b.Count);
    }

    // orig: R033+0x1390 (1462:1390) draft (register-level diffs); tests: CampAndSheetRulesTests
    // UNCERTAIN: the original calls event_renew with nine arguments; the tenth (the tag it matches on) is whatever lies on the caller's stack. 0 here.
    // C: event_renew extends a matching brew (same formula, place, owner) by 0 hours and keeps its old chance and batches.
    /// <summary>
    /// Brews the batches of a row: records the brew (ending at once, so the next passage of
    /// time completes it) and takes the ingredients from the party, the alchemist first.
    /// </summary>
    public void Brew(Batch b, int place)
    {
        var log = _s.Events;
        log.Renew(Alchemist, _s.CurrentLocation, b.Count, EventKinds.Brew, b.Formula.Index, Chance(b),
            (int)ExpiryHandler.AlchemyResult, 0, (int)EventFlags.CallExpiryHandler, 0);
        // C: event_renew's return is not used: the place and risk go to events[last_event]. With the log full (renew returns -1
        // C: without setting it) that is the last brew event its scan visited; last_event -1 or 300 (no brew event at all, or
        // C: the scan ran past slot 299) points at events[-1]/[300], outside the table (the tail of var_named[4] and the word after
        // C: the table: host memory, nothing written here).
        if (log[log.Last] is { } e)
        {
            e.Data1 = place;
            e.Data2 = b.Formula.Risk;
        }
        foreach (var i in b.Formula.Ingredients)
        {
            TakeFromParty(i.Item, i.Count * b.Count);
        }
    }

    // orig: R033+0x1630 (1462:1630) draft (bytes match)
    // C: members are visited first, 0, 1 ... first - 1, first + 1 ...; stack counts are unsigned bytes compared with the int still needed.
    /// <summary>
    /// Takes <paramref name="count"/> items of <paramref name="code"/>: the alchemist's packs
    /// first, then the others in order. Faithful quirk: after a stack is used up, the stack
    /// that moves into its place is skipped.
    /// </summary>
    private void TakeFromParty(int code, int count)
    {
        for (var k = 0; k < _s.Party.Members.Count; k++)
        {
            var m = k == 0 ? Alchemist : k > Alchemist ? k : k - 1;
            if (_s.Party.PresentMember(m) is not { } member)
            {
                continue;
            }
            for (var j = 0; j < member.Pack.Count; j++)
            {
                var st = member.Pack[j];
                if ((st.Code & 0xFFF) != code)
                {
                    continue;
                }
                if (st.Count > count)
                {
                    member.Pack[j] = st with { Count = st.Count - count };
                    return;
                }
                count -= st.Count;
                Inventory.RemoveStack(_s, m, code);
            }
        }
    }

    // orig: R033+0x14E6 (1462:14E6) draft (bytes match)
    // C: the returned count is count x batches stored in an unsigned char (wraps above 255).
    /// <summary>The alchemist's brews not yet done: shown as batches again, ingredients back (quality 25), brew removed.</summary>
    public void TakeBackUnfinishedBrews()
    {
        var log = _s.Events;
        for (var i = 0; i < EventLog.Capacity; i++)
        {
            i = log.Next(EventKinds.Brew, i);
            if (i == -1)
            {
                break;
            }
            var e = log[i]!;
            if (e.Owner != Alchemist)
            {
                continue;
            }
            foreach (var b in Batches.Where(b => b.Formula.Index == e.Type))
            {
                b.Count = e.Extra;
                foreach (var ing in b.Formula.Ingredients)
                {
                    var d = _s.Item(ing.Item);
                    Inventory.AddStack(_s, Alchemist, new ItemStack(ing.Item, (byte)d.Type, 25, (byte)(ing.Count * e.Extra), (byte)d.Weight));
                }
            }
            log.Free(i);
        }
    }
}

// orig: the brewing rules of R033 and R036 (see each member)
/// <summary>Brewing rules used outside the alchemy screen.</summary>
public static class Brewing
{
    // orig: R033+0x094A / R033+0x1390: m = n > 2 ? 100 / (n >> 1) : 100; Int + (Alchemy - mystic) + stone + m, 0 if negative or no batches, at most 99
    /// <summary>
    /// The chance of a brew of <paramref name="batches"/> batches: base 100 (100 / (batches / 2)
    /// for more than two) + Intelligence + Alchemy − the formula's mystic number + the
    /// philosopher's stone, kept in 0..99; 0 for no batches.
    /// </summary>
    public static int SuccessChance(int intelligence, int alchemy, int mysticNumber, int stone, int batches) =>
        batches == 0 ? 0 : Math.Clamp(BaseChance(batches) + intelligence + alchemy - mysticNumber + stone, 0, 99);

    /// <summary>The first term of the chance: 100, or 100 / (batches / 2) for more than two batches.</summary>
    public static int BaseChance(int batches) => batches > 2 ? 100 / (batches >> 1) : 100;

    // orig: the count of R033+0x0658 (1462:0658) draft: party[i].present, 0x48 <= id < 0x5f (unmasked), DS:E716[id & 0xfff] += qty
    /// <summary>How many of each ingredient (by item code) the present members carry (the brewing bench).</summary>
    public static int[] IngredientsCarried(GameState s)
    {
        var stock = new int[256];
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (s.Party.PresentMember(i) is { } m)
            {
                foreach (var st in m.Pack.Where(st => st.Code is >= BrewingBench.FirstIngredient and <= BrewingBench.LastIngredient))
                {
                    stock[st.Code & 0xFFF] += st.Count;
                }
            }
        }
        return stock;
    }

    /// <summary>
    /// How many of each ingredient the formula page counts: every party slot below the party size,
    /// even one whose member has left (status 0, the pack still in the slot), and the codes 0x48..0x5F.
    /// </summary>
    // orig: alc_load R033+0x17F6 (15D1:0106) matched: for (i = 0; i < party_size; i++) if (nitems > 0) for (j < nitems) if (0x48 <= id <= 0x5f) DS:E7A6[id - 0x48] += qty
    // C: DS:E7A6 has 0x17 entries (0x48..0x5E): the count of 0x5F (the first potion) is added to the word after it, DS:E7D4 = DS:E7D4, the
    // C: brewing bench's row, which the bench sets to 0 when it opens (R033+0x0000) before reading it; the formula page never shows it.
    public static int[] IngredientsOnTheFormulaPage(GameState s)
    {
        var stock = new int[256];
        for (var i = 0; i < s.Party.Members.Count; i++)
        {
            foreach (var st in s.Party.Members[i].Pack.Where(st => st.Code is >= BrewingBench.FirstIngredient and <= BrewingBench.LastIngredient + 1))
            {
                stock[st.Code] += st.Count;
            }
        }
        return stock;
    }

    // orig: R036+0x191C (1462:191C) draft (bytes match)
    /// <summary>The batches of the member's first brew under way, 0 if none.</summary>
    public static int BatchesBrewing(GameState s, int member)
    {
        for (var i = 0; i < EventLog.Capacity; i++)
        {
            i = s.Events.Next(EventKinds.Brew, i);
            if (i == -1)
            {
                break;
            }
            if (s.Events[i]!.Owner == member)
            {
                return s.Events[i]!.Extra;
            }
        }
        return 0;
    }

    // orig: R036+0x1EC2 (1462:1EC2) draft (bytes match); tests: CampAndSheetRulesTests
    // BUG: all five ingredient slots are returned (FixUnbrewEmptyIngredientSlots).
    /// <summary>
    /// Calls off the member's brews (a camp activity changes): each brew's ingredients return
    /// to the member at quality 25, one set per batch. Faithful quirk: all five ingredient slots
    /// are given back, so an empty slot adds an empty stack of item 0
    /// (<see cref="OriginalBehaviour.FixUnbrewEmptyIngredientSlots"/>).
    /// </summary>
    public static void CallOff(GameState s, IReadOnlyList<Formula> formulas, int member)
    {
        for (var i = 0; i < EventLog.Capacity; i++)
        {
            i = s.Events.Next(EventKinds.Brew, i);
            if (i == -1)
            {
                break;
            }
            var e = s.Events[i]!;
            if (e.Owner != member)
            {
                continue;
            }
            s.Events.Free(i);
            var formula = formulas.First(f => f.Index == e.Type);
            var slots = s.Behaviour.FixUnbrewEmptyIngredientSlots ? formula.Ingredients.ToList() : formula.Slots;
            for (var batch = 0; batch < e.Extra; batch++)
            {
                foreach (var ing in slots)
                {
                    var d = s.Item(ing.Item);
                    Inventory.AddStack(s, member, new ItemStack(ing.Item, (byte)d.Type, 25, (byte)ing.Count, (byte)d.Weight));
                }
            }
        }
    }

    // orig: event_remove(0x1D, -1, -1, -1, -1) in R036+0x082C (1462:082C) draft (bytes match)
    /// <summary>Leaving the camp: every brew not yet done is lost (its ingredients were used).</summary>
    public static void AbandonAll(GameState s) => s.Events.Remove(EventKinds.Brew, -1, -1, -1, -1);
}
