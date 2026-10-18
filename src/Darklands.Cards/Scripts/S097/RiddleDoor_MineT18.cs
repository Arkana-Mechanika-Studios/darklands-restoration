using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT18": Six lying faces, three of them Repin, Golibert and Hansu.</summary>
// orig: R097+0x048C (1880:000C) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT18 : DwarfRiddleDoor
{
    public override int Number => 0xD5;

    // orig: sort R097+0x063E (1880:01BE); reset R097+0x0976 (1880:04F6) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R097+0x0660 (1880:01E0); hint R097+0x069C matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R097+0x06D6 (1880:0256); hint R097+0x0712 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R097+0x074C (1880:02CC); hint R097+0x0788 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R097+0x07C2 (1880:0342); hint R097+0x080A matched
        Right(c, 2),
        // orig: R097+0x0844 (1880:03C4); hint R097+0x0880 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R097+0x08BA (1880:043A); hint R097+0x08F6 matched
        Wrong(c, 1, Attribute.Charisma),
        // orig: R097+0x0930 (1880:04B0); hint R097+0x093E matched
        TurnAway(c),
    ];
}
