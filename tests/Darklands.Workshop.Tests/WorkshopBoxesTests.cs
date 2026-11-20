using System.Text.RegularExpressions;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// The boxes the Workshop ships (docs/LOCALISATION.md) hold no word of the game: keys and the program's own screen names, numbers, and
/// arguments of filler only; and no text of the player's EXE or MSGFILES is found in the file.
/// </summary>
public sealed partial class WorkshopBoxesTests
{
    [GeneratedRegex(@"^(exe:(DS|[0-9A-F]{4}):[0-9A-F]{4}|id:[a-z0-9.-]+|lst:(item|saint|formula)/\d+\.(name|short)|loc:\d+\.name|cty:\d+\.[a-z-]+|enm:(template|type)/\d+\.name)$")]
    private static partial Regex Key();

    [GeneratedRegex(@"^([A-Z][A-Za-z]*(Screen|Screens|Page|Box|Bar|Panel|Dialog|Window)|MenuBar|PartyPanel|Hourglass|StartUp|SideWindowMessages|OverlandDrawing|Cinematics)$")]
    private static partial Regex ScreenName();

    [GeneratedRegex(@"^[n0 ]*$")]
    private static partial Regex Filler();

    [Fact]
    public void TheShippedBoxesHoldNoWordOfTheGame()
    {
        var boxes = WorkshopBoxes.Default;
        Assert.NotEmpty(boxes);
        foreach (var box in boxes)
        {
            Assert.Matches(Key(), box.Key);
            Assert.All(box.Screens, s => Assert.Matches(ScreenName(), s));
            Assert.All(box.Arguments?.Values ?? [], v => Assert.Matches(Filler(), v));
        }
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        // the file's text without its keys and the program's screen names (checked above): no text of the game's files in what is left
        var text = WorkshopBoxes.Text;
        foreach (var box in boxes)
        {
            text = text.Replace(box.Key, " ", StringComparison.Ordinal);
            foreach (var screen in box.Screens)
            {
                text = text.Replace(screen, " ", StringComparison.Ordinal);
            }
        }
        var found = OriginalTextExtractor.Extract(files).Entries
            .Where(e => e.Key.StartsWith("exe:", StringComparison.Ordinal) || e.Key.StartsWith("msg:", StringComparison.Ordinal))
            .Select(e => e.Text.Trim())
            .Where(t => t.Length >= 4 && t.Any(char.IsLetter))
            .Distinct(StringComparer.Ordinal)
            .Where(t => text.Contains(t, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(found.Count == 0, "game text in the shipped boxes: " + string.Join(" | ", found.Take(5)));
    }
}
