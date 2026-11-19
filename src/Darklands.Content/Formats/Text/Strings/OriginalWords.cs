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

    private WordTable(string name, int pointers, int first, int count, int[] offsets)
    {
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

    internal bool IsFar => _offsets.Length == 0;

    internal int Pointer(int k) => _pointers + 4 * k;

    internal int Offset(int k) => _offsets[k];

    internal static WordTable Far(string name, int pointers, int count, int first = 0) => new(name, pointers, first, count, []);

    internal static WordTable Data(string name, params int[] offsets) => new(name, 0, 0, offsets.Length, offsets);
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

    /// <summary>Every table, for reports and tests.</summary>
    public static IReadOnlyList<WordTable> All { get; } =
        [FameTitles, StandingWords, Directions, TownSizes, TownSizesTab, ShopNames, JobNames, SkillNames, RiskWords, CampWords];

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

    /// <summary>The key of entry <paramref name="k"/>: where the word is in the EXE (<c>exe:290E:XXXX</c>, <c>exe:DS:XXXX</c>).</summary>
    public string Key(WordTable table, int k)
    {
        var (segment, offset) = Where(table, k);
        return segment == DarklandsExecutable.DataGroup
            ? string.Create(CultureInfo.InvariantCulture, $"exe:DS:{offset:X4}")
            : string.Create(CultureInfo.InvariantCulture, $"exe:{segment:X4}:{offset:X4}");
    }

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
        if (!table.IsFar)
        {
            return (DarklandsExecutable.DataGroup, table.Offset(k));
        }
        var pointer = exe.Far(FarSegment, table.Pointer(k), 4);
        return (pointer[2] | pointer[3] << 8, pointer[0] | pointer[1] << 8);
    }
}
