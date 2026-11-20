using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Darklands.Workshop;

/// <summary>
/// The Workshop's look (W-0, the approved "ux2" design): a neutral dark tool, near-black and greys, one muted gold accent drawn from
/// the game for focus, selection and the primary actions; the platform's UI font on a 12 / 13 / 15 / 20 scale; the game's own look
/// only inside the preview, and the project's emblem and wordmark on the start page. The colour of each kind of chip in a text.
/// </summary>
// Not in the original: the 1.1 Workshop. Our own colours; the emblem, the painting and the wordmark's fonts are the project's own (the launcher's).
internal static class WorkshopTheme
{
    // the surfaces, from the window's ground up
    public static readonly Color Bg = Color.Parse("#151618");
    public static readonly Color Side = Color.Parse("#1b1c1f");
    public static readonly Color Surface = Color.Parse("#202125");
    public static readonly Color Raised = Color.Parse("#26282c");
    public static readonly Color Hover = Color.Parse("#2c2e33");
    public static readonly Color Line = Color.Parse("#2f3136");
    public static readonly Color Line2 = Color.Parse("#3a3c42");

    // the text
    public static readonly Color Text = Color.Parse("#e8e8ea");
    public static readonly Color Muted = Color.Parse("#a0a2a8");
    public static readonly Color Faint = Color.Parse("#6e7178");

    // the accent and the states
    public static readonly Color Accent = Color.Parse("#c9a24a");
    public static readonly Color AccentDark = Color.Parse("#8a6a2a");
    public static readonly Color AccentSoft = Color.FromArgb(0x24, 0xC9, 0xA2, 0x4A);
    public static readonly Color Ok = Color.Parse("#4fa46a");
    public static readonly Color Warn = Color.Parse("#d9a03f");
    public static readonly Color Bad = Color.Parse("#e0564e");

    // the editor's earlier names, on the new palette
    public static readonly Color Wood = Bg;
    public static readonly Color WoodLight = Side;
    public static readonly Color WoodLine = Line;
    public static readonly Color Parchment = Text;
    public static readonly Color ParchmentDim = Muted;
    public static readonly Color Ink = Text;
    public static readonly Color Gold = Accent;
    public static readonly Color GoldDark = AccentDark;
    public static readonly Color Translated = Ok;
    public static readonly Color Problem = Bad;
    public static readonly Color Untranslated = Faint;

    // the chips' colours inside the texts (on the dark surfaces): line ends, option lines and indents, names and arguments, colour tags
    public static readonly Color ChipBreak = Color.Parse("#8a8d94");
    public static readonly Color ChipOption = Color.Parse("#7aa7e0");
    public static readonly Color ChipName = Color.Parse("#8fc79c");
    public static readonly Color ChipColour = Color.Parse("#ef8a83");
    public static readonly Color ChipLiteral = Color.Parse("#c9a24a");

    public static IBrush Brush(Color c) => new SolidColorBrush(c);

    /// <summary>The UI font: the platform's (Segoe UI Variable on Windows), with the usual fallbacks.</summary>
    public static readonly FontFamily UiFont = new("avares://DarklandsWorkshop/Assets/Fonts#Inter, Segoe UI Variable Text, Segoe UI, Cantarell, Noto Sans, sans-serif");

    /// <summary>The colour a chip's text takes (null: words, the text colour).</summary>
    public static Color? ChipColourOf(Darklands.Workshop.Text.TextToken token) => token.Kind switch
    {
        Darklands.Workshop.Text.TokenKind.Name => ChipName,
        Darklands.Workshop.Text.TokenKind.Placeable when token.Text.StartsWith("{\"", StringComparison.Ordinal) => ChipLiteral,
        Darklands.Workshop.Text.TokenKind.Placeable => ChipName,
        Darklands.Workshop.Text.TokenKind.Tag when token.Text is "<br>" or "<p>" => ChipBreak,
        Darklands.Workshop.Text.TokenKind.Tag when token.Text == "</c>" || token.Text.StartsWith("<c", StringComparison.Ordinal) && token.Text.Length > 3 && char.IsAsciiDigit(token.Text[2]) => ChipColour,
        Darklands.Workshop.Text.TokenKind.Tag => ChipOption,
        _ => null,
    };

    /// <summary>The theme's resources and styles on the application.</summary>
    public static void Apply(Application app)
    {
        app.RequestedThemeVariant = ThemeVariant.Dark;
        var r = app.Resources;
        // the Fluent theme's own keys, so every control takes the colours
        r["SystemAccentColor"] = Accent;
        r["SystemAccentColorLight1"] = Accent;
        r["SystemAccentColorLight2"] = Accent;
        r["SystemAccentColorDark1"] = AccentDark;
        r["SolidBackgroundFillColorBase"] = Bg;
        r["ApplicationPageBackgroundThemeBrush"] = Brush(Bg);
        r["ContentControlThemeFontFamily"] = UiFont;
        r["ControlContentThemeFontSize"] = 13.0;
        r["TextControlBackground"] = Brush(Surface);
        r["TextControlBackgroundPointerOver"] = Brush(Surface);
        r["TextControlBackgroundFocused"] = Brush(Surface);
        r["TextControlForeground"] = Brush(Text);
        r["TextControlForegroundPointerOver"] = Brush(Text);
        r["TextControlForegroundFocused"] = Brush(Text);
        r["TextControlPlaceholderForeground"] = Brush(Faint);
        r["TextControlPlaceholderForegroundPointerOver"] = Brush(Faint);
        r["TextControlPlaceholderForegroundFocused"] = Brush(Faint);
        r["TextControlBorderBrush"] = Brush(Line2);
        r["TextControlBorderBrushPointerOver"] = Brush(Line2);
        r["TextControlBorderBrushFocused"] = Brush(Accent);
        r["TextControlSelectionHighlightColor"] = Brush(Color.FromArgb(0x70, Accent.R, Accent.G, Accent.B));
        r["ButtonBackground"] = Brush(Raised);
        r["ButtonBackgroundPointerOver"] = Brush(Hover);
        r["ButtonBackgroundPressed"] = Brush(Line2);
        r["ButtonForeground"] = Brush(Text);
        r["ButtonForegroundPointerOver"] = Brush(Text);
        r["ButtonForegroundPressed"] = Brush(Text);
        r["ButtonBorderBrush"] = Brush(Line2);
        r["ButtonBorderBrushPointerOver"] = Brush(Line2);
        r["ListBoxItemBackgroundPointerOver"] = Brush(Hover);
        r["ListBoxItemBackgroundSelected"] = Brush(AccentSoft);
        r["ListBoxItemBackgroundSelectedPointerOver"] = Brush(Color.FromArgb(0x38, Accent.R, Accent.G, Accent.B));
        r["ListBoxItemBackgroundSelectedPressed"] = Brush(Color.FromArgb(0x38, Accent.R, Accent.G, Accent.B));
        r["ComboBoxBackground"] = Brush(Surface);
        r["ComboBoxBackgroundPointerOver"] = Brush(Hover);
        r["ComboBoxForeground"] = Brush(Text);
        r["ComboBoxBorderBrush"] = Brush(Line2);
        r["ComboBoxDropDownBackground"] = Brush(Raised);
        r["ExpanderHeaderBackground"] = Brush(Side);
        r["ExpanderHeaderBackgroundPointerOver"] = Brush(Hover);
        r["ExpanderHeaderBackgroundPressed"] = Brush(Hover);
        r["ExpanderHeaderForeground"] = Brush(Text);
        r["ExpanderHeaderBorderBrush"] = Brush(Line);
        r["ExpanderContentBackground"] = Brush(Bg);
        r["ExpanderContentBorderBrush"] = Brush(Line);
        r["CheckBoxCheckBackgroundFillChecked"] = Brush(Accent);
        r["CheckBoxCheckBackgroundFillCheckedPointerOver"] = Brush(Accent);
        r["CheckBoxCheckGlyphForegroundChecked"] = Brush(Bg);
        r["TabItemHeaderForegroundSelected"] = Brush(Text);
        r["TabItemHeaderSelectedPipeFill"] = Brush(Accent);
        // a chosen line of a list: a soft accent and a mark at its left, its words as they are (the Fluent theme fills it with the accent)
        var soft = Brush(AccentSoft);
        var softer = Brush(Color.FromArgb(0x38, Accent.R, Accent.G, Accent.B));
        foreach (var (also, brush) in new[] { ((string?)null, soft), (":pointerover", softer), (":pressed", softer) })
        {
            app.Styles.Add(new Style(x =>
            {
                var item = x.OfType<ListBoxItem>().Class(":selected");
                return (also is null ? item : item.Class(also)).Template().Name("PART_ContentPresenter");
            })
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, brush) },
            });
        }
        app.Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters = { new Setter(ListBoxItem.ForegroundProperty, Brush(Text)) },
        });
    }

    /// <summary>A plain button: no fill until pointed at (the toolbar's).</summary>
    public static Button Ghost(Control content, string? tip = null)
    {
        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 5),
            CornerRadius = new CornerRadius(6),
            Foreground = Brush(Text),
        };
        if (tip is not null)
        {
            ToolTip.SetTip(button, tip);
        }
        return button;
    }

    /// <summary>The primary action (one per view): the accent filled.</summary>
    public static Button Primary(Control content) => new()
    {
        Content = content,
        Background = Brush(Accent),
        Foreground = Brush(Color.Parse("#17140c")),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(12, 6),
        CornerRadius = new CornerRadius(6),
        FontWeight = FontWeight.SemiBold,
    };

    /// <summary>A secondary action: outlined.</summary>
    public static Button Outline(Control content) => new()
    {
        Content = content,
        Background = Brushes.Transparent,
        BorderBrush = Brush(Line2),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(12, 6),
        CornerRadius = new CornerRadius(6),
        Foreground = Brush(Text),
    };

    /// <summary>An icon and words side by side (a button's content).</summary>
    public static Control IconText(string icon, string words, double size = 15) => new StackPanel
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        Spacing = 7,
        Children = { WorkshopIcons.Make(icon, size), new TextBlock { Text = words, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
    };

    /// <summary>A keycap (a shortcut's key, quiet).</summary>
    public static Control Key(string key) => new Border
    {
        BorderBrush = Brush(Line2),
        BorderThickness = new Thickness(1, 1, 1, 2),
        CornerRadius = new CornerRadius(4),
        Background = Brush(Surface),
        Padding = new Thickness(5, 0),
        Margin = new Thickness(2, 0),
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        Child = new TextBlock { Text = key, FontSize = 11, Foreground = Brush(Muted) },
    };

    /// <summary>The project's emblem (the game's icon: the castle, the red sun, "Darklands Restoration Project").</summary>
    public static Bitmap Emblem => _emblem ??= LoadEmblem();

    private static Bitmap? _emblem;

    private static Bitmap LoadEmblem()
    {
        using var stream = AssetLoader.Open(new Uri("avares://DarklandsWorkshop/Assets/emblem.png"));
        return new Bitmap(stream);
    }

    /// <summary>The Workshop's window icon: the project's emblem.</summary>
    public static WindowIcon Icon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://DarklandsWorkshop/Assets/DarklandsRP.ico"));
        return new WindowIcon(stream);
    }
}
