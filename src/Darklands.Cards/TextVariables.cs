using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Rules;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Cards;

/// <summary>
/// Replaces the $names in card texts by their values: places, party members, money, city
/// buildings, pronouns and so on. A name the game does not know stays as written.
/// </summary>
// orig: text-substitute R043+0x118E (150B:06FE) + text-slot-expand R043+0x1316 (150B:0886) + text-slot-index R043+0x1B8C (150B:10FC) draft (06fe and 10fc match the bytes; 0886 does not: its header notes 2174 against 2166 bytes; the file is 22/28 in the byte-match summary); tests: TextAndOpeningTests
// orig: the name table is text-slot-name-table = R136+0xA88F, 73 far strings (PlaceName = 0 ... LastCastleLocation = 72); the value tables are R136+0xA3D3 (months), R136+0xA813 (standing words), R136+0xA773 (directions), R136+0xA9B3 and R136+0xA9CB (lord titles), R136+0xB1EB (support words) and bell-time-labels, all read from the EXE (OriginalWords)
public static class TextVariables
{
    private static readonly string[] Ordinals = ["One", "Two", "Three", "Four", "Five"];

    // orig: city-record->names[n] at +0x6E + 32n: 0 lord, 1 overlord, 3 city square, 4 council hall, 5 fortress, 6 cathedral, 7 church, 8 market, 9 mint, 10 slum, 11 barracks, 12 pawnshop, 13 monastery, 14 inn, 15 university
    private const int NamesOffset = 0x6E;

    private const int NameSize = 0x20;

    /// <summary>Icons of the "nearest ..." places: a monastery and the last castle (<c>closest-place-of-kind</c> arguments 0x16 and 0x17).</summary>
    private const int MonasteryIcon = 0x16;

    private const int LastCastleIcon = 0x17;

    // orig: the copy loop of text-substitute (R043+0x118E), see the C: notes
    // C: the original copies the block into buf[900] and collects a name in var[200], with no bounds check; here both grow as needed (a longer block or name overflows its buffer in the original).
    // C: the frame (checked against the asm): R043+0x1191 `mov ax, 0x458` + the stack probe give 0x458 bytes of locals; the block is
    //   strcpy'd to [bp-0x458] (R043+0x11A9..0x11AE), var[] is at [bp-0xD4], the counters at [bp-0xC]..[bp-1], then the saved BP and the
    //   far return address. So buf holds 899 bytes and its zero; byte 900 on runs into var[], byte 1100 on into the counters (set after the
    //   copy, so they overwrite the text), byte 1112 on into the saved BP and from 1114 the return address. The expanded text goes to
    //   text-bank-buffer DS:9085 (both callers, R043+0x0F71 and R043+0x1F24, push 0x9085), which is also 900 bytes: the next global is the
    //   menu bar's drawn flag DS:9409 (0x9409 - 0x9085 = 0x384). The safe limit for a block is therefore 899 bytes before and after the $names.
    // DORMANT: no shipped block comes near either limit: the longest of the 3756 blocks of the 419 .MSG files of MSGFILES is 599 bytes
    //   (twelve have 599, none more: the texts were written to a 600-byte limit), and with 32 bytes for each $name's value (80 for the
    //   phrase ones: SabbatLocation, MonasteryLocation, LastCastleLocation, PlaceDesc, Direction2, Text1..4) the longest expansion is 878
    //   ($Consi00 block 3). A longer block, possible only in a mod, is expanded and drawn whole here.
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

    /// <summary>
    /// <see cref="Expand(byte[], CardContext)"/> on codes (the Unicode text path, <see cref="TextCodes"/>): the same loop, a name's value
    /// as codes (its letters kept, where the bytes would turn one the game cannot hold into '?').
    /// </summary>
    // Not in the original: the 1.1 translations (L-6b).
    public static int[] Expand(int[] text, CardContext c)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<int>(text.Length + 64);
        var name = new System.Text.StringBuilder();
        var inName = false;
        int[] Value() => TextCodes.OfText(TextVariables.Value(name.ToString(), c) ?? PackValue(name.ToString(), c) ?? "$" + name);
        foreach (var b in text)
        {
            if (b == 0)
            {
                break;
            }
            if (b == '$')
            {
                name.Clear();
                inName = true;
            }
            else if (inName && !(b < 0x80 && char.IsAsciiLetterOrDigit((char)b)))
            {
                inName = false;
                result.AddRange(Value());
            }
            if (inName && b != '$')
            {
                name.Append((char)b);
            }
            else if (!inName)
            {
                result.Add(b);
            }
        }
        if (inName)
        {
            result.AddRange(Value());
        }
        return [.. result];
    }

    private static byte[] Evaluate(List<byte> name, CardContext c)
    {
        var text = GameTextCodec.Decode([.. name]);
        return GameTextCodec.Encode(Value(text, c) ?? PackValue(text, c) ?? "$" + text);
    }

    // a translation pack's own variable, for a name the game does not know (null: none, or no pack, as the original: "$name" stays)
    private static string? PackValue(string name, CardContext c) =>
        c.PackVariable is { } pack ? pack(name, ChosenIsFemale(c)) : null;

    /// <summary>Whether the chosen one (text-slot-chosen-one) is female, as the pronouns read it (Value).</summary>
    public static bool ChosenIsFemale(CardContext c) =>
        Member(c, c.Variables.Chosen[0]) is { } chosenOne ? chosenOne.IsFemale : OutsideByte(c, c.Variables.Chosen[0], SexAt) is > 0;

    /// <summary>The value of one variable, or null when it is not one the game knows.</summary>
    // orig: text-slot-expand R043+0x1316 (150B:0886) draft (its switch on the variable index)
    // C: the cases below are in the original's order of index; each reads its source unchecked, a few are guarded here (each guard that changes a result carries its own DIVERGENCE note: LocationName, Names, PlaceName).
    public static string? Value(string name, CardContext c)
    {
        var state = c.State;
        var party = state.Party;
        var city = state.City;   // C: city-record, the last loaded city record even while the party is elsewhere
        var here = state.CurrentLocation;

        for (var k = 0; k < Ordinals.Length; k++)
        {
            if (name == $"Chosen{Ordinals[k]}Name")
            {
                // C: strcpy from party[var_chosenK] + 0x2A whatever the slot's status: an empty slot still holds its last
                // C: occupant's name in this run (Party.Slot keeps it), a never-used slot is zero ("").
                // C: an index outside 0..4 (var_chosenK is never reset between cards; NightInTheGuildLeadersHouse puts an Agility value there)
                // C: reads past the party: from slot 25 on party[k].name (DS:9C93 + 0x80k) lies in text-bank-file, the loaded .MSG (OutsideName).
                // DIVERGENCE: elsewhere (the enemy combatants of the last battle, the globals after them, the stale tail of text-bank-file's
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
                // a translation's money words when it has them (CardContext.PackMoney), else the original's
                return c.PackMoney?.Invoke(c.Variables.Money[k]) ?? c.Variables.Money[k].ToString();
            }
        }

        // C: "he/she" and the other pronouns look at the sex byte of party[text-slot-chosen-one] (0 male, any other value female).
        // C: past the party the sex byte (party[k] + 3) is read the same way (OutsideByte); elsewhere taken as male (see ChosenOneName).
        var female = Member(c, c.Variables.Chosen[0]) is { } chosenOne ? chosenOne.IsFemale : OutsideByte(c, c.Variables.Chosen[0], SexAt) is > 0;
        return name switch
        {
            "PlaceName" => PlaceName(c),
            "PlaceDesc" => PlaceDescription(c),
            "Inn" => Names(c, city, 14),
            "He" => female ? "She" : "He",
            "he" => female ? "she" : "he",
            "his" => female ? "her" : "his",
            "His" => female ? "Her" : "His",
            "him" => female ? "her" : "him",
            "himself" => female ? "herself" : "himself",
            "LeaderName" => party.Slots.Count > 0 ? party.Leader.ShortName : "",   // DIVERGENCE: no party gives "" (the original reads party[leader])
            "PlaceAttitude" => Standing(c),
            "Number1" => c.Variables.Numbers[0].ToString(System.Globalization.CultureInfo.InvariantCulture),
            "Number2" => c.Variables.Numbers[1].ToString(System.Globalization.CultureInfo.InvariantCulture),
            "citySquare" => Names(c, city, 3),
            "councilHall" => Names(c, city, 4),
            "imperialMint" => Names(c, city, 9),
            "cityBarracks" => Names(c, city, 11),
            "university" => Names(c, city, 15),
            "marketplace" => Names(c, city, 8),
            "fortress" => Names(c, city, 5),
            "pawnshop" => Names(c, city, 12),
            "hospital" or "slum" => Names(c, city, 10),   // C: both names read the same field
            "monastery" => Names(c, city, 13),
            "cathedral" => Names(c, city, 6),
            "cityChurch" => Names(c, city, 7),
            "CityLordName" => Names(c, city, 0),
            "CityLordTitle" => LordTitle(c, name),
            "CityLocation" => Names(c, city, c.Variables.CityLocation),
            "LocName" => LocationName(c, c.Variables.Location),
            "ItemName" => Shown(c, DataNames.Item(c.Variables.Item), c.World.ItemName(c.Variables.Item)),   // DIVERGENCE: an item code the list lacks gives "item N" (the original indexes item-definition-table unchecked)
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
            "Government" => LocationRules.Query(state, here, 0x18) != 0 ? Names(c, city, 4) : Names(c, city, 5),
            "CurrentBell" => c.Word(OriginalWords.BellNames, TimeOfDay.Band(state.Date.Hour)),
            "MonthName" => c.Word(OriginalWords.MonthNames, GameDate.Mod12(state.Date.Month)),   // DIVERGENCE: a month outside 0..11 wraps (the original reads past its table)
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

    // orig: party DS:9C69 (0x80 bytes a slot: +3 sex, +0x2A name), text-bank-file DS:A895
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
    // C: (a longer one overruns text-substitute's stack; here it is copied whole).
    private static string? OutsideName(CardContext c, int index)
    {
        var at = OutsideOffset(c, index, NameAt);
        if (at < 0 || Member(c, index) is not null)
        {
            return null;
        }
        var file = c.MessageFileBytes!;
        var end = Array.IndexOf(file, (byte)0, at);
        return GameTextCodec.Decode(file.AsSpan(at, (end < 0 ? file.Length : end) - at));
    }

    // orig: case 54 / 70 of R043+0x1316: locations[text-slot-place]->name
    // DIVERGENCE: an index outside the 414 locations gives "" (the original reads whatever the far table holds).
    private static string LocationName(CardContext c, int index) =>
        index >= 0 && index < c.State.Locations.Count ? PlaceShown(c, index) : "";

    // Not in the original: the 1.1 translations. A name in the pack's language where it has one (no pack: the original's); a place's
    //   name only while the saved one is still the data file's (a save keeps the original's bytes).
    private static string Shown(CardContext c, string? key, string original) =>
        key is not null && c.Translation?.Invoke(key) is { } translated ? translated : original;

    private static string PlaceShown(CardContext c, int index)
    {
        var stored = c.State.Locations[index].Name;
        return stored == c.World.FileLocationName(index) ? Shown(c, DataNames.Location(index), stored) : stored;
    }

    // orig: case 0 of R043+0x1316
    // C: location-now < 0x5c is a signed compare: the wilderness (-1) takes the city branch and names the last loaded city record, not a place.
    private static string PlaceName(CardContext c)
    {
        var here = c.State.CurrentLocation;
        if (here < Location.CityCount)
        {
            return Names(c, c.State.City, -1);
        }
        // DIVERGENCE: a place past the location table (here >= 414) gives "" here; the original reads the far table's memory after its last record.
        //   (R043+0x133E: the far pointer es:[10BE + 4 x location-now], es = DS:7EE6, then the name at +0x26 of what it points to.) Not reached in play: every writer of location-now (DS:907E) stores -1, a constant
        //   below 414, a city index, a city record's +0x4A..+0x52 word (-1..91 in DARKLAND.CTY) or the result of a search over the 414 places (-1..413).
        return here < c.State.Locations.Count ? PlaceShown(c, here) : "";
    }

    // orig: case 1: strcpy(name, city-description); city-description is the DARKLAND.DSC record of the city loaded last (read together with the city record when the party arrives in a city)
    // C: the description is looked up by the working city record's number; the original keeps city-description (DS:978B, 0x50 bytes) in its own buffer,
    //   but every one of its writers reads DARKLAND.DSC at index * 0x50 + 1 beside its DARKLAND.CTY read at index * 0x26E + 1 with the same index
    //   (the opens of darkland.dsc at R029+0x4ED4, R030+0x06F2, R037+0x9A0D, R043+0x0447, R051+0x2296/0x24AA/0x26BE/0x28D4/0x2AE8,
    //   R066+0x291E/0x2A2E/0x2B3E/0x2C4E, R082+0x15C4, R133+0x1B48: no other code names the file), so the two never part for a city; for
    //   the other indexes see CityFile.Read.
    private static string PlaceDescription(CardContext c) => c.World.CityDescription(c.State.City.City);

    // orig: case 16: R136+0xA813[place-query(location-now, 1)]
    // DIVERGENCE: in the wilderness (no location) the original indexes locations[-1]; here the standing band of an unknown place, 2, is used ("unknown").
    private static string Standing(CardContext c)
    {
        var here = c.State.CurrentLocation;
        var band = here >= 0 && here < c.State.Locations.Count ? LocationRules.Query(c.State, here, 1) : 2;
        return c.Word(OriginalWords.StandingWords, Math.Clamp(band, 0, OriginalWords.StandingWords.Count - 1));
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
            0 => Names(c, city, risen ? 1 : 0),
            1 => c.Word(OriginalWords.LordTitles1, city.Ordinal % OriginalWords.LordTitles1.Count),
            2 => c.Word(OriginalWords.LordTitles2, city.Ordinal % OriginalWords.LordTitles2.Count),
            _ => name,
        };
    }

    // orig: case 59..65: R136+0xB1EB[text-slot-support-zero..6]
    // DIVERGENCE: a value outside -1..1 gives "" (the original indexes its table unchecked and copies whatever string the neighbouring pointer gives).
    private static string Support(CardContext c, int index)
    {
        var value = c.Variables.Support[index] + 1;
        return OriginalWords.SupportWords.Contains(value) ? c.Word(OriginalWords.SupportWords, value) : "";
    }

    // orig: case 56: with a location chosen (location-now != -1) the city nearest to location text-slot-place, in the wilderness the one nearest to the party
    // C: the test is on location-now but the place measured from is text-slot-place.
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

    // orig: case 57: R136+0xA773[thunk 09C0:1FB3(text-slot-place)] = the direction from the current location to text-slot-place (R030+0x29BC)
    // C: thunk 09C0:1FB3 is R030+0x29BC (the direction from location-now to a location) and thunk 09C0:1FBD is R030+0x29FA (the direction between two points): both resolve through the atlas thunk table (vec_XXXX = thunk (XXXX - 0x17E3) / 10).
    private static string DirectionFromHere(CardContext c)
    {
        var state = c.State;
        if (state.Here is not { } from || c.Variables.Location < 0 || c.Variables.Location >= state.Locations.Count)
        {
            return "";   // DIVERGENCE: the original indexes locations[location-now] and locations[text-slot-place] unchecked
        }
        var to = state.Locations[c.Variables.Location];
        return Direction(c, PartyStanding.Direction(from.X, from.Y, to.X, to.Y));
    }

    // orig: case 58: R136+0xA773[thunk 09C0:1FBD(loc2.x, loc2.y, loc.x, loc.y)] = the direction from text-slot-place-two to text-slot-place
    private static string DirectionBetweenLocations(CardContext c)
    {
        var state = c.State;
        var (a, b) = (c.Variables.Location2, c.Variables.Location);
        if (a < 0 || a >= state.Locations.Count || b < 0 || b >= state.Locations.Count)
        {
            return "";   // DIVERGENCE: unchecked indexes in the original
        }
        return Direction(c, PartyStanding.Direction(state.Locations[a].X, state.Locations[a].Y, state.Locations[b].X, state.Locations[b].Y));
    }

    // orig: case 68: month name, a space and the day of the next witches' sabbat (thunk 09C0:20DF, thunk 09C0:21A7)
    private static string SabbatTime(CardContext c) =>
        c.Word(OriginalWords.MonthNames, GameDate.Mod12(Quests.SabbatMonth(c.State))) + " " + Quests.SabbatDayOfMonth(c.State).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // orig: case 69 of R043+0x1316: "<direction> of <nearest city>" for the site of the next sabbat (20fd, Quests.SabbatSite)
    private static string SabbatLocation(CardContext c) => RelativeToNearestCity(c, Quests.SabbatSite(c.State));

    // orig: case 71 / 72: the nearest monastery / last castle (closest-place-of-kind from the party's map position), "<direction> of <nearest city>"
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
        return Direction(c, PartyStanding.Direction(from.X, from.Y, place.X, place.Y)) + " of " + from.Name;
    }

    /// <summary>The word of a direction (0 north … 7 northwest).</summary>
    private static string Direction(CardContext c, int direction) => c.Word(OriginalWords.Directions, direction);

    /// <summary>A name of the working city record (city-record->names[n]); n = -1 is the short name at the start of the record.</summary>
    // C: _fstrcpy copies up to the first zero byte: a 32-byte field without one runs on into the next field. An index outside the 16 names reads other parts of the record.
    // DIVERGENCE: an index past the record's 16 names (the record ends at 0x6E + 16 x 0x20 = 0x26E) gives "" here; the original reads the memory after the record
    //   (city-record is the far block at DS:9785). Not reached in play: the callers pass 0..15 or -1, and $CityLocation's DS:ED52 is always 0 (see CardVariables.CityLocation).
    private static string Names(CardContext c, Core.World.CityRecord city, int index) =>
        Shown(c, DataNames.CityName(city.City, index), Names(city, index));

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
        return GameTextCodec.Decode(bytes.AsSpan(start, end - start));
    }
}
