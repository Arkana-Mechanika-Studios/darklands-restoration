using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago14": the party climbs to a dragon's lair in a cliff and the dragon's head comes out
/// of the hole above them. Hold up the dragon's stolen gold cup (if the party has it), call on
/// a saint, or scramble down the slope as fast as possible and take the flames.
/// </summary>
// orig: R085+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R085+0x04E2 (1838:04E2) matched; R085+0x0544 (1838:0544) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
public sealed class DragonOnTheCliff : Card
{
    public const int CardNumber = 0xA4;

    private const int Picture = 110;
    private const int ChristinaPicture = 286;
    private const int GodehardPicture = 287;
    private const int GregoryPicture = 323;
    private const int ScorchedPicture = 189;

    /// <summary>The card after the cup has driven the dragon back into its lair.</summary>
    private const int InsideTheLair = 0x99;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        if (c.Events.FactAnywhere(4, 0x0A))
        {
            // The cup was shown to this dragon already.
            c.Next = InsideTheLair;
        }
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 1);
        // Item 0x95 is the dragon's gold cup.
        if (c.CountItems(Who.Everybody, 0x95) == 0)
        {
            menu[0] = OptionState.Shown;
        }
        c.SetCardItems();
        c.SetCardSaints(0x15, 0x3C, 0x3F);
        menu[1] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            // orig: R085+0x017E (1838:017E) matched; R085+0x01B0 (1838:01B0) matched; tests: CardSmokeTests
            new MenuLine(() => ShowTheCup(c), OptionHint.Saying(CardTexts.Unknown)),
            // orig: R085+0x01E8 (1838:01E8) matched; R085+0x0300 (1838:0300) matched; tests: CardSmokeTests
            // orig: R085+0x057C (1838:057C) matched; R085+0x0606 (1838:0606) matched; R085+0x0690 (1838:0690) matched; tests: CardSmokeTests
            new MenuLine(() => CallASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R085+0x0338 (1838:0338) matched; R085+0x04AA (1838:04AA) matched; tests: CardSmokeTests
            new MenuLine(() => ClimbDown(c), OptionHint.Saying(CardTexts.Unknown)));
    }

    private static void ShowTheCup(CardContext c)
    {
        c.Screen.Text(1);
        // The dragon is remembered as having been shown its cup (forever).
        c.Events.Create(-2, c.Here, 0, 0x1C, 0x0A, 0, 0, 26280, 0, 4);
        c.Next = InsideTheLair;
    }

    private static void CallASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            switch (c.MenuSlot)
            {
                case 0:
                    ShowPicture(c, ChristinaPicture);
                    c.Screen.Text(2);
                    break;
                case 1:
                    ShowPicture(c, GodehardPicture);
                    c.Screen.Text(3);
                    break;
                case 2:
                    ShowPicture(c, GregoryPicture);
                    c.Screen.Text(4);
                    break;
                default:
                    // DORMANT: slot 3 holds no saint, so the pop-up never offers it.
                    break;
            }
        }
        else
        {
            c.Injure(Who.Everybody, 2, 5, 0x14);
            ShowPicture(c, ScorchedPicture);
            c.Screen.Text(5);
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    /// <summary>Everyone tries to outrun the flames: an agility roll each, the slow ones are scorched.</summary>
    private static void ClimbDown(CardContext c)
    {
        var escaped = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) == CharacterStatus.Empty)
            {
                continue;
            }
            var agility = c.EffectiveAgility(i);
            if (c.Random(100) <= agility)
            {
                escaped++;
                c.Variables.Chosen[0] = i;
                // C: arguments are evaluated right to left: the endurance loss is drawn first.
                var endurance = c.Random(5) + 10;
                var strength = c.Random(5) + 10;
                c.Injure(i, strength, endurance, 1);
            }
            else
            {
                var endurance = c.Random(0x19) + 0x19;
                var strength = c.Random(0x19) + 0x19;
                c.Injure(i, strength, endurance, 5);
            }
        }
        if (c.PartySize == escaped)
        {
            ShowPicture(c, ChristinaPicture);
            c.Screen.Text(6);
        }
        else if (escaped == 0)
        {
            ShowPicture(c, ScorchedPicture);
            c.Screen.Text(7);
        }
        else if (escaped == 1)
        {
            ShowPicture(c, GodehardPicture);
            c.Screen.Text(8);
        }
        else
        {
            ShowPicture(c, GodehardPicture);
            c.Screen.Text(9);
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }
}
