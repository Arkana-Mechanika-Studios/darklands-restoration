namespace Darklands.Core;

/// <summary>
/// A text the game shows, as an id and its arguments, never as words: an option hint, a card notice. The presentation turns it into
/// words (docs/LOCALISATION.md). A string of DARKLAND.EXE is keyed by where it is (<c>exe:DS:XXXX</c>, <c>exe:290E:XXXX</c>, the
/// original-text extractor's keys); a sentence the original builds from pieces, or one of the rewrite's own, gets an <c>id:</c>.
/// </summary>
public sealed record GameText(string Id, IReadOnlyDictionary<string, object> Args)
{
    private static readonly IReadOnlyDictionary<string, object> NoArgs = new Dictionary<string, object>();

    /// <summary>No text (an empty string where the original leaves its buffer empty).</summary>
    public static GameText None { get; } = new("", NoArgs);

    /// <summary>The text <paramref name="id"/>, with no arguments.</summary>
    public static GameText Of(string id) => id.Length == 0 ? None : new(id, NoArgs);

    /// <summary>The text <paramref name="id"/> with name / value arguments.</summary>
    public static GameText Of(string id, params (string Name, object Value)[] args) =>
        new(id, args.ToDictionary(a => a.Name, a => a.Value, StringComparer.Ordinal));

    /// <summary>
    /// One of the original's format strings (<paramref name="id"/>, an <c>exe:</c> key) with its arguments in order (named "0", "1",
    /// ...): the presentation prints it as the original's sprintf does.
    /// </summary>
    public static GameText Format(string id, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var named = new Dictionary<string, object>(args.Length, StringComparer.Ordinal);
        for (var k = 0; k < args.Length; k++)
        {
            named[k.ToString(System.Globalization.CultureInfo.InvariantCulture)] = args[k];
        }
        return new GameText(id, named);
    }

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Id.Length == 0;

    /// <summary>An integer argument.</summary>
    public int Number(string name) => Convert.ToInt32(Args[name], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A text argument (a name the game files gave, a number written out).</summary>
    public string Word(string name) => Convert.ToString(Args[name], System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>For logs and test failures: the id and its arguments, no words.</summary>
    public override string ToString() =>
        Args.Count == 0 ? Id : $"{Id} {string.Join(' ', Args.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"))}";

    // records compare dictionaries by reference: two texts are equal when the id and every argument are
    public bool Equals(GameText? other) =>
        other is not null && Id == other.Id && Args.Count == other.Args.Count &&
        Args.All(a => other.Args.TryGetValue(a.Key, out var v) && Equals(a.Value, v));

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The <c>id:</c> texts of the cards and their screens (the <c>exe:</c> ones are keyed where they are).</summary>
public static class GameTextIds
{
    /// <summary>
    /// "Chance of success is X to Y" (<c>chance-line</c> R029+0x5B62), arguments <c>low</c>, <c>high</c> (0..100) and <c>basic</c> (1:
    /// percentages, 0: quality words): DS:27CD, the low value, DS:27E5 " to ", the high value; a word is grade-words[v / 20] (DS:4FCE).
    /// </summary>
    public const string ChanceRange = "id:card.chance-range";

    /// <summary>
    /// "The chance of success is N%" or a quality word, argument <c>chance</c> (0..100) and <c>basic</c> (the Shift hint of an option
    /// line, R043+0x27E6): DS:5284, then "N%" or grade-words[chance / 20], "hopeless" (DS:52A0) for 0.
    /// </summary>
    public const string HintChance = "id:card.hint.chance";

    /// <summary>"ERROR!!!" (DS:52A9): a handler that answers −2.</summary>
    public const string HintError = "exe:DS:52A9";

    /// <summary>The rewrite's own notices where a screen is not there (test screens), argument <c>name</c> for trading.</summary>
    public const string TradingUnavailable = "id:rw.unavailable.trading";
    public const string CampUnavailable = "id:rw.unavailable.camp";
    public const string AlchemyUnavailable = "id:rw.unavailable.alchemy";
    public const string ItemStoreUnavailable = "id:rw.unavailable.item-store";
    public const string CharacterSheetUnavailable = "id:rw.unavailable.character-sheet";
    public const string PartyInfoUnavailable = "id:rw.unavailable.party-info";
    public const string LootUnavailable = "id:rw.unavailable.loot";
    public const string BattleUnavailable = "id:rw.unavailable.battle";

    /// <summary>The rewrite's own notices of the card dispatcher and mod cards: arguments <c>number</c> or <c>name</c>.</summary>
    public const string CardNotInTable = "id:rw.card.not-in-table";
    public const string CardHasNoScript = "id:rw.card.no-script";
    public const string ModCardGoesRound = "id:rw.mod.card-goes-round";
}
