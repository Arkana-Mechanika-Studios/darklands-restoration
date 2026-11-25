using SlotLayout = Darklands.Localization.SlotLayout;
using SlotLayouts = Darklands.Localization.SlotLayouts;
using LayoutProfile = Darklands.Localization.LayoutProfile;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Darklands.Presentation;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// E-text, the translators' editor (W-0 + E-text, the approved "ux2" design): a sidebar of the pack's texts in sections (cards,
/// screens, names, not seen) and groups (a card, a screen, a record kind) with their progress, filtered (all, to do, needs review,
/// problems) and searched; the chosen text under its human title, its state and the review mark; a card text in rows (the intro,
/// its passages and each option, the English beside your words) or as written (Raw); a screen text with its width; the preview as
/// the game draws it beside a card text, under a screen text; the names to insert and "accept and next" at the foot.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class TextEditorView : IWorkshopEditor
{
    private readonly TextProject _project;

    // a screen text's layout in the pack (layout.json): width, lines, font, move; shown under its preview
    private readonly WrapPanel _layoutRow = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
    private bool _layoutLoading;
    private readonly ReviewMarks _marks;
    private readonly Glossary _glossary;
    private readonly TextPreview _preview;
    private readonly IReadOnlyList<(string Section, string Key, string Title, List<TextEntry> Entries)> _groups;
    private readonly Dictionary<TextEntry, string> _titles = [];

    // the sidebar
    private readonly TextBox _search = new() { Watermark = "Search texts", Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button[] _filters;
    private int _filter = 1;
    private readonly ListBox _list = new() { Background = Brushes.Transparent };
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    // the groups the translator folded (the current text's group among them: folded all the same)
    private readonly HashSet<string> _folded = new(StringComparer.Ordinal);
    private readonly TextBlock _progress = new() { FontSize = 12 };
    private readonly Border _sidebar = new();

    // the text
    private readonly TextBlock _title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
    private readonly TextBlock _meta = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _state = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _review = WorkshopTheme.Outline(new TextBlock());
    private readonly Border _viewSwitch = new() { CornerRadius = new CornerRadius(7), Padding = new Thickness(2) };
    private readonly Button _rowsButton = new();
    private readonly Button _rawButton = new();
    private readonly TextBlock _switchHint = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _content = new() { Spacing = 0 };
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SelectableTextBlock _english = new() { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
    private readonly TranslationBox _translation = new() { MinHeight = 64, MaxHeight = 260 };
    private readonly TextBlock _chipHint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    // a placeholder the game fills from other texts of the pack ($Money: the money words) while those are not translated yet: a note
    //   (not an error), with the way to them
    private readonly TextBlock _fillText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _fillGo = new() { Content = "Show them", FontSize = 12, Padding = new Thickness(8, 2) };
    private readonly DockPanel _fillHint = new() { IsVisible = false };
    private string? _fillFirst;

    /// <summary>The placeholders the game fills from other texts: the placeholder's start, the texts' keys, what they are called.</summary>
    internal static readonly (string Placeholder, string[] Keys, string What)[] FilledFrom =
    [
        ("$Money", Darklands.Core.GameTextIds.MoneyWords, "the money words (florins, groschen, pfennigs and how they are joined)"),
    ];

    /// <summary>The note beside the translation for a placeholder filled from texts not translated yet, when shown (the tests read it).</summary>
    internal string? FillNoteForTests => _fillHint.IsVisible ? _fillText.Text : null;
    private readonly TextBlock _terms = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly StackPanel _choice = new() { Spacing = 4 };

    // the preview
    private readonly Image _picture = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _barFill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _bar = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _measure = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly StackPanel _issues = new() { Spacing = 8 };
    private readonly StackPanel _previewPanel = new() { Spacing = 8 };
    private readonly Border _previewPane = new();
    private readonly Grid _root = new();
    private PreviewResult? _shown;

    private List<CardRow> _rows = [];
    private TranslationBox? _focusedRow;
    private bool _raw;
    private bool _stacked;
    private TextEntry? _current;
    private bool _loading;
    private bool _filling;

    public TextEditorView(TextProject project)
    {
        _project = project;
        _preview = new TextPreview(project);
        _glossary = Glossary.Load(project.Folder);
        _marks = ReviewMarks.Load(project.Folder);
        _fillText.Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted);
        _fillGo.Margin = new Thickness(8, 0, 0, 0);
        _fillGo.Click += (_, _) =>
        {
            if (_fillFirst is { } first)
            {
                Select(first);
            }
        };
        ToolTip.SetTip(_fillGo, "Go to the first of these texts not translated yet");
        DockPanel.SetDock(_fillGo, Dock.Right);
        _fillHint.Children.Add(_fillGo);
        _fillHint.Children.Add(_fillText);
        RenderOptions.SetBitmapInterpolationMode(_picture, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        _bar.Child = _barFill;
        // the groups: cards by their file, screens, names by kind, the rest; in the template's order within each
        string[] order = ["Cards", "Screens", "Names", "Not seen"];
        _groups = [.. project.Entries
            .GroupBy(e => (Section: TextTitles.Section(e), Group: TextTitles.Group(e)))
            .Select(g => (g.Key.Section, g.Key.Group.Key, g.Key.Group.Title, g.ToList()))
            .OrderBy(g => Array.IndexOf(order, g.Section)).ThenBy(g => g.Section == "Cards" ? g.Title : g.Key, StringComparer.Ordinal)];
        // All, To do, Done (the pack's translated texts), Review, Problems
        _filters = new[] { "All", "To do", "Done", "Review", "Problems" }.Select((label, i) =>
        {
            var b = new Button { Content = label, FontSize = 11.5, Padding = new Thickness(0, 3), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(b, label switch
            {
                "To do" => "Texts not translated yet",
                "Done" => "Texts this pack translates",
                "Review" => "Translated texts marked “needs review”",
                "Problems" => "Texts the last check found a problem in",
                _ => "Every text",
            });
            b.Click += (_, _) => SetFilter(i);
            return b;
        }).ToArray();
        _list.ItemTemplate = new FuncDataTemplate<SideItem>((item, _) => item is null ? new Panel() : Side(item));
        _list.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, 0.0) },
        });
        _search.TextChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) => Chosen();
        _translation.TextChanged += (_, _) =>
        {
            if (!_loading && _current is not null && _translation.Text != (_current.Translation ?? ""))
            {
                Written(_translation.Text);
            }
        };
        _rowsButton.Click += (_, _) => SetRaw(false);
        _rawButton.Click += (_, _) => SetRaw(true);
        _review.Click += (_, _) => ToggleReview();
        View = Build();
        _root.SizeChanged += (_, _) => Layout();
        // a pack with translations opens at its work (Done, its first translated text); a new one at To do
        if (project.Entries.Any(e => e.Translation is not null))
        {
            _filter = 2;
        }
        Fill();
        if (Visible().FirstOrDefault() is { } first)
        {
            Select(first.Id);
        }
    }

    public string Title => $"Texts ({_project.Language})";

    public Control View { get; }

    public bool Dirty => _project.Dirty || _marks.Dirty;

    public event Action? Changed;

    public void Save()
    {
        _project.Save();
        _glossary.Save(_project.Folder);
        _marks.Save(_project.Folder);
        Changed?.Invoke();
    }

    /// <summary>The key of the text shown (for "show in game": the inspector names it; null: none).</summary>
    public string? CurrentKey => _current?.Key;

    /// <summary>The text shown (null: none).</summary>
    public TextEntry? Current => _current;

    /// <summary>The pack's glossary (its window edits it).</summary>
    public Glossary Glossary => _glossary;

    /// <summary>The project (the variables' window edits its variables).</summary>
    public TextProject Project => _project;

    /// <summary>Shows the current text again (after the glossary or the variables changed).</summary>
    /// <summary>The pack's review marks as the editor keeps them (an import marks its texts here).</summary>
    internal ReviewMarks Marks => _marks;

    /// <summary>The list's items as shown (tests: a group's heading clicked, its texts listed or not).</summary>
    internal IReadOnlyList<SideItem> Listed => _list.ItemsSource as IReadOnlyList<SideItem> ?? [];

    /// <summary>Clicks an item of the list (tests).</summary>
    internal void Click(SideItem item) => _list.SelectedItem = item;

    /// <summary>The list's filter: 0 All, 1 To do, 2 Done, 3 Review, 4 Problems.</summary>
    internal int Filter => _filter;

    /// <summary>The chips under the text (tests).</summary>
    internal IEnumerable<Button> Chips => _chips.Children.OfType<Button>();

    /// <summary>A text's title as the list shows it (its first words).</summary>
    internal string ListTitle(TextEntry entry) => TitleOf(entry);

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

    /// <summary>Shows the text <paramref name="id"/> (a finding's link, or a key), clearing the filters when they hide it.</summary>
    public void Select(string id)
    {
        if ((_project.Find(id) ?? _project.Entries.FirstOrDefault(e => e.Key == id)) is not { } entry)
        {
            return;
        }
        if (!Matches(entry))
        {
            _filter = 0;
            _search.Text = "";
        }
        _open.Add(TextTitles.Group(entry).Key);
        _folded.Remove(TextTitles.Group(entry).Key);
        Fill();
        if (_list.ItemsSource is IEnumerable<SideItem> items && items.FirstOrDefault(i => i.Entry == entry) is { } item)
        {
            _list.SelectedItem = item;
            _list.ScrollIntoView(item);
        }
    }

    /// <summary>The keys of the editor (the window passes them on): true when one was taken.</summary>
    public bool Key(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Avalonia.Input.Key.Enter when ctrl:
                Accept();
                return true;
            case Avalonia.Input.Key.Down when ctrl:
                Next(e => e.State == TextState.Untranslated, 1);
                return true;
            case Avalonia.Input.Key.Up when ctrl:
                Next(e => e.State == TextState.Untranslated, -1);
                return true;
            case Avalonia.Input.Key.F8:
                Next(e => e.State == TextState.Problem, shift ? -1 : 1);
                return true;
            case Avalonia.Input.Key.F when ctrl:
                _search.Focus();
                _search.SelectAll();
                return true;
            case >= Avalonia.Input.Key.D1 and <= Avalonia.Input.Key.D9 when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                var names = _chips.Children.OfType<Button>().Where(b => b.IsVisible && b.Tag is string).ToList();
                var k = e.Key - Avalonia.Input.Key.D1;
                if (k < names.Count)
                {
                    names[k].Command?.Execute(null);
                    InsertChip((string)names[k].Tag!);
                }
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------- the layout
    private Control Build()
    {
        // the sidebar: search, filters, the list, the progress
        // five filters, each as wide as its word needs (Problems the widest)
        var segment = new Grid { ColumnDefinitions = new ColumnDefinitions("0.8*,1*,1*,1.25*,1.5*") };
        for (var i = 0; i < _filters.Length; i++)
        {
            Grid.SetColumn(_filters[i], i);
            segment.Children.Add(_filters[i]);
        }
        var head = new StackPanel
        {
            Margin = new Thickness(12, 12, 12, 6),
            Children =
            {
                new DockPanel { Children = { Keys(["Ctrl", "F"]), _search } },
                new Border { Background = WorkshopTheme.Brush(WorkshopTheme.Surface), CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Margin = new Thickness(0, 8, 0, 0), Child = segment },
            },
        };
        DockPanel.SetDock(((DockPanel)((StackPanel)head).Children[0]).Children[0], Dock.Right);
        var foot = new Border { Padding = new Thickness(12, 6), BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(0, 1, 0, 0), Child = _progress };
        _progress.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        var side = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        side.Children.Add(head);
        side.Children.Add(foot);
        side.Children.Add(_list);
        _sidebar.Child = side;
        _sidebar.Background = WorkshopTheme.Brush(WorkshopTheme.Side);
        _sidebar.BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line);
        _sidebar.BorderThickness = new Thickness(0, 0, 1, 0);

        // the text: header, switch, content, foot
        _review.Padding = new Thickness(10, 4);
        var titleBlock = new StackPanel { Children = { _title, _meta } };
        _meta.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        _meta.Margin = new Thickness(0, 4, 0, 0);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10, Margin = new Thickness(24, 16, 24, 8) };
        header.Children.Add(titleBlock);
        Grid.SetColumn(_state, 1);
        header.Children.Add(_state);
        Grid.SetColumn(_review, 2);
        header.Children.Add(_review);
        _review.VerticalAlignment = VerticalAlignment.Center;
        foreach (var (b, icon, words) in new[] { (_rowsButton, "list", "Rows"), (_rawButton, "code", "Raw") })
        {
            b.Content = WorkshopTheme.IconText(icon, words, 13);
            b.Padding = new Thickness(12, 3);
            b.FontSize = 12;
            b.CornerRadius = new CornerRadius(5);
            b.BorderThickness = new Thickness(0);
        }
        _viewSwitch.Background = WorkshopTheme.Brush(WorkshopTheme.Surface);
        _viewSwitch.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _rowsButton, _rawButton } };
        _switchHint.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        var switchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(24, 0, 24, 8), Children = { _viewSwitch, _switchHint } };
        var accept = WorkshopTheme.Ghost(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { new TextBlock { Text = "Accept and next", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, WorkshopTheme.Key("Ctrl"), WorkshopTheme.Key("Enter") },
        }, "Mark the text done and go to the next one to do");
        accept.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        accept.Click += (_, _) => Accept();
        var footer = new DockPanel { Margin = new Thickness(24, 8) };
        DockPanel.SetDock(accept, Dock.Right);
        footer.Children.Add(accept);
        footer.Children.Add(new DockPanel
        {
            Children = { new TextBlock { Text = "Insert", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) }, _chips },
        });
        var foot2 = new Border { Child = footer, BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(0, 1, 0, 0) };
        var centre = new DockPanel();
        var top = new StackPanel { Children = { header, switchRow } };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(foot2, Dock.Bottom);
        centre.Children.Add(top);
        centre.Children.Add(foot2);
        centre.Children.Add(new ScrollViewer { Content = new Border { Padding = new Thickness(24, 0, 24, 16), Child = _content } });

        // the preview
        var frame = new Border { Child = _picture, CornerRadius = new CornerRadius(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
        _bar.Background = WorkshopTheme.Brush(WorkshopTheme.Line);
        _measure.Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted);
        _previewPanel.Children.Add(new DockPanel
        {
            Children =
            {
                WorkshopIcons.Make("eye", 14, WorkshopTheme.Brush(WorkshopTheme.Muted)),
                new TextBlock { Text = "Preview", FontWeight = FontWeight.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "as the game draws it", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center },
            },
        });
        _previewPanel.Children.Add(frame);
        _previewPanel.Children.Add(_bar);
        _previewPanel.Children.Add(_measure);
        _previewPanel.Children.Add(_layoutRow);
        _previewPanel.Children.Add(_issues);
        _previewPane.Background = WorkshopTheme.Brush(WorkshopTheme.Side);
        _previewPane.BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line);
        _previewPane.BorderThickness = new Thickness(1, 0, 0, 0);
        _previewPane.Padding = new Thickness(16);

        _root.ColumnDefinitions = new ColumnDefinitions("296,*,330");
        _root.Children.Add(_sidebar);
        Grid.SetColumn(centre, 1);
        _root.Children.Add(centre);
        Grid.SetColumn(_previewPane, 2);
        _root.Children.Add(_previewPane);
        return _root;
    }

    private static Control Keys(string[] keys)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in keys)
        {
            panel.Children.Add(WorkshopTheme.Key(k));
        }
        return panel;
    }

    // the widths: a wider sidebar and a larger preview on a large window
    private void Layout()
    {
        // the card's preview pane holds the card's window at twice the game's pixels at least (readable at 1280 x 800)
        var width = _root.Bounds.Width;
        var wide = width >= 1600;
        var side = wide ? 340 : 264;
        var twice = _shown is { CardText: true } shown ? shown.Width * 2 + 34 : 0;
        var pane = _previewPane.IsVisible ? Math.Max(wide ? 580 : 552, twice) : 0;
        _root.ColumnDefinitions[0].Width = new GridLength(side);
        _root.ColumnDefinitions[2].Width = new GridLength(pane);
        var stacked = pane > 0 && width - side - pane < 760;
        if (stacked != _stacked)
        {
            _stacked = stacked;
            if (_current is not null && _current.Key.StartsWith("msg:", StringComparison.Ordinal) && !_raw)
            {
                ShowContent();
            }
        }
        Place();
    }

    // ---------------------------------------------------------------- the sidebar
    internal sealed record SideItem(int Kind, string Title, string Detail, TextEntry? Entry, string Group, int Done, int Total, bool Open);

    private void SetFilter(int filter)
    {
        _filter = filter;
        Fill();
    }

    private TextState StateOf(TextEntry e) => e.State;

    private bool InReview(TextEntry e) => e.Translation is not null && _marks.Has(e.Key);

    private bool Matches(TextEntry e)
    {
        var ok = _filter switch
        {
            1 => e.State == TextState.Untranslated,
            2 => e.Translation is not null,
            3 => InReview(e),
            4 => e.State == TextState.Problem,
            _ => true,
        };
        if (ok && _search.Text is { Length: > 0 } q)
        {
            ok = e.English.Contains(q, StringComparison.OrdinalIgnoreCase) || (e.Translation?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || e.Key.Contains(q, StringComparison.OrdinalIgnoreCase) || TitleOf(e).Contains(q, StringComparison.OrdinalIgnoreCase);
        }
        return ok;
    }

    private IEnumerable<TextEntry> Visible() => _groups.SelectMany(g => g.Entries).Where(Matches);

    private string TitleOf(TextEntry e) => Plain(MarkedTitleOf(e));

    // the title with its hot key marked (drawn coloured)
    private string MarkedTitleOf(TextEntry e)
    {
        if (!_titles.TryGetValue(e, out var title))
        {
            title = _titles[e] = TextTitles.Marked(e.English);
        }
        return title;
    }

    private static string Plain(string marked) =>
        marked.Replace(TextTitles.HotStart.ToString(), "", StringComparison.Ordinal).Replace(TextTitles.HotEnd.ToString(), "", StringComparison.Ordinal);

    // a marked title's runs: the hot key coloured and underlined, as the game draws it
    private static IEnumerable<Avalonia.Controls.Documents.Run> HotRuns(string marked)
    {
        var parts = marked.Split(TextTitles.HotStart);
        if (parts[0].Length > 0)
        {
            yield return new Avalonia.Controls.Documents.Run(parts[0]);
        }
        foreach (var part in parts.Skip(1))
        {
            var end = part.IndexOf(TextTitles.HotEnd, StringComparison.Ordinal);
            var hot = end < 0 ? part : part[..end];
            yield return new Avalonia.Controls.Documents.Run(hot) { Foreground = WorkshopTheme.Brush(WorkshopTheme.ChipColour), TextDecorations = TextDecorations.Underline };
            if (end >= 0 && end + 1 < part.Length)
            {
                yield return new Avalonia.Controls.Documents.Run(part[(end + 1)..]);
            }
        }
    }

    private void Fill()
    {
        _filling = true;
        for (var i = 0; i < _filters.Length; i++)
        {
            _filters[i].Background = i == _filter ? WorkshopTheme.Brush(WorkshopTheme.Raised) : Brushes.Transparent;
            _filters[i].Foreground = WorkshopTheme.Brush(i == _filter ? WorkshopTheme.Text : WorkshopTheme.Muted);
        }
        var searching = (_search.Text ?? "").Length > 0;
        var items = new List<SideItem>();
        foreach (var section in _groups.GroupBy(g => g.Section))
        {
            var all = section.SelectMany(g => g.Entries).ToList();
            var shownGroups = section.Select(g => (Group: g, Shown: g.Entries.Where(Matches).ToList())).Where(g => g.Shown.Count > 0).ToList();
            if (shownGroups.Count == 0)
            {
                continue;
            }
            items.Add(new SideItem(0, section.Key.ToUpperInvariant(), $"{Count(all.Count(e => e.Translation is not null))} of {Count(all.Count)}", null, "", 0, 0, false));
            foreach (var (group, shown) in shownGroups)
            {
                var open = !_folded.Contains(group.Key) && (_open.Contains(group.Key) || searching || _current is not null && group.Entries.Contains(_current));
                items.Add(new SideItem(1, group.Title, "", null, group.Key, group.Entries.Count(e => e.Translation is not null), group.Entries.Count, open));
                if (open)
                {
                    foreach (var e in shown)
                    {
                        var hash = e.Key.IndexOf('#', StringComparison.Ordinal);
                        items.Add(new SideItem(2, MarkedTitleOf(e), e.Key.StartsWith("msg:", StringComparison.Ordinal) ? "#" + e.Key[(hash + 1)..] : "", e, group.Key, 0, 0, false));
                    }
                }
            }
        }
        _list.ItemsSource = items;
        // the current text chosen in the list; none when its group is folded (so its heading can be clicked again)
        _list.SelectedItem = _current is not null && items.FirstOrDefault(i => i.Entry == _current) is { } item ? item : null;
        _filling = false;
        UpdateProgress();
    }

    private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    private void UpdateProgress()
    {
        var (done, every) = _project.Progress();
        _progress.Text = $"{Count(done)} of {Count(every)} translated · {Count(Visible().Count())} shown";
    }

    private void Chosen()
    {
        if (_filling)
        {
            return;
        }
        switch (_list.SelectedItem)
        {
            case SideItem { Kind: 2, Entry: { } entry }:
                Show(entry);
                break;
            case SideItem { Kind: 1 } group:
                // folds an open group (the current text's too), opens a folded one
                if (group.Open)
                {
                    _open.Remove(group.Group);
                    _folded.Add(group.Group);
                }
                else
                {
                    _folded.Remove(group.Group);
                    _open.Add(group.Group);
                }
                Fill();
                break;
        }
    }

    private Control Side(SideItem item)
    {
        var faint = WorkshopTheme.Brush(WorkshopTheme.Faint);
        switch (item.Kind)
        {
            case 0:
                var total = new TextBlock { Text = item.Detail, FontSize = 11.5, Foreground = faint };
                DockPanel.SetDock(total, Dock.Right);
                return new DockPanel
                {
                    Margin = new Thickness(12, 10, 12, 4),
                    Children =
                    {
                        total,
                        new TextBlock { Text = item.Title, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) },
                    },
                };
            case 1:
                var share = item.Total == 0 ? 0 : (double)item.Done / item.Total;
                var row = new DockPanel { Margin = new Thickness(8, 5, 12, 5) };
                var count = new TextBlock { Text = $"{item.Done}/{item.Total}", FontSize = 11.5, Foreground = faint, MinWidth = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var bar = new Border
                {
                    Width = 44,
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = WorkshopTheme.Brush(WorkshopTheme.Line),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Child = new Border { Width = 44 * share, CornerRadius = new CornerRadius(2), Background = WorkshopTheme.Brush(WorkshopTheme.Accent), HorizontalAlignment = HorizontalAlignment.Left },
                };
                DockPanel.SetDock(count, Dock.Right);
                DockPanel.SetDock(bar, Dock.Right);
                row.Children.Add(count);
                row.Children.Add(bar);
                row.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        WorkshopIcons.Make(item.Open ? "chevron-down" : "chevron-right", 14, WorkshopTheme.Brush(WorkshopTheme.Muted)),
                        new TextBlock { Text = item.Title, FontSize = 12.5, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center },
                    },
                });
                return row;
            default:
                var entry = item.Entry!;
                var line = new DockPanel { Margin = new Thickness(28, 5, 12, 5) };
                var detail = new TextBlock { Text = item.Detail, FontSize = 11, Foreground = faint, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(detail, Dock.Right);
                line.Children.Add(detail);
                var dot = Dot(entry);
                ToolTip.SetTip(dot, StateWords(entry));
                line.Children.Add(dot);
                line.Children.Add(new TextBlock
                {
                    Inlines = [.. HotRuns(item.Title)],
                    Margin = new Thickness(10, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = WorkshopTheme.Brush(entry.Translation is null ? WorkshopTheme.Muted : WorkshopTheme.Text),
                });
                ToolTip.SetTip(line, entry.Key);
                return line;
        }
    }

    // a text's mark: a ring to do, a green dot done, an amber one to review, a red one with a problem
    private Control Dot(TextEntry e)
    {
        var colour = e.State == TextState.Problem ? WorkshopTheme.Bad : InReview(e) ? WorkshopTheme.Warn : e.Translation is null ? (Color?)null : WorkshopTheme.Ok;
        return new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = colour is { } c ? WorkshopTheme.Brush(c) : Brushes.Transparent,
            Stroke = colour is null ? WorkshopTheme.Brush(WorkshopTheme.Faint) : null,
            StrokeThickness = 1.5,
        };
    }

    private string StateWords(TextEntry e) => e.State == TextState.Problem ? "Problem" : InReview(e) ? "Needs review" : e.Translation is null ? "To do" : "Translated";

    private void Next(Func<TextEntry, bool> wanted, int step)
    {
        var all = _groups.SelectMany(g => g.Entries).ToList();
        var at = _current is null ? -1 : all.IndexOf(_current);
        for (var k = 1; k <= all.Count; k++)
        {
            var e = all[((at + step * k) % all.Count + all.Count) % all.Count];
            if (wanted(e))
            {
                Select(e.Id);
                return;
            }
        }
    }

    // the text done: its review mark off, then the next one to do
    private void Accept()
    {
        if (_current is not null && _current.Translation is not null)
        {
            _marks.Set(_current.Key, false);
            Changed?.Invoke();
        }
        Next(e => e.State == TextState.Untranslated, 1);
    }

    private void ToggleReview()
    {
        if (_current is null || _current.Translation is null)
        {
            return;
        }
        _marks.Set(_current.Key, !_marks.Has(_current.Key));
        Changed?.Invoke();
        ShowState();
        Fill();
    }

    // ---------------------------------------------------------------- the text
    private void Show(TextEntry? entry)
    {
        _current = entry;
        _focusedRow = null;
        if (entry is null)
        {
            _content.Children.Clear();
            _title.Text = "No text chosen";
            _meta.Text = "Choose a text in the list.";
            return;
        }
        var (_, group) = TextTitles.Group(entry);
        var card = entry.Key.StartsWith("msg:", StringComparison.Ordinal);
        _title.Inlines = card
            ? new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run(group),
                new Avalonia.Controls.Documents.Run(" · " + TitleOf(entry)) { FontWeight = FontWeight.Normal, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) },
            }
            : [.. HotRuns(MarkedTitleOf(entry))];
        _meta.Inlines = new Avalonia.Controls.Documents.InlineCollection
        {
            new Avalonia.Controls.Documents.Run((card ? "Card text" : TextTitles.Section(entry) == "Names" ? group + " · a name" : group) + " · "),
            new Avalonia.Controls.Documents.Run(entry.Key) { FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace") },
            new Avalonia.Controls.Documents.Run(Rest(entry) is { Length: > 0 } rest ? " · " + rest : ""),
        };
        ToolTip.SetTip(_meta, string.Join("\n", entry.Template.Comments.Append("Shown on: " + string.Join(", ", entry.Screens))));
        ShowState();
        // a screen text's switch: its hot key as the game draws it, or its tags (Raw)
        var hot = !card && (HotKeyTag.IsMatch(entry.English) || HotKeyTag.IsMatch(entry.Translation ?? ""));
        _viewSwitch.IsVisible = card || hot;
        _switchHint.IsVisible = card || hot;
        _rowsButton.Content = WorkshopTheme.IconText(card ? "list" : "type", card ? "Rows" : "Text", 13);
        ShowChips();
        ShowContent();
        ShowPreview();
        Layout();
    }

    private void ShowState()
    {
        if (_current is null)
        {
            return;
        }
        var words = StateWords(_current);
        var (fg, bg) = words switch
        {
            "Problem" => ("#ef8a83", Color.FromArgb(0x26, 0xE0, 0x56, 0x4E)),
            "Needs review" => ("#e6b766", Color.FromArgb(0x26, 0xD9, 0xA0, 0x3F)),
            "Translated" => ("#7cc493", Color.FromArgb(0x26, 0x4F, 0xA4, 0x6A)),
            _ => ("#a0a2a8", WorkshopTheme.Raised),
        };
        _state.Background = WorkshopTheme.Brush(bg);
        _state.Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { Dot(_current), new TextBlock { Text = words, FontSize = 11.5, FontWeight = FontWeight.Medium, Foreground = WorkshopTheme.Brush(Color.Parse(fg)) } },
        };
        _review.IsVisible = _current.Translation is not null;
        _review.Content = WorkshopTheme.IconText(InReview(_current) ? "check" : "eye", InReview(_current) ? "Mark reviewed" : "Needs review", 14);
        ToolTip.SetTip(_review, InReview(_current) ? "The translation is checked: take the mark off" : "Mark the translation to look at again (kept in the pack's workshop.json)");
    }

    private void SetRaw(bool raw)
    {
        _raw = raw;
        Show(_current);
    }

    // the card text's rows (English beside your words), or the English above your words as written
    private void ShowContent()
    {
        _content.Children.Clear();
        var entry = _current!;
        var card = entry.Key.StartsWith("msg:", StringComparison.Ordinal);
        IReadOnlyList<CardRow>? rows = null;
        IReadOnlyList<CardRow> english = [];
        string? note = null;
        if (card && !_raw)
        {
            english = CardRows.Parse(entry.English);
            rows = CardRows.Against(english, entry.Translation);
            note = rows is null ? "Your text's lines are not the English's (another number of options or passages): it is shown as written." : null;
        }
        var rowsMode = rows is not null;
        Segment(_rowsButton, card ? !_raw && rowsMode : !_raw);
        Segment(_rawButton, card ? !rowsMode : _raw);
        _switchHint.Text = rowsMode ? "Line breaks, “…” and indents are added for you."
            : !card && !_raw ? "The hot key is drawn as the game draws it; its colour tags are kept (Raw shows them)."
            : note ?? "The text as the pack's file holds it, its tags included.";
        _translation.HotKeys = !card && !_raw;
        _switchHint.Foreground = WorkshopTheme.Brush(note is null ? WorkshopTheme.Faint : WorkshopTheme.Warn);
        foreach (var chip in _chips.Children.OfType<Button>())
        {
            chip.IsVisible = !(rowsMode && chip.Tag is string text && text.StartsWith('<'));
        }
        var language = Languages.Of(_project.Language)?.Native ?? _project.Language;
        if (rowsMode)
        {
            _rows = [.. rows!];
            // side by side (English left, yours right); stacked (yours under the English) when the column is narrow beside the preview
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(_stacked ? "24,*" : "24,*,*"), ColumnSpacing = 12, RowSpacing = 8 };
            var yours = _stacked ? 1 : 2;
            var span = _stacked ? 1 : 2;
            var r = 0;
            void Add(Control c, int column, int span = 1)
            {
                Grid.SetRow(c, r);
                Grid.SetColumn(c, column);
                Grid.SetColumnSpan(c, span);
                grid.Children.Add(c);
            }
            void NextRow() => grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            NextRow();
            if (_stacked)
            {
                Add(new TextBlock { Text = "English, your " + language + " under each line", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) }, 1);
            }
            else
            {
                Add(new TextBlock { Text = "English", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) }, 1);
                Add(new TextBlock { Text = language, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) }, 2);
            }
            var option = 0;
            var lastKind = (CardRowKind?)null;
            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var en = english[i];
                var index = i;
                var section = row.Kind switch
                {
                    CardRowKind.Option => "Options",
                    CardRowKind.Passage => "Only when it applies · " + row.Label,
                    _ => "Introduction",
                };
                if (row.Kind != lastKind || row.Kind == CardRowKind.Passage)
                {
                    r++;
                    NextRow();
                    Add(new TextBlock { Text = section, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), Margin = new Thickness(0, 6, 0, -2) }, 1, span);
                }
                lastKind = row.Kind;
                r++;
                NextRow();
                if (row.Kind == CardRowKind.Option)
                {
                    Add(new TextBlock { Text = (++option).ToString(CultureInfo.InvariantCulture), FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) }, 0);
                }
                var englishText = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) };
                var lead = en.Prefix.Contains("...", StringComparison.Ordinal) ? "… " : "";
                Paint(englishText, lead + (en.Field ?? CardRow.Unwrapped(en.Content)), hotKeys: true);
                var englishCell = new StackPanel { Orientation = Orientation.Horizontal };
                var englishBox = new Border
                {
                    Padding = new Thickness(10, 7),
                    CornerRadius = new CornerRadius(6),
                    Background = WorkshopTheme.Brush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)),
                    Child = row.Kind == CardRowKind.Option && row.Label.Length > 0
                        ? new DockPanel { Children = { Badge(row.Label), englishText } }
                        : englishText,
                };
                if (englishBox.Child is DockPanel withBadge)
                {
                    DockPanel.SetDock(withBadge.Children[0], Dock.Right);
                }
                Add(englishBox, 1);
                if (_stacked)
                {
                    r++;
                    NextRow();
                }
                var box = new TranslationBox { MinHeight = row.Kind == CardRowKind.Option ? 34 : 64, MaxHeight = 260, HotKeys = true };
                _loading = true;
                box.Text = row.Field ?? CardRow.Unwrapped(row.Content);
                _loading = false;
                ToolTip.SetTip(box, row.Field is null ? "This row has tags the fields do not show as lines: it is written as the file holds it." : null);
                // the row the translator is typing in (a chip goes there); the editor's text area marks GotFocus handled
                box.AddHandler(InputElement.GotFocusEvent, (_, _) => _focusedRow = box, RoutingStrategies.Bubble, handledEventsToo: true);
                box.TextChanged += (_, _) =>
                {
                    // the box's words unchanged (it was only given the cursor): nothing is written
                    if (_loading || _current is null || box.Text == (_rows[index].Field ?? CardRow.Unwrapped(_rows[index].Content)))
                    {
                        return;
                    }
                    _rows[index] = _rows[index].Field is not null || _rows[index].Content.Length == 0 ? _rows[index].WithField(box.Text) : _rows[index] with { Content = box.Text };
                    var written = CardRows.Write(_rows);
                    _loading = true;
                    _translation.Text = written;
                    _loading = false;
                    Written(written);
                };
                Add(box, yours);
            }
            _content.Children.Add(grid);
        }
        else
        {
            Paint(_english, entry.English.TrimStart('\n').Replace("\n    ", "\n", StringComparison.Ordinal).TrimStart(' '), hotKeys: !card && !_raw);
            _english.Foreground = WorkshopTheme.Brush(WorkshopTheme.Text);
            _loading = true;
            _translation.Text = entry.Translation ?? "";
            _loading = false;
            _translation.Editable = true;
            _content.Children.Add(Label("English"));
            _content.Children.Add(new Border { Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(6), Background = WorkshopTheme.Brush(Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)), Child = Detach(_english) });
            _content.Children.Add(Label(language));
            _content.Children.Add(Detach(_translation));
        }
        _content.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0), Children = { Detach(_chipHint), Detach(_fillHint), Detach(_terms), Detach(_choice) } });
        if (!card)
        {
            _content.Children.Add(new Border { Margin = new Thickness(0, 16, 0, 0), Child = Detach(_previewPanel) });
        }
        else
        {
            _previewPane.Child = Detach(_previewPanel);
        }
        _previewPane.IsVisible = card;
        ShowHints();
    }

    private static Control Detach(Control c)
    {
        switch (c.Parent)
        {
            case Panel p:
                p.Children.Remove(c);
                break;
            case Decorator d:
                d.Child = null;
                break;
        }
        return c;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), Margin = new Thickness(0, 12, 0, 6) };

    private static Control Badge(string kind) => new Border
    {
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(6, 0),
        Margin = new Thickness(8, 1, 0, 0),
        VerticalAlignment = VerticalAlignment.Top,
        Background = WorkshopTheme.Brush(Color.FromArgb(0x29, 0x9B, 0x6E, 0xD2)),
        Child = new TextBlock { Text = kind, FontSize = 11, Foreground = WorkshopTheme.Brush(Color.Parse("#c7a6e8")) },
    };

    private static void Segment(Button button, bool on)
    {
        button.Background = on ? WorkshopTheme.Brush(WorkshopTheme.Raised) : Brushes.Transparent;
        button.Foreground = WorkshopTheme.Brush(on ? WorkshopTheme.Text : WorkshopTheme.Muted);
    }

    // the translation written (from a row or the raw box): into the project, then the counters, the preview and the hints
    private void Written(string written)
    {
        _project.Set(_current!, written);
        Changed?.Invoke();
        UpdateProgress();
        ShowState();
        ShowPreview();
        ShowHints();
    }

    // the English's names and arguments (and tags, for the raw view), each a button that puts it where the cursor is
    private void ShowChips()
    {
        _chips.Children.Clear();
        if (_current is null)
        {
            return;
        }
        var chips = TextTokens.Split(_current.English).Where(t => t.Kind != TokenKind.Words && !t.Text.StartsWith("{\"", StringComparison.Ordinal))
            .DistinctBy(t => t.Text, StringComparer.Ordinal).ToList();
        foreach (var token in chips)
        {
            _chips.Children.Add(Chip(token));
        }
        // the pack's own variables ($his1 ...): behind one chip, its menu theirs (not a row of chips on every card)
        if (_current.Key.StartsWith("msg:", StringComparison.Ordinal)
            && _project.Variables.Select(v => "$" + v.Name).Where(v => chips.All(c => c.Text != v)).ToList() is { Count: > 0 } variables)
        {
            var panel = new WrapPanel { MaxWidth = 360, Margin = new Thickness(4) };
            var menu = new Flyout { Content = panel, Placement = PlacementMode.TopEdgeAlignedLeft };
            foreach (var v in variables)
            {
                var chip = Chip(new TextToken(TokenKind.Name, v));
                chip.Margin = new Thickness(0, 0, 6, 6);
                chip.Click += (_, _) => menu.Hide();
                panel.Children.Add(chip);
            }
            var more = new Button
            {
                Content = $"Pack variables ({variables.Count}) ▾",
                Padding = new Thickness(7, 1),
                Margin = new Thickness(0, 0, 6, 0),
                FontSize = 12,
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line2),
                Background = WorkshopTheme.Brush(WorkshopTheme.Raised),
                Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted),
                Flyout = menu,
            };
            ToolTip.SetTip(more, "The pack's own variables (variables.json): pick one to put it where the cursor is");
            _chips.Children.Add(more);
        }
    }

    // a chip: a name, argument or tag of the English that a click puts where the cursor is
    private Button Chip(TextToken token)
    {
        var chip = token.Text;
        var button = new Button
        {
            Content = chip,
            Tag = chip,
            Padding = new Thickness(7, 1),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 12,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line2),
            Background = WorkshopTheme.Brush(WorkshopTheme.Raised),
            Foreground = WorkshopTheme.Brush(WorkshopTheme.ChipColourOf(token) ?? WorkshopTheme.Text),
            // a click leaves the cursor in the field it puts the chip into
            Focusable = false,
        };
        ToolTip.SetTip(button, "Put it where the cursor is");
        button.Click += (_, _) => InsertChip(chip);
        return button;
    }

    private void InsertChip(string chip)
    {
        var target = _focusedRow ?? _translation;
        var text = target.Text ?? "";
        var at = Math.Clamp(target.CaretIndex, 0, text.Length);
        target.Text = text.Insert(at, chip);
        target.CaretIndex = at + chip.Length;
        target.FocusText();
    }

    // as the translator types: the chips left out or unknown, the glossary's terms, and a choice's variants as fields
    private void ShowHints()
    {
        _chipHint.Foreground = WorkshopTheme.Brush(WorkshopTheme.Warn);
        _terms.Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted);
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
        _chipHint.IsVisible = _chipHint.Text.Length > 0;
        ShowFillHint();
        var terms = _glossary.In(_current.English);
        var missed = _current.Translation is null ? [] : _glossary.Missed(_current.English, _current.Translation);
        _terms.Text = terms.Count == 0 ? "" : "Glossary: " + string.Join(";  ", terms.Select(t => $"{t.Term} → {t.Translation}" + (missed.Contains(t) ? " (not used)" : "")));
        _terms.IsVisible = _terms.Text.Length > 0;
        ShowChoice();
    }

    // a translated text with a placeholder the game fills from other texts of the pack that are not translated yet ($Money: the money
    //   words): said beside it as a note, with a button to the first of them
    private void ShowFillHint()
    {
        _fillFirst = null;
        if (_current?.Translation is { } translation)
        {
            foreach (var (placeholder, keys, what) in FilledFrom)
            {
                if (!translation.Contains(placeholder, StringComparison.Ordinal) && !_current.English.Contains(placeholder, StringComparison.Ordinal))
                {
                    continue;
                }
                var untranslated = keys.Select(k => _project.Entries.FirstOrDefault(e => e.Key == k)).Where(e => e is { Translation: null }).ToList();
                if (untranslated.Count > 0)
                {
                    _fillFirst = untranslated[0]!.Key;
                    _fillText.Text = $"{placeholder}… fills in an amount from texts you haven't translated yet: {what}. Until then the game writes it in English.";
                    break;
                }
            }
        }
        _fillHint.IsVisible = _fillFirst is not null;
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

    // ---------------------------------------------------------------- the preview
    private void ShowPreview()
    {
        _issues.Children.Clear();
        if (_current is null)
        {
            _picture.Source = null;
            _shown = null;
            _measure.Text = "";
            _barFill.Width = 0;
            return;
        }
        var result = _preview.Render(_current, _current.Translation);
        // its pixels (a TrueType font's text at a higher resolution: Scale output pixels a game pixel)
        var (pw, ph) = (result.Width * result.Scale, result.Height * result.Scale);
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(pw, ph), new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (var frame = bitmap.Lock())
        {
            for (var row = 0; row < ph; row++)
            {
                var line = Array.ConvertAll(result.Argb[(row * pw)..((row + 1) * pw)], v => unchecked((int)v));
                System.Runtime.InteropServices.Marshal.Copy(line, 0, frame.Address + row * frame.RowBytes, pw);
            }
        }
        _picture.Source = bitmap;
        _shown = result;
        Place();
        Color state;
        if (result.CardText)
        {
            var spare = result.FootLimit - result.Foot;
            _barFill.Width = 0;
            // the English of a text not translated yet, in the pack's fonts: not the pack's fault, and not what will be measured
            var english = result.Untranslated;
            // a description's column (1.1.3) or the card's window
            var where = result.Column ?? "the card's window";
            state = result.Overflows && !english ? WorkshopTheme.Bad : result.Overflows || result.Pages > 1 ? WorkshopTheme.Warn : WorkshopTheme.Ok;
            _bar.Background = WorkshopTheme.Brush(state);
            _measure.Text = result.Overflows && english
                ? $"Not translated yet: the English, drawn in this pack's font, runs {result.Foot - result.FootLimit + 1} px past the foot of {where}. Your translation is what is measured."
                : result.Overflows
                ? $"Runs past the foot of {where} by {result.Foot - result.FootLimit + 1} px" + (result.Column is null ? " (every passage shown)." : ".")
                : result.Pages > 1
                    ? $"Too long for one window: the game shows it in {result.Pages} pages (every passage shown)."
                    : result.Column is { } column
                    ? $"Fits {column} · {spare} px to spare."
                    : $"Fits · {spare} px to spare (every passage shown).";
        }
        else if (result.Room > 0)
        {
            _bar.Background = WorkshopTheme.Brush(WorkshopTheme.Line);
            var full = Math.Max(result.Room, result.Measured);
            _barFill.Width = _bar.Width * result.Measured / full;
            state = result.Overflows ? WorkshopTheme.Bad : result.Measured > result.English ? WorkshopTheme.Warn : WorkshopTheme.Ok;
            _barFill.Background = WorkshopTheme.Brush(state);
            _measure.Text = $"{result.Measured} of {result.Room} px · English {result.English} px" + (result.Overflows ? " · too wide" : "");
        }
        else
        {
            _barFill.Width = 0;
            _bar.Background = WorkshopTheme.Brush(WorkshopTheme.Line);
            _measure.Text = $"{result.Measured} px wide; its box is not known (no recorded session showed it).";
        }
        ShowLayout(result);
        if (result.Problem is { } problem)
        {
            _issues.Children.Add(Issue(problem, WorkshopTheme.Warn));
        }
        foreach (var p in _current.Problems.Where(p => p.Severity != LanguagePackCheck.Severity.Note))
        {
            _issues.Children.Add(Issue(p.Message, p.Severity == LanguagePackCheck.Severity.Error ? WorkshopTheme.Bad : WorkshopTheme.Warn));
        }
    }

    private static Control Issue(string text, Color colour) => new Border
    {
        Padding = new Thickness(10, 8),
        CornerRadius = new CornerRadius(8),
        Background = WorkshopTheme.Brush(Color.FromArgb(0x1A, colour.R, colour.G, colour.B)),
        BorderBrush = WorkshopTheme.Brush(Color.FromArgb(0x48, colour.R, colour.G, colour.B)),
        BorderThickness = new Thickness(1),
        Child = new DockPanel
        {
            Children =
            {
                WorkshopIcons.Make("alert", 14, WorkshopTheme.Brush(colour)),
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(8, 0, 0, 0), Foreground = WorkshopTheme.Brush(colour) },
            },
        },
    };

    // the preview's size: whole multiples of the game's pixels, as large as its place allows (the pane beside a card text, the
    // text's width under a screen text)
    private void Place()
    {
        if (_shown is not { } shown)
        {
            return;
        }
        var room = shown.CardText
            ? (_root.ColumnDefinitions[2].Width.Value > 0 ? _root.ColumnDefinitions[2].Width.Value : 552) - 32
            : Math.Max(300, _root.Bounds.Width - _root.ColumnDefinitions[0].Width.Value - 48);
        // a description's column (1.1.3) is tall: at most twice its size, so its foot and the bar stay in view
        var scale = (int)Math.Clamp(room / shown.Width, 1, shown.Column is null ? 4 : 2);
        _picture.Width = shown.Width * scale;
        _picture.Height = shown.Height * scale;
        _bar.Width = shown.CardText ? _picture.Width : Math.Min(room, shown.Width * scale);
        _measure.MaxWidth = Math.Max(_bar.Width, 280);
        if (!shown.CardText && shown.Room > 0)
        {
            _barFill.Width = _bar.Width * shown.Measured / Math.Max(shown.Room, shown.Measured);
        }
    }

    // a text in the reader's box: words in the text's colour, each tag, name and argument in its chip's colour
    private static readonly System.Text.RegularExpressions.Regex HotKeyTag = new(@"<c\d+>\s*([^<\s][^<\n]*?)</c>", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void Paint(SelectableTextBlock block, string text, bool hotKeys = false)
    {
        var inlines = block.Inlines ??= [];
        inlines.Clear();
        if (hotKeys)
        {
            // a hot key as the game draws it: the letter coloured (underlined), its tags left out
            var at = 0;
            foreach (System.Text.RegularExpressions.Match m in HotKeyTag.Matches(text))
            {
                PaintTokens(inlines, text[at..m.Index]);
                inlines.Add(new Avalonia.Controls.Documents.Run(m.Groups[1].Value) { Foreground = WorkshopTheme.Brush(WorkshopTheme.ChipColour), TextDecorations = TextDecorations.Underline });
                at = m.Index + m.Length;
            }
            PaintTokens(inlines, text[at..]);
            return;
        }
        PaintTokens(inlines, text);
    }

    private static void PaintTokens(Avalonia.Controls.Documents.InlineCollection inlines, string text)
    {
        foreach (var token in TextTokens.Split(text))
        {
            var run = new Avalonia.Controls.Documents.Run(token.Text);
            if (WorkshopTheme.ChipColourOf(token) is { } colour)
            {
                run.Foreground = WorkshopTheme.Brush(colour);
            }
            inlines.Add(run);
        }
    }

    // the layout row of a screen text: its width (the room it has, or the pack's), lines 1–3, font (its own or a slot), a move, and Reset;
    // each change goes into the pack's layout.json (on Save) and the preview draws it at once
    private void ShowLayout(PreviewResult result)
    {
        _layoutRow.Children.Clear();
        _layoutRow.IsVisible = _current is { } entry && !result.CardText && result.Room > 0 && !entry.Key.StartsWith("msg:", StringComparison.Ordinal);
        if (!_layoutRow.IsVisible)
        {
            return;
        }
        var entry2 = _current!;
        var layout = _project.LayoutOf(entry2) ?? new SlotLayout();
        // its place as the original draws it (recorded sessions): a move is kept as a place (the format's), shown as a move from there
        var box = _project.Boxes.FirstOrDefault(b => b.Key == entry2.Key);
        var placed = box is { X: not null, Y: not null };
        _layoutLoading = true;
        Control Field(string label, Control input)
        {
            ToolTip.SetTip(input, label);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(0, 0, 10, 4),
                Children = { new TextBlock { Text = label, FontSize = 11.5, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), VerticalAlignment = VerticalAlignment.Center }, input },
            };
        }
        NumericUpDown Number(decimal value, decimal min, decimal max) =>
            new() { Value = value, Minimum = min, Maximum = max, Increment = 1, FormatString = "0", Width = 118, FontSize = 12, Padding = new Thickness(6, 0), ButtonSpinnerLocation = Location.Left };
        var width = Number(layout.Width ?? result.Room, 1, SlotLayouts.ScreenWidth);
        var lines = new ComboBox { ItemsSource = new[] { "1", "2", "3" }, SelectedIndex = Math.Clamp(layout.Lines, 1, 3) - 1, FontSize = 12, MinWidth = 52 };
        var font = new ComboBox { ItemsSource = new[] { "its own", "slot 1", "slot 2", "slot 3" }, SelectedIndex = Math.Clamp(layout.Font, 0, 3), FontSize = 12, MinWidth = 84 };
        var most = LayoutProfile.LanguagePack.MaxMove ?? 16;
        var dx = Number(placed ? (layout.X ?? box!.X!.Value) - box!.X!.Value : 0, -most, most);
        var dy = Number(placed ? (layout.Y ?? box!.Y!.Value) - box!.Y!.Value : 0, -most, most);
        dx.IsEnabled = dy.IsEnabled = placed;
        if (!placed)
        {
            ToolTip.SetTip(dx, "No recorded session says where the original draws this text: it cannot be moved yet");
        }
        void Apply()
        {
            if (_layoutLoading)
            {
                return;
            }
            var w = (int)(width.Value ?? result.Room);
            var (mx, my) = ((int)(dx.Value ?? 0), (int)(dy.Value ?? 0));
            _project.SetLayout(entry2, new SlotLayout(placed && mx != 0 ? box!.X + mx : null, placed && my != 0 ? box!.Y + my : null,
                w == result.Room && layout.Width is null ? null : w, lines.SelectedIndex + 1, font.SelectedIndex));
            Changed?.Invoke();
            ShowPreview();
        }
        width.ValueChanged += (_, _) => Apply();
        dx.ValueChanged += (_, _) => Apply();
        dy.ValueChanged += (_, _) => Apply();
        lines.SelectionChanged += (_, _) => Apply();
        font.SelectionChanged += (_, _) => Apply();
        var reset = WorkshopTheme.Ghost(new TextBlock { Text = "Reset", FontSize = 12 }, "Draw this text where and as the original draws it");
        reset.IsEnabled = !layout.IsOriginal;
        reset.Click += (_, _) =>
        {
            _project.SetLayout(entry2, null);
            Changed?.Invoke();
            ShowPreview();
        };
        _layoutRow.Children.Add(new TextBlock { Text = "Layout", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 4) });
        _layoutRow.Children.Add(Field("Width", width));
        _layoutRow.Children.Add(Field("Lines", lines));
        _layoutRow.Children.Add(Field("Font", font));
        _layoutRow.Children.Add(Field("Move x", dx));
        _layoutRow.Children.Add(Field("y", dy));
        _layoutRow.Children.Add(reset);
        _layoutLoading = false;
        // the layout's own problems (moves, widths past the room, off the screen, over another text): at once, as the check gives them
        var boxes = _project.Boxes.ToDictionary(b => b.Key, StringComparer.Ordinal);
        var only = entry2.Screens.Take(1).ToDictionary(s => s, _ => new Dictionary<string, SlotLayout>(StringComparer.Ordinal) { [entry2.Key] = layout }, StringComparer.Ordinal);
        foreach (var (_, _, problem) in SlotLayouts.Validate(only, boxes, LayoutProfile.LanguagePack))
        {
            _issues.Children.Add(Issue("Layout: " + problem, problem.StartsWith(SlotLayouts.MayCover, StringComparison.Ordinal) ? WorkshopTheme.Warn : WorkshopTheme.Bad));
        }
    }

    // the template's first comment without its key (the key is in the line already)
    private static string Rest(TextEntry entry)
    {
        var first = entry.Template.Comments.FirstOrDefault() ?? "";
        var rest = first.StartsWith(entry.Key + " · ", StringComparison.Ordinal) ? first[(entry.Key.Length + 3)..] : first;
        return rest.StartsWith("a card text: box: ", StringComparison.Ordinal) ? "up to " + rest["a card text: box: ".Length..] : rest;
    }
}
