using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Economy;
using Darklands.Core.Rules;

namespace Darklands.Cards;

/// <summary>Values a card can put into its texts ($Money1, $ChosenOneName, $Number1 …).</summary>
public sealed partial class CardVariables
{
    public Money[] Money { get; } = new Money[5];

    public int[] Numbers { get; } = new int[2];

    /// <summary>Party members named in texts as $ChosenOneName … $ChosenFiveName.</summary>
    public int[] Chosen { get; } = [0, 1, 2, 3, 4];

    /// <summary>Names for $NamedOneName … $NamedFiveName.</summary>
    public string[] Named { get; } = ["", "", "", "", ""];

    public string[] Texts { get; } = ["", "", "", ""];

    public int Location { get; set; } = -1;

    public int Location2 { get; set; } = -1;

    public int Item { get; set; }

    /// <summary>Which conditional passages (0x01 … 0x13) of the next texts are shown.</summary>
    public bool[] Conditions { get; } = new bool[10];
}

/// <summary>What a card script works with: the game, the screen, the world data and the rules.</summary>
public sealed partial class CardContext(GameState state, WorldData world, ICardScreen screen)
{
    private readonly bool _rulesConnected = ConnectRules(state, world, screen);

    public GameState State => state;

    public WorldData World => world;

    public ICardScreen Screen => screen;

    public CardVariables Variables { get; } = new();

    /// <summary>The full screens cards open (shop, camp, sheets, battles ...).</summary>
    public IGameScreens Screens { get; set; } = new ScreensUnavailable(screen);

    /// <summary>The card to run next; scripts set it and the card ends when it changes.</summary>
    public int Next { get; set; }

    /// <summary>Lets hours pass: party recovery, the clock, the hourglass.</summary>
    // orig: pass-hours R029+0x54D8 (1367:05C8) matched; hourglass-wait R043+0x3494 (150B:2A04) draft
    public void PassHours(int hours) => TimePassage.Advance(state, hours, screen.PassTime);

    // orig: roll-below root 0000:0C70 (0000:0C70) matched
    public int Random(int below) => state.Random.Below(below);
}
