using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT23": Three faces: press one that must speak truth. (Turning away opens the door too, as in the original.)</summary>
// orig: R098+0x0782 (18B0:0002) matched; tests: CardSmokeTests
// DORMANT: R098+0x0A50 (18B0:02D0) is in the file but no option line calls it.
public sealed class RiddleDoor_MineT23 : DwarfRiddleDoor
{
    public override int Number => 0xDA;

    // orig: sort R098+0x08C0 (18B0:0140); reset R098+0x0A96 (18B0:0316) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R098+0x08E2 (18B0:0162); hint R098+0x091E matched
        Wrong(c, 2, Attribute.Charisma),
        // orig: R098+0x0958 (18B0:01D8); hint R098+0x0994 matched
        Wrong(c, 2, Attribute.Charisma),
        // orig: R098+0x09CE (18B0:024E); hint R098+0x0A16 matched
        Right(c, 1),
        // orig: R098+0x09CE (18B0:024E); hint R098+0x0A5E matched
        Right(c, 1, hint: () => OptionHint.Say(SuccessAssured)),
    ];
}
