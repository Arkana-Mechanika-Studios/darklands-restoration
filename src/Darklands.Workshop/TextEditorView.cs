using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Darklands.Presentation;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// E-text, the translators' editor (W-0 + E-text): the pack's texts grouped by where the game shows them (a screen, a card, "not seen"),
/// filtered by state and searched; the chosen text's English above its translation, the template's comment (its key, box and what
/// each argument is) beside it. The translation is written as the pack's file holds it (docs/LOCALISATION.md).
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class TextEditorView : IWorkshopEditor
{
    private const string AllScreens = "(every screen)";

    private readonly TextProject _project;
    private readonly ComboBox _screens = new() { MinWidth = 220 };
    private readonly ComboBox _states = new() { ItemsSource = new[] { "every text", "untranslated", "translated", "with a problem" }, SelectedIndex = 0 };
    private readonly TextBox _search = new() { Watermark = "search the English, the translation or the key", MinWidth = 260 };
    private readonly ListBox _list = new();
    private readonly TextBlock _progress = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _key = new() { FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _comment = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
    private readonly TextBox _english = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
    private readonly TextBox _translation = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140 };
    private readonly TextBlock _problems = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    private TextEntry? _current;
    private bool _loading;

    public TextEditorView(TextProject project)
    {
        _project = project;
        _screens.ItemsSource = new[] { AllScreens }.Concat(project.ByScreen().Select(g => g.Screen)).ToList();
        _screens.SelectedIndex = 0;
        _list.ItemTemplate = new FuncDataTemplate<TextEntry>((entry, _) => new TextBlock
        {
            Text = entry is null ? "" : Mark(entry) + "  " + entry.Key + "   " + Preview(entry.Translation ?? entry.English),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        _screens.SelectionChanged += (_, _) => Fill();
        _states.SelectionChanged += (_, _) => Fill();
        _search.TextChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) => Show(_list.SelectedItem as TextEntry);
        _translation.TextChanged += (_, _) =>
        {
            if (!_loading && _current is not null)
            {
                _project.Set(_current, _translation.Text);
                Changed?.Invoke();
                UpdateProgress();
            }
        };
        View = Build();
        Fill();
    }

    public string Title => $"Texts ({_project.Language})";

    public Control View { get; }

    public bool Dirty => _project.Dirty;

    public event Action? Changed;

    public void Save()
    {
        _project.Save();
        Changed?.Invoke();
    }

    public IReadOnlyList<LanguagePackCheck.Finding> Check()
    {
        var findings = _project.Check();
        Fill();
        Show(_current);
        return findings;
    }

    /// <summary>Shows the text <paramref name="id"/> (a finding's link), clearing the filters when they hide it.</summary>
    public void Select(string id)
    {
        if (_project.Find(id) is not { } entry)
        {
            return;
        }
        if (!Visible().Contains(entry))
        {
            _screens.SelectedIndex = 0;
            _states.SelectedIndex = 0;
            _search.Text = "";
            Fill();
        }
        _list.SelectedItem = entry;
        _list.ScrollIntoView(entry);
    }

    private Control Build()
    {
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        filters.Children.Add(_screens);
        filters.Children.Add(_states);
        filters.Children.Add(_search);
        var left = new DockPanel();
        DockPanel.SetDock(filters, Dock.Top);
        DockPanel.SetDock(_progress, Dock.Top);
        _progress.Margin = new Thickness(2, 0, 0, 6);
        left.Children.Add(filters);
        left.Children.Add(_progress);
        left.Children.Add(_list);

        var right = new StackPanel { Spacing = 6, Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(_key);
        right.Children.Add(_comment);
        right.Children.Add(new TextBlock { Text = "English", FontWeight = FontWeight.SemiBold });
        right.Children.Add(_english);
        right.Children.Add(new TextBlock { Text = "Translation", FontWeight = FontWeight.SemiBold });
        right.Children.Add(_translation);
        right.Children.Add(_problems);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,4,2*"), Margin = new Thickness(8) };
        grid.Children.Add(left);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        var scroller = new ScrollViewer { Content = right };
        Grid.SetColumn(scroller, 2);
        grid.Children.Add(scroller);
        return grid;
    }

    private IEnumerable<TextEntry> Visible()
    {
        var screen = _screens.SelectedItem as string ?? AllScreens;
        IEnumerable<TextEntry> entries = screen == AllScreens
            ? _project.Entries
            : _project.ByScreen().FirstOrDefault(g => g.Screen == screen).Entries ?? [];
        entries = _states.SelectedIndex switch
        {
            1 => entries.Where(e => e.State == TextState.Untranslated),
            2 => entries.Where(e => e.State == TextState.Translated),
            3 => entries.Where(e => e.State == TextState.Problem),
            _ => entries,
        };
        if (_search.Text is { Length: > 0 } search)
        {
            entries = entries.Where(e => e.English.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (e.Translation?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Key.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        return entries;
    }

    private void Fill()
    {
        var selected = _current;
        var visible = Visible().ToList();
        _list.ItemsSource = visible;
        if (selected is not null && visible.Contains(selected))
        {
            _list.SelectedItem = selected;
        }
        UpdateProgress(visible);
    }

    private void UpdateProgress(IReadOnlyList<TextEntry>? visible = null)
    {
        var (done, all) = _project.Progress(visible ?? Visible());
        var (total, every) = _project.Progress();
        _progress.Text = $"{done} / {all} here, {total} / {every} in all";
    }

    private void Show(TextEntry? entry)
    {
        _current = entry;
        _loading = true;
        _key.Text = entry?.Key ?? "";
        _comment.Text = entry is null ? "" : string.Join("\n", entry.Template.Comments) + "\nShown on: " + string.Join(", ", entry.Screens);
        // the English as a translator reads it: without the line break and indent a card text's value starts with in the file
        _english.Text = entry is null ? "" : entry.English.TrimStart('\n').Replace("\n    ", "\n", StringComparison.Ordinal).TrimStart(' ');
        _translation.Text = entry?.Translation ?? "";
        _translation.IsEnabled = entry is not null;
        _problems.Text = entry is null ? "" : string.Join("\n", entry.Problems.Select(p => $"{p.Severity}: {p.Message}"));
        _loading = false;
    }

    private static string Mark(TextEntry entry) => entry.State switch
    {
        TextState.Translated => "✓",
        TextState.Problem => "!",
        _ => "·",
    };

    private static string Preview(string value)
    {
        var line = value.Replace('\n', ' ').Trim();
        return line.Length > 90 ? line[..90] + "…" : line;
    }
}
