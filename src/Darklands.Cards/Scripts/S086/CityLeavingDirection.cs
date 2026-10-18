namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityE02": "As you leave the city, you head in a ... northerly / easterly / westerly /
/// southerly direction." An unfinished placeholder of the original: none of the four lines
/// does more than let zero hours pass.
/// </summary>
// orig: R086+0x1930 (19CB:0000) matched; tests: CardSmokeTests
// DORMANT: text blocks 1 and 2 (placeholder texts) are never shown by this card.
public sealed class CityLeavingDirection : Card
{
    public const int CardNumber = 0xAB;

    private const int Picture = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        // C: this card keeps next_card = 0x0C (the map) instead of its own number.
        c.Next = 0x0C;
        // orig: R086+0x1AF6 (19CB:01C6) matched; tests: CardSmokeTests
        // DORMANT: the card's set-up routine is empty.
        // C: the four option states are copies of opt_flag_0..3, which this card never sets: what the card before left
        // C: (GameState.OptionFlags); the other lines keep opt_on as the card before left it (its reset is empty), hidden here.
        var states = new OptionState[CardMenu.MaxLines];
        for (var i = 0; i < 4; i++)
        {
            states[i] = (OptionState)(byte)c.State.OptionFlags[i];
        }
        // DIVERGENCE: no action changes next_card, so the original offers the menu for ever (its
        // loop runs while next_card is 0x0C); here one choice ends the card, with the same next card.
        var choice = c.Screen.Choose(0, states, i => i switch
        {
            // orig: R086+0x1A74 (19CB:0144) matched; R086+0x1A98 (19CB:0168) matched; R086+0x1ABC (19CB:018C) matched; R086+0x1AE0 (19CB:01B0) matched; tests: CardSmokeTests
            0 or 1 or 2 => OptionHint.ChanceOf(100, c.Leader),
            _ => OptionHint.ChanceOf(100, OptionHint.WholeParty),
        });
        switch (choice)
        {
            case 0:
                North(c);
                break;
            case 1:
                East(c);
                break;
            case 2:
                West(c);
                break;
            case 3:
                South(c);
                break;
        }
        return c.Next;
    }

    // orig: R086+0x1A68 (19CB:0138) matched; tests: CardSmokeTests
    private static void North(CardContext c) => c.PassHours(0);

    // orig: R086+0x1A8C (19CB:015C) matched; tests: CardSmokeTests
    private static void East(CardContext c) => c.PassHours(0);

    // orig: R086+0x1AB0 (19CB:0180) matched; tests: CardSmokeTests
    private static void West(CardContext c) => c.PassHours(0);

    // orig: R086+0x1AD4 (19CB:01A4) matched; tests: CardSmokeTests
    private static void South(CardContext c) => c.PassHours(0);
}
