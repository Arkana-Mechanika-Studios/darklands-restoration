using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Raubi03": the robber knight's tower (or, once broken open, his rude fort). Lay siege,
/// attack it with alchemy, ask to come inside, challenge him to single combat, sneak in after
/// dark, call a saint, storm it, or go away. His death ends the quest and the tower.
/// </summary>
// orig: R080+0x0F16 (1929:0006) matched; tests: CardSmokeTests
// orig: R080+0x1FD2 (1929:10C2) matched; R080+0x2068 (1929:1158) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 8..19 (never offered).
public sealed class RobberKnightsTower : Card
{
    public const int CardNumber = 0x93;

    private const int TowerPicture = 21;
    private const int FortPicture = 57;
    private const int SortiePicture = 33;
    private const int BeatenPicture = 177;
    private const int WelcomePicture = 52;
    private const int RefusedPicture = 260;

    /// <summary>Inside the tower: the knight's hall, and the tower's interior.</summary>
    private const int TheHall = 0x95;
    private const int Inside = 0x94;

    /// <summary>The party is known at the tower (no more polite requests).</summary>
    private const int Recognised = 0x26;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, TowerPicture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 2, 3, 4, 7);
        menu[6] = OptionState.Shown;
        c.RandomName(c.Here + 0x44C, 0, 0);
        c.SetCardSaints();
        c.SetCardItems();
        if (c.EventAtHere(Recognised))
        {
            menu[2] = OptionState.Shown;
        }
        if (c.Events.Fact(3, 5, c.Here))
        {
            menu[6] = OptionState.Enabled;
        }
        int block;
        // C: the picture buffer is the card's: the fort replaces the tower for later redraws.
        var picture = TowerPicture;
        if (c.Events.Fact(3, 0x27, c.Here))
        {
            menu[6] = OptionState.Enabled;
            picture = FortPicture;
            ShowPicture(c, picture);
            block = 0x18;
        }
        else
        {
            block = 0;
        }
        c.SetCardItems(0x66, 0x63, 0x67);
        c.SetCardSaints(0x29, 0x2E, 0x40, 0x72);
        menu.Block = block;
        menu[1] = c.ItemsUsable();
        menu[5] = c.SaintsUsable();

        // Every approach but the alchemy line and leaving makes the party known at the tower.
        void Recognise()
        {
            c.EventHere(Recognised, 6480);
            menu[2] = OptionState.Shown;
        }

        return menu.Run(
            // orig: R080+0x155A (1929:064A) matched; R080+0x16A0 (1929:0790) matched; tests: CardSmokeTests
            new MenuLine(() => { Recognise(); LaySiege(c); }, OptionHint.Saying("This may take some time")),
            // orig: R080+0x16CA (1929:07BA) matched; R080+0x1966 (1929:0A56) matched; tests: CardSmokeTests
            // orig: R080+0x20A0 (1929:1190) matched; R080+0x20DC (1929:11CC) matched; R080+0x2118 (1929:1208) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
                Recognise();
                if (AttackWithAlchemy(c, picture))
                {
                    menu[6] = OptionState.Enabled;
                }
            }, OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint()),
            // orig: R080+0x199E (1929:0A8E) matched; R080+0x1ADE (1929:0BCE) matched; tests: CardSmokeTests
            new MenuLine(() => AskToComeInside(c, Recognise), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R080+0x1B16 (1929:0C06) matched; R080+0x1C46 (1929:0D36) matched; tests: CardSmokeTests
            new MenuLine(chance => { Recognise(); ChallengeTheKnight(c, chance); }, hint: () => ChallengeChance(c)),
            // orig: R080+0x1C86 (1929:0D76) matched; R080+0x1D70 (1929:0E60) matched; tests: CardSmokeTests
            new MenuLine(chance => SneakInAfterDark(c, chance, Recognise), hint: () => SneakingChance(c)),
            // orig: R080+0x1DC4 (line 5); R080+0x2154 (1929:1244) matched; R080+0x21DE (1929:12CE) matched; R080+0x2268 (1929:1358) matched;
            // R080+0x22F2 (1929:13E2) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R080+0x1F22 (1929:1012) matched; R080+0x1F5A (1929:104A) matched; tests: CardSmokeTests
            new MenuLine(() =>
            {
                Recognise();
                c.Events.Create(-2, c.Here, 0, 0x1C, 6, 0, 0, 3, 0, 3);
                c.Next = Inside;
            }, OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R080+0x1F92 (1929:1082) matched; R080+0x1F9A (1929:108A) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = 0x0C, OptionHint.Saying(SuccessAssured)));
    }

    private static int Raids(CardContext c) => c.Events.FactValue(3, 2, c.Here);

    private static void CountRaids(CardContext c, int type, int change) =>
        c.Events.Update(-2, c.Here, 0x1C, type, change, 3, EventUpdate.Add);

    /// <summary>The knight is dead: the quest is resolved and the place becomes a ruin.</summary>
    private static void TheKnightIsDead(CardContext c)
    {
        c.Events.Create(-2, c.Here, 0x5B, 0x1C, 1, 0, 0, 0x960, 0, 3);
        c.ResolveQuest(3, c.Here);
        c.EventHere(0x27, 800);
        var tower = c.State.Locations[c.Here];
        tower.Icon = 0x1A;
        tower.Flags |= Core.World.Location.DestroyedFlag;
        tower.ArrivalCard = 0x157;
        tower.EncounterCard = 0x62;
        tower.NoticeRange = 1;
        c.Next = 0x0C;
    }

    /// <summary>The final battle against the knight and his household.</summary>
    // orig: R080+0x11B8 (1929:02A8) matched; tests: CardSmokeTests
    // C: declared without parameters, it is called with 0 or 1, which it ignores.
    private static void TheFinalBattle(CardContext c)
    {
        switch (c.RunBattle(0x75, -2, 0x23, 4, 1, -1, 0, 0, -1, Raids(c), 0))
        {
            case 0:
                ShowPicture(c, 38);
                c.Screen.Text(0x14);
                TheKnightIsDead(c);
                break;
            case 1:
            case 2:
                c.DropAway();
                if (c.BattleTargetSlain)
                {
                    ShowPicture(c, 38);
                    c.Screen.Text(0x14);
                    c.PassHours(1);
                    TheKnightIsDead(c);
                }
                else
                {
                    ShowPicture(c, FortPicture);
                    c.Screen.Text(0x15);
                    c.PassHours(2);
                    c.Next = CardNumber;
                }
                break;
            case 3:
            case 4:
                if (c.BattleTargetSlain)
                {
                    // C: the knight fell but the party lost: the text of his death, without its effects.
                    ShowPicture(c, 38);
                    c.Screen.Text(0x14);
                    c.PassHours(1);
                }
                else
                {
                    ShowPicture(c, BeatenPicture);
                    c.Screen.Text(0x16);
                    c.PassHours(24);
                    c.Injure(Who.Everybody, 0, 1, 20);
                    c.LoseItems(Who.Everybody);
                    CountRaids(c, 2, -1);
                }
                c.Next = 0x0C;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party before the tower.
                break;
        }
    }

    /// <summary>A sortie of the knight's men (kind 0x79 in a siege, 0x3E otherwise).</summary>
    // orig: R080+0x13F6 (1929:04E6) matched; tests: CardSmokeTests
    private static void TheMenSallyForth(CardContext c, bool siege)
    {
        var raids = Raids(c);
        var size = c.Random(5) + 3;
        var result = siege
            ? c.RunBattle(0x79, -2, 3, 2, size, 0, 3, 1, -1, raids, 0)
            : c.RunBattle(0x3E, -2, 3, 2, size, 0, 3, 1, -1, raids, 0);
        switch (result)
        {
            case 0:
                ShowPicture(c, FortPicture);
                c.Screen.Text(0x17);
                c.PassHours(1);
                CountRaids(c, 2, 1);
                c.Next = CardNumber;
                break;
            case 1:
            case 2:
                ShowPicture(c, FortPicture);
                c.Screen.Text(0x15);
                c.PassHours(3);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                ShowPicture(c, BeatenPicture);
                c.Screen.Text(0x16);
                c.PassHours(3);
                c.Next = 0x0C;
                break;
            default:
                // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): other battle results leave the party before the tower.
                break;
        }
    }

    /// <summary>The siege: the more raids the party has made, the sooner the knight himself comes out.</summary>
    private static void LaySiege(CardContext c)
    {
        var raids = Raids(c);
        c.PassHours(c.Random(24));
        ShowPicture(c, 293);
        c.Screen.Text(1);
        if (c.Random(3) + 2 <= raids)
        {
            ShowPicture(c, SortiePicture);
            c.Screen.Text(7);
            TheFinalBattle(c);
            return;
        }
        if (c.Random(11) <= raids + 5)
        {
            ShowPicture(c, SortiePicture);
            c.Screen.Text(8);
            TheMenSallyForth(c, siege: true);
            return;
        }
        ShowPicture(c, SortiePicture);
        c.Screen.Text(9);
        TheMenSallyForth(c, siege: false);
    }

    /// <summary>Slot 0 a thunderbolt, 1 a corrosive, 2 fire. Returns true when the tower may now be stormed.</summary>
    private static bool AttackWithAlchemy(CardContext c, int picture)
    {
        switch (c.MenuSlot)
        {
            case 0:
                CountRaids(c, 3, 1);
                if (c.Random(10) + 1 <= c.Events.FactValue(3, 3, c.Here))
                {
                    ShowPicture(c, 189);
                    c.Screen.Text(2);
                    c.EventHere(0x27, 800);
                    CountRaids(c, 2, 2);
                    TheFinalBattle(c);
                    return false;
                }
                ShowPicture(c, 189);
                c.Screen.Text(3);
                ShowPicture(c, picture);
                c.Events.Create(-2, c.Here, 0x5B, 0x1C, 5, 0, 0, 0x48, 0, 3);
                // C: the raid count is updated with op card-option-flag-6, which was just set to 1 (add).
                CountRaids(c, 2, 1);
                return true;
            case 1:
                ShowPicture(c, 333);
                c.Screen.Text(4);
                ShowPicture(c, picture);
                c.Events.Create(-2, c.Here, 0x5B, 0x1C, 5, 0, 0, 0x48, 0, 3);
                return true;
            case 2:
                CountRaids(c, 4, 1);
                if (c.Random(8) <= c.Events.FactValue(3, 4, c.Here))
                {
                    ShowPicture(c, 31);
                    c.Screen.Text(5);
                    c.PassHours(1);
                    c.EventHere(0x27, 800);
                    CountRaids(c, 2, 2);
                    TheFinalBattle(c);
                    return false;
                }
                ShowPicture(c, 31);
                c.Screen.Text(6);
                ShowPicture(c, picture);
                CountRaids(c, 2, 1);
                return false;
            default:
                // DORMANT: slot 3 holds no potion.
                return false;
        }
    }

    /// <summary>A famous party may be welcomed in; a well-raided knight sends his men or refuses.</summary>
    private static void AskToComeInside(CardContext c, Action recognise)
    {
        var raids = Raids(c);
        recognise();
        if (c.State.Party.Fame > 200)
        {
            raids--;
        }
        if (c.State.Party.Fame > 400)
        {
            raids--;
        }
        var roll = c.Random(6);
        if (raids > 0 && raids + 4 >= roll)
        {
            if (raids >= roll)
            {
                ShowPicture(c, WelcomePicture);
                c.Screen.Text(0x0C);
                c.PassHours(1);
                TheMenSallyForth(c, siege: false);
                return;
            }
            ShowPicture(c, RefusedPicture);
            c.Screen.Text(0x0A);
            c.PassHours(3);
            c.Next = CardNumber;
            return;
        }
        ShowPicture(c, 54);
        c.Screen.Text(0x0B);
        c.PassHours(1);
        c.Next = TheHall;
    }

    private static OptionHint ChallengeChance(CardContext c) =>
        OptionHint.ChanceOf(Math.Clamp(Raids(c) * 4 - c.State.Party.Fame / 10 + 50, 1, 99), OptionHint.WholeParty);

    private static void ChallengeTheKnight(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, WelcomePicture);
            c.Screen.Text(0x0D);
            c.PassHours(2);
            TheFinalBattle(c);
            return;
        }
        if (c.Random(4) <= Raids(c))
        {
            ShowPicture(c, SortiePicture);
            c.Screen.Text(0x0E);
            c.PassHours(1);
            TheMenSallyForth(c, siege: false);
            return;
        }
        ShowPicture(c, RefusedPicture);
        c.Screen.Text(0x0A);
        c.PassHours(3);
        c.Next = CardNumber;
    }

    private static OptionHint SneakingChance(CardContext c)
    {
        var chance = (c.PartyAverageSkill(0x0F) + c.PartyAverageAttribute(2)) / 2;
        if (c.Events.Fact(3, 4, c.Here))
        {
            chance += 30;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 99), OptionHint.WholeParty);
    }

    private static void SneakInAfterDark(CardContext c, int chance, Action recognise)
    {
        c.PassHours(2);
        if (c.IsDaylight)
        {
            c.WaitUntilHour(19);
        }
        recognise();
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, 32);
            c.Screen.Text(0x10);
            c.PracticeSkill(Who.Everybody, 0x0F, 1, 10, 0);
            c.Next = Inside;
            return;
        }
        ShowPicture(c, SortiePicture);
        c.Screen.Text(0x11);
        c.PracticeSkill(Who.Everybody, 0x0F, 0, 10, 0);
        TheMenSallyForth(c, siege: false);
    }

    // orig: R080+0x1DC4 (1929:0EB4) matched; R080+0x1EEA (1929:0FDA) matched; tests: CardSmokeTests
    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                case 1:
                case 2:
                    ShowPicture(c, 54);
                    c.Screen.Text(0x0F);
                    c.PassHours(1);
                    c.Next = TheHall;
                    break;
                case 3:
                    // St. Reinold lets the party scale the walls.
                    ShowPicture(c, 32);
                    c.Screen.Text(0x12);
                    c.PracticeSkill(Who.Everybody, 0x0F, 1, 10, 0);
                    c.PassHours(1);
                    c.Next = Inside;
                    break;
                default:
                    // DORMANT: no slot outside 0..3.
                    break;
            }
            return;
        }
        ShowPicture(c, 16);
        c.Screen.Text(0x13);
        c.PassHours(1);
        c.Next = CardNumber;
    }
}
