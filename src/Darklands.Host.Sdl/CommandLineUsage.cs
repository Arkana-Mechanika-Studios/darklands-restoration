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
          --language-pack <folder>   a translation pack (docs/LOCALISATION.md)
          --translator-template <folder>    write the translator's template of your game there, then stop
          --check-language-pack <folder>    list what is wrong with a translation pack, then stop
          --pseudo-language-pack <folder>   write the pseudo-locale pack (longer texts) there and list what does not fit, then stop
          --edition-pack <folder> --from-edition <game folder> [--edition-language de] [--edition-variables <file.ftl>]
                                            write a pack of another edition's card texts there (yours, not to share) and check it
          --text-boxes <file or folder>     the texts' boxes for the two above (docs/LOCALISATION.md)
          --strip-text-boxes <file>         (developers) write the boxes of --text-boxes without the game's words, then stop
          --soundfont <file>         the SoundFont for the pack's .mid music (default: the first .sf2 in SOUNDFONT next to the program)
          --hd-pack <folder>         replace the pictures
          --mod <folder or name>     play a mod; once per mod (docs/MODS.md)
          --scale <N> | --resolution <WxH>    the window's size
          --fullscreen               full screen
          --clean-drawing            the cleaner drawing of 0.9.5
          --filter <name>            how the picture is drawn: sharp (default), even, soft, softcrt, hq, crt, crtplus
          --no-banner | --no-intro   skip the start-up screen | the opening animation
          --saves <folder>           where the saved games go
          --screenshots <folder>     where the screenshots go
          --text-font <file>         the text screen's 8x16 font
          --sdl <file>               the SDL3 library to use
          --no-console               no developer console (F11)
          --no-mouse-wheel           the mouse wheel does not give up / down in the menus and lists
          --no-card-menu-keys        at a card menu, up / down only with the pointer over a line (as the original)
          --log-window               a window with the program's messages (Windows; they always go to the log)
          --sound-test               check the sound without the game, then stop
          --version | --help         the version | this text
        """;

    /// <summary>The first leftover argument that looks like an option, or null when there is none.</summary>
    public static string? Unknown(IEnumerable<string> leftover) =>
        leftover.FirstOrDefault(a => a.StartsWith('-'));
}
