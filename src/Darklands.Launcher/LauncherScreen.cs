using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher;

/// <summary>
/// The launcher's one screen, in the look of the original game's menus: the title and the release notes and the project's
/// note on parchment at the left, the settings on a blue panel at the right, everything framed in rope. Laid out on a
/// 1280x720 design (DesignWidth, DesignHeight) and scaled to the surface.
/// </summary>
internal sealed partial class LauncherScreen
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
    private const uint PaleCream = 0xE8DCC4;
    private const uint Green = 0xA6DB8E;

    private readonly LauncherModel _model;
    private readonly LauncherContext _context;
    private readonly ILauncherSurface _surface;
    private readonly IReadOnlyList<string> _notes;

    /// <summary>The notes' items with their line of detail (ReleaseNotes.Detailed), in the order of <see cref="_notes"/>.</summary>
    private readonly IReadOnlyList<(string Title, string Detail)> _noteDetails;

    /// <summary>The What's new card's detail lines as last drawn: their areas and texts (none when the card had no room for them).</summary>
    internal List<(Rect Area, string Text)> NoteDetailsShown { get; } = [];
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

    // the tooltip shown (None: none), and the control the pointer came to (its tooltip after a rest)
    private Item _tip, _tipPending;
    private Part _tipPart, _tipPendingPart;
    private bool _tipByKeyboard;

    // the status lines cut to fit, by their row (their whole text is in the row's tooltip)
    private readonly HashSet<Item> _statusCut = [];
    private readonly Dictionary<Item, (string Text, uint Colour)> _statusShown = [];
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
        _noteDetails = ReleaseNotes.Detailed(context.ReleaseNotes);
        _focus = model.CanPlay ? Item.Play : Item.GameFolder;
    }

    public enum Item
    {
        None,
        GameFolder,
        Change,
        Window,
        Fullscreen,
        Picture,
        HdPack,
        Sound,
        Roms,
        Test,
        Banner,
        Intro,
        MouseWheel,
        CardMenuKeys,
        Save,
        Play,
        Discord,
        Notes,
        AudioPack,
        LanguagePack,
        PackGuide,
        SoundFont,
        Report,
        TabGeneral,
        TabAdvanced,

        /// <summary>The frameless window's own buttons (<see cref="ILauncherSurface.Frameless"/>).</summary>
        Minimise,
        Close,

        /// <summary>The notice over the buttons (a message: why the game could not start, what an action did).</summary>
        Notice,
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

        /// <summary>The status line under a row (a click chooses the row; the pointer resting on it shows its whole text).</summary>
        Status,
    }

    public Item Focus => _focus;

    /// <summary>How long the pointer rests on a control before its tooltip shows.</summary>
    public static readonly TimeSpan TooltipDelay = TimeSpan.FromMilliseconds(450);

    /// <summary>The pointer came to a control with a tooltip: wait for it to rest (<see cref="TimePassed"/>).</summary>
    public bool TooltipPending => _tipPending != Item.None;

    /// <summary>The control whose tooltip is shown, or None.</summary>
    public Item Tooltip => _tip;

    /// <summary>Where the tooltip was drawn last (empty: none).</summary>
    internal Rect TooltipArea { get; private set; }

    private void HideTooltip()
    {
        _tip = Item.None;
        _tipPending = Item.None;
        _tipByKeyboard = false;
    }

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
    private static bool OnAdvanced(Item item) => item is Item.Roms or Item.SoundFont or Item.LanguagePack or Item.Banner or Item.Intro or Item.MouseWheel or Item.CardMenuKeys;

    private static bool OnGeneral(Item item) => item is Item.GameFolder or Item.Change or Item.Window or Item.Fullscreen or Item.Picture or Item.HdPack or Item.Sound or Item.Test or Item.AudioPack;

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
        new[] { Item.GameFolder, Item.Change, Item.Window, Item.Fullscreen, Item.Picture, Item.HdPack, Item.Sound, Item.Test, Item.AudioPack, Item.Roms, Item.SoundFont, Item.LanguagePack, Item.Banner, Item.Intro, Item.MouseWheel, Item.CardMenuKeys, Item.Save, Item.Play, Item.Discord, Item.PackGuide, Item.Report, Item.Notes }
            .Where(i => (i != Item.Test || _context.SoundTest is not null) && (i != Item.Notes || _context.ReleaseNotesLink is not null)
                && (i != Item.PackGuide || _context.PackGuide is not null) && (i != Item.Report || _context.ReportProblem is not null));

    /// <summary>Handles one input; returns the player's choice when the launcher should end.</summary>
    public LauncherChoice? Handle(LauncherInput input)
    {
        switch (input)
        {
            case CloseRequested:
                return LauncherChoice.Quit;
            case TimePassed:
                // the pointer rested on a control: its tooltip
                (_tip, _tipPart) = (_tipPending, _tipPendingPart);
                _tipPending = Item.None;
                return null;
            case PointerReleased released:
                HideTooltip();
                var (pressed, pressedPart) = (_pressed, _pressedPart);
                _pressed = Item.None;
                var under = _hotspots.LastOrDefault(h => h.Area.Contains(released.X, released.Y));
                return pressed != Item.None && under.Item == pressed && under.Part == pressedPart
                    ? Activate(pressed, 1, cycle: false)
                    : null;
            case PointerMoved moved:
                var over = _hotspots.LastOrDefault(h => h.Area.Contains(moved.X, moved.Y));
                if ((over.Item, over.Part) != (_hover, _hoverPart) || _tipByKeyboard)
                {
                    // another control (or none): its tooltip after a rest
                    HideTooltip();
                    (_tipPending, _tipPendingPart) = (over.Item, over.Part);
                }
                (_hover, _hoverPart) = (over.Item, over.Part);
                return null;
            case PathDropped dropped:
                Dropped(dropped.Path);
                return null;
            case PointerPressed press:
                HideTooltip();
                var hit = _hotspots.LastOrDefault(h => h.Area.Contains(press.X, press.Y));
                if (hit.Item is Item.None or Item.Notice)
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
                    // a click on a status line chooses its row
                    Part.Status => Selected(),
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
                var before = _focus;
                var choice = Key(key);
                if (_focus != before)
                {
                    // the keyboard chose another control: its tooltip at once (the pointer moving hides it)
                    HideTooltip();
                    (_tip, _tipPart, _tipByKeyboard) = (_focus, Part.Row, true);
                }
                return choice;
            default:
                return null;
        }
    }

    /// <summary>The plates: Browse… / Change… on their rows, Change… of the game folder, Test sound, Save and quit, Play.</summary>
    private static bool IsPlate(Item item, Part part) => part == Part.Browse || item is Item.Change or Item.Test or Item.Save or Item.Play or Item.Minimise or Item.Close;

    /// <summary>Where the controls were drawn last (the links, rows, plates, tabs and the window's buttons): a frameless window is dragged elsewhere.</summary>
    internal IReadOnlyList<(int X, int Y, int Width, int Height)> ControlAreas => [.. _hotspots.Select(h => (h.Area.X, h.Area.Y, h.Area.Width, h.Area.Height))];

    /// <summary>The rows a click only chooses: Enter or their Browse… / Change… button opens the system's dialog.</summary>
    private static bool SelectedByClick(Item item) => item is Item.HdPack or Item.AudioPack or Item.LanguagePack or Item.SoundFont or Item.Roms;

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
                if (_focus is Item.Window or Item.Fullscreen or Item.Picture or Item.HdPack or Item.AudioPack or Item.LanguagePack or Item.SoundFont or Item.Sound or Item.Banner or Item.Intro or Item.MouseWheel or Item.CardMenuKeys or Item.GameFolder)
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
            _messageColour = PaleCream;
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
                if (Pick("Choose your Darklands folder (the one with DARKLAND.EXE, or the GOG or Steam install folder)", _model.GameFolder, out var picked))
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
                    _messageColour = PaleCream;
                }
                break;
            case Item.Window:
                _model.CycleWindow(step);
                break;
            case Item.Fullscreen:
                _model.Fullscreen = !_model.Fullscreen;
                break;
            case Item.Picture:
                _model.StepFilter(step);
                break;
            case Item.HdPack or Item.AudioPack or Item.LanguagePack:
                ChoosePack(item switch { Item.HdPack => PackKind.Pictures, Item.AudioPack => PackKind.Sounds, _ => PackKind.Language }, step, cycle);
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
                if (Pick("Choose the folder with your MT-32 or CM-32L ROM files", _model.Mt32Roms, out var romFolder))
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
            case Item.MouseWheel:
                _model.MouseWheel = !_model.MouseWheel;
                break;
            case Item.CardMenuKeys:
                _model.CardMenuKeys = !_model.CardMenuKeys;
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
            Item.Picture => "display.filter",
            Item.HdPack => "display.hdPack",
            Item.AudioPack => "sound.audioPack",
            Item.LanguagePack => "languagePack",
            Item.SoundFont => "sound.soundFont",
            Item.Sound => "sound.card",
            Item.Roms => "sound.mt32Roms",
            Item.Banner => "startup.banner",
            Item.Intro => "startup.intro",
            Item.MouseWheel => "input.mouseWheel",
            Item.CardMenuKeys => "input.cardMenuKeys",
            _ => null,
        };
        return key is not null && _model.IsFixed(key, out by);
    }

    /// <summary>The design's coordinates scaled and centred on the surface.</summary>
    private readonly record struct View(double Scale, int OffsetX, int OffsetY)
    {
        public int Len(double length) => (int)Math.Round(length * Scale);

        public Rect Rect(double x, double y, double width, double height) =>
            new(OffsetX + Len(x), OffsetY + Len(y), Len(width), Len(height));
    }
}
