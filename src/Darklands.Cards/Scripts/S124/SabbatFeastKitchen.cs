using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd27": at the sabbat's cookpots an old crone sets the party to prepare the "meat":
/// unbaptized infants. They may walk away in horror (a heavy loss of Virtue and Divine Favor),
/// baptize them (Religion), preach to the cooks (Charisma + Common / 2) or pray; saving the
/// infants spoils the feast (fact 9). Back to the field (card 322), or card 334 after a lost fight.
/// </summary>
// orig: R124+0x22AC (1A62:000C) matched; tests: CardSmokeTests
//   option 0: action R124+0x2578; hint R124+0x25A2
//   option 1: action R124+0x25DA; hint R124+0x262E
//   option 2: action R124+0x2666; hint R124+0x26DA
//   option 3: action R124+0x2712; hint R124+0x2822
//   option 4: action R124+0x285A; hint R124+0x2956
//   option 5: action R124+0x2988; hint R124+0x2A5C
//   option 6: action R124+0x2A94; hint R124+0x2AB2
//   option 7: action R124+0x2AEE; hint R124+0x2B0C
//   option 8: action R124+0x2B48; hint R124+0x2B66
//   option 9: action R124+0x2BA2; hint R124+0x2BC0
//   item pop-up: R124+0x2CD6, R124+0x2D12, R124+0x2D4E,
//     R124+0x2D8A
//   saint pop-up: R124+0x2DC6, R124+0x2E4E, R124+0x2ED6,
//     R124+0x2F5E
//   other: R124+0x24DE, R124+0x2BFC, R124+0x2C9E
// DORMANT (generated): option slots 10..19 keep the default handler R124+0x2C9E ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// DIVERGENCE (generated): party_panel_rows(0x100) (root 0000:1716, a party-panel redraw) is left out; the screen redraws itself.
public sealed class SabbatFeastKitchen : Card
{
    public override int Number => 323;

    private const int CooksPicture = 138;
    private const int MobPicture = 160;
    private const int MaidenPicture = 157;

    private const int FactTag = 1;
    private const int AlarmFact = 7;
    private const int TimeFact = 8;
    private const int FeastSpoiledFact = 9;

    private const int Field = 322;

    public override int Run(CardContext c)
    {
        Show(c, CooksPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints(0x22, 0x2F, 0x28, 0x3F);

        var assured = OptionHint.Saying(SuccessAssured);
        var showAgain = new MenuLine(_ => ShowTheCookAgain(c), assured);
        return menu.Run(
            // orig: R124+0x2578 / R124+0x25A2 matched — refuse and go back to the field.
            new MenuLine(_ => { c.Screen.Text(1); AddToFact(c, AlarmFact, 1); c.Next = Field; }, assured),
            new MenuLine(_ => ReadyToHelp(c, menu), assured),
            new MenuLine(_ => FleeInHorror(c), assured),
            new MenuLine(_ => BaptizeThem(c), assured),
            new MenuLine(chance => Preach(c, chance), () => PreachingChance(c)),
            new MenuLine(_ => CallASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            showAgain, showAgain, showAgain, showAgain);
    }

    // orig: the event_update(−1, −1, 0x1C, fact, n, 1, add) of the card's handlers matched
    private static void AddToFact(CardContext c, int fact, int amount) =>
        c.Events.Update(-1, -1, 0x1C, fact, amount, FactTag, EventUpdate.Add);

    // orig: the event_new(0, cur_loc, 0, 0x1C, 9, …, 72, 0, 1) of the card's handlers matched
    private static void SpoilTheFeast(CardContext c) =>
        c.Events.Create(0, c.Here, 0, 0x1C, FeastSpoiledFact, 0, 0, 72, 0, FactTag);

    /// <summary>Everybody gains a point of Virtue and 10 Divine Favor.</summary>
    // orig: the add_skill / add_attr pair of R124+0x2712 / _05ba matched
    private static void GoodDeed(CardContext c)
    {
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
    }

    /// <summary>The table with the infants; the baptism line is greyed out unless the best Religion beats a d100.</summary>
    // orig: R124+0x25DA matched
    // C: rand_mod(100) is the only draw in the comparison, so the operands' order does not change the random stream.
    private static void ReadyToHelp(CardContext c, CardMenu menu)
    {
        menu.Offer(2, 2, 3, 4);
        menu[5] = c.SaintsUsable();
        if (c.PartyMaxSkill((int)Skill.Religion) < c.Random(100))
        {
            menu[3] = OptionState.Shown;
        }
    }

    // orig: R124+0x2666 matched
    private static void FleeInHorror(CardContext c)
    {
        AddToFact(c, AlarmFact, -2);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -10);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -40);
        c.AddAttributeMaximum(Who.Everybody, (int)Attribute.DivineFavor, -10);
        // The original redraws the party panel here.
        c.Screen.Text(5);
        AddToFact(c, TimeFact, 1);
        c.Next = Field;
    }

    // orig: R124+0x2712 matched
    private static void BaptizeThem(CardContext c)
    {
        AddToFact(c, AlarmFact, 2);
        var unnoticed = c.PartyMaxSkill((int)Skill.Religion) >= c.Random(100);
        c.Screen.Text(unnoticed ? 3 : 4);
        SpoilTheFeast(c);
        GoodDeed(c);
        var mode = unnoticed ? 1 : 0;
        c.PracticeSkill(Who.Everybody, (int)Skill.Religion, mode, 10, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Healing, mode, 10, 0);
        c.Screen.Text(5);
        c.Next = Field;
    }

    /// <summary>The best Charisma plus half the best Common (that member speaks).</summary>
    // orig: R124+0x2956 matched
    // C: checked against the asm: party_max_attr(5) is called before party_max_skill(10), so best_member (read as
    //   unsigned char) is the best Common speaker; the halving is a signed division (never negative here).
    private static OptionHint PreachingChance(CardContext c)
    {
        var charisma = c.PartyMaxAttribute((int)Attribute.Charisma);
        var chance = charisma + c.PartyMaxSkill((int)Skill.SpeakCommon) / 2;
        return OptionHint.ChanceOf(chance, c.BestMember);
    }

    // orig: R124+0x285A matched
    private static void Preach(CardContext c, int chance)
    {
        AddToFact(c, AlarmFact, 1);
        if (c.Random(100) <= chance)
        {
            c.Screen.Text(6);
            SpoilTheFeast(c);
            GoodDeed(c);
            c.PracticeSkill(Who.Everybody, (int)Skill.SpeakCommon, 1, 10, 0);
            c.Screen.Text(5);
            c.Next = Field;
            return;
        }
        ShowPicture(c, MobPicture);
        c.Screen.Text(7);
        GoodDeed(c);
        c.PracticeSkill(Who.Everybody, (int)Skill.SpeakCommon, 0, 10, 0);
        ShowPicture(c, CooksPicture);
        Fight(c);
    }

    /// <summary>
    /// Saints 0 and 1 send a maiden who takes the infants away, 2 makes the cooks smell the chrism,
    /// 3 lifts the infants into the air. A failed prayer only greys out the line (text 11 is never shown).
    /// </summary>
    // orig: R124+0x2988 matched
    // C: an int function: `return 0` after a cancelled prayer, `return;` without a value after a heard one, and
    //   options_block (2) after a failed one; ignored.
    private static void CallASaint(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            menu[5] = OptionState.Shown;
            menu.Block = 2;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
            case 1:
                ShowPicture(c, MaidenPicture);
                c.Screen.Text(8);
                c.Screen.Text(13);
                break;
            case 2:
                c.Screen.Text(9);
                break;
            case 3:
                c.Screen.Text(10);
                break;
        }
        SpoilTheFeast(c);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 3);
        c.Screen.Text(5);
        c.Next = Field;
    }

    /// <summary>The cooks attack. Won: the infants are baptized (alarm +2, card 322); lost: card 334; else stay.</summary>
    // orig: R124+0x24DE matched
    // C: rand_mod(3) is run_battle's only random argument; a result outside 0..4 leaves next_card unchanged.
    private static void Fight(CardContext c)
    {
        var cooks = c.Random(3) + 3;
        switch (c.RunBattle(0x49, -1, 0x29, 2, cooks, 0x0D, 2, 3, -1, 0, 0))
        {
            case 0:
            case 1:
                c.Screen.Text(12);
                SpoilTheFeast(c);
                AddToFact(c, AlarmFact, 2);
                c.Screen.Text(5);
                c.Next = Field;
                break;
            case 2:
            case 3:
            case 4:
                c.DropAway();
                c.Next = 334;
                break;
        }
    }

    // orig: R124+0x2A94 / _084e / _08a8 / _0902 matched
    private static void ShowTheCookAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
