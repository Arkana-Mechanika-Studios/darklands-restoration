using Darklands.Platform;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The sound device of the card oracle: every request the cards make reaches the driver as the game's one command, sound-start(n)
/// (root 047C:078A), and is a trace line "sound n", as the oracle's stub of that function writes it (tools/oracle/card_oracle.py).
/// music_play (R043+0x09AE) is a "music x" line before its own sound line; both come from <see cref="Darklands.Audio.GameSounds"/>
/// and the hooks of <see cref="CardOracleTests.Run"/>.
/// </summary>
public sealed class OracleAudio(List<string> log) : IAudio
{
    public void PlayMusic(int tune) => log.Add($"sound {tune}");

    public void StopMusic() => log.Add("sound 1");

    public void PlaySound(int effect) => log.Add($"sound {effect}");

    /// <summary>The oracle's stub of sound-start leaves AX 0.</summary>
    public int PlayMusicNow(int tune)
    {
        log.Add($"sound {tune}");
        return 0;
    }
}
