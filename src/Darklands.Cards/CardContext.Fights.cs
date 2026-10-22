using Darklands.Core.Time;

namespace Darklands.Cards;

/// <summary>
/// Card-engine pieces: the option-list block the game
/// keeps from card to card, the battle parameters a card leaves for the fight it
/// starts, the cards to go back to and the time until an hour.
/// </summary>
public sealed partial class CardContext
{
    /// <summary>
    /// The text block of the option lines (<c>options_block</c>). The game keeps it from card to
    /// card, and a few cards show their menu with whatever block the one before left.
    /// </summary>
    // orig: options_block DS:EE53
    public int OptionsBlock { get; set; }

    // ---- battles started by a card (the card returns −2..−6) ------------------------

    /// <summary>The kind of fight the card asks for (<c>battle_mode</c>).</summary>
    // orig: battle_mode DS:A766, battle_enemy DS:A768, battle_seed DS:A76A, battle_result DS:A893
    public int BattleMode { get => State.BattleOrders.Mode; set => State.BattleOrders.Mode = value; }

    /// <summary>The enemies of the fight (<c>battle_enemy</c>).</summary>
    public int BattleEnemy { get => State.BattleOrders.Enemy; set => State.BattleOrders.Enemy = value; }

    /// <summary>The fight's seed (<c>battle_seed</c>).</summary>
    public int BattleSeed { get => State.BattleOrders.Seed; set => State.BattleOrders.Seed = value; }

    /// <summary>Flags for the start of the next fight (<c>combat_flags</c>, DS:9080; bit 8: the foes expected the party).</summary>
    // C: the meaning of the bits is not established; the cards set bit 8 before an ambush.
    public int CombatFlags { get => State.BattleOrders.Flags; set => State.BattleOrders.Flags = value; }

    /// <summary>
    /// The outcome of the last fight (<c>battle_result</c>: 0 won, 3 lost ...); cards set it
    /// too when they settle a fight without one.
    /// </summary>
    public int BattleResult { get => State.BattleOrders.Result; set => State.BattleOrders.Result = value; }

    // ---- cards to go back to --------------------------------------------------------

    /// <summary>The map (card 12).</summary>
    public const int OverlandMapCard = 0x0C;

    /// <summary>The card kept to come back to (<c>saved_card</c>, DS:E7D8); the cards that lead to a place keep it.</summary>
    public int ReturnCard => State.SavedCard;

    /// <summary>The card shown before this one (<c>prev_screen</c>, DS:A88D), the map when there was none.</summary>
    // DIVERGENCE: the original returns prev_screen as it is; 0 (only inside card 0, $Initi00, or before the first
    // transition: play never produces it, the game starts on card 41) would be card 0. Here 0 leads to the map.
    public int CardBefore => State.PreviousCard != 0 ? State.PreviousCard : OverlandMapCard;

    // ---- texts ----------------------------------------------------------------------

    // $Support (Support[]) and random_name (RandomName, PersonalNames) are shared: see CardContext.Memory.cs.

    // ---- time -----------------------------------------------------------------------

    /// <summary>The hours from now until <paramref name="hour"/> (<c>hours_until</c>).</summary>
    // orig: R029+0x577A (1367:086A) hours_until matched
    public int HoursUntil(int hour) => TimeOfDay.HoursUntil(State.Date.Hour, hour);
}
