using Darklands.Platform;

namespace Darklands.Presentation.Commands;

/// <summary>
/// One thing a player can do with the keyboard and the mouse: the only vocabulary a script or an interface convenience may use to
/// play (docs/COMMANDS.md). A command is carried out by the same code as the player's own key or click, at the screen's next look at
/// the input, so it can never do what the player could not (no skipped time, no extra or missing random draws).
/// </summary>
// C: new code, not the game's: the original reads the BIOS keyboard buffer and the mouse driver (see PlayerAction).
public abstract record PlayerCommand;

/// <summary>A key press, as a game key code (<see cref="Keys"/>): the hot keys and the menu bar's F10 see it as they see a typed key.</summary>
public sealed record PressKey(int Key) : PlayerCommand;

/// <summary>The pointer moved to (x, y) and the left button pressed there.</summary>
public sealed record ClickAt(int X, int Y) : PlayerCommand;

/// <summary>The pointer moved to (x, y), no button.</summary>
public sealed record PointAt(int X, int Y) : PlayerCommand;

/// <summary>
/// Line <paramref name="Line"/> (0 = the first) of the list the screen is choosing from, chosen as a player chooses it with the mouse:
/// the screen names where that line is (<see cref="GameUi.CommandTargets"/>; while a card's item or saint box is up, the box's lines),
/// the pointer goes there in one pass and clicks there in the next (<see cref="PointAt"/>, then <see cref="ClickAt"/>). Without such
/// a list, or past its end, it does nothing. On a card's item or saint line the click opens the box and chooses nothing, as the
/// player's does: the box's entry is the next <see cref="ChooseLine"/>.
/// </summary>
public sealed record ChooseLine(int Line) : PlayerCommand;

/// <summary>The commands waiting to be carried out, in order (empty in normal play: then the screens read only the host).</summary>
public sealed class CommandQueue
{
    private readonly LinkedList<PlayerCommand> _waiting = new();

    public int Count => _waiting.Count;

    public void Enqueue(PlayerCommand command) => _waiting.AddLast(command);

    public void Enqueue(IEnumerable<PlayerCommand> commands)
    {
        foreach (var command in commands)
        {
            _waiting.AddLast(command);
        }
    }

    /// <summary>Puts <paramref name="command"/> before the waiting ones (the second half of a command that takes two passes).</summary>
    internal void PutFirst(PlayerCommand command) => _waiting.AddFirst(command);

    public bool TryTake(out PlayerCommand command)
    {
        if (_waiting.First is not { } first)
        {
            command = null!;
            return false;
        }
        _waiting.RemoveFirst();
        command = first.Value;
        return true;
    }

    public void Clear() => _waiting.Clear();
}
