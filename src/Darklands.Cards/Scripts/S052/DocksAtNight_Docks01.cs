using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Docks01": the docks of a city at night. The party looks at the boats, or escapes the
/// city by swimming away (giving up mounts, armour and heavy things; in the cold months the
/// swim is only shown), or walks away down the main street or a side street.
/// </summary>
// orig: R052+0x0000 (1838:0000) unmatched (checked against asm); tests: CardSmokeTests
public sealed class DocksAtNight_Docks01 : Card
{
    public const int CardNumber = 0x20;

    private const int Picture = 68;

    /// <summary>The picture shown while the swimmers are counted (pic_names+0x0DF7).</summary>
    private const int SwimmersPicture = 236;

    private const int Docks = 0x1F, MainStreet = 0x06, MainStreetAtNight = 0x08, SideStreet = 0x09, SideStreetAtNight = 0x0A,
        OverlandMap = 0x0C, CityOutskirts = 0x04;

    /// <summary>Item kind bits dropped by the swim: mounts and the like, and armour.</summary>
    private const uint MountKinds = 0x2000, ArmourKinds = 0x04000040;

    public override int Number => CardNumber;

    // orig: R052+0x0000 (1838:0000) unmatched (entry: option states by month and mounts, text block by season, menu loop; checked against asm)
    // orig: R052+0x06D4 (1838:06D4) matched (init helper: enables all 20 option lines, clears the item and saint slots)
    // DORMANT: R052+0x0776 (1838:0776): the init helper points opt_fn[0..19] at this "screwed up." default; the card installs its own handler for lines 0..9, and lines 10..19 (left enabled by the helper) have no text line, so it never runs.
    // DORMANT: R052+0x07AE (1838:07AE), R052+0x07EA (1838:07EA), R052+0x0826 (1838:0826), R052+0x0862 (1838:0862): item pop-up handlers ("success is assured" for the chosen member); card_items stay empty (-1), so no item pop-up exists.
    // DORMANT: R052+0x089E (1838:089E), R052+0x0928 (1838:0928), R052+0x09B2 (1838:09B2), R052+0x0A3C (1838:0A3C): saint pop-up handlers (chance text of the chosen saint); card_saints stay empty, so no saint pop-up exists.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var month = c.State.Date.Month;
        var winter = month >= 10 || month <= 4;
        var swimOnFoot = winter ? OptionState.Shown : OptionState.Enabled;
        var swimMounted = swimOnFoot;
        if (c.PartyGet(5) != 0)
        {
            swimOnFoot = OptionState.Hidden;
        }
        else
        {
            swimMounted = OptionState.Hidden;
        }

        var menu = Menu(c, winter ? 1 : 0);
        menu.Enable(0, 1, 2, 3, 4);
        menu[1] = swimOnFoot;
        menu[2] = swimMounted;
        c.Next = Number;
        return menu.Run(
            // orig: R052+0x025C (1838:025C) matched (option 0: next card 0x1F); hint R052+0x0264 (success assured)
            new MenuLine(() => c.Next = Docks),
            // orig: R052+0x029C (1838:029C) unmatched (option 1: swim away, see SwimAway; checked against asm); hint R052+0x03FC (see SwimChance)
            new MenuLine(chance => SwimAway(c, chance), () => SwimChance(c)),
            // orig: R052+0x043A (1838:043A) matched (option 2: the same swim); hint R052+0x0446 (identical to R052+0x03FC)
            new MenuLine(chance => SwimAway(c, chance), () => SwimChance(c)),
            // orig: R052+0x0484 (1838:0484) matched (option 3: main street); hint R052+0x049C (success assured)
            new MenuLine(() => c.Next = c.IsDaylight ? MainStreet : MainStreetAtNight),
            // orig: R052+0x04D4 (1838:04D4) matched (option 4: side street); hint R052+0x04EC (success assured)
            new MenuLine(() => c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight),
            // DORMANT: options 5..9 (R052+0x0524 (1838:0524), R052+0x057A (1838:057A), R052+0x05D0 (1838:05D0), R052+0x0628 (1838:0628), R052+0x067E (1838:067E)): the entry clears opt_flag_5..9, so they are never offered (and the MSG has no lines for them). Each shows text 0 and goes to the outskirts (card 4).
            // Their hints: R052+0x0542 (1838:0542), R052+0x0598 (1838:0598), R052+0x05EE (1838:05EE, 100% for the leader), R052+0x0646 (1838:0646), R052+0x069C (1838:069C).
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c), () => OptionHint.ChanceOf(100, c.Leader)),
            new MenuLine(() => LeaveForOutskirts(c)),
            new MenuLine(() => LeaveForOutskirts(c)));
    }

    // DORMANT: reached only by the hidden options 5..9.
    private static void LeaveForOutskirts(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = CityOutskirts;
    }

    /// <summary>Sure when even the weakest member has Strength above 10; else ten times the weakest Strength, for that member.</summary>
    // orig: R052+0x03FC (1838:03FC) matched
    // orig: R052+0x0446 (1838:0446) matched
    private static OptionHint SwimChance(CardContext c)
    {
        var weakest = c.PartyMinAttribute(1);
        return weakest > 10
            ? OptionHint.ChanceOf(99, OptionHint.WholeParty)
            : OptionHint.ChanceOf(weakest * 10, c.BestMember);
    }

    /// <summary>
    /// Swims away from the city: heavy things are lost and the party keeps 30% of the rest, hours pass,
    /// and unless success is assured every member who is still standing has to survive the cold water.
    /// </summary>
    // orig: R052+0x029C (1838:029C) unmatched (checked against asm)
    private static void SwimAway(CardContext c, int chance)
    {
        c.RemoveItemsWithFlags(Who.Everybody, MountKinds);
        c.RemoveItemsWithFlags(Who.Everybody, ArmourKinds);
        c.KeepItems(Who.Everybody, 30);
        var hours = c.Random(4) + 4;
        c.PassHours(hours);
        c.Variables.Numbers[0] = hours;
        if (chance < 99)
        {
            c.Variables.Chosen[0] = -1;
            // C: the party size is read again on every round; deleting a drowned member can shrink it.
            for (var member = 0; member < c.PartySize; member++)
            {
                var status = c.Status(member);
                if (status == CharacterStatus.Empty)
                {
                    continue;
                }
                var strength = c.Attribute(member, 1);
                // C: the roll is only made when Strength is below 10 (short-circuit).
                if ((strength >= 10 || c.Random(100) <= strength) && status == CharacterStatus.Able)
                {
                    if (c.Variables.Chosen[0] == -1)
                    {
                        c.Variables.Chosen[0] = member;
                    }
                    c.Variables.Chosen[1] = member;
                    ShowPicture(c, SwimmersPicture);
                    c.Screen.Text(c.Variables.Chosen[0] == c.Variables.Chosen[1] ? 4 : 5);
                }
                else
                {
                    c.Variables.Chosen[2] = member;
                    ShowPicture(c, SwimmersPicture);
                    c.Screen.Text(3);
                    c.DeleteMember(member);
                }
            }
            c.PassHours(0);
        }
        else
        {
            c.Screen.Text(2);
        }
        c.Next = OverlandMap;
    }
}
