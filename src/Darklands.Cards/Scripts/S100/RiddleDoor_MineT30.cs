using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT30": Eight brass faces and four keys.</summary>
// orig: R100+0x04C2 (1884:0002) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT30 : DwarfRiddleDoor
{
    public override int Number => 0xE1;

    // orig: sort R100+0x062C (1884:016C); reset R100+0x087A (1884:03BA) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE6, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R100+0x064E (1884:018E); hint R100+0x0696 matched
        Right(c, 2),
        // orig: R100+0x06D0 (1884:0210); hint R100+0x070C matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R100+0x0746 (1884:0286); hint R100+0x0782 matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R100+0x07BC (1884:02FC); hint R100+0x07F8 matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R100+0x0832 (1884:0372); hint R100+0x0840 matched
        TurnAway(c, c.Leader),
    ];
}
