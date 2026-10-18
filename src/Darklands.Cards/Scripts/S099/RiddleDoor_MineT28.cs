using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT28": James, John, Matthew, Peter, Stephen, Thomas: the name that belongs not.</summary>
// orig: R099+0x0FDA (1935:000A) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT28 : DwarfRiddleDoor
{
    public override int Number => 0xDF;

    // orig: sort R099+0x118C (1935:01BC); reset R099+0x14C4 (1935:04F4) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE6, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R099+0x11AE (1935:01DE); hint R099+0x11EA matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R099+0x1224 (1935:0254); hint R099+0x1260 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R099+0x129A (1935:02CA); hint R099+0x12D6 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R099+0x1310 (1935:0340); hint R099+0x134C matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R099+0x1386 (1935:03B6); hint R099+0x13CE matched
        Right(c, 2),
        // orig: R099+0x1408 (1935:0438); hint R099+0x1444 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R099+0x147E (1935:04AE); hint R099+0x148C matched
        TurnAway(c),
    ];
}
