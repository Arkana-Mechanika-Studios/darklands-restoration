using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Localization;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The data files' descriptions in the translator's template (1.1.3): every saint's, formula's and city's description in
/// <c>tables.ftl</c> with its box and its screens; every English description fits its boxes; the check finds one that does not.
/// </summary>
public sealed class DescriptionTemplateTests
{
    [Fact]
    public void TheTemplateHasEveryDescriptionInTablesWithItsBox()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var english = OriginalTextExtractor.Extract(files).Entries;
        var template = TranslatorTemplate.Build(files, ShippedBoxes.Default).ToDictionary(m => m.Key, StringComparer.Ordinal);
        foreach (var prefix in new[] { "snt:saint/", "alc:", "dsc:" })
        {
            var keys = english.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal) && e.Key.EndsWith(".desc", StringComparison.Ordinal))
                .Select(e => e.Key).ToList();
            Assert.True(keys.Count > 10, $"{prefix}: {keys.Count} descriptions");
            foreach (var key in keys)
            {
                Assert.True(template.TryGetValue(key, out var m), key + " is not in the template");
                Assert.Equal("tables.ftl", m!.File);
                Assert.Contains(" · box: ", m.Comments[0], StringComparison.Ordinal);
                Assert.NotEmpty(m.Screens);
            }
        }
        var saint = template["snt:saint/1.desc"];
        Assert.Equal(["SaintPrayerScreen"], saint.Screens);
        Assert.Contains("lines of 156 px (font 2) on the prayer screen", saint.Comments[0], StringComparison.Ordinal);
        var formula = template["alc:1.desc"];
        Assert.Equal(["AlchemyScreen"], formula.Screens);
        Assert.Contains("lines of 95 px (font 1) on the formula page", formula.Comments[0], StringComparison.Ordinal);
        Assert.Contains("lines of 105 px (font 1) in the alchemy screen's book", formula.Comments[0], StringComparison.Ordinal);
        // a city's description: the cards whose texts show $PlaceDesc
        var city = template.Values.First(m => DescriptionBoxes.IsCityDescription(m.Key));
        Assert.All(city.Screens, s => Assert.StartsWith("card ", s, StringComparison.Ordinal));
        Assert.Contains("the card text's that shows it", city.Comments[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEnglishDescriptionFitsItsBoxes()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        var checkedBoxes = 0;
        foreach (var e in OriginalTextExtractor.Extract(files).Entries)
        {
            foreach (var box in DescriptionBoxes.Of(e.Key))
            {
                var bottom = DescriptionBoxes.Bottom(box, new TextPainter(assets.Font(box.Font)), Darklands.Core.TextCodes.OfBytes(e.Raw));
                Assert.True(bottom < box.Foot, $"{e.Key} reaches row {bottom}, {box.Where} the foot is {box.Foot}");
                checkedBoxes++;
            }
        }
        Assert.True(checkedBoxes > 100, $"{checkedBoxes} boxes");
    }

    [Fact]
    public void TheCheckFindsADescriptionTooLongForItsBox()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var template = TranslatorTemplate.Build(files, []);
        var assets = new GameAssets(files);
        var folder = Path.Combine(Path.GetTempPath(), "dl-desc-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            var saint = KeyMangling.ToFluentId(DataNames.SaintDescription(1));
            var formula = KeyMangling.ToFluentId(DataNames.FormulaDescription(1));
            var fits = KeyMangling.ToFluentId(DataNames.SaintDescription(2));
            var city = KeyMangling.ToFluentId(DataNames.CityDescription(3));
            File.WriteAllText(Path.Combine(folder, "text", "de", "tables.ftl"), string.Join("\n",
                saint + " =" + string.Concat(Enumerable.Repeat(" a long sentence that goes on", 40)),
                formula + " =" + string.Concat(Enumerable.Repeat(" a long sentence", 12)),
                fits + " = A short one.",
                city + " = Garçon",
                ""));
            var pack = LanguagePack.Load(folder, out _)!;
            var findings = LanguagePackCheck.Run(pack, assets, template, []);
            string Of(string id) => string.Join(" | ", findings.Where(f => f.Id == id).Select(f => f.ToString()));
            Assert.Contains("runs past the foot of its box on the prayer screen", Of(saint), StringComparison.Ordinal);
            Assert.Contains("runs past the foot of its box on the formula page", Of(formula), StringComparison.Ordinal);
            Assert.Equal("", Of(fits));
            // a city's description is drawn in the card window's font
            Assert.Contains("the font has no 'ç'", Of(city), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
