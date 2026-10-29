using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineB03": the dwarf king again, his red cap of invisibility in hand. The party may shoot
/// the cap from his fingers, leap for it, pray, offer back his treasure (if it opened his box,
/// fact 0x0B), or fight. Taking the cap makes him swear to leave the mine; a failed attempt
/// brings his minions (and after a won fight he is cowed: block 8, easier chances).
/// </summary>
// orig: R101+0x0850 (18BD:0000) matched; tests: CardSmokeTests
public sealed class DwarfKingsRedCap_MineB03 : Card
{
    private const int Picture = 200;
    private const int MinionsPicture = 201;
    private const int KingLaughsPicture = 202;
    private const int GoblinsPicture = 204;
    private const int CapTakenPicture = 205;
    private const int TreasurePicture = 206;
    private const int SaintUnheededPicture = 207;
    private const int MineQuestTag = 0x0C;
    private const int Cowed = 8;

    public override int Number => 0xE6;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 1, 3, 4);
        if (!c.Events.FactAnywhere(MineQuestTag, 0x0B))
        {
            menu[3] = OptionState.Hidden;
        }
        if (c.CountItemsWithFlags(Who.Everybody, ItemFlags.MissileWeapon) == 0)
        {
            menu[0] = OptionState.Shown;
        }
        c.PartyMaxAttribute((int)Attribute.Intelligence);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        c.PartyMaxMountedAgility();
        c.Variables.Chosen[1] = (byte)c.BestMember;
        c.OptionsBlock = 0;
        // orig: R101+0x1050 (18BD:0800) matched
        c.SetCardItems();
        c.SetCardSaints(0x3F, 0x50);
        menu[2] = c.SaintsUsable();

        menu.EachRound = () => menu.Block = c.OptionsBlock;
        return menu.Run(
            // orig: R101+0x0C40 (18BD:03F0) matched
            new MenuLine(chance => TakeTheCap(c, chance, 4), () => OptionHint.ChanceOf(ShootChance(c), c.Variables.Chosen[0])),
            // orig: R101+0x0D24 (18BD:04D4) matched
            new MenuLine(chance => TakeTheCap(c, chance, 11), () => OptionHint.ChanceOf(LeapChance(c), Who.Everybody)),
            // orig: R101+0x0E00 (line 2); saint pop-up hints R101+0x10E0, R101+0x116A (CardMenu.SaintPopupHint)
            new MenuLine(() => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            new MenuLine(() => OfferTheTreasure(c), OptionHint.Saying(SuccessAssured)),
            // orig: R101+0x0FD6 (18BD:0786) matched
            new MenuLine(() =>
                {
                    ShowPicture(c, KingLaughsPicture);
                    c.Screen.Text(17);
                    c.BattleMode = 1;
                    c.Next = -2;
                }, OptionHint.Saying(NoRisk)));
    }

    /// <summary>The best missile skill, a third of it; +40 once the king is cowed.</summary>
    // orig: R101+0x0CE0 (18BD:0490) matched
    // C: the hint names the best shot as $ChosenOneName (a side effect of computing it).
    private static int ShootChance(CardContext c)
    {
        var chance = c.PartyBestMissileSkill() / 3;
        c.Variables.Chosen[0] = (byte)c.BestMember;
        if (c.OptionsBlock == Cowed)
        {
            chance += 0x28;
        }
        return Math.Clamp(chance, 1, 99);
    }

    /// <summary>Half the best mounted Agility; +20 once the king is cowed.</summary>
    // orig: R101+0x0DC4 (18BD:0574) matched
    private static int LeapChance(CardContext c)
    {
        var chance = c.PartyMaxMountedAgility() / 2;
        c.Variables.Chosen[0] = (byte)c.BestMember;
        if (c.OptionsBlock == Cowed)
        {
            chance += 0x14;
        }
        return Math.Clamp(chance, 1, 99);
    }

    /// <summary>The cap is taken (text <paramref name="howText"/>), the king swears and the mine is free; else his minions come.</summary>
    private void TakeTheCap(CardContext c, int chance, int howText)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, CapTakenPicture);
            c.Screen.Text(howText);
            c.Screen.Text(5);
            c.Screen.Text(6);
            ShowPicture(c, MinionsPicture);
            c.Screen.Text(7);
            c.BattleResult = 0;
            c.Next = 0x2E;
            return;
        }
        TheKingStrikesBack(c);
    }

    // orig: R101+0x0E00 (18BD:05B0) matched
    private void Pray(CardContext c, CardMenu menu)
    {
        // C: the action leaves $ChosenOneName alone: the saint pop-up's hint (R101+0x10E0, +0x1103) names the one who prays.
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        // C: any answer but 0 counts here (the other cards test for 1).
        if (answer != 0)
        {
            ShowPicture(c, CapTakenPicture);
            c.Screen.Text(12);
            c.OptionsBlock = Cowed;
            menu[2] = OptionState.Shown;
            ShowPicture(c, Picture);
            c.Next = 0xE6;
            return;
        }
        ShowPicture(c, SaintUnheededPicture);
        c.Screen.Text(13);
        TheKingStrikesBack(c);
    }

    // orig: R101+0x0EEE (18BD:069E) matched
    private void OfferTheTreasure(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        ShowPicture(c, TreasurePicture);
        c.Screen.Text(14);
        ShowPicture(c, CapTakenPicture);
        c.Screen.Text(15);
        ShowPicture(c, MinionsPicture);
        c.Screen.Text(7);
        c.BattleResult = 0;
        c.Next = 0x2E;
    }

    /// <summary>
    /// The king seals the door and sends his creatures, chosen by the mine quest's detail: dwarves
    /// (0), goblins (1, a single foe) or kobolds (2). A won fight cows him; a lost one ends the quest.
    /// </summary>
    // orig: R101+0x0A20 (18BD:01D0) matched
    private void TheKingStrikesBack(CardContext c)
    {
        int result;
        // C: % keeps the sign of the dividend; a negative detail picks none of the three.
        switch (c.MineDetail() % 3)
        {
            case 0:
                ShowPicture(c, MinionsPicture);
                c.Screen.Text(1);
                result = c.RunBattle(0x80, c.MineDetail(), 0x2F, 2, c.PartyLoreLevel() / 4 + 3, -1, 0, 0, -1, 0, 0);
                break;
            case 1:
                ShowPicture(c, GoblinsPicture);
                c.Screen.Text(2);
                result = c.RunBattle(0x81, c.MineDetail(), 0x34, 1, 1, -1, 0, 0, -1, 0, 0);
                break;
            case 2:
                ShowPicture(c, MinionsPicture);
                c.Screen.Text(3);
                result = c.RunBattle(0x7F, c.MineDetail(), 0x2A, 1, c.PartyLoreLevel() / 4 + 4, -1, 0, 0, -1, 0, 0);
                break;
            default:
                // DORMANT: a negative mine detail. DIVERGENCE: the original then reads an
                // uninitialised local as the result; here nothing happens (as for results 5+).
                return;
        }
        c.BattleResult = result;
        switch (result)
        {
            case 0:
                c.OptionsBlock = Cowed;
                ShowPicture(c, Picture);
                c.Next = 0xE6;
                break;
            case 1 or 2 or 3:
                c.DropAway();
                ShowPicture(c, MinionsPicture);
                c.Screen.Text(9);
                TheMineIsLost(c);
                break;
            case 4:
                ShowPicture(c, MinionsPicture);
                c.Screen.Text(10);
                c.LoseItems(Who.Everybody);
                TheMineIsLost(c);
                break;
        }
    }

    private static void TheMineIsLost(CardContext c)
    {
        c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, MineQuestTag, 0, 0, 0x63, 0, MineQuestTag);
        c.Next = 0x2E;
    }
}
