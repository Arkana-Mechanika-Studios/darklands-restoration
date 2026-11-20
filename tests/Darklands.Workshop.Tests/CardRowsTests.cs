using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Presentation;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// A card text as rows (the Workshop's Rows view): the intro, the passages and each option apart, the rows joined give the written
/// text back exactly, and a text written through the fields (the line breaks as lines) passes the game's own check, for every card
/// text of the player's game.
/// </summary>
public sealed class CardRowsTests
{
    [Fact]
    public void ATextIsCutIntoItsIntroPassagesAndOptions()
    {
        var written = "You come to the church. <br><p>You decide to ...<br><p><p><opt>...<indent>attend Mass.<br><p><opt-saints>...<indent>seek sanctuary.";
        var rows = CardRows.Parse(written);
        Assert.Equal([CardRowKind.Intro, CardRowKind.Option, CardRowKind.Option], rows.Select(r => r.Kind));
        Assert.Equal("You come to the church. \n\nYou decide to ...", rows[0].Field);
        Assert.Equal("<br><p><p>", rows[0].Suffix);
        Assert.Equal("<opt>...<indent>", rows[1].Prefix);
        Assert.Equal("attend Mass.", rows[1].Field);
        Assert.Equal("<br><p>", rows[1].Suffix);
        Assert.Equal("saints", rows[2].Label);
        Assert.Equal(written, CardRows.Write(rows));
        // a translation through the fields: the fixed parts are the English's
        var translated = CardRows.Write(rows.Select((r, i) => r.WithField(new[] { "Vous arrivez à l'église.\n\nVous décidez de ...", "assister à la messe.", "chercher refuge." }[i])));
        Assert.Equal("Vous arrivez à l'église.<br><p>Vous décidez de ...<br><p><p><opt>...<indent>assister à la messe.<br><p><opt-saints>...<indent>chercher refuge.", translated);
        // a passage in the intro is a row of its own
        var passage = CardRows.Parse("The army won.<br><p><if-1>n addition, peace.<endif>");
        Assert.Equal([CardRowKind.Intro, CardRowKind.Passage], passage.Select(r => r.Kind));
        Assert.Equal("passage 1", passage[1].Label);
        Assert.Equal("n addition, peace.", passage[1].Content);
        // an untranslated text: the English's rows without words; another structure: no rows (the Raw view)
        Assert.All(CardRows.Against(rows, null)!, r => Assert.Equal("", r.Content));
        Assert.Null(CardRows.Against(rows, "Only one line."));
    }

    [Fact]
    public void EveryCardTextOfTheGameGoesThroughTheRowsAndBack()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        var texts = 0;
        var rowsShownAsWritten = 0;
        var options = 0;
        foreach (var m in TranslatorTemplate.Build(files, []).Where(m => m.Key.StartsWith("msg:", StringComparison.Ordinal)))
        {
            texts++;
            var rows = CardRows.Parse(m.Value);
            Assert.True(CardRows.Write(rows) == m.Value, $"{m.Key}: the rows do not give the text back");
            Assert.Equal(m.Value.Split("<opt", StringSplitOptions.None).Length - 1, rows.Count(r => r.Kind == CardRowKind.Option));
            options += rows.Count(r => r.Kind == CardRowKind.Option);
            rowsShownAsWritten += rows.Count(r => r.Field is null);
            // written again through the fields, as a translator would leave it: the game's check takes it
            var through = CardRows.Write(rows.Select(r => r.Field is { } f ? r.WithField(f) : r));
            var hash = m.Key.IndexOf('#', StringComparison.Ordinal);
            var block = assets.Messages(m.Key[4..hash]).Blocks[int.Parse(m.Key.AsSpan(hash + 1), System.Globalization.CultureInfo.InvariantCulture)];
            Assert.True(CardTextForm.Check(through, block.Text, new HashSet<string>()) is null, $"{m.Key}: {CardTextForm.Check(through, block.Text, new HashSet<string>())}");
            Assert.NotNull(CardRows.Against(rows, m.Value));
        }
        Assert.True(texts > 3000, $"{texts} card texts");
        Assert.True(options > 3000, $"{options} options");
        // nearly every row is a plain field; the few others are shown as written (their tags in view)
        Assert.True(rowsShownAsWritten < texts / 50, $"{rowsShownAsWritten} rows shown as written");
    }
}
