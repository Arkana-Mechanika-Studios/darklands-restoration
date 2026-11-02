namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// What is left of the command line once the known options are taken: the game folder, or an unknown option that the program
/// names and answers with its usage instead of trying it as the game folder (as "--help" once was).
/// </summary>
public sealed class CommandLineUsageTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("--scale")]
    [InlineData("-x")]
    public void AnOptionLeftOverIsUnknownNotTheGameFolder(string leftover) =>
        Assert.Equal(leftover, CommandLineUsage.Unknown([leftover]));

    [Theory]
    [InlineData(@"C:\GOG Games\Darklands")]
    [InlineData("/home/deck/Darklands")]
    [InlineData("DARKLAND")]
    public void AFolderLeftOverIsTheGameFolder(string leftover) =>
        Assert.Null(CommandLineUsage.Unknown([leftover]));

    [Fact]
    public void TheUsageNamesEveryOptionTheProgramReads()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "src", "Darklands.Host.Sdl", "Program.cs"));
        var read = System.Text.RegularExpressions.Regex.Matches(program, "(?:Option|Remove|Contains)\\(\"(--[a-z0-9-]+)\"\\)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(read);
        Assert.All(read, option => Assert.Contains(option, CommandLineUsage.Text));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("Darklands.slnx");
    }
}
