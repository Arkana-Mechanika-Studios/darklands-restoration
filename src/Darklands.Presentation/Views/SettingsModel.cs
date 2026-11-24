using System.Globalization;
using System.Text.Json.Nodes;
using Darklands.Platform;

namespace Darklands.Presentation.Views;

/// <summary>What the settings screen needs of the host that runs it: the window, the picture, the packs it can offer, the settings file.</summary>
public interface ISettingsHost
{
    /// <summary>Shows the frame with the picture filter named (display.filter's names).</summary>
    void SetFilter(PictureFilter filter);

    /// <summary>Gives the window a size, or the whole screen.</summary>
    void SetWindow(int width, int height, bool fullscreen);

    /// <summary>The CRT's slight curvature on or off.</summary>
    void SetCurvature(bool on);

    /// <summary>The usable part of the screen the window is on (null: unknown, every size offered).</summary>
    (int Width, int Height)? UsableScreen { get; }

    /// <summary>The pack folders found (language, font and sound packs together) and the SoundFont files found.</summary>
    (IReadOnlyList<string> Packs, IReadOnlyList<string> SoundFonts) Found();

    /// <summary>What kind of pack a folder holds.</summary>
    SettingsPackKind KindOf(string folder);

    /// <summary>Writes the keys into the settings file (keeping its comments), or says why it cannot.</summary>
    void Write(IReadOnlyDictionary<string, JsonNode?> values);
}

/// <summary>The kinds of pack the settings screen offers.</summary>
public enum SettingsPackKind
{
    None,
    Language,
    Fonts,
    Sounds,
}

/// <summary>
/// The settings screen's model (1.2, S-1; design 3.2): the rows as the program runs now, and the changes carried out. A change that
/// applies at once is made at once (the picture, the window, the Interface switch, clean drawing); the rules for new games are set for
/// the next new game (a game being played keeps the rules it started with); what is read once at the start (packs, fonts, sound) waits
/// for the next start. Every change goes to the settings file with the same keys as the launcher's (docs/SETTINGS.md), so the two
/// always agree. A row the command line or an environment variable fixes is shown fixed, as on the launcher.
/// </summary>
// Not in the original: the 1.2 settings screen.
public sealed class SettingsModel
{
    private static readonly double[] WindowScaleFactors = [1, 1.5, 2, 2.5, 3, 4];

    private static readonly IReadOnlyList<(string Value, string? Id, string Label)> SoundCards =
    [
        ("config", SettingsTexts.AsSetUp, ""),
        ("adlib", null, "AdLib"),
        ("sb", null, "Sound Blaster"),
        ("sbpro2", null, "Sound Blaster Pro 2"),
        ("mt32", null, "Roland MT-32"),
        ("cm32l", null, "Roland CM-32L"),
        ("none", SettingsTexts.NoSound, ""),
    ];

    private readonly ISettingsHost _host;
    private readonly GameUi _ui;
    private readonly Action<Core.OriginalBehaviour>? _newGameRules;
    private readonly IReadOnlyDictionary<string, string> _fixed;

    // the values the rows show, by settings-file key (the window as "WxH")
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    // the packs found (read once, when the screen first opens)
    private (List<string> Languages, List<string> Fonts, List<string> Audio, IReadOnlyList<string> SoundFonts)? _found;

    /// <param name="host">The host that carries out the changes.</param>
    /// <param name="ui">The game's screens: their words, the Interface switch, clean drawing.</param>
    /// <param name="newGameRules">Sets the rules the next new game plays with (DarklandsGame.Behaviour; null: no new game here).</param>
    /// <param name="running">The settings the program runs with.</param>
    /// <param name="file">The settings file's own values (what the next start reads).</param>
    /// <param name="fixedKeys">The keys the command line or an environment variable fixes, with what fixes them (CommandLineOptions.Overrides).</param>
    public SettingsModel(ISettingsHost host, GameUi ui, Action<Core.OriginalBehaviour>? newGameRules, ResolvedSettings running, GameSettings file, IReadOnlyDictionary<string, string> fixedKeys)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(file);
        _host = host;
        _ui = ui;
        _newGameRules = newGameRules;
        _fixed = fixedKeys;
        var window = running.Window ?? DisplayLayout.WindowSize(null, null);
        _values["display.filter"] = PictureFilters.Name(running.Filter);
        _values["display.window"] = Size(window);
        _values["display.fullscreen"] = OnOff(running.Fullscreen);
        _values["display.crtCurvature"] = OnOff(running.CrtCurvature);
        _values["interface.mode"] = running.ComfortInterface ? "comfort" : "classic";
        _values["rules.preset"] = running.RulesPreset == "original" ? "original" : "fixed";
        _values["display.cleanDrawing"] = OnOff(running.CleanDrawing);
        // what is read at the start: the file's value (what the next start reads), or the running one where the command line fixes it
        _values["text.languagePack"] = Fixed("text.languagePack") ? running.LanguagePack ?? "" : file.LanguagePack ?? "";
        _values["text.fontPack"] = Fixed("text.fontPack") ? running.FontPack ?? "" : file.FontPack ?? "";
        _values["sound.card"] = Fixed("sound.card") ? (running.NoAudio ? "none" : running.SoundChoice ?? "config")
            : SoundCards.Any(c => c.Value == file.SoundCard) ? file.SoundCard! : "config";
        _values["sound.mt32Roms"] = Fixed("sound.mt32Roms") ? running.Mt32Roms ?? "" : file.Mt32Roms ?? "";
        _values["sound.audioPack"] = Fixed("sound.audioPack") ? running.AudioPack ?? "" : file.AudioPack ?? "";
        _values["sound.soundFont"] = Fixed("sound.soundFont") ? running.SoundFont ?? "" : file.SoundFont ?? "";
    }

    /// <summary>A view of the screen (a host runs it with the game paused); <paramref name="closeKey"/> closes it too (the key that opened it).</summary>
    public SettingsView View(int? closeKey = null) => new(SettingsLook.Of(_ui), Rows, Carry, closeKey);

    private bool Fixed(string key) => _fixed.ContainsKey(key);

    private string W(string id) => _ui.Words(id);

    private static string OnOff(bool on) => on ? "on" : "off";

    private static string Size((int Width, int Height) size) => string.Create(CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}");

    /// <summary>The rows as they are now, in the order of design 3.2.</summary>
    public IReadOnlyList<SettingRow> Rows()
    {
        var found = Found();
        (string, string)[] onOff = [("on", W(SettingsTexts.On)), ("off", W(SettingsTexts.Off))];
        return
        [
            Row("display.filter", SettingsTexts.PictureFilter, PictureFilters.Names.Select(n => (n, W(SettingsTexts.Filter(n)))), SettingApplies.AtOnce),
            Row("display.window", SettingsTexts.Window, Windows().Select(s => (Size(s), string.Create(CultureInfo.InvariantCulture, $"{s.Width} x {s.Height}"))),
                SettingApplies.AtOnce, "display.scale"),
            Row("display.fullscreen", SettingsTexts.Fullscreen, onOff, SettingApplies.AtOnce),
            Row("display.crtCurvature", SettingsTexts.Curvature, onOff, SettingApplies.AtOnce),
            Row("interface.mode", SettingsTexts.Interface, [("classic", W(SettingsTexts.Classic)), ("comfort", W(SettingsTexts.Comfort))], SettingApplies.AtOnce),
            Row("rules.preset", SettingsTexts.Rules, [("original", W(SettingsTexts.Original)), ("fixed", W(SettingsTexts.Fixed))], SettingApplies.NewGames),
            Row("text.languagePack", SettingsTexts.Language, Folders(found.Languages, "text.languagePack", W(SettingsTexts.None)), SettingApplies.NextStart),
            Row("text.fontPack", SettingsTexts.Fonts, Folders(found.Fonts, "text.fontPack", W(SettingsTexts.OriginalFonts)), SettingApplies.NextStart),
            Row("sound.card", SettingsTexts.SoundCard, SoundCards.Select(c => (c.Value, c.Id is { } id ? W(id) : c.Label)), SettingApplies.NextStart),
            Row("sound.mt32Roms", SettingsTexts.Roms, Folders([], "sound.mt32Roms", W(SettingsTexts.Default)), SettingApplies.NextStart),
            Row("sound.audioPack", SettingsTexts.AudioPack, Folders(found.Audio, "sound.audioPack", W(SettingsTexts.None)), SettingApplies.NextStart),
            Row("sound.soundFont", SettingsTexts.SoundFont, Folders(found.SoundFonts, "sound.soundFont", W(SettingsTexts.Default)), SettingApplies.NextStart),
            Row("display.cleanDrawing", SettingsTexts.CleanDrawing, onOff, SettingApplies.AtOnce),
        ];
    }

    private SettingRow Row(string key, string label, IEnumerable<(string Value, string Label)> choices, SettingApplies applies, string? fixedKey = null)
    {
        var list = choices.Select(c => new SettingChoice(c.Value, c.Label)).ToList();
        var current = Math.Max(0, list.FindIndex(c => c.Value == _values[key]));
        var by = _fixed.TryGetValue(fixedKey ?? key, out var b) ? b : null;
        return new SettingRow(key, W(label), list, current, applies, by);
    }

    // none (""), then the folders or files found, then the one chosen when it is elsewhere; shown by their names
    private IEnumerable<(string Value, string Label)> Folders(IReadOnlyList<string> found, string key, string none)
    {
        var values = new List<string> { "" };
        values.AddRange(found.Select(Slashed).Where(v => !values.Contains(v)));
        if (_values[key] is { Length: > 0 } chosen && !values.Contains(Slashed(chosen)))
        {
            values.Add(Slashed(chosen));
        }
        return values.Select(v => (v, v.Length == 0 ? none : Path.GetFileName(Path.TrimEndingDirectorySeparator(v))));
    }

    private static string Slashed(string path) => path.Replace('\\', '/');

    // the window sizes that fit the screen (the game's shape), and the current one
    private List<(int Width, int Height)> Windows()
    {
        var usable = _host.UsableScreen;
        var sizes = WindowScaleFactors
            .Select(f => ((int)(DisplayLayout.BaseWidth * f), (int)(DisplayLayout.BaseHeight * f)))
            .Where(s => usable is not { } d || (s.Item1 <= d.Width && s.Item2 <= d.Height))
            .ToList();
        if (sizes.Count == 0)
        {
            sizes.Add((DisplayLayout.BaseWidth, DisplayLayout.BaseHeight));
        }
        var current = ParseSize(_values["display.window"]);
        if (!sizes.Contains(current))
        {
            sizes.Add(current);
            sizes.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }
        return sizes;
    }

    private static (int Width, int Height) ParseSize(string text)
    {
        var parts = text.Split('x');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private (List<string> Languages, List<string> Fonts, List<string> Audio, IReadOnlyList<string> SoundFonts) Found()
    {
        if (_found is { } found)
        {
            return found;
        }
        var (packs, soundFonts) = _host.Found();
        var kinds = packs.Select(p => (Folder: p, Kind: _host.KindOf(p))).ToList();
        _found = ([.. kinds.Where(k => k.Kind == SettingsPackKind.Language).Select(k => k.Folder)],
            [.. kinds.Where(k => k.Kind == SettingsPackKind.Fonts).Select(k => k.Folder)],
            [.. kinds.Where(k => k.Kind == SettingsPackKind.Sounds).Select(k => k.Folder)],
            soundFonts);
        return _found.Value;
    }

    /// <summary>Carries out a change: at once where it applies at once, then into the settings file. A fixed row changes nothing.</summary>
    public void Carry(SettingChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!_values.ContainsKey(change.Key) || Fixed(change.Key) || (change.Key == "display.window" && Fixed("display.scale")))
        {
            return;
        }
        _values[change.Key] = change.Value;
        var on = change.Value == "on";
        var written = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        switch (change.Key)
        {
            case "display.filter":
                _host.SetFilter(PictureFilters.Parse(change.Value) ?? PictureFilter.Sharp);
                written[change.Key] = change.Value;
                break;
            case "display.window":
            case "display.fullscreen":
                var size = ParseSize(_values["display.window"]);
                _host.SetWindow(size.Width, size.Height, _values["display.fullscreen"] == "on");
                if (change.Key == "display.fullscreen")
                {
                    written[change.Key] = on;
                }
                else if (size.Width % DisplayLayout.BaseWidth == 0 && size.Height == size.Width / DisplayLayout.BaseWidth * DisplayLayout.BaseHeight)
                {
                    written["display.scale"] = size.Width / DisplayLayout.BaseWidth;
                    written["display.resolution"] = "";
                }
                else
                {
                    written["display.resolution"] = change.Value;
                }
                break;
            case "display.crtCurvature":
                _host.SetCurvature(on);
                written[change.Key] = on;
                break;
            case "display.cleanDrawing":
                _ui.Options.CleanDrawing = on;
                written[change.Key] = on;
                break;
            case "interface.mode":
                // the screens that read it pick it up at their next opening (the Game menu's settings line among them)
                _ui.ComfortInterface = change.Value == "comfort";
                written[change.Key] = change.Value;
                break;
            case "rules.preset":
                // the next new game plays with it; a game being played keeps the rules it started with (DarklandsGame.PlayEach, RecordedRules)
                var preset = Core.RulePresets.Parse(change.Value) ?? Core.RulePreset.Fixed;
                _newGameRules?.Invoke(Core.OriginalBehaviour.Of(Core.RulePresets.ForNewGames(preset)));
                written[change.Key] = change.Value;
                break;
            default:
                // read once at the start: written for the next start
                written[change.Key] = change.Value;
                break;
        }
        _host.Write(written);
    }
}
