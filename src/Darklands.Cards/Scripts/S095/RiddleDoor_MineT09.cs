using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT09": Six brass faces and four keys.</summary>
// orig: R095+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT09 : DwarfRiddleDoor
{
    public override int Number => 0xCC;

    // orig: sort R095+0x016A (1838:016A); reset R095+0x0380 (1838:0380) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R095+0x018C (1838:018C); hint R095+0x01C8 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R095+0x0202 (1838:0202); hint R095+0x023E matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R095+0x0278 (1838:0278); hint R095+0x028A matched
        Right(c, 2, doorOpens: false, doorShownAgain: false),
        // orig: R095+0x02C4 (1838:02C4); hint R095+0x0300 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R095+0x033A (1838:033A); hint R095+0x0348 matched
        TurnAway(c),
    ];
}
