using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Darklands.Workshop;

/// <summary>The Workshop's application: the Fluent theme in the Workshop's colours, its one window.</summary>
internal sealed class WorkshopApp : Application
{
    public override void Initialize()
    {
        // a pack's fonts are read as the game's hosts read them (font packs, docs/LOCALISATION.md)
        Darklands.Presentation.FontPack.ReadImage = Darklands.Rendering.PngCodec.ReadAlpha;
        // a pack's own decorated capitals (capitals/*.png), as the game reads them
        Darklands.Presentation.Graphics.PackCapitals.ReadImage = path => { var image = Darklands.Rendering.PngCodec.Read(path); return (image.Width, image.Height, image.Argb); };
        Darklands.Presentation.FontPack.LoadScalable = json => (Darklands.Rendering.TtfFont.Load(json, out var why), why);
        // a TrueType font's text in the preview: drawn again at a higher resolution, by the game's own text layer
        Darklands.Workshop.Text.TextPreview.DrawScalable = (screen, scale) =>
            new Darklands.Rendering.TextLayer().Item(screen, new Darklands.Platform.DisplayRectangle(0, 0, Darklands.Platform.Screen.Width * scale, Darklands.Platform.Screen.Height * scale))?.Texture.Argb;
        Styles.Add(new FluentTheme());
        // the text editor's look (AvaloniaEdit: the translation box)
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Darklands.Workshop/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        WorkshopTheme.Apply(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new WorkshopWindow(WorkshopSettings.Load(), desktop.Args ?? []);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
