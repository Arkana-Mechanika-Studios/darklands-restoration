namespace Darklands.Host.Sdl;

/// <summary>
/// The program's command line as the player reads it (--help) and the check of what is left once the known options are taken:
/// a leftover word that starts with '-' is an unknown option (or an option without its value), never the game folder.
/// </summary>
internal static class CommandLineUsage
{
    public const string Text = """
        Usage: DarklandsRP [game folder] [options] [-- <the game's DOS command line: -Q, -O, -A<letter>>]
          --settings <file>          the settings file (else next to the program, else %APPDATA%\DarklandsRestoration)
          --write-settings <file>    write a commented settings file there and stop
          --setup | --no-launcher    always show the launcher | never (or hold Shift as the program starts)
          --sound <card>             adlib, sb, sbpro2, mt32, cm32l, speaker or none
          --mt32-roms <folder>       the MT-32 / CM-32L ROM files (default: MT32_ROMS next to the program)
          --no-audio                 no sound at all
          --audio-pack <folder>      replace the music and sounds (docs/PACKS.md)
          --soundfont <file>         the SoundFont for the pack's .mid music (default: the first .sf2 in SOUNDFONT next to the program)
          --hd-pack <folder>         replace the pictures
          --mod <folder or name>     play a mod; once per mod (docs/MODS.md)
          --scale <N> | --resolution <WxH>    the window's size
          --fullscreen               full screen
          --clean-drawing            the cleaner drawing of 0.9.5
          --no-banner | --no-intro   skip the start-up screen | the opening animation
          --saves <folder>           where the saved games go
          --screenshots <folder>     where the screenshots go
          --text-font <file>         the text screen's 8x16 font
          --sdl <file>               the SDL2 library to use
          --no-console               no developer console (F11)
          --sound-test               check the sound without the game, then stop
          --version | --help         the version | this text
        """;

    /// <summary>The first leftover argument that looks like an option, or null when there is none.</summary>
    public static string? Unknown(IEnumerable<string> leftover) =>
        leftover.FirstOrDefault(a => a.StartsWith('-'));
}
