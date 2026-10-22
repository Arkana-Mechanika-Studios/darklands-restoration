using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd29": witches anoint themselves and fly off on pigs, barrels and broomsticks. The party
/// may attack, walk away, take a ride (and pocket a vial of the flying ointment) or preach.
/// Everything ends back on the field (0x142), or at 0x14E after a lost fight.
/// </summary>
// orig: R125+0x0D80 (1910:0000) matched; tests: CardSmokeTests
//   option 0: action R125+0x101C; hint R125+0x1042
//   option 1: action R125+0x107A; hint R125+0x10BA
//   option 2: action R125+0x10F2; hint R125+0x115A
//   option 3: action R125+0x1192; hint R125+0x126A
//   option 4: action R125+0x12A2; hint R125+0x12C0
//   option 5: action R125+0x12FC; hint R125+0x131A
//   option 6: action R125+0x1356; hint R125+0x1374
//   option 7: action R125+0x13B0; hint R125+0x13CE
//   option 8: action R125+0x140A; hint R125+0x1428
//   option 9: action R125+0x1464; hint R125+0x1482
//   item pop-up: R125+0x1598, R125+0x15D4, R125+0x1610,
//     R125+0x164C
//   saint pop-up: R125+0x1688, R125+0x1710, R125+0x1798,
//     R125+0x1820
//   other: R125+0x0F96, R125+0x14BE, R125+0x1560
// DORMANT (generated): option slots 10..19 keep the default handler R125+0x1560 ("screwed up.") and are never shown.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class SabbatBroomstickRiders : Card
{
    public override int Number => 325;

    private const int Picture = 149;
    private const int AngryCrowdPicture = 160;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // Every line is switched on, though the text has only four.
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        c.SetCardItems();
        c.SetCardSaints();

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: R125+0x12A2, R125+0x12FC, R125+0x1356, R125+0x13B0, R125+0x140A, R125+0x1464 matched — lines the text does not have.
        var noLine = new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured);
        return menu.Run(
            new MenuLine(_ => AttackTheRiders(c), assured),
            // orig: R125+0x107A / R125+0x10BA matched — walk away.
            new MenuLine(_ => BackToTheField(c, 1, 1), assured),
            new MenuLine(_ => TakeARide(c), assured),
            new MenuLine(chance => Preach(c, chance), () => PreachingChance(c)),
            noLine, noLine, noLine, noLine, noLine, noLine);
    }

    // orig: R125+0x101C matched
    private static void AttackTheRiders(CardContext c)
    {
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 10, 0);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
        FightTheRiders(c);
    }

    // orig: R125+0x0F96 matched
    // C: no random argument; a result outside 0..4 leaves next_card unchanged (the card stays).
    private static void FightTheRiders(CardContext c)
    {
        switch (c.RunBattle(0x4B, -1, 0x29, 3, 2, 0x0D, 2, 6, -1, 0, 0))
        {
            case 0 or 1:
                c.Screen.Text(1);
                BackToTheField(c, 2, 1);
                break;
            case 2 or 3 or 4:
                c.DropAway();
                c.Next = 0x14E;
                break;
        }
    }

    /// <summary>
    /// A short flight on an oaken bough, and a vial of ointment pocketed. The Virtue lost is that
    /// of whoever the last party query found (nothing on this option asks one).
    /// </summary>
    // orig: R125+0x10F2 matched
    // C: add_skill reads best_member (as unsigned char) as some earlier query left it (stale global).
    private static void TakeARide(CardContext c)
    {
        c.Screen.Text(3);
        Add(c, SightsVisited, -1);
        c.AddSkill(c.BestMember, (int)Skill.Virtue, -1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -5);
        Note(c, FlyingOintment, 72);
        c.Next = 0x142;
    }

    /// <summary>Speak Common of the member found last, plus half the best Charisma.</summary>
    // orig: R125+0x126A matched
    // C: the same code as R125+0x053A (checked against the asm there): member_skill reads the stale best_member
    //   before party_max_attr(5) sets it; the hint then names the best Charisma.
    private static OptionHint PreachingChance(CardContext c)
    {
        var chance = c.Skill(c.BestMember, (int)Skill.SpeakCommon);
        chance += c.PartyMaxAttribute((int)Attribute.Charisma) / 2;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R125+0x1192 matched
    // C: party_max_skill(9) is called only to set best_member, which add_skill reads as unsigned char.
    private static void Preach(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
            c.PracticeSkill(Who.Everybody, (int)Skill.SpeakCommon, 1, 10, 0);
            c.PartyMaxSkill((int)Skill.Virtue);
            c.AddSkill(c.BestMember, (int)Skill.Virtue, 1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 5);
            BackToTheField(c, 1, 1);
            return;
        }
        ShowPicture(c, AngryCrowdPicture);
        c.Screen.Text(5);
        ShowPicture(c, Picture);
        FightTheRiders(c);
    }
}
