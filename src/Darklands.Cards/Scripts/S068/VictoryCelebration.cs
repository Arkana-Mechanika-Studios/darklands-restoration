using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol12": the uprising has won. The crowd celebrates, the new city lord pays the party a
/// reward ($Money1) and offers a boon from his stores: horses, a fine weapon, armour, or the
/// selection of supplies (items 0x48..0x50).
/// </summary>
// orig: R068+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R068+0x0924 (only the option handlers; unused-line handler 0946)
public sealed class VictoryCelebration : Card
{
    private const int OnTheStreets = 0x12;
    private const int AtTheInnByNight = 0x19;

    public override int Number => 0x60;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        // C: card-option-flag-6..9 are not set by this card (stale from the previous one); their actions are
        // empty. They are offered hidden here (DORMANT lines below).
        var menu = Menu(c, 3);
        menu.Enable(0, 1, 2, 3, 4, 5);
        // The new lord: a woman in one city of five.
        // C: !(x % 5) with a signed x: negative multiples of 5 count too.
        c.RandomName(c.City.Ordinal + 0x0C, c.LocationQuery(c.Here, 0x21) % 5 == 0 ? 1 : 0, 0);
        c.Variables.Money[0] = Money.From(c.HereQuery(2) + c.Random(5) + 5, 0, 0);
        c.Screen.Text(0, TextFrame.BlankPage);
        c.ConflictOutcome(-1, -1, -1);
        c.PassHours(1);
        // The conditional passages of the texts (DS:9C30): the first one when the city has a fortress.
        Array.Clear(c.Variables.Conditions);
        if (c.HereQuery(0x18) != 0)
        {
            c.Variables.Conditions[0] = true;
        }
        c.Screen.Text(c.HereQuery(0x29) != 2 ? 1 : 0, TextFrame.BlankPage);
        c.State.Party.Purse = c.State.Party.Purse.Add(c.Variables.Money[0]);
        c.Screen.Text(2);

        return menu.Run(
            new MenuLine(_ => TakeHorses(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TakeAFineWeapon(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TakeArmourForAll(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TakeAKnightsArmour(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TakeAWeapon(c), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => TakePotions(c), OptionHint.Saying(SuccessAssured)),
            // DORMANT: lines 6..9 (stale flags, empty actions 0914/0918/091c/0920, empty hints 0916/091a/091e/0922).
            // C: their hint handlers are empty void functions; their struct res is a register leftover.
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(_ => { }, OptionHint.Saying(CardTexts.ScrewedUp)));
    }

    private static void Thanks(CardContext c, int text)
    {
        c.Screen.Text(text);
        c.PassHours(3);
        c.Next = c.IsDaylight ? OnTheStreets : AtTheInnByNight;
    }

    // C: the item records are built by hand: the definition's type and weight bytes, the quality
    // as a char (the low byte), count 1, then give-item-record-form; AddItemWithQuality builds the same record.

    /// <summary>A horse (0x45) for every member.</summary>
    // orig: R068+0x02EC (hint 038c)
    private static void TakeHorses(CardContext c)
    {
        var quality = c.HereQuery(2) + c.Random(6) + 0x16;
        for (var i = 0; i < c.PartySize; i++)
        {
            c.AddItemWithQuality(i, 0x45, quality);
        }
        Thanks(c, 5);
    }

    /// <summary>One fine weapon (0x44) for the leader.</summary>
    // orig: R068+0x03C4 (hint 0452)
    private static void TakeAFineWeapon(CardContext c)
    {
        var quality = c.Random(10) + (c.HereQuery(2) + 0x11) * 2;
        c.AddItemWithQuality(c.Leader, 0x44, quality);
        Thanks(c, 4);
    }

    /// <summary>Armour pieces 0x2B and 0x33 for every member.</summary>
    // orig: R068+0x048A (hint 0556)
    private static void TakeArmourForAll(CardContext c)
    {
        var quality = c.HereQuery(2) + 0x17;
        for (var i = 0; i < c.PartySize; i++)
        {
            c.AddItemWithQuality(i, 0x2B, quality);
            c.AddItemWithQuality(i, 0x33, quality);
        }
        Thanks(c, 5);
    }

    /// <summary>Armour pieces 0x2C and 0x35 of high quality for the leader (each quality drawn).</summary>
    // orig: R068+0x058E (hint 0680)
    private static void TakeAKnightsArmour(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.AddItemWithQuality(c.Leader, 0x2C, c.HereQuery(2) + c.Random(5) + 0x22);
        c.AddItemWithQuality(c.Leader, 0x35, c.HereQuery(2) + c.Random(5) + 0x22);
        Thanks(c, 4);
    }

    /// <summary>A weapon (0x20) for the leader.</summary>
    // orig: R068+0x06B8 (hint 0746)
    private static void TakeAWeapon(CardContext c)
    {
        var quality = c.HereQuery(2) + c.Random(4) + 0x16;
        c.Variables.Chosen[0] = c.Leader;
        c.AddItemWithQuality(c.Leader, 0x20, quality);
        Thanks(c, 4);
    }

    /// <summary>0..3 of each of the items 0x48..0x50 for the leader.</summary>
    // orig: R068+0x077E (hint 08dc)
    private static void TakePotions(CardContext c)
    {
        for (var code = 0x48; code <= 0x50; code++)
        {
            for (var n = c.Random(4); n > 0; n--)
            {
                c.AddItem(c.Leader, code);
            }
        }
        Thanks(c, 4);
    }
}
