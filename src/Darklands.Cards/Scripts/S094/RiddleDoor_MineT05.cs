using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT05": The gold and the silver statue: one always lies.</summary>
// orig: R094+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT05 : DwarfRiddleDoor
{
    public override int Number => 0xC5;

    // orig: sort R094+0x0126 (1838:0126); reset R094+0x0278 (1838:0278) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R094+0x0148 (1838:0148); hint R094+0x0182 matched
        Right(c, 1, doorShownAgain: false),
        // orig: R094+0x01BC (1838:01BC); hint R094+0x01F8 matched
        Wrong(c, 2, Attribute.Endurance),
        // orig: R094+0x0232 (1838:0232); hint R094+0x0240 matched
        TurnAway(c),
    ];
}
