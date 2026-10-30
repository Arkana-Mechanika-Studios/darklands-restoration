namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Villa01": a hamlet with its church, graveyard and smithy. Talk to the Schulz, visit the
/// church or the smithy, seek shelter, pray to a saint (who may reveal a curse over the
/// village), or move on. On arrival some villages fall under a lasting evil.
/// </summary>
// orig: R077+0x0E48 (191C:0008) matched; tests: CardSmokeTests
// orig: R077+0x1320 (191C:04E0) matched; R077+0x138C (191C:054C) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 6..19 (never offered).
public sealed class HamletSquare : Card
{
    public const int CardNumber = 0x85;

    private const int Picture = 27;

    /// <summary>The village lies under an evil (event 0x3D).</summary>
    private const int VillageEvil = 0x3D;

    private static readonly int[] Saints = [0x25, 0x42, 0x43, 0x5D];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(0x29);
        Show(c, Picture);
        c.Next = CardNumber;
        // C: the script keeps place-query(location-now, 0) in reputation-now; no line of this card reads it.
        MaybeTheVillageFallsUnderEvil(c);
        c.SetCardItems();
        c.SetCardSaints(Saints);
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 5);
        menu[4] = c.SaintsUsable();

        return menu.Run(
            // orig: R077+0x1088 (191C:0248) matched; R077+0x109A (191C:025A) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x86; }, OptionHint.Saying(SuccessAssured)),
            // orig: R077+0x10D2 (191C:0292) matched; R077+0x10E4 (191C:02A4) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x8B; }, OptionHint.Saying(SuccessAssured)),
            // orig: R077+0x111C (191C:02DC) matched; R077+0x112E (191C:02EE) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x87; }, OptionHint.Saying(SuccessAssured)),
            // orig: R077+0x1166 (191C:0326) matched; R077+0x1178 (191C:0338) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x89; }, OptionHint.Saying(SuccessAssured)),
            // orig: R077+0x11B0 (line 4); R077+0x13C4 (191C:0584) matched; R077+0x144E (191C:060E) matched; R077+0x14D8 (191C:0698) matched;
            // R077+0x1562 (191C:0722) matched; tests: CardSmokeTests
            new MenuLine(() => PrayForGuidance(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R077+0x12D6 (191C:0496) matched; R077+0x12E8 (191C:04A8) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>
    /// A village not yet cursed becomes so for three years when its position and the party seed
    /// fall on a multiple of 3 (when the nearest places of kinds 8 and 10 are the same) or of 10.
    /// </summary>
    private static void MaybeTheVillageFallsUnderEvil(CardContext c)
    {
        if (c.EventAtHere(VillageEvil))
        {
            return;
        }
        // C: party-map-column is passed as a long: the party's map x and y.
        var map = c.State.MapPosition;
        var nearestOf8 = c.NearestLocationOfType(map.X, map.Y, 8);
        var divisor = c.NearestLocationOfType(map.X, map.Y, 0x0A) == nearestOf8 ? 3 : 10;
        var village = c.State.Locations[c.Here];
        // C: the sum is taken as unsigned (16 bits) before the remainder.
        if ((ushort)(village.X + village.Y + c.State.Party.Seed) % divisor == 0)
        {
            c.EventHere(VillageEvil, 26280);
        }
    }

    // orig: R077+0x11B0 (191C:0370) matched; R077+0x129E (191C:045E) matched; tests: CardSmokeTests
    private static void PrayForGuidance(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.Variables.Chosen[0] = c.MenuMember;
        if (answer == 0)
        {
            ShowPicture(c, 16);
            c.Screen.Text(3);
        }
        else
        {
            if (c.MenuSlot is >= 0 and < 4)
            {
                c.NameToSlot(1, Saints[c.MenuSlot], 0);
            }
            if (c.EventAtHere(VillageEvil))
            {
                ShowPicture(c, 127);
                c.Screen.Text(1);
            }
            else
            {
                c.Screen.Text(2);
            }
        }
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }
}
