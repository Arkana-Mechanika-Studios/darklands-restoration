namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// DarklandsRP.exe opens no console window of its own (a windowed program); its messages keep going where its output is
/// redirected, and --log-window opens a window for them only when there is neither a redirect nor a terminal.
/// </summary>
public sealed class ConsoleWindowTests
{
    [Fact]
    public void TheGameProgramIsAWindowedProgram()
    {
        var project = File.ReadAllText(Path.Combine(Root(), "src", "Darklands.Host.Sdl", "Darklands.Host.Sdl.csproj"));
        Assert.Contains("<OutputType>WinExe</OutputType>", project);
    }

    [Fact]
    public void ARedirectedOutputIsKeptAndNoWindowOpens()
    {
        // the test host's output is a pipe to the test runner: the program would write there, as tools that read it expect
        var before = Console.Out;
        var where = ConsoleWindow.Connect(ownWindow: true);
        Assert.Equal(OperatingSystem.IsWindows() ? "redirected" : "terminal", where);
        Assert.Same(before, Console.Out);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Darklands.slnx not found above the test output");
    }
}
