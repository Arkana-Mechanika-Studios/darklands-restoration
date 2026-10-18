using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT16": "O, he saw four stars in heaven singing alleluia": the word that fits not.</summary>
// orig: R096+0x0BA8 (18F2:0008) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT16 : DwarfRiddleDoor
{
    public override int Number => 0xD3;

    // orig: sort R096+0x0DBE (18F2:021E); reset R096+0x1258 (18F2:06B8) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R096+0x0DE0 (18F2:0240); hint R096+0x0E1C matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x0E56 (18F2:02B6); hint R096+0x0E92 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x0ECC (18F2:032C); hint R096+0x0F08 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x0F42 (18F2:03A2); hint R096+0x0F7E matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x0FB8 (18F2:0418); hint R096+0x0FF4 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x102E (18F2:048E); hint R096+0x1076 matched
        Right(c, 2),
        // orig: R096+0x10B0 (18F2:0510); hint R096+0x10EC matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x1126 (18F2:0586); hint R096+0x1162 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x119C (18F2:05FC); hint R096+0x11D8 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R096+0x1212 (18F2:0672); hint R096+0x1220 matched
        TurnAway(c),
    ];
}
