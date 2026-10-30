namespace Darklands.Core.Battles.Generation;

/// <summary>
/// Mine battlefields (overlay R009): a mine is put together from up to sixteen sections. A section is a prepared map of IMAPS.CAT
/// ("iminegen.nnn", a random one not yet used in this mine, or "iminspec.nnn", a special one), turned by a random quarter turn and
/// mirror, with its walls re-rolled, doors and chests made, loot piles filled and enemy groups placed; sections are joined through
/// their exits and each is saved as a level. The battlefield type chooses which sections make up the mine: the small mines
/// (0x66..0x69), the one-section mines (0x0c and the lairs 0x7f..0x81, 0x6c and 0x6d), the big mines (0x0b, 0x0d, 0x0e..0x10) and
/// the fixed-layout mines (0x0a and 0x82..0x86, which build the one section named by the request).
/// </summary>
// orig: R009 1432 (32 functions) and R009 182c (16 functions); drafts written
// from the asm: 12 + 7 byte-matched per the draft notes, the rest checked through the oracle (Oracle:mine.json).
public static class MineBattlefield
{
    /// <summary>Builds the mine of a battlefield type; returns the number of levels (the party starts on level 0).</summary>
    // orig: mine-builder-entry R009+0x0000 (1432:0000) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    // C: the specs are the caller's words a..h: (type, quality, count) of the first two enemy specs, then the type and quality of the
    // third, which the fixed-layout mines read as the section to build and its turn.
    public static int Build(GenerationState g, int battlefield, (int Type, int Quality, int Count)[] specs)
    {
        var mine = new MineBuilder(g);
        return mine.Build(battlefield, specs);
    }
}

/// <summary>The state of one mine being built, and the layouts of the mine types.</summary>
internal sealed partial class MineBuilder
{
    private const int SectionCount = 16;
    private const int SlotCount = 4;
    private const int GeneralMapCount = 21;

    private readonly GenerationState _g;
    private readonly MineFlags _flags = new();

    /// <summary>DS:E037: the four level slots (the 0xCFF0-byte buffer).</summary>
    private readonly MineLevel?[] _slots = new MineLevel?[SlotCount];

    /// <summary>DS:DFD5: the slot of each section (−1 none). DS:DFCD: the section of each slot (−1 none). DS:DFC5: how many exits the section file had.</summary>
    private readonly int[] _slotOfSection = new int[SectionCount];
    private readonly int[] _sectionOfSlot = new int[SlotCount];
    private readonly int[] _fileExits = new int[SlotCount];

    /// <summary>DS:E00D: which general maps (iminegen.000..020) this mine has used.</summary>
    private readonly bool[] _usedGeneralMaps = new bool[GeneralMapCount];

    /// <summary>DS:E022: set when a section is given a slot. DORMANT: nothing in the mine generator reads it.</summary>
    private readonly bool[] _sectionPlaced = new bool[SectionCount];

    /// <summary>DS:DFF7: the next pile id (a running number over the whole mine).</summary>
    private int _nextPileId;

    /// <summary>DS:E035: the special variants 0x87..0x89 (every section from the special map 13).</summary>
    private bool _specialVariant;

    /// <summary>DS:E036: bit per slot whose section is a special map (its groups are never cleared with the level).</summary>
    private int _specialSlots;

    /// <summary>
    /// The stale word the pile filler reads one past its list of ten kinds (a leftover in the original's stack frame): zero on a fresh
    /// stack, and once the pile pass has drawn a random number the low word of a library return address (0x29CB, the multiply helper
    /// <c>rand</c> calls) lies on that stack slot.
    /// </summary>
    // UNCERTAIN: what the real game's stack holds there depends on everything that ran before; the oracle's stack (file reads are stubbed)
    // shows 0 for the first pile and 0x29CB afterwards, and a value above 9 means "no new kind picked, the last one repeats".
    private int StaleStackWord => _pileRollMade ? 0x29CB : 0;

    private bool _pileRollMade;

    public MineBuilder(GenerationState g)
    {
        _g = g;
    }

    private int Roll(int sides) => _g.Roll(sides);

    private BattlefieldForces Forces => _g.Forces;

    // C: the specs are the caller's words a..h of mine-builder-entry; they are passed on as the layouts need them.
    public int Build(int t, (int Type, int Quality, int Count)[] specs)
    {
        _nextPileId = 0;
        _specialVariant = IsSpecialVariant(t);
        _specialSlots = 0;
        t = t switch { 0x87 => 0x0B, 0x88 => 0x0D, 0x89 => 0x10, _ => t };
        ResetSections();
        var staleFrameWord = WriteTileDescription(t);
        var (ft, fq, fn) = specs[0];
        var (lv, p) = (specs[2].Type, specs[2].Quality);
        switch (t)
        {
            case 0x0B:
                return Mine0B(t);
            case 0x0C or 0x7F or 0x80 or 0x81:
                return LairLayout(t, ft, fq, fn);
            case 0x0D:
                return Mine0D(t);
            case 0x0E or 0x0F or 0x10:
                return Mine0E(t);
            case >= 0x66 and <= 0x69:
                return SmallMine(t);
            case 0x6C or 0x6D:
                return SpecialMine(t);
            case 0x82:
                return FixedMine0B(t, ft, fq, fn, lv, p);
            case 0x83:
                return FixedMine0D(t, lv, p);
            case 0x84 or 0x85 or 0x86:
                return FixedMine0E(t, lv, p);
            default:
                // DORMANT: only type 0x0a reaches this arm (every other battlefield type routed here has its own case).
                return OneSectionMine(t, staleFrameWord);
        }
    }

    /// <summary>Clears the section bookkeeping.</summary>
    // orig: R009+0x019E (1432:019E) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private void ResetSections()
    {
        Array.Clear(_usedGeneralMaps);
        Array.Fill(_sectionOfSlot, -1);
        Array.Clear(_fileExits);
        Array.Fill(_slotOfSection, -1);
        Array.Clear(_sectionPlaced);
    }

    /// <summary>Mines 0x87..0x89: mines 0x0b / 0x0d / 0x10 with special sections.</summary>
    // orig: R009+0x3F8A (1432:3F8A) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private static bool IsSpecialVariant(int t) => t is 0x87 or 0x88 or 0x89;

    /// <summary>The small mines 0x66..0x69.</summary>
    // orig: R009+0x3F54 (1432:3F54) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private static bool IsSmallMine(int t) => t is >= 0x66 and <= 0x69;

    /// <summary>The mines with a fixed layout: 0x0a and 0x82..0x86 (a section named by the request, the debugging overrides apply).</summary>
    // orig: R009+0x3F6C (1432:3F6C) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private static bool IsFixedLayout(int t) => t is 0x0A or (>= 0x82 and <= 0x86);

    /// <summary>The lairs 0x0c, 0x7f..0x81: one section, enemies in up to five groups.</summary>
    private static bool IsLair(int t) => t is 0x0C or 0x7F or 0x80 or 0x81;

    // ---- the layouts ----------------------------------------------------------------------------------------------

    /// <summary>One-section mine (the default arm: battlefield type 0x0a).</summary>
    // orig: R009+0x01E0 (1432:01E0) draft (byte-matched per the draft notes); checked against asm
    // (the rotation local is read before it is set); tests: Oracle:mine.json
    // C: 01e0's local `r` (the rotation, passed by address) is never set: a fixed-layout mine reads it. Its frame slot [bp-2] is the one
    // the tile description's second pair of draws left behind (r[1]), so the rotation is that draw (1..3), not a new random one.
    private int OneSectionMine(int t, int staleFrameWord)
    {
        var rotation = staleFrameWord;
        LoadSection(t, 0, 0, ref rotation, true);
        PlaceParty(t, 0, -1, -1);
        SaveSection(t, 0);
        return 1;
    }

    /// <summary>Small mines 0x66..0x69: one section with one or two groups.</summary>
    // orig: R009+0x0218 (1432:0218) draft (unmatched draft); tests: Oracle:mine.json
    private int SmallMine(int t)
    {
        var rotation = 0;
        LoadSection(t, 0, 0, ref rotation, false);
        var lore = _g.Context.LoreLevel();
        // C: `for (g = 0; g < 5; g++) party[g];` reads the party and does nothing.
        var s = Roll((lore + 1) / 2) + 1;
        switch (t)
        {
            case 0x66:
                PlaceGroup(t, Forces.AddKind(0, 0x3B, 1), s, 0, -1, 0, 0, 0, false, 0, -1, -1);
                break;
            case 0x67:
                {
                    var n = Roll(4) + 2;
                    var g1 = Forces.AddKind(0, 0x12, Math.Max(Math.Min(lore, 5), 1));
                    PlaceGroup(t, g1, 1, 0, Forces.AddKind(0, 7, s), n, 0, 0, false, 0, -1, -1);
                    break;
                }
            case 0x68:
                PlaceGroup(t, Forces.AddKind(0, 0x3A, 1), s, 0, -1, 0, 0, 0, false, 0, -1, -1);
                break;
            case 0x69:
                {
                    var n = s / 2;
                    s -= n;
                    var g1 = Forces.AddKind(0, 0x31, 1);
                    PlaceGroup(t, g1, s, 0, -1, 0, 0, 0, false, 0, -1, -1);
                    PlaceGroup(t, g1, n, 0, -1, 0, 0, 0, false, 1, -1, -1);
                    break;
                }
        }
        ReleaseSpot(0, 0);
        SaveSection(t, 0);
        return 1;
    }

    /// <summary>Mine 0x0b: the sections 3, 4, 7, 8, 10, 13 and 14 (the others the code names are never loaded).</summary>
    // orig: R009+0x0398 (1432:0398) draft (close per the draft notes: stack slot order only);
    // tests: Oracle:mine.json
    // C: sections 1, 2, 5, 6, 9, 11, 12 are named in joins, party entrances and saves, but never loaded: those calls find no slot and do nothing
    // (the draft of a bigger mine, cut down).
    private int Mine0B(int t)
    {
        var lore = _g.Context.LoreLevel();
        _g.MarkPersistent();
        int a = 0, b = 0, c = 0;
        var k = 0;
        LoadSection(t, 3, 0, ref a, true);
        k = 1;
        LoadSection(t, 4, k, ref b, false);
        PlaceParty(t, 1, 2, 3);
        Join(t, 1, 2, 3);
        Join(t, 2, 3, -1);
        CarveGap(t, 3, 4, -1, a);
        SaveSection(t, 1);
        SaveSection(t, 2);
        SaveSection(t, 3);
        var j = -1;
        for (var i = 7; i <= 7; i++)
        {
            if (j >= 0)
            {
                // DORMANT: the loop runs once (i = 7) with j still −1.
                break;
            }
            LoadSection(t, i, ++k, ref b, false);
            Join(t, 4, i, -1);
            j = i;
        }
        if (j < 0)
        {
            // DORMANT: j is 7 after the loop above.
            LoadSection(t, 8, ++k, ref a, true);
            Join(t, 4, 8, -1);
            j = 8;
        }
        LoadSection(t, 10, ++k, ref c, true);
        Join(t, 4, 10, -1);
        SaveSection(t, 4);
        for (var i = j + 1; i <= 8; i++)
        {
            // C: i is only ever 8 here, so the lore test is never drawn.
            if (i == 8 || Roll(10) <= lore)
            {
                LoadSection(t, i, ++k, ref a, i == 8);
                Join(t, j, i, -1);
                SaveSection(t, j);
                j = i;
            }
        }
        CarveGap(t, 8, -1, -1, a);
        SaveSection(t, 8);
        LoadSection(t, 0x0D, ++k, ref b, false);
        LoadSection(t, 0x0E, ++k, ref b, false);
        CarveGap(t, 10, 11, 13, c);
        Join(t, 11, 13, -1);
        Join(t, 13, 14, -1);
        SaveSection(t, 10);
        SaveSection(t, 11);
        SaveSection(t, 13);
        SaveSection(t, 14);
        return k + 1;
    }

    /// <summary>The lairs and mine 0x0c: one section, the enemies spread over up to five groups.</summary>
    // orig: R009+0x05E4 (1432:05E4) draft (unmatched draft); tests: Oracle:mine.json
    private int LairLayout(int t, int kindType, int quality, int count)
    {
        var lore = _g.Context.LoreLevel();
        int number;
        switch (t)
        {
            case 0x7F:
                (kindType, quality, number) = (0x2A, 1, lore * 4 + 2);
                break;
            case 0x80:
                (kindType, quality, number) = (0x2F, 1, lore * 2 + 1);
                break;
            case 0x81:
                (kindType, quality, number) = (0x34, 1, lore * 3 / 2);
                break;
            default:
                number = count;
                break;
        }
        int reinforcements;
        if (kindType == 0x34)
        {
            var x = (lore - 1) / 3 + 2;
            reinforcements = number - x;
            number = x;
        }
        else if (number > 8)
        {
            reinforcements = number - 8;
            number = 8;
        }
        else
        {
            reinforcements = 0;
        }
        var each = number / 5;
        var remainder = number % 5;
        var rotation = 0;
        LoadSection(t, 0, 0, ref rotation, false);
        var kind = Forces.AddKind(0, kindType, quality);
        for (int i = 0, offset = 0; offset < 50; i++, offset += 10)
        {
            var groupCount = each;
            if (remainder > i)
            {
                groupCount++;
            }
            if (groupCount <= 0)
            {
                break;
            }
            var reinforcement = offset == 0 && reinforcements > 0 ? reinforcements * 10 + 1 : 0;
            PlaceGroup(t, kind, groupCount, reinforcement, -1, 0, 0, 0, false, i, -1, -1);
            ReleaseSpot(0, i);
        }
        SaveSection(t, 0);
        return 1;
    }

    /// <summary>Mine 0x82: the one section named by the request (12 = the lair layout), turned by the request's quality.</summary>
    // orig: R009+0x0732 (1432:0732) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int FixedMine0B(int t, int kindType, int quality, int count, int section, int turn)
    {
        if (section == 12)
        {
            return LairLayout(section, kindType, quality, count);
        }
        if (section < 1 || section > 14)
        {
            section = 1;
        }
        var withGap = section is 3 or 8 or 10;
        LoadSection(t, section, 0, ref turn, withGap);
        PlaceParty(t, section, -1, -1);
        Join(t, section, -1, -1);
        if (section == 4)
        {
            Join(t, section, -1, -1);
        }
        if (withGap)
        {
            CarveGap(t, section, -1, -1, turn);
        }
        SaveSection(t, section);
        return 1;
    }

    /// <summary>Mine 0x0d: the sections 1, 2, 3, 5, 7, 8, 11 and 12.</summary>
    // orig: R009+0x07E4 (1432:07E4) draft (close per the draft notes: 6.00 constant-propagates k);
    // tests: Oracle:mine.json
    // C: the sections 6 and 10 are named in a join but never loaded.
    private int Mine0D(int t)
    {
        _g.Context.LoreLevel();
        _g.MarkPersistent();
        int a = 0, b = 0, c = 0, d = 0, e = 0;
        LoadSection(t, 1, 0, ref a, true);
        LoadSection(t, 2, 1, ref b, false);
        LoadSection(t, 3, 2, ref c, false);
        PlaceParty(t, 1, -1, -1);
        CarveGap(t, 1, 2, -1, a);
        CarveGap(t, 2, 3, -1, b);
        SaveSection(t, 1);
        SaveSection(t, 2);
        LoadSection(t, 7, 3, ref a, false);
        Join(t, 3, 6, 7);
        var k = 4;
        LoadSection(t, 0x0B, k, ref b, false);
        d = 0x0B;
        Join(t, 3, 0x0A, d);
        SaveSection(t, 3);
        LoadSection(t, 5, ++k, ref c, false);
        Join(t, 7, 5, -1);
        Join(t, 0x0B, 5, -1);
        SaveSection(t, 5);
        LoadSection(t, 8, ++k, ref d, true);
        CarveGap(t, 7, 8, -1, a);
        CarveGap(t, 8, -1, -1, d);
        SaveSection(t, 7);
        SaveSection(t, 8);
        LoadSection(t, 0x0C, ++k, ref e, true);
        CarveGap(t, 0x0B, 0x0C, -1, b);
        CarveGap(t, 0x0C, -1, -1, e);
        SaveSection(t, 0x0B);
        SaveSection(t, 0x0C);
        return k + 1;
    }

    /// <summary>Special one-section mines 0x6c and 0x6d (0x6d: floor tile 7 under the first enemy group).</summary>
    // orig: R009+0x0990 (1432:0990) draft (close per the draft notes: 6.00 keeps the offset and
    // segment of DS:E037 across the store); tests: Oracle:mine.json
    // C: battlefield-group-table->pos is read whether or not a group was made (the first record of level 0: zeros if none).
    private int SpecialMine(int t)
    {
        var rotation = 0;
        LoadSection(t, 0, 0, ref rotation, false);
        if (t == 0x6D)
        {
            var first = Forces.Groups[0].Count > 0 ? Forces.Groups[0][0] : new EnemyGroup();
            var x = first.X / 32;
            var y = first.Y / 16;
            var slot = _slots[0]!;
            slot.AndCell(x, y, ~0xF0u);
            slot.OrCell(x, y, 0x70);
        }
        SaveSection(t, 0);
        return 1;
    }

    /// <summary>Mine 0x83: the one section named by the request (9 and 13 = the special mines).</summary>
    // orig: R009+0x0A18 (1432:0A18) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int FixedMine0D(int t, int section, int turn)
    {
        if (section == 9)
        {
            return SpecialMine(0x6C);
        }
        if (section == 13)
        {
            return SpecialMine(0x6D);
        }
        if (section < 1 || section > 13)
        {
            section = 1;
        }
        var withGap = section is 1 or 8 or 12;
        LoadSection(t, section, 0, ref turn, withGap);
        PlaceParty(t, section, -1, -1);
        Join(t, section, -1, -1);
        if (section is 4 or 6 or 7 or 10 or 11)
        {
            Join(t, section, -1, -1);
        }
        if (section is 1 or 2 or 7 or 8 or 11 or 12)
        {
            CarveGap(t, section, -1, -1, turn);
        }
        SaveSection(t, section);
        return 1;
    }

    /// <summary>Mines 0x0e..0x10: nine sections in a chain.</summary>
    // orig: R009+0x0AF8 (1432:0AF8) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int Mine0E(int t)
    {
        _g.Context.LoreLevel();
        _g.MarkPersistent();
        int a = 0, b = 0, c = 0;
        LoadSection(t, 1, 0, ref a, true);
        LoadSection(t, 2, 1, ref c, false);
        PlaceParty(t, 1, -1, -1);
        CarveGap(t, 1, 2, -1, a);
        SaveSection(t, 1);
        LoadSection(t, 0x0A, 2, ref b, false);
        CarveGap(t, 2, 6, 0x0A, c);
        Join(t, 6, 0x0A, -1);
        SaveSection(t, 6);
        SaveSection(t, 0x0A);
        LoadSection(t, 3, 3, ref a, false);
        Join(t, 2, 3, -1);
        LoadSection(t, 0x0B, 4, ref b, false);
        CarveGap(t, 3, 7, 0x0B, a);
        Join(t, 7, 0x0B, -1);
        SaveSection(t, 2);
        SaveSection(t, 7);
        SaveSection(t, 0x0B);
        LoadSection(t, 4, 5, ref a, false);
        Join(t, 3, 4, -1);
        LoadSection(t, 0x0C, 6, ref b, false);
        CarveGap(t, 4, 8, 0x0C, a);
        Join(t, 8, 0x0C, -1);
        SaveSection(t, 3);
        SaveSection(t, 8);
        SaveSection(t, 0x0C);
        LoadSection(t, 5, 7, ref a, false);
        Join(t, 4, 5, -1);
        LoadSection(t, 0x0D, 8, ref b, true);
        CarveGap(t, 5, 9, 0x0D, a);
        Join(t, 9, 0x0D, -1);
        SaveSection(t, 4);
        SaveSection(t, 5);
        SaveSection(t, 9);
        SaveSection(t, 0x0D);
        return 9;
    }

    /// <summary>Mines 0x84..0x86: the one section named by the request.</summary>
    // orig: R009+0x0CFA (1432:0CFA) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private int FixedMine0E(int t, int section, int turn)
    {
        if (section < 1 || section > 0x0D)
        {
            section = 1;
        }
        LoadSection(t, section, 0, ref turn, section is 1 or 0x0D);
        PlaceParty(t, section, -1, -1);
        Join(t, section, -1, -1);
        if (section is >= 2 and <= 5)
        {
            Join(t, section, -1, -1);
        }
        if (section is >= 1 and <= 5)
        {
            CarveGap(t, section, -1, -1, turn);
        }
        SaveSection(t, section);
        return 1;
    }
}
