using System.Text.Json.Nodes;
using Darklands.Localization;

namespace Darklands.Workshop;

/// <summary>
/// Copying a pack into the game's packs/languages folder under a name the player gives (a sample started from, or a pack opened from
/// elsewhere added to the game): the name checked, an existing pack never overwritten, the test saves left behind.
/// </summary>
// Not in the original: the 1.1 Workshop.
internal static class PackNaming
{
    /// <summary>
    /// Why <paramref name="name"/> cannot be a new pack's folder in <paramref name="languages"/>, in words; null when it can (not
    /// empty, a plain folder name, no pack or folder of that name there yet).
    /// </summary>
    public static string? Problem(string languages, string name)
    {
        var n = name.Trim();
        if (n.Length == 0)
        {
            return "Give the pack a name.";
        }
        if (n is "." or ".." || n.StartsWith('.') || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            return $"\"{n}\" cannot be a folder's name: use letters, digits, '-' or '_'.";
        }
        if (Directory.Exists(Path.Combine(languages, n)) || File.Exists(Path.Combine(languages, n)))
        {
            return $"There is already a pack named \"{n}\" in the game's packs\\languages. Nothing is overwritten: choose another name.";
        }
        return null;
    }

    /// <summary><paramref name="wanted"/> when no folder of that name is in <paramref name="languages"/>, else wanted-2, wanted-3 …</summary>
    public static string FreeName(string languages, string wanted)
    {
        var name = wanted.Length > 0 ? wanted : "my-pack";
        for (var k = 2; Directory.Exists(Path.Combine(languages, name)); k++)
        {
            name = $"{wanted}-{k}";
        }
        return name;
    }

    /// <summary>Copies the pack in <paramref name="from"/> to <paramref name="to"/> (a new folder), all but its test saves (.workshop/saves).</summary>
    public static void Copy(string from, string to)
    {
        if (Directory.Exists(to))
        {
            throw new IOException($"{to} exists");
        }
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from));
        var saves = Path.Combine(source, ".workshop", "saves") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.StartsWith(saves, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var target = Path.Combine(to, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>A pack started from a sample is the player's: its name loses the " (sample)" mark (pack.json "name").</summary>
    public static void DropSampleMark(string folder)
    {
        var manifest = Path.Combine(folder, LanguagePack.ManifestName);
        if (!File.Exists(manifest) || JsonNode.Parse(File.ReadAllText(manifest)) is not JsonObject json || json["name"]?.GetValue<string>() is not { } name)
        {
            return;
        }
        const string Mark = " (sample)";
        if (name.EndsWith(Mark, StringComparison.Ordinal))
        {
            json["name"] = name[..^Mark.Length];
            File.WriteAllText(manifest, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        }
    }
}

/// <summary>A name asked for (the text, the answer's field with a suggestion); its result the name, or null when cancelled.</summary>
internal sealed class NameDialog : Avalonia.Controls.Window
{
    private readonly Avalonia.Controls.TextBox _name;

    public NameDialog(string title, string text, string suggested)
    {
        Title = title;
        Width = 520;
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        Background = WorkshopTheme.Brush(WorkshopTheme.Wood);
        _name = new Avalonia.Controls.TextBox { Text = suggested };
        var ok = new Avalonia.Controls.Button { Content = "OK", IsDefault = true };
        ok.Click += (_, _) => Close(_name.Text ?? "");
        var cancel = new Avalonia.Controls.Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        Content = new Avalonia.Controls.StackPanel
        {
            Spacing = 12,
            Margin = new Avalonia.Thickness(18),
            Children =
            {
                new Avalonia.Controls.TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _name,
                new Avalonia.Controls.StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        Opened += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }

    /// <summary>The name suggested (the tests read it).</summary>
    internal string Suggested => _name.Text ?? "";

    /// <summary>The words asked with (the tests read them).</summary>
    internal string Text => ((Avalonia.Controls.TextBlock)((Avalonia.Controls.StackPanel)Content!).Children[0]).Text ?? "";

    /// <summary>Answers as the player would: <paramref name="name"/> typed and OK, or null for Cancel (the tests).</summary>
    internal void Answer(string? name)
    {
        if (name is null)
        {
            Close(null);
            return;
        }
        _name.Text = name;
        Close(name);
    }
}
