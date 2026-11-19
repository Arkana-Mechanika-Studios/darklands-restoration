using System.Globalization;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Config;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Executable;
using Darklands.Content.Formats.World;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>
/// Builds the original-English string table (<c>en-original</c>) from the player's own files, using the readers Content
/// already has. Nothing of the game's text is stored in this program.
/// </summary>
/// <remarks>
/// Key spellings (never derived from the wording; entries with empty text in a record file are left out):
/// <list type="bullet">
/// <item><c>msg:NAME#n</c> block n (0-based, in file order) of the MSGFILES entry NAME, without ".MSG" (<c>msg:$CITYG04#3</c>).</item>
/// <item><c>lst:item/n.name</c>, <c>.short</c>; <c>lst:saint/n.name</c>, <c>.short</c>; <c>lst:formula/n.name</c>, <c>.short</c> (DARKLAND.LST).</item>
/// <item><c>snt:saint/n.desc</c> (DARKLAND.SNT); <c>alc:n.desc</c> (DARKLAND.ALC); <c>dsc:n.desc</c> (DARKLAND.DSC).</item>
/// <item><c>cty:n.field</c> (DARKLAND.CTY), field = short, full, leader, ruler, political-center, town-hall, fortress,
/// cathedral, church, market, imperial-mint, slum, barracks, pawnshop, monastery, inn, university.</item>
/// <item><c>enm:template/n.name</c>, <c>enm:template/n.image</c>, <c>enm:type/n.name</c> (DARKLAND.ENM).</item>
/// <item><c>ban:n</c> entry n of BANNER.DAT (code page 437 bytes, held one byte per character like the other text).</item>
/// <item><c>exe:DS:XXXX</c> the zero-terminated string at offset XXXX (4 uppercase hex digits) of DGROUP; <c>exe:290E:XXXX</c>
/// the same in the far data segment at link frame 290E (where the creation, name and load-screen tables point).</item>
/// </list>
/// Strings of the EXE are <see cref="StringOrigin.Known"/> when a pointer table the program reads points at them (or the offset is
/// one the program cites), otherwise they come from a conservative scan for printable runs (<see cref="StringOrigin.Scanned"/>).
/// Not covered: strings in the overlay code segments (<c>exe:Rnnn+xxxx</c>), whose locations Content does not know, the data
/// before link frame 290E, and DARKLAND.MSG (not read by Content).
/// </remarks>
public static class OriginalTextExtractor
{
    private const int FarSegment = 0x290E;
    private const int MinScanLength = 4;
    private const int MaxScanLength = 400;

    // DS offsets the program reads text from (battle texts, results, ...); a string starting there counts as known.
    private static readonly int[] KnownDataSegmentOffsets =
    [
        0x0FD8, 0x06AA, 0x06BA, 0x06CC, 0x06DE, 0x06F0, 0x0726, 0x4FCE, 0x23B2,
        0x064A, 0x074A, 0x0788, 0x0768, 0x1776, 0x04C8, 0x04F8, 0x0518, 0x0540, 0x055A, 0x0570, 0x0582, 0x0590, 0x0644, 0x1BCA,
        0x05B2, 0x05C6, 0x05DC, 0x05F2, 0x060C, 0x0628,
        0x2707, 0x2715, 0x2725, 0x3FED, 0x26BE, 0x26CD, // the rules' messages (Darklands.Core.GameMessageIds)
    ];

    // Far-pointer tables of 290E the program reads: (offset, count).
    private static readonly (int Offset, int Count)[] KnownPointerTables =
    [
        (0x1DD3, 3), (0x1DDF, 0x41), (0x20AB, 6), (0x1EE7, 34),
        (0x235F, 0x6E), (0x2517, 0x5A), (0x267F, 0x93), (0x28CB, 0x11),
        (0x1FA7, 16), (0x1FE7, 38), (0x2107, 31),
    ];

    public static StringTable Extract(GameFiles files)
    {
        var table = new StringTable();
        AddMessages(files, table);
        AddRecords(files, table);
        AddBanner(files, table);
        AddExecutable(files, table);
        return table;
    }

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static void AddIfText(StringTable table, string key, string text)
    {
        if (text.Length > 0)
        {
            table.Add(key, StringOrigin.Known, text);
        }
    }

    private static void AddMessages(GameFiles files, StringTable table)
    {
        if (!files.Exists("MSGFILES"))
        {
            return;
        }
        var archive = new MsgFilesArchiveDecoder().Decode(files.Read("MSGFILES"));
        foreach (var entry in archive.Entries)
        {
            var name = entry.Name.TrimEnd('\0', ' ');
            if (name.EndsWith(".MSG", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^4];
            }
            var document = archive.ParseDocument(entry.Name);
            foreach (var card in document.Cards)
            {
                table.Add($"msg:{name}#{N(card.Index)}", StringOrigin.Known, card.RawTextBytes);
            }
        }
    }

    private static void AddRecords(GameFiles files, StringTable table)
    {
        if (files.Exists("DARKLAND.LST"))
        {
            var list = DarklandListDecoder.DecodeBytes(files.Read("DARKLAND.LST"), "DARKLAND.LST");
            foreach (var item in list.Items)
            {
                AddIfText(table, $"lst:item/{N(item.Code)}.name", item.Name);
                AddIfText(table, $"lst:item/{N(item.Code)}.short", item.ShortName);
            }
            foreach (var saint in list.Saints)
            {
                AddIfText(table, $"lst:saint/{N(saint.Index)}.name", saint.FullName);
                AddIfText(table, $"lst:saint/{N(saint.Index)}.short", saint.ShortName);
            }
            foreach (var formula in list.Formulae)
            {
                AddIfText(table, $"lst:formula/{N(formula.Index)}.name", formula.FullName);
                AddIfText(table, $"lst:formula/{N(formula.Index)}.short", formula.ShortName);
            }
        }
        if (files.Exists("DARKLAND.SNT"))
        {
            foreach (var d in SaintsDecoder.DecodeBytes(files.Read("DARKLAND.SNT"), "DARKLAND.SNT").Descriptions)
            {
                AddIfText(table, $"snt:saint/{N(d.Index)}.desc", d.Text);
            }
        }
        if (files.Exists("DARKLAND.ALC"))
        {
            foreach (var f in AlchemyDecoder.DecodeBytes(files.Read("DARKLAND.ALC"), "DARKLAND.ALC").Formulas)
            {
                AddIfText(table, $"alc:{N(f.Index)}.desc", f.Description);
            }
        }
        if (files.Exists("DARKLAND.DSC"))
        {
            foreach (var d in new DarklandDescriptionsDecoder().Decode(files.Read("DARKLAND.DSC"), "DARKLAND.DSC").Records)
            {
                AddIfText(table, $"dsc:{N(d.Index)}.desc", d.Text);
            }
        }
        if (files.Exists("DARKLAND.CTY"))
        {
            foreach (var c in new DarklandCitiesDecoder().Decode(files.Read("DARKLAND.CTY"), "DARKLAND.CTY").Cities)
            {
                var p = $"cty:{N(c.Index)}.";
                AddIfText(table, p + "short", c.ShortName);
                AddIfText(table, p + "full", c.FullName);
                AddIfText(table, p + "leader", c.LeaderName);
                AddIfText(table, p + "ruler", c.RulerName);
                AddIfText(table, p + "political-center", c.PoliticalCenterName);
                AddIfText(table, p + "town-hall", c.TownHallName);
                AddIfText(table, p + "fortress", c.FortressName);
                AddIfText(table, p + "cathedral", c.CathedralName);
                AddIfText(table, p + "church", c.ChurchName);
                AddIfText(table, p + "market", c.MarketName);
                AddIfText(table, p + "imperial-mint", c.ImperialMintName);
                AddIfText(table, p + "slum", c.SlumName);
                AddIfText(table, p + "barracks", c.CityBarracksName);
                AddIfText(table, p + "pawnshop", c.PawnshopName);
                AddIfText(table, p + "monastery", c.MonasteryName);
                AddIfText(table, p + "inn", c.InnName);
                AddIfText(table, p + "university", c.UniversityName);
            }
        }
        if (files.Exists("DARKLAND.ENM"))
        {
            var enemies = EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM"));
            foreach (var t in enemies.Templates)
            {
                AddIfText(table, $"enm:template/{N(t.Index)}.name", t.Name);
                AddIfText(table, $"enm:template/{N(t.Index)}.image", t.Image);
            }
            foreach (var t in enemies.Types)
            {
                AddIfText(table, $"enm:type/{N(t.Index)}.name", t.Name);
            }
        }
    }

    private static void AddBanner(GameFiles files, StringTable table)
    {
        if (!files.Exists(BannerDecoder.DefaultResourceName))
        {
            return;
        }
        var banner = new BannerDecoder().Decode(files.Read(BannerDecoder.DefaultResourceName));
        foreach (var entry in banner.Entries)
        {
            table.Add($"ban:{N(entry.Index)}", StringOrigin.Known, entry.Bytes.ToArray());
        }
    }

    private static void AddExecutable(GameFiles files, StringTable table)
    {
        var exeBytes = files.Read("DARKLAND.EXE");
        var data = ProgramData.Load(exeBytes, FarSegment);
        var exe = DarklandsExecutable.Decode(exeBytes);

        // far segment 290E: known = targets of the pointer tables the program reads
        var known = new HashSet<int>();
        foreach (var (offset, count) in KnownPointerTables)
        {
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var target = (ushort)data.Int16(FarSegment, offset + 4 * i);
                    var segment = (ushort)data.Int16(FarSegment, offset + 4 * i + 2);
                    if (segment == FarSegment)
                    {
                        known.Add(target);
                    }
                }
                catch (InvalidDataException)
                {
                    // a table running past the data image has nothing to point at
                }
            }
        }
        var farLimit = (DarklandsExecutable.DataGroup - FarSegment) * 16;
        foreach (var (at, bytes) in ScanRuns(offset => TryFar(data, offset), farLimit, known))
        {
            table.Add($"exe:{FarSegment:X4}:{at:X4}", known.Contains(at) ? StringOrigin.Known : StringOrigin.Scanned, bytes);
        }

        var knownDs = new HashSet<int>(KnownDataSegmentOffsets);
        foreach (var (at, bytes) in ScanRuns(offset => TryDs(exe, offset), 0x10000, knownDs))
        {
            table.Add($"exe:DS:{at:X4}", knownDs.Contains(at) ? StringOrigin.Known : StringOrigin.Scanned, bytes);
        }
    }

    private static byte? TryFar(ProgramData data, int offset)
    {
        try
        {
            return data.Bytes(FarSegment, offset, 1)[0];
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static byte? TryDs(DarklandsExecutable exe, int offset)
    {
        try
        {
            return exe.DataSegment(offset, 1)[0];
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Zero-terminated runs that start at the region's start or right after a zero byte. A run is kept when it is
    /// printable ASCII (plus colour and umlaut codes below 0x20) and long enough, or when it starts at a forced offset.
    /// </summary>
    private static List<(int At, byte[] Bytes)> ScanRuns(Func<int, byte?> read, int limit, HashSet<int> forced)
    {
        var found = new List<(int, byte[])>();
        var offset = 0;
        while (offset < limit)
        {
            var start = offset;
            var run = new List<byte>();
            byte? b = null;
            while (offset < limit && (b = read(offset)) is { } value && value != 0 && run.Count <= MaxScanLength)
            {
                run.Add(value);
                offset++;
            }
            var terminated = offset < limit && b == 0;
            offset++; // past the zero
            if (run.Count == 0 || !terminated)
            {
                continue;
            }
            if (forced.Contains(start) || LooksLikeText(run))
            {
                found.Add((start, [.. run]));
            }
        }
        return found;
    }

    private static bool LooksLikeText(List<byte> run)
    {
        if (run.Count < MinScanLength || run.Count > MaxScanLength)
        {
            return false;
        }
        int ascii = 0, letters = 0;
        foreach (var b in run)
        {
            if (b is >= 0x20 and <= 0x7E)
            {
                ascii++;
                if (char.IsAsciiLetter((char)b))
                {
                    letters++;
                }
            }
            else if (b > 0x1F)
            {
                return false; // 0x7F and above: binary, not game text
            }
        }
        return letters >= MinScanLength && ascii * 5 >= run.Count * 4;
    }
}
