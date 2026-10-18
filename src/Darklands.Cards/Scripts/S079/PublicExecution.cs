using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Execu01": the headsman waits in the city square. Refuse to struggle, try to break the
/// ropes, or pray for deliverance. A last-moment reprieve may come from the city's ruler, the
/// abbot, the bankers, a quest offer or the mob; otherwise one member after another dies.
/// </summary>
// Reconstructed from the disassembly (no C):
// orig: R079+0x0A08 (18D8:0008) asm; tests: CardSmokeTests
// orig: R079+0x13BE (18D8:09BE) asm; R079+0x142A (18D8:0A2A) asm; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
public sealed class PublicExecution : Card
{
    public const int CardNumber = 0x8D;

    /// <summary>The card's MSG name (the card table has none for this card).</summary>
    private const string MsgName = "$Execu01";

    private const int Picture = 350;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        c.Next = CardNumber;
        // C: the script's copy of the reputation (cur_rep, DS:EE74); the fight lowers it.
        var standing = c.LocationQuery(c.Here, 0);
        c.SetCardItems();
        c.SetCardSaints(5, 0x3F, 0x4E, 0x50);
        var menu = Menu(c);
        menu.Enable(0, 1);
        menu[2] = c.SaintsUsable();
        // C: leader is a signed char.
        c.Variables.Chosen[0] = (sbyte)c.Leader;

        return menu.Run(
            // orig: R079+0x10DA (18D8:06DA) asm; R079+0x10E4 (18D8:06E4) asm; tests: CardSmokeTests
            new MenuLine(() => Verdict(c, -1, ref standing), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => BreakTheRopes(c, StrengthChance(c).Chance, ref standing), hint: () => StrengthChance(c)),
            // orig: R079+0x1462 (18D8:0A62) asm; R079+0x14EC (18D8:0AEC) asm; R079+0x1576 (18D8:0B76) asm; R079+0x1600 (18D8:0C00) asm; tests: CardSmokeTests
            new MenuLine(() => PrayForDeliverance(c, ref standing), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint));
    }

    private static int TownSquare(CardContext c) => c.IsDaylight ? 0x12 : 0x19;

    /// <summary>A random member still in the party.</summary>
    private static int SomeMember(CardContext c)
    {
        var who = c.Random(5);
        while (c.Status(who) == CharacterStatus.Empty)
        {
            who = (who + 1) % 5;
        }
        return who;
    }

    /// <summary>
    /// Who stops the execution: the city's ruler (by reputation), the abbot (by piety), the
    /// bankers (by florins), the mob (by notoriety), a quest offer (by fame twice, the second
    /// only without event 0x25 here), or nobody. A saint's prayer helps: St. 5 brings the
    /// ruler or the abbot, St. 0x4E the ruler, St. 0x50 any of them (and halves the dice).
    /// </summary>
    // orig: R079+0x0CF6 (18D8:02F6) asm; tests: CardSmokeTests
    private static void Verdict(CardContext c, int saint, ref int standing)
    {
        // UNCERTAIN: the abbot's score starts uninitialised when fact (2, 0) holds here; taken as 0.
        var abbot = 0;
        _ = c.Events.Fact(2, 0, c.Here);
        var piety = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Empty)
            {
                continue;
            }
            // C: the four values are fetched in this order: skill 9, skill 8, attribute 5, skill 11.
            var sum = c.Skill(i, 9) + c.Skill(i, 8) + c.Attribute(i, 5) + c.Skill(i, 0x0B);
            if (sum > piety)
            {
                piety = sum;
            }
        }
        abbot += piety / 10;
        var ruler = standing / 10;
        var dice = saint == 0x50 ? 50 : 100;
        int rescuer;
        if (c.Random(dice) <= ruler)
        {
            rescuer = 0;
        }
        else if (c.Random(dice) <= abbot)
        {
            rescuer = 1;
        }
        // C: compared with the purse's florins word (DS:906B).
        else if (c.Random(dice) <= c.State.Party.Purse.Florins)
        {
            rescuer = 2;
        }
        else if (c.Random(dice) <= Math.Abs(standing / 5))
        {
            rescuer = 3;
        }
        else if (c.Random(dice) <= c.State.Party.Fame / 10)
        {
            rescuer = 4;
        }
        // C: && short-circuits after the roll.
        else if (c.Random(dice) <= c.State.Party.Fame / 10 && !c.EventAtHere(0x25))
        {
            rescuer = 5;
        }
        else
        {
            rescuer = 6;
        }
        if (rescuer == 6)
        {
            if (saint == 5)
            {
                rescuer = c.Random(2);
            }
            else if (saint == 0x4E)
            {
                rescuer = 0;
            }
            else if (saint == 0x50)
            {
                rescuer = c.Random(6);
            }
        }
        c.PassHours(1);
        switch (rescuer)
        {
            case 0:
                // The ruler's pardon; the city now loathes the party.
                ShowPicture(c, Picture);
                c.Screen.Text(7);
                c.State.Locations[c.Here].Reputation = -9;
                c.Next = TownSquare(c);
                break;
            case 1:
                ShowPicture(c, 269);
                c.Screen.Text(8);
                c.Next = c.IsDaylight ? 0x34 : 0x35;
                break;
            case 2:
                // C: the text says the bank money is confiscated; the script takes nothing.
                ShowPicture(c, Picture);
                c.Screen.Text(9);
                c.PassHours(5);
                c.Next = TownSquare(c);
                break;
            case 3:
                ShowPicture(c, 250);
                c.Screen.Text(0x0A);
                ArmedByTheMob(c);
                FightTheGuards(c, ref standing);
                break;
            case 4:
            case 5:
                c.Next = 0x84;
                break;
            case 6:
                if (c.PresentCount() == 0)
                {
                    // DIVERGENCE: with nobody left the original searches the empty party for ever; here the game ends.
                    c.Next = 0x62;
                    break;
                }
                var victim = SomeMember(c);
                c.Variables.Chosen[0] = victim;
                ShowPicture(c, Picture);
                c.Screen.Text(1);
                ShowPicture(c, Picture);
                c.DeleteMember(victim);
                c.PassHours(0);
                c.Variables.Chosen[0] = (sbyte)c.Leader;
                c.Next = CardNumber;
                break;
        }
    }

    /// <summary>The mob hands each member a weapon of his best kind.</summary>
    private static void ArmedByTheMob(CardContext c)
    {
        for (var i = 0; i < 5; i++)
        {
            if (c.Status(i) == CharacterStatus.Empty)
            {
                continue;
            }
            c.BestWeaponSkill(i);
            switch (c.BestSkillFound)
            {
                case 0:
                case 5:
                case 6:
                    c.AddItem(i, 4);
                    break;
                case 1:
                case 2:
                    c.AddItem(i, 0x0E);
                    break;
                case 3:
                case 4:
                    c.AddItem(i, 0x15);
                    break;
                default:
                    // DORMANT: no skill number above 6.
                    break;
            }
        }
    }

    // orig: R079+0x0B64 (18D8:0164) asm; tests: CardSmokeTests
    private static void FightTheGuards(CardContext c, ref int standing)
    {
        // C: arguments are evaluated right to left: rand_mod(4) (second group) before rand_mod(2).
        var third = c.PartyLoreLevel() % 3 + 1;
        var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
        var first = c.Random(2) + c.PartyLoreLevel() / 4 + 1;
        var result = c.RunBattle(0x1B, -1, 3, first, second, 0x17, third, 1, -1, 0, 0);
        standing -= c.Random(15) + 5;
        c.LocationSet(c.Here, 0, standing);
        var hours = 2000 / c.LocationQuery(c.Here, 2);
        switch (result)
        {
            case >= 0 and <= 2:
                c.ReportAway();
                ShowPicture(c, 46);
                c.Screen.Text(0x0C);
                c.Events.Create(-2, c.Here, 0x1E, 0x12, 1, 0x63, 0, hours, 0, 0);
                c.PassHours(1);
                c.Next = 0x7A;
                break;
            case 3:
            case 4:
                ShowPicture(c, Picture);
                c.Screen.Text(0x0D);
                ShowPicture(c, Picture);
                c.PassHours(3);
                c.Next = CardNumber;
                break;
            default:
                // DORMANT: other battle results go straight to the guard's watch below.
                break;
        }
        c.EventHere(0x11, standing <= -75 ? 480 : 240);
    }

    // orig: R079+0x11F4 (18D8:07F4) asm; tests: CardSmokeTests
    // UNCERTAIN: the member is DS:9A44 (the attribute queries' member), read as BestMember.
    private static OptionHint StrengthChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyMaxAttribute(1), (byte)c.BestMember);

    // orig: R079+0x111C (18D8:071C) asm; tests: CardSmokeTests
    private static void BreakTheRopes(CardContext c, int chance, ref int standing)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 236);
            c.Screen.Text(6);
            c.PassHours(0);
            c.AddItem(Who.Everybody, 7);
            FightTheGuards(c, ref standing);
            return;
        }
        ShowPicture(c, Picture);
        // DIVERGENCE: an empty party (the original would search for ever) names slot 0.
        c.Variables.Chosen[0] = c.PresentCount() > 0 ? SomeMember(c) : 0;
        c.Screen.Text(0x0B);
        Verdict(c, -1, ref standing);
    }

    // orig: R079+0x1212 (18D8:0812) asm; R079+0x1386 (18D8:0986) asm; tests: CardSmokeTests
    private static void PrayForDeliverance(CardContext c, ref int standing)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 0)
        {
            ShowPicture(c, 288);
            c.Screen.Text(4);
            Verdict(c, -1, ref standing);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.NameToSlot(1, 5, 0);
                ShowPicture(c, 355);
                c.Screen.Text(2);
                Verdict(c, 5, ref standing);
                break;
            case 1:
                // St. Gregory Thaumaturgus sends a storm and the party escapes.
                ShowPicture(c, 46);
                c.Screen.Text(3);
                c.PassHours(3);
                c.Next = c.IsDaylight ? 9 : 0x0A;
                break;
            case 2:
            case 3:
                c.NameToSlot(1, c.CardSaints[c.MenuSlot], 0);
                ShowPicture(c, 355);
                c.Screen.Text(2);
                Verdict(c, c.CardSaints[c.MenuSlot], ref standing);
                break;
            default:
                // DORMANT: no slot outside 0..3.
                break;
        }
    }
}
