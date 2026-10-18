namespace Darklands.Cards.Scripts;

/// <summary>
/// What the cards of the Last Castle of the Apocalypse share: the way a room ends (a fight
/// against one of the castle's enemy groups, or going back with battle mode 1) and the saint
/// pop-up hint that names the praying member as $ChosenOneName.
/// </summary>
internal static class LastCastle
{
    /// <summary>The enemy group the castle's rooms send the party back to the courtyard with.</summary>
    public const int CourtyardEnemy = 0x57;

    /// <summary>Ends the card with a fight (battle mode 0) against <paramref name="enemy"/>, on a fresh seed.</summary>
    // orig: the battle ending repeated inline in the R130..R133 last-castle handlers (e.g. R130+0x0EDA) matched
    // C: battle_seed = rand_mod(1000) is the last draw of the handler, after battle_mode / battle_enemy are set.
    public static void Fight(CardContext c, int enemy)
    {
        c.BattleMode = 0;
        c.BattleEnemy = enemy;
        c.BattleSeed = c.Random(1000);
        c.Next = -2;
    }

    /// <summary>Ends the card without a fight (battle mode 1): the party turns back.</summary>
    // orig: the "leave" handlers of the last-castle cards (e.g. R130+0x13AA) matched
    public static void TurnBack(CardContext c)
    {
        c.BattleMode = 1;
        c.Next = -2;
    }

    /// <summary>
    /// The saint pop-up hint of these cards: the member chosen in the box becomes $ChosenOneName
    /// and the chance text (lower chance first) is shown for the whole party.
    /// </summary>
    // orig: the saint pop-up handlers of the last-castle cards (e.g. R130+0x14D6) matched
    // C: saint_chance_lo is called first (stored), then saint_chance_hi as chance_text's argument.
    public static OptionHint SaintHintForChosen(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var low = c.SaintChanceLow(c.MenuMember, c.CardSaints[c.MenuSlot]);
        var high = c.SaintChanceHigh(c.MenuMember, c.CardSaints[c.MenuSlot]);
        return OptionHint.Say(c.ChanceText(low, high));
    }
}
