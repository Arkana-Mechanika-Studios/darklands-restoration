using Darklands.Platform;

namespace Darklands.Presentation.Commands;

/// <summary>
/// What the player's key or click means on a card screen (its option menu and its wait after a text): the input side reads the
/// action, the card screen carries out the intent (docs/COMMANDS.md). Whether an intent does something (a highlighted line, the
/// pointer over the list, an open box) is the screen's to judge, as the original's loop does.
/// </summary>
// C: new code, not the game's: the tests of card-choice-menu R043+0x1BD2 and press-to-continue R043+0x32B6 on the key and the button, made explicit
public abstract record CardIntent
{
    /// <summary>The intent of <paramref name="action"/>, null for an action the card screens drop.</summary>
    /// <param name="windowX">The card window's left edge: a press left of it is on the portraits.</param>
    /// <param name="numberKeys">The digits 1..9 choose lines (GameUi.KeyboardCardMenus, an interface convenience).</param>
    public static CardIntent? Of(PlayerAction action, int windowX, bool numberKeys)
    {
        if (action.IsLeftClick)
        {
            return action.X < windowX ? new PressOnPortraits(action.Y) : new PressAt(action.X, action.Y);
        }
        if (action.Key is Keys.Enter or Keys.Space)
        {
            return new Confirm();
        }
        if (action.Key is Keys.Up or Keys.Down)
        {
            return new MoveHighlight(action.Key == Keys.Down ? 1 : -1);
        }
        var digit = (action.Key & 0xFF) - '1';
        if (numberKeys && action.Key >= '1' && digit is >= 0 and < 9)
        {
            return new ChooseByNumber(digit);
        }
        return null;
    }
}

/// <summary>A left press on the party's portraits (left of the card window), at screen row <paramref name="Y"/>: that member's sheet.</summary>
public sealed record PressOnPortraits(int Y) : CardIntent;

/// <summary>A left press at (x, y) in the card window: the line under it, when it is one.</summary>
public sealed record PressAt(int X, int Y) : CardIntent;

/// <summary>Enter or Space: the highlighted line, or the end of the wait after a text.</summary>
public sealed record Confirm : CardIntent;

/// <summary>Up (-1) or Down (+1): the highlight to the line before or after, wrapping.</summary>
public sealed record MoveHighlight(int Step) : CardIntent;

/// <summary>A digit 1..9 (as 0..8): the main list's line of that number (an interface convenience, not the original's).</summary>
public sealed record ChooseByNumber(int Index) : CardIntent;
