using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Villa02": the Schulz, the hamlet's headman. Ask for information (which may bring a task
/// against a robber knight), ask for help (he may join the party or give a florin), pray to a
/// saint, accuse the hamlet of satanic practices (in a village under evil, its cultists
/// attack), or leave.
/// </summary>
// orig: R077+0x15EC (1996:000C) matched; tests: CardSmokeTests
// orig: R077+0x1E5E (1996:087E) matched; R077+0x1ECA (1996:08EA) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
public sealed class VillageSchulz : Card
{
    public const int CardNumber = 0x86;

    private const int Picture = 246;
    private const int SchulzPicture = 247;
    private const int VillageEvil = 0x3D;
    private const int AskedForHelp = 0x26;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 3, 4);
        menu[1] = c.EventAtHere(AskedForHelp) ? OptionState.Shown : OptionState.Enabled;
        c.SetCardItems();
        c.SetCardSaints(0x11, 0x25, 0x42, 0x43);
        menu[2] = c.SaintsUsable();

        return menu.Run(
            new MenuLine(() => AskForInformation(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(() =>
            {
                AskForHelp(c);
                menu[1] = OptionState.Shown;
            }, OptionHint.Saying(NoRisk)),
            // orig: R077+0x1A9C (line 2); R077+0x1F02 (1996:0922) matched; R077+0x1F8C (1996:09AC) matched; R077+0x2016 (1996:0A36) matched;
            // R077+0x20A0 (1996:0AC0) matched; tests: CardSmokeTests
            new MenuLine(() => Pray(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() => AccuseTheHamlet(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R077+0x1E14 (1996:0834) matched; R077+0x1E26 (1996:0846) matched; tests: CardSmokeTests
            new MenuLine(() => { c.PassHours(0); c.Next = 0x85; }, OptionHint.Saying(SuccessAssured)));
    }

    // orig: R077+0x178E (1996:01AE) matched; R077+0x1920 (1996:0340) matched; tests: CardSmokeTests
    private static void AskForInformation(CardContext c)
    {
        c.PassHours(1);
        if (c.EventAtHere(VillageEvil))
        {
            c.Screen.Text(1);
        }
        else
        {
            if (c.LocationQuery(c.Here, 0) > 50)
            {
                ShowPicture(c, SchulzPicture);
                c.Screen.Text(2);
            }
            else
            {
                // C: && short-circuits: the 1-in-10 roll is drawn only when no such task runs.
                if (!c.Events.QuestWithValue(3, 9, c.Here) && !c.EventTypeAtHere(7, 9) && c.Random(100) < 10)
                {
                    TheRobberKnightTask(c);
                    return;
                }
                c.Variables.Location = c.Here;
                ShowPicture(c, SchulzPicture);
                c.Screen.Text(3);
            }
            ShowPicture(c, Picture);
        }
        c.Next = CardNumber;
    }

    /// <summary>The Schulz asks the party to deal with a robber knight near the village.</summary>
    private static void TheRobberKnightTask(CardContext c)
    {
        c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2) * 5, 0, 0);
        c.RandomName(c.Here + 2000, 0, 0);
        // C: nearest_loc_type takes the village's position word pair (+4, +6) as one long.
        var village = c.State.Locations[c.Here];
        c.Variables.Location = c.NearestLocationOfType(village.X, village.Y, 2);
        c.StartQuest3(9, c.Variables.Location, c.Here + 2000, 0x0F, 1, 0);
        c.PassHours(1);
        c.State.SavedCard = CardNumber;
        c.State.SavedCard2 = c.Variables.Location;
        c.Next = 0x90;
    }

    // orig: R077+0x1958 (1996:0378) matched; R077+0x1A64 (1996:0484) matched; tests: CardSmokeTests
    private static void AskForHelp(CardContext c)
    {
        if (c.LocationQuery(c.Here, 0) >= 50)
        {
            if (c.PartySize < 5)
            {
                ShowPicture(c, SchulzPicture);
                c.Screen.Text(4);
                ShowPicture(c, Picture);
                c.RandomName(c.Here + 2000, 0, 0);
                // The Schulz joins for 90 days (shield picture "F01" in the original).
                c.AddMember(c.Variables.Named[0], "Schulz", "F01", 0x870, 9);
            }
            else
            {
                // BUG: a full party gets text 6 ("Please accept this florin") but no florin; the low-standing branch adds it.
                c.Screen.Text(6);
            }
        }
        else if (c.Random(1000) <= c.State.Party.Fame)
        {
            c.Screen.Text(6);
            c.State.Party.Purse = c.State.Party.Purse.Add(1, 0, 0);
        }
        else
        {
            c.Screen.Text(5);
        }
        c.EventHere(AskedForHelp, 48);
        c.PassHours(1);
        c.Next = 0x85;
    }

    // orig: R077+0x1A9C (1996:04BC) matched; R077+0x1B8A (1996:05AA) matched; tests: CardSmokeTests
    private static void Pray(CardContext c)
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
            c.Screen.Text(7);
        }
        else if (c.EventAtHere(VillageEvil))
        {
            ShowPicture(c, 113);
            c.Screen.Text(8);
        }
        else
        {
            ShowPicture(c, 289);
            c.Screen.Text(9);
        }
        ShowPicture(c, Picture);
        c.PassHours(0);
        c.Next = CardNumber;
    }

    // orig: R077+0x1BC2 (1996:05E2) matched; R077+0x1DDC (1996:07FC) matched; tests: CardSmokeTests
    private static void AccuseTheHamlet(CardContext c)
    {
        if (c.EventAtHere(VillageEvil))
        {
            ShowPicture(c, 160);
            c.Screen.Text(0x0A);
            // C: arguments are evaluated right to left: the second group (rand_mod(4)) is drawn
            // before the first (rand_mod(3)); party_lore_level draws nothing.
            var third = c.PartyLoreLevel() / 4 + 1;
            var second = c.Random(4) + c.PartyLoreLevel() / 3 + 1;
            var first = c.Random(3) + c.PartyLoreLevel() / 4 + 3;
            switch (c.RunBattle(0x61, c.Here, 0x0B, first, second, 4, third, 1, -1, 0, 0))
            {
                case 0:
                    ShowPicture(c, 286);
                    c.Screen.Text(0x0D);
                    c.PassHours(1);
                    c.State.SavedCard = 0x8A;
                    c.Next = 0x8E;
                    break;
                case 1:
                case 2:
                    c.DropAway();
                    ShowPicture(c, 286);
                    c.Screen.Text(0x0B);
                    c.PassHours(1);
                    c.Next = 0x0C;
                    break;
                case 3:
                case 4:
                    ShowPicture(c, 144);
                    c.Screen.Text(0x0C);
                    c.WaitUntilHour(0);
                    c.Next = 0x8F;
                    break;
                default:
                    // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party with the Schulz.
                    break;
            }
            // The village now greets the party with its cultists' card.
            c.State.Locations[c.Here].ArrivalCard = 0x8A;
            return;
        }
        ShowPicture(c, 27);
        c.Screen.Text(0x0E);
        c.AddSkill(Who.Leader, 9, -2);
        c.PassHours(6);
        c.Next = 0x0C;
    }
}
