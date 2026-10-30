using System.Text;
using Darklands.Content;
using Darklands.Content.Formats.Config;
using Darklands.Platform;

namespace Darklands.Presentation;

/// <summary>What the program start leaves to the game.</summary>
/// <param name="NoAnimations">The command line's Q: no opening, ending or death animations.</param>
/// <param name="SoundDriver">The sound driver's file name as the game builds it ("psound.dlc"; "Asound.dlc" for -AA).</param>
public sealed record StartUpResult(bool NoAnimations, string SoundDriver)
{
    /// <summary>The opening's speech and effects (OPENDARK.DGT) are loaded for these drivers only.</summary>
    // orig: root 03A1:000A matched: `if (DS:A876 == 'a' || DS:A876 == 'p')` before the opening
    // C: a lower-case compare: a driver chosen with -A gets an upper-case letter (the command line is upper-cased) and no samples.
    public bool LoadsOpeningSamples => SoundDriver.Length > 0 && SoundDriver[0] is 'a' or 'p';
}

/// <summary>
/// The program start, from DOS to the opening: the BANNER.DAT banner on the text screen while the
/// configuration (CONFIG.DRK, the command line) is read and the memory checked, three to four
/// seconds of it, then the graphics screen and the drivers. Every check the original makes on the
/// way stops the program with the original's message, which stays on the screen.
/// </summary>
// orig: R000+0x0022 (main) draft, bytes match: the display check R000+0x13C8 matched; the overlay misc.exe (root 047C:07CE draft);
// DS:907A = 0; BANNER.DAT read whole (root 0617:011E draft, bytes match); the EMS check (root 05F8:0064 asm); R000+0x0114 draft
// (bytes match); the Ctrl-Break vector (root 044A:0100 asm); R000+0x052A draft (unmatched: register noise) up to its call of root
// 03A1:000A; tests: StartUpTests
// C: the copy-protection question (R041) is not part of the program start: the original asks it on the first visit to the map
// (DarklandsGame.Travel), where this rewrite leaves the question out and makes its five random draws.
// DORMANT: R000+0x00B8 draft (bytes match), the developers' menu "1) Tactical Battle code / 2) Role-Playing code / Select:"
// (BANNER.DAT entry 0 twelve times from row 12, entries 11..13 at rows 12..14): nothing calls it. With DS:907A == 1 (main clears it)
// R000+0x052A would run a test battle instead of the game (mode 0, battlefield 10, or the values at DS:0BF6..).
// DORMANT: BANNER.DAT entry 14, "Insufficient free disk space - 512K required.": nothing prints it.
public sealed class StartUp(GameUi ui, GameFiles files, IReadOnlyList<string> commandLine)
{
    /// <summary>The banner stays while the clock's whole seconds advance by at most this.</summary>
    // orig: the end of R000+0x1150: `t0 = time(0); while (time(0) - t0 <= 3) ;` - a busy wait of 3 to 4 s that reads no key
    private const int BannerSeconds = 3;

    /// <summary>633K: what DOSBox leaves free in a 640K PC.</summary>
    // C: the free memory of the machine the figures of the -O report describe.
    private const int ConventionalMemoryFree = 633 * 1024;

    private readonly TextScreen _text = new();
    private TextConsole? _console;
    private Banner? _banner;
    private bool _graphicsMode;
    private int _graphicsRow, _graphicsColumn;

    private TextConsole Console => _console ??= new TextConsole(_text);

    /// <summary>The text screen as the program start left it (for tests and screenshots).</summary>
    public TextScreen TextScreen => _text;

    /// <summary>The program start; null when it stopped the program (its message was shown until a key).</summary>
    public StartUpResult? Run()
    {
        // orig: R000+0x13C8 matched: the BIOS display state, kept to restore the text mode at the end (no screen output)
        // orig: the overlay misc.exe through root 047C:07CE and its slot table (root 047C:08C8): the timer and mouse code
        if (!LoadOverlay("misc.exe", null))
        {
            return null;
        }
        // orig: root 0617:011E("banner.dat") into DS:A895: the twenty offsets and their strings
        if (!LoadFile("banner.dat", out var bannerBytes))
        {
            return null;
        }
        _banner = new BannerDecoder().Decode(bannerBytes);
        // orig: root 05F8:0064 asm: the EMS check (the int 67h vector's "EMMXXXX0"), its result in DS:907D
        // DIVERGENCE: the EMS check always passes: there is no expanded memory to lack. DORMANT: "Unable to find EMS memory\n" and exit(0); "Darklands requires at least 176K of EMS memory free." (BANNER.DAT
        // entry 19) when fewer than 11 pages are free (R000+0x1150); the EMS error texts of R000+0x0000.
        if (StartupConfig() is not { } config || !MemoryCheck(config.Report, config.Driver))
        {
            return null;
        }
        // orig: root 044A:0100 asm: int 1Bh (Ctrl-Break) to the game's handler. DIVERGENCE: no Ctrl-Break here.
        return GameStart(config.Driver) ? new StartUpResult(config.NoAnimations, config.Driver) : null;
    }

    /// <summary>The banner, CONFIG.DRK and the command line.</summary>
    // orig: R000+0x0114 (0E76:0114) draft, bytes match, checked against asm (the CONFIG.DRK switch)
    private (bool NoAnimations, bool Report, string Driver)? StartupConfig()
    {
        // orig: int 10h AX = 3; the string writer (root 044A:00A9) at (0, 0) with FF 7F (the colour: bright white on light grey), then
        // BANNER.DAT entries 1..10 at rows 1..10, column 0 (80 characters each: the double frame and its four lines)
        Console.SetMode();
        Console.Write(0, 0, [0xFF, 0x7F]);
        for (var row = 1; row <= 10; row++)
        {
            Console.Write(row, 0, Entry(row));
        }
        // orig: fopen("config.drk", "rb"): missing, the driver is "nsound.dlc"; else fread(DS:A86E, 8, 1, f) (card, port, IRQ, DMA)
        // and the card's letter + "sound.dlc"
        // C: a shorter file leaves the rest of the eight bytes as they were (zero at the start of the program).
        var driver = "nsound.dlc";
        if (files.Exists("config.drk"))
        {
            var config = new byte[8];
            var bytes = files.Read("config.drk");
            bytes.AsSpan(0, Math.Min(8, bytes.Length)).CopyTo(config);
            driver = StartupConfigDecoder.DriverLetter(config[0] | (config[1] << 8)) + "sound.dlc";
        }
        // orig: root 044A:000A asm on every argument (the program's name too): a..z to upper case, in place
        var arguments = commandLine.Select(UpperCase).ToArray();
        Console.SetCursor(0x16, 0);
        var bad = false;
        var options = new string[arguments.Length];
        // orig: the first loop: a leading '-' or '/' is stepped over; anything else is "Unknown argument: <it>\r\n$" (DOS function 9)
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].StartsWith('-') || arguments[i].StartsWith('/'))
            {
                options[i] = arguments[i][1..];
            }
            else
            {
                Console.DosPrint(Ascii("Unknown argument: " + arguments[i] + "\r\n$"));
                bad = true;
            }
        }
        if (bad)
        {
            return Stop();
        }
        var noAnimations = false;
        var report = false;
        // orig: the second loop: switch on the option's letter; its messages show the argument with its '-' or '/'
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (options[i].Length > 0 ? options[i][0] : '\0')
            {
                case 'A':
                    // orig: the next character + "sound.dlc"; fopen(name, "r"), else "Sound driver not found: <argument>\r\n$"; else
                    // it is the driver (copied to DS:A876)
                    // C: "-A" alone gives the name "sound.dlc" (the character taken is the string's NUL).
                    var name = (options[i].Length > 1 ? options[i][1].ToString() : "") + "sound.dlc";
                    if (!files.Exists(name))
                    {
                        Console.DosPrint(Ascii("Sound driver not found: " + arguments[i] + "\r\n$"));
                        bad = true;
                    }
                    else
                    {
                        driver = name;
                    }
                    break;
                case 'O':
                    // orig: DS:0BF4 = 1: show the memory figures, and go on when there is too little
                    report = true;
                    break;
                case 'Q':
                    // orig: DS:0BF2 = 1: no opening, ending or death animation
                    noAnimations = true;
                    break;
                default:
                    Console.DosPrint(Ascii("Unknown argument: " + arguments[i] + "\r\n$"));
                    bad = true;
                    break;
            }
        }
        if (bad)
        {
            return Stop();
        }
        return (noAnimations, report, driver);

        (bool, bool, string)? Stop()
        {
            // orig: exit(0) after the loop: the messages stay under the banner and DOS comes back
            HoldText();
            return null;
        }
    }

    /// <summary>The memory check under the banner, then the banner's wait.</summary>
    // orig: R000+0x1150 (0E76:1150) draft, bytes match: the first 0x42 bytes of DGROUP saved to dk.tmp; the EMS pages (fewer than 11:
    // entry 19 and exit); the conventional memory: needed = the program's own block + 0x4B6B0 + the driver's size by its letter,
    // available = the owned blocks from the program's on + the largest free block; at (14, 0): with too little memory or -O the three
    // figures (entries 15, 16, 17); too little: with -O entry 18 and on, else exit(0); then the wait of 3 to 4 seconds
    // DIVERGENCE (host): there is no DOS memory here: the program's block is its EXE's load size (header) plus its minimum allocation and the
    // PSP, the free memory a constant; the check always passes, the -O figures are an estimate. dk.tmp (the first 0x42 bytes of DGROUP, saved at start-up and put back by exit-to-system before exit, the C runtime's copyright string, which its exit code compares to detect a null-pointer write) is not written.
    private bool MemoryCheck(bool report, string driver)
    {
        var needed = ProgramBlockBytes() + 0x4B6B0 + DriverBytes(driver[0]);
        var have = (long)ConventionalMemoryFree;
        Console.SetCursor(14, 0);
        if (have - needed < 0 || report)
        {
            Console.Printf(Format(Entry(15), needed, needed / 1024));
            Console.Printf(Format(Entry(16), have, have / 1024));
            Console.Printf(Format(Entry(17), needed - have, (needed - have) / 1024));
        }
        if (have - needed < 0)
        {
            if (!report)
            {
                HoldText();
                return false;
            }
            Console.Printf(Format(Entry(18)));
        }
        var start = ui.Clock.Milliseconds / 1000;
        // C: a busy wait: keys pressed now stay in the keyboard buffer (and one stops the opening at once).
        while (ui.Clock.Milliseconds / 1000 - start <= BannerSeconds)
        {
            ui.PresentText(_text);
            ui.Clock.Sleep(16);
        }
        return true;
    }

    /// <summary>The program's own memory block: PSP, load module and minimum allocation from the EXE header.</summary>
    // C: the C start-up code may shrink or grow the block; the header's figures are taken as it is.
    private long ProgramBlockBytes()
    {
        if (!files.Exists("DARKLAND.EXE"))
        {
            return 0;
        }
        var header = files.Read("DARKLAND.EXE")[..0x20];
        int Word(int at) => header[at] | (header[at + 1] << 8);
        var lastPage = Word(2);
        var load = (Word(4) - 1) * 512L + (lastPage == 0 ? 512 : lastPage) - Word(8) * 16L;
        return (0x100 + load + Word(0x0A) * 16L + 15) & ~15L;
    }

    // orig: the switch on the driver's first letter in R000+0x1150: A/a 0x2C90, I/i 0x2FC0, N/n 0x3B0, P/p 0x290C, R/r 0x1C60
    private static long DriverBytes(char letter) => char.ToLowerInvariant(letter) switch
    {
        'a' => 0x2C90,
        'i' => 0x2FC0,
        'n' => 0x3B0,
        'p' => 0x290C,
        'r' => 0x1C60,
        _ => 0,
    };

    /// <summary>The game start up to the main loop: the graphics driver, the graphics screen, COMMONSP.IMG, the sound driver.</summary>
    // orig: R000+0x052A (0E76:052A) draft (unmatched: register noise): the overlay mgraphic.exe with fonts.fnt; the driver's mode 13h
    // (the screen goes black); the EMS work pages; the swap store; commonsp.img (R000+0x1010); the mouse pointer; the sound driver
    // (DS:A876) loaded and started with CONFIG.DRK's port, IRQ and DMA (root 047C:0783), the timer (root 047C:0066); difficulty 1;
    // the battle menus (R000+0x083E); the keyboard handler (root 044A:019C); sound number 7; the battlefield tables; the party
    // emptied; then root 03A1:000A for ever
    // C: sound number 7 is played by the audio itself when it loads its driver (SynthAudio).
    private bool GameStart(string driver)
    {
        if (!LoadOverlay("mgraphic.exe", "fonts.fnt"))
        {
            return false;
        }
        // the graphics screen: black, the default palette until the first picture sets its own
        // UNCERTAIN: the driver's mouse pointer (shown after commonsp.img) is left out until the start screen, as during the opening.
        _graphicsMode = true;
        _graphicsRow = _graphicsColumn = 0;
        ui.Canvas.UsePalette(Content.Formats.Graphics.Palette256.DefaultVga);
        ui.Canvas.Clear(0);
        var pointer = ui.PointerVisible;
        ui.PointerVisible = false;
        try
        {
            ui.Present();
            // orig: commonsp.img read whole in R000+0x1010
            // orig: the overlay DS:A876 (root 047C:07CE): a missing driver ends the program with the loader's message, which DOS
            // prints on the graphics screen
            return LoadFile("commonsp.img", out _) && LoadOverlay(driver, null);
        }
        finally
        {
            ui.PointerVisible = pointer;
        }
    }

    /// <summary>The checks of the main loop function between the opening and the start screen; false when one stopped the program.</summary>
    // orig: root 03A1:000A matched, after the opening: srand(time(0)); the catalogue "msgfiles" (root 0617:06D8 draft, bytes match:
    // missing, it only returns 0); the world "darkland.loc" (R043+0x0100 matched); the picture pics\Illmcaps.pic and nine pictures
    // (root 0617:0230 and 045C, draft): a file that does not open ends in root 0617:067E draft (mode 3, perror(name), exit(99))
    // DIVERGENCE: the files are only looked for here; the game reads them when it needs them.
    public bool LoadGameData()
    {
        // orig: R043+0x0100 matched: _dos_findfirst of each file, else printf("Could not open %s file\n", ...) on the graphics screen
        // and exit(1): darkland.lst "list", darkland.cty "city", the location file "location", darkland.dsc "city description"
        foreach (var (name, what) in new[] { ("darkland.lst", "list"), ("darkland.cty", "city"), ("darkland.loc", "location"), ("darkland.dsc", "city description") })
        {
            if (!files.Exists(name))
            {
                GraphicsPrint($"Could not open {what} file\n");
                HoldGraphics();
                return false;
            }
        }
        foreach (var name in new[] { "pics\\Illmcaps.pic", "pics\\rpbdrtop.pic", "pics\\rpbdrbtm.pic", "pics\\rpbdrlft.pic", "pics\\rpbdrrgt.pic",
                     "pics\\timer01.pic", "pics\\timer03.pic", "pics\\timer04.pic", "pics\\timer05.pic", "pics\\textback.pic" })
        {
            if (!files.Exists(name))
            {
                return LoadFile(name, out _);
            }
        }
        return true;
    }

    /// <summary>Before the opening: its samples are missing and the driver wants them: the program stops.</summary>
    // orig: R043+0x09C8 matched (the sound file loader): `if (_dos_findfirst(name, 0, &ft) != 0) { printf("snd"); exit(1); }`
    public bool CheckOpeningSamples(StartUpResult started)
    {
        if (!started.LoadsOpeningSamples || files.Exists("opendark.dgt"))
        {
            return true;
        }
        _graphicsMode = true;
        GraphicsPrint("snd");
        HoldGraphics();
        return false;
    }

    /// <summary>Loads an overlay (a driver); a missing file prints the loader's message and stops the program.</summary>
    // orig: root 047C:07CE draft (unmatched): DOS 4B03h; on failure the error code as a hex digit (cmp al, 0Ah / sbb al, 69h / das),
    // " - " and the message (DOS function 9), then int 21h 4C99h; the second file (the graphics driver's font) is read with
    // 3D00h/3Fh; DOS error 2 of either: "Filename not found" (DS:6AE1)
    // DORMANT: the loader's other messages (DS:6A90 "Allocated 1Mb of space????", 6AAB "MS-DOS lied to us about how much memory was
    // available", 6AF4 "Not enough memory to load file", 6B13 "Overlay load failed for some reason", 6B37 "Overlay has overrun
    // allocated memory", 6B5C "Unable to shrink overlay memory allocation"): there is no DOS memory to fail.
    private bool LoadOverlay(string name, string? second)
    {
        if (files.Exists(name) && (second is null || files.Exists(second)))
        {
            return true;
        }
        const string message = "2 - Filename not found";
        if (_graphicsMode)
        {
            GraphicsPrint(message);
            HoldGraphics();
        }
        else
        {
            Console.DosPrint(Ascii(message + "$"));
            HoldText();
        }
        return false;
    }

    /// <summary>Reads a data file; a missing one stops the program with the C library's message on a cleared text screen.</summary>
    // orig: root 0617:000E (the file opener of root 0617:011E): a failed open calls root 0617:067E draft: int 10h mode 3 (the screen
    // is cleared), perror(name) (root 06A1:1B7C asm: "<name>: No such file or directory\n" for ENOENT), exit(99)
    private bool LoadFile(string name, out byte[] bytes)
    {
        if (files.Exists(name))
        {
            bytes = files.Read(name);
            return true;
        }
        bytes = [];
        _graphicsMode = false;
        Console.SetMode();
        Console.Printf(name + ": No such file or directory\n");
        HoldText();
        return false;
    }

    // DIVERGENCE: the original returns to DOS with its message on the screen; here the screen stays until a key, a click or the
    // window is closed.
    private void HoldText()
    {
        ui.FlushKeys();
        while (true)
        {
            ui.PresentText(_text);
            if (ui.TryReadKey(out _) || ui.Mouse.Buttons != MouseButtons.None)
            {
                return;
            }
            ui.Clock.Sleep(16);
        }
    }

    private void HoldGraphics()
    {
        ui.FlushKeys();
        var pointer = ui.PointerVisible;
        ui.PointerVisible = false;
        try
        {
            while (true)
            {
                ui.Present();
                if (ui.TryReadKey(out _) || ui.Mouse.Buttons != MouseButtons.None)
                {
                    return;
                }
                ui.Clock.Sleep(16);
            }
        }
        finally
        {
            ui.PointerVisible = pointer;
        }
    }

    /// <summary>DOS (or printf) writing on the graphics screen: the BIOS teletype, 40×25 cells of 8×8 dots from the top left.</summary>
    // C: colour 7 (DOS's console output passes none; DOSBox's int 29h uses 7) and the BIOS's 8×8 font (TextFont.SmallRow).
    private void GraphicsPrint(string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '\n':
                    _graphicsColumn = 0;
                    _graphicsRow = Math.Min(24, _graphicsRow + 1);
                    continue;
                case '\r':
                    _graphicsColumn = 0;
                    continue;
            }
            for (var y = 0; y < 8; y++)
            {
                var bits = TextFont.SmallRow((byte)c, y);
                for (var x = 0; x < 8; x++)
                {
                    ui.Canvas[_graphicsColumn * 8 + x, _graphicsRow * 8 + y] = (bits & (0x80 >> x)) != 0 ? (byte)7 : (byte)0;
                }
            }
            if (++_graphicsColumn == 40)
            {
                _graphicsColumn = 0;
                _graphicsRow = Math.Min(24, _graphicsRow + 1);
            }
        }
    }

    private byte[] Entry(int index) => [.. _banner!.Entries[index].Bytes];

    /// <summary>printf with a BANNER.DAT entry as the format: %ld takes a long, %d the low word of the next long.</summary>
    // C: printf(entry, needed, needed / 1024): both arguments are longs; %d reads the low word of the second as an int.
    private static string Format(byte[] format, params long[] values)
    {
        var text = new StringBuilder();
        var next = 0;
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                if (format[i + 1] == 'l' && i + 2 < format.Length && format[i + 2] == 'd')
                {
                    text.Append(values[next++]);
                    i += 2;
                    continue;
                }
                if (format[i + 1] == 'd')
                {
                    text.Append((short)values[next++]);
                    i++;
                    continue;
                }
            }
            text.Append((char)format[i]);
        }
        return text.ToString();
    }

    private static string UpperCase(string argument) =>
        string.Concat(argument.Select(c => c is >= 'a' and <= 'z' ? (char)(c - 0x20) : c));

    private static byte[] Ascii(string text) => [.. text.Select(c => c < 0x100 ? (byte)c : (byte)'?')];
}
