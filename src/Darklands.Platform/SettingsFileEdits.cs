using System.Text.Json;
using System.Text.Json.Nodes;

namespace Darklands.Platform;

/// <summary>Edits of an existing settings file that keep its comments and layout (the file is JSON with comments).</summary>
public static class SettingsFileEdits
{
    /// <summary>
    /// Sets "gameFolder" in the file at <paramref name="path"/> to <paramref name="folder"/> (written with forward slashes on
    /// Windows; elsewhere a backslash is a letter of a name and is kept).
    /// Only an empty or null value is replaced unless <paramref name="replace"/>; false (and why) when the file has no such
    /// key, already names a folder, or cannot be written.
    /// </summary>
    public static bool TrySetGameFolder(string path, string folder, bool replace, out string message)
    {
        try
        {
            var text = File.ReadAllText(path);
            var keys = JsoncMembers.Read(text);
            if (!keys.Values.TryGetValue("gameFolder", out var span))
            {
                message = $"{path} has no \"gameFolder\" key";
                return false;
            }
            var current = text[span.Start..span.End];
            if (!replace && current is not ("\"\"" or "null"))
            {
                message = $"{path} already names a game folder";
                return false;
            }
            var value = JsonSerializer.Serialize(Written(folder, Path.DirectorySeparatorChar));
            File.WriteAllText(path, text[..span.Start] + value + text[span.End..], new System.Text.UTF8Encoding(false));
            message = $"wrote \"gameFolder\": {value} into {path}";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"cannot update {path}: {e.Message}";
            return false;
        }
    }

    /// <summary>The folder as the settings file names it: forward slashes where the backslash separates folders (Windows).</summary>
    internal static string Written(string folder, char directorySeparator) =>
        directorySeparator == '\\' ? folder.Replace('\\', '/') : folder;

    /// <summary>
    /// Sets each dotted key ("display.fullscreen") to its value in the file at <paramref name="path"/>, keeping everything else
    /// as it is; a key the file lacks is added to its section (the section too, when missing). False (and why) when the file
    /// cannot be read, parsed or written; nothing is written then.
    /// </summary>
    public static bool TrySet(string path, IEnumerable<KeyValuePair<string, JsonNode?>> values, out string message)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"cannot read {path}: {e.Message}";
            return false;
        }
        string edited;
        try
        {
            edited = Set(text, values);
        }
        catch (FormatException e)
        {
            message = $"cannot update {path}: {e.Message}";
            return false;
        }
        try
        {
            File.WriteAllText(path, edited, new System.Text.UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            message = $"cannot update {path}: {e.Message}";
            return false;
        }
        message = $"saved the settings into {path}";
        return true;
    }

    /// <summary>The text with each dotted key set to its value (see <see cref="TrySet"/>). Throws <see cref="FormatException"/> on text that is not a JSON object.</summary>
    public static string Set(string text, IEnumerable<KeyValuePair<string, JsonNode?>> values)
    {
        foreach (var (key, node) in values)
        {
            var value = node?.ToJsonString() ?? "null";
            var members = JsoncMembers.Read(text);
            if (members.Values.TryGetValue(key, out var span))
            {
                text = text[..span.Start] + value + text[span.End..];
                continue;
            }
            // the deepest existing object on the key's path, and the rest of the path below it
            var parts = key.Split('.');
            var depth = parts.Length - 1;
            while (depth > 0 && !members.Objects.ContainsKey(string.Join('.', parts[..depth])))
            {
                depth--;
            }
            var parent = members.Objects[string.Join('.', parts[..depth])];
            var inner = value;
            for (var i = parts.Length - 1; i > depth; i--)
            {
                inner = "{ " + JsonSerializer.Serialize(parts[i]) + ": " + inner + " }";
            }
            var member = JsonSerializer.Serialize(parts[depth]) + ": " + inner;
            text = parent.LastValueEnd is { } last
                ? text[..last] + ",\n" + parent.Indent + member + text[last..]
                : text[..(parent.Open + 1)] + "\n" + parent.Indent + member + "\n" + parent.OuterIndent + text[(parent.Open + 1)..];
        }
        return text;
    }
}

/// <summary>
/// Where the members of a JSON-with-comments text are: each value's span by its dotted key, and each object's opening brace,
/// the end of its last value and the indentation of its members (for adding one). Only objects are followed, not arrays.
/// </summary>
internal sealed class JsoncMembers
{
    public sealed record ObjectSpan(int Open, int? LastValueEnd, string Indent, string OuterIndent);

    public Dictionary<string, (int Start, int End)> Values { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ObjectSpan> Objects { get; } = new(StringComparer.Ordinal);

    private readonly string _text;
    private int _at;

    private JsoncMembers(string text) => _text = text;

    public static JsoncMembers Read(string text)
    {
        var members = new JsoncMembers(text);
        members.Skip();
        if (members.Peek() != '{')
        {
            throw new FormatException("the settings are not a JSON object");
        }
        members.Object("");
        return members;
    }

    private char Peek() => _at < _text.Length ? _text[_at] : '\0';

    private void Object(string path)
    {
        var open = _at++;
        int? lastEnd = null;
        string? indent = null;
        while (true)
        {
            Skip();
            var c = Peek();
            if (c == '}')
            {
                _at++;
                break;
            }
            if (c == ',')
            {
                _at++;
                continue;
            }
            if (c != '"')
            {
                throw new FormatException($"unexpected '{c}' at {_at} where a key should be");
            }
            indent ??= LineIndent(_at);
            var name = JsonSerializer.Deserialize<string>(_text[_at..StringEnd()])!;
            Skip();
            if (Peek() != ':')
            {
                throw new FormatException($"':' expected after \"{name}\" at {_at}");
            }
            _at++;
            Skip();
            var key = path.Length == 0 ? name : path + "." + name;
            var start = _at;
            Value(key);
            Values[key] = (start, _at);
            lastEnd = _at;
        }
        var outer = LineIndent(open);
        Objects[path] = new ObjectSpan(open, lastEnd, indent ?? outer + "  ", outer);
    }

    private void Value(string key)
    {
        switch (Peek())
        {
            case '{':
                Object(key);
                break;
            case '[':
                Array();
                break;
            case '"':
                StringEnd();
                break;
            default:
                while (_at < _text.Length && !char.IsWhiteSpace(_text[_at]) && _text[_at] is not (',' or '}' or ']' or '/'))
                {
                    _at++;
                }
                break;
        }
    }

    private void Array()
    {
        _at++;
        while (true)
        {
            Skip();
            var c = Peek();
            if (c == '\0')
            {
                throw new FormatException("unterminated array");
            }
            if (c == ']')
            {
                _at++;
                return;
            }
            if (c == ',')
            {
                _at++;
                continue;
            }
            Value("");
        }
    }

    /// <summary>Steps over the string at the cursor; returns the index after its closing quote.</summary>
    private int StringEnd()
    {
        _at++;
        while (_at < _text.Length && _text[_at] != '"')
        {
            _at += _text[_at] == '\\' ? 2 : 1;
        }
        if (_at >= _text.Length)
        {
            throw new FormatException("unterminated string");
        }
        return ++_at;
    }

    /// <summary>Steps over white space and comments.</summary>
    private void Skip()
    {
        while (_at < _text.Length)
        {
            if (char.IsWhiteSpace(_text[_at]))
            {
                _at++;
            }
            else if (_text.AsSpan(_at).StartsWith("//"))
            {
                var end = _text.IndexOf('\n', _at);
                _at = end < 0 ? _text.Length : end + 1;
            }
            else if (_text.AsSpan(_at).StartsWith("/*"))
            {
                var end = _text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
                _at = end < 0 ? _text.Length : end + 2;
            }
            else
            {
                return;
            }
        }
    }

    private string LineIndent(int index)
    {
        var start = _text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = start;
        while (end < index && _text[end] is ' ' or '\t')
        {
            end++;
        }
        return _text[start..end];
    }
}
