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
/// <item><c>loc:n.name</c> (DARKLAND.LOC, the place names).</item>
/// <item><c>enm:template/n.name</c>, <c>enm:template/n.image</c>, <c>enm:type/n.name</c> (DARKLAND.ENM).</item>
/// <item><c>ban:n</c> entry n of BANNER.DAT (code page 437 bytes, held one byte per character like the other text).</item>
/// <item><c>exe:DS:XXXX</c> the zero-terminated string at offset XXXX (4 uppercase hex digits) of DGROUP; <c>exe:290E:XXXX</c>
/// the same in the far data segment at link frame 290E (where the creation, name and load-screen tables point);
/// <c>exe:286B:XXXX</c> the strings of the battle's far segment the battle reads (its type names, ordinals and notices).</item>
/// </list>
/// Strings of the EXE are <see cref="StringOrigin.Known"/> when a pointer table the program reads points at them (or the offset is
/// one the program cites), otherwise they come from a conservative scan for printable runs (<see cref="StringOrigin.Scanned"/>).
/// Not covered: strings in the overlay code segments (<c>exe:Rnnn+xxxx</c>), whose locations Content does not know, the data
/// before link frame 290E, and DARKLAND.MSG (not read by Content).
/// </remarks>
public static class OriginalTextExtractor
{
    private const int FarSegment = 0x290E;

    // the battle's far segment (link frame 286B): its pointer tables (offset, most entries; a table ends at its first pointer
    //   elsewhere) and the strings it names directly (BattleScreens.FarText: the "cannot view" texts, the marching order notice)
    private const int BattleSegment = 0x286B;

    private static readonly (int Offset, int Count)[] BattlePointerTables =
    [
        (0x01CF, 10),   // the shield types (the enemy panel, BattleScreen TypeName)
        (0x01F7, 28),   // the armour types
        (0x0267, 4),    // the ordinals of "Select %Fs" (R022 051C)
        (0x0277, 98),   // the weapon types
    ];

    private static readonly int[] BattleStrings = [0x0212, 0x0222, 0x0231];

    private static readonly int[] BattlePlaceNotePointers = [0x01A8, 0x01AA, 0x01AC, 0x01AE, 0x01B0];
    private const int MinScanLength = 4;
    private const int MaxScanLength = 400;

    // DS offsets the program reads text from (battle texts, results, ...); a string starting there counts as known.
    private static readonly int[] KnownDataSegmentOffsets =
    [
        0x0FD8, 0x06AA, 0x06BA, 0x06CC, 0x06DE, 0x06F0, 0x0726, 0x4FCE, 0x23B2, 0x067C, 0x0690,
        0x064A, 0x074A, 0x0788, 0x0768, 0x1776, 0x04C8, 0x04F8, 0x0518, 0x0540, 0x055A, 0x0570, 0x0582, 0x0590, 0x0644, 0x1BCA,
        0x05B2, 0x05C6, 0x05DC, 0x05F2, 0x060C, 0x0628,
        // the word lists of OriginalWords: town sizes (R034+0x0EE6, R034+0x144C), risk words (R033+0x094A), camp words (R036+0x08EC)
        0x362C, 0x3638, 0x3647, 0x371F, 0x372B, 0x373A, 0x32FE, 0x3302, 0x3309, 0x3D5C, 0x3D63, 0x3D65,
        // the Quickstart names (R001+0x0000) and the card scripts' own words they put into texts or name shops by (CardWordsTests)
        0x0E50, 0x0E61, 0x0E68, 0x0E77, 0x0E7F, 0x0E8B, 0x0E90, 0x0EA2,
        0x586E, 0x629F, 0x58A4, 0x5C27, 0x5A57, 0x5FCF, 0x58BD, 0x58C2, 0x58C9, 0x58CE, 0x58D8, 0x5D9F, 0x5DA5, 0x5DAE, 0x5DBD,
        0x5C5D, 0x5CC3, 0x5CCB, 0x652F, 0x653A, 0x6549, 0x6511, 0x6709, 0x5B8F, 0x5BBD,
        0x68AF, 0x68B6, 0x68BB, 0x68C0, 0x68C7, 0x68CD, 0x68D9, 0x68E0, 0x68E5, 0x68EA, 0x68F1, 0x68F7,
        // the messages of OriginalMessageText: " changed from " / " to " (R029+0x02EA), the notices (R029, R036)
        0x26BE, 0x26CD, 0x2707, 0x2715, 0x2725, 0x3FED,
        // the card hints (each card function loads its own copy: mov si / di, offset): CardTexts and the scripts' hints
        0x4EC4, 0x4ED0, 0x4EEA, 0x4F02, 0x4F0C, 0x4F2A, 0x4F48, 0x4F5C, 0x4F7A, 0x4F9A, 0x4FC0, 0x574B, 0x57F3, 0x5AF3, 0x5C89, 0x5F0F,
        0x5F18, 0x5F59, 0x6055, 0x60A9, 0x6131, 0x6237, 0x6249, 0x6265, 0x6273,
        // the chance texts: chance-line R029+0x5B62 (DS:27CD, DS:27E5), the Shift hint R043+0x27E6 (DS:5284, DS:52A0, DS:52A9),
        //   grade-words DS:4FCE (the seven words)
        0x27CD, 0x27E5, 0x5284, 0x52A0, 0x52A9, 0x4E54, 0x4E5E, 0x4E63, 0x4E68, 0x4E6D, 0x4E77, 0x4E81,
        // the screens' texts (L-6; Presentation's ScreenTexts): the hot keys' and the switches' words (root), the pause box, the save and
        //   load screens (R037), the prayer screen (R034)
        0x0086, 0x0093, 0x00AD, 0x00BC, 0x00C3, 0x020A, 0x0216, 0x022C, 0x022F, 0x0233, 0x0236, 0x023A, 0x023D, 0x0241, 0x0247, 0x0250,
        0x4056, 0x4204, 0x47F0, 0x4815, 0x3566, 0x3579, 0x3587, 0x35A5, 0x35C3, 0x35E4, 0x35F8, 0x3614, 0x361E, 0x33EE, 0x3404, 0x3415,
        // the loot screen (R040: each drawing function's own copies)
        0x4C42, 0x4C64, 0x4C8A, 0x4CAE, 0x4CC8, 0x4CF2, 0x4D07, 0x4D1B, 0x4D2F, 0x4D54, 0x4D7E, 0x4D93, 0x4D9D, 0x4DB1, 0x4DC5, 0x4DD9,
        // the item store (R035: each drawing function's own copies)
        0x37F6, 0x3810, 0x3863, 0x3883, 0x383B, 0x388C, 0x38A2, 0x38B6, 0x38CA, 0x3917, 0x3937, 0x38EF, 0x3940, 0x3956, 0x3960, 0x3974,
        0x3988, 0x3998,
        // the alchemy screen (R033, frames 1462 and 15D1)
        0x3268, 0x327C, 0x32A3, 0x32AA, 0x32CC, 0x330E, 0x3322, 0x332F, 0x3355, 0x33B8, 0x33D5, 0x33E2,
        // the barter screen (R032: each function's own copies)
        0x2F4A, 0x2F3A, 0x2F71, 0x2FA1, 0x2FAC, 0x2FA4, 0x2FB0, 0x2FB8, 0x2FD5, 0x2FF2, 0x300F, 0x3018, 0x302A, 0x303E, 0x3055, 0x307F,
        0x30AF, 0x30BA, 0x30B2, 0x30BE, 0x30C6, 0x30E3, 0x3100, 0x311D, 0x3126, 0x3138, 0x3142, 0x3159, 0x3172, 0x31A2, 0x31D2, 0x31E9,
        // the game's menu bar (R000+0x083E): titles, items with their tick byte, key labels
        0x0CA1, 0x0CA6, 0x0CAD, 0x0CB4, 0x0CBB, 0x0CC6, 0x0CC9, 0x0CDA, 0x0CDD, 0x0CE9, 0x0CEC, 0x0CFA, 0x0CFD, 0x0D04, 0x0D07, 0x0D11,
        0x0D14, 0x0D1B, 0x0D1E, 0x0D2B, 0x0D2F, 0x0D36, 0x0D40, 0x0DEE, 0x0DF9, 0x0E0F,
        // the party information (R034, frame 1550)
        0x3653, 0x3659, 0x3746, 0x374C, 0x3752, 0x375A, 0x3765, 0x3771, 0x377E, 0x378A, 0x3795, 0x37A0, 0x3671, 0x3683, 0x3693, 0x369E,
        0x36A3, 0x36A8, 0x36B1, 0x36B8, 0x36BE, 0x36C8, 0x36DA, 0x36EA, 0x36F5, 0x36FA, 0x36FF, 0x3708, 0x370F, 0x3715,
        // the character sheet (R031: the box texts R031+0x39C6, the open panels R031+0x2B14)
        0x2D5A, 0x2D64, 0x2D69, 0x2D6E, 0x2D78, 0x2D88, 0x2D96, 0x2DA0, 0x2DB2, 0x2DBD, 0x2DC2, 0x2DD1, 0x2DD8, 0x2DE3, 0x2DE7, 0x2DEC,
        0x2DF0, 0x2DF4, 0x2DFF, 0x2E0B, 0x2E16, 0x2E21, 0x2E2B, 0x2E2F, 0x2E33, 0x2E37, 0x2E3B, 0x2E3F, 0x2E43, 0x2E46, 0x2E50, 0x2E55,
        0x2E5A, 0x2E5F, 0x2E64, 0x2E69, 0x2E6E, 0x2E73, 0x2E78, 0x2E7D, 0x2E82, 0x2E87, 0x2E8C, 0x2E91, 0x2E95, 0x2E9A, 0x2EA4, 0x2EAF,
        0x2EBA, 0x2EC4, 0x2ECF, 0x2ED4, 0x2ED9, 0x2EDE, 0x2EE3, 0x2EE8, 0x2EED, 0x2EF2, 0x2EF6, 0x2EFA, 0x2EFE, 0x2F02, 0x2F06, 0x2F0A,
        0x2F0D,
        // the character generator (R037, frame 18C8)
        0x44BC, 0x44CC, 0x44E8, 0x44FE, 0x4515, 0x4520, 0x4536, 0x454D, 0x4558, 0x4570, 0x4585, 0x459A, 0x45B2, 0x45C7, 0x45DC, 0x45F6,
        0x4610, 0x461C, 0x4636, 0x4650, 0x465C, 0x467D, 0x4681, 0x4685, 0x4689, 0x468D, 0x4691, 0x4695, 0x4699, 0x469D, 0x46AB, 0x46B0,
        0x46B5, 0x46BA, 0x46BF, 0x46C4, 0x46C9, 0x46F1, 0x46F6, 0x46FB, 0x4700, 0x4705, 0x470A, 0x4731, 0x4736, 0x473B, 0x4740, 0x4745,
        0x474A,
        // the battle menu bar (R000+0x083E's Orders and Attack tables)
        0x06A4, 0x0706, 0x0718, 0x07AA, 0x07BC, 0x0D51, 0x0D56, 0x0D58, 0x0D64, 0x0D67, 0x0D6A, 0x0D6D, 0x0D70, 0x0D73, 0x0D76, 0x0D79,
        0x0D7C, 0x0D7F, 0x0D82, 0x0D85, 0x0D88, 0x0D8B, 0x0D8E, 0x0D91, 0x0DA6, 0x0DA9, 0x0DAF, 0x0DB2, 0x0DBE, 0x0DC0, 0x0DCB, 0x0DCD,
        0x0DD5, 0x0DD7, 0x0DDD, 0x0DDF, 0x0DEB,
        // the battle bar's Resume
        0x0D49,
        // the battle results (R002), the ordinal's Select (R022), the enemy box's labels, the load texts (R037)
        0x10CB, 0x10DC, 0x10E4, 0x10EC, 0x10F3, 0x10FD, 0x1106, 0x23DC, 0x2334, 0x2345, 0x479E, 0x4825,
        // the battle menu's "(Ldr)" and "(All)" (R000+0x083E's suffixes), and the shop names cards open barter with (R049, R051, R056)
        //   (the 1.2 translations: the untranslated-text audit)
        0x073E, 0x0744, 0x587D, 0x5887, 0x5915, 0x5A8D,
        // the guild masters' "rebel" and "conservative" (R0xx GuildMastersAudience)
        0x5E47, 0x5E4D,
        // the camp (R036)
        0x3A2C, 0x3A34, 0x3A4F, 0x3A70, 0x3A7B, 0x3A90, 0x3AA4, 0x3D69, 0x3D77, 0x3D8A, 0x3DA3, 0x3DB3, 0x3DCA, 0x3DDC, 0x3DEE, 0x3E5A,
        0x3ECE, 0x3F4C, 0x3FAD, 0x3FBE, 0x3FDE, 0x3FE7,
    ];

    // Far-pointer tables of 290E the program reads: (offset, count).
    private static readonly (int Offset, int Count)[] KnownPointerTables =
    [
        // the alchemy menu's four lines (R033+0x094A)
        (0x1DC3, 4),
        (0x1DD3, 3), (0x1DDF, 0x41), (0x20AB, 6), (0x1EE7, 34),
        (0x235F, 0x6E), (0x2517, 0x5A), (0x267F, 0x93), (0x28CB, 0x11),
        (0x1FA7, 16), (0x1FE7, 38), (0x2107, 31),
        // the far tables of OriginalWords: fame titles, the shop names (from entry -3), directions, standing words, job names
        (0x1F7B, 11), (0x2073, 15), (0x20E3, 8), (0x2183, 6), (0x219B, 25),
        // months, bells, the two lord-title tables, the support words
        (0x1D43, 12), (0x20C3, 8), (0x2323, 6), (0x233B, 9), (0x2B57, 3),
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
        // the map's messages (DARKLAND.MSG: a count byte, then records of 400 bytes, each to its first zero), as the map reads them
        //   (R030+0x2BB0, OverlandScreen.ShowMapMessage); Not in the original: the 1.2 translations
        if (files.Exists("DARKLAND.MSG"))
        {
            var bytes = files.Read("DARKLAND.MSG");
            for (var n = 0; 1 + n * 400 < bytes.Length; n++)
            {
                var record = bytes.AsSpan(1 + n * 400, Math.Min(400, bytes.Length - 1 - n * 400));
                var end = record.IndexOf((byte)0);
                AddIfText(table, $"map:{N(n)}.text", Darklands.Core.GameTextCodec.Decode(end < 0 ? record : record[..end]));
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
        if (files.Exists("DARKLAND.LOC"))
        {
            // the place names (record +0x26, 20 bytes, zero-ended), as WorldData reads them
            foreach (var l in new DarklandLocationsDecoder().Decode(files.Read("DARKLAND.LOC"), "DARKLAND.LOC").Locations)
            {
                var end = 0x26;
                while (end < 0x26 + 20 && end < l.RawBytes.Length && l.RawBytes[end] != 0)
                {
                    end++;
                }
                AddIfText(table, $"loc:{N(l.Index)}.name", Darklands.Core.GameTextCodec.Decode(l.RawBytes.AsSpan(0x26, end - 0x26)));
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

        AddBattleSegment(exeBytes, table);

        var knownDs = new HashSet<int>(KnownDataSegmentOffsets);
        // the battle panel's place notes (a doorway, stairs, a chest, the exit, the field left): word pointers at DS:01A8..01B0
        foreach (var slot in BattlePlaceNotePointers)
        {
            try
            {
                var word = exe.DataSegment(slot, 2);
                knownDs.Add(word[0] | word[1] << 8);
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }
        foreach (var (at, bytes) in ScanRuns(offset => TryDs(exe, offset), 0x10000, knownDs))
        {
            table.Add($"exe:DS:{at:X4}", knownDs.Contains(at) ? StringOrigin.Known : StringOrigin.Scanned, bytes);
        }
    }

    // Not in the original: the 1.1 translations (the battle's messages and names, exe:286B:XXXX). Only the strings the battle reads.
    private static void AddBattleSegment(byte[] exeBytes, StringTable table)
    {
        ProgramData data;
        try
        {
            data = ProgramData.Load(exeBytes, BattleSegment);
        }
        catch (InvalidDataException)
        {
            return;
        }
        var targets = new SortedSet<int>(BattleStrings);
        foreach (var (offset, count) in BattlePointerTables)
        {
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var target = (ushort)data.Int16(BattleSegment, offset + 4 * i);
                    if ((ushort)data.Int16(BattleSegment, offset + 4 * i + 2) != BattleSegment)
                    {
                        break;
                    }
                    targets.Add(target);
                }
                catch (InvalidDataException)
                {
                    break;
                }
            }
        }
        foreach (var at in targets)
        {
            var bytes = new List<byte>();
            try
            {
                for (var k = 0; k < MaxScanLength; k++)
                {
                    var b = data.Bytes(BattleSegment, at + k, 1)[0];
                    if (b == 0)
                    {
                        break;
                    }
                    bytes.Add(b);
                }
            }
            catch (InvalidDataException)
            {
            }
            if (bytes.Count > 0 && table.Find($"exe:{BattleSegment:X4}:{at:X4}") is null)
            {
                table.Add($"exe:{BattleSegment:X4}:{at:X4}", StringOrigin.Known, bytes.ToArray());
            }
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
