using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd07": an animated skeleton screams for the bone missing from its ribcage. The party may
/// destroy it, talk to it (it asks for its bone again), leave, or give it a bone: then it tells of
/// the demon lord in the high tower and his weakness for Fleadust and fire magic.
/// </summary>
// orig: R120+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R120+0x022E; hint R120+0x0248
//   option 1: action R120+0x0280; hint R120+0x028E
//   option 2: action R120+0x02C6; hint R120+0x02D4
//   option 3: action R120+0x030C; hint R120+0x0332
//   option 4: action R120+0x036A; hint R120+0x0388
//   option 5: action R120+0x03C4; hint R120+0x03E2
//   option 6: action R120+0x041E; hint R120+0x043C
//   option 7: action R120+0x0478; hint R120+0x0496
//   option 8: action R120+0x04D2; hint R120+0x04F0
//   option 9: action R120+0x052C; hint R120+0x054A
//   item pop-up: R120+0x0660, R120+0x069C, R120+0x06D8,
//     R120+0x0714
//   saint pop-up: R120+0x0750, R120+0x07D8, R120+0x0860,
//     R120+0x08E8
//   other: R120+0x0586, R120+0x0628
// DORMANT (generated): option slots 10..19 keep the default handler R120+0x0628 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 4, 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class BonelessSkeleton : Card
{
    public override int Number => 304;

    private const int Picture = 174;

    /// <summary>The bone item the skeleton wants.</summary>
    private const int Bone = 0x94;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);
        menu[3] = c.CountItems(Who.Everybody, Bone) != 0 ? OptionState.Enabled : OptionState.Shown;
        c.SetCardItems();
        c.SetCardSaints();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        return menu.Run(
            new MenuLine(_ => { c.Screen.Text(2); Leave(c, -3); }, unknown),
            new MenuLine(_ => c.Screen.Text(3), unknown),
            new MenuLine(_ => Leave(c, -2), unknown),
            new MenuLine(_ => GiveItsBone(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    // orig: R120+0x022E / R120+0x02C6 matched (the shared tail)
    private static void Leave(CardContext c, int next)
    {
        c.BattleMode = 1;
        c.Next = next;
    }

    // orig: R120+0x030C (1838:030C) matched
    private static void GiveItsBone(CardContext c)
    {
        c.TakeItem(Who.Everybody, Bone);
        c.Screen.Text(1);
        Leave(c, -2);
    }

    // orig: R120+0x036A / _03c4 / _041e / _0478 / _04d2 / _052c matched (lines 4..9, never enabled)
    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
