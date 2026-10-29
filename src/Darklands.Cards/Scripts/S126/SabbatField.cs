using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd32": the middle of the sabbat field, from where the party wanders to the baptism at
/// the stream, the broomstick riding, the feast preparation and the altar, waits, leaves the
/// sabbat, or prays in a secluded spot (a good prayer brings news of the Master and the fortress
/// monastery). When the feast was already spoiled, its new cook asks for help. However the
/// party leaves, the witches' suspicion decides: from 5 on (unless fact 0x11 is known) they
/// are found out (0x14F), from 10 on always (0x150).
/// </summary>
// orig: R126+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R126+0x0286; hint R126+0x028E
//   option 1: action R126+0x02C6; hint R126+0x02CE
//   option 2: action R126+0x0306; hint R126+0x030E
//   option 3: action R126+0x0346; hint R126+0x03B0
//   option 4: action R126+0x03E8; hint R126+0x03F0
//   option 5: action R126+0x0428; hint R126+0x04A8
//   option 6: action R126+0x04E0; hint R126+0x05BC
//   option 7: action R126+0x05F4; hint R126+0x0662
//   option 8: action R126+0x069A; hint R126+0x06C4
//   option 9: action R126+0x06FC; hint R126+0x071A
//   item pop-up: R126+0x0830, R126+0x086C, R126+0x08A8,
//     R126+0x08E4
//   saint pop-up: R126+0x0920, R126+0x09A8, R126+0x0A30,
//     R126+0x0AB8
//   other: R126+0x0756, R126+0x07F8
// DORMANT (generated): option slots 10..19 keep the default handler R126+0x07F8 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SabbatField : Card
{
    public override int Number => 328;

    private const int Picture = 148;
    private const int FeastPicture = 138;
    private const int PrayerAnsweredPicture = 158;
    private const int NewsPicture = 146;
    private const int PrayerUnansweredPicture = 218;

    /// <summary>The picture the card returns to (changed while the new cook talks).</summary>
    private int _picture;

    public override int Run(CardContext c)
    {
        _picture = Picture;
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        c.SetCardItems();
        c.SetCardSaints(0x29, 0x35, 0x40, 0x87);
        menu[6] = c.SaintsUsable();

        var assured = OptionHint.Saying(SuccessAssured);
        menu.Run(
            // orig: R126+0x0286 (1838:0286) matched
            new MenuLine(_ => c.Next = 0x14A, assured),
            // orig: R126+0x02C6 (1838:02C6) matched
            new MenuLine(_ => c.Next = 0x144, assured),
            // orig: R126+0x0306 (1838:0306) matched
            new MenuLine(_ => c.Next = 0x145, assured),
            new MenuLine(_ => GoToTheFeast(c, menu), assured),
            // orig: R126+0x03E8 (1838:03E8) matched
            new MenuLine(_ => c.Next = 0x149, assured),
            new MenuLine(_ => LeaveTheSabbat(c), assured),
            new MenuLine(_ => PrayInASecludedSpot(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => RefuseTheCook(c, menu), assured),
            new MenuLine(_ => HelpTheCook(c), assured),
            // orig: R126+0x06FC / R126+0x071A matched
            // DORMANT: line 9 is never enabled; its handler shows text 0, passes 0 hours and goes to 0x62.
            new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured));

        c.State.SavedCard = c.Next;
        c.Next = FoundOut(c) ? 0x14F : Value(c, Suspicion) < 10 ? c.Next : 0x150;
        return c.Next;
    }

    // orig: R126+0x0000 matched — the closing test (fact 7 is read twice; nothing draws or changes between).
    private static bool FoundOut(CardContext c) => Value(c, Suspicion) >= 5 && !Known(c, 0x11);

    /// <summary>The feast area, unless the feast was spoiled: then its new cook asks for help (block 6).</summary>
    // orig: R126+0x0346 matched
    // C: declared int, the handler returns without a value on both paths; the menu loop ignores it.
    private void GoToTheFeast(CardContext c, CardMenu menu)
    {
        if (!Known(c, FeastSpoiled))
        {
            c.Next = 0x143;
            return;
        }
        menu.Offer(6, 7, 8);
        _picture = FeastPicture;
        ShowPicture(c, _picture);
    }

    /// <summary>The evil here is too much: the party leaves, and the sabbat site is left behind for good.</summary>
    // orig: R126+0x0428 matched
    private static void LeaveTheSabbat(CardContext c)
    {
        c.Screen.Text(1);
        if (Known(c, FeastSpoiled))
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -5);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
            c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, -2);
        }
        else
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -10);
        }
        AbandonSite(c);
        c.Next = 0x0C;
    }

    /// <summary>Any saint who answers sends a drunk talker (text 2) and the news of the Master (text 5).</summary>
    // orig: R126+0x04E0 matched
    // C: returns 0 after a cancelled box, otherwise falls off its end without a value (ignored).
    private void PrayInASecludedSpot(CardContext c)
    {
        // C: menu_member / menu_slot are the globals the pop-up box set last (stale when chosen from the line itself).
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            // C: the drunk talker (picture 158, text 2) is a switch on menu_slot with cases 0..3 only (two identical
            //   case groups); a slot outside 0..3 skips him and goes straight to the news.
            // DORMANT: menu_slot is outside 0..3 only if a stale global holds such a value; card_saints has 4 slots.
            if (c.MenuSlot is >= 0 and <= 3)
            {
                ShowPicture(c, PrayerAnsweredPicture);
                c.Screen.Text(2);
            }
            ShowPicture(c, NewsPicture);
            c.Screen.Text(5);
        }
        else
        {
            ShowPicture(c, PrayerUnansweredPicture);
            c.Screen.Text(4);
        }
        ShowPicture(c, _picture);
    }

    /// <summary>The party will not help: suspicion rises and the field's options come back.</summary>
    // orig: R126+0x05F4 matched
    // C: declared int, falls off its end without a value (ignored).
    private void RefuseTheCook(CardContext c, CardMenu menu)
    {
        Add(c, Suspicion, 1);
        menu.Offer(0, 0, 1, 2, 3, 4, 5);
        menu[6] = c.SaintsUsable();
        _picture = Picture;
        ShowPicture(c, _picture);
    }

    // orig: R126+0x069A matched
    private static void HelpTheCook(CardContext c)
    {
        c.Screen.Text(7);
        Add(c, Suspicion, -1);
        c.Next = 0x14A;
    }
}
