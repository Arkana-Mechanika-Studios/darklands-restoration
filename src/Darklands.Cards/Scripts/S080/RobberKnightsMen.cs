using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubi02": in a robber knight's territory the party meets his armed men, from ambush if
/// its woodcraft allows (much likelier when the party was lying in wait). Attack, demand
/// their surrender, greet them, let them pass, or (caught in the open) surrender.
/// </summary>
// orig: R080+0x06DC (18A5:000C) matched; tests: CardSmokeTests
// orig: R080+0x0EA6 (18A5:07D6) matched; R080+0x0EDE (18A5:080E) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered);
// text 0 (the territory's first description) is never shown by this script.
public sealed class RobberKnightsMen : Card
{
    public const int CardNumber = 0x92;

    private const int AmbushPicture = 145;
    private const int CaughtPicture = 33;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.PlayMusic(6);
        c.Next = CardNumber;
        // C: the woodcraft roll is drawn as the right operand of >=, after the skill is read.
        int block;
        if (c.EncounterApproach == 1)
        {
            if (c.Skill(Who.Leader, 0x12) >= c.Random(100))
            {
                Show(c, AmbushPicture);
                block = 1;
            }
            else
            {
                Show(c, CaughtPicture);
                block = 2;
            }
        }
        else if (c.Skill(Who.Leader, 0x12) / 3 >= c.Random(100))
        {
            Show(c, AmbushPicture);
            block = 3;
        }
        else
        {
            Show(c, CaughtPicture);
            block = 4;
        }
        // C: party-map-column is passed as a long: the party's map x and y.
        c.Variables.Location = c.NearestLocationOfType(c.State.MapPosition.X, c.State.MapPosition.Y, 2);
        c.RandomName(c.Variables.Location + 0x44C, 0, 0);
        var menu = Menu(c, block);
        menu.Enable(0, 1, 2, 3);
        if (block == 2)
        {
            menu[1] = OptionState.Hidden;
            menu[3] = OptionState.Hidden;
        }
        if (block == 4)
        {
            menu[1] = OptionState.Hidden;
            menu[3] = OptionState.Hidden;
            menu[4] = OptionState.Enabled;
        }
        c.SetCardSaints();
        c.SetCardItems();

        return menu.Run(
            // orig: R080+0x0B24 (18A5:0454) matched; R080+0x0B3E (18A5:046E) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.PracticeSkill(c.Leader, 0x12, 1, 10, 0);
                Fight(c, block);
            }, OptionHint.Saying(CardTexts.EnterCombat)),
            new MenuLine(chance => DemandSurrender(c, block, chance), () => SurrenderChance(c)),
            new MenuLine(() => GreetThem(c, block), hint: () => GreetingHint(c)),
            // orig: R080+0x0D9C (18A5:06CC) matched; R080+0x0DAA (18A5:06DA) matched; tests: CardSmokeTests
            new MenuLine(() => GoOn(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() => GiveUp(c), OptionHint.Saying(SuccessAssured)));
    }

    private static void GoOn(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    /// <summary>One raid more against the knight (his castle's fact value 3/2).</summary>
    private static void CountTheRaid(CardContext c) =>
        c.Events.Update(-2, c.Here, 0x1C, 2, 1, 3, EventUpdate.Add);

    // orig: R080+0x0980 (18A5:02B0) matched; tests: CardSmokeTests
    private static void Fight(CardContext c, int block)
    {
        var raids = c.Events.FactValue(3, 2, c.Here);
        if (block == 1 || block == 3)
        {
            // C: from ambush the original first fights a battle of kind 0x31 and discards its result.
            var ambushSize = c.Random(5) + 3;
            c.RunBattle(0x31, -1, 7, 1, ambushSize, 0, 2, 1, -1, raids, 0);
        }
        var size = c.Random(5) + 3;
        var result = block == 4
            ? c.RunBattle(0x30, -1, 7, 1, size, 0, 2, 1, -1, raids, 0)
            : c.RunBattle(0x2F, -1, 7, 1, size, 0, 2, 1, -1, raids, 0);
        switch (result)
        {
            case 0:
            case 1:
                ShowPicture(c, 3);
                c.Screen.Text(6);
                CountTheRaid(c);
                c.PassHours(0);
                GoOn(c);
                break;
            case 2:
                c.ReportAway();
                ShowPicture(c, 287);
                c.Screen.Text(7);
                c.PassHours(1);
                GoOn(c);
                break;
            case 3:
            case 4:
                ShowPicture(c, 177);
                c.Screen.Text(8);
                c.LoseItems(Who.Everybody);
                c.PassHours(3);
                GoOn(c);
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party facing the men.
                break;
        }
    }

    // orig: R080+0x0C2A (18A5:055A) matched; tests: CardSmokeTests
    private static OptionHint SurrenderChance(CardContext c)
    {
        var sum = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Able)
            {
                sum += c.BestWeaponSkill(i);
            }
        }
        return OptionHint.ChanceOf(Math.Clamp(sum / 4, 0, 99), OptionHint.WholeParty);
    }

    // orig: R080+0x0B76 (18A5:04A6) matched; tests: CardSmokeTests
    private static void DemandSurrender(CardContext c, int block, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 286);
            c.Screen.Text(9);
            c.PassHours(3);
            CountTheRaid(c);
            GoOn(c);
            return;
        }
        ShowPicture(c, CaughtPicture);
        c.Screen.Text(0x0A);
        Fight(c, block);
    }

    // orig: R080+0x0D56 (18A5:0686) matched; tests: CardSmokeTests
    // C: the hint shows (charisma × 2) / (raids + 1), at most 69; the roll uses × 3, at most 99.
    private static OptionHint GreetingHint(CardContext c)
    {
        var raids = c.Events.FactValue(3, 2, c.Here);
        return OptionHint.ChanceOf(Math.Clamp((c.Attribute(c.Leader, 5) << 1) / (raids + 1), 1, 0x45), OptionHint.WholeParty);
    }

    /// <summary>The men may let a charming party go: the fewer raids, the likelier.</summary>
    // orig: R080+0x0C78 (18A5:05A8) matched; tests: CardSmokeTests
    private static void GreetThem(CardContext c, int block)
    {
        var raids = c.Events.FactValue(3, 2, c.Here);
        var chance = Math.Clamp(c.Attribute(c.Leader, 5) * 3 / (raids + 1), 1, 99);
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 287);
            c.Screen.Text(0x0B);
            c.PassHours(1);
            GoOn(c);
            return;
        }
        ShowPicture(c, CaughtPicture);
        c.Screen.Text(0x0A);
        Fight(c, block);
    }

    /// <summary>Caught in the open: half the money and a fifth of the goods are taken.</summary>
    // orig: R080+0x0DE2 (18A5:0712) matched; R080+0x0E6E (18A5:079E) matched; tests: CardSmokeTests
    private static void GiveUp(CardContext c)
    {
        // C: a long halved, then passed as a 16-bit int of pfennigs.
        c.State.Party.Purse = c.State.Party.Purse.Subtract(0, 0, (short)(c.State.Party.Purse.TotalPfennigs / 2));
        c.Variables.Money[0] = c.State.Party.Purse;
        c.KeepItems(Who.Everybody, 80);
        ShowPicture(c, 177);
        c.Screen.Text(5);
        c.PassHours(3);
        GoOn(c);
    }
}
