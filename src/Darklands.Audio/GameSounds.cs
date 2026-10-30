using Darklands.Platform;

namespace Darklands.Audio;

/// <summary>
/// The game's sound requests and their option checks, for the callers of <see cref="IAudio"/>.
/// Music numbers are those of the towns-and-map driver (xSOUND.DC), effect numbers those of
/// the battle driver (xSOUND.DB); every FM, Roland and speaker driver knows the same numbers.
/// </summary>
public static class GameSounds
{
    /// <summary>Towns-and-map driver: start again from silence (pressed key in the opening).</summary>
    // orig: root 03A1:000A (03a1:000a) matched - a key waiting after
    //   the opening: music-start(0), getch(), music-start(7).
    public const int Reset = 0;

    /// <summary>Stop the music (fading).</summary>
    public const int Stop = 1;

    /// <summary>Pause and resume (the game's pause, Alt-P).</summary>
    // orig: root 0000:1080 (0000:1080) draft - "Game Paused":
    //   sound-start(2), wait for a key, sound-start(3).
    public const int Pause = 2;

    public const int Resume = 3;

    /// <summary>Asks for the number of the piece playing (the game saves it to restore it later).</summary>
    // orig: R034+0x0000, R034+0x0102, R036+0x0082, R030+0x00A8 and R088+0x0B20: DS:9C3D = music-start(5),
    //   a piece of their own, then music-start(DS:9C3D) (R030+0x00A8 keeps it only).
    // C: music-start is a void function; its callers read the AX that sound-start left (ASOUND.DC image 20D0h: number 5 returns
    //   [CA60h], the number last stored). With music off it plays 1 instead and AX is what the handler of 1 leaves: 0 on the FM
    //   and Roland drivers (the play slot clears AX before the handler, which does not set it; tests: DriverTests); the speaker
    //   driver's handler leaves a value of its own, which the emulated driver gives too.
    // C: NSOUND.DLC's play slot is a bare far return, so AX is whatever the caller had; the emulated call gives its own
    //   leftover. The value only matters if music is switched on before it is played back.
    public const int CurrentPiece = 5;

    /// <summary>Silence (the city menus).</summary>
    // C: named from the reference's music-player notes ("6 silence"); called by the city cards
    //   ($CityG00, $MainS01, $citys01, $Raubi02 ...).
    public const int Silence = 6;

    /// <summary>Played once after the driver is loaded at start-up and after a skipped opening.</summary>
    // C: what number 7 does in the drivers (it makes no sound on the FM drivers).
    public const int Ready = 7;

    /// <summary>Plays a piece of music if music is on; otherwise stops the music.</summary>
    // orig: R043+0x09AE (1462:09ae) matched - music-start(x):
    //   if (music-setting) sound-start(x); else sound-start(1). tests: GameSoundsTests
    public static void PlayMusic(IAudio audio, int piece, bool musicOn) =>
        audio.PlayMusic(MusicNumber(piece, musicOn));

    /// <summary>music-start(5): the number last played if music is on (else the stop is played); the AX the callers keep.</summary>
    // orig: R043+0x09AE (1462:09ae) matched; tests: GameSoundsTests
    public static int RequestCurrentPiece(IAudio audio, bool musicOn) =>
        musicOn ? audio.RequestCurrentPiece() : audio.PlayMusicNow(Stop);

    // orig: R043+0x09AE (1462:09ae) matched
    public static int MusicNumber(int piece, bool musicOn) => musicOn ? piece : Stop;

    /// <summary>True when the battle sound may play under the sound options.</summary>
    // orig: R014+0x465E (0e76:465e) draft -
    //   (sound-setting == 1 && n >= 10) || n == 0x12 || (music-setting == 1 && n >= 7 && n <= 9).
    //   tests: GameSoundsTests
    // C: n is a signed int; the options are chars compared with 1 (they only hold 0 or 1).
    public static bool BattleSoundAllowed(int effect, bool effectsOn, bool musicOn) =>
        (effectsOn && effect >= 10) || effect == 0x12 || (musicOn && effect is >= 7 and <= 9);

    /// <summary>Plays a battle sound if the options allow it.</summary>
    // orig: R014+0x465E (0e76:465e) draft
    public static void PlayBattleSound(IAudio audio, int effect, bool effectsOn, bool musicOn)
    {
        if (BattleSoundAllowed(effect, effectsOn, musicOn))
        {
            audio.PlaySound(effect);
        }
    }
}
