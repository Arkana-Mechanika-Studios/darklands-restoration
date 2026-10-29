using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd18": a musty room of the fortress with a heap of burned books. Sifting through the
/// soot (at a cost in Charisma and Endurance) or a saint's cleansing wind turns up one unburned
/// Latin textbook by Saint Thomas Aquinas, which improves the best Latin scholar. Leaving goes
/// back to the fortress (−2), finding the book ends in −3.
/// </summary>
// orig: R122+0x159E (1991:000E) matched; tests: CardSmokeTests
//   option 0: action R122+0x17D8; hint R122+0x1800
//   option 1: action R122+0x1838; hint R122+0x18E6
//   option 2: action R122+0x191E; hint R122+0x192C
//   option 3: action R122+0x1964; hint R122+0x1972
//   option 4: action R122+0x19AA; hint R122+0x19FC
//   option 5: action R122+0x1A34; hint R122+0x1A52
//   option 6: action R122+0x1A8E; hint R122+0x1AAC
//   option 7: action R122+0x1AE8; hint R122+0x1B06
//   option 8: action R122+0x1B42; hint R122+0x1B60
//   option 9: action R122+0x1B9C; hint R122+0x1BBA
//   item pop-up: R122+0x1CD0, R122+0x1D0C, R122+0x1D48,
//     R122+0x1D84
//   saint pop-up: R122+0x1DC0, R122+0x1E48, R122+0x1ED0,
//     R122+0x1F58
//   other: R122+0x1BF6, R122+0x1C98
// DORMANT (generated): option slots 10..19 keep the default handler R122+0x1C98 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class PileOfBurnedBooks : Card
{
    public override int Number => 314;

    private const int BurnedBooksPicture = 135;
    private const int BookPicture = 134;

    public override int Run(CardContext c)
    {
        Show(c, BurnedBooksPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        c.SetCardItems();
        c.SetCardSaints(0x3F, 0x7D, 0x0F, 0x6F);
        menu[1] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        var leave = new MenuLine(_ => { c.BattleMode = 1; c.Next = -2; }, unknown);
        var showAgain = new MenuLine(_ => ShowTheRoomAgain(c), unknown);
        // DORMANT: in text block 1 the option lines 0..2 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => menu.Offer(1, 3, 4), unknown),
            new MenuLine(_ => PurifyTheRoom(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R122+0x191E / R122+0x1964 matched (two identical "leave" handlers)
            leave,
            leave,
            new MenuLine(_ => KeepSearching(c), unknown),
            showAgain, showAgain, showAgain, showAgain, showAgain);
    }

    /// <summary>A wind sweeps the ashes away and leaves the book (or only sighs).</summary>
    // orig: R122+0x1838 matched
    private void PurifyTheRoom(CardContext c)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(5);
            return;
        }
        Show(c, BookPicture);
        c.Screen.Text(4);
        // C: card_load(card_name, pic_buf): pic_buf still holds the card's own picture.
        Show(c, BurnedBooksPicture);
        c.Screen.Text(3);
        StudyTheBook(c);
    }

    /// <summary>Hours in the soot: everyone loses 2 Charisma and 1 Endurance, but the book is found.</summary>
    // orig: R122+0x19AA matched
    private static void KeepSearching(CardContext c)
    {
        c.Screen.Text(2);
        c.AddAttribute(Who.Everybody, (int)Attribute.Charisma, -2);
        c.AddAttribute(Who.Everybody, (int)Attribute.Endurance, -1);
        StudyTheBook(c);
    }

    /// <summary>The best Latin speaker gains 15 points of Latin.</summary>
    // C: party_max_skill is called only for its side effect on best_member, which add_skill reads as unsigned char.
    private static void StudyTheBook(CardContext c)
    {
        c.PartyMaxSkill((int)Skill.SpeakLatin);
        c.AddSkill(c.BestMember, (int)Skill.SpeakLatin, 15);
        c.BattleMode = 1;
        c.Next = -3;
    }

    // orig: R122+0x1A34 / _04fe / _0558 / _05b2 / _060c matched (lines 5..9, never enabled)
    private static void ShowTheRoomAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
