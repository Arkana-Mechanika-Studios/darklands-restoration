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

    private const int DesignWidth = 1080;
    private const int DesignHeight = 640;

    // the colours of the original's menus (sampled from its screens) and of the mockup they were chosen in
    private const uint Black = 0x000000;
    private const uint Yellow = 0xFFFF55;
    private const uint White = 0xFFFFFF;
    private const uint PaleBlue = 0xAAB4FF;
    private const uint Green = 0x55FF55;
    private const uint PaleRed = 0xFF9A9A;
    private const uint PanelBlue = 0x0008E8;
    private const uint FieldBlue = 0x000080;
    private const uint FieldEdge = 0x5555FF;
    private const uint Ink = 0x1A1010;
    private const uint FadedInk = 0x5A4038;
    private const uint LinkRed = 0x8C1010;
    private const uint ParchmentLight = 0xEFDCD2;
    private const uint ParchmentDark = 0xEAD3C8;
    private const uint ParchmentRule = 0xC9A99A;
    private const uint RopeLight = 0xD4935E;
    private const uint RopeMid = 0x925D34;
    private const uint RopeDark = 0x553C1C;
    private const uint RopeEdge = 0x28200C;
    private const uint CapitalLight = 0x8C1010;
    private const uint CapitalDark = 0x5A0808;
    private const uint CapitalLetter = 0xF4C4C4;
    private const double GlowStrength = 0.22;
    private const double BadgeOpacity = 0.12;

    private readonly LauncherModel _model;
    private readonly LauncherContext _context;
    private readonly ILauncherSurface _surface;
    private readonly IReadOnlyList<string> _notes;
    private readonly Dictionary<(TrueTypeFont Font, int TenthsOfPixels), TextFace> _faces = [];
    private List<(Rect Area, Item Item, Part Part)> _hotspots = [];
    private List<(Rect Area, string What)> _blocks = [];
    private Rect _panel;
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

    /// <summary>The left column (the title, the parchment) and the settings panel at the right, in design pixels.</summary>
    private const int LeftWidth = 520;
    private const int SettingsX = 548;

    private static Rect SettingsFrame(View v) => v.Rect(SettingsX, 14, DesignWidth - 14 - SettingsX, DesignHeight - 28);

    /// <summary>Draws the screen on <paramref name="canvas"/> and notes where the controls are.</summary>
    public void Render(Canvas canvas)
    {
        var s = Math.Min((double)canvas.Width / DesignWidth, (double)canvas.Height / DesignHeight);
        var v = new View(s, (int)((canvas.Width - DesignWidth * s) / 2), (int)((canvas.Height - DesignHeight * s) / 2));
        // what never changes (the frames, the panels, the badges, the parchment's text) is drawn once for each size
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
            canvas.Fill(new Rect(link.X, link.Bottom - Math.Max(1, v.Len(1)), link.Width, Math.Max(1, v.Len(1))), Ink);
        }
        DrawSettings(canvas, v);
    }

    private void DrawBackground(Canvas canvas, View v)
    {
        canvas.Clear(Black);

        // the title, on blue
        var top = v.Rect(14, 14, LeftWidth, 86);
        Rope(canvas, top, v);
        var topInner = top.Inset(v.Len(7));
        Blue(canvas, topInner);
        var badge = v.Len(52);
        canvas.Image(Assets.Badge, new Rect(topInner.X + v.Len(16), topInner.Y + (topInner.Height - badge) / 2, badge, badge));
        var title = Face(Assets.Title, 30, v);
        var small = Face(Assets.Panel, 16, v);
        var titleY = topInner.Y + (topInner.Height - title.LineHeight - small.LineHeight) / 2;
        title.Draw(canvas, "Darklands Restoration Project", topInner.X + v.Len(82), titleY, Yellow, v.Len(2), Black);
        small.Draw(canvas, "Version " + _context.Version, topInner.X + v.Len(82), titleY + title.LineHeight, White, Math.Max(1, v.Len(1)), Black);

        DrawParchment(canvas, v);

        var settings = SettingsFrame(v);
        Rope(canvas, settings, v);
        Blue(canvas, settings.Inset(v.Len(7)));
    }

    private void DrawParchment(Canvas canvas, View v)
    {
        var frame = v.Rect(14, 108, LeftWidth, DesignHeight - 122);
        Rope(canvas, frame, v);
        var inner = frame.Inset(v.Len(7));
        var cell = Math.Max(1, v.Len(2));
        canvas.Paint(inner, (x, y) => ((x / cell + y / cell) & 1) == 0 ? ParchmentLight : ParchmentDark);
        canvas.Image(Assets.Badge, new Rect(inner.X + (inner.Width - v.Len(420)) / 2, inner.Y + v.Len(30), v.Len(420), v.Len(420)), BadgeOpacity);

        var x = inner.X + v.Len(20);
        var width = inner.Width - v.Len(40);
        var y = inner.Y + v.Len(16);
        var text = Face(Assets.Text, 17, v);
        var caption = Face(Assets.Text, 13, v);
        var line = (int)Math.Round(text.LineHeight * 1.12);

        y = Capitalised(canvas, v, x, y, "What’s new in " + _context.Version, _notes.Count > 0 ? "From the release notes" : "The release notes are not included with this copy");
        y += v.Len(8);
        if (_notes.Count == 0)
        {
            text.Draw(canvas, "The release notes of this version are on the project's page.", x, y, Ink);
            y += line + v.Len(3);
        }
        foreach (var item in _notes.Take(4))
        {
            var lines = text.Wrap(item, width - v.Len(20)).Take(2).ToList();
            text.Draw(canvas, "•", x + v.Len(6), y, Ink);
            foreach (var l in lines)
            {
                text.Draw(canvas, l, x + v.Len(20), y, Ink);
                y += line;
            }
            y += v.Len(3);
        }

        if (_context.ReleaseNotesLink is not null)
        {
            var label = _notes.Count == 0 ? "Read the release notes" : "All the release notes";
            text.Draw(canvas, label, x, y, LinkRed);
            var under = y + text.Ascent + Math.Max(1, v.Len(2));
            canvas.Fill(new Rect(x, under, text.Measure(label), Math.Max(1, v.Len(1))), LinkRed);
            _notesLink = new Rect(x, y, text.Measure(label), under + Math.Max(1, v.Len(1)) - y);
            y += line;
        }
        y += v.Len(14);
        y = Capitalised(canvas, v, x, y, "About this project", "A free, open-source fan restoration");
        y += v.Len(8);
        foreach (var l in text.Wrap("MicroProse’s 1992 Darklands, rebuilt from the original program to run on today’s computers. "
                     + "You need your own copy of the game: nothing from it is included, the program reads your installation.", width))
        {
            text.Draw(canvas, l, x, y, Ink);
            y += line;
        }
        y += v.Len(8);
        var after = text.Draw(canvas, "Questions, bugs, ideas: ", x, y, Ink);
        var link = "join us on Discord";
        text.Draw(canvas, link, after, y, LinkRed);
        var underline = y + text.Ascent + Math.Max(1, v.Len(2));
        canvas.Fill(new Rect(after, underline, text.Measure(link), Math.Max(1, v.Len(1))), LinkRed);
        _discord = new Rect(after, y, text.Measure(link), underline + Math.Max(1, v.Len(1)) - y);
        if (_context.PackGuide is not null)
        {
            y += line;
            var guideAfter = text.Draw(canvas, "Your own pictures, music and sounds: ", x, y, Ink);
            var guide = "how to make a pack";
            text.Draw(canvas, guide, guideAfter, y, LinkRed);
            var guideUnder = y + text.Ascent + Math.Max(1, v.Len(2));
            canvas.Fill(new Rect(guideAfter, guideUnder, text.Measure(guide), Math.Max(1, v.Len(1))), LinkRed);
            _guideLink = new Rect(guideAfter, y, text.Measure(guide), guideUnder + Math.Max(1, v.Len(1)) - y);
        }
        if (_context.ReportProblem is not null)
        {
            y += line;
            var reportAfter = text.Draw(canvas, "Something broken? ", x, y, Ink);
            var reportLabel = "make a problem report";
            text.Draw(canvas, reportLabel, reportAfter, y, LinkRed);
            var reportUnder = y + text.Ascent + Math.Max(1, v.Len(2));
            canvas.Fill(new Rect(reportAfter, reportUnder, text.Measure(reportLabel), Math.Max(1, v.Len(1))), LinkRed);
            _reportLink = new Rect(reportAfter, y, text.Measure(reportLabel), reportUnder + Math.Max(1, v.Len(1)) - y);
        }

        var disclaimer = caption.Wrap("Darklands © 1992 MicroProse Software, Inc. Darklands and MicroProse are the property of their "
            + "respective owners. This is an unofficial fan project, not affiliated with or endorsed by them.", width);
        var dy = inner.Bottom - v.Len(16) - disclaimer.Count * caption.LineHeight;
        canvas.Fill(new Rect(x, dy - v.Len(6), width, Math.Max(1, v.Len(1))), ParchmentRule);
        foreach (var l in disclaimer)
        {
            caption.Draw(canvas, l, x, dy, FadedInk);
            dy += caption.LineHeight;
        }
    }

    /// <summary>A heading with its first letter in a red illuminated square, and a caption under it; returns the y below.</summary>
    private int Capitalised(Canvas canvas, View v, int x, int y, string heading, string caption)
    {
        var box = new Rect(x, y, v.Len(38), v.Len(38));
        var cell = Math.Max(1, v.Len(3));
        canvas.Paint(box, (px, py) => ((px / cell + py / cell) & 1) == 0 ? CapitalLight : CapitalDark);
        canvas.Frame(box, Math.Max(1, v.Len(2)), Black);
        var capital = Face(Assets.Title, 34, v);
        var letter = heading.AsSpan(0, 1);
        capital.Draw(canvas, letter, box.X + (box.Width - capital.Measure(letter)) / 2, box.Y + (box.Height - capital.LineHeight) / 2, CapitalLetter);
        var face = Face(Assets.Heading, 27, v);
        var under = Face(Assets.Text, 13, v);
        face.Draw(canvas, heading.AsSpan(1), box.Right + v.Len(10), y - v.Len(2), Ink);
        under.Draw(canvas, caption, box.Right + v.Len(10), y + face.LineHeight - v.Len(4), FadedInk);
        return Math.Max(box.Bottom, y + face.LineHeight - v.Len(4) + under.LineHeight);
    }

    /// <summary>
    /// The settings panel: the title, four groups (Game, Display, Sound and music, Start-up) of rows with their values aligned
    /// at the right, a status line under each folder or pack, then the details box (the last message, the focused row's problem
    /// in full and what it does) and the two buttons. Every text is cut or wrapped to its place: nothing is drawn over another.
    /// </summary>
    private void DrawSettings(Canvas canvas, View v)
    {
        var inner = SettingsFrame(v).Inset(v.Len(7));
        _panel = inner;
        var x = inner.X + v.Len(16);
        var right = inner.Right - v.Len(16);
        var width = right - x;
        var title = Face(Assets.Title, 28, v);
        var heading = Face(Assets.Heading, 20, v);
        var text = Face(Assets.Panel, 15, v);
        var caption = Face(Assets.Panel, 12.5, v);
        var shadow = Math.Max(1, v.Len(1));
        var gap = v.Len(8);

        var y = inner.Y + v.Len(6);
        Leaf(new Rect(x, y, title.Measure("Settings"), title.LineHeight), "title");
        title.Draw(canvas, "Settings", x, y, Yellow, v.Len(2), Black);
        y += title.LineHeight;

        int Heading(string name, int top)
        {
            top += v.Len(4);
            heading.Draw(canvas, name, x, top, Yellow, shadow, Black);
            Leaf(new Rect(x, top, heading.Measure(name), heading.LineHeight), "heading " + name);
            return top + heading.LineHeight;
        }

        int Status(int top, string line, uint colour, int lineRight)
        {
            var fitted = Fit(caption, line, lineRight - x, fromEnd: false);
            caption.Draw(canvas, fitted, x, top, colour, shadow, Black);
            Leaf(new Rect(x, top, caption.Measure(fitted), caption.LineHeight), "status " + line);
            return top + caption.LineHeight;
        }

        // the game folder: its field, Change… and whether the game is there
        y = Heading("Game", y);
        var change = "Change…";
        var buttonWidth = text.Measure(change) + v.Len(20);
        var field = new Rect(x, y, width - buttonWidth - gap, v.Len(24));
        canvas.Fill(field, FieldBlue);
        canvas.Frame(field, Math.Max(1, v.Len(1)), _focus == Item.GameFolder ? Yellow : FieldEdge);
        var shown = _editing ? _editText + "_" : _model.GameFolder ?? "";
        var (pathFont, pathSize, pathStep) = Assets.Path;
        var path = FaceAt(pathFont, Math.Max(pathStep, Math.Round(pathSize * v.Scale / pathStep) * pathStep));
        path.Draw(canvas, Fit(path, shown, field.Width - v.Len(16), fromEnd: true), field.X + v.Len(8), field.Y + (field.Height - path.LineHeight) / 2, White);
        Hot(field, Item.GameFolder, Part.Row);
        Leaf(field, "game folder");
        ParchmentButton(canvas, v, new Rect(field.Right + gap, y, buttonWidth, field.Height), change, text, Item.Change, Part.Row);
        y = field.Bottom + v.Len(2);
        var (status, statusColour) = _model.GameFolderChecked is not null
            ? ("Found" + (_model.GameFolderSource is { } source ? $" ({source})." : "."), Green)
            : _model.GameFolder is null ? ("Type its folder, drop it on this window or press Change…", PaleBlue) : ("No Darklands there (DARKLAND.EXE).", PaleRed);
        y = Status(y, status, statusColour, right);

        // display
        y = Heading("Display", y);
        y = Row(canvas, v, text, caption, x, right, y, "Window size", LauncherModel.SizeLabel(_model.Windows[_model.WindowIndex]), Item.Window);
        y = Row(canvas, v, text, caption, x, right, y, "Fullscreen", _model.Fullscreen ? "Yes" : "No", Item.Fullscreen);
        y = Row(canvas, v, text, caption, x, right, y, "HD pack", PackShown(PackKind.Pictures), Item.HdPack, browse: true);
        var (hdLine, hdColour) = PackLine(PackKind.Pictures);
        y = Status(y, hdLine, hdColour, right);

        // sound and music
        y = Heading("Sound & Music", y);
        y = Row(canvas, v, text, caption, x, right, y, "Sound card", _model.SoundCardLabel, Item.Sound);
        // the Roland ROM folder, as the game folder: its field, Change… and what it holds
        var romsLabel = "Roland ROMs";
        var labelWidth = text.Measure(romsLabel) + v.Len(10);
        var romsField = new Rect(x + labelWidth, y + v.Len(2), width - labelWidth - buttonWidth - gap, v.Len(22));
        var romsLabelY = romsField.Y + (romsField.Height - text.LineHeight) / 2;
        text.Draw(canvas, romsLabel, x, romsLabelY, White, shadow, Black);
        Leaf(new Rect(x, romsLabelY, text.Measure(romsLabel), text.LineHeight), "label Roland ROMs");
        var roms = _model.Roms;
        var romsFixed = IsFixed(Item.Roms, out _);
        canvas.Fill(romsField, FieldBlue);
        canvas.Frame(romsField, Math.Max(1, v.Len(1)), _focus == Item.Roms ? Yellow : FieldEdge);
        path.Draw(canvas, Fit(path, roms.Folder, romsField.Width - v.Len(16), fromEnd: true), romsField.X + v.Len(8),
            romsField.Y + (romsField.Height - path.LineHeight) / 2, romsFixed ? PaleBlue : White);
        Hot(romsField, Item.Roms, Part.Row);
        Leaf(romsField, "roms field");
        ParchmentButton(canvas, v, new Rect(romsField.Right + gap, romsField.Y, buttonWidth, romsField.Height), change, text, Item.Roms, Part.Browse);
        y = romsField.Bottom + v.Len(2);
        var (romsStatus, romsColour) = RomsStatus(roms);
        var statusRight = right;
        if (_context.SoundTest is { } soundTest)
        {
            var test = soundTest.Playing ? "Stop" : "Test sound";
            var testWidth = Math.Max(caption.Measure("Test sound"), caption.Measure("Stop")) + v.Len(14);
            ParchmentButton(canvas, v, new Rect(right - testWidth, y, testWidth, caption.LineHeight), test, caption, Item.Test, Part.Row);
            statusRight = right - testWidth - gap;
        }
        y = Status(y, romsStatus, romsColour, statusRight);
        y = Row(canvas, v, text, caption, x, right, y, "Audio pack", PackShown(PackKind.Sounds), Item.AudioPack, browse: true);
        var (audioLine, audioColour) = PackLine(PackKind.Sounds);
        y = Status(y, audioLine, audioColour, right);
        y = Row(canvas, v, text, caption, x, right, y, "SoundFont", SoundFontShown(), Item.SoundFont, browse: true);
        var (fontLine, fontColour) = SoundFontLine();
        y = Status(y, fontLine, fontColour, right);

        // start-up
        y = Heading("Start-up", y);
        y = Row(canvas, v, text, caption, x, right, y, "Original start-up screen", _model.Banner ? "Yes" : "No", Item.Banner);
        y = Row(canvas, v, text, caption, x, right, y, "Opening animation", _model.Intro ? "Yes" : "No", Item.Intro);
        y = Row(canvas, v, text, caption, x, right, y, "Show launcher at every start", _model.Always ? "Yes" : "No", Item.Always);

        // the buttons at the bottom right
        var buttonHeight = v.Len(34);
        var buttons = inner.Bottom - v.Len(10) - buttonHeight;
        var play = "Play";
        var playFace = Face(Assets.Title, 26, v);
        var playRect = new Rect(right - playFace.Measure(play) - v.Len(52), buttons, playFace.Measure(play) + v.Len(52), buttonHeight);
        var cell = Math.Max(1, v.Len(3));
        canvas.Paint(playRect, (px, py) => ((px / cell + py / cell) & 1) == 0 ? CapitalLight : CapitalDark);
        canvas.Frame(playRect, Math.Max(1, v.Len(2)), _focus == Item.Play ? Yellow : RopeLight);
        playFace.Draw(canvas, play, playRect.X + (playRect.Width - playFace.Measure(play)) / 2, playRect.Y + (playRect.Height - playFace.LineHeight) / 2,
            _model.CanPlay ? Yellow : PaleBlue, v.Len(2), Black);
        Hot(playRect, Item.Play, Part.Row);
        Leaf(playRect, "play");
        var save = "Save and quit";
        var saveWidth = text.Measure(save) + v.Len(26);
        ParchmentButton(canvas, v, new Rect(playRect.X - saveWidth - v.Len(10), buttons, saveWidth, buttonHeight), save, text, Item.Save, Part.Row);

        // between the rows and the buttons: the details box
        var box = new Rect(x - v.Len(6), y + v.Len(6), width + v.Len(12), buttons - v.Len(6) - (y + v.Len(6)));
        DrawDetails(canvas, v, caption, box);
    }

    /// <summary>
    /// The details box: the last message (or why the launcher is shown), the focused row's problem in full, and what the focused
    /// control does; wrapped to the box (long paths broken anywhere), the last line cut with "…" when there is more.
    /// </summary>
    private void DrawDetails(Canvas canvas, View v, TextFace caption, Rect box)
    {
        var pad = v.Len(5);
        var room = (box.Height - 2 * pad) / caption.LineHeight;
        if (room <= 0)
        {
            return;
        }
        for (var py = box.Y; py < box.Bottom; py++)
        {
            for (var px = box.X; px < box.Right; px++)
            {
                canvas.Blend(px, py, FieldBlue, 110);
            }
        }
        canvas.Frame(box, Math.Max(1, v.Len(1)), FieldEdge);
        Leaf(box, "details");
        var lineWidth = box.Width - 2 * pad;
        var lines = new List<(string Text, uint Colour)>();
        foreach (var (part, colour) in Details())
        {
            lines.AddRange(WrapAll(caption, part, lineWidth).Select(l => (l, colour)));
        }
        if (lines.Count > room)
        {
            var last = lines[room - 1];
            lines = [.. lines.Take(room - 1), (Fit(caption, last.Text + " …", lineWidth, fromEnd: false), last.Colour)];
        }
        var ly = box.Y + pad;
        var shadow = Math.Max(1, v.Len(1));
        foreach (var (line, colour) in lines)
        {
            caption.Draw(canvas, line, box.X + pad, ly, colour, shadow, Black);
            ly += caption.LineHeight;
        }
    }

    /// <summary>The details box's parts, most important first.</summary>
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
        if (_focus is Item.HdPack or Item.AudioPack && PackProblem(_focus == Item.HdPack ? PackKind.Pictures : PackKind.Sounds) is { } problem)
        {
            yield return ("Problem: " + problem, PaleRed);
        }
        if (Help(_focus) is { Length: > 0 } help)
        {
            yield return (help, PaleBlue);
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

    /// <summary>The line under a pack row: none, what the pack holds, or what is wrong (in full in the details box).</summary>
    private (string Text, uint Colour) PackLine(PackKind kind)
    {
        if (_model.PackOf(kind) is not { } folder)
        {
            return (kind == PackKind.Pictures ? "None: the game's own pictures." : "None: the game's own music and sounds.", PaleBlue);
        }
        return PackProblem(kind) is { } problem ? ("(!) " + problem, PaleRed) : (PackSummary(kind, _model.Pack(folder)), Green);
    }

    /// <summary>The line under the SoundFont row: the file that plays the packs' .mid music, or where to get a free one.</summary>
    private (string Text, uint Colour) SoundFontLine()
    {
        if (_model.SoundFont is { } chosen && !File.Exists(chosen))
        {
            return ("(!) Not found: " + chosen, PaleRed);
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

    /// <summary>
    /// A setting's row: its label at the left, its value at the right between ‹ › (each a click target: the previous or next
    /// value), with a "Browse…" button at the end when <paramref name="browse"/>; the label and the value are cut to fit. Returns the y below.
    /// </summary>
    private int Row(Canvas canvas, View v, TextFace text, TextFace small, int x, int right, int y, string label, string value, Item item, bool browse = false)
    {
        var row = new Rect(x - v.Len(6), y, right - x + v.Len(12), Math.Max(v.Len(20), text.LineHeight));
        if (_focus == item)
        {
            Highlight(canvas, row);
        }
        Hot(row, item, Part.Row);
        var shadow = Math.Max(1, v.Len(1));
        var ty = y + (row.Height - text.LineHeight) / 2;
        var valueRight = right;
        if (browse)
        {
            var browseLabel = "Browse…";
            var browseWidth = small.Measure(browseLabel) + v.Len(14);
            var buttonHeight = Math.Min(row.Height - v.Len(2), small.LineHeight + v.Len(4));
            ParchmentButton(canvas, v, new Rect(right - browseWidth, y + (row.Height - buttonHeight) / 2, browseWidth, buttonHeight), browseLabel, small, item, Part.Browse);
            valueRight = right - browseWidth - v.Len(8);
        }
        var gap = v.Len(10);
        var labelShown = Fit(text, label, (valueRight - x) * 3 / 5, fromEnd: false);
        var labelWidth = text.Measure(labelShown);
        text.Draw(canvas, labelShown, x, ty, White, shadow, Black);
        Leaf(new Rect(x, ty, labelWidth, text.LineHeight), "label " + label);

        var fixedBy = IsFixed(item, out _);
        var (open, close) = fixedBy ? ("", "") : ("‹ ", " ›");
        var room = valueRight - (x + labelWidth + gap) - text.Measure(open) - text.Measure(close);
        var valueShown = Fit(text, value, Math.Max(0, room), fromEnd: false);
        var closeX = valueRight - text.Measure(close);
        var valueX = closeX - text.Measure(valueShown);
        var openX = valueX - text.Measure(open);
        var colour = fixedBy ? PaleBlue : Yellow;
        text.Draw(canvas, open, openX, ty, colour, shadow, Black);
        text.Draw(canvas, valueShown, valueX, ty, colour, shadow, Black);
        text.Draw(canvas, close, closeX, ty, colour, shadow, Black);
        Leaf(new Rect(openX, ty, valueRight - openX, text.LineHeight), "value " + label);
        if (!fixedBy)
        {
            // the arrows: a click on ‹ or › steps back or on, as ←/→
            Hot(new Rect(openX - v.Len(4), row.Y, valueX - openX + v.Len(4), row.Height), item, Part.Previous);
            Hot(new Rect(closeX, row.Y, valueRight + v.Len(4) - closeX, row.Height), item, Part.Next);
        }
        return row.Bottom;
    }

    private void Hot(Rect area, Item item, Part part) => _hotspots.Add((area, item, part));

    /// <summary>Notes a drawn thing of the settings panel (for the layout's checks: in the panel, over nothing else).</summary>
    private void Leaf(Rect area, string what) => _blocks.Add((area, what));

    private static void Highlight(Canvas canvas, Rect row)
    {
        for (var py = row.Y; py < row.Bottom; py++)
        {
            for (var px = row.X; px < row.Right; px++)
            {
                canvas.Blend(px, py, White, 40);
            }
        }
    }

    /// <summary>A button on parchment; framed in yellow when it (or, for a row's Browse…, its row) has the focus.</summary>
    private void ParchmentButton(Canvas canvas, View v, Rect area, string label, TextFace text, Item item, Part part)
    {
        var cell = Math.Max(1, v.Len(2));
        canvas.Paint(area, (x, y) => ((x / cell + y / cell) & 1) == 0 ? ParchmentLight : ParchmentDark);
        canvas.Frame(area, Math.Max(1, v.Len(part == Part.Browse ? 1 : 2)), _focus == item ? Yellow : RopeMid);
        var shown = Fit(text, label, area.Width - v.Len(4), fromEnd: false);
        text.Draw(canvas, shown, area.X + (area.Width - text.Measure(shown)) / 2, area.Y + (area.Height - text.LineHeight) / 2, Black);
        Hot(area, item, part);
        Leaf(area, "button " + label);
    }

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

    /// <summary>The rope border of the original's panels: a twisted cord of three browns, with a dark edge.</summary>
    private static void Rope(Canvas canvas, Rect area, View v)
    {
        var period = 8 * v.Scale;
        var border = v.Len(7);
        foreach (var band in new[]
        {
            new Rect(area.X, area.Y, area.Width, border),
            new Rect(area.X, area.Bottom - border, area.Width, border),
            new Rect(area.X, area.Y + border, border, area.Height - 2 * border),
            new Rect(area.Right - border, area.Y + border, border, area.Height - 2 * border),
        })
        {
            canvas.Paint(band, (x, y) =>
            {
                var t = (band.X - area.X + x + band.Y - area.Y + y) / Math.Sqrt(2) % period / period * 8;
                return t < 3 ? RopeLight : t < 6 ? RopeMid : RopeDark;
            });
        }
        canvas.Frame(area, Math.Max(1, v.Len(1)), RopeEdge);
        canvas.Frame(area.Inset(border - Math.Max(1, v.Len(1))), Math.Max(1, v.Len(1)), RopeEdge);
    }

    /// <summary>The original's bright VGA blue, lighter at the middle and darker towards the edges.</summary>
    private static void Blue(Canvas canvas, Rect area)
    {
        var light = Colours.Shade(PanelBlue, GlowStrength);
        var dark = Colours.Shade(PanelBlue, -GlowStrength);
        double cx = area.Width * 0.5, cy = area.Height * 0.4;
        var rx = Math.Max(cx, area.Width - cx) * Math.Sqrt(2);
        var ry = Math.Max(cy, area.Height - cy) * Math.Sqrt(2);
        // the 256 shades between the two, and each pixel's place between them
        var shades = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            shades[i] = Colours.Mix(light, dark, i);
        }
        canvas.Paint(area, (x, y) =>
        {
            double dx = (x - cx) / rx, dy = (y - cy) / ry;
            return shades[(int)(Math.Min(1, Math.Sqrt(dx * dx + dy * dy)) * 255 + 0.5)];
        });
    }

    /// <summary>The design's coordinates scaled and centred on the surface.</summary>
    private readonly record struct View(double Scale, int OffsetX, int OffsetY)
    {
        public int Len(double length) => (int)Math.Round(length * Scale);

        public Rect Rect(double x, double y, double width, double height) =>
            new(OffsetX + Len(x), OffsetY + Len(y), Len(width), Len(height));
    }
}
