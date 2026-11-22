using System.Diagnostics;
using System.Xml.Linq;

namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// The release notes (docs/RC-CHECKLIST.md, A0): one fixed place from the repository's top, releases/&lt;version&gt;/release-notes.md,
/// the same on every checkout and in CI; a release build (DarklandsRelease=true, the build workflow on a v* tag) stops without
/// them, with a message that says where they go; an ordinary build goes on.
/// </summary>
public sealed class ReleaseNotesTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void TheNotesAreAtOneFixedPlaceAndTheReleaseWorkflowAsksForThem()
    {
        var props = XDocument.Load(Path.Combine(Root(), "Directory.Build.props"));
        Assert.Equal(@"$(MSBuildThisFileDirectory)releases\$(Version)\release-notes.md", props.Descendants("ReleaseNotesFile").Single().Value);
        var workflow = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "build.yml"));
        Assert.Contains("-p:DarklandsRelease=${{ startsWith(github.ref, 'refs/tags/v') }}", workflow, StringComparison.Ordinal);
    }

    private static (int Exit, string Output) Check(bool release)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = Root() };
        foreach (var arg in new[]
        {
            "msbuild", Path.Combine("src", "Darklands.Host.Sdl", "Darklands.Host.Sdl.csproj"), "-t:RequireReleaseNotes", "-nologo",
            "-p:Version=0.0.0-no-notes", $"-p:DarklandsRelease={(release ? "true" : "false")}",
        })
        {
            start.ArgumentList.Add(arg);
        }
        using var run = Process.Start(start)!;
        var output = run.StandardOutput.ReadToEndAsync();
        var error = run.StandardError.ReadToEndAsync();
        Assert.True(run.WaitForExit(120_000));
        return (run.ExitCode, output.Result + error.Result);
    }

    [Fact]
    public void AReleaseBuildWithoutItsNotesStopsAndAnOrdinaryOneGoesOn()
    {
        var (exit, output) = Check(release: true);
        Assert.NotEqual(0, exit);
        Assert.Contains("Release 0.0.0-no-notes has no release notes", output, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("releases", "0.0.0-no-notes", "release-notes.md"), output, StringComparison.Ordinal);
        var (ordinary, said) = Check(release: false);
        Assert.True(ordinary == 0, said);
    }
}
