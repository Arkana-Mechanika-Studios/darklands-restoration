using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Localization;

namespace Darklands.Presentation;

/// <summary>
/// A translation pack made from another edition of the game in the player's hands (the German edition, L-7g): its card texts taken by
/// key (the same <c>.MSG</c> file and block) where they keep the English block's structure (<see cref="CardTextForm.Check"/>), written
/// as a pack of the template's layout. The program holds none of either edition's words; the pack is the player's, for their own use
/// (a test of a long language on the screens), never one to share.
/// </summary>
// Not in the original: the 1.1 translations (L-7g).
public static class EditionPack
{
    /// <summary>What was taken: the blocks written, and the blocks left out with why (their key and the check's words).</summary>
    public sealed record Result(int Written, IReadOnlyList<(string Key, string Why)> LeftOut);

    /// <summary>Writes the pack of <paramref name="other"/>'s card texts in <paramref name="language"/> to <paramref name="folder"/>.</summary>
    public static Result Write(GameFiles english, GameFiles other, string language, string name, string folder)
    {
        ArgumentNullException.ThrowIfNull(english);
        ArgumentNullException.ThrowIfNull(other);
        var theirs = new GameAssets(other);
        var messages = new List<TranslatorTemplate.Message>();
        var leftOut = new List<(string, string)>();
        foreach (var e in OriginalTextExtractor.Extract(english).Entries.Where(e => e.Key.StartsWith("msg:", StringComparison.Ordinal)))
        {
            var written = CardTextForm.ToWritten(e.Raw);
            if (written.Length == 0)
            {
                continue;
            }
            var hash = e.Key.IndexOf('#', StringComparison.Ordinal);
            var file = e.Key[4..hash];
            var block = int.Parse(e.Key.AsSpan(hash + 1), System.Globalization.CultureInfo.InvariantCulture);
            string? translation;
            try
            {
                var blocks = theirs.Messages(file).Blocks;
                translation = block < blocks.Count ? CardTextForm.ToWritten(blocks[block].Text) : null;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or KeyNotFoundException or ArgumentException)
            {
                translation = null;
            }
            if (translation is null)
            {
                leftOut.Add((e.Key, "the other edition has no such block"));
                continue;
            }
            if (CardTextForm.Check(translation, e.Raw) is { } why)
            {
                leftOut.Add((e.Key, why));
                continue;
            }
            messages.Add(new TranslatorTemplate.Message("msg/" + file + ".ftl", KeyMangling.ToFluentId(e.Key), e.Key,
                TranslatorTemplate.CardValueOf(translation), [e.Key + " · from the other edition"]));
        }
        TranslatorTemplate.Write(folder, messages, language, name);
        return new Result(messages.Count, leftOut);
    }
}
