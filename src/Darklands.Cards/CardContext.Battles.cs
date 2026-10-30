using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Time;
using Darklands.Core.Rules;

namespace Darklands.Cards;

/// <summary>What a card sets up before it returns −2..−6 for the game to start a battle: a view of the game's one <see cref="Core.Battles.BattleOrders"/>.</summary>
public sealed class CardBattle(Core.Battles.BattleOrders orders)
{
    /// <summary>The battle's kind (<c>battle-kind</c>).</summary>
    public int Mode { get => orders.Mode; set => orders.Mode = value; }

    /// <summary>The enemy or encounter type (<c>battle-enemy-group</c>).</summary>
    public int Enemy { get => orders.Enemy; set => orders.Enemy = value; }

    /// <summary>The encounter seed (<c>encounter-seed</c>).</summary>
    public int Seed { get => orders.Seed; set => orders.Seed = value; }

    /// <summary>The combat flags (DS:9080) the card sets for the main loop.</summary>
    public int Flags { get => orders.Flags; set => orders.Flags = value; }

    /// <summary>The three words after <c>encounter-seed</c> (DS:A76C/A76E/A770: the enemy types of the three groups; card 298 sets 0, 0 and whether fact 1/0x12 is known).</summary>
    public int[] Parameters => orders.EnemyTypes;
}

/// <summary>Card helpers: the map and closed-place cards, leaving for the map, battles set up by a card, members joining and leaving.</summary>
public sealed partial class CardContext
{
    /// <summary>The overland map's card number.</summary>
    public const int MapCard = 0x0C;


    /// <summary>The arrival card of a place that cannot be entered (any more).</summary>
    public const int ClosedCard = 0x62;

    /// <summary>The party leaves the place for the overland map (<c>location-now = -1</c>, card 0x0C).</summary>
    public void LeaveForTheMap()
    {
        State.CurrentLocation = Core.GameState.Wilderness;
        Next = MapCard;
    }

    // The money deposited with the bankers (DS:9072 triple) is State.Party.BankNotes.

    // HoursUntil (hours-remaining-until) is shared: see CardContext.Fights.cs.

    /// <summary>The battle a card sets up before it returns −2..−6.</summary>
    public CardBattle CardBattle => new(State.BattleOrders);

    // party-wipe is the shared DeleteMember (CardContext.Memory.cs); party-leave is RemoveMember (CardContext.Helpers.cs).

    /// <summary>A character of the game's roster joins the party (<c>party-join</c>). Returns the slot, or −1.</summary>
    // orig: party-join R037+0x3CD6 (1462:3CD6) draft (fn matched): PartyRules.AddFromRoster
    public int CharacterJoins(int character) => PartyRules.AddFromRoster(State, character);

    // RandomName (made-up-name) is shared: see CardContext.Memory.cs.


    // PlayMusic / Music (R043 09ae) are shared: see CardContext.Services.cs.


    // OpenShop (R029 21aa) is shared: see CardContext.Memory.cs.
}
