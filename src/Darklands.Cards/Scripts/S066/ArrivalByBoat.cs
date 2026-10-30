using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Enter00": the party's boat reaches a city's wharf. Jump ashore among the reeds, pay the
/// wharf toll, talk the wharfmaster round, sneak ashore at night, pray, or sail on to one of
/// the next ports along the river (which ports sail on depends on the day). A besieged city
/// (city flag 2) only lets the party jump ashore or sail on.
/// </summary>
// orig: R066+0x1B4E (19EC:000E) matched; tests: CardSmokeTests
// orig: handler setup R066+0x2D10 (standard saint pop-ups 1274/12fe/1388/1412, unused-line
// handler 123c "screwed up.")
public sealed class ArrivalByBoat : Card
{
    private const int WharfPicture = 220;
    private const int DocksPicture = 68;
    private const int GuardsPicture = 33;
    private const int ChasePicture = 46;

    /// <summary>The city record words with the next ports (−1 none), for option lines 5..9.</summary>
    private static readonly int[] PortWords = [0x4A, 0x4C, 0x4E, 0x50, 0x52];

    public override int Number => 0x5A;

    public override int Run(CardContext c)
    {
        if (c.PreviousCard is 1 or 0x0A)
        {
            return c.IsDaylight ? 9 : 10;
        }
        // C: && evaluates left to right: the 1-in-5 roll is always made.
        if (c.Random(5) == 0 && c.PreviousCard != 0x11A)
        {
            return 0x11A;
        }
        Show(c, WharfPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        // The text block is chosen once, on arrival; sailing on does not change it.
        menu.Block = LookAtThePort(c, menu);
        c.SetCardSaints(0x32, 0x33, 0x67, 0x70);
        // C: this overrides the line-4 state a besieged city sets to 0.
        menu[4] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(_ => JumpAshore(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(chance => PayTheToll(c, chance), () => TollChance(c)),
            new MenuLine(chance => TalkToTheWharfmaster(c, menu, chance), () => TalkChance(c)),
            new MenuLine(chance => SneakAshore(c, chance), () => SneakChance(c)),
            new MenuLine(_ => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(_ => SailOn(c, menu, 0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => SailOn(c, menu, 1), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => SailOn(c, menu, 2), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => SailOn(c, menu, 3), OptionHint.Saying(SuccessAssured)),
            // C: line 9 (the fifth port) has an empty action: choosing it changes nothing and the menu comes back.
            // orig: R066+0x2CD6 (hint R066+0x2CD8)
            new MenuLine(_ => { }, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>
    /// The port's names, the toll ($Money1), and which ports sail on today (lines 5..9); returns the
    /// text block (0 open city, 1 patrolled wharf, 2 besieged).
    /// </summary>
    // orig: the arrival part of R066+0x1B4E and R066+0x204A (the same without the block)
    private static int LookAtThePort(CardContext c, CardMenu menu)
    {
        for (var k = 0; k < 4; k++)
        {
            var port = c.City.Word(PortWords[k]);
            if (port != -1)
            {
                c.Variables.Named[k] = c.LocationName(port);
            }
        }
        c.Variables.Money[0] = Money.From(0, 0, (c.LocationQuery(c.Here, 2) / 3 + 2) * c.PartySize);
        var sailing = new bool[5];
        int block;
        var flags = c.LocationQuery(c.Here, 0x20);
        if ((flags & 1) != 0)
        {
            // C: a signed sum: a negative remainder is not 0, so that port does not sail.
            for (var k = 0; k < 5; k++)
            {
                sailing[k] = (c.HereQuery(0x21) + c.State.Date.Day + k) % 4 == 0;
            }
            block = 1;
        }
        else if ((flags & 2) != 0)
        {
            var only = c.Random(4);
            for (var k = 0; k < 5; k++)
            {
                sailing[k] = only == k;
            }
            block = 2;
            menu[1] = menu[2] = menu[3] = menu[4] = OptionState.Hidden;
        }
        else
        {
            for (var k = 0; k < 5; k++)
            {
                sailing[k] = (c.HereQuery(0x21) + c.State.Date.Day + k) % 3 == 0;
            }
            block = 0;
        }
        for (var k = 0; k < 5; k++)
        {
            menu[5 + k] = c.City.Word(PortWords[k]) == -1 ? OptionState.Hidden
                : sailing[k] ? OptionState.Enabled : OptionState.Shown;
        }
        return block;
    }

    // orig: R066+0x22B8 (hint R066+0x2336)
    private static void JumpAshore(CardContext c)
    {
        c.PassHours(2);
        ShowPicture(c, 51);
        c.Screen.Text(3);
        // C: the first test reads the flags as (char); both lead to 0x73 when a flag 1 or 2 is set.
        if (((sbyte)c.LocationQuery(c.Here, 0x20) & 1) != 0)
        {
            c.Next = 0x73;
            return;
        }
        c.Next = (c.LocationQuery(c.Here, 0x20) & 2) != 0 ? 0x73 : 4;
    }

    /// <summary>The wharfmaster lets the party pay unless the city is hostile; a bad name raises the price of trust.</summary>
    // orig: R066+0x242E
    private static OptionHint TollChance(CardContext c)
    {
        int chance;
        if (c.HereQuery(0) > -10 && !c.EventAtHere(Core.Events.EventKinds.Hostile))
        {
            var factor = c.EventAtHere(Core.Events.EventKinds.Expensive) ? 2 : 1;
            chance = c.HereQuery(0) < 0 ? Math.Clamp(c.HereQuery(0) * factor + 100, 0, 100) : 100;
        }
        else
        {
            chance = 0;
        }
        return new OptionHint(chance, "", OptionHint.WholeParty);
    }

    // orig: R066+0x236E
    private void PayTheToll(CardContext c, int chance)
    {
        if (c.Succeeds(chance))
        {
            // C: the purse is not checked: the toll may take it below zero.
            var toll = Money.From(0, 0, (c.LocationQuery(c.Here, 2) / 3 + 2) * c.PartySize);
            c.State.Party.Purse = c.State.Party.Purse.Subtract(toll);
            c.Next = c.IsDaylight ? 6 : 8;
            return;
        }
        ShowPicture(c, GuardsPicture);
        c.Screen.Text(4);
        ShowPicture(c, WharfPicture);
        c.Next = Number;
    }

    // orig: R066+0x25AC
    private static OptionHint TalkChance(CardContext c)
    {
        var chance = 0;
        if (c.HereQuery(0) > -10 && !c.EventAtHere(Core.Events.EventKinds.Hostile) && !c.Hostile())
        {
            var charm = Math.Max(c.Attribute(c.Leader, (int)Attribute.Charisma), c.Skill(c.Leader, (int)Skill.SpeakCommon));
            chance = Math.Clamp(c.HereQuery(0) / 2 + charm, 0, 100);
        }
        return new OptionHint(chance, "", c.Leader);
    }

    // orig: R066+0x24B4
    private void TalkToTheWharfmaster(CardContext c, CardMenu menu, int chance)
    {
        c.PassHours(1);
        menu[2] = OptionState.Shown;
        if (c.Succeeds(chance))
        {
            ShowPicture(c, DocksPicture);
            c.Screen.Text(5);
            c.AdjustReputation(c.Here, 2, 4);
            c.Next = c.IsDaylight ? 9 : 10;
            return;
        }
        if (c.HereQuery(0) <= -10)
        {
            ShowPicture(c, GuardsPicture);
            c.Screen.Text(4);
        }
        else
        {
            ShowPicture(c, DocksPicture);
            c.Screen.Text(6);
        }
        ShowPicture(c, WharfPicture);
        c.Next = Number;
    }

    // orig: R066+0x273E
    private static OptionHint SneakChance(CardContext c)
    {
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        var streetwise = c.PartyAverageSkill((int)Skill.Streetwise);
        var value = streetwise <= stealth ? stealth : (streetwise - stealth) / 2 + stealth;
        return new OptionHint(Math.Clamp(value, 0, 99), "", OptionHint.WholeParty);
    }

    /// <summary>
    /// Wait for night (by day) and slip ashore; caught, the party is marched off (card 1) and the
    /// streets become where it "came from".
    /// </summary>
    // orig: R066+0x263E
    private static void SneakAshore(CardContext c, int chance)
    {
        if (c.IsDaylight)
        {
            c.PassHours(Core.Time.TimeOfDay.HoursUntil(c.State.Date.Hour, 21));
        }
        if (c.Succeeds(chance))
        {
            c.PartyMaxAttribute((int)Attribute.Agility);
            c.Variables.Chosen[0] = c.BestMember;
            ShowPicture(c, ChasePicture);
            c.Screen.Text(7);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 7, 10, 0);
            c.Next = 10;
            return;
        }
        c.PassHours(1);
        ShowPicture(c, ChasePicture);
        c.Screen.Text(8);
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, 0);
        // DIVERGENCE: the original sets the card before the previous one (DS:A88F) and earlier-screen (DS:A88D) to the street card (R066+0x271F,
        // R066+0x2732); the main loop then sets DS:A88F = DS:A88D (the street card) and DS:A88D = this card (root 03A1:000A), and DS:A88F has no reader
        // in the EXE (only these two stores), so it is not kept; FinishPass sets PreviousCard as the loop does.
        c.Next = 1;
    }

    // orig: R066+0x2786 (hint R066+0x285E)
    private void Pray(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.PassHours(1);
        c.Variables.Chosen[0] = c.MenuMember;
        // C: answer - 1 == 0: only an answer of exactly 1 works.
        if (answer == 1)
        {
            c.AdjustReputation(c.Here, 10, 10);
            ShowPicture(c, ChasePicture);
            c.Screen.Text(9);
            c.Next = c.IsDaylight ? 9 : 10;
            return;
        }
        ShowPicture(c, DocksPicture);
        c.Screen.Text(0x0A);
        ShowPicture(c, WharfPicture);
        c.Next = Number;
    }

    /// <summary>Sail on to the next port: the journey takes up to half a day and the card starts over there (same text block).</summary>
    // orig: R066+0x2896, 0e66 R066+0x29A6, 0f76 R066+0x2AB6, 1086 R066+0x2BC6 (hints 0e2e/0f3e/104e/115e)
    private void SailOn(CardContext c, CardMenu menu, int port)
    {
        c.PassHours(c.Random(12));
        var city = c.City.Word(PortWords[port]);
        c.State.CurrentLocation = city;
        // C: the original reads that city's DARKLAND.CTY record into city-record and its DARKLAND.DSC description.
        c.LoadCity(city);
        LookAtThePort(c, menu);
        c.Next = Number;
    }
}
