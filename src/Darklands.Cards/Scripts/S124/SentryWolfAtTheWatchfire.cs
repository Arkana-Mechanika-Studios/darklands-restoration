using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd24": with the pilgrims at a watchfire around the sabbat field, a sentry wolf sniffs
/// each newcomer's hand. Passing (Virtue − 25) lets the party follow the pilgrims to the cooks
/// (card 323) or leave; failing, they must talk their way past (into the field, card 322) or fight
/// the wolf and the pilgrims. A saint may calm or scare off the wolf. The sabbat's alarm fact
/// (tag 1, type 7) grows with each slip.
/// </summary>
// orig: R124+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R124+0x02C4; hint R124+0x0338
//   option 1: action R124+0x035A; hint R124+0x036C
//   option 2: action R124+0x03A4; hint R124+0x03AA
//   option 3: action R124+0x03E2; hint R124+0x051C
//   option 4: action R124+0x0554; hint R124+0x0590
//   option 5: action R124+0x05C8; hint R124+0x05E6
//   option 6: action R124+0x061E; hint R124+0x0670
//   option 7: action R124+0x06A8; hint R124+0x072A
//   option 8: action R124+0x0766; hint R124+0x0778
//   option 9: action R124+0x07B0; hint R124+0x07BC
//   item pop-up: R124+0x08CE, R124+0x090A, R124+0x0946,
//     R124+0x0982
//   saint pop-up: R124+0x09BE, R124+0x0A46, R124+0x0ACE,
//     R124+0x0B56
//   other: R124+0x023A, R124+0x07F4, R124+0x0896
// DORMANT (generated): option slots 10..19 keep the default handler R124+0x0896 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SentryWolfAtTheWatchfire : Card
{
    public override int Number => 320;

    private const int WatchfirePicture = 155;
    private const int WolfPicture = 153;
    private const int CooksPicture = 138;

    public override int Run(CardContext c)
    {
        Show(c, WatchfirePicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems();
        c.SetCardSaints(3, 0x3D, 0x6B, 0x79);
        menu[3] = c.SaintsUsable();

        var assured = OptionHint.Saying(SuccessAssured);
        var unknown = OptionHint.Saying(CardTexts.Unknown);
        // DORMANT: in text block 4 the option lines 0..3 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 5 the option lines 0..5 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(chance => OfferAHand(c, menu, chance), () => VirtueChance(c)),
            // orig: R124+0x035A / R124+0x036C matched — refuse: text 2, the fight.
            new MenuLine(_ => { c.Screen.Text(2); Fight(c); }, assured),
            // orig: R124+0x03A4 / R124+0x03AA matched — attack.
            new MenuLine(_ => Fight(c), assured),
            new MenuLine(_ => CallASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => FollowThem(c), unknown),
            // orig: R124+0x05C8 / R124+0x05E6 matched — into the field.
            new MenuLine(_ => { RaiseTheAlarm(c, 1); c.Next = 322; }, unknown),
            new MenuLine(_ => ClaimToSeekInitiation(c), unknown),
            new MenuLine(_ => OfferAFriendAsCook(c, menu), assured),
            // orig: R124+0x0766 / R124+0x0778 matched — fight after all.
            new MenuLine(_ => { c.Screen.Text(2); Fight(c); }, unknown),
            // orig: R124+0x07B0 / R124+0x07BC matched
            // DORMANT: line 9 is never enabled; its handler only passes 0 hours and stays on the card.
            new MenuLine(_ => c.PassHours(0), assured));
    }

    // orig: the event_update(−1, −1, 0x1C, 7, n, 1, add) of the card's handlers matched
    private static void RaiseTheAlarm(CardContext c, int amount) =>
        c.Events.Update(-1, -1, 0x1C, 7, amount, 1, EventUpdate.Add);

    /// <summary>The best Virtue less 25.</summary>
    // orig: R124+0x0338 matched
    // C: the chance may be negative (no wrap); rand_mod(100) <= it then never succeeds.
    private static OptionHint VirtueChance(CardContext c)
    {
        var chance = c.PartyMaxSkill((int)Skill.Virtue) - 25;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R124+0x02C4 matched
    private static void OfferAHand(CardContext c, CardMenu menu, int chance)
    {
        c.Screen.Text(1);
        if (c.Random(100) <= chance)
        {
            menu.Offer(4, 4, 5);
            return;
        }
        menu.Offer(5, 6, 7, 8);
    }

    /// <summary>
    /// Saint 0 charms the wolf, 1 only holds it off (alarm +2), 2 scares it away (alarm −1),
    /// 3 the same (alarm +1); then the pilgrims ask the party along. A failed prayer starts the fight.
    /// </summary>
    // orig: R124+0x03E2 matched
    // C: an int function: `return 0` after a cancelled prayer, `return;` without a value after a heard one, and it
    //   falls off its end after the fight; ignored.
    private static void CallASaint(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        ShowPicture(c, WolfPicture);
        if (result == 0)
        {
            c.Screen.Text(10);
            RaiseTheAlarm(c, 2);
            Fight(c);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(7);
                break;
            case 1:
                RaiseTheAlarm(c, 2);
                c.Screen.Text(8);
                break;
            case 2:
                RaiseTheAlarm(c, -1);
                c.Screen.Text(9);
                break;
            case 3:
                RaiseTheAlarm(c, 1);
                c.Screen.Text(9);
                break;
        }
        menu.Offer(4, 4, 5);
        ShowPicture(c, WatchfirePicture);
    }

    // orig: R124+0x0554 matched
    private static void FollowThem(CardContext c)
    {
        ShowPicture(c, CooksPicture);
        c.Screen.Text(6);
        c.Next = 323;
    }

    // orig: R124+0x061E matched
    private static void ClaimToSeekInitiation(CardContext c)
    {
        ShowPicture(c, WolfPicture);
        c.Screen.Text(11);
        RaiseTheAlarm(c, 2);
        c.Next = 322;
    }

    // orig: R124+0x06A8 matched
    private static void OfferAFriendAsCook(CardContext c, CardMenu menu)
    {
        ShowPicture(c, WolfPicture);
        c.Screen.Text(12);
        RaiseTheAlarm(c, 1);
        menu.Offer(4, 4, 5);
        ShowPicture(c, WatchfirePicture);
    }

    /// <summary>
    /// The wolf and the pilgrims attack. Won: the party slips into the crowd (card 322); lost:
    /// card 334. Any other result stays here.
    /// </summary>
    // orig: R124+0x023A matched
    // C: rand_mod(2) is run_battle's only random argument; a result outside 0..4 leaves next_card unchanged.
    private static void Fight(CardContext c)
    {
        var enemies = c.Random(2) + 4;
        switch (c.RunBattle(0x48, -1, 0x2A, 3, enemies, 0x0D, 4, 3, -1, 0, 0))
        {
            case 0:
            case 1:
                c.DropAway();
                c.Variables.Chosen[0] = c.Leader;
                ShowPicture(c, WatchfirePicture);
                c.Screen.Text(3);
                RaiseTheAlarm(c, 2);
                c.Next = 322;
                break;
            case 2:
            case 3:
            case 4:
                c.DropAway();
                c.Next = 334;
                break;
        }
    }
}
