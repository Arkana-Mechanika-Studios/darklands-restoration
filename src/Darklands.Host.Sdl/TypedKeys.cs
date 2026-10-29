using Darklands.Launcher;

namespace Darklands.Host.Sdl;

/// <summary>
/// The launcher's typing when SDL sends no text events. SDL's text input can stay silent (sdl2-compat on SDL3 under some Wayland
/// compositors and input-method setups): until the first text event arrives while typing is on, each printable key pressed is
/// turned into its character (from the key's symbol in the keyboard layout, Shift and Caps Lock; the shifted punctuation of a
/// US layout). When the first real text event then arrives, the characters already made for the same keys are not typed twice,
/// and from then on only SDL's text counts.
/// </summary>
internal sealed class TypedKeys
{
    /// <summary>Text made from keys and not yet matched by a text event (a text event for them may still come), with when.</summary>
    private readonly Queue<(string Text, long At)> _made = new();

    /// <summary>A text event came while typing was on: SDL's typing works here.</summary>
    private bool _textWorks;

    /// <summary>Typing is on (the launcher is editing a field).</summary>
    public bool On { get; set; }

    /// <summary>What one SDL event gives the launcher: its own input (or none), and a character typed when SDL sends no text.</summary>
    public IEnumerable<LauncherInput> Take(SdlNative.SdlEvent e, LauncherInput? input) =>
        Take(e.Type, e.Type == SdlNative.EventKeyDown ? e.Keyboard.Keysym.Sym : 0, e.Type == SdlNative.EventKeyDown ? e.Keyboard.Keysym.Mod : 0,
            input, Environment.TickCount64);

    internal IEnumerable<LauncherInput> Take(uint type, int symbol, int modifiers, LauncherInput? input, long now)
    {
        if (type == SdlNative.EventTextInput && input is TextTyped typed)
        {
            if (On)
            {
                _textWorks = true;
            }
            // the text of keys already typed from the key events (within a second): dropped
            while (_made.Count > 0 && now - _made.Peek().At > 1000)
            {
                _made.Dequeue();
            }
            if (_made.Count > 0 && _made.Peek().Text == typed.Text)
            {
                _made.Dequeue();
                return [];
            }
            return [typed];
        }
        if (type == SdlNative.EventKeyDown && input is null && On && !_textWorks && Character(symbol, modifiers) is { } made)
        {
            _made.Enqueue((made, now));
            return [new TextTyped(made)];
        }
        return input is null ? [] : [input];
    }

    /// <summary>The character of a printable key (an SDL key symbol is its character in the layout), or null.</summary>
    internal static string? Character(int symbol, int modifiers)
    {
        if ((modifiers & (SdlNative.KeyModLeftCtrl | SdlNative.KeyModRightCtrl | SdlNative.KeyModAlt | SdlNative.KeyModGui | SdlNative.KeyModAltGr)) != 0
            || symbol < 0x20 || symbol > 0x7E)
        {
            return null;
        }
        var shift = (modifiers & (SdlNative.KeyModLeftShift | SdlNative.KeyModRightShift)) != 0;
        var c = (char)symbol;
        if (c is >= 'a' and <= 'z')
        {
            var caps = (modifiers & SdlNative.KeyModCaps) != 0;
            return (shift != caps ? char.ToUpperInvariant(c) : c).ToString();
        }
        if (!shift)
        {
            return c.ToString();
        }
        const string plain = "`1234567890-=[]\\;',./";
        const string shifted = "~!@#$%^&*()_+{}|:\"<>?";
        var i = plain.IndexOf(c);
        return (i >= 0 ? shifted[i] : c).ToString();
    }
}
