using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineT10": three statues, "recently shuffled", and the one that cannot be Hardgrep. The
/// silver and the copper statue each open the door for an hour after the other was touched in vain.
/// </summary>
// orig: R095+0x03F0 (1877:0000) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT10 : DwarfRiddleDoor
{
    public override int Number => 0xCD;

    // orig: sort R095+0x0538 (1877:0148); reset R095+0x0804 (1877:0414) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R095+0x055A (1877:016A); hint R095+0x05A2 matched
        Right(c, 2),
        // orig: R095+0x05DC (1877:01EC); hint R095+0x0692 matched
        Shifting(c, opensWith: 2, leaves: 3, rightText: 2, wrongText: 1, Attribute.Strength),
        // orig: R095+0x06CC (1877:02DC); hint R095+0x0782 matched
        Shifting(c, opensWith: 3, leaves: 2, rightText: 2, wrongText: 1, Attribute.Strength),
        // orig: R095+0x07BC (1877:03CC); hint R095+0x07CA matched
        TurnAway(c, c.Leader),
    ];
}
