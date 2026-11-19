namespace Darklands.Localization.Tests;

/// <summary>The boxes file and the pack's messages as written (L-7w).</summary>
public sealed class TextBoxesTests
{
    [Fact]
    public void BoxesMergeToTheWidestEnglishAndTheLeastRoom()
    {
        var merged = TextBoxes.Merge(
        [
            new TextBox("exe:DS:2F71", 90, 200, false, 1, new Dictionary<string, string> { ["arg0"] = "Ritter" }),
            new TextBox("exe:DS:2F71", 120, 150, true, 1, new Dictionary<string, string> { ["arg0"] = "Wolfgang" }),
            new TextBox("exe:DS:2F71", 60, 180, false, 1, null),
            new TextBox("exe:DS:0086", 20, 40, true, 3, null),
        ]);
        Assert.Equal(["exe:DS:0086", "exe:DS:2F71"], merged.Select(b => b.Key));
        var purse = merged[1];
        Assert.Equal((120, 150, true), (purse.English, purse.Room, purse.Windowed));
        Assert.Equal("Wolfgang", purse.Arguments!["arg0"]);
    }

    [Fact]
    public void AFolderOfBoxFilesReadsAsOne()
    {
        var folder = Path.Combine(Path.GetTempPath(), "dl-boxes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            TextBoxes.Write(Path.Combine(folder, "boxes-1.json"), [new TextBox("exe:DS:2FF2", 30, 70, true, 1, null)]);
            TextBoxes.Write(Path.Combine(folder, "boxes-2.json"), [new TextBox("exe:DS:2FF2", 35, 60, false, 1, null)]);
            var read = Assert.Single(TextBoxes.Read(folder));
            Assert.Equal(new TextBox("exe:DS:2FF2", 35, 60, false, 1, null), read);
            Assert.DoesNotContain("Leave", File.ReadAllText(Path.Combine(folder, "boxes-1.json")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheMessagesOfAFileAreListedWithTheirLinesAndVariables()
    {
        var messages = FtlSource.Messages("""
            # a comment
            exe-ds-2f71 = { $arg0 } hat { $arg1 } Gulden
            -term = nicht gezählt

            msg-other00-0 =
                Erste Zeile { $count }<br>
                zweite {"{"} Zeile
            """);
        Assert.Equal(["exe-ds-2f71", "msg-other00-0"], messages.Select(m => m.Id));
        Assert.Equal([2, 5], messages.Select(m => m.Line));
        Assert.Equal(["arg0", "arg1"], messages[0].Variables.Order(StringComparer.Ordinal));
        Assert.Equal(["count"], messages[1].Variables);
    }
}
