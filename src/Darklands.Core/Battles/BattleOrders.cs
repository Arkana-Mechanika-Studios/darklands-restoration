namespace Darklands.Core.Battles;

/// <summary>
/// The battle a card hands to the main loop when it returns −2..−6, and what the main loop hands
/// back: the original's DGROUP words <c>battle-kind</c> (DS:A766), <c>battle-enemy-group</c> (DS:A768),
/// <c>encounter-seed</c> (DS:A76A), the three enemy-type words DS:A76C/A76E/A770, the combat flags
/// (DS:9080) and the outcome (<c>battle-outcome</c>, DS:A893). One set for the whole game, as in
/// the original: every card writes the same words.
/// </summary>
// orig: main loop root 03A1:000A - on −2..−6: fight-flags |= 4 (−3),
//   8 (−5), 0x80 (−6), 0xC (−4), |= 1, then battle-entry(battle-kind, battle-enemy-group, encounter-seed, −1, A76C, 0, −1, A76E, 0, −1, A770, 0)
// C: the names of DS:A76C..A770 (the enemy types of the three groups by the battle call's argument places)
//   and the meaning of most combat flag bits.
public sealed class BattleOrders
{
    /// <summary><c>battle-kind</c> (DS:A766): 0 a fresh battlefield, 1..3 reuse or levels.</summary>
    public int Mode { get; set; }

    /// <summary><c>battle-enemy-group</c> (DS:A768): the battlefield type (the level for mode 3).</summary>
    public int Enemy { get; set; }

    /// <summary><c>encounter-seed</c> (DS:A76A).</summary>
    public int Seed { get; set; }

    /// <summary>DS:A76C, DS:A76E, DS:A770: the enemy types of the three groups (quality −1, count 0 in the call).</summary>
    public int[] EnemyTypes { get; } = new int[3];

    /// <summary><c>fight-flags</c> (DS:9080): bits the cards and the main loop set for the next battle.</summary>
    public int Flags { get; set; }

    /// <summary><c>battle-outcome</c> (DS:A893): 0 won, 2 fled, 3 lost, 4 surrendered, 5 all down …; cards also set it.</summary>
    public int Result { get; set; }
}
