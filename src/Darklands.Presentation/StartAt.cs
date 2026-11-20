using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Economy;
using Darklands.Core.Time;
using Darklands.Simulation;

namespace Darklands.Presentation;

/// <summary>
/// Where a translation test starts (<c>--start-at</c>, the Workshop's Play in game): a card text's card, at the passage of the text
/// (<c>card:$CITYC00#3</c>), or a screen (<c>screen:BarterScreen</c>), on a Quick Start party. A later passage of a card is found
/// before the game starts: the card is run without a display on a fresh start state, trying its menu's choices breadth first, and
/// the choices that lead to the passage are replayed in the game (<see cref="CardScreen.Planned"/>). A passage no tried choice and
/// state reaches opens at the card's start, with a notice (<see cref="Notice"/>).
/// </summary>
// Not in the original: the 1.1 Workshop's translation test. Never used in normal play: only with a language pack (DarklandsRP --start-at).
public sealed class StartAt
{
    /// <summary>The menus a search goes down at most, and the card runs it makes at most for one card text file.</summary>
    public const int MaxDepth = 6, MaxRuns = 3000;

    /// <summary>The cards a search follows a card into, when the next one shows the same card text file (a card's later visit).</summary>
    public const int MaxHops = 3;

    /// <summary>The Quick Start seed of every start (the same party and city each time, so the found choices replay).</summary>
    public const uint Seed = 0x5EED;

    private StartAt(string target)
    {
        Target = target;
    }

    /// <summary>The target as given (<c>card:$FILE#n</c> or <c>screen:Name</c>).</summary>
    public string Target { get; }

    /// <summary>The card to start at (null: a screen's start, at the Quick Start's first card).</summary>
    public int? Card { get; private set; }

    /// <summary>The screen to open first (one of <see cref="Screens"/>; null: none).</summary>
    public string? Screen { get; private set; }

    /// <summary>The state's variant the search found the passage in (<see cref="Variants"/>).</summary>
    public int Variant { get; private set; }

    /// <summary>The planned steps before the passage: a menu's choice (0..), or -1 for a text passed over.</summary>
    public IReadOnlyList<int> Steps { get; private set; } = [];

    /// <summary>Whether the target is shown as asked (the passage reached, or the screen opened).</summary>
    public bool Reached { get; private set; }

    /// <summary>Why the target is not shown as asked (null: it is): the game starts at the nearest point and shows this once.</summary>
    public string? Notice { get; private set; }

    /// <summary>The screens a target may name (the screen names the texts' boxes record) and the short names the headless host takes.</summary>
    public static readonly IReadOnlyDictionary<string, string> Screens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CharacterSheetScreen"] = "CharacterSheetScreen", ["sheet"] = "CharacterSheetScreen",
        ["PartyInfoScreen"] = "PartyInfoScreen", ["party"] = "PartyInfoScreen",
        ["BarterScreen"] = "BarterScreen", ["barter"] = "BarterScreen",
        ["ItemStoreScreen"] = "ItemStoreScreen", ["store"] = "ItemStoreScreen",
        ["LootScreen"] = "LootScreen", ["loot"] = "LootScreen",
        ["AlchemyScreen"] = "AlchemyScreen", ["alchemy"] = "AlchemyScreen",
        ["CampScreen"] = "CampScreen", ["camp"] = "CampScreen",
        ["SaintPrayerScreen"] = "SaintPrayerScreen", ["prayer"] = "SaintPrayerScreen",
        ["PartyRosterScreen"] = "PartyRosterScreen", ["roster"] = "PartyRosterScreen",
        ["MarchingOrderScreen"] = "MarchingOrderScreen", ["order"] = "MarchingOrderScreen",
        ["OverlandScreen"] = "OverlandScreen", ["map"] = "OverlandScreen",
        ["BattleScreen"] = "BattleScreen", ["battle"] = "BattleScreen",
        ["CharacterGeneratorScreen"] = "CharacterGeneratorScreen", ["creation"] = "CharacterGeneratorScreen",
        ["LoadGameScreen"] = "LoadGameScreen", ["load"] = "LoadGameScreen",
        ["CardScreen"] = "CardScreen",
    };

    /// <summary>The state variants a search tries in turn: as the Quick Start leaves it, with the option flags set, with 300 florins, by night, by day, in another city.</summary>
    public static readonly IReadOnlyList<string> Variants = ["quick start", "option flags set", "a rich purse", "night", "day", "in another city"];

    /// <summary>
    /// The start for <paramref name="target"/>; null with <paramref name="error"/> when the target cannot be read (not
    /// <c>card:$FILE#n</c> nor <c>screen:Name</c>, an unknown screen, or no card shows that file's texts).
    /// </summary>
    public static StartAt? Resolve(string target, WorldData world, CardRegistry cards, IPersonalNames? names, GameAssets assets, out string? error)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assets);
        // the option lines a card text's block draws: the lines a player can choose (the menus enable all 20)
        int Lines(string card, int shown)
        {
            try
            {
                var blocks = assets.Messages(card).Blocks;
                return shown >= 0 && shown < blocks.Count ? blocks[shown].Text.Count(b => Graphics.CardTextLayout.IsOptionMarker(b)) : 0;
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidDataException or IOException or ArgumentException or IndexOutOfRangeException)
            {
                return 0;
            }
        }
        error = null;
        var at = new StartAt(target);
        if (target.StartsWith("screen:", StringComparison.Ordinal))
        {
            if (!Screens.TryGetValue(target[7..], out var screen))
            {
                error = $"no such screen: {target[7..]} (one of {string.Join(", ", Screens.Values.Distinct())})";
                return null;
            }
            at.Screen = screen;
            at.Reached = true;
            if (screen == "OverlandScreen")
            {
                at.Card = CardDispatcher.OverlandMap;
                at.Screen = null;
            }
            return at;
        }
        var key = target.StartsWith("card:", StringComparison.Ordinal) ? target[5..] : target.StartsWith("msg:", StringComparison.Ordinal) ? target[4..] : null;
        var hash = key?.LastIndexOf('#') ?? -1;
        if (key is null || hash <= 0 || !int.TryParse(key[(hash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var block))
        {
            error = $"not a target: {target} (card:$FILE#n or screen:Name)";
            return null;
        }
        var file = key[..hash];
        var shown = CardsShowing(cards, world, names, Lines);
        if (!shown.TryGetValue(file, out var numbers))
        {
            error = $"no card shows the texts of {file}";
            return null;
        }
        foreach (var number in numbers)
        {
            for (var variant = 0; variant < Variants.Count; variant++)
            {
                if (Search(cards, number, file, block, variant, world, names, next => numbers.Contains(next), Lines) is { } steps)
                {
                    at.Card = number;
                    at.Variant = variant;
                    at.Steps = steps;
                    at.Reached = true;
                    return at;
                }
            }
        }
        at.Card = numbers[0];
        at.Notice = block == 0
            ? "This card text could not be shown as the game would: the card opens from its start."
            : "This passage needs a story state the Workshop could not set up: the card opens from its start (the Workshop's preview shows every passage).";
        return at;
    }

    /// <summary>The start state: a Quick Start party (always the same), with the variant's changes and the card to start at.</summary>
    public GameState NewState(WorldData world, Func<uint>? clockSeed = null) => NewState(world, Variant, Card, clockSeed);

    private static GameState NewState(WorldData world, int variant, int? card, Func<uint>? clockSeed = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var state = NewGame.Quickstart(world, Seed, clockSeed ?? (() => Seed));
        state.Data = world;
        // as the screen previews: a purse to trade and give with
        state.Party.Purse = Money.From(12, 5, 3);
        // cards that return to a remembered card need one remembered (the game always has one): the city's main street
        state.SavedCard = 6;
        state.SavedCard2 = 6;
        state.PreviousCard = 6;
        switch (variant)
        {
            case 1:
                Array.Fill(state.OptionFlags, 1);
                break;
            case 2:
                state.Party.Purse = Money.From(300, 0, 0);
                Array.Fill(state.OptionFlags, 1);
                break;
            case 3:
                state.Date = state.Date with { Hour = 22 };
                break;
            case 4:
                state.Date = state.Date with { Hour = 10 };
                break;
            case 5:
                state.CurrentLocation = 3;
                Array.Fill(state.OptionFlags, 1);
                break;
        }
        if (card is { } number)
        {
            state.CurrentCard = number;
        }
        return state;
    }

    // the cards that show each card text file (as their first Show names it, or the card table does), found once
    private static Dictionary<string, List<int>>? _showing;
    private static readonly object Gate = new();

    private static Dictionary<string, List<int>> CardsShowing(CardRegistry cards, WorldData world, IPersonalNames? names, Func<string, int, int> lines)
    {
        lock (Gate)
        {
            if (_showing is not null)
            {
                return _showing;
            }
            var showing = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            void Add(string name, int number)
            {
                if (name.Length == 0)
                {
                    return;
                }
                if (!showing.TryGetValue(name, out var list))
                {
                    showing[name] = list = [];
                }
                if (!list.Contains(number))
                {
                    list.Add(number);
                }
            }
            foreach (var (number, card) in cards.Original.OrderBy(c => c.Key))
            {
                // the files a card shows within its first two menus (the start state's and the option flags' variants)
                foreach (var variant in new[] { 0, 1 })
                {
                    Explore(cards, number, variant, world, names, depth: 2, maxRuns: 60, probe =>
                    {
                        foreach (var name in probe.Names)
                        {
                            Add(name.Replace(".msg", "", StringComparison.OrdinalIgnoreCase), number);
                        }
                        return false;
                    }, file: "", block: -1, follow: _ => false, lines);
                }
                if (CardTable.Find(number)?.Name is { } tableName)
                {
                    Add(tableName.Replace(".msg", "", StringComparison.OrdinalIgnoreCase), number);
                }
            }
            return _showing = showing;
        }
    }

    private static CardContext Context(GameState state, WorldData world, ICardScreen screen, IPersonalNames? names) =>
        new(state, world, screen) { PersonalNames = names ?? NoPersonalNames.Instance };

    /// <summary>
    /// The steps before card <paramref name="card"/> shows block <paramref name="block"/> of <paramref name="file"/> in state variant
    /// <paramref name="variant"/> (null: not within <see cref="MaxDepth"/> menus and <see cref="MaxRuns"/> runs).
    /// </summary>
    public static IReadOnlyList<int>? Search(CardRegistry cards, int card, string file, int block, int variant, WorldData world, IPersonalNames? names, Func<int, bool> follow,
        Func<string, int, int> lines)
    {
        IReadOnlyList<int>? found = null;
        Explore(cards, card, variant, world, names, MaxDepth, MaxRuns, probe =>
        {
            found = probe.Found;
            return found is not null;
        }, file, block, follow, lines);
        return found;
    }

    // runs card <paramref name="card"/> breadth first over its menus' choices (and on into the next cards <paramref name="follow"/>
    // takes) until <paramref name="done"/> says so or the depth or the runs run out
    private static void Explore(CardRegistry cards, int card, int variant, WorldData world, IPersonalNames? names, int depth, int maxRuns,
        Func<Probe, bool> done, string file, int block, Func<int, bool> follow, Func<string, int, int> lines)
    {
        var queue = new Queue<int[]>();
        queue.Enqueue([]);
        for (var runs = 0; queue.Count > 0 && runs < maxRuns; runs++)
        {
            var choices = queue.Dequeue();
            var probe = new Probe(choices, file, block, lines);
            var state = NewState(world, variant, card);
            try
            {
                var number = card;
                for (var hop = 0; hop <= MaxHops && cards.Find(number) is { } script; hop++)
                {
                    var next = script.Run(Context(state, world, probe, names));
                    if (CardDispatcher.IsBattle(next) || !follow(next))
                    {
                        break;
                    }
                    CardDispatcher.FinishPass(state, next);
                    number = next;
                }
            }
            catch (Probe.Stop)
            {
            }
            catch (Exception e) when (e is InvalidOperationException or NullReferenceException or IndexOutOfRangeException or ArgumentException or KeyNotFoundException or NotSupportedException or GameOverException)
            {
                // a card that needs what a bare start state lacks: this path ends here
            }
            if (done(probe))
            {
                return;
            }
            if (probe.Offered is { } offered && choices.Length < depth)
            {
                foreach (var option in offered)
                {
                    queue.Enqueue([.. choices, option]);
                }
            }
        }
    }

    /// <summary>A card screen without a display: it gives the planned choices, records the steps, and stops at the block searched for.</summary>
    private sealed class Probe(int[] choices, string file, int block, Func<string, int, int> lines) : ICardScreen
    {
        public sealed class Stop : Exception
        {
        }

        private readonly List<int> _steps = [];
        private int _chosen;
        private string _card = "";
        private int _lastBlock = -1;

        public List<string> Names { get; } = [];

        public IReadOnlyList<int>? Found { get; private set; }

        public IReadOnlyList<int>? Offered { get; private set; }

        public void Show(string cardName, string picture)
        {
            _card = cardName.Replace(".msg", "", StringComparison.OrdinalIgnoreCase);
            Names.Add(cardName);
        }

        public void ShowPicture(string picture)
        {
        }

        private bool Hit(int shown) => shown == block && string.Equals(_card, file, StringComparison.OrdinalIgnoreCase);

        public void Text(int shown, TextFrame frame = TextFrame.OnPicture)
        {
            if (Hit(shown))
            {
                Found = [.. _steps];
                throw new Stop();
            }
            _lastBlock = shown;
            _steps.Add(-1);
            if (_steps.Count > 200)
            {
                throw new Stop();
            }
        }

        public int Choose(int shown, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
        {
            if (Hit(shown))
            {
                Found = [.. _steps];
                throw new Stop();
            }
            _lastBlock = shown;
            return Next(options);
        }

        public int ChooseOnLastLayout(IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint, Func<int, OptionHint>? popupHint) => Next(options);

        private int Next(IReadOnlyList<OptionState> options)
        {
            // the lines drawn (the block's option markers); none counted: the first ten
            var drawn = _lastBlock >= 0 ? lines(_card, _lastBlock) : 0;
            var enabled = Enumerable.Range(0, Math.Min(options.Count, drawn > 0 ? drawn : 10)).Where(i => options[i] == OptionState.Enabled).ToList();
            if (_chosen >= choices.Length)
            {
                Offered = enabled;
                throw new Stop();
            }
            var choice = choices[_chosen++];
            if (!enabled.Contains(choice))
            {
                throw new Stop();
            }
            _steps.Add(choice);
            return choice;
        }

        public void Tell(GameText message, int member = OptionHint.WholeParty)
        {
        }

        public void Post(GameMessage message)
        {
        }

        public void PassTime(int hours)
        {
        }
    }
}
