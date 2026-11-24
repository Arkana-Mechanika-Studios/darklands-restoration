using Darklands.Platform;
using Darklands.Presentation.Views;

namespace Darklands.Battles.Tests;

/// <summary>The view host (1.2, the view registry): it runs a registry view frame by frame over the game's frame, and touches nothing else.</summary>
public sealed class ViewHostTests
{
    private sealed class Surface(Screen? under, params ViewInput[][] frames) : IViewSurface
    {
        private int _frame;

        public Screen? Under => under;

        public bool QuitRequested { get; set; }

        public List<Screen> Shown { get; } = [];

        public int Waits { get; private set; }

        public IReadOnlyList<ViewInput> Inputs() => _frame < frames.Length ? frames[_frame++] : [];

        public void Show(Screen screen)
        {
            var copy = new Screen();
            screen.Pixels.CopyTo(copy.Pixels, 0);
            screen.Palette.CopyTo(copy.Palette, 0);
            Shown.Add(copy);
        }

        public void WaitFrame() => Waits++;
    }

    // a view that draws a mark in the corner and closes at a click
    private sealed class MarkView : IRegistryView
    {
        public string Id => "MarkView";

        public List<ViewInput> Seen { get; } = [];

        public void Draw(Screen screen) => screen.Pixels[0] = 7;

        public ViewResult Handle(ViewInput input)
        {
            Seen.Add(input);
            return input.Click ? ViewResult.Close : ViewResult.Stay;
        }
    }

    [Fact]
    public void AnEmptyViewShowsTheGamesFrameForItsFramesAndCloses()
    {
        var under = new Screen();
        under.Pixels[100] = 42;
        under.Palette[42] = 0xFF112233u;
        var surface = new Surface(under);
        var view = new EmptyView(5);
        Assert.Equal(ViewOutcome.Closed, ViewHost.Run(view, surface));
        Assert.Equal(5, view.Shown);
        Assert.Equal(5, surface.Shown.Count);
        Assert.Equal(5, surface.Waits);
        Assert.All(surface.Shown, s => Assert.Equal(42, s.Pixels[100]));
        Assert.All(surface.Shown, s => Assert.Equal(0xFF112233u, s.Palette[42]));
    }

    [Fact]
    public void EscClosesTheEmptyViewAtOnce()
    {
        var surface = new Surface(null, [new ViewInput(Keys.Escape, default, false)]);
        var view = new EmptyView(100);
        Assert.Equal(ViewOutcome.Closed, ViewHost.Run(view, surface));
        Assert.Equal(0, view.Shown);
    }

    [Fact]
    public void QuittingEndsTheViewAndTheGamesFrameIsNeverWritten()
    {
        var under = new Screen();
        var surface = new Surface(under, [], []);
        var view = new MarkView();
        var runs = 0;
        // the window's close button: the host says so after two frames
        var quitting = new QuitAfter(surface, 2, () => runs++);
        Assert.Equal(ViewOutcome.Quit, ViewHost.Run(view, quitting));
        Assert.Equal(2, surface.Shown.Count);
        Assert.All(surface.Shown, s => Assert.Equal(7, s.Pixels[0]));
        // the view drew on its own copy: the game's frame is as it was
        Assert.Equal(0, under.Pixels[0]);
    }

    [Fact]
    public void InputsReachTheViewInOrderAndAClickClosesIt()
    {
        var a = new ViewInput(Keys.ForCharacter('a'), new MouseState(10, 20, MouseButtons.None), false);
        var move = new ViewInput(null, new MouseState(30, 40, MouseButtons.None), false);
        var click = new ViewInput(null, new MouseState(30, 40, MouseButtons.Left), true);
        var surface = new Surface(null, [a], [], [move, click]);
        var view = new MarkView();
        Assert.Equal(ViewOutcome.Closed, ViewHost.Run(view, surface));
        Assert.Equal([a, default, move, click], view.Seen);
        Assert.Equal(2, surface.Shown.Count);
    }

    // a surface whose host asks to quit after a number of frames
    private sealed class QuitAfter(Surface inner, int frames, Action shown) : IViewSurface
    {
        public Screen? Under => inner.Under;

        public bool QuitRequested => inner.Shown.Count >= frames;

        public IReadOnlyList<ViewInput> Inputs() => inner.Inputs();

        public void Show(Screen screen)
        {
            inner.Show(screen);
            shown();
        }

        public void WaitFrame() => inner.WaitFrame();
    }
}
