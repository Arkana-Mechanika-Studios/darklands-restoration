using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineB00": past the riddle door, the dwarf who holds the mine speaks in rhyme. The party
/// may argue for the miners, threaten him (if he is cowed he offers a cuirass), hand over
/// fifteen kinds of potion once he has asked for them, or attack.
/// </summary>
// orig: R100+0x0BE0 (18F6:0000) matched; tests: CardSmokeTests
public sealed class DwarfWantsPotions_MineB00 : Card
{
    private const int Picture = 200;
    private const int DwarfAngryPicture = 203;
    private const int DwarfCowedPicture = 201;
    private const int DwarfSneersPicture = 202;
    private const int Cuirass = 0x2C;
    private const int MineQuestTag = 0x0C;

    /// <summary>The dwarf was threatened in vain (6 hours): no second threat meanwhile.</summary>
    private const int ThreatenedInVain = 0x50;

    public override int Number => 0xE3;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 1, 3);
        c.OptionsBlock = 0;
        // The dwarf has named his price (location fact 1 of the mine quest, anywhere).
        if (c.Events.FactAnywhere(MineQuestTag, 1))
        {
            menu[2] = KindsOfPotion(c) >= 15 ? OptionState.Enabled : OptionState.Shown;
        }
        // C: name kind 7 (the mine dwarf's name) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(7, c.MineDetail(), 0);
        // orig: R100+0x1284 (18F6:06A4) matched
        c.SetCardSaints();
        c.SetCardItems();

        menu.EachRound = () => menu.Block = c.OptionsBlock;
        return menu.Run(
            new MenuLine(() =>
                {
                    // orig: R100+0x0DC4 (18F6:01E4) matched
                    c.Screen.Text(1);
                    c.Screen.Text(2);
                    // the price is remembered as an event of kind 1, type 0 (tag 0x0C,
                    // 999 hours), not as the location fact (kind 0x1C, type 1) the card tests on
                    // arrival; kept as the original has it.
                    c.Events.Renew(Who.Everybody, c.Here, 0, 1, 0, 0x63, 0, 0x3E7, 0, MineQuestTag);
                    menu[2] = KindsOfPotion(c) >= 15 ? OptionState.Enabled : OptionState.Shown;
                    menu[0] = OptionState.Shown;
                    c.Next = Number;
                }, ArgueHint(c)),
            new MenuLine(chance => Threaten(c, chance, menu), () => OptionHint.ChanceOf(ThreatChance(c), c.Leader)),
            new MenuLine(() => GivePotions(c), OptionHint.Saying(SuccessAssured)),
            // orig: R100+0x10B6 (18F6:04D6) matched
            new MenuLine(() =>
                {
                    c.Screen.Text(9);
                    c.BattleMode = 1;
                    c.BattleEnemy = 0x0B;
                    c.Next = -3;
                }, OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(() => TakeTheCuirass(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R100+0x11F6 (18F6:0616) matched
            new MenuLine(() => TheDwarfAttacks(c), OptionHint.Saying(CardTexts.Unknown)));
    }

    // orig: R100+0x0E3A (18F6:025A) matched
    // C: the hint names the leader as $ChosenOneName (a side effect of computing it).
    private static Func<OptionHint> ArgueHint(CardContext c) => () =>
    {
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.Say(NoRisk, c.Leader);
    };

    /// <summary>How many of the 22 potions (0x5F..0x74) the party carries at least one of.</summary>
    private static int KindsOfPotion(CardContext c)
    {
        var kinds = 0;
        for (var k = 0; k < Potions.Count; k++)
        {
            if (c.CountItems(Who.Everybody, k + Potions.FirstPotion) != 0)
            {
                kinds++;
            }
        }
        return kinds;
    }

    /// <summary>The party's best hand-weapon skill, more for a larger party; nil just after a vain threat.</summary>
    // orig: R100+0x0FB2 (18F6:03D2) matched
    private static int ThreatChance(CardContext c)
    {
        var best = c.PartyBestHandWeaponSkill();
        var chance = Math.Clamp(c.PresentCount() * best / 5, 1, 99);
        if (c.Events.Started(ThreatenedInVain))
        {
            chance = 0;
        }
        return chance;
    }

    // orig: R100+0x0E76 (18F6:0296) matched
    private void Threaten(CardContext c, int chance, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (chance == 0)
        {
            TheDwarfAttacks(c);
            return;
        }
        if (c.Random(100) <= chance)
        {
            // Cowed, the dwarf offers a gift (block 4).
            menu.Set(OptionState.Hidden, 0, 1, 2, 3);
            menu.Enable(4, 5);
            c.OptionsBlock = 4;
            ShowPicture(c, DwarfCowedPicture);
            c.EventHere(ThreatenedInVain, 6);
        }
        else
        {
            ShowPicture(c, DwarfSneersPicture);
            c.Screen.Text(c.PartySize < 4 ? 5 : 6);
        }
        c.Next = Number;
    }

    /// <summary>Fifteen kinds of potion, one of each, from whoever carries them; the dwarf leaves the mine.</summary>
    // orig: R100+0x0FFC (18F6:041C) matched
    private static void GivePotions(CardContext c)
    {
        var given = 0;
        for (var k = 0; k < Potions.Count; k++)
        {
            if (given >= 15)
            {
                break;
            }
            if (c.CountItems(Who.Everybody, k + Potions.FirstPotion) != 0)
            {
                c.TakeItem(Who.Everybody, k + Potions.FirstPotion);
                given++;
            }
        }
        c.Screen.Text(7);
        ShowPicture(c, DwarfCowedPicture);
        c.Screen.Text(8);
        c.BattleResult = 0;
        c.Next = 0x2E;
    }

    /// <summary>The dwarf's cuirass (quality 40–49): the gift binds the party, and the mine is lost.</summary>
    // orig: R100+0x110E (18F6:052E) matched
    private static void TakeTheCuirass(CardContext c)
    {
        // C: the stack record takes the cuirass's type and weight from item 0x2C's definition and
        // a quality byte of 40 + rnd(10); one item.
        c.AddItemWithQuality(c.Leader, Cuirass, c.Random(10) + 0x28);
        c.Screen.ShowPicture(PictureNames.Of(DwarfSneersPicture));
        c.Screen.Text(10);
        c.Events.Create(Who.Everybody, c.Here, 0, EventKinds.LocationFact, MineQuestTag, 0, 0, 0x63, 0, MineQuestTag);
        c.BattleResult = 3;
        c.Next = 0x2E;
    }

    private static void TheDwarfAttacks(CardContext c)
    {
        c.Screen.ShowPicture(PictureNames.Of(DwarfAngryPicture));
        c.Screen.Text(3);
        c.BattleMode = 1;
        c.BattleEnemy = 0x0B;
        c.BattleSeed = c.Random(1000);
        c.Next = -4;
    }
}
