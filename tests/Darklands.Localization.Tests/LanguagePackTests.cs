namespace Darklands.Localization.Tests;

/// <summary>A translation pack: its manifest names the language, its .ftl files are read, a broken file is reported, not fatal.</summary>
public sealed class LanguagePackTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dl-langpack-" + Guid.NewGuid().ToString("N"));

    public LanguagePackTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void APackGivesItsWordsAndNamesItsLanguage()
    {
        Write("pack.json", """{ "language": "de", "name": "Deutsch (Test)", "author": "tests", }""");
        Write("game/exe.ftl", "exe-ds-2ff2 = Verlassen\nexe-ds-2f71 = { $arg0 } verwaltet die Börse von { $arg1 }fl\n");
        Write("ui.ftl", "id-rw-notice-press-a-key = Eine Taste drücken.\n");
        var pack = LanguagePack.Load(_folder, out var why);
        Assert.Null(why);
        Assert.NotNull(pack);
        Assert.Equal("de", pack.Language);
        Assert.Equal("Deutsch (Test)", pack.Name);
        Assert.Equal(2, pack.Files);
        Assert.Empty(pack.Problems);
        Assert.True(pack.Localizer.IsTranslated("exe:DS:2FF2"));
        Assert.False(pack.Localizer.IsTranslated("exe:DS:2FF3"));
        Assert.Equal("Verlassen", pack.Localizer.Get("exe:DS:2FF2"));
        Assert.Equal("Ritter verwaltet die Börse von 3fl", pack.Localizer.Get("exe:DS:2F71", ("arg0", "Ritter"), ("arg1", 3)));
        Assert.Equal("Eine Taste drücken.", pack.Localizer.Get("id:rw.notice.press-a-key"));
        Assert.True(LanguagePack.IsPack(_folder));
    }

    [Fact]
    public void ABrokenFileIsReportedWithItsLineAndTheRestKept()
    {
        Write("pack.json", """{ "language": "fr" }""");
        Write("a.ftl", "exe-ds-2ff2 = Partir\nthis is not fluent\n");
        var pack = LanguagePack.Load(_folder, out _)!;
        Assert.Equal("Partir", pack.Localizer.Get("exe:DS:2FF2"));
        Assert.Contains(pack.Problems, p => p.Source == "a.ftl" && p.Line == 2);
    }

    [Fact]
    public void AFolderWithoutALanguageIsNoTranslationPack()
    {
        Assert.Null(LanguagePack.Load(_folder, out var none));
        Assert.Contains("no pack.json", none);
        Write("pack.json", """{ "assets": { "PICS\\STARTSCR.PIC": "title.png" } }""");   // an HD pack's manifest
        Assert.False(LanguagePack.IsPack(_folder));
        Assert.Null(LanguagePack.Load(_folder, out var why));
        Assert.Contains("not a translation pack", why);
        Assert.Null(LanguagePack.Load(Path.Combine(_folder, "missing"), out _));
    }
}
