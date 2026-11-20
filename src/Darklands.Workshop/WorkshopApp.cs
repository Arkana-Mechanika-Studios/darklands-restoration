using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Darklands.Workshop;

/// <summary>The Workshop's application: the Fluent theme in the Workshop's colours, its one window.</summary>
internal sealed class WorkshopApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
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
