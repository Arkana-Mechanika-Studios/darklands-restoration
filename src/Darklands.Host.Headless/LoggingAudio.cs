using Darklands.Audio;
using Darklands.Audio.Packs;
using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Rendering;

/// <summary>
/// The audio of the host with each driver command written as a line "sound n": the numbers of the original's sound-start(n)
/// (root 047C:078A), whoever asked (music-start R043+0x09AE, the battle's effects R014+0x465E, a card's own call).
/// </summary>
internal sealed class LoggingAudio(IAudio inner, Action<string> write) : IAudio
{
    public void PlayMusic(int tune)
    {
        write($"sound {tune}");
        inner.PlayMusic(tune);
    }

    public void PlayMusicAt(int tune, long tick)
    {
        write($"sound {tune}");
        inner.PlayMusicAt(tune, tick);
    }

    public void StopMusic()
    {
        write("sound 1");
        inner.StopMusic();
    }

    public void PlaySound(int effect)
    {
        write($"sound {effect}");
        inner.PlaySound(effect);
    }

    // orig: music-start(5) keeps the AX the driver leaves (DS:9C3D, played back at the end of the prayer screen, the camp, the shell game ...). The
    // original's sessions run on NSOUND.DLC, whose play slot is a bare far return: the AX is what the caller had (0 in the runs seen); the silent
    // host answers -1 (no driver), so the played-back piece would differ (GameSounds.CurrentPiece)
    public int PlayMusicNow(int tune)
    {
        write($"sound {tune}");
        var ax = inner.PlayMusicNow(tune);
        return ax < 0 ? 0 : ax;
    }

    public int RequestCurrentPiece() => PlayMusicNow(5);

    // a read-only notice for a sound pack: not a driver call, so it is not logged
    public void CardShown(int card) => inner.CardShown(card);
}
