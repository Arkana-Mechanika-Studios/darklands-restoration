using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd37": the sabbat's feast. The party overhears where and when the next sabbat is. If
/// the innocents were tainted before (fact 9) only pigs are served and the party may let the
/// witches go or denounce them; otherwise the innocents are brought forth and the party must
/// attack, denounce the rite or pray. A denunciation that convinces them disperses the cult;
/// one that fails, an attack or a prayer by Saint 0x50/0x30 brings three waves of witches
/// (with the flying ointment, fact 0x0D, the party may flee before each). Saint 0x63 instead
/// moves a woman to adopt the innocents. Winning or leaving ends at the map; losing a wave
/// sends the party to "$Witcd38".
/// </summary>
// orig: R127+0x07C6 (18B4:0006) matched; tests: CardSmokeTests
//   option 0: action R127+0x0BC8; hint R127+0x0BE6
//   option 1: action R127+0x0C1E; hint R127+0x0D94
//   option 2: action R127+0x0DCA; hint R127+0x0ED2
//   option 3: action R127+0x0F0A; hint R127+0x1080
//   option 4: action R127+0x10B6; hint R127+0x1270
//   option 5: action R127+0x12A8; hint R127+0x12C6
//   option 6: action R127+0x12FE; hint R127+0x1474
//   option 7: action R127+0x14AA; hint R127+0x14C8
//   option 8: action R127+0x1504; hint R127+0x1522
//   option 9: action R127+0x155E; hint R127+0x157C
//   item pop-up: R127+0x1692, R127+0x16CE, R127+0x170A,
//     R127+0x1746
//   saint pop-up: R127+0x1782, R127+0x180A, R127+0x1892,
//     R127+0x191A
//   other: R127+0x0A56, R127+0x0AC2, R127+0x15B8,
//     R127+0x165A
// DORMANT (generated): option slots 10..19 keep the default handler R127+0x165A ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class InnocentsAtTheWitchesFeast : Card
{
    public override int Number => 333;

    private const int Picture = 138;
    private const int InnocentsPicture = 148;
    private const int AdoptionPicture = 157;

    /// <summary>The picture the card returns to after the ointment is refused (the original's pic_buf).</summary>
    private int _scene;

    public override int Run(CardContext c)
    {
        _scene = Picture;
        Show(c, Picture);
        c.Screen.Text(0);
        c.Next = Number;
        var menu = Menu(c, 1);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints(0x50, 0x30, 0x63);
        if (!c.Events.FactAnywhere(1, 9))
        {
            menu.Offer(2, 2, 3);
            menu[4] = c.SaintsUsable();
            _scene = InnocentsPicture;
            ShowPicture(c, InnocentsPicture);
        }

        var assured = OptionHint.Saying(SuccessAssured);
        Func<OptionHint> persuasion = () => PersuasionChance(c);
        // orig: R127+0x14AA / R127+0x1504 / R127+0x155E (hints R127+0x14C8 / R127+0x1522 / R127+0x157C) matched
        // DORMANT: lines 7..9 are never enabled in the card's own menu (the ointment's box shows lines 7 and 8 of
        //   block 11, but runs no handler); each shows text 0, passes 0 hours and goes to 0x62.
        Action<int> stayAWhile = _ =>
        {
            c.Screen.Text(0);
            c.PassHours(0);
            c.Next = 0x62;
        };
        // The pop-up box of the saint line runs the item handler (as the original does).
        // DORMANT: in text block 2 the option lines 0, 1 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            // orig: R127+0x0BC8 / R127+0x0BE6 matched
            new MenuLine(_ => SabbatAftermath.LeaveTheSite(c), assured),
            // orig: R127+0x0C1E / R127+0x0D94 matched
            new MenuLine(chance => Denounce(c, chance), persuasion),
            // orig: R127+0x0DCA / R127+0x0ED2 matched
            new MenuLine(_ => AttackAtOnce(c), assured),
            // orig: R127+0x0F0A / R127+0x1080 matched (the same as line 1)
            new MenuLine(chance => Denounce(c, chance), persuasion),
            new MenuLine(_ => PrayForTheInnocents(c, menu), OptionHint.Saying(CardTexts.SelectSaint),
                () => c.MemberPopupHint(SuccessAssured)),
            // orig: R127+0x12A8 / R127+0x12C6 matched
            new MenuLine(_ => SabbatAftermath.LeaveTheSite(c), assured),
            // orig: R127+0x12FE / R127+0x1474 matched (the same as line 1)
            new MenuLine(chance => Denounce(c, chance), persuasion),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured),
            new MenuLine(stayAWhile, assured));
    }

    /// <summary>The mean of the best Speak Common and the best Charisma; the Charisma query is the last, so its member speaks.</summary>
    // orig: R127+0x0D94 (18B4:05D4) matched (also R127+0x1080 (18B4:08C0), R127+0x1474 (18B4:0CB4))
    // C: checked against the asm: party_max_skill(Speak Common) is called first, party_max_attr(Charisma) second, so
    //   best_member (read as unsigned char) is the Charisma query's member. The halving truncates toward zero.
    private static OptionHint PersuasionChance(CardContext c)
    {
        var speech = c.PartyMaxSkill((int)Skill.SpeakCommon);
        var charisma = c.PartyMaxAttribute((int)Attribute.Charisma);
        return OptionHint.ChanceOf((charisma + speech) / 2, c.BestMember);
    }

    /// <summary>The party denounces the witches: they repent (text 6), or they attack (text 7).</summary>
    // orig: R127+0x0C1E (18B4:045E) matched (also R127+0x0F0A (18B4:074A), R127+0x12FE (18B4:0B3E))
    private void Denounce(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, InnocentsPicture);
            c.Screen.Text(6);
            c.PracticeSkill(Who.Everybody, (int)Skill.SpeakCommon, 1, 10, 0);
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, 2);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 20);
        }
        else
        {
            SetTheScene(c, SabbatAftermath.FightPicture);
            c.Screen.Text(7);
            if (!SurviveTheWaves(c))
            {
                return;
            }
            CelebrateTheVictory(c);
        }
        SabbatAftermath.LeaveTheSite(c);
    }

    // orig: R127+0x0DCA matched
    private void AttackAtOnce(CardContext c)
    {
        SetTheScene(c, SabbatAftermath.FightPicture);
        if (SurviveTheWaves(c))
        {
            CelebrateTheVictory(c);
            SabbatAftermath.LeaveTheSite(c);
        }
    }

    /// <summary>
    /// A saint's answer: the first two whisk the innocents away and the witches attack (text 3);
    /// the third makes a woman adopt them (block 4). An unanswered prayer (text 5) cannot be repeated.
    /// </summary>
    // orig: R127+0x10B6 matched
    // C: returns 0 after a cancelled box, otherwise falls off its end without a value (ignored).
    private void PrayForTheInnocents(CardContext c, CardMenu menu)
    {
        // C: menu_member / menu_slot are the globals the pop-up box set last.
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(5);
            menu[4] = OptionState.Shown;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
            case 1:
                SetTheScene(c, SabbatAftermath.FightPicture);
                c.Screen.Text(3);
                if (SurviveTheWaves(c))
                {
                    CelebrateTheVictory(c);
                    SabbatAftermath.LeaveTheSite(c);
                }
                break;
            case 2:
                menu.Offer(4, 5, 6);
                SetTheScene(c, AdoptionPicture);
                break;
            // DORMANT: slot 3 (card_saints[3] is −1) or a stale menu_slot outside 0..2: an answered prayer does nothing.
        }
    }

    private void SetTheScene(CardContext c, int picture)
    {
        _scene = picture;
        ShowPicture(c, picture);
    }

    /// <summary>Three waves of witches (texts 8 and 9 after the first two); false when the party fled or lost.</summary>
    // orig: the wave sequence repeated inline in R127+0x0C1E / _060a / _074a / _08f6 / _0b3e matched
    private bool SurviveTheWaves(CardContext c)
    {
        FightAWave(c, 8);
        if (!SabbatAftermath.Over(c))
        {
            FightAWave(c, 9);
        }
        if (!SabbatAftermath.Over(c))
        {
            FightAWave(c, 0);
        }
        return !SabbatAftermath.Over(c);
    }

    // orig: R127+0x0A56 matched (the ointment's box is R127+0x0AC2)
    // C: battle results outside 0..4 change nothing.
    private void FightAWave(CardContext c, int afterwards)
    {
        if (c.Events.FactAnywhere(1, 0x0D))
        {
            SabbatAftermath.OfferTheOintment(c, 0x0B, 0x0C, 7, _scene, _ => OptionHint.Say(SuccessAssured), 7, 8);
        }
        if (c.Next == SabbatAftermath.WorldMap)
        {
            return;
        }
        switch (c.RunBattle(0x50, -1, 0x1C, 3, 2, 0x29, 3, 6, -1, 1, 0))
        {
            case 0:
            case 1:
                if (afterwards != 0)
                {
                    c.Screen.Text(afterwards);
                }
                break;
            case 2:
            case 3:
            case 4:
                c.DropAway();
                c.Next = SabbatAftermath.Overwhelmed;
                break;
        }
    }

    /// <summary>The area is cleansed and the innocents rescued (text 10).</summary>
    // orig: the victory repeated inline in R127+0x0C1E / _060a / _074a / _08f6 / _0b3e matched
    private static void CelebrateTheVictory(CardContext c)
    {
        ShowPicture(c, SabbatAftermath.VictoryPicture);
        c.Screen.Text(10);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
        c.PracticeSkill(Who.Everybody, (int)Skill.Healing, 1, 10, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Religion, 1, 10, 0);
    }
}
