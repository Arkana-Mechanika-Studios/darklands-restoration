using Darklands.Content.Formats.Text;
using Darklands.Localization;

namespace Darklands.Rendering.Tests;

/// <summary>A pack's own card-text variables (docs/LOCALISATION.md): chosen by $sex, usable in its card texts beside the English $Names.</summary>
public sealed class PackVariableTests
{
    [Fact]
    public void APacksVariableIsChosenBySexAndAllowedInItsCardTexts()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-vars-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            File.WriteAllText(Path.Combine(folder, "text", "de", "ui.ftl"), string.Join("\n",
                "var-him1 =", "    { $sex ->", "        [female] ihr", "       *[male] ihm", "    }",
                "var-Him1 =", "    { $sex ->", "        [female] Ihr", "       *[male] Ihm", "    }", ""));
            var pack = LanguagePack.Load(folder, out var why)!;
            Assert.True(pack is not null, why);
            Assert.Empty(pack.Problems);
            Assert.Equal(["Him1", "him1"], pack.Variables.Order(StringComparer.Ordinal));
            Assert.Equal("ihr", pack.Variable("him1", female: true));
            Assert.Equal("ihm", pack.Variable("him1", female: false));
            Assert.Equal("Ihm", pack.Variable("Him1", female: false));
            Assert.Null(pack.Variable("him2", female: false));

            byte[] english = [.. "Give $ChosenOneName $his coin"u8];
            Assert.Null(CardTextForm.Check("Gebt $ChosenOneName $him1 Münze", english, pack.Variables));
            Assert.Null(CardTextForm.Check("Gebt $him1 Münze", english, pack.Variables));
            Assert.Contains("$him2", CardTextForm.Check("Gebt $him2 Münze", english, pack.Variables));
            // without a pack's variables: the original's names, the same set
            Assert.Contains("$Names", CardTextForm.Check("Gebt $ChosenOneName $him1 Münze", english));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
