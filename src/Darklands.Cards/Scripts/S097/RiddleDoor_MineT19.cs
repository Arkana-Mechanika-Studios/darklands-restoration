using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT19": Six brass faces that all lie or all speak true, and three keys.</summary>
// orig: R097+0x09E6 (18D6:0006) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT19 : DwarfRiddleDoor
{
    public override int Number => 0xD6;

    // orig: sort R097+0x0B2E (18D6:014E); reset R097+0x0D04 (18D6:0324) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R097+0x0B50 (18D6:0170); hint R097+0x0B8C matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R097+0x0BC6 (18D6:01E6); hint R097+0x0C0E matched
        Right(c, 2),
        // orig: R097+0x0C48 (18D6:0268); hint R097+0x0C84 matched
        Wrong(c, 1, Attribute.Intelligence),
        // orig: R097+0x0CBE (18D6:02DE); hint R097+0x0CCC matched
        TurnAway(c),
    ];
}
