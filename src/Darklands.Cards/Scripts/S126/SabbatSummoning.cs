using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd34": the summoning at the altar. If a saint purified the altar the priest first seeks
/// the guilty among the crowd (flee, meet his gaze, or pray); then Reinhard of the Great
/// Monastery convenes the sabbat and the demon rises. The party may flee, challenge the throng,
/// wait and fight the demon, let the ritual finish (0x14B) or invoke their sabotage. Fights come
/// in waves (the stolen ointment offers a way out between them); surviving them all leads to
/// 0x14C, fleeing the field to 0x0C, a lost fight to 0x14E.
/// </summary>
// orig: R126+0x1650 (199D:0000) matched; tests: CardSmokeTests
//   option 0: action R126+0x1B08; hint R126+0x1CAC
//   option 1: action R126+0x1CC2; hint R126+0x1EA6
//   option 2: action R126+0x1EBC; hint R126+0x20A8
//   option 3: action R126+0x20E0; hint R126+0x2284
//   option 4: action R126+0x22B2; hint R126+0x23A6
//   option 5: action R126+0x23DE; hint R126+0x251C
//   option 6: action R126+0x2554; hint R126+0x25C2
//   option 7: action R126+0x25FA; hint R126+0x2738
//   option 8: action R126+0x2770; hint R126+0x278E
//   option 9: action R126+0x27CA; hint R126+0x27E8
//   item pop-up: R126+0x28FE, R126+0x293A, R126+0x2976,
//     R126+0x29B2
//   saint pop-up: R126+0x29EE, R126+0x2A76, R126+0x2AFE,
//     R126+0x2B86
//   other: R126+0x196A, R126+0x1A02, R126+0x2824,
//     R126+0x28C6
// DORMANT (generated): option slots 10..19 keep the default handler R126+0x28C6 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
public sealed class SabbatSummoning : Card
{
    public override int Number => 330;

    private const int Picture = 133;
    private const int PriestPicture = 219;
    private const int RitualPicture = 150;
    private const int CrowdPicture = 148;
    private const int ThrongPicture = 160;
    private const int DemonPicture = 151;
    private const int SwoonPicture = 143;
    private const int OintmentPicture = 149;

    /// <summary>The text after the demon's fall, and the code that asks for the demon's battle.</summary>
    private const int DemonFight = 18;

    /// <summary>The picture the card returns to (<c>pic_buf</c>, changed as the ceremony goes on).</summary>
    private int _picture;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Variables.Location = c.NearestLocationOfType(c.State.MapPosition.X, c.State.MapPosition.Y, 0x16);
        c.Next = Number;
        c.StartQuest7();
        c.Screen.Text(0);
        var menu = Menu(c, 1);
        if (Known(c, AltarPurified))
        {
            menu.Offer(1, 0, 1, 2);
            _picture = PriestPicture;
        }
        else
        {
            Show(c, PriestPicture);
            c.Screen.Text(5);
            OfferTheRitualChoices(c, menu);
            _picture = RitualPicture;
        }
        ShowPicture(c, _picture);
        c.SetCardItems();
        c.SetCardSaints(0x22, 0x54);
        if (menu[2] != OptionState.Hidden)
        {
            menu[2] = c.SaintsUsable();
        }

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: R126+0x2770 / R126+0x27CA (hints R126+0x278E / R126+0x27E8) matched
        // DORMANT: lines 8 and 9 are never enabled in the card's own menu (only the ointment's box below offers them,
        //   without these handlers); each shows text 0, passes 0 hours and goes to 0x62.
        var noLine = new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured);
        return menu.Run(
            // orig: R126+0x1B08 / R126+0x1CAC matched — flee from the priest's search.
            new MenuLine(chance => SlipAway(c, chance), () => OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Streetwise), OptionHint.WholeParty)),
            // orig: R126+0x1CC2 / R126+0x1EA6 matched
            new MenuLine(chance => MeetHisGaze(c, menu, chance), () => OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Virtue), OptionHint.WholeParty)),
            new MenuLine(_ => PrayForStrength(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R126+0x20E0 / R126+0x2284 matched — the same flight as line 0, with another chance.
            new MenuLine(chance => SlipAway(c, chance), () => EscapeChance(c)),
            new MenuLine(_ => ChallengeTheThrong(c), assured),
            // orig: R126+0x23DE matched
            new MenuLine(_ => FightTheDemon(c, 16), assured),
            new MenuLine(_ => LetTheRitualFinish(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R126+0x25FA matched (the same as line 5 with text 15)
            new MenuLine(_ => FightTheDemon(c, 15), assured),
            noLine, noLine);
    }

    /// <summary>The demon rises (block 2): sabotage can only be invoked when the altar was sabotaged.</summary>
    // orig: the block-2 flag set repeated inline in R126+0x1650 / _0672 / _086c matched
    private static void OfferTheRitualChoices(CardContext c, CardMenu menu)
    {
        menu.Offer(2, 3, 4, 5, 6);
        menu[7] = Known(c, AltarSabotaged) ? OptionState.Enabled : OptionState.Hidden;
    }

    /// <summary>The priest gives up the search; Reinhard convenes the sabbat.</summary>
    // orig: the tail of R126+0x1CC2 / _086c matched
    private void TheRitualBegins(CardContext c, CardMenu menu)
    {
        c.Screen.Text(5);
        OfferTheRitualChoices(c, menu);
        _picture = RitualPicture;
        ShowPicture(c, _picture);
    }

    // orig: R126+0x2284 matched
    // C: the two averages draw nothing, so their order does not matter; the sum stays far below 32767.
    private static OptionHint EscapeChance(CardContext c)
    {
        var stealth = c.PartyAverageSkill((int)Skill.Stealth);
        var streetwise = c.PartyAverageSkill((int)Skill.Streetwise);
        return OptionHint.ChanceOf((stealth + streetwise) / 2, OptionHint.WholeParty);
    }

    /// <summary>Through the crowd and off the field (the site is left for good), or caught (text 4).</summary>
    // orig: R126+0x1B08 matched (R126+0x20E0 is identical)
    private void SlipAway(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, CrowdPicture);
            c.Screen.Text(3);
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -15);
            if (!Known(c, FeastSpoiled))
            {
                c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, -2);
            }
            AbandonSite(c);
            c.Next = 0x0C;
            return;
        }
        ShowPicture(c, PriestPicture);
        c.Screen.Text(4);
        FaceTheThrong(c);
    }

    // orig: R126+0x1CC2 matched
    // C: declared int, returns without a value on both paths (ignored).
    private void MeetHisGaze(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, CrowdPicture);
            c.Screen.Text(9);
            ShowPicture(c, PriestPicture);
            TheRitualBegins(c, menu);
            return;
        }
        ShowPicture(c, CrowdPicture);
        c.Screen.Text(10);
        FaceTheThrong(c);
    }

    // orig: R126+0x1EBC matched
    // C: returns 0 after a cancelled box, otherwise returns without a value (ignored).
    private void PrayForStrength(CardContext c, CardMenu menu)
    {
        // C: menu_member / menu_slot are the globals the pop-up box set last.
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            ShowPicture(c, CrowdPicture);
            c.Screen.Text(11);
            _picture = PriestPicture;
            ShowPicture(c, _picture);
            TheRitualBegins(c, menu);
            return;
        }
        // The throng is shown, but the card keeps its picture to return to.
        ShowPicture(c, ThrongPicture);
        c.Screen.Text(12);
        FightTheWaves(c);
    }

    // orig: R126+0x22B2 matched
    private void ChallengeTheThrong(CardContext c)
    {
        _picture = ThrongPicture;
        ShowPicture(c, _picture);
        c.Screen.Text(13);
        FightTheWaves(c);
    }

    /// <summary>The whole sabbat attacks.</summary>
    // orig: the failure path of R126+0x1B08 / _0672 / _0a90 matched
    private void FaceTheThrong(CardContext c)
    {
        _picture = ThrongPicture;
        ShowPicture(c, _picture);
        FightTheWaves(c);
    }

    private static bool Over(CardContext c) => c.Next is 0x14E or 0x0C;

    /// <summary>Three waves of witches; after them the demon's absence is judged by whether it was fought before.</summary>
    // orig: the wave sequence repeated inline in R126+0x1B08 / _0672 / _086c / _0a90 / _0c62 matched
    private void FightTheWaves(CardContext c)
    {
        FightAWave(c, 6);
        if (!Over(c))
        {
            FightAWave(c, 7);
        }
        if (!Over(c))
        {
            FightAWave(c, 0);
        }
        if (!Over(c))
        {
            if (Known(c, DemonFought))
            {
                Redeemed(c);
            }
            else
            {
                c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
                c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -15);
            }
            c.Next = 0x14C;
        }
    }

    /// <summary>The demon is challenged (text 16, or 15 with the sabotage), fought, then the waves of its witches.</summary>
    // orig: R126+0x23DE matched (R126+0x25FA differs only in the challenge text)
    private void FightTheDemon(CardContext c, int challenge)
    {
        ShowPicture(c, RitualPicture);
        c.Screen.Text(challenge);
        c.Screen.Text(17);
        Note(c, DemonFought, 72);
        _picture = DemonPicture;
        ShowPicture(c, _picture);
        FightAWave(c, DemonFight);
        _picture = ThrongPicture;
        ShowPicture(c, _picture);
        if (!Over(c))
        {
            FightAWave(c, 7);
        }
        if (!Over(c))
        {
            FightAWave(c, 0);
        }
        if (!Over(c))
        {
            Redeemed(c);
            c.Next = 0x14C;
        }
    }

    // orig: the reward repeated inline in R126+0x1B08 .. _0faa matched
    private static void Redeemed(CardContext c)
    {
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
        c.PracticeSkill(Who.Everybody, (int)Skill.Healing, 1, 10, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Religion, 1, 10, 0);
    }

    /// <summary>
    /// One fight (the demon's for <see cref="DemonFight"/>), offered the ointment's escape first;
    /// a won fight shows <paramref name="text"/> (none for 0).
    /// </summary>
    // orig: R126+0x196A matched
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
        var result = text == DemonFight
            ? c.RunBattle(0x73, -1, 0x35, 5, 1, -1, 0, 0, -1, 0, 0)
            : c.RunBattle(0x4E, -1, 0x1C, 4, 1, 0x29, 3, 7, -1, 0, 0);
        switch (result)
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

    /// <summary>A small menu of its own (block 19, lines 8 and 9): fly away on the stolen ointment or stay.</summary>
    // orig: R126+0x1A02 matched
    // C: the box writes opt_on[] directly (lines 8, 9 on, all others off) and re-points opt_fn[8], opt_fn[9] at the
    //   same "Success is assured." hints; opt_flag_0..9 are untouched, so the card's menu is unchanged afterwards.
    //   No option handler runs: only the chosen line counts (8 = fly away, anything else = stay).
    private void OfferTheOintment(CardContext c)
    {
        ShowPicture(c, OintmentPicture);
        var lines = new OptionState[CardMenu.MaxLines];
        lines[8] = OptionState.Enabled;
        lines[9] = OptionState.Enabled;
        var line = c.Screen.Choose(19, lines, _ => OptionHint.Say(SuccessAssured));
        if (line != 8)
        {
            ShowPicture(c, _picture);
            return;
        }
        c.Screen.Text(20);
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

    // orig: R126+0x2554 matched
    private static void LetTheRitualFinish(CardContext c)
    {
        ShowPicture(c, SwoonPicture);
        c.Screen.Text(14);
        Add(c, Suspicion, -3);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        c.Next = 0x14B;
    }
}
