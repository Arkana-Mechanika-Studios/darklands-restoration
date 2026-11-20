using Darklands.Content.Formats.Text;
using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Rendering.Tests;

/// <summary>A pack from another edition's card texts (L-7g): the English edition taken as the other gives every block back.</summary>
public sealed class EditionPackTests
{
    [Fact]
    public void TheSameEditionGivesEveryBlockBack()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-edition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = EditionPack.Write(files, files, "en", "the same edition", folder);
            Assert.Empty(result.LeftOut);
            Assert.True(result.Written > 3000, $"{result.Written} blocks");
            var pack = LanguagePack.Load(folder, out var why);
            Assert.True(pack is not null, why);
            Assert.Empty(pack!.Problems);
            Assert.Equal(result.Written, pack.Messages.Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ATextsOwnAngleBracketsAreTagsInTheWrittenForm()
    {
        var written = CardTextForm.ToWritten("a <b> c"u8);
        Assert.Equal("a <lt>b<gt> c", written);
        Assert.Equal("a <b> c"u8.ToArray(), CardTextForm.ToBytes(written, out _));
        Assert.Null(CardTextForm.Check(written, "a <b> c"u8));
    }
}
