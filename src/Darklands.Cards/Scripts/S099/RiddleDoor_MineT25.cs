using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT25": "1 1 2 3 5 8 …": the next number. (The fifth plate and turning away both bring the curse, as in the original.)</summary>
// orig: R099+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: R099+0x03F4 (1838:03F4) is in the file but no option line calls it.
public sealed class RiddleDoor_MineT25 : DwarfRiddleDoor
{
    public override int Number => 0xDC;

    // orig: sort R099+0x0186 (1838:0186); reset R099+0x043A (1838:043A) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R099+0x01A8 (1838:01A8); hint R099+0x01E4 matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R099+0x021E (1838:021E); hint R099+0x0258 matched
        Right(c, 1, doorShownAgain: false),
        // orig: R099+0x0292 (1838:0292); hint R099+0x02CE matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R099+0x0308 (1838:0308); hint R099+0x0344 matched
        Wrong(c, 2, Attribute.Strength),
        // orig: `case 4: case 5: R099+0x037E(l_4.a)`: line 4 shares line 5's action (the curse); hint R099+0x03BA matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R099+0x037E (1838:037E); hint R099+0x0402 matched
        Wrong(c, 2, Attribute.Strength, () => OptionHint.Say(SuccessAssured)),
    ];
}
