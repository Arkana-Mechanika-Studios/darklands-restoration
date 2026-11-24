using System.Text.Json.Nodes;
using Darklands.Platform;
using Darklands.Presentation.Views;

namespace Darklands.Host.Sdl;

/// <summary>
/// The settings screen's host (1.2, S-1): carries out the model's changes on the window (the picture, its size, the full screen), finds
/// the packs as the launcher does, and writes the settings file as the launcher does (docs/SETTINGS.md).
/// </summary>
// Not in the original: the 1.2 settings screen.
internal sealed class SettingsScreen(SdlHost host, string? settingsPath, string gameFolder, Action<string> log) : ISettingsHost
{
    /// <summary>The key that opens and closes it, as the Game menu shows it: Alt-T, in the original's way (its Alt glyph, 0x0F, and the letter).</summary>
    internal const string KeyLabel = "\x0fT";

    public void SetFilter(PictureFilter filter) => host.Filter = filter;

    public void SetWindow(int width, int height, bool fullscreen) => host.Resize(width, height, fullscreen);

    public void SetCurvature(bool on) => host.Curvature = on;

    public (int Width, int Height)? UsableScreen => host.UsableScreen();

    public (IReadOnlyList<string> Packs, IReadOnlyList<string> SoundFonts) Found()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, Darklands.Audio.Packs.SoundFontLocator.FolderName);
        IReadOnlyList<string> soundFonts = Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*.sf2").Order(StringComparer.OrdinalIgnoreCase)] : [];
        return (LauncherHost.PackFolders(gameFolder), soundFonts);
    }

    public SettingsPackKind KindOf(string folder)
    {
        try
        {
            return Darklands.Localization.LanguagePack.IsFontPack(folder) ? SettingsPackKind.Fonts
                : Darklands.Localization.LanguagePack.IsPack(folder) ? SettingsPackKind.Language
                : Darklands.Audio.Packs.AudioPack.Load(folder).Count > 0 ? SettingsPackKind.Sounds
                : SettingsPackKind.None;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Settings: {folder} cannot be read ({e.Message})");
            return SettingsPackKind.None;
        }
    }

    public void Write(IReadOnlyDictionary<string, JsonNode?> values)
    {
        if (settingsPath is null)
        {
            log("Settings: no settings file could be written; the change lasts until the program ends.");
            return;
        }
        if (!File.Exists(settingsPath) && !SettingsFile.TryWriteTemplate(settingsPath, out var made))
        {
            log("Settings: " + made);
            return;
        }
        var saved = SettingsFileEdits.TrySet(settingsPath, values, out var message);
        log("Settings: " + message + (saved ? "" : " (the change lasts until the program ends)"));
    }
}
