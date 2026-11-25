using Darklands.Platform;

namespace Darklands.Presentation.Views;

/// <summary>What a registry view's input gives: stay open, or close.</summary>
public enum ViewResult
{
    Stay,
    Close,
}

/// <summary>How a registry view ended: closed by itself, or the player asked the program to quit while it was open.</summary>
public enum ViewOutcome
{
    Closed,
    Quit,
}

/// <summary>One input a registry view handles: a key (a game key code, <see cref="Keys"/>; null: none), the pointer, and whether the left button was pressed now.</summary>
public readonly record struct ViewInput(int? Key, MouseState Mouse, bool Click);

/// <summary>
/// A registry view (the 1.2 view registry, design 2.2): not a blocking loop but a drawing and an input handler that a host runs frame
/// by frame (<see cref="ViewHost"/>). It draws into a 320x200 indexed screen (the game's frame under it, as it was when the view
/// opened), and its input gives commands; it never writes the game's model.
/// </summary>
public interface IRegistryView
{
    /// <summary>Its id in the view catalogue (<see cref="ViewCatalogue"/>).</summary>
    string Id { get; }

    /// <summary>Draws one frame over <paramref name="screen"/>, which holds the game's frame as the view opened over it.</summary>
    void Draw(Screen screen);

    /// <summary>One input; a frame with none is handled as <c>default</c> (no key, the pointer where it is) so a view can count frames.</summary>
    ViewResult Handle(ViewInput input);
}

/// <summary>What a host gives the view host: the frame under the view, the input, the window, a wait of one frame of its own.</summary>
public interface IViewSurface
{
    /// <summary>The game's frame the view opened over (null: none shown yet).</summary>
    Screen? Under { get; }

    /// <summary>The player asked the program to quit (the window's close button).</summary>
    bool QuitRequested { get; }

    /// <summary>The inputs that came since the last call (the host's own events: none of them reaches the game).</summary>
    IReadOnlyList<ViewInput> Inputs();

    /// <summary>Shows a frame of the view (not the game's frame: screenshots and reports keep the game's).</summary>
    void Show(Screen screen);

    /// <summary>Waits a frame of the host's own time (never the game's clock, which stands still while a view is open).</summary>
    void WaitFrame();
}

/// <summary>
/// Runs a registry view until it closes (design 2.2, 3.1): input, then drawing, then showing, a frame at a time. The host calls it
/// from inside its own Pump, so the game's code is not running meanwhile; the host keeps the game's clock and sound still around it
/// (the true pause). It reads and changes nothing of the game.
/// </summary>
// Not in the original: the 1.2 view registry.
public static class ViewHost
{
    /// <summary>Runs <paramref name="view"/> on <paramref name="surface"/>; returns how it ended.</summary>
    public static ViewOutcome Run(IRegistryView view, IViewSurface surface)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(surface);
        var screen = new Screen();
        while (true)
        {
            if (surface.QuitRequested)
            {
                return ViewOutcome.Quit;
            }
            var inputs = surface.Inputs();
            foreach (var input in inputs.Count > 0 ? inputs : [default])
            {
                if (view.Handle(input) == ViewResult.Close)
                {
                    return ViewOutcome.Closed;
                }
            }
            if (surface.Under is { } under)
            {
                under.Pixels.CopyTo(screen.Pixels, 0);
                under.Palette.CopyTo(screen.Palette, 0);
            }
            view.Draw(screen);
            surface.Show(screen);
            surface.WaitFrame();
        }
    }
}

/// <summary>
/// A registry view that shows the game's frame as it is and closes after a number of frames, or at Esc: the true pause's test
/// (a view opened and closed in the middle of a recorded session must change nothing of it).
/// </summary>
public sealed class EmptyView(int frames) : IRegistryView
{
    private int _left = frames;

    public string Id => "EmptyView";

    /// <summary>The frames it was shown for.</summary>
    public int Shown { get; private set; }

    public void Draw(Screen screen) => Shown++;

    public ViewResult Handle(ViewInput input) => input.Key == Keys.Escape || _left-- <= 0 ? ViewResult.Close : ViewResult.Stay;
}
