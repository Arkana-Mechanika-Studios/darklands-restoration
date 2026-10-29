using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd35": the morning after the summoning, the witches' feast. If the feast was spoiled the
/// party can only leave; otherwise they may leave, try to save the four unbaptized innocents
/// (baptize them, preach to the cooks, or pray) or pretend to take part (0x14D). Leaving the
/// sabbat sends the site to its abandoned card and the party to 0x0C.
/// </summary>
// orig: R126+0x2C0E (1AF8:000E) matched; tests: CardSmokeTests
//   option 0: action R126+0x3024; hint R126+0x3042
//   option 1: action R126+0x307A; hint R126+0x3082
//   option 2: action R126+0x30BA; hint R126+0x30F4
//   option 3: action R126+0x312C; hint R126+0x3162
//   option 4: action R126+0x319A; hint R126+0x31A2
//   option 5: action R126+0x31DA; hint R126+0x3264
//   option 6: action R126+0x328C; hint R126+0x33D2
//   option 7: action R126+0x3402; hint R126+0x34B4
//   option 8: action R126+0x34EC; hint R126+0x350A
//   option 9: action R126+0x3546; hint R126+0x3564
//   item pop-up: R126+0x367A, R126+0x36B6, R126+0x36F2,
//     R126+0x372E
//   saint pop-up: R126+0x376A, R126+0x37F2, R126+0x387A,
//     R126+0x3902
//   other: R126+0x2EB2, R126+0x2F1E, R126+0x35A0,
//     R126+0x3642
// DORMANT (generated): option slots 10..19 keep the default handler R126+0x3642 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 1, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SabbatWitchFeast : Card
{
    public override int Number => 331;

    private const int Picture = 133;
    private const int FeastPicture = 138;
    private const int OintmentPicture = 149;
    private const int ThrongPicture = 160;
    private const int MaidenPicture = 157;

    /// <summary>The picture the card returns to.</summary>
    private int _picture;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 2);
        // C: the loop rates a pop-up choice with item_fn[0][menu_slot] (R126+0x367A..: $ChosenOneName = the member, "Success is
        // C: assured."), the saint box's own handlers (R126+0x376A..: the prayer's chance beside the member) answer a Shift hint
        // C: in the box and an Enter there.
        menu.ItemBox = () => c.MemberPopupHint(SuccessAssured);
        menu.SaintBox = c.SaintPopupChanceHint;
        menu.PopupRatedByItems = true;
        menu.Enable(2, 3, 4);
        c.SetCardItems();
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        c.Screen.Text(0);
        if (Known(c, FeastSpoiled))
        {
            // Block 1 has two lines, but only "leave the Sabbat" is offered.
            menu.Offer(1, 0);
            _picture = OintmentPicture;
        }
        else
        {
            _picture = FeastPicture;
        }
        ShowPicture(c, _picture);

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: R126+0x34EC / R126+0x3546 (hints R126+0x350A / R126+0x3564) matched
        // DORMANT: lines 8 and 9 are never enabled in the card's own menu (only the ointment's box shows them, without
        //   these handlers); each shows text 0, passes 0 hours and goes to 0x62.
        var noLine = new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured);
        // DORMANT: in text block 2 the option line 0 is never offered (the first menu on this block never enables them); it can be offered in other blocks only.
        // DORMANT: in text block 12 the option lines 0, 2..4 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => LeaveTheSabbat(c), assured),
            // orig: R126+0x307A / R126+0x3082 matched
            // DORMANT: line 1 of block 1 is never enabled (cleared in both start-up branches); it would go on to 0x14D.
            new MenuLine(_ => c.Next = 0x14D, assured),
            new MenuLine(_ => LeaveInShame(c), assured),
            // orig: R126+0x312C matched
            // C: declared int, falls off its end without a value (ignored). Line 7 (the prayer) is enabled without
            //   saints_usable: this card never asks whether anyone knows one of its saints.
            new MenuLine(_ => { c.Screen.Text(3); menu.Offer(12, 5, 6, 7); }, assured),
            new MenuLine(_ => c.Next = 0x14D, assured),
            new MenuLine(chance => BaptizeTheInnocents(c, chance), () => BaptismChance(c)),
            new MenuLine(chance => Preach(c, chance), () => PreachingChance(c)),
            // The original takes the pop-up's answer from the item handlers here: the member chosen becomes $ChosenOneName.
            new MenuLine(_ => PrayForTheInnocents(c), OptionHint.Saying(CardTexts.SelectSaint), () => c.MemberPopupHint(SuccessAssured)),
            noLine, noLine);
    }

    // orig: R126+0x3024 matched
    private static void LeaveTheSabbat(CardContext c)
    {
        AbandonSite(c);
        c.Next = 0x0C;
    }

    /// <summary>Leaving the innocents to their fate.</summary>
    // orig: R126+0x30BA matched
    private static void LeaveInShame(CardContext c)
    {
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        LeaveTheSabbat(c);
    }

    /// <summary>Three quarters of the best Religion.</summary>
    // orig: R126+0x3264 matched
    // C: max Religion × 3 stays far below 32767; the division truncates.
    private static OptionHint BaptismChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyMaxSkill((int)Skill.Religion) * 3 / 4, OptionHint.WholeParty);

    /// <summary>Done quietly (text 4) or with stumbles the witches notice (text 5): the innocents are safe either way.</summary>
    // orig: R126+0x31DA matched
    private static void BaptizeTheInnocents(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(4);
        }
        else
        {
            c.Screen.Text(5);
            Add(c, Suspicion, 2);
        }
        c.PracticeSkill(Who.Everybody, (int)Skill.Religion, 1, 10, 0);
        InnocentsSaved(c);
    }

    // orig: the reward repeated inline in R126+0x31DA / _068c matched
    private static void InnocentsSaved(CardContext c)
    {
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
        Note(c, FeastSpoiled, 24);
        c.Next = 0x14D;
    }

    /// <summary>The best Charisma plus half the best Speak Common.</summary>
    // orig: R126+0x33D2 matched
    // C: party_max_attr / party_max_skill also set best_member, which nothing on this card reads afterwards.
    private static OptionHint PreachingChance(CardContext c)
    {
        var chance = c.PartyMaxAttribute((int)Attribute.Charisma);
        chance += c.PartyMaxSkill((int)Skill.SpeakCommon) / 2;
        return OptionHint.ChanceOf(chance, OptionHint.WholeParty);
    }

    // orig: R126+0x328C matched
    private void Preach(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(6);
            c.PracticeSkill(Who.Everybody, (int)Skill.SpeakCommon, 1, 10, 0);
            InnocentsSaved(c);
            return;
        }
        _picture = ThrongPicture;
        ShowPicture(c, _picture);
        c.Screen.Text(7);
        FightAWave(c, 13);
        if (!Over(c))
        {
            FightAWave(c, 14);
        }
        if (!Over(c))
        {
            FightAWave(c, 0);
        }
        if (!Over(c))
        {
            // The original shows text 7 again here, not the victory text 15.
            c.Screen.Text(7);
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
            c.PracticeSkill(Who.Everybody, (int)Skill.Healing, 1, 10, 0);
            c.PracticeSkill(Who.Everybody, (int)Skill.Religion, 1, 10, 0);
            LeaveTheSabbat(c);
        }
    }

    private static bool Over(CardContext c) => c.Next is 0x14E or 0x0C;

    /// <summary>One wave of the feast's witches, offered the ointment's escape first; a won fight shows <paramref name="text"/> (none for 0).</summary>
    // orig: R126+0x2EB2 matched
    // C: battle results outside 0..4 change nothing.
    private void FightAWave(CardContext c, int text)
    {
        if (Known(c, FlyingOintment))
        {
            OfferTheOintment(c);
        }
        if (c.Next == 0x0C)
        {
            return;
        }
        switch (c.RunBattle(0x4F, -1, 0x29, 3, 2, 0x0D, 3, 6, -1, 1, 0))
        {
            case 0 or 1:
                if (text != 0)
                {
                    c.Screen.Text(text);
                }
                break;
            case 2 or 3 or 4:
                c.DropAway();
                c.Next = 0x14E;
                break;
        }
    }

    /// <summary>A small menu of its own (block 16, lines 8 and 9): fly away on the stolen ointment or stay.</summary>
    // orig: R126+0x2F1E matched
    // C: the box writes opt_on[] directly (lines 8, 9 on, all others off) and re-points opt_fn[8], opt_fn[9] at the
    //   same hints; opt_flag_0..9 are untouched. No option handler runs: only the chosen line counts.
    private void OfferTheOintment(CardContext c)
    {
        ShowPicture(c, OintmentPicture);
        var lines = new OptionState[CardMenu.MaxLines];
        lines[8] = OptionState.Enabled;
        lines[9] = OptionState.Enabled;
        var line = c.Screen.Choose(16, lines, _ => OptionHint.Say(SuccessAssured));
        if (line != 8)
        {
            ShowPicture(c, _picture);
            return;
        }
        c.Screen.Text(17);
        AbandonSite(c);
        c.Next = 0x0C;
        if (Known(c, FeastSpoiled))
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -10);
            return;
        }
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -25);
    }

    /// <summary>
    /// Saints 0 and 1 send a maiden to lead the innocents away, the third makes the cooks find
    /// them already baptized, the fourth lifts them into the air. An unanswered prayer only shows
    /// the innocents' text again (text 12, not 11).
    /// </summary>
    // orig: R126+0x3402 matched
    // C: returns 0 after a cancelled box, otherwise falls off its end without a value (ignored).
    private static void PrayForTheInnocents(CardContext c)
    {
        // C: menu_member / menu_slot are the globals the pop-up box set last.
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(12);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0 or 1:
                ShowPicture(c, MaidenPicture);
                c.Screen.Text(8);
                c.Screen.Text(18);
                break;
            case 2:
                c.Screen.Text(9);
                break;
            case 3:
                c.Screen.Text(10);
                break;
            default:
                // DORMANT: a slot outside 0..3 (only a stale menu_slot) answers the prayer with nothing at all.
                return;
        }
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
        c.Next = 0x14D;
    }
}
