using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Darklands.Presentation;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The import's summary: how many texts it imported, how many the check finds too long or with broken tags, how many it did not take
/// (an unknown key; a reviewed text kept, its file's words offered with Use). Each line opens its text. Undo import puts every
/// changed text and its review mark back.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class ImportSummaryWindow : Window
{
    private readonly StackPanel _lines = new() { Spacing = 2 };
    private readonly TextBlock _counts = new() { FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };

    /// <param name="name">The file's name (or "the clipboard").</param>
    /// <param name="titleOf">A text's title as the editor lists it.</param>
    /// <param name="open">Opens a text in the editor.</param>
    /// <param name="use">Takes the file's words for a kept text; <paramref name="undo"/> puts the import back.</param>
    public ImportSummaryWindow(string name, ImportResult result, IReadOnlyList<TextEntry> entries, Func<TextEntry, string> titleOf,
        Action<string> open, Action<string> use, Action undo)
    {
        Title = "Import: " + name;
        Width = 720;
        Height = 520;
        Background = WorkshopTheme.Brush(WorkshopTheme.Surface);
        var byKey = entries.ToDictionary(e => e.Key, StringComparer.Ordinal);
        void Fill()
        {
            _lines.Children.Clear();
            var imported = result.Imported.Where(byKey.ContainsKey).Select(k => byKey[k]).ToList();
            var broken = imported.Where(e => e.Problems.Any(p => p.Severity == LanguagePackCheck.Severity.Error)).ToList();
            // too long: past its box, or a card text the game will show in pages (the check's note)
            static bool Long(LanguagePackCheck.Finding p) => p.Severity == LanguagePackCheck.Severity.Overflow
                || p.Severity == LanguagePackCheck.Severity.Note && p.Message.Contains("pages", StringComparison.Ordinal);
            var tooLong = imported.Where(e => e.Problems.Any(Long) && !broken.Contains(e)).ToList();
            _counts.Text = $"✓ {N(imported.Count)} imported   ⚠ {N(tooLong.Count)} too long   ✕ {N(broken.Count)} broken   ↷ {N(result.Kept.Count + result.NotImported.Count)} not imported";
            foreach (var e in broken)
            {
                Line("✕", WorkshopTheme.Bad, titleOf(e), e.Problems.First(p => p.Severity == LanguagePackCheck.Severity.Error).Message, e.Key, "Open", () => open(e.Key));
            }
            foreach (var e in tooLong)
            {
                Line("⚠", WorkshopTheme.Warn, titleOf(e), e.Problems.First(Long).Message, e.Key, "Open", () => open(e.Key));
            }
            foreach (var (key, _) in result.Kept.ToList())
            {
                var e = byKey[key];
                Line("↷", WorkshopTheme.Muted, titleOf(e), "reviewed: your words kept (open it to compare; Use takes the file's)", key, "Use", () =>
                {
                    use(key);
                    Fill();
                });
            }
            foreach (var (key, reason) in result.NotImported)
            {
                Line("↷", WorkshopTheme.Muted, key, reason, null, null, null);
            }
            if (_lines.Children.Count == 0)
            {
                _lines.Children.Add(new TextBlock { Text = "No problems: every text the file gave is in, marked for review.", Foreground = WorkshopTheme.Brush(WorkshopTheme.Ok) });
            }
        }
        void Line(string mark, Color colour, string title, string what, string? key, string? button, Action? act)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3) };
            if (button is not null && act is not null)
            {
                var b = WorkshopTheme.Outline(new TextBlock { Text = button, FontSize = 12 });
                b.Padding = new Thickness(10, 2);
                b.Click += (_, _) => act();
                DockPanel.SetDock(b, Dock.Right);
                row.Children.Add(b);
            }
            var m = new TextBlock { Text = mark, Width = 20, Foreground = WorkshopTheme.Brush(colour), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(m, Dock.Left);
            row.Children.Add(m);
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { Text = what, FontSize = 11.5, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), TextWrapping = TextWrapping.Wrap },
                },
            });
            ToolTip.SetTip(row, key);
            _lines.Children.Add(row);
        }
        var undoButton = WorkshopTheme.Outline(new TextBlock { Text = "Undo import" });
        undoButton.Click += (_, _) =>
        {
            undo();
            Close();
        };
        var ok = WorkshopTheme.Primary(new TextBlock { Text = "OK" });
        ok.IsDefault = true;
        ok.Click += (_, _) => Close();
        var foot = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { undoButton, ok } };
        DockPanel.SetDock(buttons, Dock.Right);
        foot.Children.Add(buttons);
        foot.Children.Add(new TextBlock
        {
            Text = "Every imported text is marked \"needs review\".",
            FontSize = 12,
            Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var body = new DockPanel { Margin = new Thickness(22, 18) };
        DockPanel.SetDock(_counts, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        body.Children.Add(_counts);
        body.Children.Add(foot);
        body.Children.Add(new ScrollViewer { Margin = new Thickness(0, 12, 0, 0), Content = _lines });
        Content = body;
        Fill();
    }

    private static string N(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
