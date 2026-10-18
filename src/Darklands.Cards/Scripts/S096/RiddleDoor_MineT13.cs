using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT13": Narm, Nenn and Krocht, sons of Hod, Elt and Mot. (Turning away brings the curse too, as in the original.)</summary>
// orig: R096+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: R096+0x02CE (1838:02CE) is in the file but no option line calls it.
public sealed class RiddleDoor_MineT13 : DwarfRiddleDoor
{
    public override int Number => 0xD0;

    // orig: sort R096+0x013E (1838:013E); reset R096+0x0314 (1838:0314) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R096+0x0160 (1838:0160); hint R096+0x01A8 matched
        Right(c, 1),
        // orig: R096+0x01E2 (1838:01E2); hint R096+0x021E matched
        Wrong(c, 2, Attribute.Charisma),
        // orig: R096+0x0258 (1838:0258); hint R096+0x0294 matched
        Wrong(c, 2, Attribute.Charisma),
        // orig: R096+0x0258 (1838:0258); hint R096+0x02DC matched
        Wrong(c, 2, Attribute.Charisma, () => OptionHint.Say(SuccessAssured)),
    ];
}
