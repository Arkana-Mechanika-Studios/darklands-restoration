using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>What the export dialog was asked for: the texts, the format (json, csv), card texts as rows or raw.</summary>
internal sealed record ExportChoice(IReadOnlyList<TextEntry> Entries, string Format, bool Rows);

/// <summary>
/// The whole-file export (Export texts…, Ctrl+E): which texts (all untranslated, everything, those marked for review, those with
/// problems, the chosen text's group), the format (JSON for AI chats and scripts, CSV for spreadsheets; XLIFF 2.0 next), card
/// texts as rows or raw, and how many texts and words that is. The file itself is written by the window (a save picker).
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class ExportDialog : Window
{
    private readonly TextProject _project;
    private readonly ReviewMarks _marks;
    private readonly TextEntry? _current;
    private readonly RadioButton _untranslated = new() { GroupName = "which", IsChecked = true };
    private readonly RadioButton _everything = new() { GroupName = "which" };
    private readonly RadioButton _review = new() { GroupName = "which" };
    private readonly RadioButton _problems = new() { GroupName = "which" };
    private readonly RadioButton _group = new() { GroupName = "which" };
    private readonly RadioButton _json = new() { GroupName = "format", IsChecked = true };
    private readonly RadioButton _csv = new() { GroupName = "format" };
    private readonly RadioButton _xliff = new() { GroupName = "format", IsEnabled = false };
    private readonly RadioButton _rows = new() { GroupName = "cards", IsChecked = true };
    private readonly RadioButton _raw = new() { GroupName = "cards" };
    private readonly TextBlock _count = new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };

    public ExportDialog(TextProject project, TextEntry? current)
    {
        _project = project;
        _marks = ReviewMarks.Load(project.Folder);
        _current = current;
        Title = "Export texts";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        Background = WorkshopTheme.Brush(WorkshopTheme.Surface);
        var groupTitle = current is null ? null : TextTitles.Group(current).Title;
        Set(_untranslated, $"All untranslated ({N(Count(e => e.Translation is null))})");
        Set(_everything, $"Everything ({N(project.Entries.Count)})");
        Set(_review, $"Marked for review ({N(Count(e => e.Translation is not null && _marks.Has(e.Key)))})");
        Set(_problems, $"With problems ({N(Count(e => e.State == TextState.Problem))})");
        Set(_group, groupTitle is null ? "The chosen text's group (no text chosen)" : $"The chosen text's group: {groupTitle} ({N(Group().Count)})");
        _group.IsEnabled = groupTitle is not null;
        Set(_json, "JSON    for AI chats and scripts (one object a text)");
        Set(_csv, "CSV     for spreadsheets (UTF-8, one row a text)");
        Set(_xliff, "XLIFF 2.0    for CAT tools and DeepL (the next set)");
        Set(_rows, "rows (introduction, each passage and option)");
        Set(_raw, "raw (as the pack's file holds it, with tags)");
        foreach (var r in new[] { _untranslated, _everything, _review, _problems, _group, _rows, _raw })
        {
            r.IsCheckedChanged += (_, _) => ShowCount();
        }
        var export = WorkshopTheme.Primary(new TextBlock { Text = "Export…" });
        export.IsDefault = true;
        export.Click += (_, _) => Close(new ExportChoice(Chosen(), _csv.IsChecked == true ? "csv" : "json", _rows.IsChecked == true));
        var cancel = WorkshopTheme.Outline(new TextBlock { Text = "Cancel" });
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close(null);
        var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, export } };
        DockPanel.SetDock(buttons, Dock.Right);
        foot.Children.Add(buttons);
        foot.Children.Add(_count);
        Content = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Spacing = 4,
            Children =
            {
                Heading("Which texts"),
                new WrapPanel { Children = { _untranslated, _everything, _review, _problems } },
                _group,
                Heading("Format"),
                _json,
                _csv,
                _xliff,
                Heading("Card texts as"),
                _rows,
                _raw,
                new TextBlock
                {
                    Text = "Each text comes with its context: where the game shows it, the room it has, the $Names and hot keys it must keep, "
                        + "and its glossary terms. The file starts with a short instruction (with your pack's notes for translators and AI).",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted),
                    Margin = new Thickness(0, 8, 0, 4),
                },
                foot,
            },
        };
        ShowCount();
    }

    private static string N(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private static void Set(RadioButton r, string text)
    {
        r.Content = new TextBlock { Text = text, FontSize = 13 };
        r.Margin = new Thickness(0, 0, 18, 0);
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), Margin = new Thickness(0, 10, 0, 2) };

    private int Count(Func<TextEntry, bool> which) => _project.Entries.Count(which);

    private List<TextEntry> Group()
    {
        if (_current is null)
        {
            return [];
        }
        var key = TextTitles.Group(_current).Key;
        return [.. _project.Entries.Where(e => TextTitles.Group(e).Key == key)];
    }

    /// <summary>The texts chosen.</summary>
    public IReadOnlyList<TextEntry> Chosen() =>
        _everything.IsChecked == true ? _project.Entries
        : _review.IsChecked == true ? [.. _project.Entries.Where(e => e.Translation is not null && _marks.Has(e.Key))]
        : _problems.IsChecked == true ? [.. _project.Entries.Where(e => e.State == TextState.Problem)]
        : _group.IsChecked == true ? Group()
        : [.. _project.Entries.Where(e => e.Translation is null)];

    private void ShowCount()
    {
        var chosen = Chosen();
        var words = chosen.Sum(e => e.English.Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Length);
        _count.Text = $"{N(chosen.Count)} texts · about {N(words)} words";
    }
}

/// <summary>The pack's notes for translators and AI (pack.json "style"): the instruction an export's header gives; empty: the default.</summary>
internal sealed class NotesDialog : Window
{
    public NotesDialog(TextProject project)
    {
        Title = "Notes for translators and AI";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        Background = WorkshopTheme.Brush(WorkshopTheme.Surface);
        var notes = new TextBox
        {
            Text = project.Style ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 120,
            Watermark = TextExport.DefaultStyle,
        };
        var save = WorkshopTheme.Primary(new TextBlock { Text = "Save" });
        save.Click += (_, _) =>
        {
            project.Style = notes.Text;
            Close(true);
        };
        var cancel = WorkshopTheme.Outline(new TextBlock { Text = "Cancel" });
        cancel.IsCancel = true;
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Your pack's style, for whoever translates an exported file (a person or an AI chat): its register, the forms of address, "
                        + "names to keep. Empty: the Workshop's default (shown greyed).",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5,
                    Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted),
                },
                notes,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } },
            },
        };
    }
}
