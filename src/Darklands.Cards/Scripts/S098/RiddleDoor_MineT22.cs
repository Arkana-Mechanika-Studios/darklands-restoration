using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT22": Gold, silver and copper faces: touch the one that must speak truly.</summary>
// orig: R098+0x03F0 (1877:0000) matched; tests: CardSmokeTests
// DORMANT: the original enables 5 lines and handles options [0, 1, 2, 3]; the text has only 4 option lines, so options [4] are never offered.
public sealed class RiddleDoor_MineT22 : DwarfRiddleDoor
{
    public override int Number => 0xD9;

    // orig: sort R098+0x053C (1877:014C); reset R098+0x0712 (1877:0322) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE5, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R098+0x055E (1877:016E); hint R098+0x059A matched
        Wrong(c, 2, Attribute.Endurance),
        // orig: R098+0x05D4 (1877:01E4); hint R098+0x061C matched
        Right(c, 1),
        // orig: R098+0x0656 (1877:0266); hint R098+0x0692 matched
        Wrong(c, 2, Attribute.Endurance),
        // orig: R098+0x06CC (1877:02DC); hint R098+0x06DA matched
        TurnAway(c),
    ];
}
