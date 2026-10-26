using System.Text;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Cards;

/// <summary>
/// Replaces the $names in card texts by their values: places, party members, money, city
/// buildings, pronouns and so on. A name the game does not know stays as written.
/// </summary>
// orig: text_expand R043+0x118E (150B:06FE) + var_expand R043+0x1316 (150B:0886) + var_index R043+0x1B8C (150B:10FC) draft (06fe and 10fc match the bytes; 0886 does not: its header notes 2174 against 2166 bytes; the file is 22/28 in the byte-match summary); tests: TextAndOpeningTests
// orig: the name table is var_names = R136+0xA88F, 73 far strings (PlaceName = 0 ... LastCastleLocation = 72); the value tables are R136+0xA3D3 (months), R136+0xA813 (standing words), R136+0xA773 (directions), R136+0xA9B3 and R136+0xA9CB (lord titles), R136+0xB1EB (support words) and bell_names; data, strings checked against the string pool of R136 seg 290E
public static class TextVariables
{
    /// <summary>The names of the eight canonical-hour bands (TimeOfDay.Band).</summary>
    public static readonly string[] BellNames = ["Latins", "Prime", "Terce", "Sexts", "Nones", "Vespers", "Compline", "Matins"];

    /// <summary>The month names (the table the prayer screen and $MonthName use).</summary>
    public static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly string[] Ordinals = ["One", "Two", "Three", "Four", "Five"];

    // orig: R136+0xA813, indexed by loc_query(cur_loc, 1) (the standing band 0..5)
    private static readonly string[] StandingWords = ["a local hero", "respected", "unknown", "suspected", "wanted", "hunted"];

    // orig: R136+0xA773, indexed by the result of R030+0x29FA (0 north, clockwise)
    // orig: R136+0xA9B3 (city type 1: f56 % 6) and R136+0xA9CB (city type 2: f56 % 9); the bytes are the game's own: 0x1F is its a-umlaut, { o-umlaut, | u-umlaut
    private static readonly string[] LordTitlesType1 = ["Vogt", "Erbvogt", "Obervogt", "Burggraf", "Richter", "Landhofmeister"];

    private static readonly string[] LordTitlesType2 =
    [
        "alte Herr", "\u001fltere Herren", "Frager", "Losunger", "alte Losunger", "Oberste Hauptm\u001fnn", "Schultheiss", "Sch{ff", "B|rgermeister",
    ];

    // orig: R136+0xB1EB, indexed by var_support0..6 (the pointer table was read in DARKLAND.EXE, tests: PersonalNamesTests)
    // C: R136+0xB1EB is the middle entry of the three far pointers at 290E:2B57 (-1 "oppose" 2B57, 0 "are undecided about" 2B5B, 1 "support" 2B5F): the values are the sides -1/0/1 of the guilds, so the word index is value + 1.
    private static readonly string[] SupportWords = ["oppose", "are undecided about", "support"];

    // orig: city_rec->names[n] at +0x6E + 32n: 0 lord, 1 overlord, 3 city square, 4 council hall, 5 fortress, 6 cathedral, 7 church, 8 market, 9 mint, 10 slum, 11 barracks, 12 pawnshop, 13 monastery, 14 inn, 15 university
    private const int NamesOffset = 0x6E;

    private const int NameSize = 0x20;

    /// <summary>Icons of the "nearest ..." places: a monastery and the last castle (<c>nearest_loc_type</c> arguments 0x16 and 0x17).</summary>
    private const int MonasteryIcon = 0x16;

    private const int LastCastleIcon = 0x17;

    // orig: the copy loop of text_expand (R043+0x118E), see the C: notes
    // C: the original copies the block into buf[900] and collects a name in var[200], with no bounds check; here both grow as needed (a longer block or name overflows its buffer in the original).
    // C: a '$' starts a name even inside one: the name collected so far is dropped without a trace ("$a$b" gives the value of b only), and a '$' outside a name is not copied.
    // C: a name ends at the first byte that is not an ASCII letter or digit; that byte is copied after the value. A name that runs to the end of the block is expanded there, with nothing after it.
    // C: a name the game does not know (and the five it has no case for: docks, poorhouse, whorehouse, warehouse, MapLocation) comes out as "$" + name, an empty name (a lone '$') as "$".
    public static byte[] Expand(byte[] text, CardContext c)
    {
        var result = new List<byte>(text.Length + 64);
        var name = new List<byte>();
        var inName = false;
        foreach (var b in text)
        {
            if (b == 0)
            {
                break;   // C: the block is a zero-terminated string
            }
            if (b == '$')
            {
                name.Clear();
                inName = true;
            }
            else if (inName && !char.IsAsciiLetterOrDigit((char)b))
            {
                inName = false;
                result.AddRange(Evaluate(name, c));
            }
            if (inName && b != '$')
            {
                name.Add(b);
            }
            else if (!inName)
            {
                result.Add(b);
            }
        }
        if (inName)
        {
            result.AddRange(Evaluate(name, c));
        }
        return [.. result];
    }

    private static byte[] Evaluate(List<byte> name, CardContext c)
    {
        var text = Encoding.Latin1.GetString([.. name]);
        return Encoding.Latin1.GetBytes(Value(text, c) ?? "$" + text);
    }

    /// <summary>The value of one variable, or null when it is not one the game knows.</summary>
    // orig: var_expand R043+0x1316 (150B:0886) draft (its switch on the variable index)
    // C: the cases below are in the original's order of index; each reads its source unchecked, a few are guarded here (the guards for an index outside its table carry their own DIVERGENCE note below; not yet registered: Names
    //   gives "" for a name index past the city record's 16 names, where the original reads the memory after the record, and PlaceName "" for a place past the
    //   location table).
    public static string? Value(string name, CardContext c)
    {
        var state = c.State;
        var party = state.Party;
        var city = state.City;   // C: city_rec, the last loaded city record even while the party is elsewhere
        var here = state.CurrentLocation;

        for (var k = 0; k < Ordinals.Length; k++)
        {
            if (name == $"Chosen{Ordinals[k]}Name")
            {
                // C: strcpy from party[var_chosenK] + 0x2A whatever the slot's status: an empty slot still holds its last
                // C: occupant's name in this run (Party.Slot keeps it), a never-used slot is zero ("").
                // C: an index outside 0..4 (var_chosenK is never reset between cards; NightInTheGuildLeadersHouse puts an Agility value there)
                // C: reads past the party: from slot 25 on party[k].name (DS:9C93 + 0x80k) lies in msg_file, the loaded .MSG (OutsideName).
                // DIVERGENCE: elsewhere (the enemy combatants of the last battle, the globals after them, the stale tail of msg_file's
                //   6000 bytes past the file, the word DS:A893 before it) the original reads memory that is not game state; "" here.
                var chosen = c.Variables.Chosen[k];
                return Member(c, chosen)?.ShortName ?? OutsideName(c, chosen) ?? "";
            }
            if (name == $"Named{Ordinals[k]}Name")
            {
                return c.Variables.Named[k];
            }
            if (name == $"Money{k + 1}")
            {
                return c.Variables.Money[k].ToString();
            }
        }

        // C: "he/she" and the other pronouns look at the sex byte of party[var_chosen1] (0 male, any other value female).
        // C: past the party the sex byte (party[k] + 3) is read the same way (OutsideByte); elsewhere taken as male (see ChosenOneName).
        var female = Member(c, c.Variables.Chosen[0]) is { } chosenOne ? chosenOne.IsFemale : OutsideByte(c, c.Variables.Chosen[0], SexAt) is > 0;
        return name switch
        {
            "PlaceName" => PlaceName(c),
            "PlaceDesc" => PlaceDescription(c),
            "Inn" => Names(city, 14),
            "He" => female ? "She" : "He",
            "he" => female ? "she" : "he",
            "his" => female ? "her" : "his",
            "His" => female ? "Her" : "His",
            "him" => female ? "her" : "him",
            "himself" => female ? "herself" : "himself",
            "LeaderName" => party.Members.Count > 0 ? party.Leader.ShortName : "",   // DIVERGENCE: no party gives "" (the original reads party[leader])
            "PlaceAttitude" => Standing(c),
            "Number1" => c.Variables.Numbers[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Number2" => c.Variables.Numbers[1].ToString(System.Globalization.CultureInfo.InvariantCulture),
            "citySquare" => Names(city, 3),
            "councilHall" => Names(city, 4),
            "imperialMint" => Names(city, 9),
            "cityBarracks" => Names(city, 11),
            "university" => Names(city, 15),
            "marketplace" => Names(city, 8),
            "fortress" => Names(city, 5),
            "pawnshop" => Names(city, 12),
            "hospital" or "slum" => Names(city, 10),   // C: both names read the same field
            "monastery" => Names(city, 13),
            "cathedral" => Names(city, 6),
            "cityChurch" => Names(city, 7),
            "CityLordName" => Names(city, 0),
            "CityLordTitle" => LordTitle(c, name),
            "CityLocation" => Names(city, c.Variables.CityLocation),
            "LocName" => LocationName(c, c.Variables.Location),
            "ItemName" => c.World.ItemName(c.Variables.Item),   // DIVERGENCE: an item code the list lacks gives "item N" (the original indexes item_defs unchecked)
            "NearestCity" => NearestCityName(c),
            "Direction" => DirectionFromHere(c),
            "Direction2" => DirectionBetweenLocations(c),
            "Support" => Support(c, 0),
            "Support1" => Support(c, 1),
            "Support2" => Support(c, 2),
            "Support3" => Support(c, 3),
            "Support4" => Support(c, 4),
            "Support5" => Support(c, 5),
            "Support6" => Support(c, 6),
            "Government" => LocationRules.Query(state, here, 0x18) != 0 ? Names(city, 4) : Names(city, 5),
            "CurrentBell" => BellNames[TimeOfDay.Band(state.Date.Hour)],
            "MonthName" => MonthNames[GameDate.Mod12(state.Date.Month)],   // DIVERGENCE: a month outside 0..11 wraps (the original reads past its table)
            "SabbatTime" => SabbatTime(c),
            "SabbatLocation" => SabbatLocation(c),
            "LocName2" => LocationName(c, c.Variables.Location2),
            "MonasteryLocation" => PlaceRelativeToCity(c, MonasteryIcon),
            "LastCastleLocation" => PlaceRelativeToCity(c, LastCastleIcon),
            "Text1" => c.Variables.Texts[0],
            "Text2" => c.Variables.Texts[1],
            "Text3" => c.Variables.Texts[2],
            "Text4" => c.Variables.Texts[3],
            _ => null,
        };
    }

    // C: the five 0x80-byte slots of party[] (DS:9C69), read whatever their status.
    private static Core.Characters.Character? Member(CardContext c, int index) =>
        index >= 0 && index < Core.Characters.Party.MaxMembers ? c.State.Party.Slot(index) : null;

    // orig: party DS:9C69 (0x80 bytes a slot: +3 sex, +0x2A name), msg_file DS:A895
    private const int PartyAt = 0x9C69;
    private const int SlotSize = 0x80;
    private const int SexAt = 0x03;
    private const int NameAt = 0x2A;
    private const int MessageFileAt = 0xA895;

    /// <summary>
    /// The offset into the loaded .MSG file that field <paramref name="field"/> of party slot <paramref name="index"/> (past the five) is at,
    /// or −1 when it is not inside the file (16-bit offsets in DS).
    /// </summary>
    private static int OutsideOffset(CardContext c, int index, int field)
    {
        var at = ((PartyAt + SlotSize * index + field) & 0xFFFF) - MessageFileAt;
        return c.MessageFileBytes is { } file && at >= 0 && at < file.Length ? at : -1;
    }

    /// <summary>One byte of a party slot past the five that lies in the loaded .MSG file, or null.</summary>
    private static int? OutsideByte(CardContext c, int index, int field) =>
        OutsideOffset(c, index, field) is var at and >= 0 ? c.MessageFileBytes![at] : null;

    /// <summary>The name (strcpy to the zero byte) of a party slot past the five that lies in the loaded .MSG file, or null.</summary>
    // C: every block of the file ends with a zero, so a name that starts in the file ends in it; it is copied into var[200] unchecked
    // C: (a longer one overruns text_expand's stack; here it is copied whole).
    private static string? OutsideName(CardContext c, int index)
    {
        var at = OutsideOffset(c, index, NameAt);
        if (at < 0 || Member(c, index) is not null)
        {
            return null;
        }
        var file = c.MessageFileBytes!;
        var end = Array.IndexOf(file, (byte)0, at);
        return Encoding.Latin1.GetString(file, at, (end < 0 ? file.Length : end) - at);
    }

    // orig: case 54 / 70 of R043+0x1316: locations[var_loc]->name
    // DIVERGENCE: an index outside the 414 locations gives "" (the original reads whatever the far table holds).
    private static string LocationName(CardContext c, int index) =>
        index >= 0 && index < c.State.Locations.Count ? c.State.Locations[index].Name : "";

    // orig: case 0 of R043+0x1316
    // C: cur_loc < 0x5c is a signed compare: the wilderness (-1) takes the city branch and names the last loaded city record, not a place.
    private static string PlaceName(CardContext c)
    {
        var here = c.State.CurrentLocation;
        if (here < Location.CityCount)
        {
            return Names(c.State.City, -1);
        }
        return here < c.State.Locations.Count ? c.State.Locations[here].Name : "";
    }

    // orig: case 1: strcpy(name, city_desc); city_desc is the DARKLAND.DSC record of the city loaded last (read together with the city record when the party arrives in a city)
    // UNCERTAIN: the description is looked up by the working city record's number; the original keeps city_desc in its own buffer, which only the overland travel and game loading refill.
    private static string PlaceDescription(CardContext c) => c.World.CityDescription(c.State.City.City);

    // orig: case 16: R136+0xA813[loc_query(cur_loc, 1)]
    // DIVERGENCE: in the wilderness (no location) the original indexes locations[-1]; here the standing band of an unknown place, 2, is used ("unknown").
    private static string Standing(CardContext c)
    {
        var here = c.State.CurrentLocation;
        var band = here >= 0 && here < c.State.Locations.Count ? LocationRules.Query(c.State, here, 1) : 2;
        return StandingWords[Math.Clamp(band, 0, StandingWords.Length - 1)];
    }

    // orig: case 51: the lord's title depends on the city's type word (swapped after an uprising, flag 0x80)
    // C: f56 (the city's ordinal 0..91) is never negative, so the signed % of the original needs no care here.
    // C: a type word other than 0, 1 or 2 changes nothing in the name buffer, so the text shows the bare name "CityLordTitle" (no $).
    private static string LordTitle(CardContext c, string name)
    {
        var city = c.State.City;
        var here = c.State.Here;
        var risen = here is not null && (here.Flags & Location.UprisingFlag) != 0;   // DIVERGENCE: no location counts as not risen (the original reads locations[-1])
        var kind = risen ? city.OtherCityType : city.CityType;
        return kind switch
        {
            0 => Names(city, risen ? 1 : 0),
            1 => LordTitlesType1[city.Ordinal % LordTitlesType1.Length],
            2 => LordTitlesType2[city.Ordinal % LordTitlesType2.Length],
            _ => name,
        };
    }

    // orig: case 59..65: R136+0xB1EB[var_support0..6]
    // DIVERGENCE: a value outside -1..1 gives "" (the original indexes its table unchecked and copies whatever string the neighbouring pointer gives).
    private static string Support(CardContext c, int index)
    {
        var value = c.Variables.Support[index] + 1;
        return value >= 0 && value < SupportWords.Length ? SupportWords[value] : "";
    }

    // orig: case 56: with a location chosen (cur_loc != -1) the city nearest to location var_loc, in the wilderness the one nearest to the party
    // C: the test is on cur_loc but the place measured from is var_loc.
    private static string NearestCityName(CardContext c)
    {
        var state = c.State;
        int city;
        if (state.CurrentLocation != GameState.Wilderness && c.Variables.Location >= 0 && c.Variables.Location < state.Locations.Count)
        {
            var place = state.Locations[c.Variables.Location];
            city = LocationRules.NearestCity(state, place.X, place.Y);
        }
        else
        {
            city = LocationRules.NearestCity(state, state.MapPosition.X, state.MapPosition.Y);
        }
        return LocationName(c, city);
    }

    // orig: case 57: R136+0xA773[thunk 09C0:1FB3(var_loc)] = the direction from the current location to var_loc (R030+0x29BC)
    // C: thunk 09C0:1FB3 is R030+0x29BC (the direction from cur_loc to a location) and thunk 09C0:1FBD is R030+0x29FA (the direction between two points): both resolve through the atlas thunk table (vec_XXXX = thunk (XXXX - 0x17E3) / 10).
    private static string DirectionFromHere(CardContext c)
    {
        var state = c.State;
        if (state.Here is not { } from || c.Variables.Location < 0 || c.Variables.Location >= state.Locations.Count)
        {
            return "";   // DIVERGENCE: the original indexes locations[cur_loc] and locations[var_loc] unchecked
        }
        var to = state.Locations[c.Variables.Location];
        return PartyStanding.Directions[PartyStanding.Direction(from.X, from.Y, to.X, to.Y)];
    }

    // orig: case 58: R136+0xA773[thunk 09C0:1FBD(loc2.x, loc2.y, loc.x, loc.y)] = the direction from var_loc2 to var_loc
    private static string DirectionBetweenLocations(CardContext c)
    {
        var state = c.State;
        var (a, b) = (c.Variables.Location2, c.Variables.Location);
        if (a < 0 || a >= state.Locations.Count || b < 0 || b >= state.Locations.Count)
        {
            return "";   // DIVERGENCE: unchecked indexes in the original
        }
        return PartyStanding.Directions[PartyStanding.Direction(state.Locations[a].X, state.Locations[a].Y, state.Locations[b].X, state.Locations[b].Y)];
    }

    // orig: case 68: month name, a space and the day of the next witches' sabbat (thunk 09C0:20DF, thunk 09C0:21A7)
    private static string SabbatTime(CardContext c) =>
        MonthNames[GameDate.Mod12(Quests.SabbatMonth(c.State))] + " " + Quests.SabbatDayOfMonth(c.State).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // orig: case 69 of R043+0x1316: "<direction> of <nearest city>" for the site of the next sabbat (20fd, Quests.SabbatSite)
    private static string SabbatLocation(CardContext c) => RelativeToNearestCity(c, Quests.SabbatSite(c.State));

    // orig: case 71 / 72: the nearest monastery / last castle (nearest_loc_type from the party's map position), "<direction> of <nearest city>"
    private static string PlaceRelativeToCity(CardContext c, int icon)
    {
        var state = c.State;
        return RelativeToNearestCity(c, LocationRules.NearestOfType(state, state.MapPosition.X, state.MapPosition.Y, icon));
    }

    // C: the direction is measured from the city to the site (R030+0x29FA(city, site)); the city named is the one nearest to the site.
    private static string RelativeToNearestCity(CardContext c, int site)
    {
        var state = c.State;
        if (site < 0 || site >= state.Locations.Count)
        {
            return "";   // DIVERGENCE: the original indexes locations[-1] when there is no such place
        }
        var place = state.Locations[site];
        var city = LocationRules.NearestCity(state, place.X, place.Y);
        if (city < 0)
        {
            return "";
        }
        var from = state.Locations[city];
        return PartyStanding.Directions[PartyStanding.Direction(from.X, from.Y, place.X, place.Y)] + " of " + from.Name;
    }

    /// <summary>A name of the working city record (city_rec->names[n]); n = -1 is the short name at the start of the record.</summary>
    // C: _fstrcpy copies up to the first zero byte: a 32-byte field without one runs on into the next field. An index outside the 16 names reads other parts of the record, or "" past its end.
    private static string Names(Core.World.CityRecord city, int index)
    {
        var start = index < 0 ? 0 : NamesOffset + index * NameSize;
        var bytes = city.ToArray();
        if (start < 0 || start >= bytes.Length)
        {
            return "";
        }
        var end = start;
        while (end < bytes.Length && bytes[end] != 0)
        {
            end++;
        }
        return Encoding.Latin1.GetString(bytes, start, end - start);
    }
}
