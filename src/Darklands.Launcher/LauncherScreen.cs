using Darklands.Launcher.Drawing;

namespace Darklands.Launcher;

/// <summary>
/// The launcher's one screen, in the look of the original game's menus: the title and the release notes and the project's
/// note on parchment at the left, the settings on a blue panel at the right, everything framed in rope. Laid out on a
/// 1080x600 design and scaled to the surface.
/// </summary>
internal sealed class LauncherScreen
{
    public const string DiscordLink = "https://discord.gg/HjzWvmHhqZ";

    private const int DesignWidth = 1080;
    private const int DesignHeight = 600;

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
    private List<(Rect Area, Item Item)> _hotspots = [];
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
    }

    public Item Focus => _focus;

    /// <summary>Where <paramref name="item"/> was drawn last.</summary>
    public Rect Area(Item item) => _hotspots.First(h => h.Item == item).Area;

    public bool Editing => _editing;

    private IEnumerable<Item> Order =>
        new[] { Item.GameFolder, Item.Change, Item.Window, Item.Fullscreen, Item.HdPack, Item.Sound, Item.Roms, Item.Test, Item.AudioPack, Item.Banner, Item.Intro, Item.Always, Item.Save, Item.Play, Item.Discord, Item.PackGuide, Item.Notes }
            .Where(i => (i != Item.Test || _context.SoundTest is not null) && (i != Item.Notes || _context.ReleaseNotesLink is not null)
                && (i != Item.PackGuide || _context.PackGuide is not null));

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
                return Activate(hit.Item, step: 1, cycle: false);
            case TextTyped typed when _editing:
                _editText = (_editFresh ? "" : _editText) + typed.Text;
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
                if (_focus is Item.Window or Item.Fullscreen or Item.HdPack or Item.AudioPack or Item.Sound or Item.Banner or Item.Intro or Item.Always or Item.GameFolder)
                {
                    return Activate(_focus, step, cycle: true);
                }
                MoveFocus(step);
                return null;
            case LauncherKey.Enter:
                return Activate(_focus, 1, cycle: false);
            case LauncherKey.Escape:
                return LauncherChoice.Quit;
            case LauncherKey.Paste when _focus == Item.GameFolder:
                StartEditing(_surface.Clipboard() ?? "");
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
        if (IsFixed(item, out _))
        {
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
                if (_surface.PickFolder is { } pick)
                {
                    if (pick("Choose your Darklands folder (the one with DARKLAND.EXE, or the GOG or Steam install folder)") is { } picked)
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
                    _focus = Item.GameFolder;
                    StartEditing(_model.GameFolder ?? "");
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
            case Item.PackGuide when _context.PackGuide is { } guide:
                _context.OpenLink(guide);
                break;
            case Item.Sound:
                _model.CycleSoundCard(step);
                break;
            case Item.Roms:
                if (_surface.PickFolder is { } pickRoms)
                {
                    if (pickRoms("Choose the folder with your MT-32 or CM-32L ROM files") is { } picked)
                    {
                        SetRoms(picked);
                    }
                }
                else
                {
                    _message = "Drop the folder with your MT-32 or CM-32L ROM files on this window.";
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

    private void Dropped(string path)
    {
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
            _message = $"{path} holds neither Darklands (DARKLAND.EXE), Roland ROMs nor a pack (PNG pictures, OGG or WAV sounds).";
        }
    }

    private static Item PackItem(PackKind kind) => kind == PackKind.Pictures ? Item.HdPack : Item.AudioPack;

    /// <summary>A pack row: ←/→ go through None and the packs found; Enter or a click browses for a folder (or goes on, without a dialog).</summary>
    private void ChoosePack(PackKind kind, int step, bool cycle)
    {
        if (!cycle && _surface.PickFolder is { } pick)
        {
            var what = kind == PackKind.Pictures ? "a picture (HD) pack: PNG files named after the game's pictures"
                : "a sound pack: OGG or WAV files named after the game's music and effects";
            if (pick("Choose the folder of " + what) is { } picked)
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

    /// <summary>The chosen pack's state in the message line: what it holds, or what could not be read (its manifest ...).</summary>
    private void PackMessage(PackKind kind)
    {
        if (_model.PackOf(kind) is not { } folder)
        {
            return;
        }
        var status = _model.Pack(folder);
        (_message, _messageColour) = status.Problem is { } problem ? (problem, PaleRed) : (PackSummary(kind, status), Green);
    }

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
        if (apply && _editText.Trim() is { Length: > 0 } typed && typed != _model.GameFolder)
        {
            SetGameFolder(typed.Trim('"'));
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
                _editText = (_editFresh ? "" : _editText) + (_surface.Clipboard() ?? "");
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
            Item.Sound => "sound.card",
            Item.Roms => "sound.mt32Roms",
            Item.Banner => "startup.banner",
            Item.Intro => "startup.intro",
            _ => null,
        };
        return key is not null && _model.IsFixed(key, out by);
    }

    // ---- drawing ----

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
        _hotspots = [(_discord, Item.Discord)];
        if (_notesLink is { } notes)
        {
            _hotspots.Add((notes, Item.Notes));
        }
        if (_guideLink is { } guide)
        {
            _hotspots.Add((guide, Item.PackGuide));
        }
        if ((_focus == Item.Discord ? _discord : _focus == Item.Notes ? _notesLink : _focus == Item.PackGuide ? _guideLink : null) is { } link)
        {
            canvas.Fill(new Rect(link.X, link.Bottom - Math.Max(1, v.Len(1)), link.Width, Math.Max(1, v.Len(1))), Ink);
        }
        DrawSettings(canvas, v);
    }

    private void DrawBackground(Canvas canvas, View v)
    {
        canvas.Clear(Black);

        // the title, on blue
        var top = v.Rect(14, 14, 586, 86);
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

        var settings = v.Rect(610, 14, 456, 572);
        Rope(canvas, settings, v);
        Blue(canvas, settings.Inset(v.Len(7)));
    }

    private void DrawParchment(Canvas canvas, View v)
    {
        var frame = v.Rect(14, 108, 586, 478);
        Rope(canvas, frame, v);
        var inner = frame.Inset(v.Len(7));
        var cell = Math.Max(1, v.Len(2));
        canvas.Paint(inner, (x, y) => ((x / cell + y / cell) & 1) == 0 ? ParchmentLight : ParchmentDark);
        canvas.Image(Assets.Badge, new Rect(inner.X + v.Len(76), inner.Y + v.Len(20), v.Len(420), v.Len(420)), BadgeOpacity);

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

    private void DrawSettings(Canvas canvas, View v)
    {
        var inner = v.Rect(610, 14, 456, 572).Inset(v.Len(7));

        var x = inner.X + v.Len(18);
        var right = inner.Right - v.Len(18);
        var width = right - x;
        var y = inner.Y + v.Len(12);
        var title = Face(Assets.Title, 30, v);
        var heading = Face(Assets.Heading, 22, v);
        var text = Face(Assets.Panel, 16, v);
        var caption = Face(Assets.Panel, 13, v);
        var shadow = Math.Max(1, v.Len(1));

        title.Draw(canvas, "Settings", x, y, Yellow, v.Len(2), Black);
        y += title.LineHeight;
        // the sound test's status, while it is the last thing asked for (it goes when the music ends)
        var (note, noteColour) = _showSound && _context.SoundTest is { Status: { } soundStatus } soundTester ? (soundStatus, soundTester.Playing ? Green : PaleRed)
            : _message is not null ? (_message, _messageColour) : _context.Reason switch
        {
            LauncherReason.StartFailed => (_context.Problem ?? "The game could not start.", PaleRed),
            LauncherReason.GameNotFound when !_model.CanPlay => ("Darklands was not found: choose its folder to play.", PaleRed),
            LauncherReason.FirstRun => ("Welcome! Check the settings, then press Play.", PaleBlue),
            _ => ("Changes are written to settings.json", PaleBlue),
        };
        foreach (var l in caption.Wrap(note, width).Take(3))
        {
            caption.Draw(canvas, l, x, y, noteColour, shadow, Black);
            y += caption.LineHeight;
        }

        // the game folder
        y += v.Len(4);
        heading.Draw(canvas, "Game", x, y, Yellow, shadow, Black);
        y += heading.LineHeight;
        var change = "Change…";
        var buttonWidth = text.Measure(change) + v.Len(20);
        var field = new Rect(x, y, width - buttonWidth - v.Len(8), v.Len(28));
        canvas.Fill(field, FieldBlue);
        canvas.Frame(field, Math.Max(1, v.Len(1)), _focus == Item.GameFolder ? Yellow : FieldEdge);
        var shown = _editing ? _editText + "_" : _model.GameFolder ?? "";
        var (pathFont, pathSize, pathStep) = Assets.Path;
        var path = FaceAt(pathFont, Math.Max(pathStep, Math.Round(pathSize * v.Scale / pathStep) * pathStep));
        path.Draw(canvas, Fit(path, shown, field.Width - v.Len(16), fromEnd: true), field.X + v.Len(8), field.Y + (field.Height - path.LineHeight) / 2, White);
        _hotspots.Add((field, Item.GameFolder));
        ParchmentButton(canvas, v, new Rect(field.Right + v.Len(8), y, buttonWidth, field.Height), change, text, Item.Change);
        y += field.Height + v.Len(4);
        var (status, statusColour) = _model.GameFolderChecked is not null
            ? ("Found" + (_model.GameFolderSource is { } source ? $" ({source})." : "."), Green)
            : _model.GameFolder is null ? ("Type its folder, drop it on this window or press Change…", PaleBlue) : ("No Darklands there (DARKLAND.EXE).", PaleRed);
        caption.Draw(canvas, Fit(caption, status, width, fromEnd: false), x, y, statusColour, shadow, Black);
        y += caption.LineHeight;

        // display
        y += v.Len(2);
        heading.Draw(canvas, "Display", x, y, Yellow, shadow, Black);
        y += heading.LineHeight - v.Len(3);
        y = Row(canvas, v, text, x, right, y, "Window size", LauncherModel.SizeLabel(_model.Windows[_model.WindowIndex]), Item.Window);
        y = Row(canvas, v, text, x, right, y, "Fullscreen", _model.Fullscreen ? "Yes" : "No", Item.Fullscreen);
        y = Row(canvas, v, text, x, right, y, "HD pack", Fit(text, PackShown(PackKind.Pictures), width / 2, fromEnd: false), Item.HdPack);

        // sound and music
        y += v.Len(2);
        heading.Draw(canvas, "Sound & Music", x, y, Yellow, shadow, Black);
        y += heading.LineHeight - v.Len(3);
        y = Row(canvas, v, text, x, right, y, "Sound card", _model.SoundCardLabel, Item.Sound);
        // the Roland ROM folder, as the game folder: its field, Change… and what it holds
        var romsLabel = "Roland ROMs";
        var labelWidth = text.Measure(romsLabel) + v.Len(10);
        var romsButtonWidth = text.Measure(change) + v.Len(20);
        var romsField = new Rect(x + labelWidth, y + v.Len(2), width - labelWidth - romsButtonWidth - v.Len(8), v.Len(26));
        text.Draw(canvas, romsLabel, x, romsField.Y + (romsField.Height - text.LineHeight) / 2, White, shadow, Black);
        var roms = _model.Roms;
        var romsFixed = IsFixed(Item.Roms, out _);
        canvas.Fill(romsField, FieldBlue);
        canvas.Frame(romsField, Math.Max(1, v.Len(1)), _focus == Item.Roms ? Yellow : FieldEdge);
        path.Draw(canvas, Fit(path, roms.Folder, romsField.Width - v.Len(16), fromEnd: true), romsField.X + v.Len(8),
            romsField.Y + (romsField.Height - path.LineHeight) / 2, romsFixed ? PaleBlue : White);
        _hotspots.Add((romsField, Item.Roms));
        ParchmentButton(canvas, v, new Rect(romsField.Right + v.Len(8), romsField.Y, romsButtonWidth, romsField.Height), change, text, Item.Roms);
        y = romsField.Bottom + v.Len(4);
        var (romsStatus, romsColour) = RomsStatus(roms);
        var statusRight = right;
        if (_context.SoundTest is { } soundTest)
        {
            var test = soundTest.Playing ? "Stop" : "Test sound";
            var testWidth = Math.Max(caption.Measure("Test sound"), caption.Measure("Stop")) + v.Len(14);
            ParchmentButton(canvas, v, new Rect(right - testWidth, y - v.Len(1), testWidth, caption.LineHeight + v.Len(2)), test, caption, Item.Test);
            statusRight = right - testWidth - v.Len(8);
        }
        caption.Draw(canvas, Fit(caption, romsStatus, statusRight - x, fromEnd: false), x, y, romsColour, shadow, Black);
        y += caption.LineHeight;
        y = Row(canvas, v, text, x, right, y, "Audio pack", Fit(text, PackShown(PackKind.Sounds), width / 2, fromEnd: false), Item.AudioPack);

        // start-up
        y += v.Len(2);
        heading.Draw(canvas, "Start-up", x, y, Yellow, shadow, Black);
        y += heading.LineHeight - v.Len(3);
        y = Row(canvas, v, text, x, right, y, "Original start-up screen", _model.Banner ? "Yes" : "No", Item.Banner);
        y = Row(canvas, v, text, x, right, y, "Opening animation", _model.Intro ? "Yes" : "No", Item.Intro);
        y = Row(canvas, v, text, x, right, y, "Show launcher at every start", _model.Always ? "Yes" : "No", Item.Always);

        // the buttons, and over them the focused control's explanation
        var buttons = inner.Bottom - v.Len(14) - v.Len(40);
        var play = "Play";
        var playFace = Face(Assets.Title, 28, v);
        var playRect = new Rect(right - playFace.Measure(play) - v.Len(56), buttons, playFace.Measure(play) + v.Len(56), v.Len(40));
        var cell = Math.Max(1, v.Len(3));
        canvas.Paint(playRect, (px, py) => ((px / cell + py / cell) & 1) == 0 ? CapitalLight : CapitalDark);
        canvas.Frame(playRect, Math.Max(1, v.Len(2)), _focus == Item.Play ? Yellow : RopeLight);
        playFace.Draw(canvas, play, playRect.X + (playRect.Width - playFace.Measure(play)) / 2, playRect.Y + (playRect.Height - playFace.LineHeight) / 2,
            _model.CanPlay ? Yellow : PaleBlue, v.Len(2), Black);
        _hotspots.Add((playRect, Item.Play));
        var save = "Save and quit";
        var saveWidth = text.Measure(save) + v.Len(28);
        ParchmentButton(canvas, v, new Rect(playRect.X - saveWidth - v.Len(10), buttons, saveWidth, v.Len(40)), save, text, Item.Save);

        var help = Help(_focus);
        var helpLines = caption.Wrap(help, width);
        // no further than the buttons (the texts are written to fit in two lines)
        helpLines = [.. helpLines.Take(Math.Max(1, (buttons - v.Len(8) - y - v.Len(4)) / caption.LineHeight))];
        var hy = Math.Max(y + v.Len(4), buttons - v.Len(8) - helpLines.Count * caption.LineHeight);
        foreach (var l in helpLines)
        {
            caption.Draw(canvas, l, x, hy, PaleBlue, shadow, Black);
            hy += caption.LineHeight;
        }
    }

    /// <summary>A pack row's value: the folder's name, marked when it cannot be read as a pack.</summary>
    private string PackShown(PackKind kind) =>
        _model.PackOf(kind) is { } folder && (_model.Pack(folder).Problem is not null || !_model.Holds(folder, kind))
            ? LauncherModel.PackLabel(folder) + " (!)"
            : LauncherModel.PackLabel(_model.PackOf(kind));

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

    /// <summary>A setting's row: its label at the left, its value at the right between ‹ ›; returns the y below.</summary>
    private int Row(Canvas canvas, View v, TextFace text, int x, int right, int y, string label, string value, Item item)
    {
        var row = new Rect(x - v.Len(6), y, right - x + v.Len(12), v.Len(21));
        if (_focus == item)
        {
            Highlight(canvas, row);
        }
        var shadow = Math.Max(1, v.Len(1));
        var ty = y + (row.Height - text.LineHeight) / 2;
        text.Draw(canvas, label, x, ty, White, shadow, Black);
        var fixedBy = IsFixed(item, out _);
        var shown = fixedBy ? value : $"‹ {value} ›";
        text.Draw(canvas, shown, right - text.Measure(shown), ty, fixedBy ? PaleBlue : Yellow, shadow, Black);
        _hotspots.Add((row, item));
        return row.Bottom;
    }

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

    private void ParchmentButton(Canvas canvas, View v, Rect area, string label, TextFace text, Item item)
    {
        var cell = Math.Max(1, v.Len(2));
        canvas.Paint(area, (x, y) => ((x / cell + y / cell) & 1) == 0 ? ParchmentLight : ParchmentDark);
        canvas.Frame(area, Math.Max(1, v.Len(2)), _focus == item ? Yellow : RopeMid);
        text.Draw(canvas, label, area.X + (area.Width - text.Measure(label)) / 2, area.Y + (area.Height - text.LineHeight) / 2, Black);
        _hotspots.Add((area, item));
    }

    private string Help(Item item)
    {
        var help = item switch
        {
            Item.GameFolder => _editing
                ? "Type or paste the folder, then Enter (Esc cancels)."
                : "The folder with DARKLAND.EXE; the GOG or Steam install folder is fine." + (_model.Found.Count > 1 ? " ←/→: the other folders found." : ""),
            Item.Change => _surface.PickFolder is not null ? "Choose the folder of your copy of Darklands." : "Type the folder of your copy of Darklands.",
            Item.Window => "The window's size; the game keeps its shape.",
            Item.Fullscreen => "Fill the whole screen; the game keeps its shape.",
            Item.HdPack => "Replacement pictures. " + PackHelp(PackKind.Pictures),
            Item.AudioPack => "Replacement music and sounds (OGG, WAV). " + PackHelp(PackKind.Sounds),
            Item.PackGuide => "Opens the guide: how to make a picture or sound pack, and the names of the game's pictures, music and sounds.",
            Item.Sound => "The sound card the game plays its music and sounds on.",
            Item.Roms => "Your MT-32 / CM-32L ROM files (side by side or in MT32, CM32L subfolders). "
                + (_surface.PickFolder is not null ? "Change… or drop the folder here." : "Drop the folder on this window."),
            Item.Test => _context.SoundTest is { Playing: true } ? "Stops the music." : "Plays some of the game's music on the chosen card, with these ROMs.",
            Item.Banner => "The text page the 1992 game showed for a few seconds while it loaded.",
            Item.Intro => "The animated opening before the start screen.",
            Item.Always => "Otherwise the launcher shows only when needed. Hold Shift as the game starts to see it.",
            Item.Save => "Saves the settings and closes.",
            Item.Play => "Saves the settings and starts the game.",
            Item.Discord => "Opens the project's Discord in your browser.",
            Item.Notes => "Opens the release notes of this version in your browser.",
            _ => "",
        };
        return IsFixed(item, out var by) ? $"Set by {by}: change it there. " + help : help;
    }

    /// <summary>The chosen pack's state and how to choose another.</summary>
    private string PackHelp(PackKind kind)
    {
        var state = _model.PackOf(kind) is { } folder
            ? _model.Pack(folder) is { Problem: { } problem } ? problem + " " : PackSummary(kind, _model.Pack(folder)) + " "
            : "";
        return state + "←/→: None or a pack found; " + (_surface.PickFolder is not null ? "Enter: another folder." : "or drop a folder here.");
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
