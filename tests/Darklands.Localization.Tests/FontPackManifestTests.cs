namespace Darklands.Localization.Tests;

/// <summary>A font-only pack (<c>{"fonts": true}</c>, no language): the game's English in its fonts.</summary>
public sealed class FontPackManifestTests
{
    [Fact]
    public void AFontOnlyPackLoadsWithNoLanguageAndNoTexts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-fontpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, LanguagePack.ManifestName), "{ \"name\": \"HD fonts\", \"fonts\": true }");
            Assert.True(LanguagePack.IsPack(folder));
            Assert.True(LanguagePack.IsFontPack(folder));
            var pack = LanguagePack.Load(folder, out var why);
            Assert.NotNull(pack);
            Assert.Null(why);
            Assert.True(pack.FontsOnly);
            Assert.Equal("en", pack.Language);
            Assert.Empty(pack.Problems);
            Assert.Equal(0, pack.Files);
            // a translation pack is not a font pack, and a manifest with neither is no pack
            File.WriteAllText(Path.Combine(folder, LanguagePack.ManifestName), "{ \"language\": \"fr\", \"fonts\": true }");
            Assert.False(LanguagePack.IsFontPack(folder));
            File.WriteAllText(Path.Combine(folder, LanguagePack.ManifestName), "{ \"name\": \"nothing\" }");
            Assert.False(LanguagePack.IsPack(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
