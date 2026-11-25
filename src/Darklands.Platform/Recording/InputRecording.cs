using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Darklands.Platform.Recording;

/// <summary>A saved game's file as the recording keeps it: its name in the saves folder and its time (the load screen's order).</summary>
public sealed record RecordedFile(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("time")] long UtcTicks,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>
/// What a recording says about itself (<c>recording.json</c>): the program, the game files it ran on (by their checksum), the
/// settings that change what the game does (by name: no folder of the player's), the saves folder as it was at the start, the
/// games saved during it (by checksum: a replay must write the same bytes), and whether the tape was cut at its limit.
/// </summary>
public sealed record RecordingHeader
{
    public const int CurrentFormat = 1;

    [JsonPropertyName("format")] public int Format { get; init; } = CurrentFormat;

    [JsonPropertyName("program")] public string Program { get; init; } = "";

    [JsonPropertyName("commit")] public string Commit { get; init; } = "";

    /// <summary>The game's DARKLAND.EXE checksum (the game files a replay must have).</summary>
    [JsonPropertyName("gameExe")] public string GameExe { get; init; } = "";

    /// <summary>The settings that change what the game does, by key (docs/SETTINGS.md names); packs and mods by name only.</summary>
    [JsonPropertyName("settings")] public Dictionary<string, string> Settings { get; init; } = new(StringComparer.Ordinal);

    /// <summary>The saves folder's games at the start (their files are in the recording: saves/name).</summary>
    [JsonPropertyName("saves")] public List<RecordedFile> Saves { get; init; } = [];

    /// <summary>The files the game wrote to the saves folder during the recording, in order (the last of each name counts).</summary>
    [JsonPropertyName("written")] public List<RecordedFile> Written { get; init; } = [];

    /// <summary>
    /// The game in play when the recording was taken (a problem report is made inside a pump), as the checksum of its .SAV bytes;
    /// empty when none was. A replay stops at that pump and compares.
    /// </summary>
    [JsonPropertyName("finalState")] public string FinalState { get; init; } = "";

    /// <summary>The tape's entries.</summary>
    [JsonPropertyName("entries")] public long Entries { get; init; }

    /// <summary>True when the tape reached its limit and the rest of the session is not in it.</summary>
    [JsonPropertyName("cut")] public bool Cut { get; init; }
}

/// <summary>
/// An input recording (R-1): from the program's start, every value the host gave the game, with what is needed to play it again
/// (docs/REPORTING.md): <c>recording.json</c>, <c>tape.bin</c> and <c>saves/</c>, in one zip (<c>recording.dlrec</c>).
/// </summary>
// Not in the original: R-1, the input recording of a problem report.
public sealed class InputRecording
{
    public const string FileName = "recording.dlrec";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public required RecordingHeader Header { get; init; }

    public required byte[] Tape { get; init; }

    /// <summary>The saves folder's files at the start, by name.</summary>
    public required IReadOnlyDictionary<string, byte[]> Saves { get; init; }

    /// <summary>The game in play when the recording was taken, as a .SAV (<c>final.sav</c>; null: none was).</summary>
    public byte[]? FinalState { get; init; }

    /// <summary>The files of a saves folder a game reads (the saved games and their companions; *.SAV, *.BSV, *.json beside them).</summary>
    public static IEnumerable<string> SaveFiles(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f).ToUpperInvariant() is ".SAV" or ".BSV" or ".JSON")
                .Order(StringComparer.OrdinalIgnoreCase)
            : [];

    /// <summary>A saves folder's files as they are now: their entries and their bytes (at most <paramref name="limit"/> bytes in all).</summary>
    public static (List<RecordedFile> Files, Dictionary<string, byte[]> Bytes, bool Cut) Snapshot(string folder, long limit = 6L * 1024 * 1024)
    {
        var files = new List<RecordedFile>();
        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var total = 0L;
        var cut = false;
        // the newest first: past the limit, the oldest games are the ones left out
        foreach (var path in SaveFiles(folder).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                var content = File.ReadAllBytes(path);
                if (total + content.Length > limit)
                {
                    cut = true;
                    continue;
                }
                total += content.Length;
                var name = Path.GetFileName(path);
                files.Add(new RecordedFile(name, File.GetLastWriteTimeUtc(path).Ticks, Checksum(content)));
                bytes[name] = content;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                cut = true;
            }
        }
        return (files, bytes, cut);
    }

    /// <summary>The SHA-256 of some bytes, in hex.</summary>
    public static string Checksum(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Writes the recording to <paramref name="path"/> (a zip).</summary>
    public void Write(string path)
    {
        var temp = path + ".part";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            using (var stream = zip.CreateEntry("recording.json").Open())
            {
                JsonSerializer.Serialize(stream, Header, Json);
            }
            using (var stream = zip.CreateEntry("tape.bin", CompressionLevel.NoCompression).Open())
            {
                stream.Write(Tape);
            }
            if (FinalState is { } final)
            {
                using var stream = zip.CreateEntry("final.sav").Open();
                stream.Write(final);
            }
            foreach (var (name, content) in Saves)
            {
                using var stream = zip.CreateEntry("saves/" + name).Open();
                stream.Write(content);
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads a recording <see cref="Write"/> made.</summary>
    public static InputRecording Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var header = JsonSerializer.Deserialize<RecordingHeader>(Bytes(zip, "recording.json")) ?? throw new InvalidDataException("no recording.json");
        if (header.Format != RecordingHeader.CurrentFormat)
        {
            throw new InvalidDataException($"recording format {header.Format}: this program reads format {RecordingHeader.CurrentFormat}");
        }
        var saves = zip.Entries.Where(e => e.FullName.StartsWith("saves/", StringComparison.Ordinal) && e.Name.Length > 0)
            .ToDictionary(e => e.Name, e => Bytes(zip, e.FullName), StringComparer.OrdinalIgnoreCase);
        return new InputRecording
        {
            Header = header,
            Tape = Bytes(zip, "tape.bin"),
            Saves = saves,
            FinalState = zip.GetEntry("final.sav") is null ? null : Bytes(zip, "final.sav"),
        };
    }

    /// <summary>Puts the saves folder of the start into <paramref name="folder"/> (empty or new), with the files' times.</summary>
    public void RestoreSaves(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in Header.Saves)
        {
            if (Saves.TryGetValue(file.Name, out var content))
            {
                var path = Path.Combine(folder, file.Name);
                File.WriteAllBytes(path, content);
                File.SetLastWriteTimeUtc(path, new DateTime(file.UtcTicks, DateTimeKind.Utc));
            }
        }
    }

    private static byte[] Bytes(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException("no " + name);
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
