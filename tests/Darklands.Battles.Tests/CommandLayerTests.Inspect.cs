using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

// The inspector's card lines (I-4): the card screen names its card, the block shown and the option line under a point (with this class's scripted host).
public sealed partial class CommandLayerTests
{
    [Fact]
    public void TheCardScreenNamesItsCardItsBlockAndTheOptionLineThere()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var off = new MouseState(5, 5, MouseButtons.None);
        GameUi? ui = null;
        CardScreen? screen = null;
        IReadOnlyList<string>? atLine = null;
        IReadOnlyList<string>? offLines = null;
        bool InMenu() => ui?.CommandTargets is not null;
        (int, MouseState) Line(int n, MouseButtons buttons) =>
            CardScreen.OptionPoint(n) is var (x, y) ? (0, new MouseState(x, y, buttons)) : (0, off);
        var host = new ScriptHost(
            () => (0, off), () => (Keys.Enter, off),
            () =>
            {
                if (!InMenu() || CardScreen.OptionPoint(1) is not var (x, y))
                {
                    return null;
                }
                atLine = screen!.Inspect(x, y);
                offLines = screen.Inspect(5, 5);
                return Line(1, MouseButtons.None);
            },
            () => Line(1, MouseButtons.Left), () => Line(1, MouseButtons.None));
        var world = WorldData.Load(files);
        ui = new GameUi(host, new GameAssets(files));
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        Assert.Empty(screen.Inspect(100, 100));   // no card named yet
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        screen.Choose(0, Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList());
        Assert.False(host.QuitRequested);
        Assert.NotNull(atLine);
        Assert.StartsWith("card $Other00", atLine[0]);
        Assert.Contains("text block 0, its option lines", atLine);
        Assert.Contains("option line 1 (Plain, Enabled)", atLine);
        // left of the text area: the card and its block, no option line
        Assert.Equal(2, offLines!.Count);
    }
}
