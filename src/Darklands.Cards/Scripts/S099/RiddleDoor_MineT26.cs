using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT26": Eight words on knobs: the one that belongs not.</summary>
// orig: R099+0x04AA (1882:000A) matched; tests: CardSmokeTests
// DORMANT: R099+0x09A8 (1882:0508) is in the file but no option line calls it.
// DORMANT: R099+0x0A1E (1882:057E) is in the file but no option line calls it.
// DORMANT: R099+0x0A66 (1882:05C6) is in the file but no option line calls it.
public sealed class RiddleDoor_MineT26 : DwarfRiddleDoor
{
    public override int Number => 0xDD;

    // orig: sort R099+0x067A (1882:01DA); reset R099+0x0A9E (1882:05FE) matched
    protected override (int Sort0, int Sort1, int Sort2) Beyond => (0xE6, 0xE7, 0xEB);

    protected override MenuLine[] Answers(CardContext c) =>
    [
        // orig: R099+0x069C (1882:01FC); hint R099+0x06D8 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R099+0x0712 (1882:0272); hint R099+0x074E matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R099+0x0788 (1882:02E8); hint R099+0x07C4 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R099+0x07FE (1882:035E); hint R099+0x083A matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R099+0x0874 (1882:03D4); hint R099+0x08B0 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R099+0x08EA (1882:044A); hint R099+0x0926 matched
        Wrong(c, 1, Attribute.Agility),
        // orig: R099+0x0960 (1882:04C0); hint: none (the reset's "screwed up." handler) matched
        Right(c, 2, hint: () => OptionHint.Say(CardTexts.ScrewedUp)),
        // orig: R099+0x09E2 (1882:0542); hint: none (the reset's "screwed up." handler) matched
        Wrong(c, 1, Attribute.Agility, () => OptionHint.Say(CardTexts.ScrewedUp)),
        // orig: R099+0x0A58 (1882:05B8); hint: none (the reset's "screwed up." handler) matched
        TurnAway(c, hint: CardTexts.ScrewedUp),
    ];
}
