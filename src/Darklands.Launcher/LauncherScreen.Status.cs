using Darklands.Launcher.Drawing;
using Darklands.Platform;

namespace Darklands.Launcher;

// The launcher screen: the status lines and help texts of the settings, and the fonts. The state and the input are in LauncherScreen.cs.
internal sealed partial class LauncherScreen
{
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
            return (kind == PackKind.Pictures ? "None: the game's own pictures." : "None: the game's own music and sounds.", PaleCream);
        }
        return PackProblem(kind) is { } problem ? (problem, PaleRed) : (PackSummary(kind, _model.Pack(folder)), Green);
    }

    /// <summary>
    /// A note's detail on one line: whole when it fits, else up to its last ", " that fits (when that keeps at least half the line),
    /// else cut with "…".
    /// </summary>
    private static string ClauseThatFits(TextFace face, string text, int width)
    {
        if (face.Measure(text) <= width)
        {
            return text;
        }
        for (var comma = text.LastIndexOf(", ", StringComparison.Ordinal); comma > 0; comma = text.LastIndexOf(", ", comma - 1, StringComparison.Ordinal))
        {
            var clause = text[..comma];
            if (face.Measure(clause) <= width)
            {
                return face.Measure(clause) * 2 >= width ? clause : Fit(face, text, width, fromEnd: false);
            }
        }
        return Fit(face, text, width, fromEnd: false);
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
        // short enough to fit its line at every window size; the row's tooltip says more (any .sf2, where to get the free one, its licence)
        return ("None found: put any General MIDI .sf2 in " + SoundFontFolder + ".", PaleCream);
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
            (true, true) => ("MT-32 and CM-32L ROMs found (for a Roland sound card).", PaleCream),
            (true, false) => ("MT-32 ROMs found (for a Roland sound card).", PaleCream),
            (false, true) => ("CM-32L ROMs found (for a Roland sound card).", PaleCream),
            _ => ("No ROMs here: needed only for a Roland sound card.", PaleCream),
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
            Item.Picture => "How the 320x200 picture is drawn on a large window; the game plays the same. ←/→ or Enter: the next one (Ctrl+F11 in the game).\n"
                + PictureFilters.Description(PictureFilters.Parse(_model.Filter) ?? PictureFilter.Sharp)
                + "\nThe preview at the left approximates the game window.",
            Item.HdPack => $"Replacement pictures (PNG). ←/→: None or a pack of the packs folder; {browse}.",
            Item.AudioPack => $"Replacement music and sounds (OGG, WAV, MIDI). ←/→: None or a pack of the packs folder; {browse}.",
            Item.SoundFont => _model.SoundFontFound is null && (_model.SoundFont is null || !File.Exists(_model.SoundFont))
                ? $"Without one a pack's .mid music is skipped. Any General MIDI .sf2 works: put it in {SoundFontFolder} next to the program, drop it here, or Browse…. Free: {FreeSoundFont} by Frank Wen (MIT, 140 MB; on Linux fluid-soundfont-gm)."
                : $"The SoundFont (.sf2) for the packs' .mid music. Enter or Browse…: another file; ←/→: the default (the {SoundFontFolder} folder next to the program).",
            Item.PackGuide => "Opens the guide: how to make a picture or sound pack, and the names of the game's pictures, music and sounds.",
            Item.Report => "Makes a zip with the logs, settings and saves for a bug report, and opens its folder and the report form. Nothing is sent: you attach the zip yourself.",
            Item.Sound => "The sound card the game plays its music and sounds on.",
            Item.Roms => "Your MT-32 / CM-32L ROM files (side by side or in MT32, CM32L subfolders). "
                + (_surface.PickFolder is not null ? "Enter or Change…: another folder; or drop it here." : "Drop the folder on this window."),
            Item.Test => _context.SoundTest is { Playing: true } ? "Stops the music." : "Plays some of the game's music on the chosen card, with these ROMs.",
            Item.Banner => "The text page the 1992 game showed for a few seconds while it loaded.",
            Item.Intro => "The animated opening before the start screen.",
            Item.MouseWheel => "In the game's menus and lists the wheel moves the selection, as the arrow keys do; never on the map or in a battle.",
            Item.CardMenuKeys => "At a card menu, ↑/↓ and the wheel work wherever the pointer is (the 1992 game needed it over the lines), and 1–9 choose a line.",
            Item.Save => "Writes the changes to the settings file and closes.",
            Item.Play => "Writes the changes to the settings file and starts the game.",
            Item.Discord => "Opens the project's Discord in your browser.",
            Item.Notes => "Opens the release notes of this version in your browser.",
            Item.TabGeneral => "The game folder, the display, the sound card and the packs. Ctrl+Tab: the other tab.",
            Item.TabAdvanced => "The Roland ROMs, the SoundFont for the packs' MIDI music, the start-up, the mouse wheel and the card menu keys. Ctrl+Tab: the other tab.",
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
}
