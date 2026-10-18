using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd23": hidden in the brambles beside the sabbat path, the party waits for four cloaked
/// pilgrims. Staying unseen (Woodwise or Stealth) they overhear that "Klaus is ill" and may let
/// them pass or attack; if noticed, the pilgrims attack first. Every way ends back on the hill
/// (card 318); a won fight gains their cloaks, a lost one alarms the sabbat.
/// </summary>
// orig: R123+0x2036 (1A3B:0006) matched; tests: CardSmokeTests
//   option 0: action R123+0x22EA; hint R123+0x2338
//   option 1: action R123+0x237E; hint R123+0x2388
//   option 2: action R123+0x23C0; hint R123+0x23EC
//   option 3: action R123+0x2424; hint R123+0x2452
//   option 4: action R123+0x248A; hint R123+0x24A8
//   option 5: action R123+0x24E4; hint R123+0x2502
//   option 6: action R123+0x253E; hint R123+0x255C
//   option 7: action R123+0x2598; hint R123+0x25B6
//   option 8: action R123+0x25F2; hint R123+0x2610
//   option 9: action R123+0x264C; hint R123+0x266A
//   item pop-up: R123+0x2780, R123+0x27BC, R123+0x27F8,
//     R123+0x2834
//   saint pop-up: R123+0x2870, R123+0x28F8, R123+0x2980,
//     R123+0x2A08
//   other: R123+0x2250, R123+0x26A6, R123+0x2748
// DORMANT (generated): option slots 10..19 keep the default handler R123+0x2748 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class AmbushOnThePilgrimPath : Card
{
    public override int Number => 319;

    private const int HiddenPicture = 145;

    public override int Run(CardContext c)
    {
        Show(c, HiddenPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints();

        var assured = OptionHint.Saying(SuccessAssured);
        var showAgain = new MenuLine(_ => ShowThePathAgain(c), assured);
        return menu.Run(
            new MenuLine(chance => Listen(c, menu, chance), () => ConcealmentChance(c)),
            // orig: R123+0x237E / R123+0x2388 matched — attack at once.
            new MenuLine(_ => Fight(c, noticed: false), assured),
            new MenuLine(_ => LetThemGo(c), assured),
            new MenuLine(_ => AttackAfterListening(c), assured),
            showAgain, showAgain, showAgain, showAgain, showAgain, showAgain);
    }

    /// <summary>The better of the best Woodwise and the worst Stealth.</summary>
    // orig: R123+0x2338 matched
    // C: best_member is read (as unsigned char) after each query; a tie (stealth == woodwise) takes the Stealth member.
    private static OptionHint ConcealmentChance(CardContext c)
    {
        var woodwise = c.PartyMaxSkill((int)Skill.Woodwise);
        var woodsman = c.BestMember;
        var stealth = c.PartyMinSkill((int)Skill.Stealth);
        return stealth < woodwise
            ? OptionHint.ChanceOf(woodwise, woodsman)
            : OptionHint.ChanceOf(stealth, c.BestMember);
    }

    // orig: R123+0x22EA matched
    // C: an int function: it returns options_block (1) when unseen and falls off its end after the fight; ignored.
    private static void Listen(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            menu.Offer(1, 2, 3);
            return;
        }
        c.Screen.Text(2);
        Fight(c, noticed: true);
    }

    // orig: R123+0x23C0 matched
    private static void LetThemGo(CardContext c)
    {
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 1, 10, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
        c.Next = 318;
    }

    // orig: R123+0x2424 matched
    private static void AttackAfterListening(CardContext c)
    {
        c.PracticeSkill(Who.Everybody, (int)Skill.Woodwise, 7, 10, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 7, 10, 0);
        Fight(c, noticed: false);
    }

    /// <summary>
    /// The four pilgrims (a different battle when they noticed the party). Won: their cloaks are
    /// taken (fact 6 for 72 hours); lost: they alarm the sabbat. Any other result stays here.
    /// </summary>
    // orig: R123+0x2250 matched
    // C: a result outside 0..4 (the default and the −64..−1 cases) returns with next_card unchanged; no random argument.
    private static void Fight(CardContext c, bool noticed)
    {
        switch (c.RunBattle(noticed ? 0x47 : 0x46, -1, 3, 1, 4, -1, 0, 0, -1, 0, 0))
        {
            case 0:
            case 1:
                c.DropAway();
                c.Screen.Text(3);
                c.PassHours(1);
                c.Events.Create(0, 0, 0x5F, 0x1C, 6, 0, 0, 72, 0, 1);
                c.Next = 318;
                break;
            case 2:
            case 3:
            case 4:
                c.DropAway();
                c.Screen.Text(4);
                c.Events.Update(-1, -1, 0x1C, 7, 3, 1, EventUpdate.Add);
                c.Next = 318;
                break;
        }
    }

    // orig: R123+0x248A / _04b4 / _050e / _0568 / _05c2 / _061c matched
    private static void ShowThePathAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
