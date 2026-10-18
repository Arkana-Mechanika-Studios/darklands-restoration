using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT21": Four true statements about four keys.</summary>
// orig: R098+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT21 : DwarfRiddleDoor
{
    public override int Number => 0xD8;

    // orig: sort R098+0x016A (1838:016A); reset R098+0x0380 (1838:0380) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R098+0x018C (1838:018C); hint R098+0x01C8 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R098+0x0202 (1838:0202); hint R098+0x023E matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R098+0x0278 (1838:0278); hint R098+0x02B4 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R098+0x02EE (1838:02EE); hint R098+0x0300 matched
        Right(c, 2, doorOpens: false, doorShownAgain: false),
        // orig: R098+0x033A (1838:033A); hint R098+0x0348 matched
        TurnAway(c),
    ];
}
