using Darklands.Localization;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>The translators' editor's model (W-0 + E-text): a pack edited, saved and opened again, read by the game unchanged.</summary>
public sealed class TextProjectTests
{
    private static T InTemp<T>(Func<string, T> run)
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-workshop-" + Guid.NewGuid().ToString("N"));
        try
        {
            return run(folder);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void APackEditedAndSavedLoadsInTheGameAndOpensAgainUnchanged()
    {
        if (GameFolder.Files is not { } files || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        InTemp(folder =>
        {
            var project = TextProject.Create(folder, "de", "Test", "a translator", game);
            Assert.All(project.Entries, e => Assert.Equal(TextState.Untranslated, e.State));
            var exe = project.Entries.First(e => e.Key == "exe:DS:2F71");
            var card = project.Entries.First(e => e.Key.StartsWith("msg:", StringComparison.Ordinal) && e.English.Contains("<opt>", StringComparison.Ordinal));
            var menu = project.Entries.First(e => e.Key == "exe:290E:0112");
            project.Set(exe, "{ $arg0 } verwaltet die Börse: { $arg1 } Gulden.");
            project.Set(card, card.English.Replace("...", "…", StringComparison.Ordinal));
            project.Set(menu, "<c12> M</c>ehr Chargen");
            Assert.True(project.Dirty);
            project.Save();
            Assert.False(project.Dirty);
            Assert.Equal((3, project.Entries.Count), project.Progress());

            // the game reads the pack
            var pack = LanguagePack.Load(folder, out var why);
            Assert.True(pack is not null, why);
            Assert.Empty(pack!.Problems);
            Assert.Equal("Ritter verwaltet die Börse: 5 Gulden.", pack.Localizer.Get("exe:DS:2F71", new Dictionary<string, object?> { ["arg0"] = "Ritter", ["arg1"] = "5" }));
            Assert.True(pack.Localizer.IsTranslated(card.Key));

            // and the editor opens it again with the same words
            var again = TextProject.Open(folder, game);
            Assert.Equal(exe.Translation, again.Find(exe.Id)!.Translation);
            Assert.Equal(card.Translation, again.Find(card.Id)!.Translation);
            Assert.Equal(menu.Translation, again.Find(menu.Id)!.Translation);
            Assert.Equal(3, again.Entries.Count(e => e.Translation is not null));

            // the check: the menu's hot key kept, so no problem
            again.Check();
            Assert.Equal(TextState.Translated, again.Find(menu.Id)!.State);
            again.Set(again.Find(menu.Id)!, "<c12> W</c>eitere Chargen");
            again.Save();
            again.Check();
            Assert.Equal(TextState.Problem, again.Find(menu.Id)!.State);
            return 0;
        });
    }

    [Fact]
    public void APacksOwnMessagesAreKeptAndTextsAreGroupedByScreen()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        InTemp(folder =>
        {
            var project = TextProject.Create(folder, "de", "Test", null, game);
            File.WriteAllText(Path.Combine(project.TextFolder, "variables.ftl"),
                "var-him1 =\n    { $sex ->\n        [female] ihr\n       *[male] ihm\n    }\n");
            File.WriteAllText(Path.Combine(folder, "boxes.json"), """{ "base": "483.07", "boxes": [ { "key": "exe:DS:2F71", "english": 90, "room": 200, "windowed": false, "font": 1, "args": null, "screens": [ "BarterScreen" ] } ] }""");
            var opened = TextProject.Open(folder, game);
            Assert.Single(opened.Others);
            opened.Set(opened.Entries[0], "x");
            opened.Save();
            var pack = LanguagePack.Load(folder, out _)!;
            Assert.Equal("ihm", pack.Variable("him1", female: false));
            var groups = opened.ByScreen();
            Assert.Contains(groups, g => g.Screen == "BarterScreen" && g.Entries.Any(e => e.Key == "exe:DS:2F71"));
            Assert.Equal(TextProject.NotSeen, groups[^1].Screen);
            Assert.Contains(groups, g => g.Screen.StartsWith("card $", StringComparison.Ordinal));
            return 0;
        });
    }

    [Fact]
    public void EveryTextSplitsIntoChipsAndBack()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        foreach (var m in Darklands.Presentation.TranslatorTemplate.Build(files, []))
        {
            var tokens = TextTokens.Split(m.Value);
            Assert.Equal(m.Value, TextTokens.Join(tokens));
        }
        var parts = TextTokens.Split("Ask $ChosenOneName<br><opt>{ $arg0 } and {\" \"}<c12> M</c>ore");
        Assert.Equal(
        [
            TokenKind.Words, TokenKind.Name, TokenKind.Tag, TokenKind.Tag, TokenKind.Placeable, TokenKind.Words, TokenKind.Placeable, TokenKind.Tag,
            TokenKind.Words, TokenKind.Tag, TokenKind.Words,
        ], parts.Select(p => p.Kind));
    }

    [Fact]
    public void AChoiceIsReadAsFieldsAndWrittenAsFluent()
    {
        var value = FluentChoice.Write("sex", [new("female", "ihr", false), new("male", "ihm", true)]);
        var read = FluentChoice.Read(value);
        Assert.NotNull(read);
        Assert.Equal("sex", read!.Value.Selector);
        Assert.Equal([new("female", "ihr", false), new("male", "ihm", true)], read.Value.Variants);
        var folder = Path.Combine(Path.GetTempPath(), "dl-choice-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            File.WriteAllText(Path.Combine(folder, "text", "de", "a.ftl"), FtlSource.Write("var-him1", value) + "\n");
            var pack = LanguagePack.Load(folder, out _)!;
            Assert.Empty(pack.Problems);
            Assert.Equal("ihr", pack.Variable("him1", female: true));
            Assert.Equal(value, Assert.Single(pack.Messages).Message.Value);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
        Assert.Null(FluentChoice.Read("plain words"));
    }
}
