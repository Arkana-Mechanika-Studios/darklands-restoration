using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityW05": at the city wall, away from the gates. The party can blast the sally port open with a
/// potion, have its strongest member force it, or draw weapons and fight the guards.
/// </summary>
// orig: R055+0x0FA0 (1932:0000) matched; tests: CardSmokeTests
public sealed class CityWallSallyPort : Card
{
    public const int CardNumber = 0x2F;

    private const int Picture = 0;

    private const int Strength = 1;

    /// <summary>Event kinds: the potion's noise at the wall, the guards alerted by a failed attempt, banned.</summary>
    private const int WallAlarm = 0x13, WallTried = 0x21, Banned = 0x11;

    private const int SideStreet = 0x09, SideStreetAtNight = 0x0A, OverlandMap = 0x0C, Dungeon = 0x0D, CityOutskirtsFought = 0x73,
        WallBlocked = 0x76;

    /// <summary>
    /// The party's highest Strength (the original's module word DS:8DFC, reset at every visit).
    /// </summary>
    private int _strongest;

    public override int Number => CardNumber;

    // orig: R055+0x0FA0 (1932:0000) matched (entry: the strongest member, the standing, the option flags and the menu loop)
    // orig: R055+0x14A4 (1932:0504) matched (init helper: enables all 20 lines with the "screwed up." default handler, installs the item pop-up handlers 0..2 on the 5 member rows, empties the item and saint slots)
    // DORMANT: R055+0x1506 (1932:0566): the "screwed up." handler of lines 3..19; the init loop enables them but the card text has three lines and the menu loop copies only flags 0..2.
    // DORMANT: the init helper installs no item handler for slot 3, no saint handlers and the card has three items and no saints; slot 3 keeps a stale handler that nothing can call.
    // C: the original also calls do_nothing() (an empty stub) after reading the standing.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        _strongest = 0;
        for (var member = 0; member < c.PartySize; member++)
        {
            var strength = c.Attribute(member, Strength);
            if (_strongest < strength)
            {
                c.Variables.Chosen[0] = member;
                _strongest = strength;
            }
        }
        var forceState = c.EventAtHere(WallTried) ? OptionState.Shown : OptionState.Enabled;
        c.StandingSeen = c.Reputation(c.Here);
        c.SetCardItems(0x66, 0x63, 0x67);
        var potionState = c.ItemsUsable();

        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        menu[0] = potionState;
        menu[1] = forceState;
        c.Next = Number;
        return menu.Run(
            // orig: R055+0x1136 (1932:0196) matched (option 0 action, see BlastWithPotion); hint handler R055+0x122C (1932:028C) "select a potion"; pop-up handlers see PotionHint
            new MenuLine(chance => BlastWithPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c, c.MenuSlot)),
            // orig: R055+0x1264 (1932:02C4) matched (option 1 action, see ForceTheDoor); hint handler R055+0x12EC (1932:034C), see ForceChance
            new MenuLine(chance =>
                {
                    if (ForceTheDoor(c, chance))
                    {
                        menu[1] = OptionState.Shown;
                    }
                },
                () => ForceChance(c)),
            // orig: R055+0x1316 (1932:0376) matched (option 2 action, see FightTheGuards); hint handler R055+0x146C (1932:04CC) "enter combat"
            new MenuLine(() => FightTheGuards(c), OptionHint.Saying(CardTexts.EnterCombat)));
    }

    /// <summary>The chance to break the door: a third of the member's Strength.</summary>
    // orig: R055+0x12EC (1932:034C) matched
    // BUG: the original passes the highest Strength VALUE (DS:8DFC) where the member NUMBER belongs, both in the Strength lookup and as the member the hint is about; the Strength of that slot is what counts (almost always an empty slot here: 0).
    // DIVERGENCE: a slot past the party reads as an empty member (Strength 0); the original reads whatever memory follows the party table.
    private OptionHint ForceChance(CardContext c) =>
        OptionHint.ChanceOf(c.Attribute(_strongest, Strength) / 3, _strongest);

    /// <summary>The potion pop-up's hint by slot: strong potion, weaker potion, an unknown one; all depend on the city's size.</summary>
    // orig: R055+0x153E (1932:059E) matched (slot 0: 2 x (45 - size))
    // orig: R055+0x156C (1932:05CC) matched (slot 1: 80 - 3 x size)
    // orig: R055+0x159E (1932:05FE) matched (slot 2: "you can always encounter the unknown")
    // C: slot 3 has no handler of this card (the init loop fills slots 0..2 only) and no item; it maps to the "screwed up." text here.
    private static OptionHint PotionHint(CardContext c, int slot)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var size = c.HereQuery(2);
        return slot switch
        {
            0 => OptionHint.ChanceOf(-(size - 0x2D) << 1, c.MenuMember),
            1 => OptionHint.ChanceOf(0x50 - size * 3, c.MenuMember),
            2 => OptionHint.Say(CardTexts.Unknown, c.MenuMember),
            _ => OptionHint.Say(CardTexts.ScrewedUp),
        };
    }

    /// <summary>
    /// The potion is thrown: whatever chance the pop-up showed, the original forces the potion to
    /// slot 2 (the most violent one) and rolls against 100, so it always works: the party gets
    /// through, loses a little standing, and is hurt.
    /// </summary>
    // orig: R055+0x1136 (1932:0196) matched
    // BUG: "menu_slot = 2" and "p_6 = 0x64" overwrite the chosen potion and the chance (the parameter is volatile), a left-over test cheat; the failure branch can never run (the draw is 0..99 against 100) but still draws.
    private void BlastWithPotion(CardContext c, int chance)
    {
        _ = chance;
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.Events.Renew(-2, c.Here, 0, WallAlarm, 1, 0x63, 0, 0x18, 0, 0);
        c.MenuSlot = 2;
        chance = 0x64;
        if (c.Random(0x64) > chance)
        {
            // DORMANT: the potion fails.
            c.Screen.Text(4, TextFrame.BlankPage);
            c.PassHours(0);
            c.Next = WallBlocked;
            return;
        }
        c.AdjustReputation(c.Here, 0, -3);
        switch (c.MenuSlot)
        {
            case 0:
                // DORMANT: the menu slot is always 2 by now.
                c.Screen.Text(1, TextFrame.BlankPage);
                c.AdjustReputation(c.Here, -1, -6);
                break;
            case 1:
                // DORMANT: the menu slot is always 2 by now.
                c.Screen.Text(2, TextFrame.BlankPage);
                c.AdjustReputation(c.Here, 0, -3);
                break;
            case 2:
                c.Screen.Text(3, TextFrame.BlankPage);
                c.Injure(Who.Everybody, 0, 0, 5);
                c.AdjustReputation(c.Here, -1, -4);
                break;
        }
        c.PassHours(0);
        c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
    }

    /// <summary>Forces the sally port; returns true when it fails (the option is then closed).</summary>
    // orig: R055+0x1264 (1932:02C4) matched
    private bool ForceTheDoor(CardContext c, int chance)
    {
        if (c.Random(100) >= chance)
        {
            c.Screen.Text(5, TextFrame.BlankPage);
            c.PassHours(1);
            c.AdjustReputation(c.Here, 1, 4);
            c.Next = c.IsDaylight ? SideStreet : SideStreetAtNight;
            return false;
        }
        c.Screen.Text(6, TextFrame.BlankPage);
        c.PassHours(0);
        c.Events.Create(-2, c.Here, 0, WallTried, 1, 0x63, 0, 0x30, 0, 0);
        c.Next = WallBlocked;
        return true;
    }

    /// <summary>
    /// Fights the guards on the wall. The standing falls by 40 (when it is already -40 or worse the
    /// original rolls a change it then ignores), the wall is alarmed, and the outcome decides where the party ends.
    /// </summary>
    // orig: R055+0x1316 (1932:0376) matched
    // C: arguments are evaluated right to left, so the second draw (of 4) comes before the first (of 2).
    // C: the standing is the one the card read on arrival (cur_rep), also for the ban at the end.
    private void FightTheGuards(CardContext c)
    {
        var lore = c.PartyLoreLevel();
        var archers = c.Random(4) + lore / 3 + 1;
        var guards = c.Random(2) + lore / 4 + 1;
        // The battle seed adds the card number (next_card still holds it) to the location.
        var result = c.RunBattle(0x54, CardNumber + c.Here, 8, guards, archers, 0x21, lore % 3 + 1, 1, -1, 0, 0);
        var standing = c.StandingSeen;
        if (standing <= -0x28)
        {
            c.ImproveRoll(standing, -3, -9);
        }
        else
        {
            standing -= 0x28;
        }
        // DIVERGENCE: guard for a party that is not at a location (the original would write before the location table).
        if (c.Here >= 0 && c.Here < c.State.Locations.Count)
        {
            c.SetReputation(c.Here, standing);
        }
        c.Events.Renew(-2, c.Here, 0x14, WallAlarm, 1, 0x64, 0, 0x30, 0, 0);
        switch (result)
        {
            case 0:
                c.Screen.Text(7, TextFrame.BlankPage);
                c.PassHours(0);
                c.Next = CityOutskirtsFought;
                break;
            case 1:
            case 2:
                c.ReportAway();
                c.Screen.Text(8, TextFrame.BlankPage);
                c.PassHours(1);
                c.Next = CityOutskirtsFought;
                break;
            case 3:
                c.Screen.Text(9, TextFrame.BlankPage);
                c.PassHours(0);
                c.Next = OverlandMap;
                break;
            case 4:
                c.Screen.Text(0x0A, TextFrame.BlankPage);
                c.PassHours(3);
                c.Next = Dungeon;
                break;
        }
        c.EventHere(Banned, c.StandingSeen <= -0x4B ? 0xF0 : 0x78);
    }
}
