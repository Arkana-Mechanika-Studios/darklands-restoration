using Darklands.Platform;

namespace Darklands.Launcher.Tests;

/// <summary>
/// A language pack whose letters the original fonts lack, with no font of its own (LauncherContext.NeedsFonts): choosing it turns
/// the HD fonts on, and a line says why; Fonts set back to None by the player stays None, the line then warning about the '?'.
/// With DARKLANDS_LAUNCHER_SHOTS set, the Advanced tab with the line is saved there.
/// </summary>
public sealed class LauncherFontsNoteTests : IDisposable
{
    private const string Game = @"D:\GOG Games\Darklands";

    private readonly string _root = Directory.CreateTempSubdirectory("dkfontsnote").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine([_root, .. parts])).FullName;

    private LauncherContext Context(string ru, string fr, string hd) => new(
        new GameSettings { GameFolder = Game },
        new Dictionary<string, string>(),
        "1.1.0",
        LauncherReason.Asked,
        null,
        folder => folder.TrimEnd('\\', '/').EndsWith("Darklands", StringComparison.OrdinalIgnoreCase) ? folder : null,
        () => [(Game, "GOG's registration")],
        folder => new RomStatus(folder ?? "MT32_ROMS", true, false),
        null,
        "## 1.1\n- A note.",
        _ => { })
    {
        FindPacks = _ => [hd, fr, ru],
        DescribePack = folder => folder == hd ? new PackStatus(0, 0, null, 0, 3) : new PackStatus(0, 0, null, 1, 0),
        NeedsFonts = (folder, game) => folder == ru && game == Game ? "Russian" : null,
    };

    [Fact]
    public void ALanguageNeedingLettersTurnsTheHdFontsOnAndSaysWhy()
    {
        var (ru, fr, hd) = (Folder("packs", "languages", "ru"), Folder("packs", "languages", "fr"), Folder("packs", "fonts", "hd-fonts"));
        var model = new LauncherModel(Context(ru, fr, hd), null);
        Assert.Null(model.FontsNote);
        // a language the original fonts can write: nothing changes
        model.SetPack(PackKind.Language, fr);
        Assert.Null(model.FontPack);
        Assert.Null(model.FontsNote);
        // Russian: the HD fonts on, said
        model.SetPack(PackKind.Language, ru);
        Assert.Equal(hd, model.FontPack);
        Assert.Equal(("Russian needs letters the original fonts don't have: HD fonts are on.", false), model.FontsNote);
        // the player sets Fonts back to None: kept, and the line warns
        model.SetPack(PackKind.Fonts, null);
        Assert.Null(model.FontPack);
        Assert.Equal(("Russian needs letters the original fonts don't have: with Fonts on None they show as '?'. Choose HD fonts.", true), model.FontsNote);
        // choosing Russian again while Fonts is None by choice turns them on again (a new choice of language)
        model.SetPack(PackKind.Language, fr);
        model.SetPack(PackKind.Language, ru);
        Assert.Equal(hd, model.FontPack);
        // a font pack the player chose: no line
        model.SetPack(PackKind.Fonts, hd);
        Assert.Null(model.FontsNote);
    }

    [Fact]
    public void TheLauncherSavesTheHdFontsAndShowsTheLine()
    {
        var (ru, fr, hd) = (Folder("packs", "languages", "ru"), Folder("packs", "languages", "fr"), Folder("packs", "fonts", "hd-fonts"));
        // the Advanced tab (Ctrl+Tab: the ROM folder), the SoundFont, the Language pack: None, then fr, then ru
        var surface = new ScriptedSurface(1280, 720, [new KeyPressed(LauncherKey.Tab, Control: true), new KeyPressed(LauncherKey.Tab), new KeyPressed(LauncherKey.Tab),
            new KeyPressed(LauncherKey.Right), new KeyPressed(LauncherKey.Right), new PointerMoved(300, 600)]) { Frameless = true };
        var result = new Launcher().Run(Context(ru, fr, hd), surface);
        Assert.Equal(ru, result.Settings.LanguagePack);
        Assert.Equal(hd, result.Settings.FontPack);
        Assert.Equal(hd.Replace('\\', '/'), (string)result.Changes["text.fontPack"]!);
        if (Environment.GetEnvironmentVariable("DARKLANDS_LAUNCHER_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            Darklands.Rendering.PngCodec.Write(new Darklands.Rendering.RgbaImage(1280, 720, surface.Frames[^1]), Path.Combine(shots, "fonts-note-1280x720.png"));
        }
    }
}
