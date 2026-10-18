using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT27": "27 64 125 216 343": the next number.</summary>
// orig: R099+0x0B0E (18E8:000E) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT27 : DwarfRiddleDoor
{
    public override int Number => 0xDE;

    // orig: sort R099+0x0C9E (18E8:019E); reset R099+0x0F6A (18E8:046A) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE6, 0xE7, 0xEB);

    protected override int HoursThrough => 1;

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R099+0x0CCA (18E8:01CA); hint R099+0x0D12 matched
        Right(c, 2),
        // orig: R099+0x0D4C (18E8:024C); hint R099+0x0D88 matched
        Wrong(c, 1, Attribute.Endurance),
        // orig: R099+0x0DC2 (18E8:02C2); hint R099+0x0DFE matched
        Wrong(c, 1, Attribute.Endurance),
        // orig: R099+0x0E38 (18E8:0338); hint R099+0x0E74 matched
        Wrong(c, 1, Attribute.Endurance),
        // orig: R099+0x0EAE (18E8:03AE); hint R099+0x0EEA matched
        Wrong(c, 1, Attribute.Endurance),
        // orig: R099+0x0F24 (18E8:0424); hint R099+0x0F32 matched
        TurnAway(c),
    ];
}
