namespace Darklands.Launcher.Tests;

/// <summary>The launcher tells a translation pack by its manifest's language (L-7): its .ftl files are counted, other folders hold none.</summary>
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
        Directory.CreateDirectory(Path.Combine(_folder, "game"));
        File.WriteAllText(Path.Combine(_folder, "game", "exe.ftl"), "x = y\n");
        Assert.Equal(2, PackStatus.Count(_folder).Translations);
    }
}
