namespace Darklands.Cards;

/// <summary>
/// The game's full screens that cards open (shop, camp, alchemy, item store, character sheet,
/// party information, battles). Each returns when the player leaves it.
/// </summary>
public interface IGameScreens
{
    /// <summary>A shop: <paramref name="shop"/> 0..8 city shop quality slot, −1 none, −2 pawnshop; <paramref name="categories"/> item category mask.</summary>
    void Barter(int shop, string name, long categories);

    /// <summary>Camping, an inn residence or sleeping rough (mode 0 inn, 1 wilderness, 2 other lodging, 3 rough).</summary>
    void Camp(int mode);

    /// <summary>The alchemy screen; <paramref name="place"/> is the camp mode it is used from.</summary>
    void Alchemy(int member, int place);

    /// <summary>The items stored at the current place.</summary>
    void ItemStore();

    /// <summary>A member's character sheet and pack.</summary>
    void CharacterSheet(int member);

    /// <summary>The party information screen (F6).</summary>
    void PartyInfo();

    /// <summary>Distributing loot: items and money found.</summary>
    void Loot(IList<Core.Characters.ItemStack> items, Core.Economy.Money money);

    /// <summary>A battle; returns its result code (0 won, 2 fled, 3 lost, 4 surrendered, 5 all down ...).</summary>
    int Battle(BattleRequest request);
}

/// <summary>
/// What a card asks for when it starts a fight: battlefield kind, seed and up to three enemy
/// groups (type, quality, number); <paramref name="Mode"/> is battle-entry's mode (0 a new
/// battlefield, 1 back onto the last one, 2 through a door of it, 3 through a door onto level
/// <paramref name="Battlefield"/>); <paramref name="Persistent"/> is fight-flags bit 0 (the main
/// loop's battles: winning does not end the battle, the party leaves through an exit); <paramref name="Resume"/> is bit 0x100 of the
/// combat flags (DS:9080) (a game saved in a battle: the battle is taken up again from the program's battle file).
/// </summary>
public sealed record BattleRequest(int Battlefield, int Seed, IReadOnlyList<(int Type, int Quality, int Count)> Groups, int Mode = 0, bool Persistent = false,
    int CombatFlags = 0, bool Resume = false);

/// <summary>Screens a host does not provide: each says so and returns (a battle counts as won).</summary>
public sealed class ScreensUnavailable(ICardScreen screen) : IGameScreens
{
    public void Barter(int shop, string name, long categories) => screen.Tell($"{name}: trading is not available here.");

    public void Camp(int mode) => screen.Tell("Camping is not available here.");

    public void Alchemy(int member, int place) => screen.Tell("Alchemy is not available here.");

    public void ItemStore() => screen.Tell("The item store is not available here.");

    public void CharacterSheet(int member) => screen.Tell("The character sheet is not available here.");

    public void PartyInfo() => screen.Tell("Party information is not available here.");

    public void Loot(IList<Core.Characters.ItemStack> items, Core.Economy.Money money) => screen.Tell("Loot is not available here.");

    public int Battle(BattleRequest request)
    {
        screen.Tell("Battles are not available here: you win.");
        return 0;
    }
}
