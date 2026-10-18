using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT08": Six numbered faces and four metal knobs. (The curse strikes Agility, though the text speaks of looks; the original's fight line is not in the text.)</summary>
// orig: R094+0x0A92 (18E1:0002) matched; tests: CardSmokeTests
// DORMANT: the original enables 10 lines and handles options [0, 1, 2, 3, 4, 5]; the text has only 5 option lines, so options [5] are never offered.
public sealed class RiddleDoor_MineT08 : DwarfRiddleDoor
{
    public override int Number => 0xCB;

    // orig: sort R094+0x0C46 (18E1:01B6); reset R094+0x0F08 (18E1:0478) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R094+0x0C68 (18E1:01D8); hint R094+0x0CA4 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x0CDE (18E1:024E); hint R094+0x0D26 matched
        Right(c, 2),
        // orig: R094+0x0D60 (18E1:02D0); hint R094+0x0D9C matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x0DD6 (18E1:0346); hint R094+0x0E12 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R094+0x0E4C (18E1:03BC); hint R094+0x0E88 matched
        Wrong(c, 1, Attribute.Agility),
    ];
}
