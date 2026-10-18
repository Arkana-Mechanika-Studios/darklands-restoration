using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Darklands.Core.Tests;

/// <summary>
/// No tracked file (text, gzip-compressed fixture or file name) may carry an identifier of the third-party
/// decompilation the traces were made with: the code cites original coordinates only (docs/TRACEABILITY.md).
/// The forbidden identifiers are machine-local, like the mapping itself: local/forbidden-identifiers.txt
/// (git-ignored), one entry per line, <c>re:</c> before a regular expression, otherwise a case-insensitive
/// substring; <c>#</c> starts a comment. Without that file the test passes and says so.
/// </summary>
public sealed class NoThirdPartyNamesTests
{
    [Fact]
    public void NoTrackedFileCarriesAForbiddenIdentifier()
    {
        var root = FindRepositoryRoot();
        var list = Path.Combine(root, "local", "forbidden-identifiers.txt");
        if (!File.Exists(list))
        {
            Console.WriteLine("SKIPPED: local/forbidden-identifiers.txt is absent (the machine-local list of forbidden identifiers)");
            return;
        }
        var patterns = File.ReadLines(list)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.StartsWith("re:", StringComparison.Ordinal)
                ? new Regex(l[3..], RegexOptions.CultureInvariant)
                : new Regex(Regex.Escape(l), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        Assert.True(patterns.Count > 0, "local/forbidden-identifiers.txt lists nothing");
        var problems = new List<string>();
        foreach (var relative in TrackedFiles(root))
        {
            var shown = relative.Replace(Path.DirectorySeparatorChar, '/');
            if (patterns.Any(p => p.IsMatch(shown)))
            {
                problems.Add($"{shown}: the file name");
            }
            var path = Path.Combine(root, relative);
            if (!File.Exists(path))
            {
                continue;
            }
            var bytes = File.ReadAllBytes(path);
            if (relative.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                using var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var plain = new MemoryStream();
                gz.CopyTo(plain);
                bytes = plain.ToArray();
            }
            else if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
            {
                continue; // binary
            }
            var number = 0;
            foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n'))
            {
                number++;
                if (patterns.Any(p => p.IsMatch(line)))
                {
                    problems.Add($"{shown}:{number}");
                }
            }
        }
        Assert.True(problems.Count == 0, "tracked files carry identifiers of the reference decompilation (cite coordinates):"
            + Environment.NewLine + string.Join(Environment.NewLine, problems.Take(200))
            + (problems.Count > 200 ? $"{Environment.NewLine}... {problems.Count - 200} more" : ""));
    }

    private static List<string> TrackedFiles(string root)
    {
        ProcessStartInfo info = new("git", "-c core.quotepath=off ls-files")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("cannot start git");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "git ls-files failed: " + error);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
