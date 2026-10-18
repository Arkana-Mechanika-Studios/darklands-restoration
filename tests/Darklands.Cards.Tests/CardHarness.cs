using Darklands.Cards;
using Darklands.Content;
using Darklands.Core;
using Darklands.Simulation;

namespace Darklands.Cards.Tests;

/// <summary>
/// Runs card scripts without a display: a scripted screen answers every text and picks options
/// in turn. Needs the original game files (DARKLANDS_DIR, or the "game" key of local/paths.json); without them the
/// tests that use it are skipped.
/// </summary>
public sealed class CardHarness
{
    private static readonly Lazy<WorldData?> SharedWorld = new(() =>
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? WorldData.Load(new GameFiles(folder)) : null;
    });

    public static WorldData? World => SharedWorld.Value;

    /// <summary>The EXE's name tables (empty names when DARKLAND.EXE is not there).</summary>
    public static IPersonalNames PersonalNames => SharedNames.Value;

    private static readonly Lazy<IPersonalNames> SharedNames = new(() =>
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder)
            ? new TablePersonalNames(Darklands.Content.Formats.Characters.PersonalNamesDecoder.Decode(new GameFiles(folder).Read("DARKLAND.EXE")))
            : NoPersonalNames.Instance;
    });

    public static bool Available => World is not null;

    /// <summary>The game's pictures and card texts (null without the game files).</summary>
    public static GameAssets? Assets => SharedAssets.Value;

    private static readonly Lazy<GameAssets?> SharedAssets = new(() =>
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        return GameFiles.IsGameFolder(folder) ? new GameAssets(new GameFiles(folder)) : null;
    });

    /// <summary>A Quickstart game in a given city (or the random one for the seed).</summary>
    public static GameState NewGame(uint seed = 1, int? location = null)
    {
        var state = Simulation.NewGame.Quickstart(World!, seed);
        // Cards that return to a remembered card need one remembered (the game always has one).
        state.SavedCard = 6;
        state.SavedCard2 = 6;
        state.PreviousCard = 6;
        if (location is { } here)
        {
            state.CurrentLocation = here;
        }
        return state;
    }

    /// <summary>Runs one card choosing option <paramref name="option"/> (or the first enabled one) at each menu.</summary>
    public static (int Next, ScriptedScreen Screen) Run(Card card, GameState state, int option = -1)
    {
        var screen = new ScriptedScreen(option);
        var context = new CardContext(state, World!, screen) { PersonalNames = PersonalNames };
        state.CurrentCard = card.Number;
        var next = card.Run(context);
        return (next, screen);
    }
}

/// <summary>A card screen that records what was shown and answers menus with a fixed policy.</summary>
public sealed class ScriptedScreen(int option) : ICardScreen
{
    private int _menus;

    public List<string> Log { get; } = [];

    /// <summary>The options offered at the last menu.</summary>
    public IReadOnlyList<OptionState> LastOptions { get; private set; } = [];

    public void Show(string cardName, string picture) => Log.Add($"show {cardName} {picture}");

    public void ShowPicture(string picture) => Log.Add($"picture {picture}");

    public void Text(int block, TextFrame frame = TextFrame.OnPicture) => Log.Add($"text {block}");

    public int Choose(int block, IReadOnlyList<OptionState> options, Func<int, OptionHint>? hint = null)
    {
        LastOptions = options;
        if (++_menus > 50)
        {
            throw new InvalidOperationException("The card keeps asking: it never moves on.");
        }
        Log.Add($"menu {block}");
        var enabled = Enumerable.Range(0, options.Count).Where(i => options[i] == OptionState.Enabled).ToList();
        if (enabled.Count == 0)
        {
            throw new InvalidOperationException($"Menu {block} offers no option.");
        }
        // The first menu gets the chosen option; when the card keeps asking (many original
        // choices stay on the same card), the next menus move on through the enabled options.
        var first = option >= 0 && enabled.Contains(option) ? enabled.IndexOf(option) : 0;
        var choice = enabled[(first + _menus - 1) % enabled.Count];
        _ = hint?.Invoke(choice);
        return choice;
    }

    public void Tell(string message, int member = OptionHint.WholeParty) => Log.Add($"tell {message}");

    public void PassTime(int hours) => Log.Add($"time {hours}");
}
