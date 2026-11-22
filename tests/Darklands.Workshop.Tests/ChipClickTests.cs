using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Darklands.Workshop.Text;

namespace Darklands.Workshop.Tests;

/// <summary>
/// A chip clicked with the pointer goes into the field being typed in, where its cursor is, and the cursor stays there: in a card's
/// Rows view (a row's own field, which the editor's text area takes the focus of) and in a screen text's Text view.
/// </summary>
[Collection(WorkshopWindowTests.Collection)]
public sealed class ChipClickTests
{
    private static void Click(Window window, Visual target)
    {
        var at = target.TranslatePoint(new Point(Math.Min(20, target.Bounds.Width / 2), Math.Min(10, target.Bounds.Height / 2)), window)
            ?? throw new InvalidOperationException("not in the window");
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task AClickedChipGoesIntoTheFieldBeingTypedInAndTypingGoesOn()
    {
        if (GameFolder.Files is null || Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") is not { } game)
        {
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "dl-chips-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = TextProject.Create(folder, "fr", "Test", null, game);
            // a card text with options whose English has a name (a chip), and a screen text with an argument
            var card = project.Entries.First(e => e.Key.StartsWith("msg:", StringComparison.Ordinal) && System.Text.RegularExpressions.Regex.IsMatch(e.English, @"\$[A-Z][a-z]+Name") && e.English.Contains("<opt>", StringComparison.Ordinal));
            var screen = project.Entries.First(e => !e.Key.StartsWith("msg:", StringComparison.Ordinal) && e.English.Contains("{ $", StringComparison.Ordinal));
            await WorkshopWindowTests.Session.Value.Dispatch(() =>
            {
                var window = new WorkshopWindow(new WorkshopSettings { GameFolder = game }, [folder]) { Width = 1280, Height = 800 };
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var editor = window.Editor!;
                foreach (var (entry, rows) in new[] { (card, true), (screen, false) })
                {
                    editor.Select(entry.Key);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var fields = window.GetVisualDescendants().OfType<TranslationBox>().Where(b => b.IsEffectivelyVisible).ToList();
                    // Rows: the first row's own field (the whole text's box is hidden), one a row; Text: the one field
                    Assert.True(editor.CurrentKey == entry.Key && (rows ? fields.Count >= 2 : fields.Count == 1), $"{entry.Key}: current {editor.CurrentKey}, {fields.Count} fields");
                    var field = fields[0];
                    field.Text = "";
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Click(window, field);
                    var chip = editor.Chips.First(c => c.IsEffectivelyVisible && c.Tag is string);
                    var words = (string)chip.Tag!;
                    Click(window, chip);
                    Assert.Equal(words, field.Text);
                    // the cursor stayed in the field, after the chip: typing goes on there
                    window.KeyTextInput("z");
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.Equal(words + "z", field.Text);
                    Assert.Contains(words + "z", editor.Current!.Translation ?? "", StringComparison.Ordinal);
                }
                window.Close();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return 0;
            }, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
