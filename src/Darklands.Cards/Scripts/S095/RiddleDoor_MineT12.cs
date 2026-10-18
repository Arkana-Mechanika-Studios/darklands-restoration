using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT12": Four faces, one of them Gymer's.</summary>
// orig: R095+0x0C02 (18F8:0002) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT12 : DwarfRiddleDoor
{
    public override int Number => 0xCF;

    // orig: sort R095+0x0D6C (18F8:016C); reset R095+0x0FB8 (18F8:03B8) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R095+0x0D8E (18F8:018E); hint R095+0x0DCA matched
        Wrong(c, 2, Attribute.Endurance),
        // orig: R095+0x0E04 (18F8:0204); hint R095+0x0E40 matched
        Wrong(c, 2, Attribute.Endurance),
        // orig: R095+0x0E7A (18F8:027A); hint R095+0x0EB6 matched
        Wrong(c, 2, Attribute.Endurance),
        // orig: R095+0x0EF0 (18F8:02F0); hint R095+0x0F38 matched
        Right(c, 1),
        // orig: R095+0x0F72 (18F8:0372); hint R095+0x0F80 matched
        TurnAway(c),
    ];
}
