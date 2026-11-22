using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>
/// The translation box (W-0 polish): a text editor (AvaloniaEdit) on parchment whose tags, names and arguments take the colours of
/// their chips as the translator types (line ends grey, option lines and indents blue, names and arguments green, colour tags red),
/// the words in ink. What it holds is the pack's text as written.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal sealed class TranslationBox : Border
{
    private readonly TextEditor _editor = new()
    {
        FontFamily = WorkshopTheme.UiFont,
        FontSize = 14,
        WordWrap = true,
        ShowLineNumbers = false,
        Background = WorkshopTheme.Brush(WorkshopTheme.Surface),
        Foreground = WorkshopTheme.Brush(WorkshopTheme.Ink),
        Padding = new Thickness(8, 6),
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    public TranslationBox()
    {
        Child = _editor;
        Background = WorkshopTheme.Brush(WorkshopTheme.Surface);
        BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        _editor.TextArea.TextView.LineTransformers.Add(_colours);
        _editor.TextArea.SelectionBrush = WorkshopTheme.Brush(Color.FromArgb(0x80, WorkshopTheme.Gold.R, WorkshopTheme.Gold.G, WorkshopTheme.Gold.B));
        _editor.TextArea.Caret.CaretBrush = WorkshopTheme.Brush(WorkshopTheme.Ink);
        _editor.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        // the editor's text area marks its focus events handled: heard here all the same
        AddHandler(GotFocusEvent, (_, _) => BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Gold), RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(LostFocusEvent, (_, _) => BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>The text as written.</summary>
    public string Text
    {
        get => _editor.Text;
        set => _editor.Text = value ?? "";
    }

    /// <summary>Where the cursor is (a chip goes there).</summary>
    public int CaretIndex
    {
        get => _editor.CaretOffset;
        set => _editor.CaretOffset = Math.Clamp(value, 0, _editor.Document.TextLength);
    }

    /// <summary>
    /// Whether a hot key (<c>&lt;c12&gt; M&lt;/c&gt;</c>) shows as the game draws it: the letter coloured and underlined, its tags
    /// hidden but kept (the cursor steps over each; a deleted letter shows them again). Off: the tags as written (Raw).
    /// </summary>
    public bool HotKeys
    {
        get => _colours.HotKeys;
        set
        {
            if (value == _colours.HotKeys)
            {
                return;
            }
            _colours.HotKeys = value;
            var generators = _editor.TextArea.TextView.ElementGenerators;
            if (value)
            {
                generators.Add(_hidden);
            }
            else
            {
                generators.Remove(_hidden);
            }
            _editor.TextArea.TextView.Redraw();
        }
    }

    private readonly ChipColours _colours = new();
    private readonly HiddenTags _hidden = new();

    // a hot key: its opening tag (with the padding after it), the letters, its closing tag
    private static readonly System.Text.RegularExpressions.Regex HotKey = new(@"(<c\d+>\s*)([^<\s][^<\n]*?)(</c>)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The text changed (typed, or set).</summary>
    public event EventHandler? TextChanged;

    /// <summary>Puts the cursor in the box.</summary>
    public void FocusText() => _editor.TextArea.Focus();

    /// <summary>Whether the translator may type (a text is chosen).</summary>
    public bool Editable
    {
        get => !_editor.IsReadOnly;
        set
        {
            _editor.IsReadOnly = !value;
            Opacity = value ? 1 : 0.6;
        }
    }

    // each line's tags, names and arguments in their chips' colours (TextTokens: the editor's chips)
    private sealed class ChipColours : DocumentColorizingTransformer
    {
        public bool HotKeys { get; set; }

        protected override void ColorizeLine(DocumentLine line)
        {
            var text = CurrentContext.Document.GetText(line);
            var at = line.Offset;
            foreach (var token in TextTokens.Split(text))
            {
                if (WorkshopTheme.ChipColourOf(token) is { } colour && token.Text.Length > 0)
                {
                    var brush = WorkshopTheme.Brush(colour);
                    ChangeLinePart(at, at + token.Text.Length, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(brush);
                        element.TextRunProperties.SetTypeface(new Typeface(element.TextRunProperties.Typeface.FontFamily, FontStyle.Normal, FontWeight.SemiBold));
                    });
                }
                at += token.Text.Length;
            }
            if (!HotKeys)
            {
                return;
            }
            var hot = WorkshopTheme.Brush(WorkshopTheme.ChipColour);
            foreach (System.Text.RegularExpressions.Match m in HotKey.Matches(text))
            {
                var start = line.Offset + m.Groups[2].Index;
                ChangeLinePart(start, start + m.Groups[2].Length, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(hot);
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                });
            }
        }
    }

    // a hot key's tags, hidden: each an empty element the cursor steps over
    private sealed class HiddenTags : VisualLineElementGenerator
    {
        private (int Start, int Length)? Next(int offset)
        {
            var line = CurrentContext.Document.GetLineByOffset(offset);
            var text = CurrentContext.Document.GetText(line);
            foreach (System.Text.RegularExpressions.Match m in HotKey.Matches(text))
            {
                foreach (var g in new[] { m.Groups[1], m.Groups[3] })
                {
                    if (line.Offset + g.Index >= offset)
                    {
                        return (line.Offset + g.Index, g.Length);
                    }
                }
            }
            return null;
        }

        public override int GetFirstInterestedOffset(int startOffset) => Next(startOffset) is { } next ? next.Start : -1;

        public override VisualLineElement? ConstructElement(int offset) =>
            Next(offset) is { } next && next.Start == offset ? new InlineObjectElement(next.Length, new Control { Width = 0, Height = 0 }) : null;
    }
}
