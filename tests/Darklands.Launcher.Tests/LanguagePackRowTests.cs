namespace Darklands.Launcher.Tests;

/// <summary>The launcher tells a translation pack by its manifest's language (L-7): its .ftl files under text/&lt;language&gt; are counted.</summary>
public sealed class LanguagePackRowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dl-launcher-lang-" + Guid.NewGuid().ToString("N"));

    public LanguagePackRowTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AFolderWithALanguageManifestHoldsItsFluentFiles()
    {
        File.WriteAllText(Path.Combine(_folder, "a.ftl"), "x = y\n");
        Assert.Equal(0, PackStatus.Count(_folder).Translations);
        File.WriteAllText(Path.Combine(_folder, "pack.json"), """{ "assets": {} }""");
        Assert.Equal(0, PackStatus.Count(_folder).Translations);
        File.WriteAllText(Path.Combine(_folder, "pack.json"), """{ "language": "de" }""");
        Assert.Equal(0, PackStatus.Count(_folder).Translations);   // a.ftl is outside text/de
        Directory.CreateDirectory(Path.Combine(_folder, "text", "de", "msg"));
        File.WriteAllText(Path.Combine(_folder, "text", "de", "exe.ftl"), "x = y\n");
        File.WriteAllText(Path.Combine(_folder, "text", "de", "msg", "CITYG04.ftl"), "x = y\n");
        Assert.Equal(2, PackStatus.Count(_folder).Translations);
    }
}
