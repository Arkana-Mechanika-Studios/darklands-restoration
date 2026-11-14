using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT24": "1 2 3 5 7 11 13 17 19 23": the next number.</summary>
// orig: R098+0x0B06 (18E8:0006) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT24 : DwarfRiddleDoor
{
    public override int Number => 0xDB;

    // orig: sort R098+0x0C96 (18E8:0196); reset R098+0x0F58 (18E8:0458) matched
    // BUG: this card and $MineT25 (0xDC, R099+0x0186) are two of the twelve riddles the mine builder can hang on the iron door of its section 8
    //   (0xDB..0xE2, 0x153..0x156, MineBuilder.Contents), the door of the mine's own king; but the sort-0 answer of these two is 0xE5, the
    //   meeting every ordinary dwarf door of the other sections leads to ($MineB02, R101+0x0622), while the other ten lead to 0xE6, the
    //   king's own card ($MineB03, R101+0x0850). In one draw of the door in six (and with the mine's sort 0) the party that answers
    //   right therefore meets the wrong card; what this costs the quest (the king's treasure is only offered back from 0xE6) is not proved here. Reproduced as the original.
    //   Decision: later, an optional fixed rule after 1.0.
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
