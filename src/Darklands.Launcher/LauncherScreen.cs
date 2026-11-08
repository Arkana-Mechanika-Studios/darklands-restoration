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

    private IEnumerable<Item> Order =>
        new[] { Item.GameFolder, Item.Change, Item.Window, Item.Fullscreen, Item.HdPack, Item.Sound, Item.Roms, Item.Test, Item.AudioPack, Item.SoundFont, Item.Banner, Item.Intro, Item.Always, Item.Save, Item.Play, Item.Discord, Item.PackGuide, Item.Report, Item.Notes }
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
    private static bool IsPlate(Item item, Part part) => part == Part.Browse || item is Item.Change or Item.Test or Item.Save or Item.Play;

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

    private static Rect SettingsFrame(View v) => v.Rect(768, 14, 494, 692);

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
        DrawSettings(canvas, v);
    }

    private void DrawBackground(Canvas canvas, View v)
    {
        canvas.Clear(0x1C1712);
        DrawPainting(canvas, v);
        DrawTitle(canvas, v);
        var card = CardFrame(v);
        CardShadow(canvas, card, v);
        Parchment(canvas, card, v, "card-left", 11);
        DrawCard(canvas, v, card);
        var settings = SettingsFrame(v);
        CardShadow(canvas, settings, v);
        Parchment(canvas, settings, v, "card-settings", 23);
        Corners(canvas, settings, v);
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
        var big = Face(Assets.Display, 84, v);
        var sub = Face(Assets.Strong, 36, v);
        var centre = v.OffsetX + v.Len(343);
        var y = v.OffsetY + v.Len(22);
        var shadow = Math.Max(1, v.Len(2));
        big.Draw(canvas, "Darklands", centre - big.Measure("Darklands") / 2, y, Cream, shadow, Black);
        y += (int)(big.LineHeight * 0.86);
        var spacing = v.Len(1.5);
        Spaced(canvas, sub, "Restoration Project", centre - SpacedWidth(sub, "Restoration Project", spacing) / 2, y, Cream, spacing, shadow);
        y += sub.LineHeight + v.Len(6);
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
    /// The settings panel: the title, four groups with their icons (Game installation, Display, Sound &amp; Music, Start-up), rows
    /// with their values centred between ‹ › in one column and their Browse… / Change… plates in another, a status or problem
    /// line under each folder and pack, then the hint bar and the two buttons. Every text is cut or wrapped to its place.
    /// </summary>
    private void DrawSettings(Canvas canvas, View v)
    {
        var frame = SettingsFrame(v);
        _panel = frame.Inset(v.Len(12));
        var x0 = frame.X + v.Len(30);
        var x1 = frame.Right - v.Len(30);
        var layout = new RowLayout(
            x0 + v.Len(30),
            x1 - v.Len(78),
            x0 + v.Len(136),
            x1 - v.Len(88),
            x1 - v.Len(88) - v.Len(96),
            x1);
        var title = Face(Assets.Display, 46, v);
        var heading = Face(Assets.Strong, 20.5, v);
        var text = Face(Assets.Text, 16, v);
        var line = Face(Assets.Italic, 13, v);
        var small = Face(Assets.Text, 13, v);

        var y = frame.Y + v.Len(16);
        // with the panel's art, "Settings" stands on its dark header band
        var band = SettingsBand(v);
        if (band > 0)
        {
            y = frame.Y + (band - title.LineHeight) / 2 + v.Len(2);
        }
        title.Draw(canvas, "Settings", x0, y, band > 0 ? Cream : Ink, band > 0 ? Math.Max(1, v.Len(1.5)) : 0, Black);
        // the letters' own height: the display face's line box has much room above and below them
        Leaf(new Rect(x0, y + title.LineHeight / 5, title.Measure("Settings"), title.LineHeight * 3 / 5), "title");
        var ruleLeft = x0 + title.Measure("Settings") + v.Len(16);
        Fleuron(canvas, v, (ruleLeft + x1) / 2, y + title.LineHeight / 2, (x1 - ruleLeft) / 2, band > 0 ? Gold : InkSoft, onDark: band > 0);
        y = Math.Max(y + title.LineHeight - v.Len(2), frame.Y + band + v.Len(2));

        int Section(string name, string icon, int top)
        {
            top += v.Len(6);
            var size = v.Len(22);
            var iconArea = new Rect(x0, top + (heading.LineHeight - size) / 2, size, size);
            Icon(canvas, v, icon, iconArea, InkSoft);
            StrongText(canvas, v, heading, name, x0 + v.Len(30), top, Ink);
            Leaf(new Rect(x0, top, v.Len(30) + heading.Measure(name) + (Assets.HasStrong ? 0 : v.Len(1)), heading.LineHeight - v.Len(3)), "heading " + name);
            return top + heading.LineHeight - v.Len(3);
        }

        int Status(int top, string text, uint colour, int right, bool problem)
        {
            var size = v.Len(12);
            var iconArea = new Rect(layout.Label, top + (line.LineHeight - size) / 2, size, size);
            var ink = OnParchment(colour);
            if (problem)
            {
                Icon(canvas, v, "icon-problem", iconArea, InkRed);
            }
            else if (colour == Green)
            {
                Icon(canvas, v, "icon-found", iconArea, InkGreen);
            }
            var tx = layout.Label + size + v.Len(6);
            var fitted = Fit(line, text, right - tx, fromEnd: false);
            line.Draw(canvas, fitted, tx, top, ink);
            Leaf(new Rect(layout.Label, top, tx + line.Measure(fitted) - layout.Label, line.LineHeight), "status " + text);
            return top + line.LineHeight;
        }

        // the game installation: its field, Change… and whether the game is there
        y = Section("Game installation", "icon-game", y);
        var fieldHeight = v.Len(26);
        var field = new Rect(layout.Label, y + v.Len(2), layout.Plate - v.Len(10) - layout.Label, fieldHeight);
        Field(canvas, v, field, _editing ? _editText + "_" : _model.GameFolder ?? "", _focus == Item.GameFolder, IsFixed(Item.GameFolder, out _), text);
        Hot(field, Item.GameFolder, Part.Row);
        Leaf(field, "game folder");
        Plate(canvas, v, new Rect(layout.Plate, field.Y + (field.Height - SmallPlateHeight(v)) / 2, layout.Right - layout.Plate, SmallPlateHeight(v)), "Change…", small, Item.Change, Part.Row);
        y = field.Bottom + v.Len(2);
        var (status, statusColour) = _model.GameFolderChecked is not null
            ? ("Found" + (_model.GameFolderSource is { } source ? $" ({source})." : "."), Green)
            : _model.GameFolder is null ? ("Type its folder, drop it on this window or press Change…", PaleBlue) : ("No Darklands there (DARKLAND.EXE).", PaleRed);
        y = Status(y, status, statusColour, layout.Right, statusColour == PaleRed);

        // display
        y = Section("Display", "icon-display", y);
        y = Row(canvas, v, text, small, layout, y, "Window size", LauncherModel.SizeLabel(_model.Windows[_model.WindowIndex]), Item.Window);
        y = Row(canvas, v, text, small, layout, y, "Fullscreen", _model.Fullscreen ? "Yes" : "No", Item.Fullscreen);
        y = Row(canvas, v, text, small, layout, y, "HD pack", PackShown(PackKind.Pictures), Item.HdPack, browse: true);
        var (hdLine, hdColour) = PackLine(PackKind.Pictures);
        y = Status(y, hdLine, hdColour, layout.Right, hdColour == PaleRed);

        // sound and music: the card, then the Roland ROM folder as a row of its own
        y = Section("Sound & Music", "icon-sound", y);
        y = Row(canvas, v, text, small, layout, y, "Sound card", _model.SoundCardLabel, Item.Sound);
        var romsRow = new Rect(layout.Label - v.Len(8), y, layout.Right - layout.Label + v.Len(12), Math.Max(v.Len(28), text.LineHeight));
        if (_focus == Item.Roms)
        {
            Shade(canvas, romsRow, Ink, 26);
        }
        var romsLabelY = romsRow.Y + (romsRow.Height - text.LineHeight) / 2;
        var romsLabel = Fit(text, "Roland ROMs", layout.Select - layout.Label - v.Len(8), fromEnd: false);
        text.Draw(canvas, romsLabel, layout.Label, romsLabelY, Ink);
        Leaf(new Rect(layout.Label, romsLabelY, text.Measure(romsLabel), text.LineHeight), "label Roland ROMs");
        var romsField = new Rect(layout.Select, romsRow.Y + v.Len(2), layout.SelectEnd - layout.Select, romsRow.Height - v.Len(4));
        Field(canvas, v, romsField, _model.Roms.Folder, _focus == Item.Roms, IsFixed(Item.Roms, out _), small);
        Hot(romsField, Item.Roms, Part.Row);
        Leaf(romsField, "roms field");
        Plate(canvas, v, new Rect(layout.Plate, romsRow.Y + (romsRow.Height - SmallPlateHeight(v)) / 2, layout.Right - layout.Plate, SmallPlateHeight(v)), "Change…", small, Item.Roms, Part.Browse);
        y = romsRow.Bottom;
        var (romsStatus, romsColour) = RomsStatus(_model.Roms);
        var statusRight = layout.Right;
        if (_context.SoundTest is { } soundTest)
        {
            var testHeight = Math.Min(line.LineHeight, SmallPlateHeight(v));
            Plate(canvas, v, new Rect(layout.Plate, y + (line.LineHeight - testHeight) / 2, layout.Right - layout.Plate, testHeight), soundTest.Playing ? "Stop" : "Test sound", Face(Assets.Text, 12, v), Item.Test, Part.Row);
            statusRight = layout.Plate - v.Len(8);
        }
        y = Status(y, romsStatus, romsColour, statusRight, romsColour == PaleRed);
        y = Row(canvas, v, text, small, layout, y, "Audio pack", PackShown(PackKind.Sounds), Item.AudioPack, browse: true);
        var (audioLine, audioColour) = PackLine(PackKind.Sounds);
        y = Status(y, audioLine, audioColour, layout.Right, audioColour == PaleRed);
        y = Row(canvas, v, text, small, layout, y, "SoundFont", SoundFontShown(), Item.SoundFont, browse: true);
        var (fontLine, fontColour) = SoundFontLine();
        y = Status(y, fontLine, fontColour, layout.Right, fontColour == PaleRed);

        // start-up: Yes / No, in a narrower column at the right
        y = Section("Start-up", "icon-startup", y);
        var narrow = layout with { Select = layout.SelectNarrow };
        y = Row(canvas, v, text, small, narrow, y, "Original start-up screen", _model.Banner ? "Yes" : "No", Item.Banner);
        y = Row(canvas, v, text, small, narrow, y, "Opening animation", _model.Intro ? "Yes" : "No", Item.Intro);
        y = Row(canvas, v, text, small, narrow, y, "Show launcher at every start", _model.Always ? "Yes" : "No", Item.Always);

        // the buttons at the bottom right: Save and quit on stone, Play on red
        var buttonHeight = v.Len(44);
        var buttons = frame.Bottom - v.Len(22) - buttonHeight;
        var playWidth = v.Len(168);
        var playRect = new Rect(x1 - playWidth, buttons, playWidth, buttonHeight);
        BigPlate(canvas, v, playRect, "Play", red: true, Item.Play);
        var saveRect = new Rect(playRect.X - v.Len(18) - v.Len(168), buttons, v.Len(168), buttonHeight);
        BigPlate(canvas, v, saveRect, "Save and quit", red: false, Item.Save);

        // between the rows and the buttons: the hint bar
        var bar = new Rect(x0, y + v.Len(10), x1 - x0, buttons - v.Len(10) - (y + v.Len(10)));
        DrawHints(canvas, v, Face(Assets.Italic, 14, v), bar);
    }

    /// <summary>The columns of the settings' rows: the labels, the plates, the value selectors (wide, or narrow for Yes / No).</summary>
    private readonly record struct RowLayout(int Label, int Plate, int Select, int SelectEnd, int SelectNarrow, int Right);

    private static int SmallPlateHeight(View v) => v.Len(22);

    /// <summary>
    /// A setting's row: its label at the left, its value centred between ‹ › in the selector column (each arrow a click target:
    /// the previous or next value), and a "Browse…" plate at the end when <paramref name="browse"/>. Returns the y below.
    /// </summary>
    private int Row(Canvas canvas, View v, TextFace text, TextFace small, RowLayout layout, int y, string label, string value, Item item, bool browse = false)
    {
        var row = new Rect(layout.Label - v.Len(8), y, layout.Right - layout.Label + v.Len(12), Math.Max(v.Len(24), text.LineHeight));
        if (_focus == item)
        {
            Shade(canvas, row, Ink, 26);
        }
        Hot(row, item, Part.Row);
        var ty = y + (row.Height - text.LineHeight) / 2;
        if (browse)
        {
            Plate(canvas, v, new Rect(layout.Plate, y + (row.Height - SmallPlateHeight(v)) / 2, layout.Right - layout.Plate, SmallPlateHeight(v)), "Browse…", small, item, Part.Browse);
        }
        var labelShown = Fit(text, label, layout.Select - layout.Label - v.Len(10), fromEnd: false);
        text.Draw(canvas, labelShown, layout.Label, ty, Ink);
        Leaf(new Rect(layout.Label, ty, text.Measure(labelShown), text.LineHeight), "label " + label);

        var fixedBy = IsFixed(item, out _);
        var left = layout.Select;
        var right = layout.SelectEnd;
        var arrowLeft = fixedBy ? "" : "‹";
        var arrowRight = fixedBy ? "" : "›";
        var gap = v.Len(8);
        var room = right - left - text.Measure(arrowLeft) - text.Measure(arrowRight) - 2 * gap;
        var valueShown = Fit(text, value, Math.Max(0, room), fromEnd: false);
        var inner = left + text.Measure(arrowLeft) + gap;
        var valueX = inner + (room - text.Measure(valueShown)) / 2;
        text.Draw(canvas, arrowLeft, left, ty, InkSoft);
        text.Draw(canvas, valueShown, valueX, ty, fixedBy ? InkSoft : Ink);
        text.Draw(canvas, arrowRight, right - text.Measure(arrowRight), ty, InkSoft);
        Leaf(new Rect(left, ty, right - left, text.LineHeight), "value " + label);
        if (!fixedBy)
        {
            // the arrows: a click on ‹ or › steps back or on, as ←/→
            Hot(new Rect(left - v.Len(6), row.Y, inner - left + v.Len(6), row.Height), item, Part.Previous);
            Hot(new Rect(right - text.Measure(arrowRight) - gap, row.Y, text.Measure(arrowRight) + gap + v.Len(6), row.Height), item, Part.Next);
        }
        return row.Bottom;
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
        var face = red ? Face(Assets.Display, 32, v) : Face(Assets.Strong, 19, v);
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

    /// <summary>The height of the settings art's dark header band on the screen (0 without the art): where its parchment starts, down its middle.</summary>
    private static int SettingsBand(View v)
    {
        if (Assets.Art("card-settings") is not { } art || Assets.Slice("card-settings") is not { } slice)
        {
            return 0;
        }
        var x = slice.Left / 2;
        for (var y = 12; y < slice.Top; y++)
        {
            var c = art.At(x, y);
            var light = ((c >> 16) & 255) + ((c >> 8) & 255) + (c & 255);
            if (c >> 24 > 200 && light > 3 * 150)
            {
                return (int)Math.Round(y * v.Scale / 2);
            }
        }
        return 0;
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

    private static int SpacedWidth(TextFace face, string text, int spacing) => text.Sum(c => face.Measure([c]) + spacing) - spacing;

    /// <summary>Text with its letters spread by <paramref name="spacing"/> pixels.</summary>
    private static void Spaced(Canvas canvas, TextFace face, string text, int x, int y, uint colour, int spacing, int shadow)
    {
        foreach (var c in text)
        {
            face.Draw(canvas, [c], x, y, colour, shadow, Black);
            x += face.Measure([c]) + spacing;
        }
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
