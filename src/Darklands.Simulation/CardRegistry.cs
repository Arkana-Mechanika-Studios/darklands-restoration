using System.Reflection;
using Darklands.Cards;

namespace Darklands.Simulation;

/// <summary>Where a registered card comes from: the game itself or a mod (its folder name, its own name and version).</summary>
public sealed record CardSource(string Id, string Name, string Version)
{
    /// <summary>The game's own cards (the 366 scripts of <see cref="CardTable"/>).</summary>
    public static readonly CardSource Game = new("", "the game", "");

    public bool IsGame => Id.Length == 0;

    public override string ToString() => IsGame ? Name : Version.Length > 0 ? $"mod \"{Name}\" {Version} ({Id})" : $"mod \"{Name}\" ({Id})";
}

/// <summary>A card a mod added: the card, its mod, and the game's own card a saved game names in its place.</summary>
/// <param name="SavesAs">
/// For a new card (a number above <see cref="CardRegistry.LastOriginalCard"/>): the game's card the original save format
/// names instead (the place the game goes on from without the mod). For a card that replaces one of the game's: its own number.
/// </param>
public sealed record RegisteredCard(Card Card, CardSource Source, int SavesAs);

/// <summary>
/// The cards the dispatcher can run: the game's own 366 scripts, kept apart and never changed, and the cards mods
/// register on top of them. Classic (no mod) runs exactly the game's scripts.
/// </summary>
/// <remarks>
/// Precedence: a number is looked up among the mod cards first, then among the game's. A mod card numbered above
/// <see cref="LastOriginalCard"/> is a new card; a mod card with a game card's number replaces that card only when it is
/// registered as an override. Mods register in their load order; when two register the same number, the later one wins
/// and the replacement is logged. The map (0x0C), the end of the game (0x62) and the negative codes (battles, quit) are
/// not cards the dispatcher runs and cannot be registered.
/// </remarks>
// orig: the card table R136+0xD090[] that the dispatch loop of root 03A1:000A indexes (CardDispatcher): Original holds the scripts of its entries
// DIVERGENCE: no original: the original's card table R136+0xD090[] is a fixed array of 366 far pointers (CardDispatcher); this registry
//   holds that table unchanged (Original) and adds the mods' cards beside it.
public sealed class CardRegistry
{
    /// <summary>The highest card number of the game's table (0..365): every number above it belongs to a mod.</summary>
    public const int LastOriginalCard = 365;

    /// <summary>The highest number a mod card may take (a saved card number is a signed word).</summary>
    public const int LastModCard = 0x7FFF;

    private readonly Dictionary<int, Card> _original;
    private readonly Dictionary<int, RegisteredCard> _mods = [];
    private readonly List<string> _log = [];

    /// <summary>The game's card scripts found in the assemblies (default: Darklands.Cards).</summary>
    public CardRegistry(params Assembly[] scriptAssemblies)
    {
        _original = (scriptAssemblies.Length > 0 ? scriptAssemblies : [typeof(Card).Assembly])
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsSubclassOf(typeof(Card)) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Card)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Number);
    }

    /// <summary>Where the registry's lines go as they are written (a host passes its console); they are kept in <see cref="Log"/> too.</summary>
    public Action<string>? Logger { get; set; }

    /// <summary>What registering did: every card added, replaced or refused, one line each.</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>The game's own cards by number, as the scripts define them (mods never change this table).</summary>
    public IReadOnlyDictionary<int, Card> Original => _original;

    /// <summary>The mods' cards by number.</summary>
    public IReadOnlyDictionary<int, RegisteredCard> ModCards => _mods;

    /// <summary>True when a mod card is registered: a game played now may run one, and its saves get a companion file.</summary>
    public bool HasModCards => _mods.Count > 0;

    /// <summary>The mods whose cards are registered, in the order they first registered one.</summary>
    public IReadOnlyList<CardSource> Mods => _mods.Values.Select(r => r.Source).Distinct().ToList();

    /// <summary>How many numbers have a card (the game's and the new ones of mods).</summary>
    public int Count => _original.Keys.Union(_mods.Keys).Count();

    /// <summary>The card that runs for a number: a mod's first, then the game's; null for a number with neither.</summary>
    public Card? Find(int number) =>
        _mods.TryGetValue(number, out var mod) ? mod.Card : _original.GetValueOrDefault(number);

    public bool Contains(int number) => _mods.ContainsKey(number) || _original.ContainsKey(number);

    /// <summary>The game's own card of that number (what a mod's override replaces), null if the game has none.</summary>
    public Card? OriginalOf(int number) => _original.GetValueOrDefault(number);

    /// <summary>
    /// The number the original save format may hold for <paramref name="number"/>: the number itself for the game's
    /// cards (and anything that is not a card number, such as a battle code), a new card's <see cref="RegisteredCard.SavesAs"/>,
    /// null for a number above <see cref="LastOriginalCard"/> that no mod registered.
    /// </summary>
    public int? SavedNumberOf(int number) =>
        number <= LastOriginalCard ? number : _mods.TryGetValue(number, out var mod) ? mod.SavesAs : null;

    /// <summary>A number the dispatcher never runs as a card: the map, the end of the game, the negative codes.</summary>
    public static bool IsReserved(int number) =>
        number is CardDispatcher.OverlandMap or CardDispatcher.EndOfGame || number < 0;

    /// <summary>
    /// Registers a mod's card. A number above <see cref="LastOriginalCard"/> adds a card (with <paramref name="savesAs"/>,
    /// one of the game's cards, for the saved games); a game card's number replaces that card only with
    /// <paramref name="overrideOriginal"/>. Returns false (and logs why) when the card is refused.
    /// </summary>
    public bool Register(Card card, CardSource source, bool overrideOriginal = false, int? savesAs = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(source);
        var number = card.Number;
        if (source.IsGame)
        {
            return Refuse(number, source, "only a mod registers cards (the game's own are found in its scripts)");
        }
        if (IsReserved(number))
        {
            return Refuse(number, source, $"{Show(number)} is not a card the game runs (the map, the end of the game or a battle or quit code)");
        }
        if (number > LastModCard)
        {
            return Refuse(number, source, $"card numbers stop at {LastModCard}");
        }
        int saved;
        if (number <= LastOriginalCard)
        {
            if (!overrideOriginal)
            {
                return Refuse(number, source, $"{Show(number)} is one of the game's cards: a mod replaces it only as an override (\"overrides\": true)");
            }
            if (!_original.ContainsKey(number))
            {
                return Refuse(number, source, $"the game has no card {Show(number)} to override");
            }
            saved = number;
        }
        else
        {
            if (overrideOriginal)
            {
                return Refuse(number, source, $"{Show(number)} is a new card (above {LastOriginalCard}), not one of the game's: it cannot be an override");
            }
            if (savesAs is not { } fallback || fallback > LastOriginalCard || fallback < 0 || fallback == CardDispatcher.EndOfGame
                || (fallback != CardDispatcher.OverlandMap && !_original.ContainsKey(fallback)))
            {
                return Refuse(number, source, $"a new card needs \"savesAs\": one of the game's cards (0..{LastOriginalCard}, the map 12 included) a saved game names in its place");
            }
            saved = fallback;
        }
        var replaced = _mods.TryGetValue(number, out var earlier) ? earlier : null;
        _mods[number] = new RegisteredCard(card, source, saved);
        if (replaced is not null)
        {
            Write($"card {Show(number)}: {source} replaces the card of {replaced.Source} (the mod loaded later wins)");
        }
        else if (number <= LastOriginalCard)
        {
            Write($"card {Show(number)}: {source} overrides the game's card {CardTable.Find(number)?.Name}");
        }
        else
        {
            Write($"card {Show(number)}: added by {source} (saved games name card {Show(saved)} in its place)");
        }
        return true;
    }

    private bool Refuse(int number, CardSource source, string why)
    {
        Write($"card {Show(number)} of {source} refused: {why}");
        return false;
    }

    private void Write(string line)
    {
        _log.Add(line);
        Logger?.Invoke(line);
    }

    private static string Show(int number) => number < 0 ? number.ToString() : $"{number} (0x{number:X})";
}
