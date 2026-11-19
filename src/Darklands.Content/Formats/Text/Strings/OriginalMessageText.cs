using System.Globalization;
using Darklands.Content.Formats.Exe;
using Darklands.Core;

namespace Darklands.Content.Formats.Text.Strings;

/// <summary>
/// A rule's <see cref="GameMessage"/> in the original's words, read from the player's DARKLAND.EXE: the original-language source of
/// the messages, beside <see cref="OriginalTextExtractor"/> (an <c>exe:DS:offset</c> id is the string there; a built sentence is
/// put together from the EXE's pieces as the original does). Only the presentation asks it (the game carries ids: the L-0
/// architecture test); the localiser takes its place in front of the screens with the language setting.
/// </summary>
public sealed class OriginalMessageText(DarklandsExecutable exe)
{
    private readonly OriginalWords _words = new(exe);

    // orig: raise-skill-by R029+0x02EA and ability-change-notice R021+0x0080: the skill's name (the far table 290E:2137, OriginalWords.SkillNames), " changed from " (DS:26BE), the old
    //   value, " to " (DS:26CD), the new value, in text-bank-buffer (DS:9085)
    private const int ChangedFrom = 0x26BE;
    private const int To = 0x26CD;

    /// <summary>The message's text; an id it does not know comes back as the id itself (never an empty string).</summary>
    public string Text(GameMessage message)
    {
        if (message.Id == GameMessageIds.SkillChanged)
        {
            return _words.Text(OriginalWords.SkillNames, message.Number("skill")) + exe.String(ChangedFrom) + Number(message, "from") + exe.String(To) +
                Number(message, "to");
        }
        if (message.Id.StartsWith("exe:DS:", StringComparison.Ordinal) &&
            int.TryParse(message.Id.AsSpan(7), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset))
        {
            return exe.String(offset);
        }
        return message.Id;
    }

    private static string Number(GameMessage message, string name) => message.Number(name).ToString(CultureInfo.InvariantCulture);
}
