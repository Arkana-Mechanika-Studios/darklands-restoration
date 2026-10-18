using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT15": "W I T N L I T ?": the next letter. (The fifth letter and turning away both show the curse without harm, as in the original.)</summary>
// orig: R096+0x0712 (18A9:0002) matched; tests: CardSmokeTests
// DORMANT: R096+0x0AF2 (18A9:03E2) is in the file but no option line calls it.
public sealed class RiddleDoor_MineT15 : DwarfRiddleDoor
{
    public override int Number => 0xD2;

    // orig: sort R096+0x0898 (18A9:0188); reset R096+0x0B38 (18A9:0428) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    /// <summary>The curse's text, without its harm.</summary>
    private static void ShowTheCurse(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.Screen.Text(2);
    }

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R096+0x08BA (18A9:01AA); hint R096+0x08F6 matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R096+0x0930 (18A9:0220); hint R096+0x096C matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R096+0x09A6 (18A9:0296); hint R096+0x09E2 matched
        Wrong(c, 2, Attribute.Strength),
        // orig: R096+0x0A1C (18A9:030C); hint R096+0x0A64 matched
        Right(c, 1),
        // orig: `case 4: case 5: R096+0x0A9E(l_4.a)`: line 4 shares line 5's action (the curse shown); hint R096+0x0AB8 matched
        new MenuLine(() => ShowTheCurse(c), () => Unknown(c)),
        // orig: R096+0x0A9E (18A9:038E); hint R096+0x0B00 matched
        new MenuLine(() => ShowTheCurse(c), () => OptionHint.Say(SuccessAssured)),
    ];
}
