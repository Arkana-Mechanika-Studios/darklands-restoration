using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The pack's decorated capitals (docs/LOCALISATION.md, "Decorated capitals"): the first letters its card texts start with, each
/// drawn as the game draws it with where its picture comes from (the game's, the pack's, a Latin look-alike's, or none: the letter in
/// a box); a PNG imported for a letter (checked for its size and colours), one taken out; the game's A–Z saved as templates.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class CapitalsWindow : Window
{
    private readonly TextProject _project;
    private readonly TextPreview _preview;
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
    private readonly TextBox _letter = new() { Watermark = "Ж", Width = 56, MaxLength = 2 };

    /// <summary>A capital was imported or taken out (the editor's preview reads them again).</summary>
    public event Action? Changed;

    public CapitalsWindow(TextProject project)
    {
        _project = project;
        _preview = new TextPreview(project);
        Title = "Decorated capitals";
        Width = 640;
        Height = 620;
        FontFamily = WorkshopTheme.UiFont;
        Background = WorkshopTheme.Brush(WorkshopTheme.Bg);
        Foreground = WorkshopTheme.Brush(WorkshopTheme.Text);
        var intro = new TextBlock
        {
            Text = "A card text opens with a decorated capital. The game has pictures of A–Z; another letter takes the picture of its Latin " +
                "look-alike (В: B), else it is drawn in a box. Your pack can bring its own: a 20×20 PNG in the capitals' colours (start " +
                "from the game's, saved as templates), one a letter, in the pack's capitals folder.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted),
        };
        var add = new Button { Content = "Import PNG for this letter…" };
        add.Click += async (_, _) =>
        {
            if ((_letter.Text ?? "").Trim() is { Length: > 0 } typed && PackCapitalsCode(typed) is { } code)
            {
                await PickAndImport(code);
            }
            else
            {
                Say("Type the letter first (one capital letter).", WorkshopTheme.Warn);
            }
        };
        var templates = new Button { Content = "Save the game's A–Z as templates…" };
        templates.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "A folder for the 26 templates", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folder)
            {
                var n = SaveTemplates(folder);
                Say($"Saved {n} templates (A.png … Z.png) in {folder}.", WorkshopTheme.Ok);
            }
        };
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();
        var other = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = "Another letter:", VerticalAlignment = VerticalAlignment.Center, Foreground = WorkshopTheme.Brush(WorkshopTheme.Muted) }, _letter, add },
        };
        var foot = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        DockPanel.SetDock(close, Dock.Right);
        foot.Children.Add(close);
        foot.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { templates } });
        var top = new StackPanel { Spacing = 10, Children = { intro, other, _message } };
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(foot, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(foot);
        root.Children.Add(new ScrollViewer { Content = _rows, Margin = new Thickness(0, 10, 0, 0) });
        Content = root;
        Fill();
    }

    private static int? PackCapitalsCode(string typed) =>
        typed.Length == 1 || (typed.Length == 2 && char.IsSurrogatePair(typed, 0)) ? char.ConvertToUtf32(typed, 0) : null;

    /// <summary>The letters listed (tests).</summary>
    internal IReadOnlyList<CapitalUse> Listed { get; private set; } = [];

    /// <summary>The last message (tests).</summary>
    internal string Message => _message.Text ?? "";

    private void Say(string text, Color colour)
    {
        _message.Text = text;
        _message.Foreground = WorkshopTheme.Brush(colour);
    }

    private void Fill()
    {
        _rows.Children.Clear();
        _preview.ReloadFonts();
        Listed = CapitalList.Of(_project, _preview.Capitals);
        foreach (var problem in _preview.Capitals.Problems)
        {
            _rows.Children.Add(new TextBlock { Text = problem, Foreground = WorkshopTheme.Brush(WorkshopTheme.Bad), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        if (Listed.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = "No card text is translated yet: its first letters are listed here as you translate.", Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint) });
        }
        foreach (var use in Listed)
        {
            _rows.Children.Add(Row(use));
        }
    }

    private Control Row(CapitalUse use)
    {
        var (words, colour) = use.Source switch
        {
            CapitalSource.Game => ("the game's picture", WorkshopTheme.Ok),
            CapitalSource.Pack => ("your pack's picture", WorkshopTheme.Ok),
            CapitalSource.LookAlike => ($"the game's picture of {use.LookAlike} (its look-alike)", WorkshopTheme.Muted),
            _ => ("no picture: the letter in a box", WorkshopTheme.Warn),
        };
        var picture = new Image { Width = 40, Height = 40, Source = Bitmap(_preview.Capital(use.Code)) };
        RenderOptions.SetBitmapInterpolationMode(picture, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        var import = new Button { Content = use.Source == CapitalSource.Pack ? "Replace…" : "Import PNG…", FontSize = 12 };
        import.Click += async (_, _) => await PickAndImport(use.Code);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { import } };
        if (use.Source == CapitalSource.Pack)
        {
            var remove = new Button { Content = "Remove", FontSize = 12 };
            remove.Click += (_, _) =>
            {
                CapitalList.Remove(_project, use.Code);
                Say($"Took out the pack's {use.Letter}.", WorkshopTheme.Muted);
                Changed?.Invoke();
                Fill();
            };
            actions.Children.Add(remove);
        }
        var row = new DockPanel { Margin = new Thickness(0, 2) };
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        row.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Border { Child = picture, BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Line), BorderThickness = new Thickness(1) },
                new TextBlock { Text = use.Letter, FontSize = 20, Width = 30, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = words, Foreground = WorkshopTheme.Brush(colour), FontSize = 12.5 },
                        new TextBlock
                        {
                            Text = (use.Texts == 1 ? "1 card text starts with it" : $"{use.Texts} card texts start with it") + $" · U+{use.Code:X4}",
                            Foreground = WorkshopTheme.Brush(WorkshopTheme.Faint),
                            FontSize = 11.5,
                        },
                    },
                },
            },
        });
        return new Border { Child = row, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(6), Background = WorkshopTheme.Brush(WorkshopTheme.Surface) };
    }

    private static Avalonia.Media.Imaging.WriteableBitmap Bitmap(uint[] argb)
    {
        var size = Darklands.Presentation.Graphics.PackCapitals.Size;
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using var frame = bitmap.Lock();
        for (var row = 0; row < size; row++)
        {
            var line = Array.ConvertAll(argb[(row * size)..((row + 1) * size)], v => unchecked((int)v));
            System.Runtime.InteropServices.Marshal.Copy(line, 0, frame.Address + row * frame.RowBytes, size);
        }
        return bitmap;
    }

    private async Task PickAndImport(int code)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"A 20×20 PNG of {char.ConvertFromUtf32(code)} in the capitals' colours",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PNG pictures") { Patterns = ["*.png"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            ImportFile(code, path);
        }
    }

    /// <summary>Imports <paramref name="png"/> as <paramref name="code"/>'s capital; says what happened (tests call it too).</summary>
    internal bool ImportFile(int code, string png)
    {
        if (CapitalList.Import(_project, code, png) is { } why)
        {
            Say($"{Path.GetFileName(png)} is not used: {why}.", WorkshopTheme.Bad);
            return false;
        }
        Say($"{char.ConvertFromUtf32(code)}: your pack's picture now (capitals/{Darklands.Presentation.Graphics.PackCapitals.FileNameOf(code)}).", WorkshopTheme.Ok);
        Changed?.Invoke();
        Fill();
        return true;
    }

    /// <summary>Writes the game's A–Z capitals as PNGs into <paramref name="folder"/>; how many (tests call it too).</summary>
    internal int SaveTemplates(string folder)
    {
        Directory.CreateDirectory(folder);
        var size = Darklands.Presentation.Graphics.PackCapitals.Size;
        var n = 0;
        foreach (var (letter, argb) in CapitalList.Templates(_project.Game))
        {
            Darklands.Rendering.PngCodec.Write(new Darklands.Rendering.RgbaImage(size, size, argb), Path.Combine(folder, letter + ".png"));
            n++;
        }
        return n;
    }
}
