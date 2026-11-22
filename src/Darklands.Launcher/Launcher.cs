using Darklands.Launcher.Drawing;

namespace Darklands.Launcher;

/// <summary>The launcher in the look of the original game's menus (<see cref="LauncherScreen"/>).</summary>
public sealed class Launcher : ILauncher
{
    public LauncherResult Run(LauncherContext context, ILauncherSurface surface)
    {
        var model = new LauncherModel(context, surface.DisplaySize);
        // the packs read while the player looks at the screen (the Language row's first click would otherwise read them all)
        _ = model.WarmPacks();
        var screen = new LauncherScreen(model, context, surface);
        Canvas? canvas = null;
        while (true)
        {
            var (width, height) = surface.Size;
            if (canvas is null || canvas.Width != width || canvas.Height != height)
            {
                canvas = new Canvas(width, height);
            }
            screen.Render(canvas);
            surface.Controls(screen.ControlAreas);
            surface.Present(canvas.Pixels);
            // the pointer came to a control: a short wait, then its tooltip
            foreach (var input in screen.TooltipPending ? surface.Wait(LauncherScreen.TooltipDelay) : surface.Wait())
            {
                if (screen.Handle(input) is { } choice)
                {
                    return new LauncherResult(choice, model.Settings(), model.Changes(playing: choice == LauncherChoice.Play));
                }
            }
        }
    }
}
