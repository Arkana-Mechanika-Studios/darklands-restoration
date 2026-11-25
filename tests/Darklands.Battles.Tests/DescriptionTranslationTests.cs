using Darklands.Content;
using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The data files' descriptions with a translation pack (1.1.3): a saint's (DARKLAND.SNT) and a formula's (DARKLAND.ALC) as the
/// prayer and alchemy screens ask for them; the pack's words for a key it has, the original's for the others and without a pack.
/// </summary>
public sealed class DescriptionTranslationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dl-langpack-desc-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheDescriptionsHaveKeysOfTheirOwn()
    {
        Assert.Equal("snt:saint/7.desc", DataNames.SaintDescription(7));
        Assert.Equal("alc:12.desc", DataNames.FormulaDescription(12));
        Assert.Equal("dsc:3.desc", DataNames.CityDescription(3));
        Assert.Contains("saint's description", DataNames.What("snt:saint/7.desc"), StringComparison.Ordinal);
        Assert.Contains("formula's description", DataNames.What("alc:12.desc"), StringComparison.Ordinal);
        Assert.Contains("$PlaceDesc", DataNames.What("dsc:3.desc"), StringComparison.Ordinal);
        // a saint's or a city's short name or other field is not a description
        Assert.Null(DataNames.What("snt:saint/7.name"));
        Assert.Null(DataNames.What("dsc:3.name"));
        Assert.Equal([DescriptionBoxes.Saint], DescriptionBoxes.Of("snt:saint/7.desc"));
        Assert.Equal([DescriptionBoxes.FormulaPage, DescriptionBoxes.FormulaBook], DescriptionBoxes.Of("alc:12.desc"));
        Assert.Empty(DescriptionBoxes.Of("dsc:3.desc"));
        Assert.True(DescriptionBoxes.IsCityDescription("dsc:3.desc"));
    }

    [Fact]
    public void APackTranslatesTheSaintsAndTheFormulasDescriptions()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var saint = ui.Assets.SaintDescription(1);
        Assert.NotEqual("", saint);
        // no pack: the original's, as given
        Assert.Equal(saint, ui.SaintDescription(1, saint));
        Assert.Equal("the formula's own", ui.FormulaDescription(2, "the formula's own"));

        Directory.CreateDirectory(Path.Combine(_folder, "text", "zh"));
        File.WriteAllText(Path.Combine(_folder, "pack.json"), """{ "language": "zh" }""");
        File.WriteAllText(Path.Combine(_folder, "text", "zh", "tables.ftl"),
            KeyMangling.ToFluentId(DataNames.SaintDescription(1)) + " = 圣人的故事\n" +
            KeyMangling.ToFluentId(DataNames.FormulaDescription(2)) + " = 一种药水\n");
        Assert.True(ui.UseLanguagePack(_folder, _ => { }));

        Assert.Equal("圣人的故事", ui.SaintDescription(1, saint));
        Assert.Equal("一种药水", ui.FormulaDescription(2, "the formula's own"));
        // a key the pack lacks keeps the description given
        Assert.Equal(ui.Assets.SaintDescription(2), ui.SaintDescription(2, ui.Assets.SaintDescription(2)));
        Assert.Equal("the formula's own", ui.FormulaDescription(3, "the formula's own"));
    }
}
