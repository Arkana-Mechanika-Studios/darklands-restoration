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
        Darklands.Presentation.FontPack.LoadScalable = json => (Darklands.Rendering.TtfFont.Load(json, out var why), why);
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
