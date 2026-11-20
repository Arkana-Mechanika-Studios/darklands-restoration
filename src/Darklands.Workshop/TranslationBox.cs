using Avalonia;
using Avalonia.Controls;
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
        FontFamily = new FontFamily("Segoe UI, Inter, DejaVu Sans, sans-serif"),
        FontSize = 14,
        WordWrap = true,
        ShowLineNumbers = false,
        Background = WorkshopTheme.Brush(WorkshopTheme.Parchment),
        Foreground = WorkshopTheme.Brush(WorkshopTheme.Ink),
        Padding = new Thickness(8, 6),
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    public TranslationBox()
    {
        Child = _editor;
        Background = WorkshopTheme.Brush(WorkshopTheme.Parchment);
        BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        _editor.TextArea.TextView.LineTransformers.Add(new ChipColours());
        _editor.TextArea.SelectionBrush = WorkshopTheme.Brush(Color.FromArgb(0x80, WorkshopTheme.Gold.R, WorkshopTheme.Gold.G, WorkshopTheme.Gold.B));
        _editor.TextArea.Caret.CaretBrush = WorkshopTheme.Brush(WorkshopTheme.Ink);
        _editor.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        GotFocus += (_, _) => BorderBrush = WorkshopTheme.Brush(WorkshopTheme.Gold);
        LostFocus += (_, _) => BorderBrush = WorkshopTheme.Brush(WorkshopTheme.WoodLine);
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
        }
    }
}
