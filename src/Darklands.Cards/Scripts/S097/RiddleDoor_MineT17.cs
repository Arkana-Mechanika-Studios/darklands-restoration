using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineT17": three metal heads, one of them Krusad's. The gold and the silver head each open
/// the door for an hour after the other was pressed in vain; the way through takes an hour.
/// </summary>
// orig: R097+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT17 : DwarfRiddleDoor
{
    public override int Number => 0xD4;

    // orig: sort R097+0x0148 (1838:0148); reset R097+0x041C (1838:041C) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override int HoursThrough => 1;

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R097+0x0174 (1838:0174); hint R097+0x022A matched
        Shifting(c, opensWith: 3, leaves: 4, rightText: 2, wrongText: 1, Attribute.Endurance),
        // orig: R097+0x0264 (1838:0264); hint R097+0x031A matched
        Shifting(c, opensWith: 4, leaves: 3, rightText: 2, wrongText: 1, Attribute.Endurance),
        // orig: R097+0x0354 (1838:0354); hint R097+0x039C matched
        Right(c, 2),
        // orig: R097+0x03D6 (1838:03D6); hint R097+0x03E4 matched
        TurnAway(c),
    ];
}
