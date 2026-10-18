using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT07": Four metal heads, one of them Hootvin, and two knobs.</summary>
// orig: R094+0x079C (18B1:000C) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT07 : DwarfRiddleDoor
{
    public override int Number => 0xCA;

    // orig: sort R094+0x08C2 (18B1:0132); reset R094+0x0A22 (18B1:0292) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R094+0x08E4 (18B1:0154); hint R094+0x0920 matched
        Wrong(c, 1, Attribute.Endurance),
        // orig: R094+0x095A (18B1:01CA); hint R094+0x09A2 matched
        Right(c, 2),
        // orig: R094+0x09DC (18B1:024C); hint R094+0x09EA matched
        TurnAway(c),
    ];
}
