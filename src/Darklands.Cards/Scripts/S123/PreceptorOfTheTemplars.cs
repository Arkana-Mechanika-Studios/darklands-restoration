using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd20": the Preceptor of the Templars kills a noble he suspects of treachery and draws his
/// sword on the party (an ambush, −3; the fight is remembered for 99 hours). Brought down, he lies
/// dying: the party may finish him or press him about the seals or about the Templars before
/// going back to the fortress (−2).
/// </summary>
// orig: R123+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R123+0x0260; hint R123+0x027A
//   option 1: action R123+0x02B2; hint R123+0x02CC
//   option 2: action R123+0x0304; hint R123+0x031E
//   option 3: action R123+0x0356; hint R123+0x0374
//   option 4: action R123+0x03B0; hint R123+0x03CE
//   option 5: action R123+0x040A; hint R123+0x0428
//   option 6: action R123+0x0464; hint R123+0x0482
//   option 7: action R123+0x04BE; hint R123+0x04DC
//   option 8: action R123+0x0518; hint R123+0x0536
//   option 9: action R123+0x0572; hint R123+0x0590
//   item pop-up: R123+0x06A6, R123+0x06E2, R123+0x071E,
//     R123+0x075A
//   saint pop-up: R123+0x0796, R123+0x081E, R123+0x08A6,
//     R123+0x092E
//   other: R123+0x05CC, R123+0x066E
// DORMANT (generated): option slots 10..19 keep the default handler R123+0x066E ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class PreceptorOfTheTemplars : Card
{
    public override int Number => 316;

    private const int PreceptorPicture = 171;

    /// <summary>The fortress fact "the Preceptor has been fought" (tag 1, type 0x1C).</summary>
    private const int FactTag = 1;
    private const int PreceptorFoughtFact = 0x1C;

    public override int Run(CardContext c)
    {
        Show(c, PreceptorPicture);
        if (!c.Events.FactAnywhere(FactTag, PreceptorFoughtFact))
        {
            c.Events.Create(-2, c.Here, 0, 0x1C, PreceptorFoughtFact, 0, 0, 99, 0, FactTag);
            c.Screen.Text(0); // UNCERTAIN: card_text(0, 2): frame mode 2 read as drawing like mode 0
            c.CombatFlags |= 8;
            c.BattleMode = 1;
            // C: returns −3 without storing it in next_card (which keeps the previous card's value), and before the
            //   table set-up R123+0x05CC, so card_items / card_saints stay as the previous card left them.
            return -3;
        }

        c.Next = Number;
        // Line 3 is switched on although text 1 has only three option lines (as in the original).
        var menu = Menu(c, 1);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems();
        c.SetCardSaints();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        var showAgain = new MenuLine(_ => ShowTheRoomAgain(c), unknown);
        return menu.Run(
            new MenuLine(_ => LastWords(c, 2), unknown),
            new MenuLine(_ => LastWords(c, 3), unknown),
            new MenuLine(_ => LastWords(c, 4), unknown),
            showAgain, showAgain, showAgain, showAgain, showAgain, showAgain, showAgain);
    }

    /// <summary>Text 2: he is dispatched; 3: he speaks of the seals; 4: of the Templars. Then he dies.</summary>
    // orig: R123+0x0260 / _02b2 / _0304 matched
    private static void LastWords(CardContext c, int text)
    {
        c.Screen.Text(text);
        c.BattleMode = 1;
        c.Next = -2;
    }

    // orig: R123+0x0356 / _03b0 / _040a / _0464 / _04be / _0518 / _0572 matched
    private static void ShowTheRoomAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
