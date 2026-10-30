using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd40": the man in the broad-brimmed cap is back with an angry crowd that attacks at once.
/// Three waves follow (with the flying ointment, fact 0x0D, the party may flee before each);
/// surviving them cleanses the area, but costs Virtue and Divine Favour unless the innocents were
/// saved (fact 9). Losing a wave sends the party to "$Witcd38", winning or fleeing to the map.
/// </summary>
// orig: R128+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R128+0x0482; hint R128+0x04A0
//   option 1: action R128+0x04DC; hint R128+0x04FA
//   option 2: action R128+0x0536; hint R128+0x0554
//   option 3: action R128+0x0590; hint R128+0x05AE
//   option 4: action R128+0x05EA; hint R128+0x0608
//   option 5: action R128+0x0644; hint R128+0x0662
//   option 6: action R128+0x069E; hint R128+0x06BC
//   option 7: action R128+0x06F8; hint R128+0x0716
//   option 8: action R128+0x0752; hint R128+0x0770
//   option 9: action R128+0x07AC; hint R128+0x07CA
//   item pop-up: R128+0x08E0, R128+0x091C, R128+0x0958,
//     R128+0x0994
//   saint pop-up: R128+0x09D0, R128+0x0A58, R128+0x0AE0,
//     R128+0x0B68
//   other: R128+0x034C, R128+0x03B8, R128+0x0806,
//     R128+0x08A8
// DORMANT (generated): option slots 10..19 keep the default handler R128+0x08A8 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 2, 3, 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card-item-lines stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card-saint-lines stays all -1, so no line opens a saint pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class StrangerBringsAMob : Card
{
    public override int Number => 336;

    private const int Picture = 156;

    /// <summary>The picture the card returns to after the ointment is refused (the original's image-buffer).</summary>
    private int _scene;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints();
        c.Screen.Text(0);
        _scene = SabbatAftermath.FightPicture;
        ShowPicture(c, SabbatAftermath.FightPicture);
        FightAWave(c, 1);
        if (!SabbatAftermath.Over(c))
        {
            FightAWave(c, 2);
        }
        if (!SabbatAftermath.Over(c))
        {
            FightAWave(c, 0);
        }
        if (!SabbatAftermath.Over(c))
        {
            CleanseTheArea(c);
        }

        // COVERAGE GAP (reachable: result 5 with a member left Away by an earlier flight; no oracle run ended so): a wave that returns 5 changes nothing and the next wave or the victory follows.
        // DORMANT: even so every way through the fight leaves the card (the victory always sets 0x0C, a lost wave 0x14E;
        //   results 6 and above cannot occur, negative ones never come from battle-driver), so this menu is never reached. Its ten handlers
        //   (R128+0x0482 .. R128+0x07AC, hints R128+0x04A0 .. R128+0x07CA, "Success is
        //   assured.") show text 0 and go to 0x62; lines 0 and 1 would be offered.
        // C: when the menu's choice comes from the pop-up, the C calls item-handler-table[0][menu-choice-index] (member 0's handler).
        // orig: R128+0x08E0 / _091c / _0958 / _0994 matched — item pop-up: "Success is assured.", $ChosenOneName = the member.
        return menu.Run([.. Enumerable.Range(0, 10).Select(k => new MenuLine(
            _ => CardMenu.ShowFirstTextAndEnd(c), OptionHint.Saying(SuccessAssured),
            () => c.MemberPopupHint(SuccessAssured)))]);
    }

    // orig: R128+0x034C matched
    private void FightAWave(CardContext c, int afterwards)
    {
        if (c.Events.FactAnywhere(1, 0x0D))
        {
            // orig: R128+0x03B8 matched
            // BUG: the offer's option lines are never switched on (R128+0x0806 cleared card-option-enabled[] and the menu
            //   loop that copies the flags has not run yet), so the box offers nothing to choose.
            // C: card-choice-menu (R043+0x1BD2) returns only for a click on an enabled line's hot spot or Enter / space on a highlighted one
            //   (R043+0x1C83: DS:E96F != 0xFF); with no line laid out neither can happen. Direct call j_menu.py (local/batch/evid1o): from the
            //   card state banks-markt, card-option-enabled cleared, card-option-lines then card-choice-menu took every key and 552 clicks over the text window and did
            //   not return (the highlight stayed 0xFF); with card-option-enabled[0] = 1 the same input returned.
            // DIVERGENCE: the original waits for ever there (only the menu bar and the portraits answer); the screen takes line 0 for an
            //   empty option list (CardScreen.Select), the escape line: the party flees with the ointment's cost.
            //   Decided by the project owner (2026-10-26): kept as a deliberate divergence, the game must not hang.
            SabbatAftermath.OfferTheOintment(c, 4, 5, 0, _scene, _ => OptionHint.Say(CardTexts.ScrewedUp));
        }
        if (c.Next == SabbatAftermath.WorldMap)
        {
            return;
        }
        switch (c.RunBattle(0x51, -1, 0x29, 3, 2, 0x0D, 3, 6, 0x0D, 2, 6))
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

    // orig: (part of R128+0x0000) matched — the victory after the three waves
    /// <summary>The crowd is beaten (text 3).</summary>
    private static void CleanseTheArea(CardContext c)
    {
        ShowPicture(c, SabbatAftermath.VictoryPicture);
        c.Screen.Text(3);
        c.PracticeSkill(Who.Everybody, (int)Skill.Healing, 1, 10, 0);
        c.PracticeSkill(Who.Everybody, (int)Skill.Religion, 1, 10, 0);
        if (c.Events.FactAnywhere(1, 9))
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, 1);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 10);
        }
        else
        {
            c.AddSkill(Who.Everybody, (int)Skill.Virtue, -2);
            c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -15);
        }
        SabbatAftermath.LeaveTheSite(c);
    }
}
