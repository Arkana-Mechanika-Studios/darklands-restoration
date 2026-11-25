using System.Security.Cryptography;
using System.Text;
using Darklands.Content.Formats.Text;
using Darklands.Content.Formats.Text.Strings;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The original-text extractor against the player's files (vacuous without them). Assertions are on keys, counts and
/// hashes only; no game text is stored here.
/// </summary>
public sealed class OriginalTextExtractorTests
{
    private static readonly Lazy<StringTable?> Table = new(
        () => GameFolder.Files is { } files ? OriginalTextExtractor.Extract(files) : null);

    [Fact]
    public void ExtractionSucceedsAndCountsAreSane()
    {
        if (Table.Value is not { } table)
        {
            return;
        }
        var counts = table.CountsByPrefix();
        Assert.True(counts["msg"] >= 1000, "msg " + counts["msg"]);
        Assert.True(counts["lst"] >= 400, "lst " + counts["lst"]);
        Assert.True(counts["cty"] >= 500, "cty " + counts["cty"]);
        Assert.True(counts["enm"] >= 100, "enm " + counts["enm"]);
        Assert.True(counts["snt"] >= 130, "snt " + counts["snt"]);
        Assert.True(counts["ban"] >= 10, "ban " + counts["ban"]);
        Assert.True(counts["exe"] >= 1000, "exe " + counts["exe"]);
        Assert.True(table.Entries.Count(e => e.Key.StartsWith("exe:", StringComparison.Ordinal) && e.Origin == StringOrigin.Known) >= 200);
    }

    [Fact]
    public void KeysAreUniqueAndWellFormed()
    {
        if (Table.Value is not { } table)
        {
            return;
        }
        Assert.Equal(table.Count, table.Entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var e in table.Entries)
        {
            Assert.Matches(@"^(msg:[^#]+#\d+|lst:(item|saint|formula)/\d+\.(name|short)|snt:saint/\d+\.desc|alc:\d+\.desc|dsc:\d+\.desc|loc:\d+\.name|map:\d+\.text"
                + @"|cty:\d+\.[a-z-]+|enm:(template|type)/\d+\.(name|image)|ban:\d+|exe:(DS|290E|286B):[0-9A-F]{4})$", e.Key);
        }
    }

    [Fact]
    public void AddingAKeyTwiceIsAnError()
    {
        var table = new StringTable();
        table.Add("x:1", StringOrigin.Known, "a");
        Assert.Throws<ArgumentException>(() => table.Add("x:1", StringOrigin.Scanned, "b"));
    }

    [Fact]
    public void SomeKeysExistAndAreNotEmpty()
    {
        if (Table.Value is not { } table)
        {
            return;
        }
        foreach (var key in new[] { "lst:item/1.name", "lst:saint/0.name", "lst:formula/0.name", "cty:0.full", "enm:type/0.name", "snt:saint/0.desc", "ban:1" })
        {
            var entry = table.Find(key);
            Assert.NotNull(entry);
            Assert.NotEmpty(entry.Text);
            Assert.Equal(entry.Raw.Length, entry.Text.Length);
        }
        Assert.NotNull(table.Entries.FirstOrDefault(e => e.Key.StartsWith("msg:", StringComparison.Ordinal)));
    }

    [Fact]
    public void TwoRunsGiveTheSameTable()
    {
        if (GameFolder.Files is not { } files || Table.Value is not { } first)
        {
            return;
        }
        var second = OriginalTextExtractor.Extract(files);
        Assert.Equal(Fingerprint(first), Fingerprint(second));
        Assert.Equal(first.Count, second.Count);
    }

    [Fact]
    public void EveryMessageBlockOfTheArchiveIsPresent()
    {
        if (GameFolder.Files is not { } files || Table.Value is not { } table)
        {
            return;
        }
        var archive = new MsgFilesArchiveDecoder().Decode(files.Read("MSGFILES"));
        var blocks = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.Name.TrimEnd('\0', ' ');
            name = name.EndsWith(".MSG", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
            foreach (var card in archive.ParseDocument(entry.Name).Cards)
            {
                var found = table.Find($"msg:{name}#{card.Index}");
                Assert.NotNull(found);
                Assert.Equal(card.RawTextBytes, found.Raw);
                blocks++;
            }
        }
        Assert.Equal(blocks, table.Entries.Count(e => e.Key.StartsWith("msg:", StringComparison.Ordinal)));
    }

    [Fact]
    public void FontCoverageReportsGlyphsPerFont()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var coverage = FontCoverage.Read(files);
        Assert.NotEmpty(coverage);
        foreach (var font in coverage)
        {
            Assert.NotEmpty(font.Drawn);
            Assert.All(font.Drawn, c => Assert.InRange(c, font.FirstCode, font.LastCode));
        }
    }

    /// <summary>Writes the whole table to the file named by DARKLANDS_L10N_DUMP (a developer aid; not run otherwise).</summary>
    [Fact]
    public void DumpsTheTableWhenAskedTo()
    {
        var path = Environment.GetEnvironmentVariable("DARKLANDS_L10N_DUMP");
        if (string.IsNullOrWhiteSpace(path) || Table.Value is not { } table)
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            table.WriteTsv(writer);
        }
        var report = new StringBuilder();
        foreach (var (prefix, count) in table.CountsByPrefix())
        {
            report.AppendLine($"{prefix}\t{count}");
        }
        report.AppendLine("exe known\t" + table.Entries.Count(e => e.Key.StartsWith("exe:", StringComparison.Ordinal) && e.Origin == StringOrigin.Known));
        report.AppendLine("exe scanned\t" + table.Entries.Count(e => e.Key.StartsWith("exe:", StringComparison.Ordinal) && e.Origin == StringOrigin.Scanned));
        foreach (var font in FontCoverage.Read(GameFolder.Files!))
        {
            report.AppendLine($"font {font.File}#{font.Font}\trange {font.FirstCode}..{font.LastCode}\tdrawn {font.Drawn.Count}");
        }
        File.WriteAllText(Path.ChangeExtension(path, ".counts.txt"), report.ToString());
    }

    private static string Fingerprint(StringTable table)
    {
        var hash = SHA256.Create();
        foreach (var e in table.Entries)
        {
            var head = Encoding.UTF8.GetBytes(e.Key + "\0" + (int)e.Origin + "\0");
            hash.TransformBlock(head, 0, head.Length, null, 0);
            hash.TransformBlock(e.Raw, 0, e.Raw.Length, null, 0);
        }
        hash.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(hash.Hash!);
    }
}
