using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT14": Four masks in a square: is Krott a dwarf, a kobold or a gnome?</summary>
// orig: R096+0x0384 (1870:0004) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT14 : DwarfRiddleDoor
{
    public override int Number => 0xD1;

    // orig: sort R096+0x04CC (1870:014C); reset R096+0x06A2 (1870:0322) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R096+0x04EE (1870:016E); hint R096+0x052A matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R096+0x0564 (1870:01E4); hint R096+0x05A0 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R096+0x05DA (1870:025A); hint R096+0x0622 matched
        Right(c, 2),
        // orig: R096+0x065C (1870:02DC); hint R096+0x066A matched
        TurnAway(c),
    ];
}
