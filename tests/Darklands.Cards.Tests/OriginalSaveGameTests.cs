using Darklands.Persistence;
using Darklands.Simulation;

namespace Darklands.Cards.Tests;

/// <summary>
/// A saved game of the original (from the player's SAVES folder, copied to a temporary folder)
/// is loaded and the game goes on from its card without a display; saving it again afterwards
/// writes a file the loader reads back.
/// </summary>
public sealed class OriginalSaveGameTests
{
    [Theory]
    [InlineData("DKSAVE12.SAV")]
    [InlineData("DKSAVE18.SAV")]
    [InlineData("Dksave21.sav")]
    public void AnOriginalSaveLoadsAndPlaysOn(string name)
    {
        if (!CardHarness.Available || CopyOfSaves() is not { } folder || !folder.Names().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }
        var loaded = folder.Load(name, CardHarness.World!);
        var state = loaded.State;
        var card = state.CurrentCard;
        Assert.True(state.Party.Members.Count > 0);
        Assert.Equal(state.CurrentLocation is >= 0 and < 92 ? state.CurrentLocation : state.City.City, state.City.City);

        var screen = new ScriptedScreen(0);
        var context = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        var dispatcher = new CardDispatcher();
        Assert.True(dispatcher.HasScript(card), $"card {card:X} has a script");
        var next = dispatcher.RunCard(context);

        Assert.Contains(screen.Log, l => l.StartsWith("show ", StringComparison.Ordinal) || l.StartsWith("text ", StringComparison.Ordinal));
        Assert.Equal(card, state.PreviousCard);
        Assert.Equal(next, state.CurrentCard);

        // The game as it now stands saves and loads again.
        var again = folder.Save(state, "after a card", loaded.Music, loaded.SoundEffects);
        var reloaded = folder.Load(again, CardHarness.World!).State;
        Assert.Equal(state.CurrentCard, reloaded.CurrentCard);
        Assert.Equal(state.Date, reloaded.Date);
        Assert.Equal(state.Party.Purse, reloaded.Party.Purse);
        Assert.Equal(state.Events.All().Count(), reloaded.Events.All().Count());
        Assert.Equal(state.Party.Members.Select(m => m.Name), reloaded.Party.Members.Select(m => m.Name));
    }

    private static SaveFolder? CopyOfSaves()
    {
        var game = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        var saves = Path.Combine(game, "SAVES");
        if (!Directory.Exists(saves))
        {
            return null;
        }
        var copy = TempCopies.Create("darklands-play-");
        foreach (var f in Directory.GetFiles(saves))
        {
            File.Copy(f, Path.Combine(copy, Path.GetFileName(f)));
        }
        return new SaveFolder(copy);
    }
}
