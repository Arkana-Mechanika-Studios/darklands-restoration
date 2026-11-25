using Darklands.Localization;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Views;

/// <summary>When a setting's change takes effect.</summary>
public enum SettingApplies
{
    /// <summary>At once (the screens that read it pick it up at their next opening).</summary>
    AtOnce,

    /// <summary>For the next new game: a game being played keeps the rules it started with.</summary>
    NewGames,

    /// <summary>At the program's next start (what is read once as it starts: packs, fonts, sound).</summary>
    NextStart,
}

/// <summary>One value a setting can take: as the settings file writes it, and as the screen shows it.</summary>
public sealed record SettingChoice(string Value, string Label);

/// <summary>
/// One row of the settings screen: its settings-file key (docs/SETTINGS.md), its label, its choices with the current one, when a change
/// takes effect, and what fixes it (a command-line option or an environment variable: the row is shown but cannot change; null: free).
/// </summary>
public sealed record SettingRow(string Key, string Label, IReadOnlyList<SettingChoice> Choices, int Current, SettingApplies Applies, string? FixedBy = null)
{
    /// <summary>The current value's label ("" with no choices).</summary>
    public string ValueLabel => Choices.Count == 0 ? "" : Choices[Math.Clamp(Current, 0, Choices.Count - 1)].Label;

    /// <summary>Whether the player can change it here: not fixed, and more than one choice.</summary>
    public bool CanChange => FixedBy is null && Choices.Count > 1;

    /// <summary>The choice <paramref name="step"/> places on (round), null when it cannot change.</summary>
    public SettingChoice? Stepped(int step)
    {
        if (!CanChange)
        {
            return null;
        }
        var n = Choices.Count;
        return Choices[(((Current + step) % n) + n) % n];
    }
}

/// <summary>The settings screen's command: set <paramref name="Key"/> to <paramref name="Value"/> (the host carries it out and writes the file).</summary>
public sealed record SettingChange(string Key, string Value);

/// <summary>The keys of the settings screen's own texts (new texts: the original has no such screen), translatable by a language pack.</summary>
public static class SettingsTexts
{
    public const string Title = "id:rw.settings.title";
    public const string Close = "id:rw.settings.close";
    public const string AppliesAtOnce = "id:rw.settings.applies.at-once";
    public const string AppliesNewGames = "id:rw.settings.applies.new-games";
    public const string AppliesNextStart = "id:rw.settings.applies.next-start";
    public const string FixedBy = "id:rw.settings.fixed-by";
    public const string Keys = "id:rw.settings.keys";
    public const string MenuLine = "id:rw.settings.menu";
    public const string PictureFilter = "id:rw.settings.row.filter";
    public const string Window = "id:rw.settings.row.window";
    public const string Fullscreen = "id:rw.settings.row.fullscreen";
    public const string Curvature = "id:rw.settings.row.curvature";
    public const string Interface = "id:rw.settings.row.interface";
    public const string Rules = "id:rw.settings.row.rules";
    public const string Language = "id:rw.settings.row.language";
    public const string Fonts = "id:rw.settings.row.fonts";
    public const string SoundCard = "id:rw.settings.row.sound-card";
    public const string Roms = "id:rw.settings.row.roms";
    public const string AudioPack = "id:rw.settings.row.audio-pack";
    public const string SoundFont = "id:rw.settings.row.soundfont";
    public const string CleanDrawing = "id:rw.settings.row.clean-drawing";
    public const string On = "id:rw.settings.on";
    public const string Off = "id:rw.settings.off";
    public const string None = "id:rw.settings.none";
    public const string Default = "id:rw.settings.default";
    public const string Classic = "id:rw.settings.interface.classic";
    public const string Comfort = "id:rw.settings.interface.comfort";
    public const string Original = "id:rw.settings.rules.original";
    public const string Fixed = "id:rw.settings.rules.fixed";
    public const string OriginalFonts = "id:rw.settings.fonts.original";
    public const string AsSetUp = "id:rw.settings.sound.as-set-up";
    public const string NoSound = "id:rw.settings.sound.none";

    /// <summary>The picture filter's label (display.filter's name: sharp, even ...).</summary>
    public static string Filter(string name) => "id:rw.settings.filter." + name;
}

/// <summary>
/// The settings screen's slots (the 1.2 slot kinds, design 2.2): the rows as a list, the Close button as an area. Its drawing and its
/// hit tests read the same entries, so a layout that moves one moves the other.
/// </summary>
public static class SettingsLayout
{
    /// <summary>Its id in the view catalogue, and the screen its texts are listed under.</summary>
    public const string Screen = "Settings";

    /// <summary>The page's left edge: the card window's rope frame (260 x 200) centred on the screen.</summary>
    public const int PageX = (Screen320 - PageWidth) / 2;

    public const int PageWidth = 260;

    private const int Screen320 = 320;

    /// <summary>The rows: a list from under the title, one row a setting.</summary>
    public static SlotLayout Rows(int count) => new(X: PageX + 12, Y: 22, Width: PageWidth - 24, Kind: SlotKinds.List, Rows: count, RowHeight: 10);

    /// <summary>The Close button, at the page's foot on the right.</summary>
    public static SlotLayout Close { get; } = new(X: PageX + PageWidth - 12 - 44, Y: 181, Width: 44, Height: 11, Kind: SlotKinds.Area);

    /// <summary>The keys' line, over the page's foot.</summary>
    public static SlotLayout KeysLine { get; } = new(X: PageX + 12, Y: 171, Width: PageWidth - 24);

    /// <summary>The note on the row chosen (when its change takes effect, what fixes it), at the page's foot on the left.</summary>
    public static SlotLayout Note { get; } = new(X: PageX + 12, Y: 154, Width: PageWidth - 24, Lines: 2);

    /// <summary>Where the values start in a row (the labels on its left).</summary>
    public const int ValueColumn = 112;
}

/// <summary>What the settings screen draws with: the game's fonts, the words (a language pack's when one is loaded), the card window's rope frame and the colours of its page.</summary>
/// <param name="Text">The card window's font (slot 3): the title and the rows.</param>
/// <param name="Small">The menu bar's font (slot 1): the note and the button.</param>
/// <param name="Words">A text's words by its key.</param>
/// <param name="Rope">The rope frame's four pictures (top, left, right, bottom; null: drawn without them).</param>
/// <param name="PageColors">The page's colours by index (the rope's and the parchment's, as the card pictures define them).</param>
public sealed record SettingsLook(TextPainter Text, TextPainter Small, Func<Core.GameText, string> Words,
    (Content.Formats.Graphics.IndexedImage Top, Content.Formats.Graphics.IndexedImage Left, Content.Formats.Graphics.IndexedImage Right, Content.Formats.Graphics.IndexedImage Bottom)? Rope,
    IReadOnlyDictionary<int, uint> PageColors)
{
    /// <summary>The look of the game the player runs: its fonts and words as the screens have them, the rope pictures, and the page's colours from LOADSCRN.PIC.</summary>
    public static SettingsLook Of(GameUi ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var assets = ui.Assets;
        (Content.Formats.Graphics.IndexedImage, Content.Formats.Graphics.IndexedImage, Content.Formats.Graphics.IndexedImage, Content.Formats.Graphics.IndexedImage)? rope =
            new[] { "rpbdrtop", "rpbdrlft", "rpbdrrgt", "rpbdrbtm" }.All(assets.PictureExists)
                ? (assets.Picture("rpbdrtop").Image, assets.Picture("rpbdrlft").Image, assets.Picture("rpbdrrgt").Image, assets.Picture("rpbdrbtm").Image)
                : null;
        var colors = new Dictionary<int, uint>();
        // the card pictures all define the rope's colours (0x81..0x8E) and the parchment (0xFF) alike; the load screen's picture is one
        if (assets.PictureExists("loadscrn") && assets.Picture("loadscrn").EmbeddedPalette is { } chunk)
        {
            foreach (var i in SettingsView.PageIndices)
            {
                if (i >= chunk.StartIndex && i <= chunk.EndIndex && i < chunk.Entries.Count && chunk.Entries[i] is { } rgb)
                {
                    colors[i] = 0xFF000000u | (uint)rgb.R << 16 | (uint)rgb.G << 8 | rgb.B;
                }
            }
        }
        return new SettingsLook(ui.CardText, ui.Text, t => ui.Words(t), rope, colors);
    }
}

/// <summary>
/// The in-game settings screen (1.2, S-1; design 3.2-3.3), the first registry view: a page in the game's own style (the card window's
/// rope frame on parchment, the game's fonts) over the game's frame darkened. ↑ / ↓ choose a row, ← / → (Enter, Space) change it, a
/// click on a row changes it, Esc or the key that opened it (or Close) closes it. A change is a command (<see cref="SettingChange"/>)
/// the host carries out and writes to the settings file; the rows it shows are the host's (read again each frame).
/// </summary>
// Not in the original: the 1.2 settings screen (design note DESIGN-view-registry-and-settings, 3.2-3.3).
public sealed class SettingsView(SettingsLook look, Func<IReadOnlyList<SettingRow>> rows, Action<SettingChange> carryOut, int? closeKey = null) : IRegistryView
{
    // the colours of the startup palette (0..15, the same on every screen) the page's text uses
    private const byte Ink = 0;
    private const byte ValueInk = 6;
    // the chosen row: the menus' highlight colour, as the game's menus mark their chosen line (no pointer: the user's choice, 2026-11-25)
    private const byte ChosenInk = Palette.Highlight;
    private const byte FixedInk = 7;
    private const byte TitleInk = 4;
    private const byte NoteInk = 8;
    private const byte Parchment = 0xFF;

    /// <summary>The palette entries the page draws with: the startup colours, the rope's, the parchment.</summary>
    internal static readonly int[] PageIndices = [0x81, 0x82, 0x83, 0x84, 0x86, 0x8A, 0x8E, Parchment];

    private int _chosen;

    // the darkened frame under the page, made once from the frame the view opened over
    private Screen? _darkened;

    public string Id => SettingsLayout.Screen;

    /// <summary>The row chosen (0 the first).</summary>
    public int Chosen => _chosen;

    public ViewResult Handle(ViewInput input)
    {
        var all = rows();
        if (all.Count == 0)
        {
            return ViewResult.Close;
        }
        _chosen = Math.Clamp(_chosen, 0, all.Count - 1);
        if (input.Key is { } key)
        {
            switch (key)
            {
                case Keys.Escape:
                    return ViewResult.Close;
                case Keys.Up:
                    _chosen = (_chosen + all.Count - 1) % all.Count;
                    break;
                case Keys.Down:
                    _chosen = (_chosen + 1) % all.Count;
                    break;
                case Keys.Left:
                    Change(all[_chosen], -1);
                    break;
                case Keys.Right:
                case Keys.Enter:
                case Keys.Space:
                    Change(all[_chosen], 1);
                    break;
                default:
                    if (key == closeKey)
                    {
                        return ViewResult.Close;
                    }
                    break;
            }
            return ViewResult.Stay;
        }
        var (x, y) = (input.Mouse.X, input.Mouse.Y);
        var list = SettingsLayout.Rows(all.Count);
        var row = list.RowAt(x, y);
        if (input.Click)
        {
            if (SettingsLayout.Close.Contains(x, y))
            {
                return ViewResult.Close;
            }
            if (row >= 0)
            {
                _chosen = row;
                Change(all[row], 1);
            }
        }
        else if (row >= 0 && input.Mouse != _lastPointer)
        {
            // the pointer over a row chooses it (as the card menus light the line under it)
            _chosen = row;
        }
        _lastPointer = input.Mouse;
        return ViewResult.Stay;
    }

    private MouseState _lastPointer;

    private string W(string id) => look.Words(Core.GameText.Of(id));

    private void Change(SettingRow row, int step)
    {
        if (row.Stepped(step) is { } choice)
        {
            carryOut(new SettingChange(row.Key, choice.Value));
        }
    }

    public void Draw(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        Darken(screen);
        foreach (var (index, color) in look.PageColors)
        {
            screen.Palette[index] = color;
        }
        if (!look.PageColors.ContainsKey(Parchment))
        {
            screen.Palette[Parchment] = 0xFFFFE7DBu;
        }
        var canvas = new Canvas(screen);
        var x0 = SettingsLayout.PageX;
        canvas.Fill(x0 + 7, 0, SettingsLayout.PageWidth - 15, Canvas.Height, Parchment);
        if (look.Rope is var (top, left, right, bottom))
        {
            // the card frame's four pictures, where the card screen draws them in its window (CardScreen.DrawFrame)
            canvas.Draw(left, x0, 0, transparent: true);
            canvas.Draw(right, x0 + 0xFC, 0, transparent: true);
            canvas.Draw(top, x0 + 7, 0, transparent: true);
            canvas.Draw(bottom, x0 + 7, 0xC2, transparent: true);
        }
        var title = W(SettingsTexts.Title);
        look.Text.Draw(canvas, x0 + (SettingsLayout.PageWidth - look.Text.Measure(title)) / 2, 9, title, TitleInk);
        var all = rows();
        _chosen = Math.Clamp(_chosen, 0, Math.Max(0, all.Count - 1));
        var list = SettingsLayout.Rows(all.Count);
        if (list.Rectangle is var (lx, ly, lw, _) && list.RowHeight is { } rowHeight)
        {
            for (var i = 0; i < all.Count; i++)
            {
                var row = all[i];
                var y = ly + i * rowHeight;
                var chosen = i == _chosen;
                // the chosen row's label is always in the highlight colour (a fixed row too: its value stays grey, its note says why)
                var ink = chosen ? ChosenInk : row.FixedBy is not null ? FixedInk : Ink;
                var valueInk = row.FixedBy is not null ? FixedInk : chosen ? ChosenInk : ValueInk;
                look.Text.Draw(canvas, lx, y + 1, row.Label, ink, lx + SettingsLayout.ValueColumn - 4);
                var value = row.ValueLabel;
                if (chosen && row.CanChange)
                {
                    value = "< " + value + " >";
                }
                look.Text.Draw(canvas, lx + SettingsLayout.ValueColumn, y + 1, value, valueInk, lx + lw);
            }
        }
        if (all.Count > 0)
        {
            DrawNote(canvas, all[_chosen]);
        }
        if (SettingsLayout.Close.Rectangle is var (cx, cy, cw, ch))
        {
            // a button framed in the rope's browns (the dark outside, the light a pixel in), as the game's boxes are framed
            canvas.Fill(cx, cy, cw, ch, Parchment);
            canvas.Outline(cx, cy, cw - 1, ch - 1, 0x86);
            canvas.Outline(cx + 1, cy + 1, cw - 3, ch - 3, 0x82);
            var close = W(SettingsTexts.Close);
            look.Small.Draw(canvas, cx + (cw - look.Small.Measure(close)) / 2, cy + (ch - look.Small.LineHeight) / 2 + 1, close, Ink);
        }
    }

    // the note on the row chosen: when its change takes effect, or what fixes it; then the keys
    private void DrawNote(Canvas canvas, SettingRow row)
    {
        var note = row.FixedBy is { } by
            ? look.Words(Core.GameText.Of(SettingsTexts.FixedBy, ("by", by)))
            : W(row.Applies switch
            {
                SettingApplies.NewGames => SettingsTexts.AppliesNewGames,
                SettingApplies.NextStart => SettingsTexts.AppliesNextStart,
                _ => SettingsTexts.AppliesAtOnce,
            });
        var layout = SettingsLayout.Note;
        var (x, y, width) = (layout.X ?? 0, layout.Y ?? 0, layout.Width ?? 0);
        var lines = Wrap(note, width).Take(layout.Lines).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            look.Small.Draw(canvas, x, y + i * (look.Small.LineHeight + 1), lines[i], NoteInk, x + width);
        }
        var keys = SettingsLayout.KeysLine;
        look.Small.Draw(canvas, keys.X ?? 0, keys.Y ?? 0, W(SettingsTexts.Keys), NoteInk, (keys.X ?? 0) + (keys.Width ?? 0));
    }

    // words of the small font to lines no wider than width (a word wider than the line is cut by the drawing)
    private IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && look.Small.Measure(longer) > width)
            {
                yield return line;
                line = word;
            }
            else
            {
                line = longer;
            }
        }
        if (line.Length > 0)
        {
            yield return line;
        }
    }

    /// <summary>
    /// The game's frame darkened: every colour the frame uses at a third of its light, the colours the page itself draws with (the
    /// startup colours, the rope's, the parchment) taken by the nearest darkened colour of the others, so the page's colours and the
    /// darkened frame share one palette.
    /// </summary>
    private void Darken(Screen screen)
    {
        if (_darkened is null || !SameFrame(screen))
        {
            _darkened = Darkened(screen);
        }
        _darkened.Pixels.CopyTo(screen.Pixels, 0);
        _darkened.Palette.CopyTo(screen.Palette, 0);
    }

    // the view host copies the same frame under the view each time: the darkened one is kept while it is the same
    private bool SameFrame(Screen frame) => _source is { } source && source.AsSpan().SequenceEqual(frame.Pixels) && _sourcePalette.AsSpan().SequenceEqual(frame.Palette);

    private byte[]? _source;
    private uint[] _sourcePalette = [];

    private Screen Darkened(Screen frame)
    {
        _source = (byte[])frame.Pixels.Clone();
        _sourcePalette = (uint[])frame.Palette.Clone();
        var reserved = new bool[256];
        for (var i = 0; i < 16; i++)
        {
            reserved[i] = true;
        }
        foreach (var i in PageIndices)
        {
            reserved[i] = true;
        }
        var dark = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            dark[i] = Dim(frame.Palette[i]);
        }
        // a reserved colour's pixels take the free colour nearest to its darkened colour
        var map = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            map[i] = (byte)i;
            if (!reserved[i])
            {
                continue;
            }
            var best = -1;
            var bestDistance = long.MaxValue;
            for (var j = 16; j < 256; j++)
            {
                if (reserved[j])
                {
                    continue;
                }
                var distance = Distance(dark[i], dark[j]);
                if (distance < bestDistance)
                {
                    (best, bestDistance) = (j, distance);
                }
            }
            map[i] = (byte)(best < 0 ? 0 : best);
        }
        var result = new Screen();
        for (var p = 0; p < frame.Pixels.Length; p++)
        {
            result.Pixels[p] = map[frame.Pixels[p]];
        }
        // the startup colours as every screen keeps them (the page's inks)
        for (var i = 0; i < 16; i++)
        {
            var c = Palette.StartupColors[i];
            result.Palette[i] = 0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
        }
        for (var i = 16; i < 256; i++)
        {
            result.Palette[i] = dark[i];
        }
        return result;
    }

    private static uint Dim(uint argb) =>
        0xFF000000u | (((argb >> 16) & 0xFF) / 3) << 16 | (((argb >> 8) & 0xFF) / 3) << 8 | ((argb & 0xFF) / 3);

    private static long Distance(uint a, uint b)
    {
        long dr = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF);
        long dg = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF);
        long db = (int)(a & 0xFF) - (int)(b & 0xFF);
        return (dr * dr) + (dg * dg) + (db * db);
    }
}
