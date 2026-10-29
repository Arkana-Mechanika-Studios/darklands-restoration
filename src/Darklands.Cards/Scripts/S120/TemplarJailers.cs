using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd10": Templar knights enter the cell and demand the party's weapons. Submitting costs
/// most of the party's things before a stranger unlocks the door; the party may also fight, throw
/// a potion, or pray: some saints carry them out, others reveal the secret door in the east wall,
/// which then becomes a way out.
/// </summary>
// orig: R120+0x14EA (1986:000A) matched; tests: CardSmokeTests
//   option 0: action R120+0x1744; hint R120+0x176E
//   option 1: action R120+0x17A6; hint R120+0x17BE
//   option 2: action R120+0x17F6; hint R120+0x191E
//   option 3: action R120+0x1956; hint R120+0x19B4
//   option 4: action R120+0x19EC; hint R120+0x1A34
//   option 5: action R120+0x1A6C; hint R120+0x1A8A
//   option 6: action R120+0x1AC6; hint R120+0x1AE4
//   option 7: action R120+0x1B20; hint R120+0x1B3E
//   option 8: action R120+0x1B7A; hint R120+0x1B98
//   option 9: action R120+0x1BD4; hint R120+0x1BF2
//   item pop-up: R120+0x1D08, R120+0x1D44, R120+0x1D80,
//     R120+0x1DBC
//   saint pop-up: R120+0x1DF8, R120+0x1E80, R120+0x1F08,
//     R120+0x1F90
//   other: R120+0x1C2E, R120+0x1CD0
// DORMANT (generated): option slots 10..19 keep the default handler R120+0x1CD0 ("screwed up.") and are never shown.
// DORMANT (generated): option line(s) 5, 6, 7, 8, 9 are never enabled (every assignment sets their flag to 0).
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
public sealed class TemplarJailers : Card
{
    public override int Number => 306;

    private const int Picture = 167;
    private const int KnightPicture = 168;
    private const int SecretDoorPicture = 185;
    private const int EscapePicture = 186;

    private const int UnusedLineCard = 0x62;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3);
        c.SetCardItems(0x5F, 0x60, 0x61, 0x65);
        c.SetCardSaints(0x15, 0x4E, 0x6A, 0x6C);
        menu[3] = c.ItemsUsable();
        menu[2] = c.SaintsUsable();

        var unknown = OptionHint.Saying(CardTexts.Unknown);
        // DORMANT: in text block 0 the option line 4 is never offered (the first menu on this block never enables them); it can be offered in other blocks only.
        return menu.Run(
            new MenuLine(_ => Submit(c), unknown),
            new MenuLine(_ => { c.CombatFlags |= 8; Escape(c); }, unknown),
            new MenuLine(_ => Pray(c, menu), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupChanceHint),
            new MenuLine(_ => ThrowPotion(c), OptionHint.Saying(CardTexts.SelectPotion), () => c.MemberPopupHint(CardTexts.Unknown)),
            // orig: R120+0x19EC matched — through the secret door.
            new MenuLine(_ => { ShowPicture(c, SecretDoorPicture); c.Screen.Text(10); Escape(c); }, unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown),
            new MenuLine(_ => RepeatOpening(c), unknown));
    }

    /// <summary>Out of the cell and back into the fortress (battle mode 3, enemy 8).</summary>
    // orig: the common tail of R120+0x1744 / _02c6 / _0316 / _0476 / _050c matched
    private static void Escape(CardContext c)
    {
        c.BattleMode = 3;
        c.BattleEnemy = 8;
        c.Next = -2;
    }

    // orig: R120+0x1744 matched
    private static void Submit(CardContext c)
    {
        c.LoseItems(Who.Everybody);
        c.Screen.Text(3);
        Escape(c);
    }

    // orig: R120+0x17F6 matched
    // C: an int function that falls off its end without a value (except `return 0` after a cancelled prayer); the
    //   menu ignores it. card_load_near(card_name, pic_buf) in case 1 reloads the card's own picture.
    private static void Pray(CardContext c, CardMenu menu)
    {
        var result = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (result == -1)
        {
            return;
        }
        if (result == 0)
        {
            c.Screen.Text(8);
            menu.Block = 1;
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                ShowPicture(c, EscapePicture);
                c.Screen.Text(4);
                Escape(c);
                break;
            case 1:
                ShowPicture(c, KnightPicture);
                c.Screen.Text(5);
                ShowPicture(c, Picture);
                menu.Block = 1;
                menu[4] = OptionState.Enabled;
                break;
            case 2:
                c.Screen.Text(6);
                menu.Block = 1;
                menu[4] = OptionState.Enabled;
                break;
            case 3:
                ShowPicture(c, EscapePicture);
                c.Screen.Text(7);
                Escape(c);
                break;
        }
    }

    // orig: R120+0x1956 matched
    private static void ThrowPotion(CardContext c)
    {
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        ShowPicture(c, EscapePicture);
        c.Screen.Text(9);
        Escape(c);
    }

    /// <summary>The handler of the option lines the text does not have: the opening text again, then card 0x62.</summary>
    // orig: R120+0x1A6C / _05e6 / _0640 / _069a / _06f4 matched
    private static void RepeatOpening(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = UnusedLineCard;
    }
}
