using System.Globalization;
using Darklands.Content.Formats.Exe;
using Darklands.Core.Rules;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>
/// One of the original's word tables: a far-pointer table of segment 290E (entry k is the string its pointer k points at) or a
/// list of DGROUP strings. <see cref="First"/> is the lowest index the program uses (below 0 where it reads before the table).
/// </summary>
public sealed class WordTable
{
    private readonly int _pointers;
    private readonly int[] _offsets;

    private WordTable(string name, int pointers, int first, int count, int[] offsets, bool near = false)
    {
        IsNear = near;
        Name = name;
        _pointers = pointers;
        First = first;
        Count = count;
        _offsets = offsets;
    }

    /// <summary>A name for the table (for reports and tests).</summary>
    public string Name { get; }

    public int First { get; }

    public int Count { get; }

    /// <summary>Whether <paramref name="k"/> is an entry of the table.</summary>
    public bool Contains(int k) => k >= First && k < First + Count;

    internal bool IsFar => !IsNear && _offsets.Length == 0;

    /// <summary>A table of DGROUP near pointers (entry k is the string its word k points at).</summary>
    internal bool IsNear { get; }

    internal int NearPointer(int k) => _pointers + 2 * k;

    internal int Pointer(int k) => _pointers + 4 * k;

    internal int Offset(int k) => _offsets[k];

    internal static WordTable Far(string name, int pointers, int count, int first = 0) => new(name, pointers, first, count, []);

    internal static WordTable Data(string name, params int[] offsets) => new(name, 0, 0, offsets.Length, offsets);

    internal static WordTable Near(string name, int pointers, int count) => new(name, pointers, 0, count, [], near: true);
}

/// <summary>
/// The original's word tables, read from the player's DARKLAND.EXE the way the program reads them: a far table through its
/// pointers (the key of a word is where the pointer points, <c>exe:290E:XXXX</c>), a DGROUP word at its offset (<c>exe:DS:XXXX</c>);
/// the keys are those of <see cref="OriginalTextExtractor"/>. The rules keep the numbers (a fame rank, a standing band, a skill, a
/// shop ...); the screens, the card texts and the message text turn them into words here. Nothing of the game's text is stored in
/// this program.
/// </summary>
public sealed class OriginalWords(DarklandsExecutable exe)
{
    private const int FarSegment = 0x290E;
    private const int MaxLength = 200;

    // orig: R136+0xA60B = 290E:1F7B, 11 far strings, read by R034+0x11DC (PartyStanding.FameRank)
    public static readonly WordTable FameTitles = WordTable.Far("fame-titles", 0x1F7B, 11);

    /// <summary>The character creation's six family backgrounds (290E:20AB), as its list shows them.</summary>
    // Not in the original as a table of this program: the 1.2 translations (the creation screen asks for its words by key).
    public static readonly WordTable BackgroundNames = WordTable.Far("background-names", 0x20AB, 6);

    /// <summary>The character creation's 34 occupations (290E:1EE7), as its list shows them.</summary>
    public static readonly WordTable OccupationNames = WordTable.Far("occupation-names", 0x1EE7, Darklands.Core.Characters.Creation.CharacterCreation.OccupationCount);

    // orig: R136+0xA813 = 290E:2183, 6 far strings, read by R034+0x0EE6, R034+0x144C, R034+0x166E and the card texts' case 16 (R043+0x1316)
    //   (PartyStanding.StandingBand)
    public static readonly WordTable StandingWords = WordTable.Far("standing-words", 0x2183, 6);

    // orig: R136+0xA773 = 290E:20E3, 8 far strings, read by R034+0x166E and the card texts' cases 57, 58, 69, 71, 72 (R043+0x1316)
    //   (PartyStanding.Direction)
    public static readonly WordTable Directions = WordTable.Far("directions", 0x20E3, 8);

    // orig: R034+0x0EE6 (the pointer over the small map): mov si, 0x362C / 0x3638 / (0x3647) (PartyStanding.SizeClass)
    public static readonly WordTable TownSizes = WordTable.Data("town-sizes", 0x362C, 0x3638, 0x3647);

    // orig: R034+0x144C (Tab / Shift-Tab): its own copy, mov si, 0x371F / 0x372B / (0x373A)
    public static readonly WordTable TownSizesTab = WordTable.Data("town-sizes-tab", 0x371F, 0x372B, 0x373A);

    // orig: R136+0xA70F = 290E:207F, read by trade-screen-entry R032+0x0058 and R029+0x21CE with any shop number: 0..8 the shops, 9..11
    //   Physician, Hospital, Nobility; the three pointers before it (290E:2073, -3..-1) are Tope, Ull, Volund (Trading.HasTableName)
    public static readonly WordTable ShopNames = WordTable.Far("shop-names", 0x207F, 15, first: -3);

    // orig: R136+0xA82B = 290E:219B, read by R036+0x197E with the job record's name byte (DS:3ACE + 18k); entry 0 is the day labourer
    //   (DayJobs.Job.NameEntry); the 25 entries the 31 jobs use (the table's further pointers are the card texts' variable names)
    public static readonly WordTable JobNames = WordTable.Far("job-names", 0x219B, 25);

    // orig: 290E:2137 (entries 12..30 of the far table 290E:2107), read by raise-skill-by R029+0x02EA (0x033C, 0x04AA), the
    //   ability-change notice R021+0x0080 (0x00AB) and the camp's teacher list R036+0x1D24 (0x1DBB): the short names at 290E:0BF2..
    // C: the character sheet's longer names (290E:0522.., pointers at 290E:1E83, read by R031+0x2B14) are a different table.
    public static readonly WordTable SkillNames = WordTable.Far("skill-names", 0x2137, 19);

    // orig: R033+0x094A cases 0, 1, 2: the risk word copied from DS:32FE / DS:3302 / DS:3309 (Formula.RiskWord)
    public static readonly WordTable RiskWords = WordTable.Data("risk-words", 0x32FE, 0x3302, 0x3309);

    // orig: R036+0x08EC: "potion" (DS:3D5C, R036+0x0CED), then "s" (DS:3D63, R036+0x0D03) unless the count is 1; the teacher's fee and
    //   "pfs" (DS:3D65, R036+0x0BE7) (CampStay.Detail)
    public static readonly WordTable CampWords = WordTable.Data("camp-words", 0x3D5C, 0x3D63, 0x3D65);

    // orig: R136+0xA4F7 = 290E:1E67, 26 far strings: the character sheet's attribute and skill labels of the open boxes (R031+0x2B14)
    public static readonly WordTable SheetLabels = WordTable.Far("sheet-labels", 0x1E67, 26);

    // orig: grade-words DS:4FCE, 7 near pointers (DS:4E54 "very poor" .. DS:4E81 "superb"), read by chance-line R029+0x5B62 and the
    //   Shift hint R043+0x27E6; the words round it (index 7 DS:6F43, -1 DS:0000, -2 DS:7461) are empty strings (GradeWord)
    public static readonly WordTable GradeWords = WordTable.Near("grade-words", 0x4FCE, 7);

    // orig: R136+0xA3D3 = 290E:1D43, 12 far strings: the month names, read by the card texts' $MonthName and $SabbatDate (R043+0x1316)
    //   and the party information and prayer screens (R034)
    public static readonly WordTable MonthNames = WordTable.Far("month-names", 0x1D43, 12);

    // orig: 290E:20C3, 8 far strings: the canonical hours of TimeOfDay.Band (bell-time-labels), read by the card texts' $CurrentBell
    //   (R043+0x1316), label-to-slot kinds 3..5 (R029, CardContext.NameToSlot), the hourglass (R043) and the party information (R034)
    public static readonly WordTable BellNames = WordTable.Far("bell-names", 0x20C3, 8);

    // orig: R136+0xA9B3 = 290E:2323 (city type 1: f56 % 6) and R136+0xA9CB = 290E:233B (city type 2: f56 % 9): the lord titles of the
    //   card texts' $CityLordTitle (R043+0x1316, case 51); the bytes are the game's own (0x1F its a-umlaut, { o-umlaut, | u-umlaut)
    public static readonly WordTable LordTitles1 = WordTable.Far("lord-titles-1", 0x2323, 6);

    public static readonly WordTable LordTitles2 = WordTable.Far("lord-titles-2", 0x233B, 9);

    // orig: 290E:2B57, 3 far strings (-1 "oppose", 0 "are undecided about", 1 "support"): R136+0xB1EB is the middle one, indexed by
    //   text-slot-support-zero..6 (the card texts' $Support..$Support6, R043+0x1316); the word of a side s is entry s + 1
    public static readonly WordTable SupportWords = WordTable.Far("support-words", 0x2B57, 3);

    // orig: 290E:2107 entries 0..11 (goods merchant .. castle), the patrons of the market (R049) and the council hall (R054)
    public static readonly WordTable PatronNames = WordTable.Far("patron-names", 0x2107, 12);

    // orig: 290E:1FA7 (16), 290E:1FE7 (38) and 290E:2107 (31): the dragons', dwarfs' and titles' names label-to-slot kinds 6..8 copy
    //   (R029+0x1FB8, PersonalNamesDecoder); Not in the original: their keys for the 1.2 translations (the untranslated-text audit)
    public static readonly WordTable DragonNames = WordTable.Far("dragon-names", 0x1FA7, 16);

    public static readonly WordTable DwarfNames = WordTable.Far("dwarf-names", 0x1FE7, 38);

    public static readonly WordTable TitleNames = WordTable.Far("title-names", 0x2107, 31);

    // orig: 290E:235F (0x6C drawn), 290E:2517 (0x58), 290E:267F (0x92) and 290E:28CB (0x10): the men's and women's first names, the
    //   surnames and the merchant families made-up-name draws from (R029+0x5CC4, PersonalNamesDecoder); Not in the original: their keys
    //   for the 1.2 translations (GitHub issue 5: the names the cards put into their texts)
    public static readonly WordTable MenFirstNames = WordTable.Far("men-first-names", 0x235F, 0x6C);

    public static readonly WordTable WomenFirstNames = WordTable.Far("women-first-names", 0x2517, 0x58);

    public static readonly WordTable Surnames = WordTable.Far("surnames", 0x267F, 0x92);

    public static readonly WordTable MerchantFamilies = WordTable.Far("merchant-families", 0x28CB, 0x10);

    // orig: party-defaults R001+0x0000 copies each Quickstart member's full and short name from DS:0E50 / 0E61, 0E68 / 0E77, 0E7F / 0E8B,
    //   0E90 / 0EA2 (DefaultParty)
    public static readonly WordTable QuickstartNames = WordTable.Data("quickstart-names", 0x0E50, 0x0E61, 0x0E68, 0x0E77, 0x0E7F, 0x0E8B, 0x0E90, 0x0EA2);

    /// <summary>Every table, for reports and tests.</summary>
    public static IReadOnlyList<WordTable> All { get; } =
        [FameTitles, StandingWords, Directions, TownSizes, TownSizesTab, ShopNames, JobNames, SkillNames, RiskWords, CampWords, GradeWords, SheetLabels,
         MonthNames, BellNames, LordTitles1, LordTitles2, SupportWords, PatronNames, QuickstartNames];

    /// <summary>The words read from the player's EXE.</summary>
    public static OriginalWords Load(GameFiles files) => new(DarklandsExecutable.Load(files));

    /// <summary>Entry <paramref name="k"/> of a table, in the original's bytes (one Latin-1 character a byte).</summary>
    public string Text(WordTable table, int k)
    {
        var (segment, offset) = Where(table, k);
        var bytes = segment == DarklandsExecutable.DataGroup
            ? exe.DataSegment(offset, Math.Min(MaxLength, 0x10000 - offset))
            : exe.Far(segment, offset, MaxLength);
        var end = bytes.IndexOf((byte)0);
        return Darklands.Core.GameTextCodec.Decode(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>The four Quickstart members' names, as party-defaults copies them (DefaultParty.Create).</summary>
    public IReadOnlyList<Darklands.Core.Characters.QuickstartName> Quickstart() =>
        [.. Enumerable.Range(0, 4).Select(m => new Darklands.Core.Characters.QuickstartName(Text(QuickstartNames, 2 * m), Text(QuickstartNames, 2 * m + 1)))];

    /// <summary>The words at an EXE key (<c>exe:DS:XXXX</c>, <c>exe:290E:XXXX</c>, the extractor's keys) in the original's bytes.</summary>
    public string Exe(string key) => ExeOrNull(key) ?? throw new ArgumentException($"Not a key of the EXE: {key}", nameof(key));

    /// <summary>The words at an EXE key, or null when <paramref name="key"/> is not one.</summary>
    public string? ExeOrNull(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var parts = key.Split(':');
        if (parts.Length != 3 || parts[0] != "exe" || parts[2].Length != 4 ||
            !int.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset))
        {
            return null;
        }
        if (parts[1] == "DS")
        {
            return exe.String(offset);
        }
        if (parts[1].Length != 4 || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var segment))
        {
            return null;
        }
        var bytes = exe.Far(segment, offset, MaxLength);
        var end = bytes.IndexOf((byte)0);
        return Darklands.Core.GameTextCodec.Decode(end < 0 ? bytes : bytes[..end]);
    }

    /// <summary>The key of entry <paramref name="k"/>: where the word is in the EXE (<c>exe:290E:XXXX</c>, <c>exe:DS:XXXX</c>).</summary>
    public string Key(WordTable table, int k)
    {
        var (segment, offset) = Where(table, k);
        return segment == DarklandsExecutable.DataGroup
            ? string.Create(CultureInfo.InvariantCulture, $"exe:DS:{offset:X4}")
            : string.Create(CultureInfo.InvariantCulture, $"exe:{segment:X4}:{offset:X4}");
    }

    /// <summary>
    /// Grade word <paramref name="k"/> (grade-words[k]): "very poor" .. "superb" for 0..6; the entries next to the table (−2, −1, 7)
    /// point at empty strings, so they say nothing.
    /// </summary>
    public string GradeWord(int k) => GradeWords.Contains(k) ? Text(GradeWords, k) : k is -2 or -1 or 7 ? "" :
        throw new ArgumentOutOfRangeException(nameof(k), k, "Not an entry of grade-words or its empty neighbours.");

    /// <summary>
    /// A camp member's detail as the camp screen shows it: "potion" and "s" for a count other than 1, a job's name, or the
    /// teacher's fee and "pfs"; nothing for none.
    /// </summary>
    public string Detail(CampDetail detail) => detail.Kind switch
    {
        CampDetailKind.Potions => Text(CampWords, 0) + (detail.Number != 1 ? Text(CampWords, 1) : ""),
        CampDetailKind.Job => Text(JobNames, detail.Number),
        CampDetailKind.Fee => detail.Number.ToString(CultureInfo.InvariantCulture) + Text(CampWords, 2),
        _ => "",
    };

    private (int Segment, int Offset) Where(WordTable table, int k)
    {
        if (!table.Contains(k))
        {
            throw new ArgumentOutOfRangeException(nameof(k), k, $"Not an entry of the table {table.Name}.");
        }
        if (table.IsNear)
        {
            var near = exe.DataSegment(table.NearPointer(k), 2);
            return (DarklandsExecutable.DataGroup, near[0] | near[1] << 8);
        }
        if (!table.IsFar)
        {
            return (DarklandsExecutable.DataGroup, table.Offset(k));
        }
        var pointer = exe.Far(FarSegment, table.Pointer(k), 4);
        return (pointer[2] | pointer[3] << 8, pointer[0] | pointer[1] << 8);
    }
}
