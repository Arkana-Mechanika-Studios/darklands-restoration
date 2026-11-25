namespace Darklands.Architecture.Tests;

/// <summary>
/// No raw control character in the repository's text files (sources, docs, registers, scripts): an escape written by a tool and
/// turned into its byte ("\b" of "pics\button4.pic" became a backspace in a comment and then in docs/faithfulness/dormant.csv) is
/// invisible in an editor and spreads into the generated registers. Tabs and line ends only; a control code in a string is written
/// as its escape (\u0001).
/// </summary>
public sealed class ControlCharacterTests
{
    private static readonly string[] Extensions =
        [".cs", ".md", ".csv", ".py", ".json", ".txt", ".ftl", ".yml", ".yaml", ".props", ".targets", ".csproj", ".slnx", ".sh", ".xml"];

    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", "local", "node_modules"];

    [Fact]
    public void TextFilesHoldNoRawControlCharacters()
    {
        var root = FindRoot();
        var found = new List<string>();
        var checkedFiles = 0;
        foreach (var top in new[] { "src", "tests", "tools", "docs", "releases", ".github" })
        {
            var folder = Path.Combine(root, top);
            if (!Directory.Exists(folder))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    || relative.Split(Path.DirectorySeparatorChar).Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }
                checkedFiles++;
                var bytes = File.ReadAllBytes(file);
                for (var i = 0; i < bytes.Length; i++)
                {
                    if (bytes[i] < 0x20 && bytes[i] is not (0x09 or 0x0A or 0x0D))
                    {
                        var line = 1 + bytes.AsSpan(0, i).Count((byte)0x0A);
                        found.Add($"{relative}:{line}: byte 0x{bytes[i]:X2}");
                        break;
                    }
                }
            }
        }
        Assert.True(checkedFiles > 1000, $"{checkedFiles} files checked");
        Assert.True(found.Count == 0, "raw control characters (write them as escapes):\n" + string.Join("\n", found));
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Darklands.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Darklands.slnx not found above " + AppContext.BaseDirectory);
    }
}
