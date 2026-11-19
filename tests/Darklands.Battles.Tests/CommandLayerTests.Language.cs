using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Simulation;

namespace Darklands.Battles.Tests;

// A card text from a translation pack (L-7): drawn in the pack's words, its menu chooses as the original's (with this class's scripted host).
public sealed partial class CommandLayerTests
{
    private static (int Chosen, byte[] Frame, List<string> Reports) ChooseLine(GameFiles files, string? pack)
    {
        var off = new MouseState(5, 5, MouseButtons.None);
        GameUi? ui = null;
        byte[]? frame = null;
        bool InMenu() => ui?.CommandTargets is not null;
        (int, MouseState) Line(int n, MouseButtons buttons) =>
            CardScreen.OptionPoint(n) is var (x, y) ? (0, new MouseState(x, y, buttons)) : (0, off);
        var host = new ScriptHost(
            () => (0, off), () => (Keys.Enter, off),
            () =>
            {
                if (!InMenu())
                {
                    return null;
                }
                frame = (byte[])ui!.Canvas.Screen.Pixels.Clone();
                return Line(1, MouseButtons.None);
            },
            () => Line(1, MouseButtons.Left), () => Line(1, MouseButtons.None));
        var world = WorldData.Load(files);
        ui = new GameUi(host, new GameAssets(files));
        var reports = new List<string>();
        if (pack is not null)
        {
            Assert.True(ui.UseLanguagePack(pack, reports.Add));
        }
        var state = NewGame.Quickstart(world, 1, () => 1);
        state.Data = world;
        Darklands.Cards.CardContext? context = null;
        var screen = new CardScreen(ui, () => context!, new PartyPanel(ui.Assets));
        context = new Darklands.Cards.CardContext(state, world, screen);
        screen.Show("$Other00", Darklands.Cards.PictureNames.Of(34));
        var chosen = screen.Choose(0, Enumerable.Repeat(Darklands.Cards.OptionState.Enabled, 10).ToList());
        Assert.False(host.QuitRequested);
        return (chosen, frame!, reports);
    }

    [Fact]
    public void ATranslatedCardTextIsDrawnInItsWordsAndChoosesAsTheOriginal()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var english = ChooseLine(files, null);
        var raw = new GameAssets(files).Messages("$Other00").Blocks[0].Text;
        var folder = Path.Combine(Path.GetTempPath(), "dl-cardtext-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de", "msg"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            // the block's own written form with other words before it: the same option lines
            File.WriteAllText(Path.Combine(folder, "text", "de", "msg", "$OTHER00.ftl"), "msg-other00-0 = Übersetzt. " + CardTextForm.ToWritten(raw) + "\n");
            var translated = ChooseLine(files, folder);
            Assert.Equal(english.Chosen, translated.Chosen);
            Assert.NotEqual(english.Frame, translated.Frame);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
