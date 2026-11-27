using System.Globalization;
using Darklands.Platform;
using Darklands.Platform.Recording;

namespace Darklands.Presentation.Recording;

/// <summary>
/// The input recording of a session (R-1, docs/REPORTING.md): from the program's start, every value the host gives the game, the
/// seeds the game takes from the clock, the settings changed while it runs (the settings screen), the saves folder as it was at
/// the start, and the checksums of the games it saves; a problem report packs it (<see cref="InputRecording"/>), and
/// <see cref="GameReplay"/> plays it again to the same saved games. Inputs only: nothing personal, no folder of the player's.
/// </summary>
// Not in the original: R-1. The game is unchanged: the recording host hands it exactly what the inner host gives.
public sealed class GameRecording : IDisposable
{
    private readonly TapeWriter _tape = new();
    private List<RecordedFile> _saves = [];
    private Dictionary<string, byte[]> _saveBytes = new(StringComparer.OrdinalIgnoreCase);
    private bool _savesCut;
    private readonly List<RecordedFile> _written = [];
    private readonly Lock _gate = new();
    private DarklandsGame? _game;
    private string _settingsNow = "";

    /// <summary>Starts a recording over <paramref name="inner"/>; <paramref name="savesFolder"/>: the saves folder as it is now (else <see cref="TakeSaves"/>).</summary>
    public GameRecording(IHost inner, string? savesFolder = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Host = new RecordingHost(inner, _tape) { AfterPump = NoteSettings };
        if (savesFolder is not null)
        {
            TakeSaves(savesFolder);
        }
    }

    /// <summary>Takes the saves folder's games as they are now (before the game runs): a replay starts from them.</summary>
    public void TakeSaves(string savesFolder)
    {
        ArgumentNullException.ThrowIfNull(savesFolder);
        lock (_gate)
        {
            (_saves, _saveBytes, _savesCut) = InputRecording.Snapshot(savesFolder);
        }
    }

    /// <summary>The host to give the game (it records what the inner host gives).</summary>
    public RecordingHost Host { get; }

    /// <summary>The tape's size so far, compressed (bytes, about).</summary>
    public long TapeBytes => _tape.CompressedBytes;

    /// <summary>The tape's entries so far.</summary>
    public long Entries => _tape.Entries;

    /// <summary>The recording in a report's words: its size, and whether it was cut (the tape at its limit, saves left out).</summary>
    public string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"since the program started: {_tape.Entries} entries, {Math.Max(1, _tape.CompressedBytes / 1024)} KB")
        + (_tape.Full ? "; cut at its limit (the end of the session is not in it)" : "")
        + (_savesCut ? "; the oldest saved games left out (the saves folder is over the limit)" : "");

    /// <summary>
    /// Follows <paramref name="game"/> (built over <see cref="Host"/>): its seeds and its tick words of the day go on the tape, its
    /// saves are noted by checksum, and its settings (those that change what it does) are taken now and after every pump.
    /// </summary>
    public void Attach(DarklandsGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        var seed = game.Seed;
        game.Seed = () =>
        {
            var value = seed();
            _tape.Value(TapeKind.Seed, value);
            return value;
        };
        var ticks = game.PartyTicks;
        game.PartyTicks = () =>
        {
            var value = ticks();
            _tape.Value(TapeKind.PartyTicks, value);
            return value;
        };
        var used = game.SaveUsed;
        game.SaveUsed = (folder, name, loading) =>
        {
            used?.Invoke(folder, name, loading);
            if (!loading)
            {
                Saved(folder, name);
            }
        };
        _settingsNow = GameSettingsNote.Of(game);
        // what the game's setup decides (the host adds its packs and mods by name)
        StartSettings["game"] = _settingsNow;
        StartSettings["startup.banner"] = game.ShowStartUp ? "1" : "0";
        StartSettings["startup.intro"] = game.PlayOpening ? "1" : "0";
        StartSettings["commandLine"] = string.Join("\u001f", game.CommandLine);
        StartSettings["recordedRules"] = game.RecordedRules ? "1" : "0";
        StartSettings["settingsLine"] = game.Ui.OpenSettings is null ? "0" : "1";
        StartSettings["pressedButtonMilliseconds"] = game.Ui.PressedButtonMilliseconds.ToString(CultureInfo.InvariantCulture);
        StartSettings["edgeScrollMilliseconds"] = game.Ui.EdgeScrollMilliseconds.ToString(CultureInfo.InvariantCulture);
        StartSettings["keyScrollMilliseconds"] = game.Ui.KeyScrollMilliseconds.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The settings that change what the game does at the start (by name; packs and mods by their folders' names).</summary>
    public Dictionary<string, string> StartSettings { get; } = new(StringComparer.Ordinal);

    /// <summary>The recording as it is now (the session goes on); this build's version and commit (<see cref="RecordingBuild"/>).</summary>
    public InputRecording Snapshot(string gameExeChecksum)
    {
        var (program, commit) = (RecordingBuild.Version, RecordingBuild.Commit);
        lock (_gate)
        {
            var tape = _tape.Snapshot();
            var final = _game is { } playing ? GameReplay.State(playing) : null;
            return new InputRecording
            {
                Header = new RecordingHeader
                {
                    Program = program,
                    Commit = commit,
                    GameExe = gameExeChecksum,
                    Settings = new Dictionary<string, string>(StartSettings, StringComparer.Ordinal),
                    Saves = [.. _saves],
                    Written = [.. _written],
                    Entries = _tape.Entries,
                    FinalState = final is null ? "" : InputRecording.Checksum(final),
                    Cut = _tape.Full || _savesCut,
                },
                Tape = tape,
                Saves = new Dictionary<string, byte[]>(_saveBytes, StringComparer.OrdinalIgnoreCase),
                FinalState = final,
            };
        }
    }

    /// <summary>Notes a developer console command (run inside the pump: <see cref="GameReplay"/> runs it there too).</summary>
    public void ConsoleCommand(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _tape.Note(ConsolePrefix + line);
    }

    /// <summary>A console command's note begins so.</summary>
    public const string ConsolePrefix = "console ";

    public void Dispose() => _tape.Dispose();

    // after each pump: a setting the game plays by changed (the settings screen runs inside the pump, the game paused)
    private void NoteSettings()
    {
        if (_game is null)
        {
            return;
        }
        var now = GameSettingsNote.Of(_game);
        if (now != _settingsNow)
        {
            _settingsNow = now;
            _tape.Note(GameSettingsNote.Prefix + now);
        }
    }

    private void Saved(string folder, string name)
    {
        lock (_gate)
        {
            foreach (var file in InputRecording.SaveFiles(folder))
            {
                var fileName = Path.GetFileName(file);
                var stem = Path.GetFileNameWithoutExtension(name);
                if (!fileName.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    var bytes = File.ReadAllBytes(file);
                    _written.Add(new RecordedFile(fileName.ToUpperInvariant(), File.GetLastWriteTimeUtc(file).Ticks, InputRecording.Checksum(bytes)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}

/// <summary>
/// The settings a game plays by that the settings screen can change while it runs, as one line: the Interface switch's two flags,
/// clean drawing, the music and sound options (saved with a game; the console can set them) and the rules new games get. A recording notes the line when it changes; a replay sets it at the same place.
/// </summary>
// Not in the original: R-1.
public static class GameSettingsNote
{
    public const string Prefix = "settings ";

    /// <summary>The line of <paramref name="game"/> now.</summary>
    public static string Of(DarklandsGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var flags = string.Join(",", game.Behaviour.Differences().Select(d => d.Id + "=" + (d.On ? "1" : "0")));
        return string.Create(CultureInfo.InvariantCulture,
            $"comfort={(game.Ui.ComfortInterface ? 1 : 0)};cardkeys={(game.Ui.KeyboardCardMenus ? 1 : 0)};clean={(game.Options.CleanDrawing ? 1 : 0)};music={(game.Options.Music ? 1 : 0)};sound={(game.Options.SoundEffects ? 1 : 0)};rules={Core.RulePresets.Name(game.Behaviour.Preset)};flags={flags}");
    }

    /// <summary>Sets <paramref name="game"/> to a line <see cref="Of"/> made.</summary>
    public static void Apply(DarklandsGame game, string line)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(line);
        var values = line.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        game.Ui.ComfortInterface = values.GetValueOrDefault("comfort") == "1";
        game.Ui.KeyboardCardMenus = values.GetValueOrDefault("cardkeys") == "1";
        game.Options.CleanDrawing = values.GetValueOrDefault("clean") == "1";
        if (values.TryGetValue("music", out var music))
        {
            game.Options.Music = music == "1";
        }
        if (values.TryGetValue("sound", out var sound))
        {
            game.Options.SoundEffects = sound == "1";
        }
        var rules = Core.OriginalBehaviour.Of(Core.RulePresets.Parse(values.GetValueOrDefault("rules")) ?? Core.RulePreset.Faithful);
        foreach (var flag in (values.GetValueOrDefault("flags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var (id, on) = (flag[..flag.IndexOf('=', StringComparison.Ordinal)], flag.EndsWith("=1", StringComparison.Ordinal));
            rules.Set(id, on);
        }
        game.Behaviour = rules;
    }
}
