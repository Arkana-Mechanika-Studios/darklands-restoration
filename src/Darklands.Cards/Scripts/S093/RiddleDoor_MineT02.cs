using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT02": "Brothers and sisters have I none": the painted dwarf's riddle.</summary>
// orig: R093+0x09B4 (18D3:0004) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT02 : DwarfRiddleDoor
{
    public override int Number => 0xC8;

    // orig: sort R093+0x0B1E (18D3:016E); reset R093+0x0D6A (18D3:03BA) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R093+0x0B40 (18D3:0190); hint R093+0x0B7C matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R093+0x0BB6 (18D3:0206); hint R093+0x0BF2 matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R093+0x0C2C (18D3:027C); hint R093+0x0C74 matched
        Right(c, 1),
        // orig: R093+0x0CAE (18D3:02FE); hint R093+0x0CEA matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R093+0x0D24 (18D3:0374); hint R093+0x0D32 matched
        TurnAway(c),
    ];
}
