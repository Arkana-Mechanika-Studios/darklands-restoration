namespace Darklands.Audio.Tests;

/// <summary>The option checks around the game's sound requests.</summary>
public sealed class GameSoundsTests
{
    [Fact]
    public void MusicOffStopsInsteadOfPlaying()
    {
        Assert.Equal(9, GameSounds.MusicNumber(9, musicOn: true));
        Assert.Equal(GameSounds.Stop, GameSounds.MusicNumber(9, musicOn: false));
    }

    [Theory]
    [InlineData(10, true, false, true)]
    [InlineData(10, false, true, false)]
    [InlineData(0x12, false, false, true)]   // "cannot go there" always sounds
    [InlineData(8, false, true, true)]       // 7..9 follow the music option
    [InlineData(8, true, false, false)]
    [InlineData(6, true, true, false)]
    public void BattleSoundsFollowTheOptions(int effect, bool effectsOn, bool musicOn, bool allowed)
    {
        Assert.Equal(allowed, GameSounds.BattleSoundAllowed(effect, effectsOn, musicOn));
    }
}
