using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd30": witches prepare the altar for tomorrow's summoning and ask the party to help.
/// Refusing, the party may slip away or pray to purify the place (a failed prayer brings an
/// old man to ask what they are saying: attack him or talk, better with the initiator's cap);
/// helping, they draw the pentagrams or sabotage the altar. Everything ends back on the field
/// (0x142), or at 0x14E after a lost fight.
/// </summary>
// orig: R125+0x18A8 (19C2:0008) matched; tests: CardSmokeTests
//   option 0: action R125+0x1BB8; hint R125+0x1BE0
//   option 1: action R125+0x1C18; hint R125+0x1C42
//   option 2: action R125+0x1C7A; hint R125+0x1C98
//   option 3: action R125+0x1CD0; hint R125+0x1DEE
//   option 4: action R125+0x1E26; hint R125+0x1E38
//   option 5: action R125+0x1E70; hint R125+0x1F46
//   option 6: action R125+0x1F66; hint R125+0x1FFA
//   option 7: action R125+0x2032; hint R125+0x2140
//   option 8: action R125+0x2160; hint R125+0x217E
//   option 9: action R125+0x21BA; hint R125+0x21D8
//   item pop-up: R125+0x22EE, R125+0x232A, R125+0x2366,
//     R125+0x23A2
//   saint pop-up: R125+0x23DE, R125+0x2466, R125+0x24EE,
//     R125+0x2576
//   other: R125+0x1ADA, R125+0x2214, R125+0x22B6
// DORMANT (generated): option slots 10..19 keep the default handler R125+0x22B6 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SabbatAltarPreparation : Card
{
    public override int Number => 326;

    private const int Picture = 148;
    private const int AfterTheFightPicture = 146;
    private const int OldManPicture = 155;
    private const int ForemanPicture = 149;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints(0x0F, 0x2B, 0x3F, 0x6F);

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        var assured = OptionHint.Saying(SuccessAssured);
        // orig: R125+0x2160 / R125+0x21BA matched — lines 8 and 9 (never enabled).
        var noLine = new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured);
        // DORMANT: in text block 1 the option lines 0, 1 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 3 the option lines 0..3 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        // DORMANT: in text block 10 the option lines 0..5 are never offered (an Offer for this block lists other lines); they can be offered in other blocks only.
        return menu.Run(
            // orig: R125+0x1BB8 / R125+0x1BE0 matched — refuse (an int function returning options_block; ignored).
            new MenuLine(_ => menu.Offer(1, 2, 3), unknown),
            // orig: R125+0x1C18 / R125+0x1C42 matched — help.
            new MenuLine(_ => menu.Offer(10, 6, 7), assured),
            // orig: R125+0x1C7A / R125+0x1C98 matched — slip away.
            new MenuLine(_ => { Add(c, Suspicion, 2); c.Next = 0x142; }, unknown),
            new MenuLine(_ => PrayToPurifyThePlace(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R125+0x1E26 / R125+0x1E38 matched — attack the old man.
            new MenuLine(_ => { c.Screen.Text(5); FightTheWitches(c); }, assured),
            new MenuLine(chance => TalkToTheOldMan(c, chance), () => TalkingChance(c)),
            new MenuLine(_ => DrawThePentagrams(c), assured),
            new MenuLine(chance => SabotageTheAltar(c, chance), () => SabotageChance(c)),
            noLine, noLine);
    }

    /// <summary>Back to the field by way of text 4, the card's own picture shown.</summary>
    // orig: the won-fight tail of R125+0x1ADA matched
    private static void WanderOff(CardContext c, int suspicion)
    {
        Add(c, Suspicion, suspicion);
        ShowPicture(c, Picture);
        c.Screen.Text(4);
        Add(c, SightsVisited, 1);
        c.Next = 0x142;
    }

    // orig: R125+0x1ADA matched
    // C: no random argument; a result outside 0..4 leaves next_card unchanged (the card stays).
    private static void FightTheWitches(CardContext c)
    {
        switch (c.RunBattle(0x4C, -1, 0x29, 3, 2, 0x0D, 2, 6, -1, 0, 0))
        {
            case 0 or 1:
                ShowPicture(c, AfterTheFightPicture);
                c.Screen.Text(6);
                WanderOff(c, 2);
                break;
            case 2 or 3 or 4:
                c.DropAway();
                c.Next = 0x14E;
                break;
        }
    }

    // orig: R125+0x1CD0 matched
    // C: an int function: `return 0` after a cancelled prayer (the suspicion is already raised), `return;` without a
    //   value after a heard one, and it falls off its end after a failed one; ignored.
    private static void PrayToPurifyThePlace(CardContext c, CardMenu menu)
    {
        Add(c, Suspicion, 1);
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result != 0)
        {
            ShowPicture(c, Picture);
            c.Screen.Text(2);
            Note(c, AltarPurified, 72);
            ShowPicture(c, Picture);
            c.Screen.Text(4);
            Add(c, SightsVisited, 1);
            c.Next = 0x142;
            return;
        }
        menu.Offer(3, 4, 5);
        ShowPicture(c, OldManPicture);
    }

    // orig: R125+0x1F46 matched
    // C: party_max_skill_sum sets best_member, which the hint reads (as unsigned char).
    private static OptionHint TalkingChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyMaxSkillSum((int)Skill.SpeakCommon, (int)Skill.Streetwise), c.BestMember);

    /// <summary>With the initiator's cap a member plays the master (texts 7 and 11); else the party talks (text 8).</summary>
    // orig: R125+0x1E70 matched
    private static void TalkToTheOldMan(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            if (Known(c, InitiatorsCap))
            {
                c.Screen.Text(7);
                c.Screen.Text(11);
            }
            else
            {
                c.Screen.Text(8);
            }
            Add(c, Suspicion, 1);
            c.PracticeSkill(Who.Everybody, (int)Skill.Streetwise, 1, 10, 0);
            ShowPicture(c, Picture);
            c.Screen.Text(4);
            Add(c, SightsVisited, 1);
            c.Next = 0x142;
            return;
        }
        c.Screen.Text(9);
        c.Screen.Text(5);
        FightTheWitches(c);
    }

    /// <summary>The Virtue lost is that of whoever the last party query found.</summary>
    // orig: R125+0x1F66 matched
    // C: add_skill reads best_member (as unsigned char) as some earlier query left it (stale global).
    private static void DrawThePentagrams(CardContext c)
    {
        ShowPicture(c, Picture);
        c.Screen.Text(12);
        Add(c, Suspicion, -1);
        c.AddSkill(c.BestMember, (int)Skill.Virtue, -3);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -10);
        c.Screen.Text(4);
        Add(c, SightsVisited, 1);
        c.Next = 0x142;
    }

    /// <summary>One and a half times the party's average Stealth.</summary>
    // orig: R125+0x2140 matched
    // C: (avg * 3) / 2 in 16 bits, a signed division; the average is at most 100, so nothing wraps.
    private static OptionHint SabotageChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Stealth) * 3 / 2, OptionHint.WholeParty);

    // orig: R125+0x2032 matched
    private static void SabotageTheAltar(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, ForemanPicture);
            c.Screen.Text(13);
            Add(c, Suspicion, 1);
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            Note(c, AltarSabotaged, 72);
            ShowPicture(c, Picture);
            c.Screen.Text(4);
            Add(c, SightsVisited, 1);
            c.Next = 0x142;
            return;
        }
        ShowPicture(c, ForemanPicture);
        c.Screen.Text(14);
        FightTheWitches(c);
    }
}
