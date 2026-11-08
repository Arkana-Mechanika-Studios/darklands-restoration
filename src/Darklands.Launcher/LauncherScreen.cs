using Darklands.Launcher.Drawing;

namespace Darklands.Launcher;

/// <summary>
/// The launcher's one screen, in the look of the original game's menus: the title and the release notes and the project's
/// note on parchment at the left, the settings on a blue panel at the right, everything framed in rope. Laid out on a
/// 1080x640 design and scaled to the surface.
/// </summary>
internal sealed class LauncherScreen
{
    public const string DiscordLink = "https://discord.gg/HjzWvmHhqZ";

    private const int DesignWidth = 1280;
    private const int DesignHeight = 720;

    // the look (art direction of 2026-11-08: a painted landscape, parchment cards, ink, red and gold)
    private const uint Black = 0x000000;
    private const uint Ink = 0x2B1D12;
    private const uint InkSoft = 0x5C4631;
    private const uint InkRed = 0x8E1B12;
    private const uint InkGreen = 0x356A26;
    private const uint LinkRed = 0x8C1A10;
    private const uint Cream = 0xF1E6CF;
    private const uint Gold = 0xD2A94E;
    private const uint ParchmentLight = 0xE2D2B0;
    private const uint ParchmentDark = 0xC4AA80;
    private const uint ParchmentBurn = 0x6E5132;
    private const uint FieldFill = 0xEFE4CB;
    private const uint DropCapRed = 0x7E1A12;
    private const uint DropCapDark = 0x4A0E08;
    private const uint HintFill = 0x231B14;

    // the colours of messages and status lines as given (for the dark hint bar); on parchment they become ink (OnParchment)
    private const uint PaleRed = 0xFF9E8C;
    private const uint PaleBlue = 0xE8DCC4;
    private const uint Green = 0xA6DB8E;

    private readonly LauncherModel _model;
    private readonly LauncherContext _context;
    private readonly ILauncherSurface _surface;
    private readonly IReadOnlyList<string> _notes;
    private readonly Dictionary<(TrueTypeFont Font, int TenthsOfPixels), TextFace> _faces = [];
    private List<(Rect Area, Item Item, Part Part)> _hotspots = [];
    private List<(Rect Area, string What)> _blocks = [];
    private Rect _panel;

    /// <summary>What the pointer is over (the hint bar tells about it), or None.</summary>
    private Item _hover;
    private Part _hoverPart;

    /// <summary>The plate the mouse button is held down on (drawn pressed; it acts when let go over it), or None.</summary>
    private Item _pressed;
    private Part _pressedPart;
    private Item _focus;
    private bool _editing;
    private string _editText = "";
    private bool _editFresh;
    private string? _message;
    private uint _messageColour = PaleRed;
    private bool _showSound;
    private bool _advanced;
    private Rect? _notesLink;
    private Canvas? _background;
    private Rect _discord;
    private Rect? _guideLink;
    private Rect? _reportLink;

    public LauncherScreen(LauncherModel model, LauncherContext context, ILauncherSurface surface)
    {
        _model = model;
        _context = context;
        _surface = surface;
        _notes = ReleaseNotes.Items(context.ReleaseNotes);
        _focus = model.CanPlay ? Item.Play : Item.GameFolder;
    }

    public enum Item
    {
        None,
        GameFolder,
        Change,
        Window,
        Fullscreen,
        HdPack,
        Sound,
        Roms,
        Test,
        Banner,
        Intro,
        Always,
        Save,
        Play,
        Discord,
        Notes,
        AudioPack,
        PackGuide,
        SoundFont,
        Report,
        TabGeneral,
        TabAdvanced,

        /// <summary>The frameless window's own buttons (<see cref="ILauncherSurface.Frameless"/>).</summary>
        Minimise,
        Close,
    }

    /// <summary>The parts of a control a click can hit.</summary>
    public enum Part
    {
        /// <summary>The control itself (a row, a field, a button, a link).</summary>
        Row,

        /// <summary>A row's ‹: the previous value.</summary>
        Previous,

        /// <summary>A row's ›: the next value.</summary>
        Next,

        /// <summary>A row's own "Browse…" (or "Change…") button: the system's folder or file dialog.</summary>
        Browse,
    }

    public Item Focus => _focus;

    /// <summary>Where <paramref name="item"/> was drawn last.</summary>
    public Rect Area(Item item, Part part = Part.Row) => _hotspots.First(h => h.Item == item && h.Part == part).Area;

    /// <summary>The settings panel's inside, as drawn last: everything of the settings stays in it.</summary>
    internal Rect SettingsPanel => _panel;

    /// <summary>What the settings panel was drawn with last (rows, fields, buttons, lines of text), each once: none of them overlaps another.</summary>
    internal IReadOnlyList<(Rect Area, string What)> Blocks => _blocks;

    public bool Editing => _editing;

    /// <summary>The Advanced tab is open (else General).</summary>
    public bool AdvancedShown => _advanced;

    /// <summary>The rows of the Advanced tab; the other settings rows are on General.</summary>
    private static bool OnAdvanced(Item item) => item is Item.Roms or Item.SoundFont or Item.Banner or Item.Intro or Item.Always;

    private static bool OnGeneral(Item item) => item is Item.GameFolder or Item.Change or Item.Window or Item.Fullscreen or Item.HdPack or Item.Sound or Item.Test or Item.AudioPack;

    /// <summary>Opens a tab and puts the focus on its first row.</summary>
    private void ShowTab(bool advanced)
    {
        if (_editing)
        {
            StopEditing(apply: true);
        }
        _advanced = advanced;
        _focus = advanced ? Item.Roms : Item.GameFolder;
    }

    // Tab and the arrows go through General's rows, then Advanced's (its tab opens), then the buttons and the links
    private IEnumerable<Item> Order =>
        new[] { Item.GameFolder, Item.Change, Item.Window, Item.Fullscreen, Item.HdPack, Item.Sound, Item.Test, Item.AudioPack, Item.Roms, Item.SoundFont, Item.Banner, Item.Intro, Item.Always, Item.Save, Item.Play, Item.Discord, Item.PackGuide, Item.Report, Item.Notes }
            .Where(i => (i != Item.Test || _context.SoundTest is not null) && (i != Item.Notes || _context.ReleaseNotesLink is not null)
                && (i != Item.PackGuide || _context.PackGuide is not null) && (i != Item.Report || _context.ReportProblem is not null));

    /// <summary>Handles one input; returns the player's choice when the launcher should end.</summary>
    public LauncherChoice? Handle(LauncherInput input)
    {
        switch (input)
        {
            case CloseRequested:
                return LauncherChoice.Quit;
            case PointerReleased released:
                var (pressed, pressedPart) = (_pressed, _pressedPart);
                _pressed = Item.None;
                var under = _hotspots.LastOrDefault(h => h.Area.Contains(released.X, released.Y));
                return pressed != Item.None && under.Item == pressed && under.Part == pressedPart
                    ? Activate(pressed, 1, cycle: false)
                    : null;
            case PointerMoved moved:
                var over = _hotspots.LastOrDefault(h => h.Area.Contains(moved.X, moved.Y));
                (_hover, _hoverPart) = (over.Item, over.Part);
                return null;
            case PathDropped dropped:
                Dropped(dropped.Path);
                return null;
            case PointerPressed press:
                var hit = _hotspots.LastOrDefault(h => h.Area.Contains(press.X, press.Y));
                if (hit.Item == Item.None)
                {
                    return null;
                }
                if (hit.Item is Item.Minimise or Item.Close)
                {
                    // the window's own buttons take no focus
                    (_pressed, _pressedPart) = (hit.Item, hit.Part);
                    return null;
                }
                if (_editing && hit.Item != Item.GameFolder)
                {
                    StopEditing(apply: true);
                }
                _focus = hit.Item;
                if (IsPlate(hit.Item, hit.Part))
                {
                    // a plate acts when the button is let go over it (and looks pressed until then)
                    (_pressed, _pressedPart) = (hit.Item, hit.Part);
                    return null;
                }
                return hit.Part switch
                {
                    Part.Previous => Activate(hit.Item, -1, cycle: true),
                    Part.Next => Activate(hit.Item, 1, cycle: true),
                    Part.Browse => Activate(hit.Item, 1, cycle: false),
                    // a click on a row with a dialog behind it only chooses the row (←/→ then work); its Browse… opens the dialog
                    _ when SelectedByClick(hit.Item) => Selected(),
                    _ => Activate(hit.Item, step: 1, cycle: false),
                };
            case TextTyped typed when _editing:
                _editText = (_editFresh ? "" : _editText) + typed.Text;
                _editFresh = false;
                return null;
            case TextTyped typed when _focus == Item.GameFolder && !IsFixed(Item.GameFolder, out _):
                // typing on the focused game folder starts editing it (no click or Enter needed)
                StartEditing(_model.GameFolder ?? "");
                _editText = typed.Text;
                _editFresh = false;
                return null;
            case KeyPressed key when _editing:
                EditKey(key);
                return null;
            case KeyPressed key:
                return Key(key);
            default:
                return null;
        }
    }

    /// <summary>The plates: Browse… / Change… on their rows, Change… of the game folder, Test sound, Save and quit, Play.</summary>
    private static bool IsPlate(Item item, Part part) => part == Part.Browse || item is Item.Change or Item.Test or Item.Save or Item.Play or Item.Minimise or Item.Close;

    /// <summary>Where the controls were drawn last (the links, rows, plates, tabs and the window's buttons): a frameless window is dragged elsewhere.</summary>
    internal IReadOnlyList<(int X, int Y, int Width, int Height)> ControlAreas => [.. _hotspots.Select(h => (h.Area.X, h.Area.Y, h.Area.Width, h.Area.Height))];

    /// <summary>The rows a click only chooses: Enter or their Browse… / Change… button opens the system's dialog.</summary>
    private static bool SelectedByClick(Item item) => item is Item.HdPack or Item.AudioPack or Item.SoundFont or Item.Roms;

    private LauncherChoice? Selected()
    {
        _message = null;
        _showSound = false;
        return null;
    }

    private LauncherChoice? Key(KeyPressed key)
    {
        switch (key.Key)
        {
            case LauncherKey.Up:
                MoveFocus(-1);
                return null;
            case LauncherKey.Down:
                MoveFocus(1);
                return null;
            case LauncherKey.Tab when key.Control:
                // Ctrl+Tab: the other tab
                ShowTab(!_advanced);
                return null;
            case LauncherKey.Tab:
                MoveFocus(key.Shift ? -1 : 1);
                return null;
            case LauncherKey.Left:
            case LauncherKey.Right:
                var step = key.Key == LauncherKey.Left ? -1 : 1;
                if (_focus is Item.Window or Item.Fullscreen or Item.HdPack or Item.AudioPack or Item.SoundFont or Item.Sound or Item.Banner or Item.Intro or Item.Always or Item.GameFolder)
                {
                    return Activate(_focus, step, cycle: true);
                }
                MoveFocus(step);
                return null;
            case LauncherKey.Enter:
                return Activate(_focus, 1, cycle: false);
            case LauncherKey.Escape:
                return LauncherChoice.Quit;
            case LauncherKey.Paste when _focus is Item.GameFolder or Item.Change:
                if (IsFixed(Item.GameFolder, out var by))
                {
                    _message = $"The game folder is set by {by}: change it there.";
                    return null;
                }
                _focus = Item.GameFolder;
                StartEditing(CleanPath(_surface.Clipboard()));
                _editFresh = false;
                return null;
            default:
                return null;
        }
    }

    private LauncherChoice? Activate(Item item, int step, bool cycle)
    {
        _message = null;
        _messageColour = PaleRed;
        _showSound = false;
        switch (item)
        {
            case Item.TabGeneral or Item.TabAdvanced:
                ShowTab(item == Item.TabAdvanced);
                return null;
            case Item.Close:
                return LauncherChoice.Quit;
            case Item.Minimise:
                _surface.Minimise();
                return null;
        }
        if (IsFixed(item, out var fixedBy))
        {
            _message = $"Set by {fixedBy}: change it there.";
            _messageColour = PaleBlue;
            return null;
        }
        switch (item)
        {
            case Item.GameFolder when cycle:
                _model.CycleFoundFolder(step);
                break;
            case Item.GameFolder:
                StartEditing(_model.GameFolder ?? "");
                break;
            case Item.Change:
                if (Pick("Choose your Darklands folder (the one with DARKLAND.EXE, or the GOG or Steam install folder)", out var picked))
                {
                    if (picked is not null)
                    {
                        SetGameFolder(picked);
                        if (!_model.CanPlay)
                        {
                            _message = $"{picked} holds no Darklands (DARKLAND.EXE).";
                        }
                    }
                }
                else
                {
                    // no dialog here: the folder is typed, pasted (Ctrl+V) or dropped instead
                    _focus = Item.GameFolder;
                    StartEditing(_model.GameFolder ?? "");
                    _message ??= "Type or paste (Ctrl+V) the folder, or drop it on this window.";
                    _messageColour = PaleBlue;
                }
                break;
            case Item.Window:
                _model.CycleWindow(step);
                break;
            case Item.Fullscreen:
                _model.Fullscreen = !_model.Fullscreen;
                break;
            case Item.HdPack or Item.AudioPack:
                ChoosePack(item == Item.HdPack ? PackKind.Pictures : PackKind.Sounds, step, cycle);
                break;
            case Item.SoundFont when cycle:
                // left/right: back to the default (the SOUNDFONT folder)
                if (IsFixed(Item.SoundFont, out var soundFontBy))
                {
                    _message = $"The SoundFont is set by {soundFontBy}.";
                }
                else
                {
                    _model.SoundFont = null;
                    SoundFontMessage();
                }
                break;
            case Item.SoundFont:
                PickSoundFont();
                break;
            case Item.Report when _context.ReportProblem is { } report:
                _message = report();
                break;
            case Item.PackGuide when _context.PackGuide is { } guide:
                _context.OpenLink(guide);
                break;
            case Item.Sound:
                _model.CycleSoundCard(step);
                break;
            case Item.Roms:
                if (Pick("Choose the folder with your MT-32 or CM-32L ROM files", out var romFolder))
                {
                    if (romFolder is not null)
                    {
                        SetRoms(romFolder);
                    }
                }
                else
                {
                    _message = (_message is null ? "" : _message + " ") + "Drop the folder with your MT-32 or CM-32L ROM files on this window.";
                }
                break;
            case Item.Test when _model.GameFolderChecked is null:
                _message = "Choose the game folder first: the sound drivers are in it.";
                break;
            case Item.Test when _context.SoundTest is { Playing: true } playing:
                playing.Stop();
                break;
            case Item.Test:
                _context.SoundTest?.Play(new SoundTestRequest(_model.SoundCard, _model.GameFolderChecked, _model.Mt32Roms));
                _showSound = true;
                break;
            case Item.Banner:
                _model.Banner = !_model.Banner;
                break;
            case Item.Intro:
                _model.Intro = !_model.Intro;
                break;
            case Item.Always:
                _model.Always = !_model.Always;
                break;
            case Item.Save:
                return LauncherChoice.Quit;
            case Item.Play:
                if (_model.CanPlay)
                {
                    return LauncherChoice.Play;
                }
                _focus = Item.GameFolder;
                _message = "Choose your Darklands folder first.";
                break;
            case Item.Discord:
                _context.OpenLink(DiscordLink);
                break;
            case Item.Notes when _context.ReleaseNotesLink is { } notes:
                _context.OpenLink(notes);
                break;
        }
        return null;
    }

    /// <summary>
    /// The system's folder dialog: false when there is none (or it could not be shown: <see cref="_message"/> says why); else true,
    /// with the folder chosen or null when cancelled.
    /// </summary>
    private bool Pick(string title, out string? folder)
    {
        folder = null;
        if (_surface.PickFolder is not { } pick)
        {
            return false;
        }
        try
        {
            folder = pick(title) is { } picked ? CleanPath(picked) : null;
            return true;
        }
        catch (NotSupportedException e)
        {
            _message = e.Message;
            _messageColour = PaleRed;
            return false;
        }
    }

    /// <summary>
    /// A folder as typed, pasted or dropped, made usable: its first line, without the quotes and spaces around it, a file:// link
    /// as its path, and a leading ~ as the home folder (as a Linux shell reads it).
    /// </summary>
    internal static string CleanPath(string? text)
    {
        var line = (text ?? "").Split('\n', '\r').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        line = line.Trim('"', '\'').Trim();
        if (line.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(line, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            line = uri.LocalPath;
        }
        if (line == "~" || line.StartsWith("~/", StringComparison.Ordinal) || line.StartsWith(@"~\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0)
            {
                line = line.Length == 1 ? home : Path.Combine(home, line[2..]);
            }
        }
        return line;
    }

    private void Dropped(string path)
    {
        path = CleanPath(path);
        if (_editing)
        {
            StopEditing(apply: false);
        }
        if (_context.CheckGameFolder(path) is not null)
        {
            SetGameFolder(path);
        }
        else if (_context.DescribeRoms(path).Found)
        {
            SetRoms(path);
        }
        else if (File.Exists(path) && path.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase))
        {
            SetSoundFont(path);
        }
        else if (Directory.Exists(path) && (_model.Holds(path, PackKind.Pictures) || _model.Holds(path, PackKind.Sounds)))
        {
            // a folder of pictures, of sounds or of both: it becomes the pack of each kind it holds
            foreach (var kind in new[] { PackKind.Sounds, PackKind.Pictures }.Where(k => _model.Holds(path, k)))
            {
                SetPack(kind, path);
            }
        }
        else
        {
            _message = $"{path} holds neither Darklands (DARKLAND.EXE), Roland ROMs nor a pack (PNG pictures, OGG, WAV or MIDI sounds), and is not a SoundFont (.sf2).";
        }
    }

    private static Item PackItem(PackKind kind) => kind == PackKind.Pictures ? Item.HdPack : Item.AudioPack;

    /// <summary>A pack row: ←/→ (or a click on ‹ ›) go through None and the packs found; Enter or its Browse… asks the system for a folder.</summary>
    private void ChoosePack(PackKind kind, int step, bool cycle)
    {
        var what = kind == PackKind.Pictures ? "a picture (HD) pack: PNG files named after the game's pictures"
            : "a sound pack: OGG or WAV files named after the game's music and effects";
        if (!cycle && _surface.PickFolder is null)
        {
            (_message, _messageColour) = ("No folder dialog here: drop the pack's folder on this window, or ←/→ for the packs found.", PaleBlue);
            return;
        }
        if (!cycle && Pick("Choose the folder of " + what, out var chosen))
        {
            if (chosen is { } picked)
            {
                if (_model.Holds(picked, kind))
                {
                    SetPack(kind, picked);
                }
                else
                {
                    _message = $"{picked} holds no {(kind == PackKind.Pictures ? "PNG pictures" : "OGG or WAV sounds")}.";
                }
            }
            return;
        }
        _model.CyclePack(kind, step);
        PackMessage(kind);
    }

    private void SetPack(PackKind kind, string folder)
    {
        if (IsFixed(PackItem(kind), out var by))
        {
            _message = $"The {(kind == PackKind.Pictures ? "HD" : "audio")} pack is set by {by}.";
            return;
        }
        _model.SetPack(kind, folder);
        _focus = PackItem(kind);
        PackMessage(kind);
    }

    /// <summary>A pack chosen: its state shows on the line under its row (and in full in the details box), not as a message.</summary>
    private void PackMessage(PackKind kind) => _message = null;

    private string PackSummary(PackKind kind, PackStatus status) => kind == PackKind.Pictures
        ? $"{status.Pictures} picture{(status.Pictures == 1 ? "" : "s")}."
        : $"{status.Sounds} sound{(status.Sounds == 1 ? "" : "s")}{(_model.PackOf(kind) is { } f && File.Exists(Path.Combine(f, "audio.json")) ? ", audio.json read" : "")}.";

    private void SetGameFolder(string folder)
    {
        if (IsFixed(Item.GameFolder, out var by))
        {
            _message = $"The game folder is set by {by}.";
            return;
        }
        _model.SetGameFolder(folder);
        _focus = _model.CanPlay ? Item.Play : Item.GameFolder;
    }

    private void PickSoundFont()
    {
        if (_surface.PickFile is not { } pick)
        {
            _message = "Drop a SoundFont (.sf2 file) on this window, or put it in the SOUNDFONT folder next to the program.";
            return;
        }
        try
        {
            if (pick("Choose a General MIDI SoundFont (.sf2 file)") is { } chosen)
            {
                SetSoundFont(CleanPath(chosen));
            }
        }
        catch (NotSupportedException e)
        {
            (_message, _messageColour) = (e.Message, PaleRed);
        }
    }

    private void SetSoundFont(string file)
    {
        if (IsFixed(Item.SoundFont, out var by))
        {
            _message = $"The SoundFont is set by {by}.";
            return;
        }
        if (!file.EndsWith(".sf2", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            _message = $"{file} is not a SoundFont (.sf2 file).";
            return;
        }
        _model.SoundFont = file;
        _focus = Item.SoundFont;
        SoundFontMessage();
    }

    /// <summary>A SoundFont chosen: its state shows on the line under its row, not as a message.</summary>
    private void SoundFontMessage() => _message = null;

    /// <summary>The SoundFont row's value: the file's name; "(default)" when it comes from the SOUNDFONT folder; "none found".</summary>
    private string SoundFontShown()
    {
        if (_model.SoundFont is { } chosen)
        {
            return Path.GetFileName(chosen) + (File.Exists(chosen) ? "" : " (!)");
        }
        return _model.SoundFontFound is { } found ? Path.GetFileName(found) + " (default)" : "none found";
    }

    private void SetRoms(string folder)
    {
        if (IsFixed(Item.Roms, out var by))
        {
            _message = $"The ROM folder is set by {by}.";
            return;
        }
        var status = _context.DescribeRoms(folder);
        if (!status.Found)
        {
            _message = $"No MT-32 or CM-32L ROM files in {folder}.";
            return;
        }
        _model.Mt32Roms = folder;
        _focus = Item.Roms;
    }

    private void StartEditing(string text)
    {
        if (IsFixed(Item.GameFolder, out _))
        {
            return;
        }
        _editing = true;
        _editText = text;
        _editFresh = true;
        _surface.TextInput(true);
    }

    private void StopEditing(bool apply)
    {
        _editing = false;
        _surface.TextInput(false);
        if (apply && CleanPath(_editText) is { Length: > 0 } typed && typed != _model.GameFolder)
        {
            SetGameFolder(typed);
            if (!_model.CanPlay)
            {
                _message = $"{typed} holds no Darklands (DARKLAND.EXE).";
            }
        }
    }

    private void EditKey(KeyPressed key)
    {
        switch (key.Key)
        {
            case LauncherKey.Enter:
            case LauncherKey.Tab:
                StopEditing(apply: true);
                break;
            case LauncherKey.Escape:
                StopEditing(apply: false);
                break;
            case LauncherKey.Backspace when _editText.Length > 0:
                _editText = _editFresh ? "" : _editText[..^1];
                break;
            case LauncherKey.Paste:
                _editText = (_editFresh ? "" : _editText) + CleanPath(_surface.Clipboard());
                break;
        }
        _editFresh = false;
    }

    private void MoveFocus(int step)
    {
        var order = Order.ToList();
        var i = order.IndexOf(_focus);
        _focus = order[(i + step + order.Count) % order.Count];
    }

    private bool IsFixed(Item item, out string by)
    {
        by = "";
        var key = item switch
        {
            Item.GameFolder or Item.Change => "gameFolder",
            Item.Window => "display.resolution",
            Item.Fullscreen => "display.fullscreen",
            Item.HdPack => "display.hdPack",
            Item.AudioPack => "sound.audioPack",
            Item.SoundFont => "sound.soundFont",
            Item.Sound => "sound.card",
            Item.Roms => "sound.mt32Roms",
            Item.Banner => "startup.banner",
            Item.Intro => "startup.intro",
            _ => null,
        };
        return key is not null && _model.IsFixed(key, out by);
    }

    // ---- drawing ----

    /// <summary>The left card and the settings panel, in design pixels (1280 x 720, the painting's shape).</summary>
    private static Rect CardFrame(View v) => v.Rect(136, 196, 414, 508);

    /// <summary>The settings panel, below the frameless window's buttons at the top-right corner.</summary>
    private static Rect SettingsFrame(View v) => v.Rect(768, 46, 494, 660);

    /// <summary>
    /// Where the painting's focus (the castle on its hill: 30.5% across, 34% down the picture) sits in the design: in the gap
    /// between the card and the settings, as in the art direction's mock-up; the painting is enlarged around it to cover the window.
    /// </summary>
    private const double FocusX = 0.305;
    private const double FocusY = 0.34;
    private const double FocusAtX = 620;
    private const double FocusAtY = 318;
    private const double PaintingScale = 1.12;

    /// <summary>Draws the screen on <paramref name="canvas"/> and notes where the controls are.</summary>
    public void Render(Canvas canvas)
    {
        var s = Math.Min((double)canvas.Width / DesignWidth, (double)canvas.Height / DesignHeight);
        var v = new View(s, (int)((canvas.Width - DesignWidth * s) / 2), (int)((canvas.Height - DesignHeight * s) / 2));
        // what never changes (the painting, the title, the parchments, the card's text) is drawn once for each size
        if (_background is null || _background.Width != canvas.Width || _background.Height != canvas.Height)
        {
            _background = new Canvas(canvas.Width, canvas.Height);
            DrawBackground(_background, v);
        }
        _background.Pixels.CopyTo(canvas.Pixels, 0);
        _hotspots = [(_discord, Item.Discord, Part.Row)];
        _blocks = [];
        if (_notesLink is { } notes)
        {
            _hotspots.Add((notes, Item.Notes, Part.Row));
        }
        if (_guideLink is { } guide)
        {
            _hotspots.Add((guide, Item.PackGuide, Part.Row));
        }
        if (_reportLink is { } reportLink)
        {
            _hotspots.Add((reportLink, Item.Report, Part.Row));
        }
        if ((_focus == Item.Discord ? _discord : _focus == Item.Notes ? _notesLink : _focus == Item.PackGuide ? _guideLink : _focus == Item.Report ? _reportLink : null) is { } link)
        {
            // the focused link: a band behind it
            Shade(canvas, new Rect(link.X - v.Len(3), link.Y - v.Len(1), link.Width + v.Len(6), link.Height + v.Len(2)), Ink, 34);
        }
        // a row of the other tab took the focus (Tab, the arrows, a dropped folder): its tab opens
        if (OnAdvanced(_focus))
        {
            _advanced = true;
        }
        else if (OnGeneral(_focus))
        {
            _advanced = false;
        }
        DrawSettings(canvas, v);
        if (_surface.Frameless)
        {
            DrawWindowButtons(canvas, v);
        }
    }

    private void DrawBackground(Canvas canvas, View v)
    {
        canvas.Clear(0x1C1712);
        DrawPainting(canvas, v);
        DrawTitle(canvas, v);
        var card = CardFrame(v);
        PanelShadow(canvas, card, v, "card-left");
        Parchment(canvas, card, v, "card-left", 11);
        DrawCard(canvas, v, card);
        var settings = SettingsFrame(v);
        PanelShadow(canvas, settings, v, "card-settings");
        Parchment(canvas, settings, v, "card-settings", 23);
        if (Assets.Art("card-settings") is null)
        {
            // the panel's art has its own corners
            Corners(canvas, settings, v);
        }
    }

    /// <summary>The painting (or a dusk gradient without it), enlarged around its focus to cover the window, darkened at the edges.</summary>
    private static void DrawPainting(Canvas canvas, View v)
    {
        var painting = Assets.Art("background");
        if (painting is null)
        {
            canvas.Paint(new Rect(0, 0, canvas.Width, canvas.Height), (x, y) =>
                Colours.Mix(0x6E6258, 0x1E1A15, (int)(255 * Math.Clamp((double)y / canvas.Height * 1.3, 0, 1))));
        }
        else
        {
            // the focus's place on the canvas, and the scale that keeps it there and still covers every edge
            var cx = v.OffsetX + FocusAtX * v.Scale;
            var cy = v.OffsetY + FocusAtY * v.Scale;
            double fx = FocusX * painting.Width, fy = FocusY * painting.Height;
            var scale = Math.Max(PaintingScale * DesignWidth / painting.Width * v.Scale,
                Math.Max(Math.Max(cx / fx, (canvas.Width - cx) / (painting.Width - fx)), Math.Max(cy / fy, (canvas.Height - cy) / (painting.Height - fy))));
            var left = fx - cx / scale;
            var top = fy - cy / scale;
            var part = ImageOps.Resized(painting, left, top, canvas.Width / scale, canvas.Height / scale, canvas.Width, canvas.Height);
            canvas.Image(part, new Rect(0, 0, canvas.Width, canvas.Height));
            if (scale > 1.05)
            {
                // enlarged: sharpened a little, so the brushwork still reads (a larger painting is the real cure)
                Sharpen(canvas, Math.Min(0.8, (scale - 1) * 1.6));
            }
        }
        // a little darker overall, more at the edges and behind the title, for the text on it
        double w = canvas.Width, h = canvas.Height;
        var titleX = v.OffsetX + 350 * v.Scale;
        var titleY = v.OffsetY + 110 * v.Scale;
        var titleR = 330 * v.Scale;
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                double dx = (x - w / 2) / (w / 2), dy = (y - h / 2) / (h / 2);
                var edge = Math.Clamp((dx * dx + dy * dy - 0.35) / 1.4, 0, 1);
                double tx = (x - titleX) / titleR, ty = (y - titleY) / (titleR * 0.45);
                var title = Math.Clamp(1 - (tx * tx + ty * ty), 0, 1);
                canvas.Blend(x, y, Black, (int)(55 * edge + 40 * title));
            }
        }
    }

    /// <summary>An unsharp mask over the whole canvas: each channel pushed away from its 3 x 3 average by <paramref name="amount"/>.</summary>
    private static void Sharpen(Canvas canvas, double amount)
    {
        var source = (uint[])canvas.Pixels.Clone();
        int w = canvas.Width, h = canvas.Height;
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                int r = 0, g = 0, b = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var q = source[(y + dy) * w + x + dx];
                        r += (int)(q >> 16 & 255);
                        g += (int)(q >> 8 & 255);
                        b += (int)(q & 255);
                    }
                }
                var c = source[y * w + x];
                int Push(int value, int sum) => Math.Clamp((int)Math.Round(value + amount * (value - sum / 9.0)), 0, 255);
                canvas.Pixels[y * w + x] = 0xFF000000u | (uint)Push((int)(c >> 16 & 255), r) << 16 | (uint)Push((int)(c >> 8 & 255), g) << 8 | (uint)Push((int)(c & 255), b);
            }
        }
    }

    /// <summary>"Darklands" and "Restoration Project" over the painting, with a fleuron rule under them.</summary>
    private void DrawTitle(Canvas canvas, View v)
    {
        // as in the art direction: "Restoration Project" as wide as "Darklands" under it, both in cream on a soft shadow
        var big = Face(Assets.Display, 80, v);
        var sub = Face(Assets.Strong, 40, v);
        var centre = v.OffsetX + v.Len(343);
        var y = v.OffsetY + v.Len(18);
        var shadow = Math.Max(1, v.Len(2.5));
        big.Draw(canvas, "Darklands", centre - big.Measure("Darklands") / 2, y, Cream, shadow, Black);
        y += (int)(big.LineHeight * 0.80);
        sub.Draw(canvas, "Restoration Project", centre - sub.Measure("Restoration Project") / 2, y, Cream, shadow, Black);
        y += sub.LineHeight + v.Len(10);
        Fleuron(canvas, v, centre, y, v.Len(190), Cream, onDark: true);
    }

    /// <summary>The left card: what is new, about the project with its links, and the copyright note at the bottom.</summary>
    private void DrawCard(Canvas canvas, View v, Rect card)
    {
        var x = card.X + v.Len(28);
        var right = card.Right - v.Len(26);
        var width = right - x;
        var y = card.Y + v.Len(26);
        var text = Face(Assets.Text, 15, v);
        var small = Face(Assets.Text, 11.5, v);
        var line = (int)Math.Round(text.LineHeight * 1.05);
        var bottomNote = small.Wrap("Darklands © 1992 MicroProse Software, Inc. Darklands and MicroProse are the property of their "
            + "respective owners. This is an unofficial fan project, not affiliated with or endorsed by them.", width);
        var noteTop = card.Bottom - v.Len(22) - bottomNote.Count * small.LineHeight;

        y = Capitalised(canvas, v, x, y, "What’s new in " + _context.Version, _notes.Count > 0 ? "From the release notes." : "The release notes are not included with this copy.");
        y += v.Len(8);
        if (_notes.Count == 0)
        {
            foreach (var l in text.Wrap("The release notes of this version are on the project's page.", width))
            {
                text.Draw(canvas, l, x, y, Ink);
                y += line;
            }
        }
        // the notes' first items, as many as leave room for the rest of the card
        var reserved = v.Len(250);
        foreach (var item in _notes.Take(4))
        {
            var lines = text.Wrap(item, width - v.Len(16)).Take(2).ToList();
            if (y + lines.Count * line > noteTop - reserved)
            {
                break;
            }
            text.Draw(canvas, "•", x + v.Len(2), y, InkSoft);
            foreach (var l in lines)
            {
                text.Draw(canvas, l, x + v.Len(16), y, Ink);
                y += line;
            }
            y += v.Len(2);
        }
        if (_context.ReleaseNotesLink is not null)
        {
            _notesLink = LinkLine(canvas, v, text, x, y, width, "", "", _notes.Count == 0 ? "Read the release notes" : "All the release notes");
            y += line;
        }
        y += v.Len(4);
        Fleuron(canvas, v, (x + right) / 2, y + v.Len(8), width / 2 - v.Len(10), InkSoft, onDark: false);
        y += v.Len(20);
        y = Capitalised(canvas, v, x, y, "About this project", "A free, open-source fan restoration.");
        y += v.Len(8);
        foreach (var l in text.Wrap("MicroProse’s 1992 Darklands, rebuilt from the original program to run on today’s computers. "
                     + "You need your own copy of the game: nothing from it is included, the program reads your installation.", width))
        {
            text.Draw(canvas, l, x, y, Ink);
            y += line;
        }
        y += v.Len(6);
        _discord = LinkLine(canvas, v, text, x, y, width, "Questions, bugs, ideas: ", "Questions: ", "join us on Discord");
        if (_context.PackGuide is not null)
        {
            y += line;
            _guideLink = LinkLine(canvas, v, text, x, y, width, "Your own pictures, music and sounds: ", "Your own art: ", "how to make a pack");
        }
        if (_context.ReportProblem is not null)
        {
            y += line;
            _reportLink = LinkLine(canvas, v, text, x, y, width, "Something broken? ", "", "make a problem report");
        }

        Fleuron(canvas, v, (x + right) / 2, noteTop - v.Len(12), width / 2 - v.Len(10), InkSoft, onDark: false);
        foreach (var l in bottomNote)
        {
            small.Draw(canvas, l, x, noteTop, InkSoft);
            noteTop += small.LineHeight;
        }
    }

    /// <summary>A line with a link: the link icon, a lead-in (the shorter one when the line would not fit) and the underlined link; returns the link's area.</summary>
    private Rect LinkLine(Canvas canvas, View v, TextFace text, int x, int y, int width, string lead, string shortLead, string link)
    {
        var icon = Assets.Art("icon-link") is null ? v.Len(11) : v.Len(16);
        if (icon + v.Len(9) + text.Measure(lead) + text.Measure(link) > width)
        {
            lead = shortLead;
        }
        var iy = y + (text.LineHeight - icon) / 2;
        Icon(canvas, v, "icon-link", new Rect(x, iy, icon, icon), LinkRed);
        var after = text.Draw(canvas, lead, x + icon + v.Len(9), y, Ink);
        text.Draw(canvas, link, after, y, LinkRed);
        var under = y + text.Ascent + Math.Max(1, v.Len(2));
        canvas.Fill(new Rect(after, under, text.Measure(link), Math.Max(1, v.Len(1))), LinkRed);
        return new Rect(after, y, text.Measure(link), under + Math.Max(1, v.Len(1)) - y);
    }

    /// <summary>A heading with its first letter in a red illuminated square, and an italic caption under it; returns the y below.</summary>
    private int Capitalised(Canvas canvas, View v, int x, int y, string heading, string caption)
    {
        var box = new Rect(x, y, v.Len(46), v.Len(46));
        if (Assets.Art("dropcap") is { } frame)
        {
            ImageOps.Draw(canvas, frame, box);
        }
        else
        {
            canvas.Fill(box, DropCapRed);
            canvas.Frame(box, Math.Max(1, v.Len(2)), DropCapDark);
            canvas.Frame(box.Inset(Math.Max(2, v.Len(4))), Math.Max(1, v.Len(1)), Gold);
        }
        var capital = Face(Assets.Strong, 38, v);
        var letter = heading.AsSpan(0, 1);
        StrongText(canvas, v, capital, letter, box.X + (box.Width - capital.Measure(letter)) / 2, box.Y + (box.Height - capital.LineHeight) / 2 + v.Len(1), Cream);
        var face = Face(Assets.Strong, 25, v);
        var under = Face(Assets.Italic, 14, v);
        StrongText(canvas, v, face, heading.AsSpan(1), box.Right + v.Len(10), y - v.Len(1), Ink);
        under.Draw(canvas, caption, box.Right + v.Len(10), y + face.LineHeight - v.Len(3), InkSoft);
        return Math.Max(box.Bottom, y + face.LineHeight - v.Len(3) + under.LineHeight);
    }

    /// <summary>
    /// The settings panel: the title, the General and Advanced tabs, and the open tab's sections with their icons (General: Game
    /// installation, Display, Sound &amp; Music; Advanced: the Roland ROMs, the SoundFont, Start-up). It is drawn on one grid: rows of
    /// one height, a status line of its own height under a folder or pack, the same gap before each section, the labels, the values
    /// between ‹ › and the small plates (Browse…, Change…, Test sound) in three columns. Under the rows come the hint bar, then
    /// Save and quit at the left and Play, the larger, at the right. Every text is cut or wrapped to its place.
    /// </summary>
    private void DrawSettings(Canvas canvas, View v)
    {
        var frame = SettingsFrame(v);
        _panel = frame.Inset(v.Len(12));
        var x0 = frame.X + v.Len(34);
        var x1 = frame.Right - v.Len(34);
        var plateWidth = v.Len(PlateWidth);
        var layout = new RowLayout(
            x0 + v.Len(30),
            x1 - plateWidth,
            x0 + v.Len(132),
            x1 - plateWidth - v.Len(16),
            x1 - plateWidth - v.Len(16) - v.Len(84),
            x1);
        var title = Face(Assets.Display, 44, v);
        var heading = Face(Assets.Strong, 20.5, v);
        var text = Face(Assets.Text, 16, v);
        var line = Face(Assets.Italic, 13, v);
        var small = Face(Assets.Text, 13, v);
        var rowHeight = v.Len(RowHeight);
        var plateHeight = v.Len(PlateHeight);

        var y = frame.Y + v.Len(16);
        title.Draw(canvas, "Settings", x0, y, Ink);
        // the letters' own height: the display face's line box has much room above and below them
        Leaf(new Rect(x0, y + title.LineHeight / 5, title.Measure("Settings"), title.LineHeight * 3 / 5), "title");
        var ruleLeft = x0 + title.Measure("Settings") + v.Len(16);
        Fleuron(canvas, v, (ruleLeft + x1) / 2, y + title.LineHeight / 2, (x1 - ruleLeft) / 2, InkSoft, onDark: false);
        y += title.LineHeight - v.Len(4);
        y = Tabs(canvas, v, heading, x0, x1, y);

        int Section(string name, string icon, int top)
        {
            top += v.Len(SectionGap);
            var height = v.Len(HeadingHeight);
            var size = v.Len(22);
            Icon(canvas, v, icon, new Rect(x0, top + (height - size) / 2, size, size), InkSoft);
            var ty = top + (height - heading.LineHeight) / 2;
            StrongText(canvas, v, heading, name, x0 + v.Len(30), ty, Ink);
            Leaf(new Rect(x0, ty, v.Len(30) + heading.Measure(name) + (Assets.HasStrong ? 0 : v.Len(1)), heading.LineHeight - v.Len(3)), "heading " + name);
            return top + height;
        }

        int Status(int top, (string Text, uint Colour) status)
        {
            var height = v.Len(StatusHeight);
            var size = v.Len(12);
            var iconArea = new Rect(layout.Label, top + (height - size) / 2, size, size);
            if (status.Colour == PaleRed)
            {
                Icon(canvas, v, "icon-problem", iconArea, InkRed);
            }
            else if (status.Colour == Green)
            {
                Icon(canvas, v, "icon-found", iconArea, InkGreen);
            }
            var tx = layout.Label + size + v.Len(6);
            var fitted = Fit(line, status.Text, layout.Right - tx, fromEnd: false);
            var ty = top + (height - line.LineHeight) / 2;
            line.Draw(canvas, fitted, tx, ty, OnParchment(status.Colour));
            Leaf(new Rect(layout.Label, Math.Max(top, ty), tx + line.Measure(fitted) - layout.Label, Math.Min(height, line.LineHeight)), "status " + status.Text);
            return top + height;
        }

        // a row with its value between ‹ ›, and a plate at its end (its own Browse…, or another item's: Test sound)
        int Select(int top, string label, string value, Item item, string? plate = null, Item? plateItem = null, bool narrow = false)
        {
            var l = narrow ? layout with { Select = layout.SelectNarrow, SelectEnd = layout.Right } : layout;
            if (plate is not null)
            {
                var owner = plateItem ?? item;
                Plate(canvas, v, new Rect(layout.Plate, top + (rowHeight - plateHeight) / 2, plateWidth, plateHeight), plate, small, owner, owner == item ? Part.Browse : Part.Row);
            }
            var row = new Rect(l.Label - v.Len(8), top, (plate is null ? l.Right : l.SelectEnd + v.Len(6)) - l.Label + v.Len(8), rowHeight);
            if (_focus == item)
            {
                Shade(canvas, row, Ink, 26);
            }
            Hot(row, item, Part.Row);
            var ty = top + (rowHeight - text.LineHeight) / 2;
            var labelShown = Fit(text, label, l.Select - l.Label - v.Len(10), fromEnd: false);
            text.Draw(canvas, labelShown, l.Label, ty, Ink);
            Leaf(new Rect(l.Label, ty, text.Measure(labelShown), text.LineHeight), "label " + label);

            var fixedBy = IsFixed(item, out _);
            var arrowLeft = fixedBy ? "" : "‹";
            var arrowRight = fixedBy ? "" : "›";
            var gap = v.Len(8);
            var room = l.SelectEnd - l.Select - text.Measure(arrowLeft) - text.Measure(arrowRight) - 2 * gap;
            var shown = Fit(text, value, Math.Max(0, room), fromEnd: false);
            var inner = l.Select + text.Measure(arrowLeft) + gap;
            text.Draw(canvas, arrowLeft, l.Select, ty, InkSoft);
            text.Draw(canvas, shown, inner + (room - text.Measure(shown)) / 2, ty, fixedBy ? InkSoft : Ink);
            text.Draw(canvas, arrowRight, l.SelectEnd - text.Measure(arrowRight), ty, InkSoft);
            Leaf(new Rect(l.Select, ty, l.SelectEnd - l.Select, text.LineHeight), "value " + label);
            if (!fixedBy)
            {
                // the arrows: a click on ‹ or › steps back or on, as ←/→
                Hot(new Rect(l.Select - v.Len(6), row.Y, inner - l.Select + v.Len(6), row.Height), item, Part.Previous);
                Hot(new Rect(l.SelectEnd - text.Measure(arrowRight) - gap, row.Y, text.Measure(arrowRight) + gap + v.Len(6), row.Height), item, Part.Next);
            }
            return top + rowHeight;
        }

        // a folder: its field (under the heading, or after a label) and its Change… plate
        int Folder(int top, string? label, string shown, Item item, Item plateItem, Part platePart)
        {
            var fieldLeft = label is null ? layout.Label : layout.Select;
            if (label is not null)
            {
                if (_focus == item)
                {
                    Shade(canvas, new Rect(layout.Label - v.Len(8), top, layout.SelectEnd - layout.Label + v.Len(14), rowHeight), Ink, 26);
                }
                var labelY = top + (rowHeight - text.LineHeight) / 2;
                var labelShown = Fit(text, label, layout.Select - layout.Label - v.Len(10), fromEnd: false);
                text.Draw(canvas, labelShown, layout.Label, labelY, Ink);
                Leaf(new Rect(layout.Label, labelY, text.Measure(labelShown), text.LineHeight), "label " + label);
            }
            var fieldHeight = Math.Min(rowHeight - v.Len(4), v.Len(27));
            var field = new Rect(fieldLeft, top + (rowHeight - fieldHeight) / 2, layout.SelectEnd - fieldLeft, fieldHeight);
            Field(canvas, v, field, shown, _focus == item, IsFixed(item, out _), label is null ? text : small);
            Hot(field, item, Part.Row);
            Leaf(field, "field " + (label ?? "game folder"));
            Plate(canvas, v, new Rect(layout.Plate, top + (rowHeight - plateHeight) / 2, plateWidth, plateHeight), "Change…", small, plateItem, platePart);
            return top + rowHeight;
        }

        if (!_advanced)
        {
            y = Section("Game installation", "icon-game", y);
            y = Folder(y, null, _editing ? _editText + "_" : _model.GameFolder ?? "", Item.GameFolder, Item.Change, Part.Row);
            y = Status(y, _model.GameFolderChecked is not null
                ? ("Found" + (_model.GameFolderSource is { } source ? $" ({source})." : "."), Green)
                : _model.GameFolder is null ? ("Type its folder, drop it on this window or press Change…", PaleBlue) : ("No Darklands there (DARKLAND.EXE).", PaleRed));

            y = Section("Display", "icon-display", y);
            y = Select(y, "Window size", LauncherModel.SizeLabel(_model.Windows[_model.WindowIndex]), Item.Window);
            y = Select(y, "Fullscreen", _model.Fullscreen ? "Yes" : "No", Item.Fullscreen);
            y = Select(y, "HD pack", PackShown(PackKind.Pictures), Item.HdPack, "Browse…");
            y = Status(y, PackLine(PackKind.Pictures));

            // sound and music: the card with its sound test, whether its ROMs are there (they are chosen on Advanced), the audio pack
            y = Section("Sound & Music", "icon-sound", y);
            var test = _context.SoundTest is { } soundTest ? (soundTest.Playing ? "Stop" : "Test sound") : null;
            y = Select(y, "Sound card", _model.SoundCardLabel, Item.Sound, test, Item.Test);
            y = Status(y, RomsStatus(_model.Roms));
            y = Select(y, "Audio pack", PackShown(PackKind.Sounds), Item.AudioPack, "Browse…");
            y = Status(y, PackLine(PackKind.Sounds));
        }
        else
        {
            y = Section("Roland MT-32 / CM-32L", "icon-sound", y);
            y = Folder(y, "ROM folder", _model.Roms.Folder, Item.Roms, Item.Roms, Part.Browse);
            y = Status(y, RomsStatus(_model.Roms));
            y = Section("General MIDI (for audio packs)", "icon-sound", y);
            y = Select(y, "SoundFont", SoundFontShown(), Item.SoundFont, "Browse…");
            y = Status(y, SoundFontLine());
            y = Section("Start-up", "icon-startup", y);
            y = Select(y, "Original start-up screen", _model.Banner ? "Yes" : "No", Item.Banner, narrow: true);
            y = Select(y, "Opening animation", _model.Intro ? "Yes" : "No", Item.Intro, narrow: true);
            y = Select(y, "Show launcher at every start", _model.Always ? "Yes" : "No", Item.Always, narrow: true);
        }

        // the panel's foot: Play at the right, the larger; Save and quit at the left, apart from it
        var play = new Rect(x1 - v.Len(212), frame.Bottom - v.Len(26) - v.Len(56), v.Len(212), v.Len(56));
        BigPlate(canvas, v, play, "Play", red: true, Item.Play);
        BigPlate(canvas, v, new Rect(x0, play.Y + v.Len(8), v.Len(160), v.Len(40)), "Save and quit", red: false, Item.Save);

        // over the buttons, a gap above and below: the hint bar (two lines)
        var barBottom = play.Y - v.Len(16);
        var barTop = Math.Max(y + v.Len(12), barBottom - v.Len(50));
        DrawHints(canvas, v, Face(Assets.Italic, 14, v), new Rect(x0, barTop, x1 - x0, barBottom - barTop));
    }

    /// <summary>The settings grid, in design pixels.</summary>
    private const double RowHeight = 31, StatusHeight = 18, SectionGap = 14, HeadingHeight = 28, PlateWidth = 88, PlateHeight = 24;

    /// <summary>The panel's two pages, General and Advanced: their names, the open one in ink and underlined in red, over a rule.</summary>
    private int Tabs(Canvas canvas, View v, TextFace face, int x0, int x1, int y)
    {
        var height = v.Len(34);
        var x = x0;
        foreach (var (name, item, open) in new[] { ("General", Item.TabGeneral, !_advanced), ("Advanced", Item.TabAdvanced, _advanced) })
        {
            var width = face.Measure(name);
            var ty = y + (height - face.LineHeight) / 2;
            if (_hover == item && !open)
            {
                Shade(canvas, new Rect(x - v.Len(6), y + v.Len(2), width + v.Len(12), height - v.Len(6)), Ink, 20);
            }
            StrongText(canvas, v, face, name, x, ty, open ? Ink : InkSoft);
            if (open)
            {
                canvas.Fill(new Rect(x - v.Len(2), y + height - v.Len(4), width + v.Len(4), Math.Max(2, v.Len(3))), DropCapRed);
            }
            Hot(new Rect(x - v.Len(8), y, width + v.Len(16), height), item, Part.Row);
            Leaf(new Rect(x - v.Len(2), y + v.Len(2), width + v.Len(4), height - v.Len(2)), "tab " + name);
            x += width + v.Len(30);
        }
        Shade(canvas, new Rect(x0, y + height - v.Len(1), x1 - x0, Math.Max(1, v.Len(1))), InkSoft, 120);
        return y + height;
    }

    /// <summary>The columns of the settings' rows: the labels, the plates, the value selectors (wide, or narrow for Yes / No).</summary>
    private readonly record struct RowLayout(int Label, int Plate, int Select, int SelectEnd, int SelectNarrow, int Right);

    /// <summary>
    /// A frameless window's own buttons at its top-right corner: minimise (left out when the window fills the screen, where it would do
    /// nothing: fullscreen, or gamescope's Game Mode) and close. Small dark plates, lit under the pointer, sunk while held.
    /// </summary>
    private void DrawWindowButtons(Canvas canvas, View v)
    {
        var width = Math.Max(20, v.Len(30));
        var height = Math.Max(17, v.Len(26));
        var margin = Math.Max(5, v.Len(8));
        var close = new Rect(canvas.Width - margin - width, margin, width, height);
        var buttons = _surface.FillsScreen
            ? new[] { (close, Item.Close) }
            : [(new Rect(close.X - Math.Max(4, v.Len(6)) - width, margin, width, height), Item.Minimise), (close, Item.Close)];
        foreach (var (area, item) in buttons)
        {
            var lit = _hover == item;
            var down = _pressed == item;
            var name = down && Assets.Art("plate-small-pressed") is not null ? "plate-small-pressed"
                : lit && Assets.Art("plate-small-hover") is not null ? "plate-small-hover" : "plate-small";
            if (Assets.Art(name) is { } art)
            {
                Sliced(canvas, v, art, name, area);
            }
            else
            {
                canvas.Fill(area, lit ? 0x4A4036U : 0x2C251EU);
                canvas.Frame(area, Math.Max(1, v.Len(1)), lit ? Gold : 0x16110C);
            }
            var sink = down ? Math.Max(1, v.Len(1)) : 0;
            var cx = area.X + area.Width / 2 + sink;
            var cy = area.Y + area.Height / 2 + sink;
            var stroke = Math.Max(1, v.Len(2));
            var half = Math.Max(3, v.Len(6));
            var ink = item == Item.Close && lit ? PaleRed : Cream;
            if (item == Item.Minimise)
            {
                canvas.Fill(new Rect(cx - half, cy + half / 2, 2 * half, stroke), ink);
            }
            else
            {
                for (var i = -half; i <= half; i++)
                {
                    canvas.Fill(new Rect(cx + i - stroke / 2, cy + i - stroke / 2, stroke, stroke), ink);
                    canvas.Fill(new Rect(cx + i - stroke / 2, cy - i - stroke / 2, stroke, stroke), ink);
                }
            }
            Hot(area, item, Part.Row);
        }
    }

    /// <summary>A folder's field: a lighter inset on the parchment, the path cut at its start to keep its end.</summary>
    private void Field(Canvas canvas, View v, Rect field, string shown, bool focused, bool fixedBy, TextFace text)
    {
        canvas.Fill(field, FieldFill);
        canvas.Frame(field, Math.Max(1, v.Len(1)), focused ? Gold : InkSoft);
        if (focused)
        {
            canvas.Frame(field.Inset(Math.Max(1, v.Len(1))), Math.Max(1, v.Len(1)), Gold);
        }
        var fitted = Fit(text, shown, field.Width - v.Len(16), fromEnd: true);
        text.Draw(canvas, fitted, field.X + v.Len(8), field.Y + (field.Height - text.LineHeight) / 2, fixedBy ? InkSoft : Ink);
    }

    /// <summary>A small dark plate (Browse…, Change…, Test sound): plate-small's art, or drawn; framed in gold when its row has the focus.</summary>
    private void Plate(Canvas canvas, View v, Rect area, string label, TextFace text, Item item, Part part)
    {
        var lit = _focus == item || _hover == item && _hoverPart == part;
        var down = _pressed == item && _pressedPart == part;
        var plateName = down && Assets.Art("plate-small-pressed") is not null ? "plate-small-pressed"
            : lit && Assets.Art("plate-small-hover") is not null ? "plate-small-hover" : "plate-small";
        if (Assets.Art(plateName) is { } art)
        {
            Sliced(canvas, v, art, plateName, area);
        }
        else
        {
            canvas.Paint(area, (_, y) => Colours.Mix(0x4A4036, 0x2C251E, area.Height <= 1 ? 0 : 255 * y / (area.Height - 1)));
            canvas.Fill(new Rect(area.X, area.Y, area.Width, Math.Max(1, v.Len(1))), 0x7A6B58);
            canvas.Frame(area, Math.Max(1, v.Len(1)), 0x16110C);
        }
        if (_focus == item && Assets.Art("plate-small-hover") is null || _hover == item && _hoverPart == part && Assets.Art("plate-small") is null)
        {
            canvas.Frame(area, Math.Max(1, v.Len(1.5)), Gold);
        }
        if (down && Assets.Art("plate-small-pressed") is null)
        {
            Shade(canvas, area, Black, 70);
        }
        var shown = Fit(text, label, area.Width - v.Len(6), fromEnd: false);
        var sink = down ? Math.Max(1, v.Len(1)) : 0;
        text.Draw(canvas, shown, area.X + (area.Width - text.Measure(shown)) / 2 + sink, area.Y + (area.Height - text.LineHeight) / 2 + sink, Cream);
        Hot(area, item, part);
        Leaf(area, "button " + label);
    }

    /// <summary>Save and quit (a stone plate) and Play (a red plate with gold), from their art or drawn.</summary>
    private void BigPlate(Canvas canvas, View v, Rect area, string label, bool red, Item item)
    {
        if (Assets.Art(red ? "plate-red" : "plate-stone") is { } art)
        {
            Sliced(canvas, v, art, red ? "plate-red" : "plate-stone", area);
        }
        else
        {
            var (top, bottom, edge) = red ? (0xA2281AU, 0x5E0E08U, 0x2E0603U) : (0xDAD3C5U, 0x9E9687U, 0x3E372EU);
            canvas.Paint(area, (_, y) => Colours.Mix(top, bottom, area.Height <= 1 ? 0 : 255 * y / (area.Height - 1)));
            canvas.Frame(area, Math.Max(1, v.Len(2)), edge);
            canvas.Frame(area.Inset(Math.Max(2, v.Len(4))), Math.Max(1, v.Len(1)), red ? Gold : 0xF4EFE4);
        }
        if (_focus == item || _hover == item)
        {
            canvas.Frame(new Rect(area.X - v.Len(3), area.Y - v.Len(3), area.Width + v.Len(6), area.Height + v.Len(6)), Math.Max(1, v.Len(2)), Gold);
        }
        var down = _pressed == item;
        if (down)
        {
            // held down: darker, and its word sunk by a pixel
            Shade(canvas, area.Inset(Math.Max(1, v.Len(2))), Black, 60);
        }
        // both in the text face ("Play" a little larger, the main button)
        var face = red ? Face(Assets.Strong, 23, v) : Face(Assets.Strong, 19, v);
        var colour = red ? (_model.CanPlay ? Cream : 0xC9B9A0) : Ink;
        var textX = area.X + (area.Width - face.Measure(label)) / 2 + (down ? Math.Max(1, v.Len(1)) : 0);
        var textY = area.Y + (area.Height - face.LineHeight) / 2 + (down ? Math.Max(1, v.Len(1)) : 0);
        if (red)
        {
            face.Draw(canvas, label, textX, textY, colour, Math.Max(1, v.Len(1.5)), Black);
            var d = v.Len(5);
            Diamond(canvas, textX - v.Len(22), area.Y + area.Height / 2, d, Gold);
            Diamond(canvas, textX + face.Measure(label) + v.Len(22), area.Y + area.Height / 2, d, Gold);
        }
        else
        {
            StrongText(canvas, v, face, label, textX, textY, colour);
        }
        Hot(area, item, Part.Row);
        Leaf(area, "button " + label);
    }

    /// <summary>
    /// The hint bar: the last message (or why the launcher is shown), the pointed-at or chosen row's problem in full, and what it
    /// does; wrapped to the bar (long paths broken anywhere), the last line cut with "…" when there is more.
    /// </summary>
    private void DrawHints(Canvas canvas, View v, TextFace face, Rect bar)
    {
        var pad = v.Len(5);
        var room = (bar.Height - 2 * pad) / face.LineHeight;
        if (room <= 0)
        {
            return;
        }
        if (Assets.Art("hint-bar") is { } art)
        {
            Sliced(canvas, v, art, "hint-bar", bar);
        }
        else
        {
            Shade(canvas, bar, HintFill, 215);
            canvas.Frame(bar, Math.Max(1, v.Len(1)), 0x8A7454);
        }
        Leaf(bar, "hints");
        var icon = v.Len(16);
        Icon(canvas, v, "icon-problem", new Rect(bar.X + pad, bar.Y + pad + (face.LineHeight - icon) / 2, icon, icon), Cream);
        var textX = bar.X + pad + icon + v.Len(10);
        var lineWidth = bar.Right - pad - textX;
        var lines = new List<(string Text, uint Colour)>();
        foreach (var (part, colour) in Details())
        {
            lines.AddRange(WrapAll(face, part, lineWidth).Select(l => (l, colour)));
        }
        if (lines.Count > room)
        {
            var last = lines[room - 1];
            lines = [.. lines.Take(room - 1), (Fit(face, last.Text + " …", lineWidth, fromEnd: false), last.Colour)];
        }
        // one line: centred in the bar's height
        var ly = lines.Count == 1 ? bar.Y + (bar.Height - face.LineHeight) / 2 : bar.Y + pad;
        foreach (var (text, colour) in lines)
        {
            face.Draw(canvas, text, textX, ly, colour);
            ly += face.LineHeight;
        }
    }

    /// <summary>The hint bar's parts, most important first: what the pointer is over, else the focused control.</summary>
    private IEnumerable<(string Text, uint Colour)> Details()
    {
        if (_showSound && _context.SoundTest is { Status: { } soundStatus } soundTester)
        {
            yield return (soundStatus, soundTester.Playing ? Green : PaleRed);
        }
        else if (_message is not null)
        {
            yield return (_message, _messageColour);
        }
        else if (_context.Reason switch
                 {
                     LauncherReason.StartFailed => _context.Problem ?? "The game could not start.",
                     LauncherReason.GameNotFound when !_model.CanPlay => "Darklands was not found: choose its folder to play.",
                     LauncherReason.FirstRun => "Welcome! Check the settings, then press Play.",
                     _ => null,
                 } is { } note)
        {
            yield return (note, _context.Reason == LauncherReason.FirstRun ? Green : PaleRed);
        }
        var subject = _hover != Item.None ? _hover : _focus;
        if (subject is Item.HdPack or Item.AudioPack && PackProblem(subject == Item.HdPack ? PackKind.Pictures : PackKind.Sounds) is { } problem)
        {
            yield return ("Problem: " + problem, PaleRed);
        }
        if (Help(subject) is { Length: > 0 } help)
        {
            yield return (help, PaleBlue);
        }
    }

    /// <summary>A message or status colour (chosen for the dark hint bar) as ink on parchment.</summary>
    private static uint OnParchment(uint colour) => colour switch
    {
        PaleRed => InkRed,
        Green => InkGreen,
        _ => InkSoft,
    };

    // ---- the stand-ins for the art, and small ornaments ----

    /// <summary>A parchment card: its art scaled to <paramref name="area"/>, or parchment drawn with ragged, burnt edges.</summary>
    private static void Parchment(Canvas canvas, Rect area, View v, string art, int seed)
    {
        if (Assets.Art(art) is { } image)
        {
            Sliced(canvas, v, image, art, area);
            return;
        }
        var scale = Math.Max(0.01, v.Scale);
        for (var y = Math.Max(0, area.Y); y < Math.Min(canvas.Height, area.Bottom); y++)
        {
            for (var x = Math.Max(0, area.X); x < Math.Min(canvas.Width, area.Right); x++)
            {
                double dx = (x - area.X) / scale, dy = (y - area.Y) / scale;
                double w = area.Width / scale, h = area.Height / scale;
                var edge = Math.Min(Math.Min(dx, w - dx), Math.Min(dy, h - dy));
                // the ragged edge: up to 5 design pixels eaten, varying along the border
                var along = dx < 8 || w - dx < 8 ? dy : dx;
                var ragged = 1.5 + 3.5 * Noise(along / 9, (dx < 8 ? 1 : w - dx < 8 ? 2 : dy < 8 ? 3 : 4) * 17, seed);
                if (edge < ragged)
                {
                    continue;
                }
                var grain = 0.55 * Noise(dx / 45, dy / 45, seed) + 0.3 * Noise(dx / 11, dy / 11, seed + 1) + 0.15 * Noise(dx / 3, dy / 3, seed + 2);
                var colour = Colours.Mix(ParchmentLight, ParchmentDark, (int)(255 * Math.Clamp(grain * 1.1 - 0.15, 0, 1)));
                var burn = Math.Clamp(1 - (edge - ragged) / 26, 0, 1);
                colour = Colours.Mix(colour, ParchmentBurn, (int)(170 * burn * burn));
                canvas.Blend(x, y, colour, 255);
            }
        }
    }

    /// <summary>A soft shadow under a card, on the painting.</summary>
    private static readonly Dictionary<string, Image> Silhouettes = [];

    /// <summary>
    /// A soft shadow under a panel, the shape of its art (ragged edges, curled corners): the art's outline, blurred and moved
    /// a little down and right; without the art, the drawn card's rectangular shadow.
    /// </summary>
    private static void PanelShadow(Canvas canvas, Rect area, View v, string art)
    {
        if (Assets.Art(art) is not { } image)
        {
            CardShadow(canvas, area, v);
            return;
        }
        Image silhouette;
        lock (Silhouettes)
        {
            if (!Silhouettes.TryGetValue(art, out silhouette!))
            {
                var white = new uint[image.Width * image.Height];
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        white[y * image.Width + x] = image.At(x, y) & 0xFF000000 | 0xFFFFFF;
                    }
                }
                Silhouettes[art] = silhouette = new Image(image.Width, image.Height, white);
            }
        }
        var blur = Math.Max(1, v.Len(9));
        var margin = 3 * blur;
        var mask = new Canvas(area.Width + 2 * margin, area.Height + 2 * margin);
        mask.Clear(0);
        Sliced(mask, v, silhouette, art, new Rect(margin, margin, area.Width, area.Height));
        // the coverage (0..255), blurred by two box passes each way
        var w = mask.Width;
        var h = mask.Height;
        var a = new int[w * h];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = (int)(mask.Pixels[i] & 0xFF);
        }
        var t = new int[w * h];
        for (var pass = 0; pass < 2; pass++)
        {
            BoxBlur(a, t, w, h, blur, horizontal: true);
            BoxBlur(t, a, w, h, blur, horizontal: false);
        }
        int dx = v.Len(4), dy = v.Len(7);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (a[y * w + x] > 0)
                {
                    canvas.Blend(area.X - margin + x + dx, area.Y - margin + y + dy, Black, a[y * w + x] * 150 / 255);
                }
            }
        }
    }

    /// <summary>One pass of a box blur of <paramref name="radius"/> along rows or columns, from <paramref name="from"/> into <paramref name="to"/>.</summary>
    private static void BoxBlur(int[] from, int[] to, int w, int h, int radius, bool horizontal)
    {
        int lines = horizontal ? h : w, length = horizontal ? w : h, span = 2 * radius + 1;
        for (var line = 0; line < lines; line++)
        {
            int At(int i) => horizontal ? from[line * w + Math.Clamp(i, 0, w - 1)] : from[Math.Clamp(i, 0, h - 1) * w + line];
            var sum = 0;
            for (var i = -radius; i <= radius; i++)
            {
                sum += At(i);
            }
            for (var i = 0; i < length; i++)
            {
                to[horizontal ? line * w + i : i * w + line] = sum / span;
                sum += At(i + radius + 1) - At(i - radius);
            }
        }
    }

    private static void CardShadow(Canvas canvas, Rect area, View v)
    {
        var reach = Math.Max(2, v.Len(14));
        var offset = v.Len(6);
        for (var y = area.Y - reach + offset; y < area.Bottom + reach + offset; y++)
        {
            for (var x = area.X - reach + offset; x < area.Right + reach + offset; x++)
            {
                var dx = Math.Max(Math.Max(area.X + offset - x, x - (area.Right + offset - 1)), 0);
                var dy = Math.Max(Math.Max(area.Y + offset - y, y - (area.Bottom + offset - 1)), 0);
                var d = Math.Sqrt(dx * dx + dy * dy) / reach;
                if (d < 1)
                {
                    canvas.Blend(x, y, Black, (int)(150 * (1 - d) * (1 - d)));
                }
            }
        }
    }

    /// <summary>The settings panel's corner pieces: the corner art mirrored to each corner, or drawn iron brackets.</summary>
    private static void Corners(Canvas canvas, Rect area, View v)
    {
        var size = Assets.Art("corner") is null ? v.Len(34) : v.Len(60);
        var inset = Assets.Art("corner") is null ? v.Len(2) : -v.Len(9);
        if (Assets.Art("corner") is { } corner)
        {
            foreach (var (h, vert) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                var x = h ? area.Right - inset - size : area.X + inset;
                var y = vert ? area.Bottom - inset - size : area.Y + inset;
                ImageOps.Draw(canvas, ImageOps.Mirrored(corner, h, vert), new Rect(x, y, size, size));
            }
            return;
        }
        var bar = Math.Max(2, v.Len(5));
        var length = v.Len(30);
        foreach (var (h, vert) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var cx = h ? area.Right - inset - bar : area.X + inset;
            var cy = vert ? area.Bottom - inset - bar : area.Y + inset;
            var horizontal = new Rect(h ? cx + bar - length : cx, cy, length, bar);
            var upright = new Rect(cx, vert ? cy + bar - length : cy, bar, length);
            foreach (var piece in new[] { horizontal, upright })
            {
                canvas.Fill(piece, 0x2A231C);
                canvas.Frame(piece, Math.Max(1, v.Len(1)), 0x0F0C09);
            }
            var stud = Math.Max(2, v.Len(4));
            canvas.Fill(new Rect(cx + (bar - stud) / 2, cy + (bar - stud) / 2, stud, stud), Gold);
        }
    }

    /// <summary>One of the small icons (art "icon-…", else a drawn sign in <paramref name="ink"/>).</summary>
    private static void Icon(Canvas canvas, View v, string name, Rect area, uint ink)
    {
        if (Assets.Art(name) is { } art)
        {
            ImageOps.Draw(canvas, art, area);
            return;
        }
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        var r = area.Width / 2;
        var stroke = Math.Max(1, area.Width / 9);
        switch (name)
        {
            case "icon-sound":
                // a quaver: a head, a stem and a flag
                Disc(canvas, cx - r / 3, cy + r / 2, Math.Max(2, r * 2 / 5), ink);
                canvas.Fill(new Rect(cx - r / 3 + Math.Max(2, r * 2 / 5) - stroke, cy - r, stroke, r * 3 / 2), ink);
                canvas.Fill(new Rect(cx - r / 3 + Math.Max(2, r * 2 / 5) - stroke, cy - r, r * 2 / 3, stroke * 2), ink);
                break;
            case "icon-link":
                // a box with an arrow leaving it
                canvas.Frame(new Rect(area.X, area.Y + area.Height / 3, area.Width * 2 / 3, area.Height * 2 / 3), stroke, ink);
                for (var i = 0; i < area.Width * 2 / 3; i++)
                {
                    canvas.Fill(new Rect(area.X + area.Width / 3 + i, area.Bottom - area.Height / 3 - i - stroke, stroke + 1, stroke), ink);
                }
                canvas.Fill(new Rect(area.Right - area.Width / 2, area.Y, area.Width / 2, stroke), ink);
                canvas.Fill(new Rect(area.Right - stroke, area.Y, stroke, area.Height / 2), ink);
                break;
            case "icon-problem":
                // ⓘ: a ring with an i
                Ring(canvas, cx, cy, r, stroke, ink);
                canvas.Fill(new Rect(cx - stroke / 2, cy - r / 2 + stroke, Math.Max(1, stroke), Math.Max(1, stroke)), ink);
                canvas.Fill(new Rect(cx - stroke / 2, cy - r / 2 + 3 * stroke, Math.Max(1, stroke), Math.Max(1, r - 2 * stroke)), ink);
                break;
            case "icon-found":
                // ✓
                for (var i = 0; i <= r / 2; i++)
                {
                    canvas.Fill(new Rect(cx - r + i, cy + i - stroke, stroke + 1, stroke + 1), ink);
                }
                for (var i = 0; i <= r * 3 / 2; i++)
                {
                    canvas.Fill(new Rect(cx - r / 2 + i, cy + r / 2 - i - stroke, stroke + 1, stroke + 1), ink);
                }
                break;
            default:
                // a cross with pointed arms (the game, the display, the start-up)
                Diamond(canvas, cx, cy - r / 2, r / 2, ink);
                Diamond(canvas, cx, cy + r / 2, r / 2, ink);
                Diamond(canvas, cx - r / 2, cy, r / 2, ink);
                Diamond(canvas, cx + r / 2, cy, r / 2, ink);
                Disc(canvas, cx, cy, Math.Max(1, r / 4), ink);
                break;
        }
    }

    /// <summary>A rule with a fleuron at its middle (art "fleuron", else a drawn diamond between two dots).</summary>
    private static void Fleuron(Canvas canvas, View v, int cx, int cy, int halfWidth, uint ink, bool onDark)
    {
        var thick = Math.Max(1, v.Len(1));
        var gap = v.Len(26);
        if (Assets.Art("fleuron") is { } flower && Assets.Art("rule-left") is { } ruleLeft && Assets.Art("rule-right") is { } ruleRight)
        {
            var fh = v.Len(onDark ? 26 : 20);
            var fw = (int)Math.Round((double)fh * flower.Width / flower.Height);
            ImageOps.Draw(canvas, flower, new Rect(cx - fw / 2, cy - fh / 2, fw, fh));
            var rh = Math.Max(2, v.Len(onDark ? 7 : 5));
            var space = fw / 2 + v.Len(4);
            if (halfWidth > space + rh)
            {
                // the knob end keeps its shape (three times the rule's height), the tapering line stretches
                var knob = ruleLeft.Height * 3;
                ImageOps.ThreeSlice(canvas, ruleLeft, new Rect(cx - halfWidth, cy - rh / 2, halfWidth - space, rh), 0, Math.Min(knob, ruleLeft.Width / 2));
                ImageOps.ThreeSlice(canvas, ruleRight, new Rect(cx + space, cy - rh / 2, halfWidth - space, rh), Math.Min(knob, ruleRight.Width / 2), 0);
            }
            return;
        }
        if (halfWidth > gap)
        {
            canvas.Fill(new Rect(cx - halfWidth, cy, halfWidth - gap, thick), ink);
            canvas.Fill(new Rect(cx + gap, cy, halfWidth - gap, thick), ink);
            Diamond(canvas, cx - halfWidth, cy, v.Len(2.5), ink);
            Diamond(canvas, cx + halfWidth, cy, v.Len(2.5), ink);
        }
        if (Assets.Art("fleuron") is { } art)
        {
            var w = v.Len(44);
            var h = (int)Math.Round((double)w * art.Height / art.Width);
            ImageOps.Draw(canvas, art, new Rect(cx - w / 2, cy - h / 2, w, h));
            return;
        }
        Diamond(canvas, cx, cy, v.Len(6), ink);
        Disc(canvas, cx - v.Len(14), cy, Math.Max(1, v.Len(2)), ink);
        Disc(canvas, cx + v.Len(14), cy, Math.Max(1, v.Len(2)), ink);
        if (onDark)
        {
            Diamond(canvas, cx, cy, v.Len(2.5), 0x3A2E22);
        }
    }

    /// <summary>
    /// A piece of art in its box: in nine slices (its borders at half size: the art is made at twice the design's size), in three
    /// (a plate: its ends kept at the box's height), or stretched whole when it is not sliced.
    /// </summary>
    private static void Sliced(Canvas canvas, View v, Image art, string name, Rect area)
    {
        switch (Assets.Slice(name))
        {
            case { Top: 0, Bottom: 0 } plate:
                ImageOps.ThreeSlice(canvas, art, area, plate.Left, plate.Right);
                break;
            case { } nine:
                ImageOps.NineSlice(canvas, art, area, nine, v.Scale / 2);
                break;
            default:
                ImageOps.Draw(canvas, art, area);
                break;
        }
    }

    private static void Diamond(Canvas canvas, int cx, int cy, int r, uint colour)
    {
        for (var dy = -r; dy <= r; dy++)
        {
            var w = r - Math.Abs(dy);
            canvas.Fill(new Rect(cx - w, cy + dy, 2 * w + 1, 1), colour);
        }
    }

    private static void Disc(Canvas canvas, int cx, int cy, int r, uint colour)
    {
        for (var dy = -r; dy <= r; dy++)
        {
            var w = (int)Math.Round(Math.Sqrt(r * r - dy * dy));
            canvas.Fill(new Rect(cx - w, cy + dy, 2 * w + 1, 1), colour);
        }
    }

    private static void Ring(Canvas canvas, int cx, int cy, int r, int stroke, uint colour)
    {
        for (var y = cy - r; y <= cy + r; y++)
        {
            for (var x = cx - r; x <= cx + r; x++)
            {
                var d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                if (d <= r && d > r - stroke - 0.5)
                {
                    canvas.Blend(x, y, colour, 255);
                }
            }
        }
    }

    /// <summary><paramref name="colour"/> laid over <paramref name="area"/> at <paramref name="alpha"/>.</summary>
    private static void Shade(Canvas canvas, Rect area, uint colour, int alpha)
    {
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                canvas.Blend(x, y, colour, alpha);
            }
        }
    }

    /// <summary>Text in the strong face; thickened by a pixel when the SemiBold file is not embedded.</summary>
    private static void StrongText(Canvas canvas, View v, TextFace face, ReadOnlySpan<char> text, int x, int y, uint colour)
    {
        if (!Assets.HasStrong)
        {
            face.Draw(canvas, text, x + Math.Max(1, v.Len(0.6)), y, colour);
        }
        face.Draw(canvas, text, x, y, colour);
    }

    /// <summary>Smooth value noise in 0..1 (a fixed pattern for each seed).</summary>
    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        double a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed), c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0xFFFFFF;
        }
    }

    /// <summary>The text wrapped at spaces to <paramref name="width"/>; a word longer than a line (a path) is broken where it must.</summary>
    internal static List<string> WrapAll(TextFace face, string text, int width)
    {
        var lines = new List<string>();
        foreach (var line in face.Wrap(text, width))
        {
            var rest = line;
            while (face.Measure(rest) > width && rest.Length > 1)
            {
                // the longest start that fits, broken after a path's separator when there is one in it
                var cut = 1;
                while (cut < rest.Length && face.Measure(rest.AsSpan(0, cut + 1)) <= width)
                {
                    cut++;
                }
                var separator = rest.AsSpan(0, cut).LastIndexOfAny('\\', '/');
                if (separator > cut / 2)
                {
                    cut = separator + 1;
                }
                lines.Add(rest[..cut]);
                rest = rest[cut..];
            }
            lines.Add(rest);
        }
        return lines;
    }

    /// <summary>A pack row's value: the folder's name, marked when it cannot be read as a pack.</summary>
    private string PackShown(PackKind kind) =>
        _model.PackOf(kind) is { } folder && (_model.Pack(folder).Problem is not null || !_model.Holds(folder, kind))
            ? LauncherModel.PackLabel(folder) + " (!)"
            : LauncherModel.PackLabel(_model.PackOf(kind));

    /// <summary>What is wrong with the chosen pack (its manifest could not be read ...), without its folder's path; null when nothing.</summary>
    private string? PackProblem(PackKind kind)
    {
        if (_model.PackOf(kind) is not { } folder)
        {
            return null;
        }
        if (_model.Pack(folder).Problem is { } problem)
        {
            return ShortProblem(folder, problem);
        }
        return _model.Holds(folder, kind) ? null : kind == PackKind.Pictures ? "no PNG pictures in this folder." : "no OGG, WAV or MIDI sounds in this folder.";
    }

    /// <summary>A pack's problem as the row shows it: without "Audio pack:" (the row says it) and with the pack's own files named from its folder.</summary>
    internal static string ShortProblem(string folder, string problem)
    {
        foreach (var prefix in new[] { "Audio pack: ", "HD pack: " })
        {
            if (problem.StartsWith(prefix, StringComparison.Ordinal))
            {
                problem = problem[prefix.Length..];
            }
        }
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        foreach (var form in new[] { trimmed, trimmed.Replace('\\', '/'), trimmed.Replace('/', '\\') }.Distinct())
        {
            problem = problem.Replace(form + Path.DirectorySeparatorChar, "", StringComparison.OrdinalIgnoreCase)
                .Replace(form + Path.AltDirectorySeparatorChar, "", StringComparison.OrdinalIgnoreCase)
                .Replace(form + "\\", "", StringComparison.OrdinalIgnoreCase)
                .Replace(form, Path.GetFileName(trimmed), StringComparison.OrdinalIgnoreCase);
        }
        return problem;
    }

    /// <summary>The line under a pack row: none, what the pack holds, or what is wrong (in full in the hint bar).</summary>
    private (string Text, uint Colour) PackLine(PackKind kind)
    {
        if (_model.PackOf(kind) is not { } folder)
        {
            return (kind == PackKind.Pictures ? "None: the game's own pictures." : "None: the game's own music and sounds.", PaleBlue);
        }
        return PackProblem(kind) is { } problem ? (problem, PaleRed) : (PackSummary(kind, _model.Pack(folder)), Green);
    }

    /// <summary>The line under the SoundFont row: the file that plays the packs' .mid music, or where to get a free one.</summary>
    private (string Text, uint Colour) SoundFontLine()
    {
        if (_model.SoundFont is { } chosen && !File.Exists(chosen))
        {
            return ("Not found: " + chosen, PaleRed);
        }
        if (_model.SoundFontFound is not null)
        {
            return ("Plays the packs' MIDI (.mid) music.", Green);
        }
        return ("None found: any General MIDI .sf2 works (a free one: " + FreeSoundFont + ", MIT); put it in " + SoundFontFolder + ".", PaleBlue);
    }

    /// <summary>A free General MIDI SoundFont the launcher points to when none is found (not included: it is about 140 MB); any .sf2 the player has works as well.</summary>
    internal const string FreeSoundFont = "FluidR3_GM.sf2";

    private const string SoundFontFolder = "SOUNDFONT";

    /// <summary>What the ROM folder means for the sound card chosen: for a Roland module, whether its ROMs are there.</summary>
    private (string Text, uint Colour) RomsStatus(RomStatus roms) => _model.SoundCard switch
    {
        "mt32" => roms.Mt32 ? ("MT-32 ROMs found.", Green) : ("No MT-32 ROMs here: the FM music plays instead.", PaleRed),
        "cm32l" => roms.Cm32l ? ("CM-32L ROMs found.", Green) : ("No CM-32L ROMs here: the FM music plays instead.", PaleRed),
        _ => (roms.Mt32, roms.Cm32l) switch
        {
            (true, true) => ("MT-32 and CM-32L ROMs found (for a Roland sound card).", PaleBlue),
            (true, false) => ("MT-32 ROMs found (for a Roland sound card).", PaleBlue),
            (false, true) => ("CM-32L ROMs found (for a Roland sound card).", PaleBlue),
            _ => ("No ROMs here: needed only for a Roland sound card.", PaleBlue),
        },
    };

    private void Hot(Rect area, Item item, Part part) => _hotspots.Add((area, item, part));

    /// <summary>Notes a drawn thing of the settings panel (for the layout's checks: in the panel, over nothing else).</summary>
    private void Leaf(Rect area, string what) => _blocks.Add((area, what));

    private string Help(Item item)
    {
        var browse = _surface.PickFolder is not null ? "Enter or Browse…: another folder" : "or drop a folder on this window";
        var help = item switch
        {
            Item.GameFolder => _editing
                ? "Type or paste (Ctrl+V) the folder, then Enter (Esc cancels). ~ is your home folder."
                : "The folder with DARKLAND.EXE (the GOG or Steam install folder is fine): type, paste (Ctrl+V) or drop it." + (_model.Found.Count > 1 ? " ←/→: the other folders found." : ""),
            Item.Change => _surface.PickFolder is not null ? "Choose the folder of your copy of Darklands." : "Type or paste (Ctrl+V) the folder of your copy of Darklands.",
            Item.Window => "The window's size; the game keeps its shape.",
            Item.Fullscreen => "Fill the whole screen; the game keeps its shape.",
            Item.HdPack => $"Replacement pictures (PNG). ←/→: None or a pack of the packs folder; {browse}.",
            Item.AudioPack => $"Replacement music and sounds (OGG, WAV, MIDI). ←/→: None or a pack of the packs folder; {browse}.",
            Item.SoundFont => _model.SoundFontFound is null && (_model.SoundFont is null || !File.Exists(_model.SoundFont))
                ? $"No SoundFont: a pack's .mid music is skipped. Any General MIDI SoundFont (.sf2) you have works: put it in the {SoundFontFolder} folder next to the program, drop it here, or Browse…. A free one: {FreeSoundFont} by Frank Wen, MIT licence, about 140 MB (search the web for \"FluidR3_GM sf2\"; on Linux the fluid-soundfont-gm package)."
                : $"The SoundFont (.sf2) for the packs' .mid music. Enter or Browse…: another file; ←/→: the default (the {SoundFontFolder} folder next to the program).",
            Item.PackGuide => "Opens the guide: how to make a picture or sound pack, and the names of the game's pictures, music and sounds.",
            Item.Report => "Makes a zip with the logs, settings and saves for a bug report, and opens its folder and the report form. Nothing is sent: you attach the zip yourself.",
            Item.Sound => "The sound card the game plays its music and sounds on.",
            Item.Roms => "Your MT-32 / CM-32L ROM files (side by side or in MT32, CM32L subfolders). "
                + (_surface.PickFolder is not null ? "Enter or Change…: another folder; or drop it here." : "Drop the folder on this window."),
            Item.Test => _context.SoundTest is { Playing: true } ? "Stops the music." : "Plays some of the game's music on the chosen card, with these ROMs.",
            Item.Banner => "The text page the 1992 game showed for a few seconds while it loaded.",
            Item.Intro => "The animated opening before the start screen.",
            Item.Always => "Otherwise DarklandsRP goes straight to the game, and DarklandsRP Launcher (or Shift held as it starts) shows this screen.",
            Item.Save => "Writes the changes to the settings file and closes.",
            Item.Play => "Writes the changes to the settings file and starts the game.",
            Item.Discord => "Opens the project's Discord in your browser.",
            Item.Notes => "Opens the release notes of this version in your browser.",
            Item.TabGeneral => "The game folder, the display, the sound card and the packs. Ctrl+Tab: the other tab.",
            Item.TabAdvanced => "The Roland ROMs, the SoundFont for the packs' MIDI music, and the start-up. Ctrl+Tab: the other tab.",
            Item.Minimise => "Minimises the window.",
            Item.Close => "Closes the launcher (as Esc): the changes are written to the settings file.",
            _ => "",
        };
        return IsFixed(item, out var by) ? $"Set by {by}: change it there. " + help : help;
    }

    /// <summary>The text cut to <paramref name="width"/> with an ellipsis (at its start when <paramref name="fromEnd"/>, to keep a path's end).</summary>
    private static string Fit(TextFace face, string text, int width, bool fromEnd)
    {
        if (face.Measure(text) <= width)
        {
            return text;
        }
        for (var cut = 1; cut < text.Length; cut++)
        {
            var candidate = fromEnd ? string.Concat("…", text.AsSpan(cut)) : string.Concat(text.AsSpan(0, text.Length - cut), "…");
            if (face.Measure(candidate) <= width)
            {
                return candidate;
            }
        }
        return "…";
    }

    private TextFace Face(TrueTypeFont font, double designSize, View v) => FaceAt(font, Math.Max(6, Math.Round(designSize * v.Scale)));

    /// <summary>A font at a size in pixels per em (kept for each size).</summary>
    private TextFace FaceAt(TrueTypeFont font, double pixels)
    {
        var key = (font, (int)Math.Round(pixels * 10));
        if (!_faces.TryGetValue(key, out var face))
        {
            var fallback = font == Assets.Text ? null : FaceAt(Assets.Text, pixels);
            face = _faces[key] = new TextFace(font, (float)pixels, fallback);
        }
        return face;
    }

    /// <summary>The design's coordinates scaled and centred on the surface.</summary>
    private readonly record struct View(double Scale, int OffsetX, int OffsetY)
    {
        public int Len(double length) => (int)Math.Round(length * Scale);

        public Rect Rect(double x, double y, double width, double height) =>
            new(OffsetX + Len(x), OffsetY + Len(y), Len(width), Len(height));
    }
}
