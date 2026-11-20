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
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _save = Tool("Save", "Save the pack's files");
    private readonly Button _check = Tool("Check", "Save, then run the game's own check of the pack");
    private readonly Button _run = Tool("Run game", "Save, then start the game with this pack");
    private readonly Button _show = Tool("Show in game", "Start the game with the pack; the text's key goes to the clipboard");
    private readonly Button _glossaryButton = Tool("Glossary", "The pack's terms and their translations");
    private readonly Button _variablesButton = Tool("Variables", "The pack's own variables ($him1 …), chosen by the character's sex");
    private string? _game;
    private TextProject? _project;
    private TextEditorView? _editor;

    public WorkshopWindow(WorkshopSettings settings, string[] args)
    {
        _settings = settings;
        Title = "Darklands Workshop";
        Icon = WorkshopTheme.Icon();
        Width = 1280;
        Height = 800;
        Background = WorkshopTheme.Brush(WorkshopTheme.Wood);
        Foreground = WorkshopTheme.Brush(WorkshopTheme.Parchment);
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

    private static Button Tool(string label, string tip)
    {
        var button = new Button { Content = label, Padding = new Thickness(10, 4), FontSize = 13 };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Control Gap() => new Border { Width = 1, Margin = new Thickness(6, 4), Background = WorkshopTheme.Brush(WorkshopTheme.WoodLine) };

    private Control Build()
    {
        var gameButton = Tool("Game…", "The folder of your Darklands (it holds DARKLAND.EXE)");
        gameButton.Click += async (_, _) => await ChooseGame();
        var newButton = Tool("New pack…", "Make a translation pack for a language");
        newButton.Click += async (_, _) => await NewPack();
        var openButton = Tool("Open pack…", "Open a translation pack (its folder holds pack.json)");
        openButton.Click += async (_, _) => await OpenPack();
        _save.Click += (_, _) => _editor?.Save();
        _check.Click += (_, _) => RunCheck();
        _run.Click += (_, _) => RunGame();
        _show.Click += async (_, _) => await ShowInGame();
        _glossaryButton.Click += async (_, _) =>
        {
            if (_editor is not null)
            {
                await new GlossaryWindow(_editor.Glossary).ShowDialog(this);
                _editor.Refresh();
            }
        };
        _variablesButton.Click += async (_, _) =>
        {
            if (_editor is not null)
            {
                await new VariablesWindow(_editor.Project).ShowDialog(this);
                _editor.Refresh();
            }
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var c in new Control[] { gameButton, newButton, openButton, Gap(), _save, _check, Gap(), _run, _show, Gap(), _glossaryButton, _variablesButton })
        {
            bar.Children.Add(c);
        }
        var top = new Border { Child = bar, Padding = new Thickness(8, 6), Background = WorkshopTheme.Brush(WorkshopTheme.WoodLight), BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine), BorderThickness = new Thickness(0, 0, 0, 1) };

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

        // the status line: the message on the left, the game folder and the pack on the right, never cut off
        var statusLine = new DockPanel { Margin = new Thickness(10, 4) };
        DockPanel.SetDock(_where, Dock.Right);
        statusLine.Children.Add(_where);
        statusLine.Children.Add(_status);
        var bottom = new Border { Child = statusLine, Background = WorkshopTheme.Brush(WorkshopTheme.WoodLight), BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine), BorderThickness = new Thickness(0, 1, 0, 0) };

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

    // the first-start page: find the game, then make or open a pack (the packs opened lately below)
    private Control BuildStart()
    {
        var parchment = WorkshopTheme.Brush(WorkshopTheme.Parchment);
        var dim = WorkshopTheme.Brush(WorkshopTheme.ParchmentDim);
        var gold = WorkshopTheme.Brush(WorkshopTheme.Gold);
        var panel = new StackPanel { Spacing = 10, Width = 560 };
        panel.Children.Add(new TextBlock { Text = "Darklands Workshop", FontSize = 30, FontWeight = FontWeight.SemiBold, Foreground = gold });
        panel.Children.Add(new TextBlock { Text = "Translate the game's texts and see each one as the game draws it.", FontSize = 15, Foreground = dim, TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(new TextBlock { Text = "1   Your game", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = parchment, Margin = new Thickness(0, 14, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = _game is null ? "Not found yet. Choose the folder that holds DARKLAND.EXE." : "Found: " + _game,
            Foreground = _game is null ? WorkshopTheme.Brush(WorkshopTheme.Problem) : parchment,
            TextWrapping = TextWrapping.Wrap,
        });
        var find = new Button { Content = _game is null ? "Find the game folder…" : "Choose another folder…", Padding = new Thickness(14, 6) };
        find.Click += async (_, _) => await ChooseGame();
        panel.Children.Add(find);

        panel.Children.Add(new TextBlock { Text = "2   A translation pack", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = parchment, Margin = new Thickness(0, 14, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "A pack is a folder of your own: the game's files are only read, never changed.", Foreground = dim, TextWrapping = TextWrapping.Wrap });
        var newButton = new Button { Content = "New pack…", Padding = new Thickness(14, 6), IsEnabled = _game is not null };
        newButton.Click += async (_, _) => await NewPack();
        var openButton = new Button { Content = "Open pack…", Padding = new Thickness(14, 6), IsEnabled = _game is not null };
        openButton.Click += async (_, _) => await OpenPack();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { newButton, openButton } });

        var recent = _settings.RecentPacks.Where(Directory.Exists).Take(5).ToList();
        if (recent.Count > 0 && _game is not null)
        {
            panel.Children.Add(new TextBlock { Text = "Opened lately", Foreground = dim, Margin = new Thickness(0, 8, 0, 0) });
            foreach (var folder in recent)
            {
                var link = new Button { Content = folder, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = gold, Padding = new Thickness(0, 2) };
                link.Click += (_, _) => Open(folder);
                panel.Children.Add(link);
            }
        }
        return new ScrollViewer
        {
            Content = new Border
            {
                Child = panel,
                Padding = new Thickness(36, 28),
                Margin = new Thickness(0, 40),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Background = WorkshopTheme.Brush(WorkshopTheme.WoodLight),
                BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
            },
        };
    }

    private void UpdateState()
    {
        _where.Text = $"Game: {_game ?? "not found"}" + (_project is null ? "" : $"     Pack: {_project.Folder}");
        _save.IsEnabled = _check.IsEnabled = _run.IsEnabled = _show.IsEnabled = _glossaryButton.IsEnabled = _variablesButton.IsEnabled = _project is not null;
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
        var dialog = new NewPackDialog();
        if (await dialog.ShowDialog<bool>(this) && await PickFolder("An empty folder for the new pack (outside the game's folder)") is { } folder)
        {
            try
            {
                Show(TextProject.Create(folder, dialog.Language, dialog.PackName, dialog.Author, _game));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _status.Text = e.Message;
            }
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

/// <summary>A new pack's language tag, name and author.</summary>
internal sealed class NewPackDialog : Window
{
    private readonly TextBox _language = new() { Watermark = "a language tag: de, fr, pt-BR", Text = "" };
    private readonly TextBox _name = new() { Watermark = "the pack's name" };
    private readonly TextBox _author = new() { Watermark = "your name (optional)" };

    public NewPackDialog()
    {
        Title = "New translation pack";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        Background = WorkshopTheme.Brush(WorkshopTheme.Wood);
        var ok = new Button { Content = "Make the pack", IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (Language.Length > 0)
            {
                Close(true);
            }
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Language" });
        panel.Children.Add(_language);
        panel.Children.Add(new TextBlock { Text = "Name" });
        panel.Children.Add(_name);
        panel.Children.Add(new TextBlock { Text = "Author" });
        panel.Children.Add(_author);
        panel.Children.Add(buttons);
        Content = panel;
    }

    public string Language => (_language.Text ?? "").Trim();

    public string PackName => string.IsNullOrWhiteSpace(_name.Text) ? Language : _name.Text.Trim();

    public string? Author => string.IsNullOrWhiteSpace(_author.Text) ? null : _author.Text.Trim();
}
