using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC14": the inner gate of the last castle opens only for a party carrying all six Keys
/// of the Apocalypse (items 0x8C..0x91); then the party enters the chamber and faces its
/// guardian (battle 0x5D), otherwise it is sent back (−2, battle mode 1).
/// </summary>
// orig: R133+0x11DC (1955:000C) matched; tests: CardSmokeTests
//   other: R133+0x132A, R133+0x1362
// C: the card-engine globals DS:EE70 / DS:EE72 the C sets are the pop-up boxes' drawing flags (R043+0x2ACA, see DIVERGENCE_REVIEW); presentation only, not modelled.
// DORMANT: the file's table set-up R133+0x132A and its "screwed up." default handler R133+0x1362 are never called
//   (the card runs no menu); they would only clear the option table and the item/saint lists.
public sealed class GateOfTheSixKeys : Card
{
    public override int Number => 358;

    private const int Picture = 0;
    private const int ShutGate = 257;
    private const int OpenGate = 259;

    public override int Run(CardContext c)
    {
        // The card has no options: the original leaves the item and saint lists as they are.
        Show(c, Picture);
        if (HasAllSixKeys(c))
        {
            ShowPicture(c, OpenGate);
            c.Screen.Text(1);
            c.BattleMode = 0;
            c.BattleEnemy = 0x5D;
            c.BattleSeed = c.Random(1000);
        }
        else
        {
            ShowPicture(c, ShutGate);
            c.Screen.Text(0);
            c.BattleMode = 1;
        }
        c.Next = -2;
        return -2;
    }

    // orig: (part of R133+0x11DC) matched
    // C: && short-circuits: count_items stops at the first key missing (no side effects either way).
    /// <summary>The keys are looked for in this order (the test stops at the first one missing).</summary>
    private static bool HasAllSixKeys(CardContext c) =>
        new[] { 0x8F, 0x8C, 0x8E, 0x8D, 0x90, 0x91 }.All(key => c.CountItems(Who.Everybody, key) != 0);
}
