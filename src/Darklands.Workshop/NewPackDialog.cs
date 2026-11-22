using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// A new translation pack (W-0 polish, the approved design): its language from a searchable list of the evident ones (each by its
/// own name, its English name and its tag) or another, checked as a BCP 47 tag; its name and author; where it goes (the game's
/// packs/languages folder by default, where the launcher lists packs); and, when the language has letters the game's fonts lack, the matching
/// font in the game's pixel style.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class NewPackDialog : Window
{
    private static readonly Language Other = new("", "Other language…", "Other language…");

    private readonly string _game;
    private readonly string _packs;
    private readonly TextBox _search = new() { Watermark = "Search by name or tag" };
    private readonly ListBox _list = new() { Height = 168 };
    private readonly TextBox _tag = new() { Watermark = "a language tag: gsw-CH, sr-Latn …" };
    private readonly TextBox _tagName = new() { Watermark = "its name in the language" };
    private readonly TextBlock _tagCheck = new() { FontSize = 12 };
    private readonly StackPanel _other = new() { Spacing = 6, IsVisible = false };
    private readonly TextBox _name = new();
    private readonly TextBox _author = new() { Watermark = "optional" };
    private readonly TextBox _folder = new();
    private readonly CheckBox _font = new() { IsChecked = true };
    private readonly TextBlock _fontText = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Border _fontBox;
    private readonly Button _create = WorkshopTheme.Primary(new TextBlock { Text = "Create pack" });
    private readonly TextBlock _problem = new() { FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Bad), TextWrapping = TextWrapping.Wrap };
    // what Chosen last wrote into the name and the folder: a box whose text differs is the player's, and keeps it (Avalonia posts
    //   TextChanged to the dispatcher, so a flag set there comes after the dialog's own writes and cannot tell them from typing)
    private string _nameWritten = "";
    private string _folderWritten = "";

    public NewPackDialog(string game, string packsFolder, string? author)
    {
        _game = game;
        _packs = packsFolder;
        Title = "New translation pack";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WorkshopTheme.Brush(WorkshopTheme.Side);
        Foreground = WorkshopTheme.Brush(WorkshopTheme.Text);
        FontFamily = WorkshopTheme.UiFont;
        _author.Text = author ?? "";
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Language>((l, _) => l is null ? new TextBlock() : LanguageLine(l));
        _search.TextChanged += (_, _) => Fill();
        _list.SelectionChanged += (_, _) => Chosen();
        _tag.TextChanged += (_, _) => Chosen();
        _tagName.TextChanged += (_, _) => Chosen();
        _folder.TextChanged += (_, _) => Validate();
        _fontBox = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            Background = WorkshopTheme.Brush(WorkshopTheme.Surface),
            Margin = new Thickness(0, 16, 0, 0),
            Child = new DockPanel
            {
                Children =
                {
                    _font,
                    new StackPanel
                    {
                        Margin = new Thickness(4, 2, 0, 0),
                        Children = { new TextBlock { Text = "Build a matching font", FontWeight = FontWeight.SemiBold }, _fontText },
                    },
                },
            },
        };
        var cancel = WorkshopTheme.Outline(new TextBlock { Text = "Cancel" });
        cancel.Click += (_, _) => Close(false);
        _create.Click += (_, _) =>
        {
            if (Validate())
            {
                Close(true);
            }
        };
        var browse = WorkshopTheme.Outline(new TextBlock { Text = "Browse…" });
        browse.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A folder for the new pack", AllowMultiple = false });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            {
                _folder.Text = Path.Combine(path, LanguageTag.Length > 0 ? LanguageTag : "pack");
            }
        };
        _other.Children.Add(Label("Language tag"));
        _other.Children.Add(_tag);
        _other.Children.Add(_tagCheck);
        _other.Children.Add(Label("Its name"));
        _other.Children.Add(_tagName);
        var location = new DockPanel { Children = { browse, _folder } };
        DockPanel.SetDock(browse, Dock.Right);
        browse.Margin = new Thickness(8, 0, 0, 0);
        var names = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var nameColumn = new StackPanel { Children = { Label("Pack name"), _name } };
        var authorColumn = new StackPanel { Children = { Label("Author"), _author } };
        Grid.SetColumn(authorColumn, 2);
        names.Children.Add(nameColumn);
        names.Children.Add(authorColumn);
        var body = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 16),
            Children =
            {
                new TextBlock { Text = "New translation pack", FontSize = 15, FontWeight = FontWeight.SemiBold },
                Label("Language"),
                _search,
                new Border { Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(6), BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line2), BorderThickness = new Thickness(1), Child = _list },
                _other,
                names,
                Label("Location"),
                location,
                new TextBlock { Text = "The launcher lists the packs in the game's packs folder (packs/languages).", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), Margin = new Thickness(0, 6, 0, 0) },
                _fontBox,
                _problem,
            },
        };
        var footer = new Border
        {
            BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 14),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, _create } },
        };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        Fill();
        Opened += (_, _) => _search.Focus();
    }

    /// <summary>The chosen language's tag.</summary>
    public string LanguageTag => _list.SelectedItem is Language { Tag.Length: > 0 } l ? l.Tag : (_tag.Text ?? "").Trim();

    /// <summary>The pack's name.</summary>
    public string PackName => string.IsNullOrWhiteSpace(_name.Text) ? LanguageTag : _name.Text.Trim();

    /// <summary>The author, if given.</summary>
    public string? Author => string.IsNullOrWhiteSpace(_author.Text) ? null : _author.Text.Trim();

    /// <summary>The pack's folder.</summary>
    public string Folder => (_folder.Text ?? "").Trim();

    /// <summary>Whether to build the matching font with the pack.</summary>
    public bool BuildFont => _fontBox.IsVisible && _font.IsChecked == true;

    /// <summary>Types <paramref name="text"/> into the language search box, as a player does (the tests).</summary>
    internal void TypeSearchForTests(string text) => _search.Text = text;

    /// <summary>Types <paramref name="text"/> as the pack's name, as a player does (the tests).</summary>
    internal void TypeNameForTests(string text) => _name.Text = text;

    /// <summary>Picks a language (the tests' pictures; the list's selection).</summary>
    internal void Pick(string query, string? tag = null)
    {
        _search.Text = query;
        Fill();
        _list.SelectedItem = _list.ItemsSource?.Cast<Language>().FirstOrDefault(l => tag is null || l.Tag == tag);
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), Margin = new Thickness(0, 14, 0, 6) };

    private static Control LanguageLine(Language l)
    {
        if (l == Other)
        {
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { WorkshopIcons.Make("plus", 14, WorkshopTheme.Brush(WorkshopTheme.Muted)), new TextBlock { Text = "Other language…", Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) } },
            };
        }
        var line = new DockPanel();
        var tag = new TextBlock { Text = l.Tag, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tag, Dock.Right);
        line.Children.Add(tag);
        line.Children.Add(new TextBlock
        {
            Inlines = new Avalonia.Controls.Documents.InlineCollection
            {
                new Avalonia.Controls.Documents.Run(l.Native) { FontWeight = FontWeight.SemiBold },
                new Avalonia.Controls.Documents.Run(l.Native == l.English ? "" : " · " + l.English) { Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) },
            },
        });
        return line;
    }

    private void Fill()
    {
        var keep = _list.SelectedItem;
        var found = Languages.Find(_search.Text).Append(Other).ToList();
        _list.ItemsSource = found;
        _list.SelectedItem = keep is Language l && found.Contains(l) ? l : found.Count > 1 && (_search.Text ?? "").Length > 0 ? found[0] : null;
        Chosen();
    }

    // the language chosen: the other's fields, the defaults (name, folder), the font's offer
    private void Chosen()
    {
        var other = ReferenceEquals(_list.SelectedItem, Other);
        _other.IsVisible = other;
        var tag = LanguageTag;
        var listed = Languages.Of(tag);
        if ((_name.Text ?? "") == _nameWritten)
        {
            _name.Text = _nameWritten = listed?.Native ?? (other ? (_tagName.Text ?? "").Trim() : "");
        }
        if ((_folder.Text ?? "") == _folderWritten)
        {
            _folder.Text = _folderWritten = tag.Length > 0 ? Path.Combine(_packs, tag) : "";
        }
        var lacking = Languages.IsWellFormed(tag) ? FontNeeds.LackedByTheGame(_game, tag) : "";
        _fontBox.IsVisible = lacking.Length > 0;
        _fontText.Text = $"The game's fonts have no {string.Join(" ", lacking.Take(10).Select(c => c.ToString()))}{(lacking.Length > 10 ? " …" : "")}. The Workshop draws them in the game's own pixel style, from your game's fonts.";
        _fontText.Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted);
        Validate();
    }

    // whether the pack can be made: a well-formed tag, and a folder that is not a pack already
    private bool Validate()
    {
        var tag = LanguageTag;
        if (_other.IsVisible)
        {
            var ok = Languages.IsWellFormed(tag);
            _tagCheck.Text = tag.Length == 0 ? "A tag such as gsw-CH (Swiss German) or sr-Latn (Serbian in Latin letters)." : ok ? "✓ a well-formed language tag" : "Not a well-formed language tag (BCP 47).";
            _tagCheck.Foreground = WorkshopTheme.Brush(tag.Length == 0 ? WorkshopTheme.Faint : ok ? WorkshopTheme.Ok : WorkshopTheme.Bad);
        }
        string? problem = null;
        if (!Languages.IsWellFormed(tag))
        {
            problem = "";
        }
        else if (Folder.Length == 0)
        {
            problem = "Choose where the pack goes.";
        }
        else if (File.Exists(Path.Combine(Folder, Darklands.Localization.LanguagePack.ManifestName)))
        {
            problem = "There is a pack in that folder already: open it from the list, or choose another folder.";
        }
        _problem.Text = problem ?? "";
        _problem.Margin = new Thickness(0, problem is { Length: > 0 } ? 10 : 0, 0, 0);
        _create.IsEnabled = problem is null;
        return problem is null;
    }
}
