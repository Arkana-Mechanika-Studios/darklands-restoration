using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT06": "O T T F F S S ?": the eighth letter of the sequence.</summary>
// orig: R094+0x02E8 (1866:0008) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT06 : DwarfRiddleDoor
{
    public override int Number => 0xC9;

    // orig: sort R094+0x0478 (1866:0198); reset R094+0x072C (1866:044C) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R094+0x049A (1866:01BA); hint R094+0x04D6 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x0510 (1866:0230); hint R094+0x054A matched
        Right(c, 2, doorShownAgain: false),
        // orig: R094+0x0584 (1866:02A4); hint R094+0x05C0 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x05FA (1866:031A); hint R094+0x0636 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x0670 (1866:0390); hint R094+0x06AC matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x06E6 (1866:0406); hint R094+0x06F4 matched
        TurnAway(c),
    ];
}
