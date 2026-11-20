using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
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
    private readonly ComboBox _screens = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _states = new() { ItemsSource = new[] { "every text", "untranslated", "translated", "with a problem" }, SelectedIndex = 0 };
    private readonly TextBox _search = new() { Watermark = "search the English, the translation or the key" };
    private readonly ListBox _list = new();
    private readonly TextBlock _progress = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    private readonly TextBlock _key = new() { FontWeight = FontWeight.Bold, FontSize = 15, Foreground = WorkshopTheme.Brush(WorkshopTheme.Gold) };
    private readonly TextBlock _comment = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim) };
    // the English, read only, its tags and names in their colours (the chips' colours)
    private readonly SelectableTextBlock _english = new() { TextWrapping = TextWrapping.Wrap, Foreground = WorkshopTheme.Brush(WorkshopTheme.Ink), FontSize = 14 };
    // the translation, its tags, names and arguments in their chips' colours as the translator types (AvaloniaEdit)
    private readonly TranslationBox _translation = new() { MinHeight = 96, MaxHeight = 260 };
    private readonly TextBlock _problems = new() { TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Left, Foreground = WorkshopTheme.Brush(WorkshopTheme.Problem) };
    private readonly Image _picture = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _barFill = new() { Height = 10, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _bar = new() { Height = 10, Background = WorkshopTheme.Brush(WorkshopTheme.WoodLine), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _measure = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextPreview _preview;
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _chipHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Gold) };
    private readonly StackPanel _choice = new() { Spacing = 4 };
    private readonly TextBlock _terms = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim) };
    private readonly Grid _editor = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*"), Margin = new Thickness(12, 0, 0, 0) };
    private readonly StackPanel _previewPanel = new() { Spacing = 4 };
    // the Rows view of a card text (the default): the intro, the passages and each option in a field of its own
    private readonly StackPanel _rowsPanel = new() { Spacing = 10 };
    private readonly ScrollViewer _rowsScroll = new() { IsVisible = false };
    private readonly TextBlock _rowsNote = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 2, 0, 4) };
    private readonly Button _rowsButton = new() { Content = "Rows" };
    private readonly Button _rawButton = new() { Content = "Raw" };
    private readonly Border _viewSwitch = new();
    private readonly List<Control> _rawParts = [];
    private List<CardRow> _rows = [];
    private TranslationBox? _focusedRow;
    private bool _raw;
    private Panel? _chipsHome;
    private PreviewResult? _shown;
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
        _list.ItemTemplate = new FuncDataTemplate<TextEntry>((entry, _) => Row(entry));
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(6, 4)), new Setter(ListBoxItem.MinHeightProperty, 0.0) },
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
        _editor.SizeChanged += (_, _) => Place();
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

    /// <summary>Reads the pack's fonts again and draws the text again (after a font pack was built).</summary>
    public void FontsChanged()
    {
        _preview.ReloadFonts();
        Show(_current);
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
        var dim = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim);
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto"), RowDefinitions = new RowDefinitions("Auto,6,Auto") };
        filters.Children.Add(_screens);
        Grid.SetColumn(_states, 2);
        filters.Children.Add(_states);
        Grid.SetRow(_search, 2);
        Grid.SetColumnSpan(_search, 3);
        filters.Children.Add(_search);
        var counters = new DockPanel { Margin = new Thickness(2, 8, 2, 6) };
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var state in new[] { TextState.Translated, TextState.Problem, TextState.Untranslated })
        {
            legend.Children.Add(Dot(state));
            legend.Children.Add(new TextBlock { Text = ShortName(state), FontSize = 11, Foreground = dim, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        DockPanel.SetDock(legend, Dock.Right);
        counters.Children.Add(legend);
        counters.Children.Add(_progress);
        var left = new DockPanel();
        DockPanel.SetDock(filters, Dock.Top);
        DockPanel.SetDock(counters, Dock.Top);
        left.Children.Add(filters);
        left.Children.Add(counters);
        left.Children.Add(new Border { Child = _list, BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine), BorderThickness = new Thickness(1) });

        Control Heading(string text) => new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2), VerticalAlignment = VerticalAlignment.Center };
        void Put(Control c, int row, int column = 0, int span = 1)
        {
            Grid.SetRow(c, row);
            Grid.SetColumn(c, column);
            Grid.SetColumnSpan(c, span);
            _editor.Children.Add(c);
        }
        Put(new StackPanel { Spacing = 2, Children = { _key, _comment } }, 0, 0, 2);
        // the English heading, with the Rows | Raw switch of a card text
        _viewSwitch.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _rowsButton, _rawButton } };
        _viewSwitch.Background = WorkshopTheme.Brush(WorkshopTheme.Surface);
        _viewSwitch.CornerRadius = new CornerRadius(7);
        _viewSwitch.Padding = new Thickness(2);
        _viewSwitch.HorizontalAlignment = HorizontalAlignment.Left;
        _viewSwitch.Margin = new Thickness(0, 4, 0, 4);
        foreach (var b in new[] { _rowsButton, _rawButton })
        {
            b.Padding = new Thickness(12, 3);
            b.FontSize = 12;
            b.CornerRadius = new CornerRadius(5);
            b.BorderThickness = new Thickness(0);
            ToolTip.SetTip(b, b == _rowsButton ? "The intro and each option in a field of its own; line breaks and indents are written for you" : "The text as the pack's file holds it, its tags included");
        }
        _rowsButton.Click += (_, _) => SetRaw(false);
        _rawButton.Click += (_, _) => SetRaw(true);
        var englishHead = Heading("English");
        Put(new StackPanel { Children = { _viewSwitch, _rowsNote, englishHead } }, 1);
        _rawParts.Add(englishHead);
        var englishBox = new Border
        {
            Background = WorkshopTheme.Brush(WorkshopTheme.Side),
            BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = new ScrollViewer { Content = _english, Padding = new Thickness(10, 6) },
            MinHeight = 56,
            MaxHeight = 240,
        };
        Put(englishBox, 2);
        Put(_terms, 3);
        var translationHead = new DockPanel { Children = { Heading("Translation"), _chips } };
        _chipsHome = translationHead;
        _chips.Margin = new Thickness(12, 4, 0, 0);
        Put(translationHead, 4);
        Put(_translation, 5);
        _rawParts.AddRange([englishBox, translationHead, _translation]);
        // the rows take the place of the English and the translation boxes
        _rowsScroll.Content = _rowsPanel;
        Put(_rowsScroll, 2);
        Grid.SetRowSpan(_rowsScroll, 7);
        Put(new StackPanel { Spacing = 4, Children = { _chipHint, _choice } }, 6);
        var frame = new Border { Child = _picture, BorderBrush = WorkshopTheme.Brush(WorkshopTheme.GoldDark), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left };
        _previewPanel.Children.Add(Heading("As the game draws it"));
        _previewPanel.Children.Add(frame);
        _previewPanel.Children.Add(_bar);
        _previewPanel.Children.Add(_measure);
        _previewPanel.Children.Add(_problems);
        _editor.Children.Add(_previewPanel);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("380,4,*"), Margin = new Thickness(8) };
        grid.Children.Add(left);
        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
        Grid.SetColumn(splitter, 1);
        grid.Children.Add(splitter);
        Grid.SetColumn(_editor, 2);
        grid.Children.Add(_editor);
        return grid;
    }

    // a row of the list: the state's mark, the key, the text (its translation, else the English)
    private static Control Row(TextEntry? entry)
    {
        if (entry is null)
        {
            return new TextBlock();
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,112,*") };
        var dot = Dot(entry.State);
        ToolTip.SetTip(dot, StateName(entry.State));
        row.Children.Add(dot);
        var key = new TextBlock { Text = entry.Key, FontSize = 11, Foreground = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(key, 1);
        row.Children.Add(key);
        var text = new TextBlock
        {
            Text = Preview(entry.Translation ?? entry.English),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WorkshopTheme.Brush(entry.Translation is null ? WorkshopTheme.Untranslated : WorkshopTheme.Parchment),
        };
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        return row;
    }

    // the state's mark: a full green dot translated, a full red one with a problem, an empty ring not translated yet
    private static Control Dot(TextState state) => new Avalonia.Controls.Shapes.Ellipse
    {
        Width = 9,
        Height = 9,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Left,
        Fill = state switch
        {
            TextState.Translated => WorkshopTheme.Brush(WorkshopTheme.Translated),
            TextState.Problem => WorkshopTheme.Brush(WorkshopTheme.Problem),
            _ => Brushes.Transparent,
        },
        Stroke = state == TextState.Untranslated ? WorkshopTheme.Brush(WorkshopTheme.Untranslated) : null,
        StrokeThickness = 1.5,
    };

    private static string StateName(TextState state) => state switch
    {
        TextState.Translated => "translated",
        TextState.Problem => "translated, with a problem the check found",
        _ => "not translated yet",
    };

    // the legend's words, short enough to stand beside the counters
    private static string ShortName(TextState state) => state switch
    {
        TextState.Translated => "done",
        TextState.Problem => "problem",
        _ => "to do",
    };

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
        var shown = (visible ?? Visible().ToList()).Count;
        var (total, every) = _project.Progress();
        var n = System.Globalization.CultureInfo.InvariantCulture;
        _progress.Text = $"{total.ToString("N0", n)} of {every.ToString("N0", n)} translated · {shown.ToString("N0", n)} shown";
    }

    private void Show(TextEntry? entry)
    {
        _current = entry;
        _loading = true;
        _key.Text = entry?.Key ?? "";
        _comment.Text = entry is null ? "" : "Shown on: " + string.Join(", ", entry.Screens) + "\n" + string.Join("\n", entry.Template.Comments.Skip(1).Prepend(Rest(entry)));
        ToolTip.SetTip(_comment, entry is null ? null : string.Join("\n", entry.Template.Comments));
        // the English as a translator reads it: without the line break and indent a card text's value starts with in the file
        Paint(_english, entry is null ? "" : entry.English.TrimStart('\n').Replace("\n    ", "\n", StringComparison.Ordinal).TrimStart(' '));
        _translation.Text = entry?.Translation ?? "";
        _translation.Editable = entry is not null;
        _problems.Text = entry is null ? "" : string.Join("\n", entry.Problems.Select(p => $"{p.Severity}: {p.Message}"));
        _loading = false;
        ShowChips();
        ShowRows();
        ShowPreview();
        ShowHints();
    }

    // ---------------------------------------------------------------- the Rows view of a card text
    private void SetRaw(bool raw)
    {
        _raw = raw;
        Show(_current);
    }

    // the card text's rows, or the raw boxes (a screen text, the Raw switch, or a translation whose lines are not the English's)
    private void ShowRows()
    {
        var card = _current is not null && _current.Key.StartsWith("msg:", StringComparison.Ordinal);
        _viewSwitch.IsVisible = card;
        _rowsNote.IsVisible = false;
        IReadOnlyList<CardRow>? rows = null;
        IReadOnlyList<CardRow> english = [];
        if (card && !_raw)
        {
            english = CardRows.Parse(_current!.English);
            rows = CardRows.Against(english, _current.Translation);
            if (rows is null)
            {
                _rowsNote.Text = "Your text's lines are not the English's (another number of options or passages): it is shown as written.";
                _rowsNote.Foreground = WorkshopTheme.Brush(WorkshopTheme.Warn);
                _rowsNote.IsVisible = true;
            }
        }
        var rowsMode = rows is not null;
        _rowsScroll.IsVisible = rowsMode;
        foreach (var part in _rawParts)
        {
            part.IsVisible = !rowsMode;
        }
        Segment(_rowsButton, card && rowsMode || card && !_raw);
        Segment(_rawButton, card && !rowsMode);
        _rowsPanel.Children.Clear();
        _focusedRow = null;
        // the chips: above the rows (they go into the field the cursor is in), else beside the translation's heading
        (_chips.Parent as Panel)?.Children.Remove(_chips);
        // in the rows the line breaks, option markers and indents are written for the translator: only the names are offered
        foreach (var chip in _chips.Children.OfType<Button>())
        {
            chip.IsVisible = !(rowsMode && chip.Content is string text && text.StartsWith('<'));
        }
        if (!rowsMode)
        {
            _chipsHome?.Children.Add(_chips);
            return;
        }
        _rowsPanel.Children.Add(new DockPanel
        {
            Children =
            {
                new TextBlock { Text = "Insert", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 10, 0) },
                _chips,
            },
        });
        _rows = [.. rows!];
        var option = 0;
        var muted = WorkshopTheme.Brush(WorkshopTheme.Muted);
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            var en = english[i];
            var index = i;
            var label = row.Kind switch
            {
                CardRowKind.Option => $"Option {++option}",
                CardRowKind.Passage => $"Only when it applies · {row.Label}",
                _ => i == 0 ? "Introduction" : "Introduction, continued",
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = muted });
            if (row.Kind == CardRowKind.Option && row.Label.Length > 0)
            {
                head.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 0),
                    Background = WorkshopTheme.Brush(Color.FromArgb(0x29, 0x9B, 0x6E, 0xD2)),
                    Child = new TextBlock { Text = row.Label, FontSize = 11, Foreground = WorkshopTheme.Brush(Color.Parse("#c7a6e8")) },
                });
            }
            var englishText = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = muted };
            Paint(englishText, (en.Prefix.Contains("...", StringComparison.Ordinal) ? "… " : "") + (en.Field ?? CardRow.Unwrapped(en.Content)));
            var box = new TranslationBox { MinHeight = row.Kind == CardRowKind.Option ? 34 : 64, MaxHeight = 220 };
            _loading = true;
            box.Text = row.Field ?? CardRow.Unwrapped(row.Content);
            _loading = false;
            ToolTip.SetTip(box, row.Field is null ? "This row has tags the fields do not show as lines: it is written as the file holds it." : null);
            box.GotFocus += (_, _) => _focusedRow = box;
            box.TextChanged += (_, _) =>
            {
                if (_loading || _current is null)
                {
                    return;
                }
                _rows[index] = _rows[index].Field is not null || _rows[index].Content.Length == 0 ? _rows[index].WithField(box.Text) : _rows[index] with { Content = box.Text };
                var written = CardRows.Write(_rows);
                _project.Set(_current, written);
                _loading = true;
                _translation.Text = written;
                _loading = false;
                Changed?.Invoke();
                UpdateProgress();
                ShowPreview();
                ShowHints();
            };
            _rowsPanel.Children.Add(new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    head,
                    new Border { Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), Background = WorkshopTheme.Brush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)), Child = englishText },
                    box,
                },
            });
        }
    }

    // a segment of the Rows | Raw switch, on or off
    private static void Segment(Button button, bool on)
    {
        button.Background = on ? WorkshopTheme.Brush(WorkshopTheme.Raised) : Brushes.Transparent;
        button.Foreground = WorkshopTheme.Brush(on ? WorkshopTheme.Text : WorkshopTheme.Muted);
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
            .DistinctBy(t => t.Text, StringComparer.Ordinal).ToList();
        if (_current.Key.StartsWith("msg:", StringComparison.Ordinal))
        {
            chips.AddRange(_project.Variables.Select(v => new TextToken(TokenKind.Name, "$" + v.Name)));
        }
        foreach (var token in chips)
        {
            var chip = token.Text;
            var button = new Button
            {
                Content = chip,
                Padding = new Thickness(6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Background = WorkshopTheme.Brush(WorkshopTheme.Raised),
                Foreground = WorkshopTheme.Brush(WorkshopTheme.ChipColourOf(token) ?? WorkshopTheme.Ink),
                FontWeight = FontWeight.SemiBold,
            };
            ToolTip.SetTip(button, "Put it where the cursor is");
            button.Click += (_, _) =>
            {
                var target = _rowsScroll.IsVisible && _focusedRow is not null ? _focusedRow : _translation;
                var text = target.Text ?? "";
                var at = Math.Clamp(target.CaretIndex, 0, text.Length);
                target.Text = text.Insert(at, chip);
                target.CaretIndex = at + chip.Length;
                target.FocusText();
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
            _shown = null;
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
        _shown = result;
        Place();
        if (result.CardText)
        {
            var spare = result.FootLimit - result.Foot;
            _barFill.Width = 0;
            _bar.Background = WorkshopTheme.Brush(result.Overflows ? WorkshopTheme.Problem : result.Pages > 1 ? WorkshopTheme.Gold : WorkshopTheme.Translated);
            _measure.Text = result.Overflows
                ? $"The text runs past the foot of the card's window by {result.Foot - result.FootLimit + 1} px (every passage shown)."
                : result.Pages > 1
                    ? $"Too long for one window: the game shows it in {result.Pages} pages (shown one under another; every passage shown)."
                    : $"{spare} px to spare above the foot of the card's window (every passage shown).";
        }
        else if (result.Room > 0)
        {
            _bar.Background = WorkshopTheme.Brush(WorkshopTheme.WoodLine);
            var full = Math.Max(result.Room, result.Measured);
            _barFill.Width = _bar.Width * result.Measured / full;
            _barFill.Background = WorkshopTheme.Brush(result.Overflows ? WorkshopTheme.Problem : result.Measured > result.English ? WorkshopTheme.Gold : WorkshopTheme.Translated);
            _measure.Text = $"{result.Measured} px of {result.Room} px of room (the English {result.English} px)" + (result.Overflows ? ": too wide" : "");
        }
        else
        {
            _barFill.Width = 0;
            _bar.Background = WorkshopTheme.Brush(WorkshopTheme.WoodLine);
            _measure.Text = $"{result.Measured} px wide; its box is not known (no recorded session showed it).";
        }
        if (result.Problem is { } problem)
        {
            _measure.Text += "\n" + problem;
        }
    }

    // the preview's place and size: a card text's window beside the texts, a screen text's line under the translation; whole multiples
    // of the game's pixels, as large as the room allows (at most four times)
    private void Place()
    {
        var room = _editor.Bounds;
        var (width, height) = room.Width > 0 ? (room.Width, room.Height) : (820.0, 640.0);
        if (_shown is not { } shown)
        {
            Grid.SetRow(_previewPanel, 7);
            Grid.SetRowSpan(_previewPanel, 1);
            Grid.SetColumn(_previewPanel, 0);
            Grid.SetColumnSpan(_previewPanel, 2);
            return;
        }
        int scale;
        if (shown.CardText)
        {
            // the texts keep 330 px beside the card's window
            scale = (int)Math.Clamp(Math.Min((width - 330) / shown.Width, (height - 120) / shown.Height), 1, 4);
            Grid.SetRow(_previewPanel, 1);
            Grid.SetRowSpan(_previewPanel, 8);
            Grid.SetColumn(_previewPanel, 1);
            Grid.SetColumnSpan(_previewPanel, 1);
            _previewPanel.Margin = new Thickness(14, 0, 0, 0);
        }
        else
        {
            scale = (int)Math.Clamp((width - 4) / shown.Width, 1, 4);
            Grid.SetRow(_previewPanel, 7);
            Grid.SetRowSpan(_previewPanel, 1);
            Grid.SetColumn(_previewPanel, 0);
            Grid.SetColumnSpan(_previewPanel, 2);
            _previewPanel.Margin = new Thickness(0, 4, 0, 0);
        }
        _picture.Width = shown.Width * scale;
        _picture.Height = shown.Height * scale;
        _bar.Width = _picture.Width + 2;
        _measure.MaxWidth = _problems.MaxWidth = Math.Max(_bar.Width, 300);
        if (!shown.CardText && shown.Room > 0)
        {
            _barFill.Width = _bar.Width * shown.Measured / Math.Max(shown.Room, shown.Measured);
        }
    }

    // a text in the reader's box: words in ink, each tag, name and argument in its chip's colour
    private static void Paint(SelectableTextBlock block, string text)
    {
        var inlines = block.Inlines ??= [];
        inlines.Clear();
        foreach (var token in TextTokens.Split(text))
        {
            var run = new Avalonia.Controls.Documents.Run(token.Text);
            if (WorkshopTheme.ChipColourOf(token) is { } colour)
            {
                run.Foreground = WorkshopTheme.Brush(colour);
                run.FontWeight = FontWeight.SemiBold;
            }
            inlines.Add(run);
        }
    }

    // the template's first comment without its key (the key is the heading)
    private static string Rest(TextEntry entry)
    {
        var first = entry.Template.Comments.FirstOrDefault() ?? "";
        return first.StartsWith(entry.Key + " · ", StringComparison.Ordinal) ? first[(entry.Key.Length + 3)..] : first;
    }

    private static string Preview(string value)
    {
        var line = value.Replace('\n', ' ').Trim();
        return line.Length > 90 ? line[..90] + "…" : line;
    }
}
