using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The screens' words with a translation pack (L-7): a key the pack has gives the pack's words (a format string's arguments as $arg0 ..,
/// a word table's entries by their keys), every other key the original's; without a pack nothing changes.
/// </summary>
public sealed class LanguagePackWordsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dl-langpack-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void APackTranslatesTheKeysItHasAndLeavesTheOthers()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var leave = ui.Words("exe:DS:2FF2");
        var purse = ui.Format("exe:DS:2F71", "Ritter", 3, 4, 5, 6L);
        var month = ui.Word(OriginalWords.MonthNames, 0);
        var other = ui.Words("exe:DS:2FB8");
        Assert.Equal("January", month);

        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "pack.json"), """{ "language": "de" }""");
        File.WriteAllText(Path.Combine(_folder, "test.ftl"),
            "exe-ds-2ff2 = Verlassen\nexe-ds-2f71 = { $arg0 } hat { $arg1 } Gulden\nexe-290e-0000 = Januar\nid-rw-save-damaged-1 = Beschädigt.\n");
        var reports = new List<string>();
        Assert.True(ui.UseLanguagePack(_folder, reports.Add));
        Assert.Contains(reports, r => r.Contains("(de), 1 file(s)", StringComparison.Ordinal));

        Assert.Equal("Verlassen", ui.Words("exe:DS:2FF2"));
        Assert.Equal("Ritter hat 3 Gulden", ui.Format("exe:DS:2F71", "Ritter", 3, 4, 5, 6L));
        Assert.Equal("Januar", ui.Word(OriginalWords.MonthNames, 0));
        Assert.Equal("Beschädigt.", ui.Words(GameText.Of("id:rw.save.damaged.1")));
        // a key the pack lacks keeps the original's words; a literal stays as written
        Assert.Equal(other, ui.Words("exe:DS:2FB8"));
        Assert.Equal("February", ui.Word(OriginalWords.MonthNames, 1));
        Assert.Equal("a literal", ui.Words("a literal"));

        ui.Language = null;
        Assert.Equal(leave, ui.Words("exe:DS:2FF2"));
        Assert.Equal(purse, ui.Format("exe:DS:2F71", "Ritter", 3, 4, 5, 6L));
    }

    [Fact]
    public void APackThatCannotBeReadLeavesTheOriginalText()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var reports = new List<string>();
        Assert.False(ui.UseLanguagePack(_folder, reports.Add));
        Assert.Null(ui.Language);
        Assert.Contains(reports, r => r.Contains("the original text is shown", StringComparison.Ordinal));
    }
}
