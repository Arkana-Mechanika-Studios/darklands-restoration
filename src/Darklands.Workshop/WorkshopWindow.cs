using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Darklands.Platform;
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
    private readonly ListBox _findings = new() { Height = 100 };
    private readonly Expander _checkPanel = new() { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(8, 0, 8, 4) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
    private readonly Button _save = WorkshopTheme.Ghost(WorkshopTheme.IconText("save", "Save", 14), "Save the pack's files (Ctrl+S)");
    private readonly Button _check = WorkshopTheme.Ghost(new StackPanel(), "Save, then run the game's own check of the pack");
    private readonly TextBlock _checkCount = new() { FontSize = 11.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _checkPill;
    private readonly Button _run = WorkshopTheme.Primary(WorkshopTheme.IconText("play", "Play in game", 13));
    private readonly Button _runMenu = WorkshopTheme.Primary(WorkshopIcons.Make("chevron-down", 14));
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
        _run.Click += (_, _) => RunGame(atText: true);
        ToolTip.SetTip(_run, "Save, then start the game with this pack at the text chosen (a Quick Start party; its saves stay in the pack's .workshop folder)");
        var runMenu = new MenuFlyout();
        var atText = new MenuItem { Header = "At this text", Icon = WorkshopIcons.Make("play", 15) };
        atText.Click += (_, _) => RunGame(atText: true);
        var fromStart = new MenuItem { Header = "From the start", Icon = WorkshopIcons.Make("gamepad", 15) };
        fromStart.Click += (_, _) => RunGame(atText: false);
        runMenu.Items.Add(atText);
        runMenu.Items.Add(fromStart);
        _runMenu.Flyout = runMenu;
        _runMenu.Padding = new Thickness(6, 0);
        ToolTip.SetTip(_runMenu, "Play at this text, or from the start");
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
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { _gameButton, _packActions, _more, _run, _runMenu } };
        _run.Margin = new Thickness(8, 0, 0, 0);
        _run.CornerRadius = new CornerRadius(6, 0, 0, 6);
        _runMenu.CornerRadius = new CornerRadius(0, 6, 6, 0);
        _runMenu.Margin = new Thickness(-3, 0, 0, 0);
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
        // the samples apart (listed under the packs, in their own order), never among your packs even once opened
        var packs = PackCatalog.Find(PackCatalog.PackFolders(WorkshopApp.GameProgramFolder, _game), _settings.RecentPacks)
            .Where(p => !WorkshopApp.IsSample(p.Folder)).ToList();
        var samples = PackCatalog.Find([WorkshopApp.SamplesFolder], [])
            .OrderBy(p => Array.IndexOf(SampleOrder, Path.GetFileName(p.Folder)) is var k and >= 0 ? k : SampleOrder.Length).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
        var page = new StartPage(packs, TemplateCount(), _game, _settings.RecentPacks.FirstOrDefault(p => !WorkshopApp.IsSample(p)))
        {
            Samples = samples,
            PlaySample = folder => _ = PlaySample(folder),
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
        menu.Items.Add(Item("Decorated capitals…", "book", () => _ = Capitals(), pack));
        menu.Items.Add(Item("Notes for translators and AI…", "book", () => _ = Notes(), pack));
        menu.Items.Add(Item("Export texts…", "arrow-right", () => _ = Export(), pack, "Ctrl+E"));
        menu.Items.Add(Item("Import texts…", "folder", () => _ = Import(), pack, "Ctrl+I"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy for AI (this card or screen)", "chat", () => _ = CopyForAi(), pack));
        menu.Items.Add(Item("Paste result", "check", () => _ = PasteResult(), pack));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Game folder…", "gamepad", () => _ = ChooseGame()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Translator's guide", "book", () => _ = Launcher.LaunchUriAsync(new Uri(ProjectLinks.TranslatorGuide))));
        menu.Items.Add(Item("Report a problem…", "alert", ReportProblem));
        return menu;
    }

    // a problem report (WorkshopReport): the zip beside the program, its folder and the issue form opened; nothing is sent
    private void ReportProblem()
    {
        if (_editor?.Dirty == true)
        {
            _editor.Save();
        }
        var (result, url, message) = WorkshopReport.Make(_game, _project);
        _status.Text = message;
        if (result is not null && url is not null)
        {
            WorkshopReport.Present(result, url, say => _status.Text = say);
        }
    }

    // the pack's decorated capitals (CapitalsWindow): an imported one shows at once in the preview
    private async Task Capitals()
    {
        if (_editor is null)
        {
            return;
        }
        var window = new CapitalsWindow(_editor.Project);
        window.Changed += _editor.FontsChanged;
        await window.ShowDialog(this);
    }

    /// <summary>The capitals window, shown (tests).</summary>
    internal CapitalsWindow? ShowCapitalsForTests()
    {
        if (_editor is null)
        {
            return null;
        }
        var window = new CapitalsWindow(_editor.Project);
        window.Changed += _editor.FontsChanged;
        window.Show(this);
        return window;
    }

    // the pack's notes for translators and AI (pack.json "style")
    private async Task Notes()
    {
        if (_editor is not null && await new NotesDialog(_editor.Project).ShowDialog<bool>(this))
        {
            _editor.Save();
            UpdateState();
        }
    }

    // the whole-file export: the dialog, then where to write the file
    private async Task Export()
    {
        if (_editor is null || _project is null)
        {
            return;
        }
        if (await new ExportDialog(_editor.Project, _editor.Current).ShowDialog<ExportChoice?>(this) is not { } choice)
        {
            return;
        }
        var name = $"{_editor.Project.Language}-texts.{choice.Format}";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export texts",
            SuggestedFileName = name,
            DefaultExtension = choice.Format,
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }
        var count = WriteExport(_editor.Project, _editor.Glossary, choice, path);
        _status.Text = $"Exported {count:N0} texts to {path}.";
    }

    /// <summary>
    /// The request Copy for AI puts on the clipboard: the instruction header and the chosen text's group (its card, its screen) as the
    /// export's JSON, ready to paste into an AI chat; null without a chosen text.
    /// </summary>
    internal string? AiRequest()
    {
        if (_editor?.Current is not { } current)
        {
            return null;
        }
        var key = TextTitles.Group(current).Key;
        var group = _editor.Project.Entries.Where(e => TextTitles.Group(e).Key == key).ToList();
        var items = TextExport.Items(_editor.Project, group, _editor.Glossary);
        return "Please translate these texts as the instructions say, and answer with the same JSON, each \"translation\" filled in.\n\n```json\n"
            + TextExport.Json(_editor.Project, items) + "```\n";
    }

    // Copy for AI: the request on the clipboard (nothing is sent anywhere)
    private async Task CopyForAi()
    {
        if (AiRequest() is { } request && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(request);
            _status.Text = "Copied for AI: paste it into a chat, then copy the answer and choose Paste result.";
        }
    }

    // Paste result: the AI's answer from the clipboard, through the import
    private async Task PasteResult()
    {
        if (Clipboard is { } clipboard && await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard) is { Length: > 0 } answer)
        {
            ImportText(answer, null, "the clipboard");
        }
        else
        {
            _status.Text = "The clipboard holds no text: copy the AI's answer first.";
        }
    }

    // the whole-file import: a file the export wrote (JSON, CSV), translated; then the check and the summary
    private async Task Import()
    {
        if (_editor is null)
        {
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import texts",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Exported texts (JSON, CSV, XLIFF)") { Patterns = ["*.json", "*.csv", "*.xlf", "*.xliff", "*.txt"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }
        ImportText(File.ReadAllText(path), Path.GetExtension(path), Path.GetFileName(path));
    }

    /// <summary>Imports <paramref name="text"/> (a file's or the clipboard's), runs the check and shows the summary (returned for the tests).</summary>
    internal ImportSummaryWindow? ImportText(string text, string? extension, string name)
    {
        if (_editor is null)
        {
            return null;
        }
        IReadOnlyList<ImportedText> read;
        try
        {
            read = TextImport.Read(text, extension);
        }
        catch (System.Text.Json.JsonException e)
        {
            _status.Text = $"{name}: not a file of texts the Workshop can read ({e.Message}).";
            return null;
        }
        var editor = _editor;
        var result = TextImport.Apply(editor.Project, editor.Marks, read);
        editor.Save();
        RunCheck();
        editor.Refresh();
        var summary = new ImportSummaryWindow(name, result, editor.Project.Entries, editor.ListTitle,
            key =>
            {
                editor.Select(key);
                Activate();
            },
            key =>
            {
                TextImport.Use(editor.Project, editor.Marks, result, key);
                editor.Save();
                RunCheck();
                editor.Refresh();
            },
            () =>
            {
                TextImport.Undo(editor.Project, editor.Marks, result);
                editor.Save();
                RunCheck();
                editor.Refresh();
                _status.Text = $"The import of {name} is undone.";
            });
        summary.Show(this);
        _status.Text = $"{name}: {result.Imported.Count:N0} texts imported, marked for review.";
        return summary;
    }

    /// <summary>Writes the export <paramref name="choice"/> of <paramref name="project"/> to <paramref name="path"/>; the number of entries written.</summary>
    internal static int WriteExport(TextProject project, Glossary glossary, ExportChoice choice, string path)
    {
        var items = TextExport.Items(project, choice.Entries, glossary, choice.Rows);
        var text = choice.Format switch
        {
            "csv" => TextExport.Csv(project, items),
            "xlf" => TextXliff.Write(project, items),
            _ => TextExport.Json(project, items),
        };
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        return items.Count;
    }

    /// <summary>Opens the export dialog without waiting (the window's pictures in the tests).</summary>
    internal ExportDialog? ShowExportForTests()
    {
        if (_editor is null)
        {
            return null;
        }
        var dialog = new ExportDialog(_editor.Project, _editor.Current);
        dialog.Show(this);
        return dialog;
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
        if (_project is not null && _editor is not null && _editor.Key(e))
        {
            e.Handled = true;
        }
        else if (ctrl && e.Key == Avalonia.Input.Key.I && _editor is not null)
        {
            _ = Import();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Avalonia.Input.Key.E && _editor is not null)
        {
            _ = Export();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Avalonia.Input.Key.S && _editor is not null)
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
        _packActions.IsVisible = _run.IsVisible = _runMenu.IsVisible = _project is not null;
        _gameButton.IsVisible = _project is null;
        _gameButton.Content = WorkshopTheme.IconText("gamepad", _game ?? "Find the game…", 14);
        _gameButton.Foreground = WorkshopTheme.Brush(_game is null ? WorkshopTheme.Warn : WorkshopTheme.Muted);
        _crumb.Content = _project is null
            ? new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new TextBlock { Text = "Darklands Workshop", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, WorkshopTheme.BetaBadge() },
            }
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
        else if (_editor is not null && _centre.Content != _editor.View)
        {
            _centre.Content = _editor.View;
        }
        // its version in the title bar, a beta said so (WorkshopVersion)
        Title = WorkshopVersion.Label + (WorkshopVersion.IsBeta ? " (Beta)" : "")
            + (_project is null ? "" : $" — {_project.Language} · {Path.GetFileName(_project.Folder)}{(_editor?.Dirty == true ? " (not saved)" : "")}");
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
                WorkshopTheme.BetaBadge(),
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
        var dialog = new NewPackDialog(_game, PackLayout.KindFolder(WorkshopApp.GameProgramFolder, PackLayout.Languages), _settings.Author);
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

    // the samples' order on the start page (WorkshopApp.SamplesFolder's folders; others after them)
    private static readonly string[] SampleOrder = ["sample-fr", "sample-es", "sample-pl", "sample-ru", "sample-el", "sample-ja", "sample-ko", "sample-fr-layouts"];

    private void Open(string folder)
    {
        if (OpenProject(folder) && WorkshopApp.IsSample(folder))
        {
            _ = OfferSampleFont();
        }
    }

    private bool OpenProject(string folder)
    {
        if (_game is null)
        {
            _status.Text = "Choose the game's folder first: the texts come from your own game files.";
            return false;
        }
        try
        {
            Show(TextProject.Open(folder, _game));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _status.Text = $"{folder}: {e.Message}";
            return false;
        }
    }

    // a sample opened: the font from the player's own game offered when its letters need one (the samples ship no pixel font,
    //   which is built from the game's fonts; the CJK samples bring their TrueType font)
    private Task OfferSampleFont() =>
        _project is { } project && !FontNeeds.IsCjk(project.Language) ? BuildFontPack(ask: true) : Task.CompletedTask;

    // a sample played from the start page: opened (its font offered first), then the game started with it at the church
    private async Task PlaySample(string folder)
    {
        if (!OpenProject(folder))
        {
            return;
        }
        await OfferSampleFont();
        if (_editor is not null && _project!.Entries.Any(e => e.Key == SampleStart))
        {
            _editor.Select(SampleStart);
        }
        RunGame(atText: true);
    }

    /// <summary>Where a sample plays from the start page: the church by day, the text every sample translates.</summary>
    internal const string SampleStart = "msg:$CITYC00#0";

    private void Show(TextProject project)
    {
        if (_editor?.Dirty == true)
        {
            _editor.Save();
        }
        _project = project;
        _editor = new TextEditorView(project);
        _editor.Changed += UpdateState;
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
        if (FontNeeds.IsCjk(_project.Language))
        {
            // Chinese, Japanese, Korean: a TrueType font, not pixel fonts (the guide says how); nothing is built or asked
            _status.Text = FontNeeds.CjkFontHint;
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

    /// <summary>The text editor of the pack open (the tests; null: none).</summary>
    internal TextEditorView? Editor => _editor;

    /// <summary>Runs the check as its button does (the window's pictures in the tests).</summary>
    internal void RunCheckForTests() => RunCheck();

    /// <summary>Opens a sample as its start page row does (its font offered when needed).</summary>
    internal void OpenSampleForTests(string folder) => Open(folder);

    /// <summary>Play in game (at the text, or from the start), as its buttons do; the status line after it.</summary>
    internal string PlayInGameForTests(bool atText)
    {
        RunGame(atText);
        return _status.Text ?? "";
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

    /// <summary>
    /// Where the game's translation test starts for <paramref name="entry"/> (DarklandsRP --start-at): a card text's card and passage,
    /// a screen text's screen; null for a text with neither (names, texts no session showed).
    /// </summary>
    internal static string? StartTarget(TextEntry entry) =>
        entry.Key.StartsWith("msg:", StringComparison.Ordinal) ? "card:" + entry.Key[4..]
        // a screen text: the screen its box records (StartAt.Screens), none for a text whose screen was not seen
        : TextTitles.Section(entry) == "Screens" && entry.Screens.FirstOrDefault(StartAt.Screens.ContainsKey) is { } screen ? "screen:" + screen
        : null;

    // the game with the pack: at the text chosen (a translation test, --start-at), or from the start
    private void RunGame(bool atText)
    {
        if (_project is null || _editor is null)
        {
            return;
        }
        _editor.Save();
        var program = WorkshopApp.GameProgram;
        if (!File.Exists(program))
        {
            _status.Text = $"The game's program is not in the folder above the Workshop's, nor beside it ({program}).";
            return;
        }
        // an older game refuses the options below and quits without a word: said here instead (WorkshopVersion.NeedsGame)
        if (WorkshopApp.OlderGame() is { } older)
        {
            _status.Text = $"Play in game needs the game {WorkshopVersion.NeedsGame} or later; the one beside the Workshop is {WorkshopApp.GameProgramVersion()}.";
            _ = SayOlderGame(older);
            return;
        }
        var start = new ProcessStartInfo(program) { UseShellExecute = false };
        start.ArgumentList.Add("--language-pack");
        start.ArgumentList.Add(_project.Folder);
        // a test game's saves go to the pack's own .workshop folder, never the player's SAVES (at a text and from the start alike)
        var saves = Path.Combine(_project.Folder, ".workshop", "saves");
        Directory.CreateDirectory(saves);
        start.ArgumentList.Add("--saves");
        start.ArgumentList.Add(saves);
        var target = atText && _editor.Current is { } current ? StartTarget(current) : null;
        if (target is not null)
        {
            start.ArgumentList.Add("--start-at");
            start.ArgumentList.Add(target);
        }
        if (_game is not null)
        {
            start.ArgumentList.Add(_game);
        }
        Process.Start(start);
        UpdateState();
        _status.Text = target is not null ? $"The game starts at {target[(target.IndexOf(':', StringComparison.Ordinal) + 1)..]} (a Quick Start party; saves go to the pack's .workshop folder)."
            : atText ? "This text is not on a card or a screen the game can open at: the game starts from the start."
            : "The game starts from the start (its saves go to the pack's .workshop folder).";
    }

    private async Task SayOlderGame(string words)
    {
        if (await new QuestionDialog("The game is older than the Workshop", words, "Open the releases page", "Close").ShowDialog<bool>(this))
        {
            _ = Launcher.LaunchUriAsync(new Uri(ProjectLinks.Releases));
        }
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
