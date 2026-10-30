using Darklands.Cards.Scripts;

namespace Darklands.Cards.Tests;

/// <summary>The cards' music requests: through the music option (music_play) or straight to the driver (sound-start).</summary>
public sealed class MusicCardTests
{
    private static (CardContext Context, List<string> Requests) Context(Core.GameState state, ScriptedScreen screen, int playing)
    {
        var requests = new List<string>();
        var context = new CardContext(state, CardHarness.World!, screen) { PersonalNames = CardHarness.PersonalNames };
        context.Music = n => requests.Add($"music {n:X2}");
        context.SoundPlayer = n => requests.Add($"sound {n:X2}");
        context.MusicPlaying = () =>
        {
            requests.Add("music 05");
            return playing;
        };
        return (context, requests);
    }

    /// <summary>$Shell00 keeps the piece playing in the shared word (command 5), plays 0x10 and plays the kept piece back.</summary>
    [Fact]
    public void TheShellGameKeepsThePiecePlayingAndPlaysItBack()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        var screen = new ScriptedScreen(1);
        var (context, requests) = Context(state, screen, 0x0B);
        state.CurrentCard = ShellGameSwindler.CardNumber;
        new ShellGameSwindler().Run(context);
        Assert.Equal(["music 05", "music 10", "music 0B"], requests);
        Assert.Equal(0x0B, context.SavedMusic.Value);
    }

    /// <summary>The side streets and the approach to a walled city play the silence straight to the driver, whatever the music option.</summary>
    [Theory]
    [InlineData(typeof(SideStreet))]
    [InlineData(typeof(SideStreetAtNight))]
    [InlineData(typeof(WalledCityApproach_CityE00))]
    [InlineData(typeof(BeforeTheCityWalls))]
    public void SomeCardsPlayTheSilenceDirectly(Type card)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        var (context, requests) = Context(state, new ScriptedScreen(-1), 0);
        var script = (Card)Activator.CreateInstance(card)!;
        state.CurrentCard = script.Number;
        script.Run(context);
        Assert.Equal("sound 06", requests[0]);
        Assert.DoesNotContain(requests, r => r.StartsWith("music", StringComparison.Ordinal));
    }

    /// <summary>The main street's silence goes through the music option.</summary>
    [Fact]
    public void TheMainStreetPlaysTheSilenceThroughTheMusicOption()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        var (context, requests) = Context(state, new ScriptedScreen(-1), 0);
        state.CurrentCard = new MainStreet().Number;
        new MainStreet().Run(context);
        Assert.Equal("music 06", requests[0]);
    }

    /// <summary>A card's battle stops the music first (the card engine's shutdown); with nobody left the game's death follows.</summary>
    [Fact]
    public void ACardsBattleStopsTheMusicAndADefeatEndsTheGame()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame();
        var (context, requests) = Context(state, new ScriptedScreen(-1), 0);
        var dead = 0;
        context.Battle = (_, _, _) =>
        {
            requests.Add("battle");
            foreach (var m in state.Party.Members)
            {
                m.Status = Core.Characters.CharacterStatus.Dead;
            }
            return 0;
        };
        context.AllDead = () => dead++;
        context.RunBattle(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.Equal(["sound 01", "battle"], requests);
        Assert.Equal(1, dead);
    }
}
