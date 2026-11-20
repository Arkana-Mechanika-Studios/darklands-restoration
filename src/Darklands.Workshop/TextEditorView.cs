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
    private readonly Image _picture = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _barFill = new() { Height = 10, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _bar = new() { Height = 10, Background = Brushes.Gainsboro, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _measure = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextPreview _preview;
    private readonly WrapPanel _chips = new();
    private readonly TextBlock _chipHint = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGoldenrod };
    private readonly StackPanel _choice = new() { Spacing = 4 };
    private readonly TextBlock _terms = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SteelBlue };
    private readonly Glossary _glossary;
    private TextEntry? _current;
    private bool _loading;

    public TextEditorView(TextProject project)
    {
        _project = project;
        _preview = new TextPreview(project);
        _glossary = Glossary.Load(project.Folder);
        RenderOptions.SetBitmapInterpolationMode(_picture, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        _bar.Child = _barFill;
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
                ShowPreview();
                ShowHints();
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
        _glossary.Save(_project.Folder);
        Changed?.Invoke();
    }

    /// <summary>The key of the text shown (for "show in game": the inspector names it; null: none).</summary>
    public string? CurrentKey => _current?.Key;

    /// <summary>The pack's glossary (its window edits it).</summary>
    public Glossary Glossary => _glossary;

    /// <summary>The project (the variables' window edits its variables).</summary>
    public TextProject Project => _project;

    /// <summary>Shows the current text again (after the glossary or the variables changed).</summary>
    public void Refresh()
    {
        Show(_current);
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
        right.Children.Add(_terms);
        right.Children.Add(new TextBlock { Text = "Translation", FontWeight = FontWeight.SemiBold });
        right.Children.Add(_chips);
        right.Children.Add(_translation);
        right.Children.Add(_chipHint);
        right.Children.Add(_choice);
        right.Children.Add(new TextBlock { Text = "As the game draws it", FontWeight = FontWeight.SemiBold });
        right.Children.Add(new Border { Child = _picture, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left });
        right.Children.Add(_bar);
        right.Children.Add(_measure);
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
        ShowChips();
        ShowPreview();
        ShowHints();
    }

    // the English's chips (tags, $Names, placeables) and the pack's own variables, each a button that puts it where the cursor is
    private void ShowChips()
    {
        _chips.Children.Clear();
        if (_current is null)
        {
            return;
        }
        var chips = TextTokens.Split(_current.English).Where(t => t.Kind != TokenKind.Words && !t.Text.StartsWith("{\"", StringComparison.Ordinal))
            .Select(t => t.Text).Distinct(StringComparer.Ordinal).ToList();
        if (_current.Key.StartsWith("msg:", StringComparison.Ordinal))
        {
            chips.AddRange(_project.Variables.Select(v => "$" + v.Name));
        }
        foreach (var chip in chips)
        {
            var button = new Button { Content = chip, Padding = new Thickness(6, 1), Margin = new Thickness(0, 0, 4, 4), FontSize = 11 };
            ToolTip.SetTip(button, "Put it where the cursor is");
            button.Click += (_, _) =>
            {
                var text = _translation.Text ?? "";
                var at = Math.Clamp(_translation.CaretIndex, 0, text.Length);
                _translation.Text = text.Insert(at, chip);
                _translation.CaretIndex = at + chip.Length;
                _translation.Focus();
            };
            _chips.Children.Add(button);
        }
    }

    // as the translator types: the chips left out or unknown, the glossary's terms, and a choice's variants as fields
    private void ShowHints()
    {
        if (_current is null)
        {
            _chipHint.Text = _terms.Text = "";
            _choice.Children.Clear();
            return;
        }
        var names = _project.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        if (_current.Translation is { } translation)
        {
            var (missing, unknown) = TextTokens.Compare(_current.English, translation, names);
            _chipHint.Text = (missing.Count > 0 ? "Left out: " + string.Join(" ", missing) + "   " : "") + (unknown.Count > 0 ? "Not the English's: " + string.Join(" ", unknown) : "");
        }
        else
        {
            _chipHint.Text = "";
        }
        var terms = _glossary.In(_current.English);
        var missed = _current.Translation is null ? [] : _glossary.Missed(_current.English, _current.Translation);
        _terms.Text = terms.Count == 0 ? "" : "Glossary: " + string.Join(";  ", terms.Select(t => $"{t.Term} → {t.Translation}" + (missed.Contains(t) ? " (not used)" : "")));
        ShowChoice();
    }

    // a translation that is one choice (a plural, a $sex choice) as one field a variant
    private void ShowChoice()
    {
        _choice.Children.Clear();
        if (_current?.Translation is not { } value || FluentChoice.Read(value) is not { } choice)
        {
            return;
        }
        _choice.Children.Add(new TextBlock { Text = $"A choice by ${choice.Selector}:", FontWeight = FontWeight.SemiBold });
        var fields = new List<(FluentChoice.Variant Variant, TextBox Box)>();
        foreach (var variant in choice.Variants)
        {
            var row = new DockPanel();
            var label = new TextBlock { Text = (variant.Default ? "*" : "") + variant.Key, Width = 80, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(label, Dock.Left);
            var box = new TextBox { Text = variant.Words };
            row.Children.Add(label);
            row.Children.Add(box);
            fields.Add((variant, box));
            _choice.Children.Add(row);
        }
        foreach (var (_, box) in fields)
        {
            box.LostFocus += (_, _) =>
            {
                var written = FluentChoice.Write(choice.Selector, fields.Select(f => f.Variant with { Words = f.Box.Text ?? "" }));
                if (written != _translation.Text)
                {
                    _translation.Text = written;
                }
            };
        }
    }

    // the text as the game draws it (TextPreview: the game's painter and layout), three times the game's pixels, and its width bar
    private void ShowPreview()
    {
        if (_current is null)
        {
            _picture.Source = null;
            _measure.Text = "";
            _barFill.Width = 0;
            return;
        }
        var result = _preview.Render(_current, _current.Translation);
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(result.Width, result.Height), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var frame = bitmap.Lock())
        {
            for (var row = 0; row < result.Height; row++)
            {
                var line = Array.ConvertAll(result.Argb[(row * result.Width)..((row + 1) * result.Width)], v => unchecked((int)v));
                System.Runtime.InteropServices.Marshal.Copy(line, 0, frame.Address + row * frame.RowBytes, result.Width);
            }
        }
        _picture.Source = bitmap;
        _picture.Width = result.Width * 3;
        _picture.Height = result.Height * 3;
        if (result.CardText)
        {
            var spare = result.FootLimit - result.Foot;
            _barFill.Width = 0;
            _bar.Background = result.Overflows ? Brushes.IndianRed : Brushes.MediumSeaGreen;
            _measure.Text = result.Overflows
                ? $"The text runs past the foot of the card's window by {result.Foot - result.FootLimit + 1} px (every passage shown)."
                : $"{spare} px to spare above the foot of the card's window (every passage shown).";
        }
        else if (result.Room > 0)
        {
            _bar.Background = Brushes.Gainsboro;
            var full = Math.Max(result.Room, result.Measured);
            var total = _bar.Bounds.Width > 0 ? _bar.Bounds.Width : 300;
            _barFill.Width = total * result.Measured / full;
            _barFill.Background = result.Overflows ? Brushes.IndianRed : result.Measured > result.English ? Brushes.Goldenrod : Brushes.MediumSeaGreen;
            _measure.Text = $"{result.Measured} px of {result.Room} px of room (the English {result.English} px)" + (result.Overflows ? ": too wide" : "");
        }
        else
        {
            _barFill.Width = 0;
            _bar.Background = Brushes.Gainsboro;
            _measure.Text = $"{result.Measured} px wide; its box is not known (no recorded session showed it).";
        }
        if (result.Problem is { } problem)
        {
            _measure.Text += "\n" + problem;
        }
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
