using System.Globalization;
using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Localization;
using Darklands.Presentation;

namespace Darklands.Rendering.Tests;

/// <summary>The translator's template and the pack check (L-7w): the template gives every text back, the check finds what a pack gets wrong.</summary>
public sealed class TranslatorTemplateTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "dl-template-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TheTemplateLoadedAsAPackGivesEveryTextBack()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var template = TranslatorTemplate.Build(files, []);
        var folder = TempFolder();
        try
        {
            TranslatorTemplate.Write(folder, template);
            var pack = LanguagePack.Load(folder, out var why);
            Assert.True(pack is not null, why);
            Assert.Empty(pack!.Problems);
            Assert.Equal(template.Count, pack.Messages.Count);
            var english = OriginalTextExtractor.Extract(files);
            var formats = 0;
            foreach (var m in template)
            {
                // as GameUi.Words gives it: the colour tags as the program's colour characters
                var got = Darklands.Core.TextCodes.FromTags(pack.Localizer.Get(m.Key, Arguments(m, english, out var expected)));
                if (m.Key.StartsWith("msg:", StringComparison.Ordinal))
                {
                    var back = CardTextForm.ToBytes(got, out var problem);
                    Assert.True(back is not null, $"{m.Key}: {problem}");
                    Assert.Equal(CardTextForm.ToWritten(english.Find(m.Key)!.Raw), CardTextForm.ToWritten(back));
                    continue;
                }
                formats += m.Variables.Count > 0 ? 1 : 0;
                Assert.True(expected == got, $"{m.Key}: \"{got}\", the English \"{expected}\"");
            }
            Assert.True(formats >= TranslatorNotes.Keys.Count(k => english.Find(k) is { Origin: StringOrigin.Known }), $"{formats} formats");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // the arguments a message is given and the English it must then show: a format's printed pieces, a notice's words
    private static Dictionary<string, object?>? Arguments(TranslatorTemplate.Message m, StringTable english, out string expected)
    {
        if (m.Key.StartsWith("id:", StringComparison.Ordinal))
        {
            var named = m.Variables.ToDictionary(v => v, v => (object)("5" + v), StringComparer.Ordinal);
            expected = OriginalMessageText.OwnWords(new Darklands.Core.GameText(m.Key, named))!;
            return named.ToDictionary(a => a.Key, a => (object?)a.Value, StringComparer.Ordinal);
        }
        var text = english.Find(m.Key)?.Text ?? "";
        if (m.Variables.Count == 0)
        {
            expected = text;
            return null;
        }
        var conversions = OriginalFormat.Conversions(text);
        var values = conversions.Select((c, n) => text[c.Start + c.Length - 1] is 's' ? (object)("Name" + n) : 40 + n).ToList();
        expected = OriginalFormat.Format(text, values);
        return OriginalFormat.Pieces(text, values)
            .Select((p, n) => (p, n)).ToDictionary(e => "arg" + e.n.ToString(CultureInfo.InvariantCulture), e => (object?)e.p, StringComparer.Ordinal);
    }

    [Fact]
    public void TheCheckFindsWhatAPackGetsWrong()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var template = TranslatorTemplate.Build(files, []);
        var assets = new GameAssets(files);
        var plain = template.First(m => m.Key.StartsWith("exe:DS:", StringComparison.Ordinal) && m.Variables.Count == 0 && !m.Value.Contains('{'));
        var other = template.Last(m => m.Key.StartsWith("exe:DS:", StringComparison.Ordinal) && m.Variables.Count == 0 && !m.Value.Contains('{'));
        var format = template.First(m => m.Key == "exe:DS:2F71");
        var cards = template.Where(m => m.Key.StartsWith("msg:", StringComparison.Ordinal) && m.Value.Contains("<opt>", StringComparison.Ordinal)).Take(2).ToList();
        var folder = TempFolder();
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            File.WriteAllText(Path.Combine(folder, "text", "de", "a.ftl"), string.Join("\n",
                "no-such-text = Unsinn",
                format.Id + " = { $arg0 } hat { $arg9 } Gulden",
                plain.Id + " = Ein viel zu langer Text für dieses kleine Feld",
                other.Id + " = Garçon",
                cards[0].Id + " = Nur ein Satz ohne Wahl.",
                cards[1].Id + " =" + string.Concat(Enumerable.Repeat(" Ein langer Satz, der immer weiter geht.", 120)) + CardTextForm.ToWritten(assets.Messages(cards[1].Key[4..cards[1].Key.IndexOf('#')]).Blocks[int.Parse(cards[1].Key[(cards[1].Key.IndexOf('#') + 1)..], CultureInfo.InvariantCulture)].Text),
                ""));
            var pack = LanguagePack.Load(folder, out _)!;
            var boxes = new[] { new TextBox(plain.Key, 30, 40, true, 1, null) };
            var findings = LanguagePackCheck.Run(pack, assets, template, boxes);
            string Of(string id) => string.Join(" | ", findings.Where(f => f.Id == id).Select(f => f.ToString()));
            Assert.Contains("error: no-such-text: no text of the game has this id", Of("no-such-text"));
            Assert.Contains("uses $arg9", Of(format.Id));
            Assert.Contains("note: " + format.Id + ": does not use $arg1", Of(format.Id));
            Assert.Contains("overflow", Of(plain.Id));
            Assert.Contains("40 px of room (cut at the window's edge", Of(plain.Id));
            Assert.Contains("the font has no 'ç'", Of(other.Id));
            Assert.Contains("option lines", Of(cards[0].Id));
            Assert.Contains("note: " + cards[1].Id + ": laid out in", Of(cards[1].Id));
            Assert.All(findings.Where(f => f.Id.Length > 0), f => Assert.StartsWith("text/de/a.ftl:", f.Where));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
