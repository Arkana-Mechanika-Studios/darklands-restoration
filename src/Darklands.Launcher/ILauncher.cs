using System.Text.Json.Nodes;
using Darklands.Platform;

namespace Darklands.Launcher;

/// <summary>
/// The settings screen shown before the game. A host gives it the settings and a few services, and a surface to draw on;
/// it returns what the player chose and the settings to save. It writes no file: the host saves the changes.
/// Any other launcher (another look, another toolkit) fits in by implementing this.
/// </summary>
public interface ILauncher
{
    LauncherResult Run(LauncherContext context, ILauncherSurface surface);
}

/// <summary>Why the launcher is shown.</summary>
public enum LauncherReason
{
    /// <summary>No settings file existed before this start.</summary>
    FirstRun,

    /// <summary>The game folder is not set or holds no Darklands.</summary>
    GameNotFound,

    /// <summary>The game could not start; <see cref="LauncherContext.Problem"/> says why.</summary>
    StartFailed,

    /// <summary>The player asked for it (--setup, Shift held).</summary>
    Asked,
}

/// <summary>What the launcher is given.</summary>
/// <param name="Settings">The settings file's values.</param>
/// <param name="FixedKeys">Settings-file keys the command line or the environment overrides, with what overrides them: shown, not editable.</param>
/// <param name="Version">The program's version.</param>
/// <param name="Reason">Why the launcher is shown.</param>
/// <param name="Problem">A message for the player (why the game could not start), or null.</param>
/// <param name="CheckGameFolder">A folder the player named → the game folder in it (it or its DARKLAND subfolder), or null.</param>
/// <param name="FoundGameFolders">The game folders found in the usual places, with where each was found.</param>
/// <param name="DescribeRoms">The Roland ROM folder of the settings (null: the default) → what is in the folder it stands for.</param>
/// <param name="SoundTest">Plays a few seconds of the game's music on request; null when the host cannot.</param>
/// <param name="ReleaseNotes">The release notes shipped with the program (Markdown; the first section is shown).</param>
/// <param name="OpenLink">Opens a web link in the player's browser.</param>
/// <param name="ReleaseNotesLink">The release notes of this version on the web, or null.</param>
public sealed record LauncherContext(
    GameSettings Settings,
    IReadOnlyDictionary<string, string> FixedKeys,
    string Version,
    LauncherReason Reason,
    string? Problem,
    Func<string, string?> CheckGameFolder,
    Func<IReadOnlyList<(string Folder, string Source)>> FoundGameFolders,
    Func<string?, RomStatus> DescribeRoms,
    ISoundTest? SoundTest,
    string ReleaseNotes,
    Action<string> OpenLink,
    string? ReleaseNotesLink = null);

/// <summary>
/// The sound test: a little of the game's music on the chosen card. It may start in the background; whenever it starts
/// playing, fails or ends, the host wakes the launcher (an input from <see cref="ILauncherSurface.Wait"/>) to show it.
/// </summary>
public interface ISoundTest
{
    /// <summary>Starts the test (at once <see cref="Playing"/>, while it gets ready too).</summary>
    void Play(SoundTestRequest request);

    void Stop();

    /// <summary>Starting or playing.</summary>
    bool Playing { get; }

    /// <summary>What the test is doing ("Playing ..."), or why it could not play (then not <see cref="Playing"/>); null when stopped.</summary>
    string? Status { get; }
}

/// <summary>What the sound test should play on: the sound card choice ("config", "mt32" ...), the game folder (its drivers) and the ROM folder.</summary>
public sealed record SoundTestRequest(string Card, string GameFolder, string? RomFolder);

/// <summary>The Roland ROMs in a folder (one folder serves both modules: their files side by side, or MT32 and CM32L subfolders).</summary>
/// <param name="Folder">The folder looked in (the default one when none is set).</param>
/// <param name="Mt32">An MT-32's control and PCM ROMs are there.</param>
/// <param name="Cm32l">A CM-32L's control and PCM ROMs are there.</param>
public sealed record RomStatus(string Folder, bool Mt32, bool Cm32l)
{
    public bool Found => Mt32 || Cm32l;
}

public enum LauncherChoice
{
    /// <summary>Start the game with the settings.</summary>
    Play,

    /// <summary>Leave the program (after saving the changes, if any).</summary>
    Quit,
}

/// <summary>What the player chose, the settings to run with, and the settings-file keys that changed (dotted, with their new JSON values).</summary>
public sealed record LauncherResult(LauncherChoice Choice, GameSettings Settings, IReadOnlyDictionary<string, JsonNode?> Changes);

/// <summary>A window the launcher draws on and reads input from.</summary>
public interface ILauncherSurface
{
    /// <summary>The drawing area in pixels.</summary>
    (int Width, int Height) Size { get; }

    /// <summary>The screen's size, for the window sizes worth offering; null when unknown.</summary>
    (int Width, int Height)? DisplaySize { get; }

    /// <summary>Shows a finished frame: <paramref name="pixels"/> holds <see cref="Size"/> ARGB pixels, row by row.</summary>
    void Present(ReadOnlySpan<uint> pixels);

    /// <summary>Waits for input and returns what came (at least one item).</summary>
    IReadOnlyList<LauncherInput> Wait();

    /// <summary>Starts or stops typed-text input (an on-screen keyboard or input method may show).</summary>
    void TextInput(bool on);

    /// <summary>The clipboard's text, or null.</summary>
    string? Clipboard();

    /// <summary>Asks the system for a folder (its own dialog); null when cancelled or not available.</summary>
    Func<string, string?>? PickFolder { get; }
}

/// <summary>An input event, in surface pixels.</summary>
public abstract record LauncherInput;

public sealed record PointerMoved(int X, int Y) : LauncherInput;

public sealed record PointerPressed(int X, int Y) : LauncherInput;

public sealed record KeyPressed(LauncherKey Key, bool Shift = false, bool Control = false) : LauncherInput;

public sealed record TextTyped(string Text) : LauncherInput;

public sealed record PathDropped(string Path) : LauncherInput;

/// <summary>The surface changed size; draw again at the new <see cref="ILauncherSurface.Size"/>.</summary>
public sealed record SurfaceResized : LauncherInput;

/// <summary>The window's close button.</summary>
public sealed record CloseRequested : LauncherInput;

public enum LauncherKey
{
    Up,
    Down,
    Left,
    Right,
    Enter,
    Escape,
    Tab,
    Backspace,
    Delete,
    Home,
    End,
    Paste,
}
