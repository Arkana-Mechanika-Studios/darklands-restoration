using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd25": alone on the path to the sabbat field, the party finds three wolves guarding the
/// ring of watchfires. They may sneak past (the party's average Stealth), attack, pray, or, wearing
/// the pilgrims' cloaks taken earlier, bluff their way through, until a hidden watcher asks after
/// Klaus. Getting in leads to the field (card 322); a lost fight to card 334.
/// </summary>
// orig: R124+0x0BDE (18F5:000E) matched; tests: CardSmokeTests
//   option 0: action R124+0x0EA0; hint R124+0x0F1E
//   option 1: action R124+0x0F34; hint R124+0x0F46
//   option 2: action R124+0x0F7E; hint R124+0x100A
//   option 3: action R124+0x1042; hint R124+0x106C
//   option 4: action R124+0x10A4; hint R124+0x10B6
//   option 5: action R124+0x10EE; hint R124+0x1100
//   option 6: action R124+0x1138; hint R124+0x114A
//   option 7: action R124+0x1182; hint R124+0x1196
//   option 8: action R124+0x11CE; hint R124+0x11E0
//   option 9: action R124+0x1218; hint R124+0x1236
//   item pop-up: R124+0x134C, R124+0x1388, R124+0x13C4,
//     R124+0x1400
//   saint pop-up: R124+0x143C, R124+0x14C4, R124+0x154C,
//     R124+0x15D4
//   other: R124+0x0E30, R124+0x1272, R124+0x1314
// DORMANT (generated): option slots 10..19 keep the default handler R124+0x1314 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class WolvesAtTheWatchfireChain : Card
{
    public override int Number => 321;

    private const int WolvesPicture = 154;
    private const int FieldPicture = 144;
    private const int FlightPicture = 149;

    /// <summary>The pilgrims' cloaks were taken (location fact tag 1, type 6).</summary>
    private const int CloaksFact = 6;

    public override int Run(CardContext c)
    {
        Show(c, WolvesPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        if (c.Events.FactAnywhere(1, CloaksFact))
        {
            menu[3] = OptionState.Enabled;
        }
        c.SetCardItems();
        c.SetCardSaints(3, 0x3D, 0x6B, 0x79);
        menu[2] = c.SaintsUsable();

        var assured = OptionHint.Saying(SuccessAssured);
        // orig: R124+0x10A4 / _051e / _0568 / _05fe matched (identical) — the bluff fails: text 7, the fight.
        var unmasked = new MenuLine(_ => { c.Screen.Text(7); Fight(c); }, assured);
        return menu.Run(
            new MenuLine(chance => SneakPast(c, chance), () => SneakChance(c)),
            // orig: R124+0x0F34 / R124+0x0F46 matched — attack the wolves.
            new MenuLine(_ => { c.Screen.Text(5); Fight(c); }, OptionHint.Saying(CardTexts.Unknown)),
            new MenuLine(_ => CallASaint(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            // orig: R124+0x1042 / R124+0x106C matched — wear the cloaks.
            new MenuLine(_ => menu.Offer(6, 4, 5, 6, 7, 8), assured),
            unmasked,
            unmasked,
            unmasked,
            // orig: R124+0x1182 / R124+0x1196 matched — "Klaus is ill".
            new MenuLine(_ => { c.Screen.Text(8); c.Next = 322; }, assured),
            unmasked,
            new MenuLine(_ => ShowTheFiresAgain(c), assured));
    }

    // orig: R124+0x0F1E matched
    private static OptionHint SneakChance(CardContext c) =>
        OptionHint.ChanceOf(c.PartyAverageSkill((int)Skill.Stealth), OptionHint.WholeParty);

    // orig: R124+0x0EA0 matched
    private static void SneakPast(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 1, 10, 0);
            ShowPicture(c, FieldPicture);
            c.Screen.Text(1);
            c.Next = 322;
            return;
        }
        c.PracticeSkill(Who.Everybody, (int)Skill.Stealth, 0, 10, 0);
        c.Screen.Text(2);
        Fight(c);
    }

    /// <summary>Saints 0, 2 and 3 frighten the wolves off; saint 1 lifts the party over the fires.</summary>
    // orig: R124+0x0F7E matched
    // C: an int function: `return 0` after a cancelled prayer, `return;` without a value after a heard one, and it
    //   falls off its end after a failed one; ignored. A heard prayer from a slot outside 0..3 still goes to 322.
    private static void CallASaint(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(10);
            menu[2] = OptionState.Shown;
            return;
        }
        if (c.MenuSlot == 1)
        {
            ShowPicture(c, FlightPicture);
            c.Screen.Text(9);
        }
        else if (c.MenuSlot is 0 or 2 or 3)
        {
            c.Screen.Text(4);
        }
        c.Next = 322;
    }

    /// <summary>The wolves attack. Won: into the crowd (alarm +3, card 322); lost: card 334; else stay.</summary>
    // orig: R124+0x0E30 matched
    // C: rand_mod(2) is run_battle's only random argument; a result outside 0..4 leaves next_card unchanged.
    private static void Fight(CardContext c)
    {
        var wolves = c.Random(2) + 4;
        switch (c.RunBattle(0x48, -1, 0x2A, 3, wolves, -1, 0, 0, -1, 0, 0))
        {
            case 0:
            case 1:
                c.Screen.Text(3);
                c.Events.Update(-1, -1, 0x1C, 7, 3, 1, EventUpdate.Add);
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

    // orig: R124+0x1218 matched
    private static void ShowTheFiresAgain(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
