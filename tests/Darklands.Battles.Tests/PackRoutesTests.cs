using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Localization;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>
/// The screens' texts a translation pack reaches since the untranslated-text audit (1.2): the formulae's short names, the saved games'
/// places, the camp's details, the character generator's word tables, the map's messages and their box, the fixed columns' boxes, the
/// menus' ticks and the store's message. Without a pack each is the original's.
/// </summary>
public sealed class PackRoutesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dl-packroutes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private void Pack(GameUi ui, params (string Key, string Words)[] texts)
    {
        Directory.CreateDirectory(Path.Combine(_folder, "text", "de"));
        File.WriteAllText(Path.Combine(_folder, "pack.json"), """{ "language": "de" }""");
        File.WriteAllText(Path.Combine(_folder, "text", "de", "test.ftl"),
            string.Concat(texts.Select(t => KeyMangling.ToFluentId(t.Key) + " = " + t.Words + "\n")));
        Assert.True(ui.UseLanguagePack(_folder, _ => { }));
    }

    [Fact]
    public void APackNamesTheShortFormulaeTheSavedPlacesAndTheCampsDetails()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var words = ui.Assets.Words;
        var place = WorldData.Load(files).CreateLocations()[0];
        var job = new Darklands.Core.Rules.CampDetail(Darklands.Core.Rules.CampDetailKind.Job, 2);
        var potions = new Darklands.Core.Rules.CampDetail(Darklands.Core.Rules.CampDetailKind.Potions, 3);
        var fee = new Darklands.Core.Rules.CampDetail(Darklands.Core.Rules.CampDetailKind.Fee, 12);
        // no pack: the words given, the saved name, the original's details
        Assert.Equal("Short", ui.FormulaShortName(2, "Short"));
        Assert.Equal(place.Name, ui.SavedPlaceName(place.Name));
        Assert.Equal(words.Detail(job), ui.CampDetail(job));
        Assert.Equal(words.Detail(potions), ui.CampDetail(potions));
        Assert.Equal(words.Detail(fee), ui.CampDetail(fee));

        Pack(ui, (DataNames.FormulaShort(2), "Kurz"), (DataNames.Location(place.Index), "Ort"),
            (words.Key(OriginalWords.JobNames, 2), "Arbeit"));
        Assert.Equal("Kurz", ui.FormulaShortName(2, "Short"));
        Assert.Equal("Ort", ui.SavedPlaceName(place.Name));
        // a name the party gave (no place of DARKLAND.LOC): as saved
        Assert.Equal("Elsewhere", ui.SavedPlaceName("Elsewhere"));
        Assert.Equal("Arbeit", ui.CampDetail(job));
        Assert.Equal(words.Detail(fee), ui.CampDetail(fee));
    }

    [Fact]
    public void TheCharacterGeneratorsWordTablesAreTheCreationData()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var backgrounds = Enumerable.Range(0, 6).Select(i => ui.Word(OriginalWords.BackgroundNames, i)).ToList();
        var creation = Darklands.Content.Formats.Characters.CreationDataDecoder.Decode(files.Read("DARKLAND.EXE")).Tables;
        var occupations = Enumerable.Range(0, Darklands.Core.Characters.Creation.CharacterCreation.OccupationCount)
            .Select(i => ui.Word(OriginalWords.OccupationNames, i)).ToList();
        // the word tables are the creation data's names, every entry (the generator's lists draw them by index)
        Assert.Equal(creation.Backgrounds.Select(b => b.Name), backgrounds);
        Assert.Equal(creation.Occupations.Select(o => o.Name), occupations);
        // each entry has a key of the template
        var table = OriginalTextExtractor.Extract(files);
        Assert.All(Enumerable.Range(0, occupations.Count), i => Assert.True(table.Contains(ui.Assets.Words.Key(OriginalWords.OccupationNames, i)), i.ToString()));
        Assert.All(Enumerable.Range(0, 6), i => Assert.True(table.Contains(ui.Assets.Words.Key(OriginalWords.BackgroundNames, i)), i.ToString()));

        Pack(ui, (ui.Assets.Words.Key(OriginalWords.OccupationNames, 0), "Beruf"));
        Assert.Equal("Beruf", ui.Word(OriginalWords.OccupationNames, 0));
        Assert.Equal(occupations[1], ui.Word(OriginalWords.OccupationNames, 1));
    }

    [Fact]
    public void TheMapsMessagesAreKeysWithTheirBox()
    {
        if (GameFolder.Files is not { } files || !files.Exists("DARKLAND.MSG"))
        {
            return;
        }
        var table = OriginalTextExtractor.Extract(files);
        var keys = table.Entries.Where(e => e.Key.StartsWith("map:", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(keys);
        Assert.Contains(keys, e => e.Key == DataNames.MapMessage(0));
        Assert.All(keys, e => Assert.NotNull(DataNames.What(e.Key)));
        var box = Assert.Single(DescriptionBoxes.Of(DataNames.MapMessage(0)));
        Assert.Same(DescriptionBoxes.MapMessage, box);
        var painter = new TextPainter(new GameAssets(files).Font(box.Font));
        // the font's own line height, leading 0: the lines of the box (100, 50, 180 x 115) from row 55 to its frame
        Assert.Equal(painter.LineHeight, box.Step(painter));
        Assert.True(box.Lines(painter) >= 8);
        // every message fits its box in the English
        var layout = new CardTextLayout(painter);
        Assert.All(keys, e => Assert.True(layout.MeasureHeight(e.Raw, box.Width, box.Leading) <= box.Foot - box.Y + 1, e.Key));
    }

    [Fact]
    public void TheFormulaeShortNamesHaveTheBatchColumnsBox()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        var formulae = WorldData.Load(files).Items.Formulae;
        var boxes = ColumnBoxes.For(assets);
        Assert.Equal(formulae.Count, boxes.Count);
        Assert.Equal(61, ColumnBoxes.FormulaShortRoom);
        Assert.All(boxes, b => Assert.Equal(61, b.Room));
        Assert.All(boxes, b => Assert.True(b.English <= b.Room, b.Key));
        Assert.Equal(formulae.Select(f => DataNames.FormulaShort(f.Index)), boxes.Select(b => b.Key));
        // a box the shipped boxes have wins
        var own = boxes[0] with { Room = 50 };
        var merged = ColumnBoxes.With([own], assets);
        Assert.Equal(boxes.Count, merged.Count);
        Assert.Equal(50, merged.Single(b => b.Key == own.Key).Room);
    }

    [Theory]
    [InlineData("\u0001Sound", "Sound")]
    [InlineData("\u0002Sound", "Sound")]
    [InlineData("Ton", "Ton")]
    [InlineData("", "")]
    public void AMenuItemsWordsLoseOnlyTheirTick(string words, string untick)
    {
        Assert.Equal(untick, MenuBar.Untick(words));
    }

    [Fact]
    public void TheStoresNoRoomMessageIsAKey()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        Assert.Equal(Darklands.Core.Rules.Storing.NoRoomMessage, ui.Words(GameText.Of("id:rw.store.no-room")));
        Pack(ui, ("id:rw.store.no-room", "Kein Platz."));
        Assert.Equal("Kein Platz.", ui.Words(GameText.Of("id:rw.store.no-room")));
    }

    [Fact]
    public void TheBattleMenusSuffixesAreTheExesStrings()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var words = new GameAssets(files).Words;
        Assert.Equal(Darklands.Core.Battles.Tactics.BattleMenus.Leader, words.Exe("exe:DS:073E"));
        Assert.Equal(Darklands.Core.Battles.Tactics.BattleMenus.All, words.Exe("exe:DS:0744"));
        var table = OriginalTextExtractor.Extract(files);
        foreach (var key in new[] { "exe:DS:073E", "exe:DS:0744", "exe:DS:587D", "exe:DS:5887", "exe:DS:5915", "exe:DS:5A8D", "exe:DS:5E47", "exe:DS:5E4D" })
        {
            Assert.True(table.Contains(key), key);
        }
    }
}
