using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Darklands.Presentation;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's window (W-0): the game folder and the pack in use at the top with the commands (new pack, open pack, save, check,
/// run the game with the pack), the editors as tabs, and the game's check of the pack below (each finding leads to its text).
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class WorkshopWindow : Window
{
    private readonly WorkshopSettings _settings;
    private readonly TextBlock _where = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TabControl _tabs = new();
    private readonly ListBox _findings = new() { Height = 150 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 2) };
    private readonly Button _save = new() { Content = "Save" };
    private readonly Button _check = new() { Content = "Check" };
    private readonly Button _run = new() { Content = "Run the game with this pack" };
    private string? _game;
    private TextProject? _project;
    private TextEditorView? _editor;

    public WorkshopWindow(WorkshopSettings settings, string[] args)
    {
        _settings = settings;
        Title = "Darklands Workshop";
        Width = 1280;
        Height = 820;
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
        var gameButton = new Button { Content = "Game folder…" };
        gameButton.Click += async (_, _) => await ChooseGame();
        var newButton = new Button { Content = "New pack…" };
        newButton.Click += async (_, _) => await NewPack();
        var openButton = new Button { Content = "Open pack…" };
        openButton.Click += async (_, _) =>
        {
            if (await PickFolder("Open a translation pack (its folder holds pack.json)") is { } folder)
            {
                Open(folder);
            }
        };
        _save.Click += (_, _) => _editor?.Save();
        _check.Click += (_, _) => RunCheck();
        _run.Click += (_, _) => RunGame();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        foreach (var b in new[] { gameButton, newButton, openButton, _save, _check, _run })
        {
            bar.Children.Add(b);
        }
        bar.Children.Add(_where);

        _findings.DoubleTapped += (_, _) =>
        {
            if (_findings.SelectedItem is LanguagePackCheck.Finding { Id.Length: > 0 } f)
            {
                _editor?.Select(f.Id);
            }
        };
        var bottom = new DockPanel();
        var heading = new TextBlock { Text = "The game's check of the saved pack (double-click a finding to see its text)", Margin = new Thickness(8, 4) };
        DockPanel.SetDock(heading, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        bottom.Children.Add(heading);
        bottom.Children.Add(_status);
        bottom.Children.Add(_findings);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(bottom);
        root.Children.Add(_tabs);
        return root;
    }

    private void UpdateState()
    {
        _where.Text = $"Game: {_game ?? "not found (choose its folder)"}" + (_project is null ? "" : $"    Pack: {_project.Folder}");
        _save.IsEnabled = _check.IsEnabled = _run.IsEnabled = _project is not null;
        Title = "Darklands Workshop" + (_project is null ? "" : $" — {Path.GetFileName(_project.Folder)}{(_editor?.Dirty == true ? " *" : "")}");
    }

    private async Task ChooseGame()
    {
        if (await PickFolder("The folder of your Darklands (it holds DARKLAND.EXE)") is { } folder)
        {
            if (Darklands.Platform.GameFolderSearch.GameFolderIn(folder, Darklands.Content.GameFiles.IsGameFolder) is { } game)
            {
                _game = _settings.GameFolder = game;
                _settings.Save();
            }
            else
            {
                _status.Text = $"{folder} holds no DARKLAND.EXE.";
            }
            UpdateState();
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
        _tabs.ItemsSource = new[] { new TabItem { Header = _editor.Title, Content = _editor.View } };
        _tabs.SelectedIndex = 0;
        _findings.ItemsSource = null;
        _settings.Opened(project.Folder);
        _settings.Save();
        _status.Text = $"{project.Progress().Translated} of {project.Entries.Count} texts translated.";
        UpdateState();
    }

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
        _status.Text = $"Checked: {errors} error(s), {overflows} too wide or too long, {findings.Count - errors - overflows} note(s).";
        UpdateState();
    }

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
