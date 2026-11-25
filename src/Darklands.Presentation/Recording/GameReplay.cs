using System.Globalization;
using Darklands.Content;
using Darklands.Platform.Recording;

namespace Darklands.Presentation.Recording;

/// <summary>How a replay ended and what it proved: the tape played to its end (or where it parted), and each game saved during the
/// recording written again with the same bytes, or not.</summary>
public sealed record ReplayResult(bool ReachedEnd, string? Parted, long Entries, long TapeEntries,
    IReadOnlyList<(string Name, bool Same)> Saves, IReadOnlyList<string> Notes, bool? FinalStateSame = null)
{
    /// <summary>Why the recording was not played at all (another build made it), or null.</summary>
    public string? Refused { get; init; }

    /// <summary>
    /// True when the whole tape played, every save the recording made came out with the same bytes, and the game in play at the end
    /// is the same (when the recording says what it was).
    /// </summary>
    public bool Same => Refused is null && ReachedEnd && Parted is null && Saves.All(s => s.Same) && FinalStateSame != false;
}

/// <summary>
/// Plays an input recording again (R-1): the game set up as the recording says (its rules, interface, packs and mods by name, the
/// saves folder as it was), the tape's values handed to it in the same order (<see cref="TapeHost"/>), the settings notes applied
/// where they were made; at the end, each game the recording saved is compared with the one the replay wrote.
/// </summary>
// Not in the original: R-1.
public static class GameReplay
{
    /// <summary>
    /// Replays <paramref name="recording"/> on the game files <paramref name="files"/>, its saves in <paramref name="workFolder"/>
    /// (made empty first if it does not exist). <paramref name="findPack"/> gives the folder of a pack named in the recording
    /// ("language:fr", "font:hd-fonts"), <paramref name="mods"/> loads the mods it names; null: left out (said in the notes).
    /// </summary>
    public static ReplayResult Run(InputRecording recording, GameFiles files, string workFolder, Func<string, string?>? findPack = null,
        Func<IReadOnlyList<string>, Simulation.Mods.ModSet?>? mods = null, Action<DarklandsGame>? setUp = null,
        Func<DarklandsGame, Action<string>>? console = null, bool anyBuild = false)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(files);
        var notes = new List<string>();
        var header = recording.Header;
        // another build's recording is not played (anyBuild: a developer's try, said in the notes)
        if (RecordingBuild.Refusal(header.Program, header.Commit, RecordingBuild.Version, RecordingBuild.Commit) is { } refusal)
        {
            if (!anyBuild)
            {
                return new ReplayResult(false, null, 0, header.Entries, [], notes) { Refused = refusal };
            }
            notes.Add(refusal + " (played anyway)");
        }
        if (header.Cut)
        {
            notes.Add("the recording was cut at its limit: the replay stops where it ends");
        }
        if (header.GameExe.Length > 0 && files.Exists("DARKLAND.EXE") && InputRecording.Checksum(files.Read("DARKLAND.EXE")) != header.GameExe)
        {
            notes.Add("DARKLAND.EXE is not the recording's (another version of the game): the replay may part early");
        }
        var saves = Path.Combine(workFolder, "saves");
        recording.RestoreSaves(saves);
        var host = new TapeHost(new TapeReader(recording.Tape));
        var settings = header.Settings;
        string Setting(string key) => settings.GetValueOrDefault(key) ?? "";
        var game = new DarklandsGame(host, files)
        {
            Seed = host.Seed,
            PartyTicks = host.PartyTicks,
            ShowStartUp = Setting("startup.banner") == "1",
            PlayOpening = Setting("startup.intro") == "1",
            CommandLine = Setting("commandLine").Length == 0 ? [] : Setting("commandLine").Split('\u001f'),
            RecordedRules = Setting("recordedRules") == "1",
            SaveDirectory = saves,
        };
        if (settings.TryGetValue("quickstartCard", out var card))
        {
            game.QuickstartCard = int.Parse(card, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        if (settings.TryGetValue("startWithSave", out var first))
        {
            game.StartWithSave = first;
        }
        foreach (var (key, kind) in new[] { ("pack.language", "language"), ("pack.font", "font") })
        {
            if (settings.TryGetValue(key, out var name) && name.Length > 0)
            {
                if (findPack?.Invoke(kind + ":" + name) is not { } folder)
                {
                    notes.Add($"the {kind} pack \"{name}\" is not here: the replay plays without it and may part early");
                }
                else if (kind == "language")
                {
                    game.Ui.UseLanguagePack(folder, notes.Add);
                }
                else
                {
                    game.Ui.UseFontPack(folder, notes.Add);
                }
            }
        }
        if (settings.TryGetValue("mods", out var modNames) && modNames.Length > 0)
        {
            var names = modNames.Split('\u001f');
            if (mods?.Invoke(names) is { } set)
            {
                game.Mods = set;
            }
            else
            {
                notes.Add($"the mods {string.Join(", ", names)} are not here: the replay plays without them and may part early");
            }
        }
        GameSettingsNote.Apply(game, Setting("game"));
        game.Ui.PressedButtonMilliseconds = int.Parse(Setting("pressedButtonMilliseconds") is { Length: > 0 } p ? p : "0", CultureInfo.InvariantCulture);
        game.Ui.EdgeScrollMilliseconds = int.Parse(Setting("edgeScrollMilliseconds") is { Length: > 0 } edge ? edge : "0", CultureInfo.InvariantCulture);
        if (Setting("settingsLine") == "1")
        {
            // the settings screen is the host's (it ran paused, inside a pump): its changes come as notes; the menu line is there as in the recording
            game.Ui.OpenSettings = () => { };
        }
        var runCommand = console?.Invoke(game);
        host.Note = note =>
        {
            if (note.StartsWith(GameSettingsNote.Prefix, StringComparison.Ordinal))
            {
                GameSettingsNote.Apply(game, note[GameSettingsNote.Prefix.Length..]);
            }
            else if (note.StartsWith(GameRecording.ConsolePrefix, StringComparison.Ordinal) && runCommand is not null)
            {
                runCommand(note[GameRecording.ConsolePrefix.Length..]);
            }
            else
            {
                notes.Add("a note the replay does not carry out: " + note);
            }
        };
        byte[]? final = null;
        host.Ended = () => final = State(game);
        setUp?.Invoke(game);
        string? parted = null;
        var reachedEnd = false;
        bool? finalSame = null;
        try
        {
            game.Run();
            // the program ended (Alt-Q, the window closed): as recorded only when the tape ends there too
            reachedEnd = host.Position >= header.Entries;
            if (!reachedEnd)
            {
                parted = $"entry {host.Position}: the game ended here, the recording goes on to entry {header.Entries}";
            }
        }
        catch (TapeEndedException)
        {
            reachedEnd = true;
            if (header.FinalState.Length > 0)
            {
                if (final is not null)
                {
                    File.WriteAllBytes(Path.Combine(workFolder, "final-replay.sav"), final);
                }
                finalSame = final is not null && InputRecording.Checksum(final) == header.FinalState;
            }
        }
        catch (TapeMismatchException mismatch)
        {
            parted = mismatch.Message;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            parted = $"entry {host.Position}: the game stopped with {e.GetType().Name}: {e.Message}";
        }
        var compared = new List<(string, bool)>();
        foreach (var written in header.Written.GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()))
        {
            var path = Path.Combine(saves, written.Name);
            var same = File.Exists(path) && InputRecording.Checksum(File.ReadAllBytes(path)) == written.Sha256;
            compared.Add((written.Name, same));
        }
        return new ReplayResult(reachedEnd, parted, host.Position, header.Entries, compared, notes, finalSame);
    }

    /// <summary>The game in play as a .SAV (the in-battle bit set in a battle, as a save made there); null when none is.</summary>
    public static byte[]? State(DarklandsGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Playing is not { } playing)
        {
            return null;
        }
        var (sav, _) = Persistence.OriginalSaves.Write(playing, "Darklands", game.Options.Music, game.Options.SoundEffects, savedCardOf: game.SavedCardOf);
        if (game.Fighting)
        {
            Persistence.OriginalSaves.MarkWrittenInBattle(sav);
        }
        return sav;
    }
}
