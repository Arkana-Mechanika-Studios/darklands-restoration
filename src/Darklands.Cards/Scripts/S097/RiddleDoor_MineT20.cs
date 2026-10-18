using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT20": Three faces that each say at least one truth, and three keys.</summary>
// orig: R097+0x0D74 (190F:0004) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT20 : DwarfRiddleDoor
{
    public override int Number => 0xD7;

    // orig: sort R097+0x0EBC (190F:014C); reset R097+0x1094 (190F:0324) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R097+0x0EDE (190F:016E); hint R097+0x0F1A matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R097+0x0F54 (190F:01E4); hint R097+0x0F9C matched
        Right(c, 2),
        // orig: R097+0x0FD6 (190F:0266); hint R097+0x1012 matched
        Wrong(c, 1, Attribute.Strength),
        // orig: R097+0x104C (190F:02DC); hint R097+0x105A matched
        TurnAway(c, c.Leader),
    ];
}
