using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf13": the party hands the patron the object of his commission (the quest event kept in
/// the saved card slot). He takes it, delights in it in a way that fits its kind, and the quest
/// event ends. No menu: the party goes on to the card kept in the second saved slot.
/// </summary>
// orig: R128+0x16A2 (19A2:0002) matched; tests: CardSmokeTests
//   item pop-up: R128+0x1B10, R128+0x1B4C, R128+0x1B88,
//     R128+0x1BC4
//   saint pop-up: R128+0x1C00, R128+0x1C8A, R128+0x1D14,
//     R128+0x1D9E
//   other: R128+0x17B6, R128+0x17C8, R128+0x17F6,
//     R128+0x1808, R128+0x1836, R128+0x1848,
//     R128+0x1876, R128+0x1888, R128+0x18B6,
//     R128+0x18C8, R128+0x18F6, R128+0x1908,
//     R128+0x1936, R128+0x1948, R128+0x1976,
//     R128+0x1988, R128+0x19B6, R128+0x19C8,
//     R128+0x19F6, R128+0x1A08, R128+0x1A36,
//     R128+0x1AD8
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card-text-block(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card-text-block R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
// C (generated): some hints return r uninitialised (stale stack): read as their message for the whole party.
public sealed class CollectorReceivesTheItem : Card
{
    public override int Number => 338;

    private const int Picture = 356;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.SetCardItems();
        c.SetCardSaints();
        var quest = c.State.SavedCard;
        c.Variables.Item = c.EventDetail(quest);
        c.TakeItem(Who.Everybody, c.Variables.Item);
        // C: the event record is read through the raw pointer (+0x2E item, +0x1E value).
        // DIVERGENCE: an empty slot (the card run without its quest) has a null pointer: the original reads the interrupt vectors at
        // 0000:002E / 0000:001E (whatever the machine holds there); 0 here (card oracle: such runs are marked "null pointer").
        // C: card-text-block(k, 2): frame mode 2 drawn like mode 0 (card-text-block R043+0x0E40 treats 0 and 2 alike).
        // C: the C's switch lists cases 0 and 1 and a default that all show text 0.
        c.Screen.Text(c.EventValue(quest) switch
        {
            2 => 1,
            3 => 2,
            4 => 3,
            5 => 4,
            6 or 9 or 10 or 11 => 5,
            7 => 6,
            8 => 7,
            _ => 0,
        });
        c.Events.Free(quest);
        return c.State.SavedCard2;
    }

    /// <summary>The option table the card file also holds (ten lines: straight to 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R128+0x17B6 R128+0x17C8 R128+0x17F6 R128+0x1808 R128+0x1836
    //   R128+0x1848 R128+0x1876 R128+0x1888 R128+0x18B6 R128+0x18C8
    //   R128+0x18F6 R128+0x1908 R128+0x1936 R128+0x1948 R128+0x1976
    //   R128+0x1988 R128+0x19B6 R128+0x19C8 R128+0x19F6 R128+0x1A08
    //   R128+0x1A36 R128+0x1AD8 R128+0x1B10 R128+0x1B4C R128+0x1B88
    //   R128+0x1BC4 R128+0x1C00 R128+0x1C8A R128+0x1D14 R128+0x1D9E
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c, CardTexts.SuccessAssured, showFirstText: false);
}
