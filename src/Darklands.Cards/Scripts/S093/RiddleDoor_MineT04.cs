using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT04": Four knobs and four statements, only one wholly true.</summary>
// orig: R093+0x115A (194D:000A) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT04 : DwarfRiddleDoor
{
    public override int Number => 0x71;

    // orig: sort R093+0x12C2 (194D:0172); reset R093+0x1500 (194D:03B0) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R093+0x12E4 (194D:0194); hint R093+0x1320 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R093+0x135A (194D:020A); hint R093+0x1394 matched
        Right(c, 2, doorShownAgain: false),
        // orig: R093+0x13CE (194D:027E); hint R093+0x140A matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R093+0x1444 (194D:02F4); hint R093+0x1480 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R093+0x14BA (194D:036A); hint R093+0x14C8 matched
        TurnAway(c),
    ];
}
