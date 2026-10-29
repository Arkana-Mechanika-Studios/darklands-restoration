using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;
using static Darklands.Cards.Scripts.SabbatFieldFacts;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd28": at a stream on the sabbat field a man in black robes dunks a line of witches in a
/// mockery of baptism. The party may attack, walk away, preach to the line, or queue up and then
/// refuse, submit to the soaking or call on a saint (Cyprian, Emydius, Gregory). Everything
/// ends back on the field (0x142), or at 0x14E when a lost fight leaves the party scattered.
/// </summary>
// orig: R125+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R125+0x02D2; hint R125+0x033C
//   option 1: action R125+0x0374; hint R125+0x03C0
//   option 2: action R125+0x03F8; hint R125+0x0444
//   option 3: action R125+0x0462; hint R125+0x053A
//   option 4: action R125+0x0572; hint R125+0x05BE
//   option 5: action R125+0x05F6; hint R125+0x066C
//   option 6: action R125+0x06A4; hint R125+0x07DE
//   option 7: action R125+0x0816; hint R125+0x0856
//   option 8: action R125+0x088E; hint R125+0x0904
//   option 9: action R125+0x093C; hint R125+0x095A
//   item pop-up: R125+0x0A70, R125+0x0AAC, R125+0x0AE8,
//     R125+0x0B24
//   saint pop-up: R125+0x0B60, R125+0x0BE8, R125+0x0C70,
//     R125+0x0CF8
//   other: R125+0x022C, R125+0x0996, R125+0x0A38
// DORMANT (generated): option slots 10..19 keep the default handler R125+0x0A38 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class SabbatMockBaptism : Card
{
    public override int Number => 324;

    private const int Picture = 141;
    private const int AngryCrowdPicture = 160;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems();
        c.SetCardSaints(0x1D, 0x2B, 0x3F);

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        var assured = OptionHint.Saying(SuccessAssured);
        // DORMANT: in text block 4 the option lines 0..3 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        // DORMANT: in text block 5 the option lines 0..3 are never offered (the switch to this block hides them); they can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => DisperseTheLine(c), unknown),
            new MenuLine(_ => WalkAway(c), assured),
            new MenuLine(chance => GetInLine(c, menu, chance), () => OverhearingChance(c)),
            new MenuLine(chance => Preach(c, chance), () => PreachingChance(c)),
            new MenuLine(_ => WalkAway(c), assured),
            new MenuLine(_ => TakeTheSoaking(c), assured),
            new MenuLine(_ => PrayAtTheStream(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R125+0x0816 / R125+0x0856 matched — break free and go back.
            new MenuLine(_ => BackToTheField(c, 1, 1), assured),
            // orig: R125+0x088E / R125+0x0904 matched (the same as R125+0x05F6)
            new MenuLine(_ => TakeTheSoaking(c), assured),
            // orig: R125+0x093C / R125+0x095A matched
            new MenuLine(_ => { c.Screen.Text(0); c.PassHours(0); c.Next = 0x62; }, assured));
    }

    /// <summary>The surprised line scatters; the black-robed man's cap is taken.</summary>
    // orig: R125+0x02D2 matched (also the won fight of R125+0x022C)
    private static void DisperseTheLine(CardContext c)
    {
        c.Screen.Text(1);
        Add(c, Suspicion, 2);
        Note(c, InitiatorsCap, 72);
        ReturnToTheField(c, 1);
    }

    // orig: R125+0x0374 / _0572 matched (identical)
    private static void WalkAway(CardContext c)
    {
        c.Screen.Text(3);
        BackToTheField(c, 1, 1);
    }

    // orig: R125+0x0444 matched
    private static OptionHint OverhearingChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyMaxAttribute((int)Attribute.Perception), c.BestMember);

    /// <summary>In the line the party may overhear where the next sabbat is (text 4) or not (text 5).</summary>
    // orig: R125+0x03F8 matched
    // C: an int function returning the new options_block; ignored.
    private static void GetInLine(CardContext c, CardMenu menu, int chance)
    {
        menu.Offer(0, 4, 5);
        menu[6] = c.SaintsUsable();
        menu.Block = c.Random(100) <= chance ? 4 : 5;
    }

    /// <summary>Speak Common of the member found last, plus half the best Charisma.</summary>
    // orig: R125+0x053A matched
    // C: checked against the asm: member_skill reads best_member (as unsigned char) BEFORE party_max_attr(5) sets it,
    //   i.e. the member some earlier query left there (stale global); the hint then names the best Charisma.
    private static OptionHint PreachingChance(CardContext c)
    {
        var chance = c.Skill(c.BestMember, (int)Skill.SpeakCommon);
        chance += c.PartyMaxAttribute((int)Attribute.Charisma) / 2;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R125+0x0462 matched
    // C: party_max_skill(9) is called only to set best_member, which add_skill reads as unsigned char.
    private static void Preach(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(6);
            c.PracticeSkill(Who.Everybody, (int)Skill.SpeakCommon, 1, 10, 0);
            c.PartyMaxSkill((int)Skill.Virtue);
            c.AddSkill(c.BestMember, (int)Skill.Virtue, 1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 5);
            BackToTheField(c, 1, 1);
            return;
        }
        ShowPicture(c, AngryCrowdPicture);
        c.Screen.Text(7);
        ShowPicture(c, Picture);
        FightTheLine(c);
    }

    // orig: R125+0x022C matched
    // C: no random argument; a result outside 0..4 leaves next_card unchanged (the card stays).
    private static void FightTheLine(CardContext c)
    {
        switch (c.RunBattle(0x4A, -1, 0x29, 3, 2, 0x0D, 2, 6, -1, 0, 0))
        {
            case 0 or 1:
                DisperseTheLine(c);
                break;
            case 2 or 3 or 4:
                c.DropAway();
                c.Next = 0x14E;
                break;
        }
    }

    /// <summary>The unholy baptism taints everyone.</summary>
    // orig: R125+0x05F6 matched
    private static void TakeTheSoaking(CardContext c)
    {
        c.Screen.Text(8);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -20);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -20);
        c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, -5);
        BackToTheField(c, -3, 1);
    }

    /// <summary>
    /// Cyprian spoils each dunking, Emydius sends an earthquake, Gregory turns the stream back;
    /// a failed prayer gets the praying member grabbed by the man in black (block 13).
    /// </summary>
    // orig: R125+0x06A4 matched
    // C: an int function: `return 0` after a cancelled prayer, `return;` without a value after a heard one (a slot
    //   outside 0..2 changes nothing), and it falls off its end after a failed one; ignored.
    private static void PrayAtTheStream(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            menu.Offer(13, 7, 8);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(9);
                BackToTheField(c, -3, 1);
                break;
            case 1:
                c.Screen.Text(11);
                c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
                Note(c, InitiationsEnded, 16);
                BackToTheField(c, -1, 3);
                break;
            case 2:
                c.Screen.Text(12);
                c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
                BackToTheField(c, 1, 1);
                break;
        }
    }
}
