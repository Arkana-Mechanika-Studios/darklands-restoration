using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineP00": the party has fallen into the dwarfs' pit trap in a mine. Climbing out, waiting,
/// a saint, a potion, a human pyramid or digging are the ways out; most of them end in a fight
/// with the dwarfs (the card returns -2 and sets the battle up), only waiting and a failed
/// climb, potion, pyramid or saint leave the party in the pit or take it on to the next card.
/// </summary>
// orig: R056+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R056+0x0540 (1838:0540) matched (init helper: clears the 20 option flags, installs the item and saint pop-up handlers for the 5 member rows and empties the card_items and card_saints lists; the card then fills 2 items and 4 saints)
// DORMANT: R056+0x05CA (1838:05CA) the init helper points opt_fn[0..19] at this "screwed up." default; lines 6..19 are cleared (flag 0) and the MSG has only 6 lines, so it never runs.
// DORMANT: item_fn[k][2] and item_fn[k][3] are never installed by the init helper (they keep stale handlers) and card_items has only two entries, so those pop-up slots are empty and cannot be chosen.
public sealed class MinePitTrap_MineP00 : Card
{
    public const int CardNumber = 0x30;

    private const int Picture = 211;

    /// <summary>The cards the pit leads to: the dwarfs' cells (MineP01).</summary>
    private const int DwarfCells = 0x5B;

    /// <summary>Attack the dwarfs: the card hands the main loop a battle (mode 1) by returning -2.</summary>
    private const int StartBattle = -2;

    private const int ClimberOption = 0, SaintOption = 2, PyramidOption = 4;

    // The options array lives in Run so that the actions can close a line (opt_flag = 2) while the card stays.
    private CardMenu _menu = null!;

    public override int Number => CardNumber;

    // orig: R056+0x0000 (1838:0000) matched (entry: option table, best climber, item and saint lists, the menu loop)
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        var pyramid = c.PresentCount() < 3 ? OptionState.Shown : OptionState.Enabled;
        // C: party_max_eff_attr2() sets best_member: the best climber (highest effective agility) is named in the texts.
        _ = c.PartyMaxEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        c.SetCardItems(0x68, 0x66);
        c.SetCardSaints(0x23, 0x3F, 0x6C, 0x72);
        var potion = c.ItemsUsable();
        var saint = c.SaintsUsable();

        _menu = Menu(c, 0);
        _menu.Enable(0, 1, 2, 3, 4, 5);
        _menu[2] = saint;
        _menu[3] = potion;
        _menu[4] = pyramid;
        c.Next = Number;
        return _menu.Run(
            // orig: R056+0x01EE (1838:01EE) matched (option 0 action, see Climb); chance handler R056+0x0248 (see ClimberChance)
            Round(c, chance => Climb(c, chance), () => ClimberChance(c)),
            // orig: R056+0x0268 (1838:0268) matched (option 1 action: wait in the pit); hint R056+0x0282 "You can always encounter the unknown"
            Round(c, () => WaitForYourDoom(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R056+0x02BA (1838:02BA) unmatched (option 2 action, see CallUponASaint; read with the asm); hint R056+0x0322 "select a saint"
            // C: the pop-up's handler runs before the action and names the member in the text.
            Round(c, _ => CallUponASaint(c), OptionHint.Saying(CardTexts.SelectSaint), () => SaintHint(c)),
            // orig: R056+0x035A (1838:035A) matched (option 3 action, see UseAPotion); hint R056+0x03E6 "select a potion"; pop-up handlers 0602/063e (see PotionHint)
            Round(c, chance => UseAPotion(c, chance), OptionHint.Saying(CardTexts.SelectPotion), () => PotionHint(c, c.MenuSlot)),
            // orig: R056+0x041E (1838:041E) matched (option 4 action, see HumanPyramid); chance handler R056+0x0468 (see PyramidChance)
            Round(c, chance => HumanPyramid(c, chance), () => PyramidChance(c)),
            // orig: R056+0x04A2 (1838:04A2) matched (option 5 action, see DigOut); chance handler R056+0x052A (see DigChance)
            Round(c, chance => DigOut(c, chance), () => DigChance(c)));
    }

    /// <summary>
    /// One choice of the menu loop: the action, and, when the card stays, the start of the next
    /// round (the best climber is named again, as the original loop does at its top).
    /// </summary>
    private static MenuLine Round(CardContext c, Action action, Func<OptionHint> hint) =>
        new(() =>
            {
                action();
                if (c.Next == CardNumber)
                {
                    _ = c.PartyMaxEffectiveAgility();
                    c.Variables.Chosen[0] = c.BestMember;
                }
            },
            hint);

    /// <summary>Like the other <c>Round</c>, for an action that takes the chance of its hint (the pop-up's, for a pop-up choice).</summary>
    private static MenuLine Round(CardContext c, Action<int> action, Func<OptionHint> hint, Func<OptionHint>? popup = null) =>
        new(chance =>
            {
                action(chance);
                if (c.Next == CardNumber)
                {
                    _ = c.PartyMaxEffectiveAgility();
                    c.Variables.Chosen[0] = c.BestMember;
                }
            },
            hint, popup);

    private void CloseOption(int option) => _menu[option] = OptionState.Shown;

    /// <summary>The best climber's chance is the party's best effective agility; the hint names that member.</summary>
    // orig: R056+0x0248 (1838:0248) matched
    private static OptionHint ClimberChance(CardContext c)
    {
        var chance = c.PartyMaxEffectiveAgility();
        // C: the handler leaves the best member in the text variable as well.
        c.Variables.Chosen[0] = c.BestMember;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R056+0x01EE (1838:01EE) matched
    private void Climb(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(1);
            c.BattleSetup.Mode = 1;
            c.Next = StartBattle;
            return;
        }
        // The climber fails and falls back: the original changes his Endurance by +1..10 with add_attr, kept as is.
        c.AddAttribute(c.Variables.Chosen[0], 0, c.Random(10) + 1);
        c.Screen.Text(2);
        CloseOption(ClimberOption);
        c.Next = CardNumber;
    }

    // orig: R056+0x0268 (1838:0268) matched
    private static void WaitForYourDoom(CardContext c)
    {
        c.PassHours(c.Random(6));
        c.Next = DwarfCells;
    }

    /// <summary>The saint pop-up's hint (all four slots are the same handler): the member's chance with the saint, named in the text.</summary>
    // orig: R056+0x0664 (1838:0664) matched (saint slot 0)
    // orig: R056+0x06EE (1838:06EE) matched (saint slot 1; same body)
    // orig: R056+0x0778 (1838:0778) matched (saint slot 2; same body)
    // orig: R056+0x0802 (1838:0802) matched (saint slot 3; same body)
    private static OptionHint SaintHint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuSlot];
        var low = c.SaintChanceLow(c.MenuMember, saint);
        return OptionHint.Say(c.ChanceText(low, c.SaintChanceHigh(c.MenuMember, saint)));
    }

    /// <summary>A saint answers: the way out opens (slots 0..2) or the party is carried out (slot 3), and the dwarfs come.</summary>
    // orig: R056+0x02BA (1838:02BA) unmatched (checked against asm: the C returns an int without a value; the switch on menu_slot is a jump on 0..2 / 3)
    private void CallUponASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 0)
        {
            CloseOption(SaintOption);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0 or 1 or 2:
                c.Screen.Text(3);
                c.BattleSetup.Mode = 1;
                c.Next = StartBattle;
                break;
            case 3:
                c.Screen.Text(4);
                c.BattleSetup.Mode = 1;
                c.Next = StartBattle;
                break;
        }
    }

    /// <summary>The potion pop-up's hints by slot: the first potion always works, the second needs a quick party.</summary>
    // orig: R056+0x0602 (1838:0602) matched (item slot 0: success assured, names the member)
    // orig: R056+0x063E (1838:063E) matched (item slot 1: the party's average agility + 40)
    private static OptionHint PotionHint(CardContext c, int slot)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        return slot switch
        {
            0 => OptionHint.Say(SuccessAssured, c.MenuMember),
            1 => OptionHint.ChanceOf(c.PartyAverageAttribute(2) + 0x28, c.MenuMember),
            // UNCERTAIN: item_fn[k][2..3] are never installed (stale handlers); card_items has only two entries, so the slots stay empty.
            _ => OptionHint.Say(CardTexts.ScrewedUp),
        };
    }

    // orig: R056+0x035A (1838:035A) matched
    private void UseAPotion(CardContext c, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(5);
                c.BattleSetup.Mode = 1;
                c.Next = StartBattle;
                break;
            case 1:
                // C: the arguments are evaluated right to left: the Endurance minimum is drawn before the Strength minimum.
                var endurance = c.Random(10) + 3;
                var strength = c.Random(10) + 3;
                c.Injure(Who.Everybody, strength, endurance, 10);
                if (c.Random(100) <= chance)
                {
                    c.Screen.Text(6);
                    c.BattleSetup.Mode = 1;
                    c.Next = StartBattle;
                }
                else
                {
                    c.Screen.Text(7);
                    c.Next = CardNumber;
                }
                break;
        }
    }

    /// <summary>The party's average of Agility and Strength, kept between 1 and 99.</summary>
    // orig: R056+0x0468 (1838:0468) matched
    private static OptionHint PyramidChance(CardContext c)
    {
        var agility = c.PartyAverageAttribute(2);
        var strength = c.PartyAverageAttribute(1);
        return OptionHint.ChanceOf(Math.Clamp((agility + strength) / 2, 1, 99), OptionHint.WholeParty);
    }

    // orig: R056+0x041E (1838:041E) matched
    private void HumanPyramid(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = c.Leader;
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(8);
            c.BattleSetup.Mode = 1;
            c.Next = StartBattle;
            return;
        }
        c.Screen.Text(9);
        CloseOption(PyramidOption);
        c.Next = CardNumber;
    }

    /// <summary>The party's average Artifice skill.</summary>
    // orig: R056+0x052A (1838:052A) matched
    private static OptionHint DigChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Artifice), OptionHint.WholeParty);

    // orig: R056+0x04A2 (1838:04A2) matched
    private static void DigOut(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(c.Random(4) + 1);
            c.PracticeSkill(c.Leader, (int)Skill.Artifice, 1, 10, 0);
            c.Screen.Text(0x0A);
            c.BattleSetup.Mode = 1;
            c.Next = StartBattle;
            return;
        }
        c.PassHours(c.Random(6) + 1);
        c.PracticeSkill(c.Leader, (int)Skill.Artifice, 0, 10, 0);
        c.Screen.Text(0x0B);
        c.Next = DwarfCells;
    }
}
