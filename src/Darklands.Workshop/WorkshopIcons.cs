using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's line icons: 24-unit outlines drawn as strokes (round caps and joins, 1.75 wide), in the style of the Lucide icon
/// set (ISC licence, THIRD_PARTY_NOTICES.md). Each takes the colour of the text around it.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal static class WorkshopIcons
{
    private static string Circle(double cx, double cy, double r) =>
        FormattableString.Invariant($"M{cx - r},{cy} a{r},{r} 0 1 0 {2 * r},0 a{r},{r} 0 1 0 {-2 * r},0");

    private static readonly Dictionary<string, string> Paths = new(StringComparer.Ordinal)
    {
        ["folder"] = "M4 20h16a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.7-.9l-.8-1.2A2 2 0 0 0 7.9 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        ["plus"] = "M5 12h14M12 5v14",
        ["save"] = "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7M7 3v4a1 1 0 0 0 1 1h7",
        ["check"] = "M20 6 9 17l-5-5",
        ["check-circle"] = Circle(12, 12, 10) + "M9 12l2 2 4-4",
        ["play"] = "M6 3 20 12 6 21Z",
        ["book"] = "M12 7v14M3 18a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h5a4 4 0 0 1 4 4 4 4 0 0 1 4-4h5a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1h-6a3 3 0 0 0-3 3 3 3 0 0 0-3-3z",
        ["braces"] = "M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1",
        ["type"] = "M4 7V4h16v3M9 20h6M12 4v16",
        ["search"] = Circle(11, 11, 8) + "M21 21l-4.3-4.3",
        ["chevron-down"] = "M6 9l6 6 6-6",
        ["chevron-right"] = "M9 18l6-6-6-6",
        ["more"] = Circle(12, 12, 1) + Circle(19, 12, 1) + Circle(5, 12, 1),
        ["alert"] = "M21.7 18l-8-14a2 2 0 0 0-3.5 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.7-3M12 9v4M12 17h.01",
        ["globe"] = Circle(12, 12, 10) + "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20M2 12h20",
        ["gamepad"] = "M6 12h4M8 10v4M15 13h.01M18 11h.01M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z",
        ["x"] = "M18 6 6 18M6 6l12 12",
        ["arrow-right"] = "M5 12h14M12 5l7 7-7 7",
        ["github"] = "M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.4 5.4 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65S8.93 17.38 9 18v4M9 18c-4.51 2-5-2-7-2",
        ["chat"] = "M7.9 20A9 9 0 1 0 4 16.1L2 22Z",
        ["languages"] = "M5 8l6 6M4 14l6-6 2-3M2 5h12M7 2h1M22 22l-5-10-5 10M14 18h6",
    };

    /// <summary>The icon <paramref name="name"/> at <paramref name="size"/> pixels, in the text's colour (or <paramref name="colour"/>).</summary>
    public static Control Make(string name, double size = 16, IBrush? colour = null)
    {
        var path = new Path
        {
            Data = StreamGeometry.Parse(Paths[name]),
            StrokeThickness = 1.75,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.None,
            Width = 24,
            Height = 24,
        };
        if (name == "play")
        {
            path.Fill = colour;
        }
        if (colour is not null)
        {
            path.Stroke = colour;
        }
        else
        {
            // the colour of the text around it (the button's or the block's foreground)
            path.Bind(Shape.StrokeProperty, path.GetObservable(TextElement.ForegroundProperty));
            if (name == "play")
            {
                path.Bind(Shape.FillProperty, path.GetObservable(TextElement.ForegroundProperty));
            }
        }
        return new Viewbox { Width = size, Height = size, Child = path, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    }
}
