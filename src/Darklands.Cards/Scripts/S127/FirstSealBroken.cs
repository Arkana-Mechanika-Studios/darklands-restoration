using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd36": the sabbat's defenders are vanquished. On the altar lies the tome with the first
/// of the seals; breaking it commits the party to destroying the whole witch cult. The party
/// gains Virtue, Divine Favour, fame and the nearest city's regard, learns the way to the
/// fortress monastery (fact 1 of tag 0x1C), and the site is abandoned from now on.
/// The card has no menu: it goes straight back to the map.
/// </summary>
// orig: R127+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   item pop-up: R127+0x04B6, R127+0x04F2, R127+0x052E,
//     R127+0x056A
//   saint pop-up: R127+0x05A6, R127+0x062E, R127+0x06B6,
//     R127+0x073E
//   other: R127+0x00D0, R127+0x00E2, R127+0x011E,
//     R127+0x0130, R127+0x016C, R127+0x017E,
//     R127+0x01BA, R127+0x01CC, R127+0x0208,
//     R127+0x021A, R127+0x0256, R127+0x0268,
//     R127+0x02A4, R127+0x02B6, R127+0x02F2,
//     R127+0x0304, R127+0x0340, R127+0x0352,
//     R127+0x038E, R127+0x03A0, R127+0x03DC,
//     R127+0x047E
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// C: card-text-block(k, 2) (frame mode 2) is drawn like mode 0, on the picture (card-text-block R043+0x0E40 and R043+0x0F54 compare the mode only with 0, 2 and 1).
// C (generated): DS:270F as an argument is the immediate 0x270F = 9999 (forever, EventLog.Forever), printed as a symbol.
public sealed class FirstSealBroken : Card
{
    public override int Number => 332;

    private const int Picture = 151;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, 3);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 99);
        c.GiveReward(3);
        // C: closest-city(party-map-column) passes the long party-map-column, i.e. the two words party-map-column and party-map-row.
        c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), 25, 50);
        c.Events.Create(Who.Everybody, c.Here, 0, 0x1C, 1, 0, 0, EventLog.Forever, 0, 1);
        // card-text-block(0, 2): frame mode 2 drawn like mode 0 (card-text-block R043+0x0E40 treats 0 and 2 alike).
        c.Screen.Text(0);
        SabbatAftermath.MarkTheSiteAbandoned(c);
        // The original's option handlers (straight to 0x62) are never offered: see DormantOptions.
        return SabbatAftermath.WorldMap;
    }

    /// <summary>The option table the card file also holds (ten lines: straight to 0x62; the usual pop-ups).</summary>
    // DORMANT: the card function never runs a menu (nor the table set-up), so none of these handlers is reached;
    //   they would run if a menu were offered with every line enabled.
    // orig (all matched):
    //   R127+0x00D0 R127+0x00E2 R127+0x011E R127+0x0130 R127+0x016C
    //   R127+0x017E R127+0x01BA R127+0x01CC R127+0x0208 R127+0x021A
    //   R127+0x0256 R127+0x0268 R127+0x02A4 R127+0x02B6 R127+0x02F2
    //   R127+0x0304 R127+0x0340 R127+0x0352 R127+0x038E R127+0x03A0
    //   R127+0x03DC R127+0x047E R127+0x04B6 R127+0x04F2 R127+0x052E
    //   R127+0x056A R127+0x05A6 R127+0x062E R127+0x06B6 R127+0x073E
    internal static MenuLine[] DormantOptions(CardContext c) => CardMenu.FirstTextAndEndTable(c, CardTexts.SuccessAssured, showFirstText: false);
}
