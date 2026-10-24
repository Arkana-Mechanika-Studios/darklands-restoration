using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Devil01": the party sets foot on the Devil's Bridge and the demon appears to claim the
/// first soul to cross. Offer one of the party (chosen at random) as his fee, argue the
/// contract's technicalities (and fight, with a member lost into his purse unless the
/// argument wins), or call a saint. Any way across the village is left to the hamlet card,
/// with a lasting mark on the place.
/// </summary>
// orig: R088+0x0210 (1859:0000) matched; tests: CardSmokeTests
public sealed class DevilsBridgeCrossing : Card
{
    public const int CardNumber = 0xB1;

    private const int Picture = 112;
    private const int FightPicture = 113;
    private const int VictoryPicture = 114;
    private const int NoSaintPicture = 115;

    /// <summary>The village square after the bridge is crossed.</summary>
    private const int VillageSquare = 0x85;

    private static readonly int[] Saints = [0x24, 0x70, 0x7D];

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // The original calls a do-nothing routine here (a stub left in the script).
        // orig: R088+0x08E8 (1859:06D8) matched; R088+0x094A (1859:073A) matched; tests: CardSmokeTests
        // DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
        c.SetCardItems();
        c.SetCardSaints(Saints);
        var menu = Menu(c);
        menu.Enable(0, 1);
        menu[2] = c.SaintsUsable();

        // C: the start-up cannot leave this card, so the menu loop test is always true.
        return menu.Run(
            // orig: R088+0x0354 (1859:0144) matched; R088+0x0408 (1859:01F8) matched; tests: CardSmokeTests
            new MenuLine(() => OfferOneOfThePartyAsTheFee(c), OptionHint.Saying(SuccessAssured)),
            // orig: R088+0x0440 (1859:0230) matched; R088+0x0792 (1859:0582) matched; tests: CardSmokeTests
            new MenuLine(chance => ArgueTheContract(c, chance), hint: () => LawChance(c)),
            // orig: R088+0x07B4 (1859:05A4) matched; R088+0x08B0 (1859:06A0) matched; tests: CardSmokeTests
            // orig: R088+0x0982 (1859:0772) matched; R088+0x0A0C (1859:07FC) matched; R088+0x0A96 (1859:0886) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint));
    }

    /// <summary>The next occupied slot from <paramref name="start"/>, wrapping after slot 4.</summary>
    private static int OccupiedSlotFrom(CardContext c, int start)
    {
        // C: the original's first member (offering a member) tests the drawn slot with an if and then steps with a do/while; the others use a while loop. Both end on the first occupied slot at or after the draw.
        var slot = start;
        while (c.Status(slot) == CharacterStatus.Empty)
        {
            slot = (slot + 1) % 5;
        }
        return slot;
    }

    /// <summary>Leaving the bridge for the village: the place is marked and the square is shown.</summary>
    private static void CrossIntoTheVillage(CardContext c)
    {
        c.State.Locations[c.Here].ArrivalCard = VillageSquare;
        c.LocationSet(c.Here, 0x20, 0x10);
        c.Next = VillageSquare;
    }

    private static void OfferOneOfThePartyAsTheFee(CardContext c)
    {
        var victim = OccupiedSlotFrom(c, c.Random(c.PartySize));
        c.Variables.Chosen[0] = victim;
        c.SetStatus(victim, CharacterStatus.Empty);
        c.DeleteMember(victim);
        ShowPicture(c, FightPicture);
        c.Screen.Text(1);
        CrossIntoTheVillage(c);
    }

    private static OptionHint LawChance(CardContext c)
    {
        var chance = c.PartyMaxSkill(0x0B);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    /// <summary>
    /// Arguing the contract wins the party its freedom when the roll is within the religion
    /// chance; otherwise the demon seizes a member, and a battle follows either way.
    /// </summary>
    private static void ArgueTheContract(CardContext c, int chance)
    {
        var roll = c.Random(100);
        ShowPicture(c, FightPicture);
        var argumentWins = roll <= chance;
        if (argumentWins)
        {
            c.Screen.Text(2);
        }
        else
        {
            // The demon puts the member in his purse (the slot is empty meanwhile, not deleted).
            var captive = OccupiedSlotFrom(c, c.Random(c.PartySize));
            c.Variables.Chosen[1] = captive;
            c.SetStatus(captive, CharacterStatus.Empty);
            c.Screen.Text(3);
            c.Screen.Text(4);
        }

        // C: arguments are evaluated right to left: rand_mod(5) + 4 (the second group) is drawn before rand_mod(3).
        var second = c.Random(5) + 4;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x37, c.Here, 0x35, first, second, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, VictoryPicture);
                if (argumentWins)
                {
                    c.Screen.Text(5);
                }
                else
                {
                    c.SetStatus(c.Variables.Chosen[1], CharacterStatus.Able);
                    c.Screen.Text(6);
                }
                break;
            case 1:
                c.DropAway();
                // C: Chosen[1] is the member seized in the failed argument; after a won argument it is stale.
                if (argumentWins)
                {
                    c.SetStatus(c.Variables.Chosen[1], CharacterStatus.Able);
                }
                c.Screen.Text(7);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                return;
            case 2:
                c.DropAway();
                if (argumentWins)
                {
                    c.SetStatus(c.Variables.Chosen[1], CharacterStatus.Able);
                }
                c.Screen.Text(8);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                return;
            case 3:
            case 4:
                if (argumentWins)
                {
                    c.Variables.Chosen[0] = OccupiedSlotFrom(c, c.Random(c.PartySize));
                    c.SetStatus(c.Variables.Chosen[0], CharacterStatus.Empty);
                    c.DeleteMember(c.Variables.Chosen[0]);
                    ShowPicture(c, VictoryPicture);
                    c.Screen.Text(9);
                }
                else
                {
                    c.Variables.Chosen[1] = OccupiedSlotFrom(c, c.Random(c.PartySize));
                    c.SetStatus(c.Variables.Chosen[1], CharacterStatus.Empty);
                    c.DeleteMember(c.Variables.Chosen[1]);
                    ShowPicture(c, Picture);
                    c.Screen.Text(0x0A);
                }
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results (the jump table's negative cases too) leave the party on the bridge with nothing done.
                return;
        }
        CrossIntoTheVillage(c);
    }

    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    c.NameToSlot(1, 0x24, 0);
                    break;
                case 1:
                    c.NameToSlot(1, 0x70, 0);
                    break;
                case 2:
                    c.NameToSlot(1, 0x7D, 0);
                    break;
                default:
                    // DORMANT: slot 3 holds no saint.
                    break;
            }
            ShowPicture(c, Picture);
            c.Screen.Text(0x0B);
            c.Screen.Text(0x0C);
            CrossIntoTheVillage(c);
            return;
        }
        ShowPicture(c, NoSaintPicture);
        c.Screen.Text(0x0D);
        c.Next = CardNumber;
    }
}
