using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT03": Three metal knobs and six statements, each metal's pair both true or both false.</summary>
// orig: R093+0x0DDA (1915:000A) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT03 : DwarfRiddleDoor
{
    public override int Number => 0x5C;

    // orig: sort R093+0x0F22 (1915:0152); reset R093+0x10EA (1915:031A) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R093+0x0F44 (1915:0174); hint R093+0x0F80 matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R093+0x0FBA (1915:01EA); hint R093+0x0FF4 matched
        Right(c, 2, doorShownAgain: false),
        // orig: R093+0x102E (1915:025E); hint R093+0x106A matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R093+0x10A4 (1915:02D4); hint R093+0x10B2 matched
        TurnAway(c),
    ];
}
