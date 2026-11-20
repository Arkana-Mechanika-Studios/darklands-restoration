using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Darklands.Presentation;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's window (W-0): a compact toolbar (game folder, new pack, open pack, save, check, run the game with the pack, the
/// glossary and the variables), the editors as tabs, the game's check of the pack below once a check has results (each finding leads
/// to its text), and a status line with the game folder and the pack. With no pack open, a first-start page: find the game, then make
/// or open a pack.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class WorkshopWindow : Window
{
    private readonly WorkshopSettings _settings;
    private readonly TextBlock _where = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim) };
    private readonly ContentControl _centre = new();
    private readonly TabControl _tabs = new() { Padding = new Thickness(0) };
    private readonly ListBox _findings = new() { Height = 100 };
    private readonly Expander _checkPanel = new() { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 8, 4) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
    private readonly Button _save = WorkshopTheme.Ghost(WorkshopTheme.IconText("save", "Save", 14), "Save the pack's files (Ctrl+S)");
    private readonly Button _check = WorkshopTheme.Ghost(new StackPanel(), "Save, then run the game's own check of the pack");
    private readonly TextBlock _checkCount = new() { FontSize = 11.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _checkPill;
    private readonly Button _run = WorkshopTheme.Primary(WorkshopTheme.IconText("play", "Play in game", 13));
    private readonly Button _glossaryButton = WorkshopTheme.Ghost(WorkshopTheme.IconText("book", "Glossary", 14), "The pack's terms and their translations");
    private readonly Button _more = WorkshopTheme.Ghost(WorkshopIcons.Make("more", 16), "More");
    private readonly ContentControl _crumb = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _packActions = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _gameButton = WorkshopTheme.Ghost(new StackPanel(), "The folder of your Darklands");
    private readonly StackPanel _keys = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private string? _game;
    private TextProject? _project;
    private TextEditorView? _editor;

    public WorkshopWindow(WorkshopSettings settings, string[] args)
    {
        _settings = settings;
        _checkPill = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(6, 0),
            Height = 18,
            Margin = new Thickness(4, 0, 0, 0),
            Background = WorkshopTheme.Brush(Color.FromArgb(0x26, 0xE0, 0x56, 0x4E)),
            Child = _checkCount,
            IsVisible = false,
        };
        _checkCount.Foreground = WorkshopTheme.Brush(Color.Parse("#ef8a83"));
        _check.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { WorkshopTheme.IconText("check-circle", "Check", 14), _checkPill } };
        FontFamily = WorkshopTheme.UiFont;
        FontSize = 13;
        Title = "Darklands Workshop";
        Icon = WorkshopTheme.Icon();
        Width = 1280;
        Height = 800;
        Background = WorkshopTheme.Brush(WorkshopTheme.Bg);
        Foreground = WorkshopTheme.Brush(WorkshopTheme.Text);
        KeyDown += OnKey;
        var gameArg = Array.IndexOf(args, "--game") is var g and >= 0 && g + 1 < args.Length ? args[g + 1] : null;
        _game = gameArg is not null && Darklands.Content.GameFiles.IsGameFolder(gameArg) ? gameArg : settings.FindGame();
        Content = Build();
        UpdateState();
        var pack = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || args[i - 1] != "--game")).LastOrDefault();
        if (pack is not null)
        {
            Opened += (_, _) => Open(pack);
        }
        Closing += (_, e) =>
        {
            if (_editor?.Dirty == true)
            {
                // unsaved words are never lost: they are saved before the window goes
                _editor.Save();
            }
            _settings.Save();
        };
    }

    private Control Build()
    {
        _gameButton.Click += async (_, _) => await ChooseGame();
        _save.Click += (_, _) => _editor?.Save();
        _check.Click += (_, _) => RunCheck();
        _run.Click += (_, _) => RunGame();
        ToolTip.SetTip(_run, "Save, then start the game with this pack");
        _glossaryButton.Click += async (_, _) =>
        {
            if (_editor is not null)
            {
                await new GlossaryWindow(_editor.Glossary).ShowDialog(this);
                _editor.Refresh();
            }
        };
        _more.Click += (_, _) => MoreMenu().ShowAt(_more);
        foreach (var c in new Control[] { _save, _check, _glossaryButton })
        {
            _packActions.Children.Add(c);
        }
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), ClipToBounds = true, Child = new Image { Source = WorkshopTheme.Emblem, Stretch = Stretch.UniformToFill } },
                _crumb,
            },
        };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _gameButton, _packActions, _more, _run } };
        _run.Margin = new Thickness(8, 0, 0, 0);
        var topBar = new DockPanel { Height = 44, Margin = new Thickness(12, 0) };
        DockPanel.SetDock(right, Dock.Right);
        topBar.Children.Add(right);
        topBar.Children.Add(brand);
        var top = new Border { Child = topBar, Background = WorkshopTheme.Brush(WorkshopTheme.Side), BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(0, 0, 0, 1) };

        _findings.Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType<ListBoxItem>(x))
        {
            Setters = { new Avalonia.Styling.Setter(ListBoxItem.PaddingProperty, new Thickness(8, 3)), new Avalonia.Styling.Setter(ListBoxItem.MinHeightProperty, 0.0) },
        });
        _findings.DoubleTapped += (_, _) =>
        {
            if (_findings.SelectedItem is LanguagePackCheck.Finding { Id.Length: > 0 } f)
            {
                _editor?.Select(f.Id);
            }
        };
        _checkPanel.Content = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Double-click a finding to see its text.", Foreground = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim), FontSize = 12 },
                _findings,
            },
        };

        // the status line, quiet: the message on the left; the shortcuts, the game folder and the pack on the right, never cut off
        _status.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        _where.FontSize = 12;
        _where.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        var statusRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, VerticalAlignment = VerticalAlignment.Center, Children = { _keys, _where } };
        var statusLine = new DockPanel { Height = 26, Margin = new Thickness(12, 0) };
        DockPanel.SetDock(statusRight, Dock.Right);
        statusLine.Children.Add(statusRight);
        statusLine.Children.Add(_status);
        var bottom = new Border { Child = statusLine, Background = WorkshopTheme.Brush(WorkshopTheme.Side), BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(0, 1, 0, 0) };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(_checkPanel, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_checkPanel);
        root.Children.Add(_centre);
        return root;
    }

    // the start page: the banner, the packs found on this computer, the actions (StartPage)
    private Control BuildStart()
    {
        var packs = PackCatalog.Find(PackCatalog.PackFolders(AppContext.BaseDirectory, _game), _settings.RecentPacks);
        var page = new StartPage(packs, TemplateCount(), _game, _settings.RecentPacks.FirstOrDefault())
        {
            Open = Open,
            NewPack = () => _ = NewPack(),
            OpenFolder = () => _ = OpenPack(),
            ChooseGame = () => _ = ChooseGame(),
            Link = address => _ = Launcher.LaunchUriAsync(new Uri(address)),
        };
        _startPacks = packs;
        _status.Text = packs.Count == 0 ? "No pack found yet."
            : $"{packs.Count} pack{(packs.Count == 1 ? "" : "s")} · looking in the program's packs folder, the game's packs folder and the folders you opened";
        return page.Build();
    }

    private IReadOnlyList<PackSummary> _startPacks = [];

    // how many texts a pack can translate (the template's), for the packs' progress; 0 without a game
    private int TemplateCount()
    {
        if (_game is null)
        {
            return 0;
        }
        if (_templateCount.Game != _game)
        {
            try
            {
                _templateCount = (_game, TranslatorTemplate.Build(new Darklands.Content.GameFiles(_game), []).Count);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _templateCount = (_game, 0);
            }
        }
        return _templateCount.Count;
    }

    private (string? Game, int Count) _templateCount;

    // the menu of the actions that are not on the bar
    private MenuFlyout MoreMenu()
    {
        var menu = new MenuFlyout();
        MenuItem Item(string text, string icon, Action run, bool enabled = true, string? gesture = null)
        {
            var item = new MenuItem { Header = text, Icon = WorkshopIcons.Make(icon, 15), IsEnabled = enabled, InputGesture = gesture is null ? null : Avalonia.Input.KeyGesture.Parse(gesture) };
            item.Click += (_, _) => run();
            return item;
        }
        var pack = _project is not null;
        menu.Items.Add(Item("New pack…", "plus", () => _ = NewPack(), _game is not null, "Ctrl+N"));
        menu.Items.Add(Item("Open pack…", "folder", () => _ = OpenPack(), _game is not null, "Ctrl+O"));
        menu.Items.Add(Item("All packs", "chevron-right", ClosePack, pack));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Pack variables…", "braces", () => _ = Variables(), pack));
        menu.Items.Add(Item("Font pack…", "type", () => _ = BuildFontPack(ask: false), pack));
        menu.Items.Add(Item("Show in game", "play", () => _ = ShowInGame(), pack));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Game folder…", "gamepad", () => _ = ChooseGame()));
        return menu;
    }

    private async Task Variables()
    {
        if (_editor is not null)
        {
            await new VariablesWindow(_editor.Project).ShowDialog(this);
            _editor.Refresh();
        }
    }

    // back to the start page (the pack saved)
    private void ClosePack()
    {
        if (_editor?.Dirty == true)
        {
            _editor.Save();
        }
        _project = null;
        _editor = null;
        _checkPanel.IsVisible = false;
        UpdateState();
    }

    private void OnKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        if (ctrl && e.Key == Avalonia.Input.Key.S && _editor is not null)
        {
            _editor.Save();
            _status.Text = "Saved " + DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + ".";
            e.Handled = true;
        }
        else if (ctrl && e.Key == Avalonia.Input.Key.N && _game is not null)
        {
            _ = NewPack();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Avalonia.Input.Key.O && _game is not null)
        {
            _ = OpenPack();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Enter && _project is null && _startPacks.Count > 0 && FocusManager?.GetFocusedElement() is not TextBox)
        {
            Open(_settings.RecentPacks.FirstOrDefault(Directory.Exists) ?? _startPacks[0].Folder);
            e.Handled = true;
        }
    }

    private void UpdateState()
    {
        _where.Text = _project is null ? "" : $"Game {_game ?? "not found"} · Pack {_project.Folder}";
        _packActions.IsVisible = _run.IsVisible = _project is not null;
        _gameButton.IsVisible = _project is null;
        _gameButton.Content = WorkshopTheme.IconText("gamepad", _game ?? "Find the game…", 14);
        _gameButton.Foreground = WorkshopTheme.Brush(_game is null ? WorkshopTheme.Warn : WorkshopTheme.Muted);
        _crumb.Content = _project is null
            ? new TextBlock { Text = "Darklands Workshop", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }
            : Crumb(_project);
        _keys.Children.Clear();
        if (_project is null)
        {
            _keys.Children.Add(new TextBlock { Text = "Open the last pack", VerticalAlignment = VerticalAlignment.Center });
            _keys.Children.Add(WorkshopTheme.Key("Enter"));
            _keys.Children.Add(new TextBlock { Text = "  New pack", VerticalAlignment = VerticalAlignment.Center });
            _keys.Children.Add(WorkshopTheme.Key("Ctrl"));
            _keys.Children.Add(WorkshopTheme.Key("N"));
        }
        else
        {
            _keys.Children.Add(new TextBlock { Text = "Save", VerticalAlignment = VerticalAlignment.Center });
            _keys.Children.Add(WorkshopTheme.Key("Ctrl"));
            _keys.Children.Add(WorkshopTheme.Key("S"));
        }
        foreach (var t in _keys.Children.OfType<TextBlock>())
        {
            t.FontSize = 12;
            t.Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint);
        }
        if (_project is null)
        {
            _centre.Content = BuildStart();
        }
        else if (_centre.Content != _tabs)
        {
            _centre.Content = _tabs;
        }
        Title = "Darklands Workshop" + (_project is null ? "" : $" — {_project.Language} · {Path.GetFileName(_project.Folder)}{(_editor?.Dirty == true ? " (not saved)" : "")}");
    }

    // "Packs › Français fr": the way back to the start page and the pack's language
    private Control Crumb(TextProject project)
    {
        var back = WorkshopTheme.Ghost(new TextBlock { Text = "Packs", Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) }, "All packs");
        back.Padding = new Thickness(6, 3);
        back.Click += (_, _) => ClosePack();
        var language = Languages.Of(project.Language);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                back,
                WorkshopIcons.Make("chevron-right", 14, WorkshopTheme.Brush(WorkshopTheme.Faint)),
                new TextBlock { Text = language?.Native ?? project.Language, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(7, 0),
                    Height = 18,
                    Background = WorkshopTheme.Brush(WorkshopTheme.Raised),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = project.Language, FontSize = 11.5, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };
    }

    private async Task ChooseGame()
    {
        if (await PickFolder("The folder of your Darklands (it holds DARKLAND.EXE)") is { } folder)
        {
            if (Darklands.Platform.GameFolderSearch.GameFolderIn(folder, Darklands.Content.GameFiles.IsGameFolder) is { } game)
            {
                _game = _settings.GameFolder = game;
                _settings.Save();
                _status.Text = "Game folder: " + game;
            }
            else
            {
                _status.Text = $"{folder} holds no DARKLAND.EXE.";
            }
            UpdateState();
        }
    }

    private async Task OpenPack()
    {
        if (await PickFolder("Open a translation pack (its folder holds pack.json)") is { } folder)
        {
            Open(folder);
        }
    }

    private async Task NewPack()
    {
        if (_game is null)
        {
            _status.Text = "Choose the game's folder first: the texts come from your own game files.";
            return;
        }
        var dialog = new NewPackDialog(_game, Path.Combine(AppContext.BaseDirectory, "packs"), _settings.Author);
        if (!await dialog.ShowDialog<bool>(this))
        {
            return;
        }
        try
        {
            _settings.Author = dialog.Author ?? _settings.Author;
            var project = TextProject.Create(dialog.Folder, dialog.LanguageTag, dialog.PackName, dialog.Author, _game);
            if (dialog.BuildFont)
            {
                var (game, folder, language) = (_game, dialog.Folder, dialog.LanguageTag);
                _status.Text = "Building the font…";
                await Task.Run(() => FontNeeds.Build(game, folder, language));
            }
            Show(project);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = e.Message;
        }
    }

    private void Open(string folder)
    {
        if (_game is null)
        {
            _status.Text = "Choose the game's folder first: the texts come from your own game files.";
            return;
        }
        try
        {
            Show(TextProject.Open(folder, _game));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = $"{folder}: {e.Message}";
        }
    }

    private void Show(TextProject project)
    {
        if (_editor?.Dirty == true)
        {
            _editor.Save();
        }
        _project = project;
        _editor = new TextEditorView(project);
        _editor.Changed += UpdateState;
        _tabs.ItemsSource = new[] { new TabItem { Header = _editor.Title, Content = _editor.View, FontSize = 15 } };
        _tabs.SelectedIndex = 0;
        _findings.ItemsSource = null;
        _checkPanel.IsVisible = false;
        _settings.Opened(project.Folder);
        _settings.Save();
        _status.Text = "Opened " + Path.GetFileName(project.Folder) + ".";
        UpdateState();
    }

    // the font pack from the player's own fonts, for the pack's language: offered when a new pack's language has letters the game's
    // fonts lack (ask), built at once from the toolbar; the preview uses it at once, and the build's glyph charts are shown
    private async Task BuildFontPack(bool ask)
    {
        if (_project is null || _editor is null || _game is null)
        {
            return;
        }
        var lacking = FontNeeds.LackedByTheGame(_game, _project.Language);
        if (ask && (lacking.Length == 0 || FontNeeds.HasFontPack(_project.Folder)))
        {
            return;
        }
        if (ask)
        {
            var question = new QuestionDialog("A font for your language",
                $"The game's fonts have no {Sample(lacking)}. The Workshop can build them in the game's own pixel style from your game's fonts, "
                + "into your pack (its fonts folder); players get them with the pack. The preview uses them at once.",
                "Build the font pack", "Not now");
            if (!await question.ShowDialog<bool>(this))
            {
                _status.Text = "No font pack: letters the game's fonts lack are drawn as '?'. Font pack… builds it.";
                return;
            }
        }
        var (game, folder, language) = (_game, _project.Folder, _project.Language);
        _status.Text = "Building the font pack…";
        var counts = await Task.Run(() => FontNeeds.Build(game, folder, language));
        _editor.FontsChanged();
        _status.Text = $"Font pack built for {language} ({FontNeeds.Scripts(language)}): {string.Join(", ", counts.Select((n, i) => $"font {i + 1} {n} letters"))}.";
        if (FontNeeds.Charts(folder) is { Count: > 0 } charts)
        {
            await new ChartsWindow(charts).ShowDialog(this);
        }
    }

    private static string Sample(string letters) =>
        letters.Length <= 12 ? string.Join(" ", letters.Select(c => c.ToString())) : string.Join(" ", letters.Take(12).Select(c => c.ToString())) + " …";

    /// <summary>Runs the check as its button does (the window's pictures in the tests).</summary>
    internal void RunCheckForTests() => RunCheck();

    private void RunCheck()
    {
        if (_editor is null)
        {
            return;
        }
        _editor.Save();
        var findings = _editor.Check();
        _findings.ItemsSource = findings;
        var errors = findings.Count(f => f.Severity == LanguagePackCheck.Severity.Error);
        var overflows = findings.Count(f => f.Severity == LanguagePackCheck.Severity.Overflow);
        var notes = findings.Count - errors - overflows;
        var summary = findings.Count == 0
            ? "The game's check: no findings."
            : $"The game's check: {Count(errors, "error")} · {overflows.ToString("N0", CultureInfo.InvariantCulture)} too wide or too long · {Count(notes, "note")}";
        _checkPanel.Header = summary;
        _checkCount.Text = errors.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _checkPill.IsVisible = errors > 0;
        _checkPanel.IsVisible = true;
        _checkPanel.IsExpanded = findings.Count > 0;
        _status.Text = summary;
        UpdateState();
    }

    private static string Count(int n, string what) => n.ToString("N0", CultureInfo.InvariantCulture) + " " + what + (n == 1 ? "" : "s");

    private void RunGame()
    {
        if (_project is null || _editor is null)
        {
            return;
        }
        _editor.Save();
        var program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "DarklandsRP.exe" : "DarklandsRP");
        if (!File.Exists(program))
        {
            _status.Text = $"The game's program is not beside the Workshop ({program}).";
            return;
        }
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        start.ArgumentList.Add("--language-pack");
        start.ArgumentList.Add(_project.Folder);
        if (_game is not null)
        {
            start.ArgumentList.Add(_game);
        }
        Process.Start(start);
        UpdateState();
    }

    // the game with the pack, and the text's key in the clipboard: the developer's inspector (F9) names the text under the pointer
    private async Task ShowInGame()
    {
        if (_editor?.CurrentKey is not { } key)
        {
            return;
        }
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(key);
        }
        RunGame();
        _status.Text = key.StartsWith("msg:", StringComparison.Ordinal)
            ? $"The game runs with the pack; {key} is the card {key[4..key.IndexOf('#', StringComparison.Ordinal)]}'s text (the key is in the clipboard)."
            : $"The game runs with the pack; the key {key} is in the clipboard (a developer build's inspector, F9, names the text under the pointer).";
    }

    private async Task<string?> PickFolder(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}

/// <summary>A question with two answers (true: the first).</summary>
internal sealed class QuestionDialog : Window
{
    public QuestionDialog(string title, string text, string yes, string no)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        Background = WorkshopTheme.Brush(WorkshopTheme.Wood);
        var ok = new Button { Content = yes, IsDefault = true };
        ok.Click += (_, _) => Close(true);
        var cancel = new Button { Content = no, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        Content = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(18),
            Children =
            {
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
    }
}

/// <summary>The font pack's glyph charts (the card texts' font, each script), drawn without smoothing.</summary>
internal sealed class ChartsWindow : Window
{
    public ChartsWindow(IReadOnlyList<string> charts)
    {
        Title = "The font pack's letters";
        Width = 900;
        Height = 640;
        Background = WorkshopTheme.Brush(WorkshopTheme.Wood);
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        foreach (var chart in charts)
        {
            panel.Children.Add(new TextBlock { Text = Path.GetFileNameWithoutExtension(chart), FontWeight = FontWeight.SemiBold, Foreground = WorkshopTheme.Brush(WorkshopTheme.Gold) });
            var bitmap = new Avalonia.Media.Imaging.Bitmap(chart);
            var image = new Image { Source = bitmap, Width = bitmap.PixelSize.Width, Height = bitmap.PixelSize.Height, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            panel.Children.Add(image);
        }
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }
}
