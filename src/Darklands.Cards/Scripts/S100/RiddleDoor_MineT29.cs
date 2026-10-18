using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT29": "ooo + ooo = aaah": press H.</summary>
// orig: R100+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT29 : DwarfRiddleDoor
{
    public override int Number => 0xE0;

    // orig: sort R100+0x0190 (1838:0190); reset R100+0x0452 (1838:0452) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE6, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R100+0x01B2 (1838:01B2); hint R100+0x01FA matched
        Right(c, 2),
        // orig: R100+0x0234 (1838:0234); hint R100+0x0270 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R100+0x02AA (1838:02AA); hint R100+0x02E6 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R100+0x0320 (1838:0320); hint R100+0x035C matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R100+0x0396 (1838:0396); hint R100+0x03D2 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R100+0x040C (1838:040C); hint R100+0x041A matched
        TurnAway(c),
    ];
}
