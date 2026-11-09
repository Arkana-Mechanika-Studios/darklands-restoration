namespace Darklands.Host.Sdl.Tests;

/// <summary>
/// Without SDL 3 the program says so in plain words before any window is tried (MissingSdl): how to get it on each system, and on
/// Windows that SDL3.dll comes with the zip.
/// </summary>
public sealed class MissingSdlTests
{
    [Fact]
    public void OnLinuxTheMessageSaysHowToInstallSdl3OrUseSteam()
    {
        var text = MissingSdl.Message(windows: false);
        Assert.Contains("libSDL3.so.0", text);
        Assert.Contains("sudo apt install libsdl3-0", text);
        Assert.Contains("sudo dnf install SDL3", text);
        Assert.Contains("sudo pacman -S sdl3", text);
        Assert.Contains("Ubuntu 24.04 and Debian 12", text);
        Assert.Contains("non-Steam game", text);
    }

    [Fact]
    public void OnWindowsTheMessageSaysSdl3DllComesWithTheZip()
    {
        var text = MissingSdl.Message(windows: true);
        Assert.Contains("SDL3.dll", text);
        Assert.Contains("unpack the whole zip again", text);
    }

    /// <summary>Where the tests run, the SDL3 library is found (beside the tests, or the system's on the Linux build machine).</summary>
    [Fact]
    public void TheLibraryBesideTheProgramOrTheSystemsIsFound()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "SDL3.dll");
        SdlNative.ConfigureLibrary(File.Exists(beside) ? beside : null);
        Assert.True(SdlNative.CanLoad());
    }
}
