using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Darklands.Platform;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's start page (W-0 polish, the approved design): the project's banner (its emblem, the launcher's painting and
/// wordmark, the version, its links), then the translation packs found on this computer with their progress, the last one opened
/// first, and the actions: filter, open another folder, new pack. With no game folder or no pack, it says what to do.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class StartPage
{
    private const double Column = 920;
    private static readonly FontFamily Gothic = new("avares://DarklandsWorkshop/Assets/Fonts#Grenze Gotisch");
    private static readonly FontFamily Grenze = new("avares://DarklandsWorkshop/Assets/Fonts#Grenze");

    private readonly IReadOnlyList<PackSummary> _packs;
    private readonly int _total;
    private readonly string? _game;
    private readonly string? _last;
    private readonly StackPanel _rows = new();
    private readonly TextBox _filter;

    /// <summary>Opens a pack (its folder).</summary>
    public required Action<string> Open { get; init; }

    /// <summary>The new pack dialog.</summary>
    public required Action NewPack { get; init; }

    /// <summary>Opens a pack from another folder.</summary>
    public required Action OpenFolder { get; init; }

    /// <summary>Chooses the game folder.</summary>
    public required Action ChooseGame { get; init; }

    /// <summary>Opens a web address.</summary>
    public required Action<string> Link { get; init; }

    public StartPage(IReadOnlyList<PackSummary> packs, int total, string? game, string? last)
    {
        _packs = packs;
        _total = total;
        _game = game;
        _last = last;
        _filter = new TextBox { Watermark = "Filter packs", Width = 240, Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        _filter.TextChanged += (_, _) => FillRows();
    }

    /// <summary>The page.</summary>
    public Control Build()
    {
        var body = new StackPanel { Width = Column, Margin = new Thickness(0, 24, 0, 24), HorizontalAlignment = HorizontalAlignment.Center };
        if (_game is null)
        {
            body.Children.Add(Callout("Find your game first", "The Workshop reads the texts from your own Darklands. Choose the folder that holds DARKLAND.EXE.",
                "Choose the game folder…", ChooseGame));
        }
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Margin = new Thickness(0, 0, 0, 16) };
        head.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Translation packs", FontSize = 15, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Pick up where you left off, or start a new language.", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), Margin = new Thickness(0, 2, 0, 0) },
            },
        });
        Grid.SetColumn(_filter, 1);
        _filter.Margin = new Thickness(0, 0, 12, 0);
        head.Children.Add(_filter);
        var open = WorkshopTheme.Outline(WorkshopTheme.IconText("folder", "Open folder…", 14));
        open.Click += (_, _) => OpenFolder();
        open.Margin = new Thickness(0, 0, 8, 0);
        open.IsEnabled = _game is not null;
        Grid.SetColumn(open, 2);
        head.Children.Add(open);
        var create = WorkshopTheme.Primary(WorkshopTheme.IconText("plus", "New pack", 14));
        create.Click += (_, _) => NewPack();
        create.IsEnabled = _game is not null;
        Grid.SetColumn(create, 3);
        head.Children.Add(create);
        body.Children.Add(head);
        body.Children.Add(new Border
        {
            BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Background = WorkshopTheme.Brush(WorkshopTheme.Side),
            ClipToBounds = true,
            Child = _rows,
        });
        FillRows();
        var page = new DockPanel();
        var banner = Banner();
        DockPanel.SetDock(banner, Dock.Top);
        page.Children.Add(banner);
        page.Children.Add(new ScrollViewer { Content = body });
        return page;
    }

    // ---------------------------------------------------------------- the banner
    private Control Banner()
    {
        var band = new Grid { Height = 128, ClipToBounds = true };
        band.Children.Add(new Image
        {
            Source = Asset("banner.png"),
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        var bg = WorkshopTheme.Bg;
        Color A(double a) => Color.FromArgb((byte)(a * 255), bg.R, bg.G, bg.B);
        band.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(A(.96), 0), new GradientStop(A(.80), .36), new GradientStop(A(.35), .65), new GradientStop(A(.2), 1) },
            },
        });
        band.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(A(.9), 0), new GradientStop(A(0), .55) },
            },
        });
        var inner = new Grid { Width = Column, HorizontalAlignment = HorizontalAlignment.Center, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var emblem = new Border
        {
            Width = 88,
            Height = 88,
            CornerRadius = new CornerRadius(44),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 20, Color = Color.FromArgb(0x8C, 0, 0, 0) }),
            Child = new Image { Source = WorkshopTheme.Emblem, Stretch = Stretch.UniformToFill },
        };
        inner.Children.Add(emblem);
        var cream = WorkshopTheme.Brush(Color.Parse("#efe4c8"));
        var words = new StackPanel { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Darklands", FontFamily = Gothic, FontWeight = FontWeight.Black, FontSize = 40, Foreground = cream, VerticalAlignment = VerticalAlignment.Bottom },
                new TextBlock { Text = "Restoration Project", FontFamily = Grenze, FontWeight = FontWeight.Black, FontSize = 22, Foreground = cream, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) },
            },
        });
        words.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 0),
            Opacity = .8,
            Children =
            {
                new Image { Source = Asset("rule-left.png"), Height = 8 },
                new Image { Source = Asset("fleuron.png"), Height = 13 },
                new Image { Source = Asset("rule-right.png"), Height = 8 },
            },
        });
        words.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new TextBlock { Text = "Workshop · translations and packs", FontSize = 12.5, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) },
                WorkshopTheme.BetaBadge(),
                new TextBlock { Text = Versions(), FontSize = 12.5, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) },
            },
        });
        Grid.SetColumn(words, 1);
        inner.Children.Add(words);
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (icon, address, tip) in new[]
        {
            ("globe", ProjectLinks.Website, "The project's website"),
            ("github", ProjectLinks.Repository, "GitHub: the source, the releases and problem reports"),
            ("chat", ProjectLinks.Discord, "Discord: questions and help"),
        })
        {
            var button = WorkshopTheme.Ghost(WorkshopIcons.Make(icon, 16), tip);
            button.Background = WorkshopTheme.Brush(Color.FromArgb(0x73, bg.R, bg.G, bg.B));
            button.Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted);
            button.Padding = new Thickness(7);
            button.Click += (_, _) => Link(address);
            links.Children.Add(button);
        }
        Grid.SetColumn(links, 2);
        inner.Children.Add(links);
        band.Children.Add(inner);
        return new Border { Child = band, BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    /// <summary>"Restoration 1.1.0", and "· game 1.0.0" when the game beside the Workshop is another version.</summary>
    public static string Versions()
    {
        static string Clean(string? v) => (v ?? "").Split('+')[0].Trim();
        // the Workshop's own version (WorkshopVersion), then the Restoration's this build is of (Directory.Build.props)
        var own = Clean(typeof(StartPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        var words = "Workshop " + Darklands.Workshop.Text.WorkshopVersion.Current + " · Restoration " + own;
        var program = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "DarklandsRP.exe" : "DarklandsRP");
        if (File.Exists(program) && Clean(FileVersionInfo.GetVersionInfo(program).ProductVersion) is { Length: > 0 } game && game != own)
        {
            words += " · game " + game;
        }
        return words;
    }

    // ---------------------------------------------------------------- the packs
    private void FillRows()
    {
        _rows.Children.Clear();
        var header = Row(36);
        string[] heads = ["Language", "Pack", "Progress", "Changed", ""];
        for (var k = 0; k < heads.Length; k++)
        {
            var t = new TextBlock { Text = heads[k], FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(t, k);
            header.Children.Add(t);
        }
        _rows.Children.Add(Lined(header));
        var q = _filter.Text ?? "";
        // the last pack opened first, then the newest
        var shown = _packs.Where(p => q.Length == 0 || $"{p.Name} {p.Language} {p.Author} {Languages.Of(p.Language)}".Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(p => IsLast(p) ? 0 : 1).ThenByDescending(p => p.Changed).ToList();
        if (shown.Count == 0)
        {
            _rows.Children.Add(new StackPanel
            {
                Margin = new Thickness(16, 28),
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = _packs.Count == 0 ? "No translation pack yet" : "No pack matches the filter", FontSize = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = _packs.Count == 0 ? "Start one with New pack: choose a language, and the Workshop makes the folder." : "Clear the filter to see all your packs.",
                        FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center },
                },
            });
            return;
        }
        for (var k = 0; k < shown.Count; k++)
        {
            var row = PackRow(shown[k]);
            _rows.Children.Add(k < shown.Count - 1 ? Lined(row) : row);
        }
    }

    private static Grid Row(double height) => new()
    {
        Height = height,
        ColumnDefinitions = new ColumnDefinitions("1.5*,1.6*,200,120,28"),
        ColumnSpacing = 16,
        Margin = new Thickness(16, 0),
    };

    private bool IsLast(PackSummary pack) =>
        _last is not null && string.Equals(Path.TrimEndingDirectorySeparator(pack.Folder), Path.TrimEndingDirectorySeparator(_last), StringComparison.OrdinalIgnoreCase);

    private static Control Lined(Control c) => new Border { Child = c, BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(0, 0, 0, 1) };

    private Control PackRow(PackSummary pack)
    {
        var last = IsLast(pack);
        var row = Row(60);
        var language = Languages.Of(pack.Language);
        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = WorkshopTheme.Brush(WorkshopTheme.Raised),
            Child = new TextBlock
            {
                Text = pack.Language.Split('-')[0].ToUpperInvariant(),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = WorkshopTheme.Brush(last ? WorkshopTheme.Accent : WorkshopTheme.Muted),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                tile,
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = language?.Native ?? pack.Language, FontSize = 14, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = (language is null ? "" : language.English + " · ") + pack.Language, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) },
                    },
                },
            },
        });
        var name = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = pack.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = (pack.Author is { Length: > 0 } a ? a + " · " : "") + Short(pack.Folder), FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint), TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        ToolTip.SetTip(name, pack.Folder);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        var share = _total > 0 ? Math.Clamp((double)pack.Translated / _total, 0, 1) : 0;
        var n = CultureInfo.InvariantCulture;
        var progress = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border
                {
                    Height = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = WorkshopTheme.Brush(WorkshopTheme.Line),
                    Child = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = WorkshopTheme.Brush(WorkshopTheme.Accent), HorizontalAlignment = HorizontalAlignment.Left, Width = 200 * share },
                },
                new TextBlock
                {
                    Text = _total > 0 ? $"{pack.Translated.ToString("N0", n)} of {_total.ToString("N0", n)}" : $"{pack.Translated.ToString("N0", n)} translated",
                    FontSize = 12,
                    Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted),
                    Margin = new Thickness(0, 6, 0, 0),
                },
            },
        };
        Grid.SetColumn(progress, 2);
        row.Children.Add(progress);
        var changed = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = When(pack.Changed), FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) } },
        };
        if (last)
        {
            changed.Children.Add(new TextBlock { Text = "last opened", FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) });
        }
        Grid.SetColumn(changed, 3);
        row.Children.Add(changed);
        var go = WorkshopIcons.Make(last ? "arrow-right" : "chevron-right", 16, WorkshopTheme.Brush(last ? WorkshopTheme.Accent : WorkshopTheme.Faint));
        Grid.SetColumn(go, 4);
        row.Children.Add(go);
        var cell = new Border
        {
            Child = row,
            Background = last ? WorkshopTheme.Brush(WorkshopTheme.AccentSoft) : Brushes.Transparent,
            BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Accent),
            BorderThickness = new Thickness(last ? 2 : 0, 0, 0, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(cell, last ? "Continue with this pack (Enter)" : "Open this pack");
        cell.PointerEntered += (_, _) => cell.Background = WorkshopTheme.Brush(last ? Color.FromArgb(0x38, 0xC9, 0xA2, 0x4A) : WorkshopTheme.Hover);
        cell.PointerExited += (_, _) => cell.Background = last ? WorkshopTheme.Brush(WorkshopTheme.AccentSoft) : Brushes.Transparent;
        cell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed)
            {
                Open(pack.Folder);
            }
        };
        return cell;
    }

    private Control Callout(string title, string text, string action, Action run)
    {
        var button = WorkshopTheme.Primary(new TextBlock { Text = action });
        button.Click += (_, _) => run();
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 20),
            Padding = new Thickness(16, 14),
            CornerRadius = new CornerRadius(8),
            Background = WorkshopTheme.Brush(Color.FromArgb(0x1A, 0xD9, 0xA0, 0x3F)),
            BorderBrush = WorkshopTheme.Brush(Color.FromArgb(0x55, 0xD9, 0xA0, 0x3F)),
            BorderThickness = new Thickness(1),
            Child = new DockPanel
            {
                Children =
                {
                    WorkshopIcons.Make("alert", 18, WorkshopTheme.Brush(WorkshopTheme.Warn)),
                    new StackPanel
                    {
                        Margin = new Thickness(12, 0),
                        Children =
                        {
                            new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                            new TextBlock { Text = text, FontSize = 12, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) },
                            button,
                        },
                    },
                },
            },
        };
    }

    // "Today, 16:42", "Yesterday", "18 Nov", "18 Nov 2025"
    private static string When(DateTime at)
    {
        var today = DateTime.Today;
        return at.Date == today ? "Today, " + at.ToString("HH:mm", CultureInfo.InvariantCulture)
            : at.Date == today.AddDays(-1) ? "Yesterday"
            : at.Year == today.Year ? at.ToString("d MMM", CultureInfo.InvariantCulture)
            : at.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    // a folder's last two parts, "…\packs\fr"
    private static string Short(string folder)
    {
        var parts = Path.TrimEndingDirectorySeparator(folder).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length <= 3 ? folder : "…" + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts[^3..]);
    }

    private static Bitmap Asset(string name)
    {
        using var stream = AssetLoader.Open(new Uri("avares://DarklandsWorkshop/Assets/" + name));
        return new Bitmap(stream);
    }
}
