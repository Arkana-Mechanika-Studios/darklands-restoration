using Darklands.Content;
using Darklands.Platform;
using Darklands.Presentation.Commands;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation;

/// <summary>The player asked to leave the game (window closed or Alt-Q confirmed).</summary>
// orig: exit-to-system root 0000:13A0 draft (matches the bytes in the file's matching group): after "Quit to DOS? y/n" -> yes: shutdown, delete the temporary save "dk.tmp", exit(0); also quit-check root 0000:1338 when the exit flag is set
// DIVERGENCE: the game leaves through an exception that unwinds to DarklandsGame.Run (nothing to shut down on a PC host, no dk.tmp)
public sealed class QuitGameException : Exception
{
    public QuitGameException() : base("The player quit the game.") { }
}

/// <summary>The program ends where the original ends it with a message (a file it cannot open: exit 99 with perror's line).</summary>
// orig: root 0617:000E (the file opener of the loaders root 0617:011E and 0617:045C): a failed open calls root 0617:067E: int 10h mode 3
//   (the screen cleared), perror(name) (root 06A1:1B7C: "<name>: No such file or directory\n" for ENOENT), exit(99)
// C: DarklandsGame.Run shows <see cref="Printed"/> on the cleared text screen (StartUp.Exit) and the program ends.
public sealed class ProgramExitException(string printed, int code = 99) : Exception("The program ended: " + printed.TrimEnd())
{
    /// <summary>What the original printed on the text screen before it exited.</summary>
    public string Printed { get; } = printed;

    /// <summary>The exit code.</summary>
    public int Code { get; } = code;

    /// <summary>A file the original's file opener could not open: perror's "name: No such file or directory".</summary>
    public static ProgramExitException FileNotFound(string name) => new(name + ": No such file or directory\n");
}

/// <summary>Nobody is left in the party: the death film has been shown and the game is over.</summary>
// orig: hours-elapse R029+0x255A and battle-driver R029+0x2278 end the program with exit_ after the death film
// DIVERGENCE: the original exits to DOS (exit_) after the death film; here the player comes back to the start screen, which stands for a
// new run of the program (DarklandsGame.RestartProgram: the program's globals go back to their start values).
public sealed class GameOverException : Exception
{
    public GameOverException() : base("The party has perished.") { }
}

/// <summary>One thing the player did: a key press, or a mouse click at a screen position.</summary>
// orig: key-waiting-test()/read-key() of the graphics driver (root 047C:073D / root 047C:0744, root 047C:073D and 047C:0744, asm, BIOS scan code in the high byte and the character in the low) and the mouse driver's click record (struct mouse { f0, click, x, y } returned by root 047C:1320(6, 0, 0, 0)); status asm
// DIVERGENCE: the original polls: a loop tests key-waiting-test(), mouse-button-state (bit 1 left, 2 right, held) and the click counter itself; this record is one event of an edge-triggered queue, so a button held across screens produces one PlayerAction, not many.
public readonly record struct PlayerAction(int Key, int X, int Y, MouseButtons Click)
{
    public bool IsKey => Key != Keys.None;

    public bool IsLeftClick => (Click & MouseButtons.Left) != 0;

    public bool IsRightClick => (Click & MouseButtons.Right) != 0;
}

/// <summary>
/// The screen and the player, for code that runs step by step like the original: draw,
/// then wait for the player. Every wait keeps the window responsive and shows the pointer.
/// </summary>
// orig: the screen/key/mouse/timing calls the C screens use directly: gfx-* (root 047c, asm), key-waiting-test/read-key, menu-bar-loop root 0000:0000 (which calls hotkey-table on every poll, see HotKeys), wait-ticks R030+0x3096 (1767:0046) matched; tests: none (needs a host)
// DIVERGENCE: waits sleep 16 ms per frame instead of spinning; the pointer (the COMMONSP.IMG gauntlet) is drawn into a copy of the screen (the original draws the mouse cursor through the driver); F12 saves a screenshot (host feature).
public sealed class GameUi
{
    private const int FrameMilliseconds = 16;

    private readonly IHost _host;
    private readonly Screen _screen = new();
    private readonly Screen _shown = new();
    private MouseButtons _previousButtons;
    private bool _leftWasDown;
    private int _leftReleases;
    private int _lastReleaseX;
    private int _lastReleaseY;
    // the left releases counted since the start, never reset (TakeLeftReleases resets _leftReleases)
    private int _leftReleasesSeen;

    public GameUi(IHost host, GameAssets assets)
    {
        _host = host;
        Assets = assets;
        Canvas = new Canvas(_screen);
        // C: 16..255 keep the VGA mode's colours until a picture sets them; 0..15 are the game's start-up colours
        //   (set here too so screens shown without the start-up sequence, e.g. the headless host's, have them).
        Canvas.UsePalette(Content.Formats.Graphics.Palette256.DefaultVga);
        UseStartupColors();
    }

    /// <summary>Sets colours 0..15 to the game's start-up colours (colour 5 black, 6 brown); pictures only define 16..255.</summary>
    // orig: root 03A1:000A, right after the opening: the driver's EGA palette entry (root thunk 047C:0584) with the list at DS:01C0
    //   (see Palette.StartupColors); the graphics mode switch and the opening's animations had set all 256 colours before.
    public void UseStartupColors()
    {
        for (var i = 0; i < Palette.StartupColors.Count; i++)
        {
            Canvas.SetColor(i, Palette.StartupColors[i]);
        }
    }

    /// <summary>
    /// The interface's program globals back at their start values, as a new run of the program finds them (the start screen shown again
    /// after a game, DarklandsGame.RestartProgram): the options on, the VGA colours with the start-up colours, the card window's font, no
    /// picture page, no kept piece of music, the palette cycles held and empty, the barter listing, the panel's selection and order mode
    /// cleared, the mouse driver's release count at 0. What a host set (its callbacks, the clean drawing, the keyboard menus, the
    /// screenshot folder, its command queue) stays.
    /// </summary>
    // orig: DS:01BE / DS:01BF (music-setting, sound-setting) are 1 in the EXE's data image; DS:9C3D, DS:86A2, DS:9064, DS:A6B3 and DS:9C46
    //   are BSS (0 at start-up; DS:A6B2 chosen-member is set before it is read); the card window's font byte is 3 in the image (DS:016A+0x10);
    //   the mode switch of R000+0x052A sets the VGA mode's colours and root 03A1:000A the start-up colours 0..15; the mouse driver's reset
    //   (INT 33h AX = 0) at start-up clears its release counts; the palette cycle slots: PaletteCycles.Reset
    public void ProgramStart()
    {
        Options.SoundEffects = true;
        Options.Music = true;
        CardWindowFont = 3;
        PicturePage = null;
        _held = null;
        SavedMusic.Value = 0;
        Cycles.Reset();
        Array.Clear(BarterListing);
        Array.Clear(PanelSelection);
        PanelOrderMode = (false, -1);
        PartyIconsDirty = false;
        _leftReleases = 0;
        _lastReleaseX = _lastReleaseY = 0;
        Canvas.UsePalette(Content.Formats.Graphics.Palette256.DefaultVga);
        UseStartupColors();
    }

    public Canvas Canvas { get; }

    /// <summary>
    /// The canvas of what the display shows: the screen held by <see cref="HoldScreen"/> while a card's picture waits on the off-screen page,
    /// else <see cref="Canvas"/>. A screen that draws straight into video memory while the picture is held (the hourglass) draws here.
    /// </summary>
    // orig: card-run R043+0x0A9A draws the picture on page 1 (DS:EE55 set); the hourglass R043+0x3494 draws into video memory, over the previous card's screen
    public Canvas ShownCanvas => _held is null ? Canvas : new Canvas(_held);

    public GameAssets Assets { get; }

    /// <summary>The font of slot 1 (font 0 of FONTS.FNT, 6 pixels high): the side window, the screen pages, the party panel's pages, the menu bar.</summary>
    // orig: the window records in DGROUP (page, x, y, last column, last row, mode, colour, background, font at +0x10): DS:00F2 (the
    //   side window, pointer DS:0104), DS:0106 (DS:0118), DS:0156 (DS:0168, the panel's page), DS:017E (DS:0190), DS:0192 (DS:01A4)
    //   have font 1; the menu bar loop root 0000:0000 sets the card window's font to 1 while the bar runs. checked against the DS bytes
    // C: read on first use: the program start checks FONTS.FNT where the original loads it (with the graphics driver).
    public TextPainter Text => _text ??= new TextPainter(Assets.Font(1));

    private TextPainter? _text;

    /// <summary>The font of slot 3 (font 2 of FONTS.FNT, 7 pixels high): the card window (card texts, the hourglass, the message boxes).</summary>
    // orig: the card window (pointer DS:017C -> the record DS:016A: page 0, x 0x3C, last column 0x103, colours 15 / 7, font 3);
    //   DS:012E and DS:0142 (pointer DS:0154) have font 3 too; the load-game screen R037+0x8B76 (1C7D:09C6) sets the card window's font
    //   to 3 again. The driver's font slot n is font n - 1 of FONTS.FNT (drv+0x13A4: the slot indexes the offset table at the start of
    //   the file the loader root 047C:07CE reads behind the driver). checked against asm and the DS bytes; tests: tools/visuals/compare.py
    public TextPainter CardText => _cardText ??= new TextPainter(Assets.Font(3));

    private TextPainter? _cardText;

    /// <summary>The card window's font slot as the boxes drawn in it take it: 3, but 1 while the menu bar's loop runs (its hot keys included).</summary>
    // orig: root 0000:0006..0x001E: menu-bar-loop keeps the card window's colour and font (DS:017C -> +0x0C, +0x10) and sets the font to 1;
    //   root 0000:0029..0x0033 runs hotkey-table (root 0000:0CAA) for a waiting key with that font; root 0000:014C..0x0158 and 0x019D..0x01A9
    //   put both back before calling a chosen item or sub-item, root 0000:01BF..0x01C5 at the end. So a box of a hot key (Alt-S's "Saving
    //   Game", Alt-Q's question, Alt-P, the Alt-D / F / M / C messages) is in font 1, the same box from the menu in font 3. game-load-entry
    //   R037+0x8B76 sets the font to 3 first. checked against asm and the original's screen (vis10 session D-b797f7, local/batch/batpal)
    public int CardWindowFont { get; set; } = 3;

    /// <summary>The painter of <see cref="CardWindowFont"/>.</summary>
    public TextPainter CardWindowText => CardWindowFont switch
    {
        1 => Text,
        3 => CardText,
        _ => new TextPainter(Assets.Font(CardWindowFont)),
    };

    /// <summary>Runs <paramref name="run"/> as menu-bar-loop runs its part: with the card window's font set to 1, put back after.</summary>
    public T InMenuBarLoop<T>(Func<T> run)
    {
        var font = CardWindowFont;
        CardWindowFont = 1;
        try
        {
            return run();
        }
        finally
        {
            CardWindowFont = font;
        }
    }

    public IAudio Audio => _host.Audio;

    /// <summary>The host's clock (real-time screens such as the battle).</summary>
    public IClock Clock => _host.Clock;

    /// <summary>The game's timer: the clock of the music and the animations.</summary>
    public IGameTimer Timer => _host.Timer;

    /// <summary>The palette cycles the timer's frame work runs (the battle's water and fire, the enemies' schemes).</summary>
    public PaletteCycles Cycles { get; } = new();

    /// <summary>The timer frame up to which the cycles have run (while they run).</summary>
    private long _cycleFrame;

    private readonly byte[] _dac = new byte[768];

    /// <summary>Takes the colours <paramref name="first"/> .. <paramref name="last"/> of the palette as shown now into cycle slot <paramref name="slot"/>.</summary>
    // orig: root 047C:0362 reads them from the VGA DAC (port 3C7/3C9): the palette last set, which is the screen's palette here (6 bits a channel)
    public void TakeCycle(int slot, int reload, int first, int last)
    {
        for (var i = 0; i < 256; i++)
        {
            var argb = _screen.Palette[i];
            _dac[3 * i] = (byte)((argb >> 18) & 0x3F);
            _dac[3 * i + 1] = (byte)((argb >> 10) & 0x3F);
            _dac[3 * i + 2] = (byte)((argb >> 2) & 0x3F);
        }
        Cycles.Take(slot, reload, first, last, _dac);
    }

    /// <summary>Lets the timer's frame work run the cycles (DS:07D8 = 1) or holds them (0).</summary>
    // C: the frames are counted on the game timer from here: the phase of the first step against the timer's frames is within one frame
    public void RunCycles(bool run)
    {
        Cycles.Running = run;
        _cycleFrame = Timer.Frames;
    }

    /// <summary>The palette was set: the DAC shows it until each cycle's next step.</summary>
    // C: palette-load (root 0410:0364) writes all 768 bytes of the DAC (holding DS:07D8 meanwhile); a cycle writes its range at its next step
    public void PaletteSet() => Cycles.PaletteSet();

    // orig: root 047C:000A: `cmp byte [7D8h], 0 / je / lcall 047C:042A` on every frame of the timer (70.086 Hz), the frame left out of the
    //   60 Hz ticks included; the ranges the cycles wrote are what the DAC shows
    // C: the frames that passed since the last present are run here, at once (the DAC's writes between two presents are not seen anyway)
    // DIVERGENCE: the steps come on the host's timer frames (IGameTimer.Frames; a timer that counts only the 60 Hz ticks gives 7 frames per 6
    //   ticks), counted from the call that let the cycles run: the original's interrupt comes in the middle of whatever the game was doing,
    //   so a step can come up to a frame (14 ms) earlier or later here. Every step, its colours and the frames between steps are the
    //   original's (PaletteCycleTests against direct calls of 047C:0362/03DA/040B/042A). Only the battle's palette sets and the load
    //   screen (which stops the cycles first, as the original) drop the shown steps (PaletteSet).
    private void ApplyCycles(uint[] palette)
    {
        if (Cycles.Running)
        {
            var frames = Timer.Frames;
            for (var n = Math.Min(frames - _cycleFrame, 4096); n > 0; n--)
            {
                Cycles.Frame(_dac);
            }
            _cycleFrame = frames;
        }
        for (var slot = 1; slot <= PaletteCycles.Slots; slot++)
        {
            if (Cycles.Written(slot) is not { } written)
            {
                continue;
            }
            var (first, colours) = written;
            var bytes = colours.Span;
            for (var i = 0; i + 2 < bytes.Length; i += 3)
            {
                palette[(first + i / 3) & 0xFF] = 0xFF000000u | (uint)Expand(bytes[i]) << 16 | (uint)Expand(bytes[i + 1]) << 8 | Expand(bytes[i + 2]);
            }
        }
    }

    private static byte Expand(byte value) => (byte)((value & 0x3F) << 2 | (value & 0x3F) >> 4);

    /// <summary>Receives a line for each step of a timed sequence (the animations' cels); hosts write it to a trace.</summary>
    public Action<string>? Trace { get; set; }

    /// <summary>
    /// A tester's aid, off in play (DARKLANDS_SOUND_LOG in the headless host): receives "music x" for each music-start(x) request, before the
    /// option makes of it what it does; the driver's own commands ("sound n") are logged by the host around <see cref="Audio"/>.
    /// tools/oracle/lockstep.py compares both with the original's music-start (R043+0x09AE) and sound-start (root 047C:078A) calls.
    /// </summary>
    public Action<string>? SoundLog { get; set; }

    /// <summary>Options toggled by the hot keys (sound effects, music).</summary>
    // orig: sound-setting / music-setting (DGROUP bytes 01BF / 01BE, saved with the game)
    public GameOptions Options { get; set; } = new();

    /// <summary>While a battle runs (DS:907A == 1): the order letter of a slot (root 0000:18FA), which the party panel of a
    /// screen over the battle (the loot screen, a sheet) shows too; null outside a battle.</summary>
    public Func<int, char?>? BattleStatusLetter { get; set; }

    /// <summary>While a battle runs: its order mode and chosen member as they stand (DS:A6B3 / DS:A6B2), for the party panel of a screen
    /// over the battle; null outside a battle (then <see cref="PanelOrderMode"/>, as the last battle left them).</summary>
    public Func<(bool On, int Member)>? BattleOrderMode { get; set; }

    /// <summary>Order mode as the party panel reads it: the running battle's, else as the last battle left it.</summary>
    public (bool On, int Member) CurrentOrderMode => BattleOrderMode?.Invoke() ?? PanelOrderMode;

    /// <summary>Plays a music cue unless music is off; then it silences instead.</summary>
    // orig: music-start = thunk 09C0:18F1 -> R043+0x09AE (1462:09AE) `if (music-setting) sound-start(x); else sound-start(1);` (sound-start(1) stops the music)
    public void PlayMusic(int tune)
    {
        SoundLog?.Invoke($"music {tune}");
        Audio.PlayMusic(Darklands.Audio.GameSounds.MusicNumber(tune, Options.Music));
    }

    /// <summary>Notes that a card is about to be shown, for a sound pack's music per card; -1 when music is off. Read-only: the game never reads it back.</summary>
    // New code (replacement sound, not part of the game): no original code; IAudio.CardShown does nothing without a pack
    public void CardShown(int card) => Audio.CardShown(Options.Music ? card : -1);

    /// <summary>music-start(5): the number of the piece playing (with music off the stop is played instead and its AX given).</summary>
    // orig: thunk 09C0:18F1 -> R043+0x09AE (1462:09AE) matched; its callers keep the AX the driver left (GameSounds.CurrentPiece)
    public int MusicPlaying()
    {
        SoundLog?.Invoke("music 5");
        return Darklands.Audio.GameSounds.RequestCurrentPiece(Audio, Options.Music);
    }

    /// <summary>The piece kept to be played back (the prayer screen, the camp, the map and the shell game share it).</summary>
    // orig: DS:9C3D (an int of the game's data, zero at start: past the data image's initialised bytes)
    public System.Runtime.CompilerServices.StrongBox<int> SavedMusic { get; } = new(0);

    /// <summary>DS:9C3D = music-start(5): the piece playing is kept.</summary>
    // orig: thunk 09C0:18F1(5) with its result stored to DS:9C3D, repeated inline in R034+0x0000, R034+0x0102, R036+0x0082, R030+0x00A8
    public void SaveMusic() => SavedMusic.Value = MusicPlaying();

    /// <summary>music-start(DS:9C3D): the kept piece again.</summary>
    // orig: thunk 09C0:18F1(DS:9C3D), repeated inline in R034+0x0000, R034+0x0102, R036+0x082C
    public void RestoreMusic() => PlayMusic(SavedMusic.Value);

    /// <summary>As <see cref="PlayMusic"/>, carried out at a tick of the game timer (the animations' tunes).</summary>
    public void PlayMusicAt(int tune, long tick)
    {
        SoundLog?.Invoke($"music {tune}");
        Audio.PlayMusicAt(Darklands.Audio.GameSounds.MusicNumber(tune, Options.Music), tick);
    }

    /// <summary>Redraws the current screen after a full screen (a character sheet, the party information) has been shown over it; the screen that is waiting sets this.</summary>
    // orig: the redraw that follows member-sheet-overlay-entry / party-info-overlay-entry in card-hotkeys, R043+0x1E30 and 0c44: R043+0x1E82 (card) or the label `redraw` of R030+0x0C44 (map)
    public Action? Redraw { get; set; }

    /// <summary>Throws away the keys waiting.</summary>
    // orig: key-buffer-clear root 0000:1384: `while (key-waiting-test()) read-key();`
    public void FlushKeys()
    {
        while (ReadHostKey(out _))
        {
        }
    }

    public MouseState Mouse => _host.Input.Mouse;

    public bool ShiftHeld => _host.Input.ShiftHeld;

    /// <summary>True while a Ctrl key is held (the BIOS keyboard flags' bit 2).</summary>
    public bool CtrlHeld => _host.Input.CtrlHeld;

    /// <summary>The mouse driver's function 4: the pointer is put at (x, y).</summary>
    // orig: root 047C:1266 (root 047C, INT 33h AX = 4)
    public void MovePointer(int x, int y) => _host.Input.MovePointer(x, y);

    public bool PointerVisible { get; set; } = true;

    /// <summary>
    /// False while the host's pointer is outside the game's window (IInput.PointerInWindow): the drawn pointers are left out of the picture.
    /// A host enhancement: the mouse state the game reads is not touched.
    /// </summary>
    public bool PointerInWindow => _host.Input.PointerInWindow;

    /// <summary>
    /// The hot spot of the pointer's shape (DS:6EF6 / DS:6EF8, pointer-shape-set's first two arguments): the gauntlet is drawn at the mouse
    /// less it. (0, 0) from start-up; the map sets (5, 0) and the screens below put (0, 0) back.
    /// </summary>
    // orig: pointer-shape-set root 047C:124F(hx, hy, shape) with the default shape DS:9C42: (0, 0) at start-up R000+0x0666; (5, 0) at
    //   world-map-start R030+0x0242 (each time the map is entered, never put back when it is left, so the cards, the camp and the screens
    //   opened from them keep it); (0, 0) by the character sheet (R031+0x0B0B in its drawing R031+0x0A82, R031+0x09A2, the clean-up
    //   R031+0x0924), the alchemy screen's exit R033+0x064D, the party information R034+0x0F2D, a battle's start and end (R002+0x02A0,
    //   R002+0x0514) and a load inside a battle (R037+0x8BBC, only with DS:907A == 1). The card engine (R043), the camp (R036), the inn,
    //   the shops and the load screen outside a battle set no shape. checked against asm (every call of root 047C:124F in the EXE) and the
    //   original's screen (vis10 session creation-map: the pointer 5 pixels left of the mouse on the map and in the camp); tests: PointerHotSpotTests
    public (int X, int Y) PointerHotSpot { get; set; }

    /// <summary>
    /// The pointer's shape when a screen set one of its own (pointer-shape-set's third argument), null: the start-up gauntlet. The battle
    /// sets its current shape for the screens a hot key opens over it, where the driver's pointer shows with the battle's last shape.
    /// </summary>
    // orig: pointer-shape-set root 047C:124F stores the shape the driver draws; the battle's pass sets its own (R014+0x33A2) and the
    //   hot keys' screens over a battle set none (R037+0x2ACE: pointer-hide, the "Saving Game" box, pointer-show at R037+0x2B97 while
    //   the file is written); the menu bar sets the battle's hand (root 0000:0000+0x00B6); every pointer-shape-set of the gauntlet
    //   (the map, the sheet, the party information, the alchemy's exit, the loot screen's R014+0xE598) sets it back to null
    public Content.Formats.Battles.RowRunSprite? PointerShape { get; set; }

    /// <summary>
    /// An interface convenience the original does not have (off unless a host turns it on), made of player commands (docs/COMMANDS.md):
    /// at a card menu, with the pointer off the lines, Up / Down put the pointer onto the line they name (<see cref="PointAt"/>), and the
    /// digits 1..9 choose the line of that number as the mouse does (<see cref="ChooseLine"/>; while an item or saint box is up, the
    /// box's lines). The key itself is dropped, as the original drops it (CardScreen.Select).
    /// </summary>
    public bool KeyboardCardMenus { get; set; }

    /// <summary>
    /// True while the screen up has a selection that the cursor keys ↑ / ↓ move (a card menu and its item or saint box, an open list or pack
    /// of the character sheet, the shop, barter and loot lists, the load list, the camp, the inn's roster, the alchemy list, the creation
    /// menus, the menu bar's pull-downs); null or false elsewhere (the map, a battle, a card's text, the sheet with no list open). A screen
    /// sets it for its loop with <see cref="UseSelectionList"/>, and a screen that opens over such a screen without a list of its own puts
    /// null there. The waits for any key (<see cref="PollKeyOnly"/>, <see cref="ReadKey"/>,
    /// <see cref="WaitForKey"/>: the message boxes, the party information) never have one.
    /// </summary>
    // Host comfort, not the original's: each present tells the host (IInput.SelectionListActive), which then turns the mouse wheel into ↑ / ↓
    //   presses (the SDL host's input.mouseWheel, as DOSBox-X maps the wheel to the cursor keys); the game reads them as a player's keys and
    //   nothing else changes. Hosts without a wheel (Host.Headless, the oracle tools) never give one; tests: MouseWheelListTests
    public Func<bool>? SelectionList { get; private set; }

    /// <summary>
    /// Makes <paramref name="active"/> the <see cref="SelectionList"/> until the scope returned is disposed (<c>using var _ = ...</c>), when the
    /// one before comes back; null for a screen without a list that opens over one with a list.
    /// </summary>
    public SelectionListScope UseSelectionList(Func<bool>? active)
    {
        var scope = new SelectionListScope(this, SelectionList);
        SelectionList = active;
        return scope;
    }

    /// <summary>A <see cref="UseSelectionList"/>: its disposal puts the screen's <see cref="SelectionList"/> before it back.</summary>
    public readonly struct SelectionListScope(GameUi ui, Func<bool>? below) : IDisposable
    {
        public void Dispose() => ui.SelectionList = below;
    }

    /// <summary>Above 0 inside a wait for any key: no selection list then.</summary>
    private int _anyKeyWait;

    /// <summary>What the host is told before each pump (<see cref="SelectionList"/>).</summary>
    private bool SelectionListActive => _anyKeyWait == 0 && SelectionList?.Invoke() == true;

    /// <summary>Game-wide keys (saving, the character sheets): returns true when it took the key.</summary>
    public Func<int, bool>? GameKey { get; set; }

    /// <summary>
    /// True while the card engine (its menus and press-to-continue) or the map waits for input: only these loops take F1..F5, F6 and
    /// Ctrl-F1..F5 (the original's card-hotkeys R043+0x33C6 and the cases of R030+0x0C44); HotKeys looks at it.
    /// </summary>
    public bool SheetKeys { get; set; }

    /// <summary>One <see cref="Poll"/> of a loop that takes F1..F5 and Ctrl-F1..F5 (<see cref="SheetKeys"/>).</summary>
    public PlayerAction? PollWithSheetKeys()
    {
        var keep = SheetKeys;
        SheetKeys = true;
        try
        {
            return Poll();
        }
        finally
        {
            SheetKeys = keep;
        }
    }

    /// <summary><see cref="WaitForAction"/> in a loop that takes F1..F5 and Ctrl-F1..F5 (<see cref="SheetKeys"/>).</summary>
    public PlayerAction WaitForActionWithSheetKeys()
    {
        var keep = SheetKeys;
        SheetKeys = true;
        try
        {
            return WaitForAction();
        }
        finally
        {
            SheetKeys = keep;
        }
    }

    /// <summary>The menu bar the right button and F10 open while playing (null: none, e.g. in a battle).</summary>
    public MenuBar? MenuBar { get; set; }

    /// <summary>Takes a waiting key without the hot keys (for the menu bar's keyboard mode).</summary>
    public bool TryReadKey(out int key) => ReadHostKey(out key);

    /// <summary>True when a key is waiting (it stays waiting: the next read takes it).</summary>
    // orig: root 047C:073D() (root): looks at the BIOS buffer without taking the key (misc.exe slot 90: `mov ah, 1; int 16h; je none;
    //   retf` with AX the key word, `sub ax, ax` when none): the word 0000h a Ctrl-Break leaves at the buffer's head reads as no key, and
    //   so does every key typed behind it until a read takes it
    public bool KeyWaiting
    {
        get
        {
            if (_peeked == 0)
            {
                // C: a Ctrl-Break that comes while a key is looked at is taken at the read (TakeKey): the read gets 0000h
                TakeCtrlBreak();
            }
            if (_breakWaiting)
            {
                return false;
            }
            if (_peeked == 0 && _host.Input.TryReadKey(out var key))
            {
                _peeked = key;
            }
            return _peeked != 0;
        }
    }

    /// <summary>Takes the next key, waiting for one when none is waiting (no hot keys, no menu bar): the screen stays as it is meanwhile.</summary>
    // orig: root 047C:0744 (graphics driver slot 91, misc.exe +0x49: `sub ah, ah; int 16h; retf`, the BIOS read that waits for a key);
    //   it takes the word 0000h of a Ctrl-Break at once (0 here)
    public int ReadKey()
    {
        int key;
        _anyKeyWait++;
        try
        {
            while (!TakeKey(out key))
            {
                Present();
                if (TakeKey(out key))
                {
                    break;
                }
                _host.Clock.Sleep(FrameMilliseconds);
            }
        }
        finally
        {
            _anyKeyWait--;
        }
        return key;
    }

    /// <summary>Puts a key taken back in front of the waiting keys (a hot-key test that did not want it).</summary>
    public void PushBackKey(int key) => _peeked = key;

    /// <summary>The key looked at by <see cref="KeyWaiting"/> (0: none).</summary>
    private int _peeked;

    /// <summary>The word 0000h the BIOS put into its emptied buffer at a Ctrl-Break, not read yet: it stands before every key typed after it.</summary>
    private bool _breakWaiting;

    /// <summary>A read took that word since the last <see cref="Poll"/> began (<see cref="TakeBreakKeyRead"/>).</summary>
    private bool _breakRead;

    /// <summary>
    /// DS:07F4: Ctrl-Break was pressed (the game's INT 1Bh handler sets it); <see cref="QuitCheck"/> ends the program while it is set.
    /// Nothing clears it.
    /// </summary>
    // orig: root 044A:0100 (R000+0x0086 at start-up) puts the INT 1Bh vector to root 044A:013F: `push ds; push ax; ds = DGROUP; DS:07F4 = 0xFF;
    //   pop ax; pop ds; iret` (the vector is put back by the shutdown, root 044A:0122)
    public bool CtrlBreakPressed { get; private set; }

    /// <summary>True (once) when a read since the last <see cref="Poll"/> began took the word 0000h of a Ctrl-Break: the screen's key 0.</summary>
    // C: a loop's `if (key-waiting-test()) key = read-key();` gets key 0 when the Ctrl-Break comes between its look and its read (the
    //   look found a key, the BIOS emptied the buffer, the read takes 0000h); Poll and the loops that test first then see no key, and
    //   the screens that act on key 0 (the map: R030+0x0C44 with key 0) ask here
    public bool TakeBreakKeyRead()
    {
        var read = _breakRead;
        _breakRead = false;
        return read;
    }

    /// <summary>The host's Ctrl-Break, at a look or a read of the keyboard: the keys waiting are gone and the word 0000h waits; DS:07F4 is set.</summary>
    // orig: the BIOS keyboard interrupt on Ctrl-Break (E0 46, Ctrl held): the buffer emptied (head = tail), 0040:0071 bit 7, INT 1Bh (the
    //   game's handler: DS:07F4 = 0xFF) and the word 0000h stored. The game's own INT 9 handler (root 044A:020E), run first, keeps no
    //   byte for the key (scan 0x46 has 0 in DS:088A's table)
    private void TakeCtrlBreak()
    {
        if (_host.Input.TakeCtrlBreak())
        {
            _peeked = 0;
            _breakWaiting = true;
            CtrlBreakPressed = true;
        }
    }

    /// <summary>The program ends when Ctrl-Break has been pressed (the card engine's loops ask after each pass or key).</summary>
    // orig: quit-check root 0000:1338: `if (DS:07F4) { 1354(); 044A:0122(); exit(0); }`, called only by press-to-continue (R043+0x3343,
    //   every pass) and card-choice-menu (R043+0x1DEB, after a key it does not leave on). root 0000:1354: 044A:0122 (the INT 1Bh vector
    //   back), the mouse driver reset when present (047C:123F), the graphics driver's end (047C:00A9), int 10h AX = 3 (the text screen,
    //   cleared); then exit(0) (root 06A1:01D7). Unlike exit-to-system (root 0000:13A0) it neither puts the INT 9 vector back (044A:01F4)
    //   nor stops the sound (047C:0791), frees no EMS (05F8:0139) and puts back no dk.tmp.
    // DIVERGENCE: what the original leaves in the machine after this exit (the INT 8 / INT 9 vectors into the freed program, the EMS pages
    //   not freed, DGROUP's first 0x42 bytes not put back for exit's null-pointer check root 06A1:02E6) is the DOS machine's; here the
    //   program just ends. A Ctrl-Break before root 044A:0100 installs the handler (R000+0x0086, the start-up's first moments) is DOS's
    //   (its INT 23h ends the program at the next DOS call that checks); a host's Ctrl-Break reaches the game at its first look. The game
    //   sets no INT 23h (the C run-time's signal root 06A1:2A2E and raise 06A1:2917 have no caller), and what DOS's console driver makes of
    //   the 0000h word when the game reads the console through DOS (the load question's kbhit / getch, root 06A1:1C0A / 1C30) is DOS's.
    public void QuitCheck()
    {
        TakeCtrlBreak();
        if (CtrlBreakPressed)
        {
            throw new ProgramExitException("", 0);
        }
    }

    /// <summary>The next word of the keyboard buffer, without waiting (INT 16h function 0 when one is there): the 0000h of a Ctrl-Break first.</summary>
    private bool TakeKey(out int key)
    {
        TakeCtrlBreak();
        if (_breakWaiting)
        {
            _breakWaiting = false;
            _breakRead = true;
            key = Keys.None;
            return true;
        }
        if (_peeked != 0)
        {
            key = _peeked;
            _peeked = 0;
            return true;
        }
        if (!_host.Input.TryReadKey(out key))
        {
            return false;
        }
        // C: a Ctrl-Break just before the read (a scripted host's kbreak on this key): the key is gone, the read takes 0000h
        TakeCtrlBreak();
        if (_breakWaiting)
        {
            _breakWaiting = false;
            _breakRead = true;
            key = Keys.None;
        }
        return true;
    }

    /// <summary>`if (key-waiting-test()) key = read-key();`: the key, or false when none is waiting (or the read took a Ctrl-Break's 0000h).</summary>
    private bool ReadHostKey(out int key)
    {
        if (!KeyWaiting)
        {
            key = Keys.None;
            return false;
        }
        TakeKey(out key);
        return key != Keys.None;
    }

    /// <summary>Optional folder where F12 saves screenshots.</summary>
    public string? ScreenshotFolder { get; set; }

    /// <summary>Shows the screen (with the pointer on top) and processes window events.</summary>
    // orig: nothing: the original draws straight into video memory; quit-check root 0000:1338 is the check for a quit request that the loops call
    public void Present()
    {
        _host.Input.SelectionListActive = SelectionListActive;
        _host.Input.Pump();
        CountReleases();
        if (_host.Input.QuitRequested)
        {
            throw new QuitGameException();
        }
        var source = _held ?? _screen;
        source.Pixels.CopyTo(_shown.Pixels, 0);
        source.Palette.CopyTo(_shown.Palette, 0);
        ApplyCycles(_shown.Palette);
        source.CopyOwnershipTo(_shown);
        // host enhancement (IInput.PointerInWindow): no pointer while the host's pointer is outside the window; the shown copy only
        if (PointerVisible && PointerInWindow)
        {
            // orig: pointer-shape-set(0, 0, mouse-cursor-default) at start-up (R000+0x052A): the gauntlet of COMMONSP.IMG, hot spot at its top-left
            //   corner; the driver draws the shape at the mouse less the hot spot (PointerHotSpot)
            SpriteDrawing.Draw(new Canvas(_shown), PointerShape ?? Assets.CommonSprites.Pointer, Mouse.X - PointerHotSpot.X, Mouse.Y - PointerHotSpot.Y);
        }
        _host.Video.Present(_shown);
    }

    /// <summary>The screen as it was when <see cref="HoldScreen"/> was called, shown instead of the canvas until <see cref="ReleaseScreen"/>.</summary>
    private Screen? _held;

    /// <summary>
    /// The graphics driver's picture page (page 1) as the last screen that keeps it here left it: the load screen's picture, which a card
    /// that only names itself right after a load restores its text window from.
    /// </summary>
    // orig: image-paint-palette(1, "pics\loadscrn.pic") R037+0x83FE; the card window's text area is copied from page 1 (R043+0x3376)
    public SavedScreen? PicturePage { get; set; }

    /// <summary>
    /// Keeps the screen as it is now on show while the canvas is drawn on (the original's off-screen picture page): until
    /// <see cref="ReleaseScreen"/> (the card's first text, a battle) or the next <see cref="Poll"/> (a screen's own wait), a present
    /// shows the screen as it was here.
    /// </summary>
    // orig: card-run R043+0x0A9A draws the picture on page 1 and sets DS:EE55 ("picture not yet shown"); the first card text or option list
    //   (R043+0x0E40 / R043+0x0F2A) copies the page to the screen; a battle shows its own screen (R014+0x02F8)
    // C: only R043+0x0C70, R043+0x0E93 and R043+0x0FA4 read DS:EE55: the waits that read the keyboard alone (PollKeyOnly, PollWithoutBar,
    //   ReadKey, WaitForButtonRelease) do not show the picture, and none of them is reached while a picture is held (instrumented replay of
    //   the 816 sessions: local/batch/b24fix)
    public void HoldScreen()
    {
        if (_held is not null)
        {
            return;
        }
        _held = new Screen();
        _screen.Pixels.CopyTo(_held.Pixels, 0);
        _screen.Palette.CopyTo(_held.Palette, 0);
        _screen.CopyOwnershipTo(_held);
    }

    /// <summary>Ends <see cref="HoldScreen"/>: the next present shows the canvas.</summary>
    public void ReleaseScreen() => _held = null;

    /// <summary>Shows the 80×25 text screen (the program start) and processes window events; keys stay waiting.</summary>
    // orig: nothing: the program start writes through the BIOS and DOS, see TextConsole
    public void PresentText(TextScreen screen)
    {
        _host.Input.Pump();
        CountReleases();
        if (_host.Input.QuitRequested)
        {
            throw new QuitGameException();
        }
        _host.Video.PresentText(screen);
    }

    /// <summary>The next key press or click, or null if the player did nothing this frame.</summary>
    // orig: one pass of the usual wait loop: menu-bar-loop(DS:9419, 0) (hotkey-table when a key is waiting) then key-waiting-test() / mouse-button-state; GameKey stands for hotkey-table
    // C: hotkey-table only takes the keys it knows (Alt-Q ... F10) and leaves every other key in the buffer for the screen; GameKey returning false does the same. Keys the hot keys take never reach the screen, as here.
    public PlayerAction? Poll()
    {
        ReleaseScreen();
        Present();
        _breakRead = false;
        // C: a waiting command stands for the player's key or click of this pass (docs/COMMANDS.md); the host's input waits for the next pass
        var commanded = Commands.Count > 0;
        var key = Keys.None;
        if (commanded && CarryOutCommand(out key) is { } click)
        {
            return click;
        }
        if (commanded ? key != Keys.None : ReadHostKey(out key))
        {
            if (key == Keys.F12 && ScreenshotFolder is not null)
            {
                SaveScreenshot();
                return null;
            }
            if (key == Keys.F10 && MenuBar is not null)
            {
                // orig: hotkey-table case 0x4400: menu mode DS:9C26 = 1, then menu-bar-loop's loop drives the bar
                var seen = _leftReleasesSeen;
                MenuBar.Run(keyboard: true);
                // orig: root 0000:0048..0x0078 / 0000:0DD2 test only keys and DS:6EF0 & 2: a left click made in menu mode stays in the INT 33h
                //   function 6 latch, and the screen's next pass takes it at the release place (R043+0x2713, after menu-bar-loop R043+0x1C14)
                if (_leftReleasesSeen != seen && !Mouse.Left)
                {
                    _previousButtons = Mouse.Buttons;
                    return new PlayerAction(Keys.None, _lastReleaseX, _lastReleaseY, MouseButtons.Left);
                }
                return null;
            }
            // orig: root 0000:0029..0x0033 runs hotkey-table with the font 1; F1..F5 / Ctrl-F1..F5 (GameKey's too) are the screen's own keys,
            //   read with the font put back (HotKeys.InHotKeyTable)
            if (GameKey is { } gameKey && (HotKeys.InHotKeyTable(key) ? InMenuBarLoop(() => gameKey(key)) : gameKey(key)))
            {
                return null;
            }
            return new PlayerAction(key, Mouse.X, Mouse.Y, MouseButtons.None);
        }
        if (commanded)
        {
            return null;
        }
        var buttons = Mouse.Buttons;
        var pressed = buttons & ~_previousButtons;
        _previousButtons = buttons;
        if ((buttons & MouseButtons.Right) != 0 && MenuBar is not null)
        {
            // orig: root 0000:0000 root 0000:0063: `if (mouse-button-state & 2)` (the button held, not pressed) the bar runs while it is held;
            // a left button still held afterwards is seen by the screen's own test (here: as a press at the next poll)
            MenuBar.Run(keyboard: false);
            _previousButtons = Mouse.Buttons & ~MouseButtons.Left;
            return null;
        }
        if (pressed != MouseButtons.None)
        {
            return new PlayerAction(Keys.None, Mouse.X, Mouse.Y, pressed);
        }
        return null;
    }

    /// <summary>
    /// The commands a script or an interface convenience issues (docs/COMMANDS.md): every loop that reads the player through
    /// <see cref="Poll"/> takes the next one instead of the host's input, one per pass. Empty in normal play.
    /// </summary>
    public CommandQueue Commands { get; } = new();

    /// <summary>
    /// Where line n of the list the screen is choosing from is (a point inside it), for <see cref="ChooseLine"/>; null while no
    /// screen offers such a list. The screen sets it while it chooses and clears it after.
    /// </summary>
    public Func<int, (int X, int Y)?>? CommandTargets { get; set; }

    /// <summary>The next command as the input it stands for: a click (returned), a key (<paramref name="key"/>), or neither (a pointer move, or a line no list has).</summary>
    // C: the host's pointer is moved, as the mouse driver's function 4 moves it for the game's own cursor keys; a host that cannot
    //   move it keeps its pointer, and the next pass sees that one
    private PlayerAction? CarryOutCommand(out int key)
    {
        key = Keys.None;
        if (!Commands.TryTake(out var command))
        {
            return null;
        }
        switch (command)
        {
            case PressKey press:
                key = press.Key;
                return null;
            case PointAt point:
                _host.Input.MovePointer(OnScreenX(point.X), OnScreenY(point.Y));
                return null;
            case ClickAt click:
                var (x, y) = (OnScreenX(click.X), OnScreenY(click.Y));
                _host.Input.MovePointer(x, y);
                return new PlayerAction(Keys.None, x, y, MouseButtons.Left);
            case ChooseLine line when CommandTargets?.Invoke(line.Line) is { } target:
                // C: the pointer first, the click in the next pass: the screen follows the pointer in between (a card's box opens,
                //   the pointer enters it), as for a player's hand
                _host.Input.MovePointer(OnScreenX(target.X), OnScreenY(target.Y));
                Commands.PutFirst(new ClickAt(target.X, target.Y));
                return null;
            default:
                return null;
        }
    }

    /// <summary>A command's point kept on the screen, as the host keeps a real pointer (0..319, 0..199): no click where no mouse can reach.</summary>
    private static int OnScreenX(int x) => Math.Clamp(x, 0, Screen.Width - 1);

    private static int OnScreenY(int y) => Math.Clamp(y, 0, Screen.Height - 1);

    /// <summary>
    /// One pass of a loop that reads the keyboard and the left button itself (no menu bar, no hot keys): a waiting key or a left
    /// press, null when there is neither. The right button does nothing here.
    /// </summary>
    // orig: `if (root 047C:073D()) key = root 047C:0744();` and the `DS:6EF0 & 1` tests of loops that do not call root 0000:0000
    //   (R037+0x4664's loop, R037+0x7F06 / R037+0x802A's waits)
    /// <param name="rightPresses">Report right presses too (the generator's right click, a host comfort); by default only the left.</param>
    public PlayerAction? PollWithoutBar(bool rightPresses = false)
    {
        Present();
        if (ReadHostKey(out var key))
        {
            if (key == Keys.F12 && ScreenshotFolder is not null)
            {
                SaveScreenshot();
                return null;
            }
            return new PlayerAction(key, Mouse.X, Mouse.Y, MouseButtons.None);
        }
        var buttons = Mouse.Buttons;
        var pressed = buttons & ~_previousButtons & (rightPresses ? MouseButtons.Left | MouseButtons.Right : MouseButtons.Left);
        _previousButtons = buttons;
        return pressed != MouseButtons.None ? new PlayerAction(Keys.None, Mouse.X, Mouse.Y, pressed) : null;
    }

    /// <summary>Waits for the next key or left press (<see cref="PollWithoutBar"/>: no menu bar, no hot keys).</summary>
    public PlayerAction WaitForActionWithoutBar()
    {
        while (true)
        {
            if (PollWithoutBar() is { } action)
            {
                return action;
            }
            _host.Clock.Sleep(FrameMilliseconds);
        }
    }

    /// <summary>One frame of a screen's loop: the player's action, or null after waiting a frame.</summary>
    public PlayerAction? PollFrame()
    {
        if (Poll() is { } action)
        {
            return action;
        }
        _host.Clock.Sleep(FrameMilliseconds);
        return null;
    }

    /// <summary>Lets one frame of time pass without reading the player (for loops that poll and animate).</summary>
    public void Idle() => _host.Clock.Sleep(FrameMilliseconds);

    /// <summary>Waits for the next key press or click.</summary>
    // orig: loops like `for (;;) { menu-bar-loop(...); if (mouse-button-state & 1) ...; if (key-waiting-test()) ... }` in press-to-continue R043+0x32B6 (150B:2826) and R043+0x29D0
    public PlayerAction WaitForAction()
    {
        while (true)
        {
            if (Poll() is { } action)
            {
                return action;
            }
            _host.Clock.Sleep(FrameMilliseconds);
        }
    }

    /// <summary>The least time a pressed button's picture is shown in the player's window, in ms (the SDL host sets <see cref="PressedButtonFloor"/>).</summary>
    /// <remarks>0 (the default; Host.Headless, the oracles, the tests): no wait, as the original.</remarks>
    public int PressedButtonMilliseconds { get; set; }

    /// <summary>The player's window's minimum for a pressed button: about six frames, long enough to be seen.</summary>
    public const int PressedButtonFloor = 100;

    /// <summary>Shows a pressed button just drawn (the creation and inn roster commands, the load screen's buttons), then holds it for
    /// <see cref="PressedButtonMilliseconds"/> before the command runs.</summary>
    // orig: the screens draw the pressed button (gfx-picture-draw root 047C:05A5), run the command and copy the button's area back
    //   (root 047C:0553) with no wait between: in the emulated original at 3000 instructions per ms an inn roster command shows its
    //   button 0.13 ms, a creation command that reads charactr.tmp 36 ms (plus the disk's time); a press that leaves the screen keeps it
    //   until the next screen is drawn (0.5 to 0.9 s there, the pictures decoded)
    // DIVERGENCE: host comfort (machine speed): the pressed picture is held in the player's window at least PressedButtonMilliseconds
    //   (0.1 s) before the command runs, so the press is seen on a fast machine; the command, the drawing and its order are unchanged, and
    //   nothing is read meanwhile (a key or click waits, as in the BIOS buffer and the mouse driver's counts). Headless and the oracles
    //   keep 0: no frame or clock of theirs changes.
    public void PresentPressedButton()
    {
        Present();
        if (PressedButtonMilliseconds > 0)
        {
            Wait(PressedButtonMilliseconds, skippable: false);
        }
    }

    /// <summary>Waits a while, showing the screen; a key or click ends the wait early when allowed.</summary>
    // orig: wait-ticks(n) (R030+0x3096): spins until clock() has advanced by n, no input checks at all; notice-display waits root 0410:0302(ticks) (a timer-driver wait, see MessageBox)
    // C: a clock() tick is one millisecond (root 06A1:2682 multiplies the elapsed seconds by 1000, MSC 6 CLK_TCK = 1000); the 0410 timer ticks have another unit,
    //   see MessageBox and Cinematics.
    // C: a delay that cannot be skipped reads nothing (wait-ticks spins on clock() alone): a key pressed meanwhile stays waiting for the screen's next look, as in the BIOS buffer.
    public void Wait(int milliseconds, bool skippable = true)
    {
        var end = _host.Clock.Milliseconds + milliseconds;
        while (_host.Clock.Milliseconds < end)
        {
            if (!skippable)
            {
                Present();
            }
            else if (Poll() is not null)
            {
                return;
            }
            _host.Clock.Sleep(FrameMilliseconds);
        }
    }

    /// <summary>
    /// The mouse driver's left-button release count and the pointer's place at the last release (INT 33h function 6 for the
    /// left button): the releases since the last call (the count is then reset) and where the last one was.
    /// </summary>
    // orig: root 047C:1320 called with (6, 0, ...): INT 33h AX = 6, BX = 0; the count and place of the releases the driver latched
    // C: the count goes on while the game waits elsewhere (a modal box, a delay): a release made then is reported at the next call.
    // DIVERGENCE: the host reports the buttons once a frame; a release is counted when a frame sees the button up after one that saw
    // it down, at the pointer's place in that frame (the hosts keep a click within one frame for a frame).
    public (int Count, int X, int Y) TakeLeftReleases()
    {
        var result = (_leftReleases, _lastReleaseX, _lastReleaseY);
        _leftReleases = 0;
        return result;
    }

    /// <summary>The direction keys held, as the game's keyboard interrupt keeps them (DS:0880 / DS:0881).</summary>
    public KeyboardDirections Directions { get; } = new();

    private void CountReleases()
    {
        while (_host.Input.TryReadKeyTransition(out var scanCode, out var released))
        {
            Directions.Feed(scanCode, released, _host.Clock.Milliseconds);
        }
        var mouse = _host.Input.Mouse;
        if (_leftWasDown && !mouse.Left)
        {
            _leftReleases++;
            _leftReleasesSeen++;
            _lastReleaseX = mouse.X;
            _lastReleaseY = mouse.Y;
        }
        _leftWasDown = mouse.Left;
    }

    /// <summary>
    /// The end of one pass of a screen's loop that reads the mouse itself: root 0000:0000 (the menu bar and the hot keys of
    /// <see cref="Poll"/>), then a waiting key, which replaces the key the mouse gave in this pass; a frame passes when no key is
    /// waiting. A left click is not taken here: the loop's own tests read the button.
    /// </summary>
    // orig: `root 0000:0000(DS:9419, 0); if (DS:9C46) { DS:9C46 = 0; <the screen drawn again>; key = 0; } if (root 047C:073D()) key = root 047C:0744();`
    //   at the end of the screens' loops
    /// <param name="mouseKey">The key the pass's mouse tests gave.</param>
    /// <param name="redrawIfDirty">The screen's redraw for <see cref="PartyIconsDirty"/>, for the screens whose loop has that test.</param>
    /// <param name="redrawKeepsTheKey">The screen's redraw leaves the mouse's key as it is (barter, R032+0x06BF; the others set it to 0).</param>
    public int KeyAfterTheMouse(int mouseKey, Action? redrawIfDirty = null, bool redrawKeepsTheKey = false)
    {
        KeyTaken = false;
        var action = Poll();
        if (redrawIfDirty is not null && PartyIconsDirty)
        {
            PartyIconsDirty = false;
            redrawIfDirty();
            if (!redrawKeepsTheKey)
            {
                mouseKey = 0;
            }
        }
        if (action is { IsKey: true } pressed)
        {
            KeyTaken = true;
            return pressed.Key;
        }
        _host.Clock.Sleep(FrameMilliseconds);
        return mouseKey;
    }

    /// <summary>The last <see cref="KeyAfterTheMouse"/> returned a key that was pressed, not the mouse's choice.</summary>
    public bool KeyTaken { get; private set; }

    /// <summary>
    /// The party information screen was shown since the last screen that tested for it: the screens of the loops with that test draw
    /// themselves again (<see cref="KeyAfterTheMouse"/>).
    /// </summary>
    // orig: DS:9C46, set by party-info-screen-close (R034+0x119C, the only writer of 1) and cleared by the redraw after root 0000:0000 in the loops of
    //   R033+0x0000, R033+0x16FA, R034+0x022C, R036+0x0082, R032+0x0000 (barter), R031+0x0124 (the sheet) and the other screens that test it
    // C: the screens with that test and no GameUi.Redraw: alchemy (both pages), camp, prayer, barter, the sheet; R035 (the item store) and
    //   R040 (loot) have none (the flag stays set).
    public bool PartyIconsDirty { get; set; }

    /// <summary>
    /// The barter screen's list of the codes for sale, kept for the whole run of the game: a listing writes only its own
    /// entries, so the rows past a shorter list's end keep what an earlier, longer listing left (0 before any).
    /// </summary>
    // orig: DS:86A2 (int[200], BSS: 0 at start-up), written only by the stock listing R032+0x0ACD (scan of every record for the
    //   address) and never cleared; a saved game neither holds nor restores it
    public int[] BarterListing { get; } = new int[200];

    /// <summary>
    /// The party panel's selection bytes, one per row: a row whose byte is set has its frame drawn lit, in a battle and outside it. Kept
    /// for the whole run of the game: a battle starts from them and leaves them as its members' selection stands (a game loaded from the
    /// battle's menu bar with a member selected in order mode leaves that frame lit on the map, the cards and the next battle).
    /// </summary>
    // orig: DS:9064 (five bytes, BSS: 0 at start-up), read by party-panel-frame-set root 0000:1588; set only by command-mode-begin
    //   R014+0xBC0C (21F0:06B3), cleared by command-mode-end R014+0xBCA8 and R016+0x13D4 for the selected member, kept aside and put back by
    //   the marching order R022+0x051C; nothing writes them at a battle's start or end or at a load (vis10 sessions uncert-march-load and
    //   uncert-march-load-selected, every write to DS:9064..9068 logged from start-up; the map's panel after the load shows the frame lit)
    public bool[] PanelSelection { get; } = new bool[5];

    /// <summary>
    /// Order mode and the member in it as a battle left them: while order mode stands, the party panel draws the rows of the other
    /// members dim (colour 7), outside a battle too. A game loaded from the battle's menu bar with a member in order mode leaves it on.
    /// </summary>
    // orig: command-mode DS:A6B3 (set by command-mode-begin R014+0xBC0C, cleared by command-mode-end R014+0xBCA8 and R016+0x13D4) and
    //   chosen-member DS:A6B2 (0xFF at a battle's set-up), read by party-panel-row-set root 0000:1716 (`command-mode == 1 && chosen-member != i`
    //   gives colour 7); a load writes neither (vis10 session uncert-march-load, every write to DS:A6B2..DS:A6B8 logged from start-up: after the
    //   load A6B2 = 3, A6B3 = 1, and the map's panel shows rows 0..2 in colour 7)
    public (bool On, int Member) PanelOrderMode { get; set; } = (false, -1);

    /// <summary>
    /// One pass of a loop that reads the keyboard itself (no menu bar, no hot keys): the screen is shown and a waiting key
    /// taken (0: none, and a frame passes).
    /// </summary>
    // orig: `if (root 047C:073D()) key = root 047C:0744();` repeated inline in loops without root 0000:0000 (R034+0x0EE6, ...)
    public int PollKeyOnly()
    {
        _anyKeyWait++;
        try
        {
            Present();
        }
        finally
        {
            _anyKeyWait--;
        }
        if (ReadHostKey(out var key))
        {
            return key;
        }
        _host.Clock.Sleep(FrameMilliseconds);
        return 0;
    }

    /// <summary>Lets <paramref name="milliseconds"/> pass, showing the screen, without reading the keyboard or the mouse (the keys stay waiting).</summary>
    // orig: R030+0x3096 (1767:0046) matched: spins on clock(), no input test
    public void Delay(int milliseconds)
    {
        var end = _host.Clock.Milliseconds + milliseconds;
        do
        {
            Present();
            _host.Clock.Sleep(Math.Min(FrameMilliseconds, milliseconds));
        }
        while (_host.Clock.Milliseconds < end);
    }

    /// <summary>Lets clock() (<see cref="DosClock"/>) advance by <paramref name="units"/>, showing the screen, without reading the keyboard or the mouse: the wait ends at a BIOS tick.</summary>
    // orig: R030+0x3096 (1767:0046) matched: `end = clock() + n; while (clock() < end);` on a clock() that moves only at the BIOS tick: a
    //   wait of 30 (the overland pass) ends at the next tick, whatever part of the tick the pass took before it
    public void DelayClock(int units)
    {
        var end = DosClock.Milliseconds(_host.Clock.Milliseconds) + units;
        do
        {
            Present();
            var now = _host.Clock.Milliseconds;
            if (DosClock.Milliseconds(now) >= end)
            {
                break;
            }
            _host.Clock.Sleep((int)Math.Clamp(DosClock.NextTickAt(now) - now, 1, FrameMilliseconds));
        }
        while (DosClock.Milliseconds(_host.Clock.Milliseconds) < end);
    }

    /// <summary>Waits until the left mouse button is let go (the original's `do {} while (mouse-button-state & 1)` after a click it acts on).</summary>
    // orig: R043+0x2A3C press-to-continue's tail and the card engine's click handling (R043 150B) wait the button out before going on;
    //   without it the next screen sees the button still held (the world map then steers the party toward the pointer).
    public void WaitForButtonRelease()
    {
        while (Mouse.Left)
        {
            Present();
            _host.Clock.Sleep(FrameMilliseconds);
        }
        _previousButtons = Mouse.Buttons;
    }

    /// <summary>Waits a while, showing the screen; true as soon as a key is pressed.</summary>
    // orig: loops that test key-waiting-test() while the PAN player or a delay runs (e.g. `if (kbhit()) return;` in root 030B:0150, `!key-waiting-test() && !(mouse-button-state & 1)` in R043+0x3494)
    // C: the key stays in the buffer in the original (kbhit/key-waiting-test only look); here it is read, so the caller that skips must act as if the key were still waiting.
    public bool WaitForKey(int milliseconds)
    {
        var end = _host.Clock.Milliseconds + milliseconds;
        _anyKeyWait++;
        try
        {
            do
            {
                if (Poll() is { IsKey: true })
                {
                    return true;
                }
                _host.Clock.Sleep(Math.Min(FrameMilliseconds, milliseconds));
            }
            while (_host.Clock.Milliseconds < end);
            return false;
        }
        finally
        {
            _anyKeyWait--;
        }
    }

    /// <summary>Saves the current screen as a PNG file.</summary>
    public void SaveScreenshot(string? path = null)
    {
        path ??= Path.Combine(ScreenshotFolder ?? ".", $"darklands-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
        PngWriter.Write(_screen, path);
    }
}
