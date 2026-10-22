using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol08": the city guard surrounds the rebels' hideout, and a crowd gathers. The party
/// may stir the crowd to storm it, call on the rebels to surrender, pray, storm the building
/// with the guard, or slip away.
/// </summary>
// orig: R089+0x147A (197F:000A) matched; tests: CardSmokeTests
public sealed class ArrestingTheRebels : Card
{
    private const int Picture = 0;
    private const int RebelsTaken = 0x54;

    public override int Number => 0xB7;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 5);
        c.PartyMaxSkill((int)Skill.SpeakCommon);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        // C: the block is the script's own global DS:8E1E, set to 0 on arrival.
        menu.Block = 0;
        // orig: R089+0x1B52 (197F:06E2) matched (resets the handlers and card_saints, not card_items)
        c.SetCardSaints(0x50, 0x3F, 0x7E, 0x29);
        menu[2] = c.SaintsUsable();

        var speaker = c.Variables.Chosen[0];
        menu.EachRound = () => speaker = c.Variables.Chosen[0];
        return menu.Run(
            // orig: R089+0x1668 (197F:01F8); hint R089+0x16F8 matched
            new MenuLine(chance => UrgeTheCrowd(c, menu, chance),
                () => OptionHint.ChanceOf(StormChance(c, speaker), speaker)),
            // orig: R089+0x175E (197F:02EE); hint R089+0x17EE matched
            new MenuLine(chance => CallForSurrender(c, menu, chance),
                () => OptionHint.ChanceOf(SurrenderChance(c, speaker), speaker)),
            // orig: R089+0x1846 (197F:03D6); hint R089+0x18DE; saint hints R089+0x1BEC..091a matched
            new MenuLine(() => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R089+0x1916 (197F:04A6); hint R089+0x1A50 matched
            new MenuLine(() => StormTheBuilding(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R089+0x1A88 (197F:0618); hint R089+0x1A8A matched
            // DORMANT: line 4 (the text's "4" placeholder) is hidden and does nothing.
            new MenuLine(() => { }),
            // orig: R089+0x1AC6 (197F:0656); hint R089+0x1B30 matched
            new MenuLine(chance => SlipAway(c, chance), () => OptionHint.ChanceOf(SlipAwayChance(c), Who.Everybody)));
    }

    private static int StormChance(CardContext c, int speaker) =>
        Math.Clamp((c.Skill(speaker, (int)Skill.SpeakCommon) + c.Attribute(speaker, (int)Attribute.Charisma)
            + c.ReputationHere + c.SidePercent(1)) / 5, 0, 99);

    private static int SurrenderChance(CardContext c, int speaker) =>
        Math.Clamp((c.Skill(speaker, (int)Skill.SpeakCommon) + c.SidePercent(1) + c.Attribute(speaker, (int)Attribute.Charisma) * 2) / 3, 0, 99);

    private static int SlipAwayChance(CardContext c) => Math.Clamp(c.SidePercent(1) / 2, 0, 99);

    /// <summary>A speech that fails leaves the crowd too restless for another (block 1).</summary>
    private static void UrgeTheCrowd(CardContext c, CardMenu menu, int chance)
    {
        var speaker = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 0x14, 0x21);
            c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 1, 10, -1);
            c.PassHours(1);
            c.Screen.Text(2);
            c.Next = RebelsTaken;
            return;
        }
        c.AdjustReputation(c.Here, -5, -5);
        menu[0] = menu[1] = OptionState.Shown;
        c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 0, 10, -1);
        c.PassHours(1);
        menu.Block = 1;
    }

    private static void CallForSurrender(CardContext c, CardMenu menu, int chance)
    {
        var speaker = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 10, 15);
            c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 1, 10, -1);
            c.PassHours(1);
            c.Screen.Text(4);
            c.Next = RebelsTaken;
            return;
        }
        c.AdjustReputation(c.Here, -3, -1);
        c.PracticeSkill(speaker, (int)Skill.SpeakCommon, 0, 10, -1);
        menu[0] = menu[1] = OptionState.Shown;
        c.PassHours(1);
        menu.Block = 1;
    }

    /// <summary>
    /// A prayer: Saint 0x50 or 0x3F calms the crowd, 0x7E distracts it, 0x29 makes the rebel
    /// leader repent. An unanswered prayer leaves the menu on block 2 (the original's choice).
    /// </summary>
    private static void Pray(CardContext c, CardMenu menu)
    {
        // C: the saint pop-up's hint, called on the choice, names the one who prays as $ChosenOneName.
        if (c.MenuPopup)
        {
            c.Variables.Chosen[0] = c.MenuMember;
        }
        c.Variables.Chosen[1] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 1)
        {
            // block 2 has no option lines (block 8 is the text written for this); kept as
            // the original sets it.
            menu.Block = 2;
            return;
        }
        // C: each case passes its hour itself (after the standing for saints 0 and 1); another slot passes none.
        switch (c.MenuSlot)
        {
            case 0 or 1:
                c.AdjustReputation(c.Here, 15, 20);
                c.PassHours(1);
                c.Screen.Text(5);
                break;
            case 2:
                c.PassHours(1);
                c.Screen.Text(6);
                break;
            case 3:
                c.PassHours(1);
                c.Screen.Text(7);
                break;
        }
        c.Next = RebelsTaken;
    }

    private static void StormTheBuilding(CardContext c)
    {
        // Arguments are evaluated right to left: the second group's number is drawn first.
        var second = c.Random(4) + 3;
        var first = c.Random(5) + 3;
        // C: run_battle's battle_result; results 5+ and negative ones leave the party at the menu.
        var result = c.RunBattle(0x25, c.Here + 0x70, 0x0C, 2, first, 0, 2, second, -1, 0, 0);
        c.BattleResult = result;
        switch (result)
        {
            case 0:
                c.AdjustReputation(c.Here, 3, 5);
                c.PassHours(1);
                c.Screen.Text(9);
                c.Next = RebelsTaken;
                break;
            case 1 or 2:
                c.RejoinAway();
                c.PassHours(1);
                c.AdjustReputation(c.Here, -3, -5);
                c.ConflictOutcome(1, 1, -1);
                c.Screen.Text(10);
                c.Next = 0x82;
                break;
            case 3:
                c.PassHours(3);
                c.ConflictOutcome(1, 1, -1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(12);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
            case 4:
                c.PassHours(1);
                c.AdjustReputation(c.Here, -3, -5);
                c.ConflictOutcome(1, 1, -1);
                c.LoseItems(Who.Everybody);
                c.Screen.Text(11);
                c.Next = c.IsDaylight ? 9 : 10;
                break;
        }
    }

    private static void SlipAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, -5, -5);
            c.PassHours(1);
            c.Screen.Text(16);
            c.Next = RebelsTaken;
            return;
        }
        c.ConflictOutcome(1, 1, -1);
        c.Screen.Text(15);
        c.Next = c.IsDaylight ? 9 : 10;
    }
}
