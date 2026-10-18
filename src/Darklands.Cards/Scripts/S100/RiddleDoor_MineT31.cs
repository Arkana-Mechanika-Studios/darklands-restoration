using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT31": Gold and silver faces: "exactly one of us speaks the truth".</summary>
// orig: R100+0x08EA (18C6:000A) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT31 : DwarfRiddleDoor
{
    public override int Number => 0xE2;

    // orig: sort R100+0x0A10 (18C6:0130); reset R100+0x0B70 (18C6:0290) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE6, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R100+0x0A32 (18C6:0152); hint R100+0x0A7A matched
        Right(c, 2),
        // orig: R100+0x0AB4 (18C6:01D4); hint R100+0x0AF0 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R100+0x0B2A (18C6:024A); hint R100+0x0B38 matched
        TurnAway(c),
    ];
}
