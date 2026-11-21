using System.Diagnostics;

namespace Darklands.Presentation;

/// <summary>
/// The screen a text is being drawn on, for the developer's text boxes (the translators' editor groups the texts by screen, W-0): the
/// first type of the drawing call's stack that is one of the game's screens (a <c>...Screen</c>, a shop page, a message box, the menu
/// bar, the party panel ...). Read from the stack only in a developer build's recording runs; it never changes what is drawn.
/// </summary>
// Not in the original: a development aid (1.1, the Workshop's text editor).
public static class ScreenNames
{
    private static readonly HashSet<string> Others = new(StringComparer.Ordinal)
    {
        "BuySellPage", "MenuBar", "MessageBox", "PartyPanel", "SaveGameDialog", "TextInputBox", "Hourglass", "StartUp", "SideWindowMessages",
        "OverlandDrawing", "Cinematics",
    };

    /// <summary>The screen of the current drawing (null: none found).</summary>
    private static readonly HashSet<string> Overlays = new(StringComparer.Ordinal) { "MenuBar", "MessageBox", "SaveGameDialog", "TextInputBox" };

    public static string? Current()
    {
        // a screen on the stack wins over the helper it draws through (a shop page's line belongs to the barter, store or loot screen)
        string? helper = null;
        foreach (var frame in new StackTrace(1, false).GetFrames())
        {
            var type = frame.GetMethod()?.DeclaringType;
            while (type?.DeclaringType is not null)
            {
                type = type.DeclaringType;
            }
            if (type is null || type.Namespace is not { } space || !space.StartsWith("Darklands.Presentation", StringComparison.Ordinal))
            {
                continue;
            }
            var name = type.Name;
            // an overlay over a screen (the menu bar, a message box, the save dialog, a text input): its own texts, laid out on their own
            if (Overlays.Contains(name))
            {
                return name;
            }
            if (name.EndsWith("Screen", StringComparison.Ordinal) || name.EndsWith("Screens", StringComparison.Ordinal))
            {
                return name;
            }
            if (helper is null && Others.Contains(name))
            {
                helper = name;
            }
        }
        return helper;
    }
}
