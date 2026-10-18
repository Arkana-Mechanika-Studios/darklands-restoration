using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT24": "1 2 3 5 7 11 13 17 19 23": the next number.</summary>
// orig: R098+0x0B06 (18E8:0006) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT24 : DwarfRiddleDoor
{
    public override int Number => 0xDB;

    // orig: sort R098+0x0C96 (18E8:0196); reset R098+0x0F58 (18E8:0458) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R098+0x0CB8 (18E8:01B8); hint R098+0x0CF4 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R098+0x0D2E (18E8:022E); hint R098+0x0D6A matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R098+0x0DA4 (18E8:02A4); hint R098+0x0DE0 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R098+0x0E1A (18E8:031A); hint R098+0x0E62 matched
        Right(c, 2),
        // orig: R098+0x0E9C (18E8:039C); hint R098+0x0ED8 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R098+0x0F12 (18E8:0412); hint R098+0x0F20 matched
        TurnAway(c),
    ];
}
