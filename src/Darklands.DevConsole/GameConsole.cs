namespace Darklands.DevConsole;

/// <summary>One line of the console's scrollback.</summary>
public readonly record struct ConsoleLine(string Text, ConsoleLineKind Kind);

public enum ConsoleLineKind
{
    Output,
    Command,
    Error,
}

/// <summary>A command: its name, its usage line, and what it does with the words after the name.</summary>
public sealed record ConsoleCommand(string Name, string Usage, Func<string[], ConsoleResult> Run);

/// <summary>What a command answers: lines to show, as output or as an error.</summary>
public sealed record ConsoleResult(IReadOnlyList<string> Lines, bool IsError = false)
{
    public static ConsoleResult Ok(params string[] lines) => new(lines);

    public static ConsoleResult Error(params string[] lines) => new(lines, true);
}

/// <summary>
/// The developer console: a host feature outside the game. It keeps the input line (with a cursor), the commands
/// typed before (Up / Down) and the scrollback, and runs the commands registered with it. The host draws it and
/// feeds it text and editing keys; the game never sees it.
/// </summary>
public sealed class GameConsole
{
    public const int ScrollbackLimit = 1000;

    private readonly Dictionary<string, ConsoleCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ConsoleLine> _lines = [];
    private readonly List<string> _history = [];
    private int _historyAt;

    public GameConsole()
    {
        Add(new ConsoleCommand("help", "help [command]", Help));
        Add(new ConsoleCommand("clear", "clear", _ =>
        {
            _lines.Clear();
            return ConsoleResult.Ok();
        }));
    }

    /// <summary>Open: the host shows it and keeps the game's input from the game.</summary>
    public bool IsOpen { get; set; }

    /// <summary>The line being typed.</summary>
    public string Input { get; private set; } = "";

    /// <summary>The cursor's place in <see cref="Input"/>.</summary>
    public int Cursor { get; private set; }

    /// <summary>Lines scrolled back from the newest (0: the newest line is at the bottom).</summary>
    public int ScrollOffset { get; private set; }

    public IReadOnlyList<ConsoleLine> Lines => _lines;

    public IReadOnlyCollection<ConsoleCommand> Commands => _commands.Values;

    public void Add(ConsoleCommand command) => _commands[command.Name] = command;

    public void Toggle() => IsOpen = !IsOpen;

    public void Write(string text, ConsoleLineKind kind = ConsoleLineKind.Output)
    {
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            _lines.Add(new ConsoleLine(line, kind));
        }
        if (_lines.Count > ScrollbackLimit)
        {
            _lines.RemoveRange(0, _lines.Count - ScrollbackLimit);
        }
        ScrollOffset = 0;
    }

    // ---- editing

    public void Type(string text)
    {
        var printable = new string(text.Where(c => !char.IsControl(c)).ToArray());
        Input = Input.Insert(Cursor, printable);
        Cursor += printable.Length;
    }

    public void Backspace()
    {
        if (Cursor > 0)
        {
            Input = Input.Remove(Cursor - 1, 1);
            Cursor--;
        }
    }

    public void Delete()
    {
        if (Cursor < Input.Length)
        {
            Input = Input.Remove(Cursor, 1);
        }
    }

    public void MoveCursor(int by) => Cursor = Math.Clamp(Cursor + by, 0, Input.Length);

    public void Home() => Cursor = 0;

    public void End() => Cursor = Input.Length;

    /// <summary>The command typed before (-1) or after (+1) in the history.</summary>
    public void Recall(int by)
    {
        if (_history.Count == 0)
        {
            return;
        }
        _historyAt = Math.Clamp(_historyAt + by, 0, _history.Count);
        Input = _historyAt == _history.Count ? "" : _history[_historyAt];
        Cursor = Input.Length;
    }

    public void Scroll(int lines) => ScrollOffset = Math.Clamp(ScrollOffset + lines, 0, Math.Max(0, _lines.Count - 1));

    /// <summary>Runs the input line and clears it.</summary>
    public void Submit()
    {
        var line = Input.Trim();
        Input = "";
        Cursor = 0;
        if (line.Length == 0)
        {
            return;
        }
        if (_history.Count == 0 || _history[^1] != line)
        {
            _history.Add(line);
        }
        _historyAt = _history.Count;
        Execute(line);
    }

    /// <summary>Runs one command line and writes it and its answer to the scrollback.</summary>
    public ConsoleResult Execute(string line)
    {
        Write("> " + line, ConsoleLineKind.Command);
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ConsoleResult result;
        if (words.Length == 0)
        {
            result = ConsoleResult.Ok();
        }
        else if (!_commands.TryGetValue(words[0], out var command))
        {
            result = ConsoleResult.Error($"unknown command \"{words[0]}\"; type help");
        }
        else
        {
            try
            {
                result = command.Run(words[1..]);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or InvalidOperationException)
            {
                result = ConsoleResult.Error(e.Message, "usage: " + command.Usage);
            }
        }
        foreach (var text in result.Lines)
        {
            Write(text, result.IsError ? ConsoleLineKind.Error : ConsoleLineKind.Output);
        }
        return result;
    }

    private ConsoleResult Help(string[] words)
    {
        if (words.Length > 0)
        {
            return _commands.TryGetValue(words[0], out var command)
                ? ConsoleResult.Ok(command.Usage)
                : ConsoleResult.Error($"unknown command \"{words[0]}\"");
        }
        return new ConsoleResult(_commands.Values.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => c.Usage).ToList());
    }
}
