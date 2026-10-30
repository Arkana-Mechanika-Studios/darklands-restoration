using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf08": the ruins of an old church on a barren knoll, with a crypt below where the
/// dead are not at rest. When an item quest (tag 10) leads here, the item lies in the crypt:
/// the party can pray, move the rubble or blast a way in, then take the item (the skeletons
/// rise) or first purify the place with a saint or a potion.
/// </summary>
// orig: R108+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R108+0x0C04 (1838:0C04) matched (registration: item pop-up R108+0x0CDE; item slots 1..3 and saint slot 3 are empty functions; saint pop-ups do not name the member)
public sealed class CryptUnderTheRuinedChurch : Card
{
    private const int RuinPicture = 286;
    private const int CryptPicture = 354;
    private const int SkeletonsPicture = 174;

    // C: the card's own picture buffer (image-buffer); a lost battle replaces it with the crypt.
    private int _cardPicture;

    public override int Number => 256;

    public override int Run(CardContext c)
    {
        _cardPicture = RuinPicture;
        Show(c, RuinPicture);
        c.Next = Number;
        if (!c.Events.QuestAt(10, c.Here))
        {
            // Nothing here; the original returns 0x0C without changing following-card.
            c.Screen.Text(18);
            return CardContext.MapCard;
        }
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 9);
        // C: event-search does not set latest-event; event-detail-word-of(latest-event) reads the stale global.
        _ = c.Events.Find(8, -1, c.Here, -1, 10, -1);
        c.NameToSlot(0, c.EventDetail(c.LastEvent), 0);
        if (c.EventAtHere(0x55))
        {
            menu[1] = OptionState.Shown;
        }
        c.SetCardItems(0x66);
        c.SetCardSaints(0x11, 0x35, 0x44);
        menu[2] = c.ItemsUsable();
        menu[0] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();
        // C: item-handler-table slot 0 is the potion handler R108+0x0CDE, slots 1..3 bare retf (R108+0x0D30/0D32/0D34): Enter in the item box rates
        // C: line 2 with slot 2's and line 5 with slot 1's.
        menu.ItemBox = () => c.MenuSlot == 0 ? PotionHint(c, menu) : c.EmptyBoxHandler();

        return menu.Run(
            // orig: R108+0x04A8 (line 0); hint R108+0x054A (1838:054A) matched
            new MenuLine(_ => PrayForInsight(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R108+0x0582 (line 1); hint R108+0x067E (1838:067E) matched
            new MenuLine(p => MoveTheRubble(c, menu, p), () => RubbleHint(c)),
            // orig: R108+0x06AE (line 2); hint R108+0x0792 (1838:0792) matched
            new MenuLine(p => BlastAWayIn(c, menu, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c, menu)),
            // orig: R108+0x07CA (line 3); hint R108+0x0816 (1838:0816) matched
            new MenuLine(_ => TakeTheItem(c, menu), () => OptionHint.Say("You can always encounter the unknown")),
            // orig: R108+0x084E (line 4); hint R108+0x0A26 (1838:0A26) matched
            new MenuLine(_ => AskASaintToPurify(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R108+0x0A5E (line 5); hint R108+0x0B7A (1838:0B7A) matched
            new MenuLine(p => PurifyWithAPotion(c, menu, p), () => OptionHint.Say("Select a person and a potion."), () => PotionHint(c, menu)),
            // DORMANT: lines 6..8 are never offered; actions R108+0x0BB2/0bb6/0bba and hints
            // C: R108+0x0BB4/0bb8/0bbc are empty; the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            // orig: R108+0x0BBE (1838:0BBE) matched; hint R108+0x0BCC
            new MenuLine(_ => c.LeaveForTheMap(), () => OptionHint.Say(SuccessAssured)));
    }

    /// <summary>The potion's chance: its quality plus 10 and Artifice outside, (quality + 5) × 2 inside.</summary>
    // orig: R108+0x0CDE (1838:0CDE) matched
    // C: `r.a + 5 << 1` is (a + 5) * 2 (the shift binds looser).
    // C: slots 1..3 have bare retf handlers: the card lists one potion, so a click or a Shift hint never reaches them, but Enter does (see Run).
    // C: The handler reads the entry's potion (menu-choice-index).
    private static OptionHint PotionHint(CardContext c, CardMenu menu)
    {
        var m = c.MenuMember;
        c.Variables.Chosen[0] = m;
        var chance = c.ItemQuality(m, c.CardItems[c.MenuEntrySlot]);
        chance = menu.Block != 0 ? (chance + 5) * 2 : chance + c.Skill(m, (int)Skill.Artifice) + 10;
        return OptionHint.ChanceOf(chance, m);
    }

    // C: no clamp; top-scoring-member is left by the skill query.
    private static OptionHint RubbleHint(CardContext c) =>
        OptionHint.ChanceOf(c.PartyAverageAttribute((int)Attribute.Strength) + c.PartyMaxSkill((int)Skill.Artifice) / 2, OptionHint.WholeParty);

    /// <summary>Inside the crypt: take the item, purify the place by a saint or a potion, or get out.</summary>
    private static void InTheCrypt(CardContext c, CardMenu menu, int block)
    {
        menu.Set(OptionState.Hidden, 0, 1, 2, 6, 7, 8);
        menu.Set(OptionState.Enabled, 3, 4, 5, 9);
        c.SetCardItems(0x6E);
        c.SetCardSaints(0x0F, 0x6F, 0x2B);
        menu[5] = c.ItemsUsable();
        menu[4] = c.SaintsUsable();
        menu.Block = block;
    }

    // orig: R108+0x04A8 (1838:04A8) matched; tests: CardSmokeTests
    private void PrayForInsight(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            ShowPicture(c, 232);
            c.Screen.Text(2);
        }
        else
        {
            ShowPicture(c, 16);
            c.Screen.Text(1);
        }
        ShowPicture(c, _cardPicture);
        c.Next = 256;
    }

    /// <summary>Strength and craft open a way in; failing, the rubble caves in and the party must wait for dawn to try again.</summary>
    // orig: R108+0x0582 (1838:0582) matched; tests: CardSmokeTests
    private void MoveTheRubble(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            InTheCrypt(c, menu, 0x0C);
            ShowPicture(c, 164);
            menu.Block = 4;
            c.PassHours(2);
        }
        else
        {
            menu[1] = OptionState.Shown;
            c.EventHere(0x55, c.HoursUntil(5));
            c.PassHours(1);
            ShowPicture(c, RuinPicture);
            c.Screen.Text(13);
            ShowPicture(c, _cardPicture);
        }
        c.Next = 256;
    }

    /// <summary>The potion is spent; it blasts an entrance, or only scatters stones.</summary>
    // orig: R108+0x06AE (1838:06AE) matched; tests: CardSmokeTests
    private void BlastAWayIn(CardContext c, CardMenu menu, int chance)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= chance)
        {
            InTheCrypt(c, menu, 3);
            ShowPicture(c, 20);
        }
        else
        {
            ShowPicture(c, 19);
            c.Screen.Text(14);
            ShowPicture(c, _cardPicture);
        }
        c.Next = 256;
    }

    /// <summary>The item is touched and the dead rise; whatever the battle, the quest counts as done afterwards.</summary>
    // orig: R108+0x07CA (1838:07CA) matched; tests: CardSmokeTests
    private void TakeTheItem(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.Leader;
        ShowPicture(c, SkeletonsPicture);
        c.Screen.Text(7);
        FightTheSkeletons(c, menu);
        // BUG: a victory has already left for the map (location-now -1), so the errand's thing is given only after a defeat.
        c.FinishQuest10(c.Here);
    }

    /// <summary>
    /// An answered prayer seals the tombs (the item is taken). St. 0x2B shakes the crypt: the
    /// least agile member is hurt unless very clumsy indeed (10 or less, the original's test).
    /// </summary>
    // orig: R108+0x084E (1838:084E) matched; tests: CardSmokeTests
    private static void AskASaintToPurify(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        var answer = c.InvokeSaint(c.MenuMember, saint);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            c.FinishQuest10(c.Here);
            if (saint == 0x2B)
            {
                var agility = c.PartyMinEffectiveAgility();
                c.Variables.Chosen[1] = c.BestMember;
                if (agility <= 10)
                {
                    ShowPicture(c, 92);
                    c.Screen.Text(9);
                }
                else
                {
                    c.Injure(c.BestMember, 4, 8, 12);
                    ShowPicture(c, 93);
                    c.Screen.Text(16);
                }
            }
            else
            {
                ShowPicture(c, 63);
                c.Screen.Text(6);
            }
            c.PracticeSkill(c.MenuMember, (int)Skill.Virtue, 7, 10, 0);
            c.LeaveForTheMap();
            return;
        }
        InTheCrypt(c, menu, 0x0C);
        ShowPicture(c, 16);
        c.Screen.Text(1);
        ShowPicture(c, CryptPicture);
        c.Next = 256;
    }

    /// <summary>The potion (not used up: the original does not take it) purifies the crypt, or turns to mist.</summary>
    // orig: R108+0x0A5E (1838:0A5E) matched; tests: CardSmokeTests
    private static void PurifyWithAPotion(CardContext c, CardMenu menu, int chance)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        if (c.Random(100) <= chance)
        {
            c.FinishQuest10(c.Here);
            ShowPicture(c, 63);
            c.Screen.Text(5);
            c.PracticeSkill(c.MenuMember, (int)Skill.Virtue, 1, 10, 0);
            // The original leaves for the map without clearing location-now here.
            c.Next = CardContext.MapCard;
            return;
        }
        InTheCrypt(c, menu, 0x0C);
        ShowPicture(c, 333);
        c.Screen.Text(15);
        ShowPicture(c, CryptPicture);
        c.Next = 256;
    }

    /// <summary>
    /// The skeletons: won or fled, the party gets away (those away are lost); beaten, it wakes
    /// outside without the item and may go back in.
    /// </summary>
    // orig: R108+0x02B0 (1838:02B0) matched; tests: CardSmokeTests
    // C: right-to-left arguments: the number of skeletons is drawn before battle-driver draws its random seed.
    private void FightTheSkeletons(CardContext c, CardMenu menu)
    {
        var skeletons = c.Random(5) + 3;
        switch (c.RunBattle(0x5F, -1, 0x2D, 1, skeletons, -1, 0, 0, -1, 0, 0))
        {
            case 0:
                c.PassHours(1);
                c.Screen.Text(8);
                c.LeaveForTheMap();
                return;
            case 1:
            case 2:
                c.PassHours(1);
                ShowPicture(c, 287);
                c.Screen.Text(c.DropAway() != 0 ? 17 : 10);
                c.LeaveForTheMap();
                return;
            case 3:
                c.PassHours(3);
                InTheCrypt(c, menu, 0x0C);
                ShowPicture(c, CryptPicture);
                c.Next = 256;
                break;
            case 4:
                c.PassHours(3);
                InTheCrypt(c, menu, 0x0C);
                _cardPicture = CryptPicture;
                ShowPicture(c, SkeletonsPicture);
                c.Screen.Text(11);
                ShowPicture(c, _cardPicture);
                c.Next = 256;
                break;
        }
    }
}
