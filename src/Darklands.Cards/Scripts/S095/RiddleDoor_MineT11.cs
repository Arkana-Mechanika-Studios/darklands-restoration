using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT11": "Name Tifsyn's folk": dwarf, gnome or kobold.</summary>
// orig: R095+0x0874 (18BF:0004) matched; tests: CardSmokeTests
public sealed class RiddleDoor_MineT11 : DwarfRiddleDoor
{
    public override int Number => 0xCE;

    // orig: sort R095+0x09BC (18BF:014C); reset R095+0x0B92 (18BF:0322) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE3, 0xE7, 0xEA);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R095+0x09DE (18BF:016E); hint R095+0x0A26 matched
        Right(c, 2),
        // orig: R095+0x0A60 (18BF:01F0); hint R095+0x0A9C matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R095+0x0AD6 (18BF:0266); hint R095+0x0B12 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R095+0x0B4C (18BF:02DC); hint R095+0x0B5A matched
        TurnAway(c),
    ];
}
