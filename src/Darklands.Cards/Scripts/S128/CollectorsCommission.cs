namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf12": a patron describes the object he wants found (the quest event kept in the saved
/// card slot): the hermit's prayer book or gospels, the holy hermit's habit, an ancient scroll,
/// a noble's crown, a dwarf's alchemical ingredient, secret reports, an heirloom, the Tarnhelm,
/// a stolen document, debt notes or love letters, and where to look ($LocName, $LocName2).
/// No menu: the party goes on to the card kept in the second saved slot.
/// </summary>
// orig: R128+0x0BF0 (18F7:0000) matched; tests: CardSmokeTests
//   item pop-up: R128+0x138A, R128+0x13C6, R128+0x1402,
//     R128+0x143E
//   saint pop-up: R128+0x147A, R128+0x1504, R128+0x158E,
//     R128+0x1618
//   other: R128+0x1036, R128+0x1042, R128+0x1070,
//     R128+0x1082, R128+0x10B0, R128+0x10C2,
//     R128+0x10F0, R128+0x1102, R128+0x1130,
//     R128+0x1142, R128+0x1170, R128+0x1182,
//     R128+0x11B0, R128+0x11C2, R128+0x11F0,
//     R128+0x1202, R128+0x1230, R128+0x1242,
//     R128+0x1270, R128+0x1282, R128+0x12B0,
//     R128+0x1352
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
// C (generated): some hints return r uninitialised (stale stack): read as their message for the whole party.
public sealed class CollectorsCommission : Card
{
    public override int Number => 337;

    private const int Picture = 356;

    /// <summary>The name kinds (name_to_slot kind 8) of the four kinds of owner, by the quest's first data word.</summary>
    private static readonly int[] OwnerNames = [0, 6, 8, 7];

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.SetCardItems();
        c.SetCardSaints();
        var quest = c.State.SavedCard;
        c.Variables.Item = c.EventDetail(quest);
        c.Variables.Location = c.EventLocation(quest);
        var site = PlaceAt(c, c.Variables.Location);
        c.Variables.Location2 = c.NearestCity(site?.X ?? 0, site?.Y ?? 0);
        var siteKind = site?.Icon ?? -1;
        var owner = c.Events[quest]?.Data1 ?? 0;
        // C: party_seed + cur_loc + 0x11C1.. is 16-bit int arithmetic; random_name takes it as it is.
        var nameSeed = c.State.Party.Seed + c.Here;

        // UNCERTAIN: card_text(k, 2) below: frame mode 2 read as drawing like mode 0.
        switch (c.EventType(quest))
        {
            case 0:
                c.RandomName(nameSeed + 0x11C1, 4, 0);
                c.Screen.Text(1);
                break;
            case 1:
                c.RandomName(nameSeed + 0x11C2, 4, 0);
                c.Screen.Text(2);
                break;
            case 2:
                c.Screen.Text(3);
                break;
            case 3:
                c.RandomName(nameSeed + 0x11C3, 4, 0);
                switch (siteKind)
                {
                    case 0:
                        NameTheOwner(c, owner % 4, 1);
                        c.Screen.Text(5);
                        break;
                    case 0x0D:
                        c.Screen.Text(4);
                        break;
                    case 0x12:
                        c.Screen.Text(6);
                        break;
                    case 0x13:
                        c.Screen.Text(7);
                        break;
                }
                break;
            case 4:
                c.Screen.Text(8);
                break;
            case 5:
                c.Screen.Text(9);
                break;
            case 6:
                NameTheOwner(c, owner % 4, 0);
                c.Screen.Text(0x0A);
                break;
            case 7:
                NameTheSite(c, siteKind);
                c.Screen.Text(c.Random(2) != 0 ? 0x0B : 0x0C);
                break;
            case 8:
                NameTheSite(c, siteKind);
                c.Screen.Text(0x0D);
                break;
            case 9:
                NameTheOwner(c, owner % 4, 0);
                c.Screen.Text(0x0E);
                break;
            case 10:
                // Only the second and third kinds of owner here.
                switch (owner % 2)
                {
                    case 0:
                        c.NameToSlot(8, 6, 0);
                        break;
                    case 1:
                        c.NameToSlot(8, 8, 0);
                        break;
                }
                c.Screen.Text(0x0F);
                break;
            case 11:
                NameTheOwner(c, owner % 4, 0);
                c.Screen.Text(0x10);
                break;
            default:
                c.Screen.Text(0);
                break;
        }
        return c.State.SavedCard2;
    }

    // DIVERGENCE: the original reads locations[var_loc] (and the event record) without a check; a missing one is
    //   treated here as no place (site kind −1, map position 0,0) and an empty slot as owner 0.
    /// <summary>The original reads the location record without a check; a missing one is treated as no place.</summary>
    private static Core.World.Location? PlaceAt(CardContext c, int loc) =>
        loc >= 0 && loc < c.State.Locations.Count ? c.State.Locations[loc] : null;

    /// <summary>The owner's title into $NamedOneName/$NamedTwoName (a negative remainder names nobody, as in the original).</summary>
    // C: +0x2A % 4 is a signed remainder (negative for a negative word), and then no case matches: nobody is named.
    // C: name_to_slot kind 8 (titles) is read from the EXE's table (PersonalNames, NameToSlot).
    private static void NameTheOwner(CardContext c, int kind, int slot)
    {
        if (kind is >= 0 and < 4)
        {
            c.NameToSlot(8, OwnerNames[kind], slot);
        }
    }

    /// <summary>What kind of place it is, into $NamedOneName (other kinds leave it as it was).</summary>
    private static void NameTheSite(CardContext c, int kind)
    {
        var word = kind switch
        {
            0x10 => "spring",
            0x11 => "lake",
            5 or 0x13 => "cave",
            0x12 => "shrine",
            0x0D => "mound",
            0x14 => "pagan altar",
            _ => null,
        };
        if (word is not null)
        {
            c.Variables.Named[0] = word;
        }
    }

    /// <summary>
    /// The option table the file also holds. Line 0 only lets time catch up (and stays on the card); lines 1..9 go
    /// to 0x62. Their hints copy "Success is assured." and return an uninitialised result.
    /// </summary>
    // DORMANT: the card function never runs a menu, so none of these is reached.
    // orig: R128+0x1036 R128+0x1042 (line 0); R128+0x1070 R128+0x1082 R128+0x10B0
    //   R128+0x10C2 R128+0x10F0 R128+0x1102 R128+0x1130 R128+0x1142 R128+0x1170
    //   R128+0x1182 R128+0x11B0 R128+0x11C2 R128+0x11F0 R128+0x1202 R128+0x1230
    //   R128+0x1242 R128+0x1270 R128+0x1282 (lines 1..9); R128+0x12B0 (table set-up),
    //   R128+0x1352 ("screwed up."), R128+0x138A R128+0x13C6 R128+0x1402 R128+0x143E (item
    //   pop-up), R128+0x147A R128+0x1504 R128+0x158E R128+0x1618 (saint pop-up); all matched
    // C: the hints leave r uninitialised (stale stack): read as "Success is assured." for the whole party.
    internal static MenuLine[] DormantOptions(CardContext c)
    {
        var options = CardMenu.FirstTextAndEndTable(c, SuccessAssured, showFirstText: false);
        options[0] = new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured));
        return options;
    }
}
