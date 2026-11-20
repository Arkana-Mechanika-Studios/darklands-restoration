using Darklands.Content;
using Darklands.Content.Formats.Characters;
using Darklands.Content.Formats.Saves;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Localization;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The Unicode text path (L-6b) leaves the game's own text on the byte path: every text of the player's files fits the bytes and is
/// drawn as its bytes are; the menus' texts read from tables have keys, and a pack keeps their hot keys.
/// </summary>
public sealed class UnicodePathTests
{
    [Fact]
    public void EveryTextOfTheGameKeepsTheBytePathAndItsPixels()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var assets = new GameAssets(files);
        var painters = new[] { 1, 2, 3 }.Select(slot => new TextPainter(assets.Font(slot))).ToList();
        var entries = OriginalTextExtractor.Extract(files).Entries;
        Assert.True(entries.Count > 9000);
        foreach (var e in entries)
        {
            var raw = e.Raw.ToArray();
            var text = GameTextCodec.Decode(raw);
            Assert.True(TextCodes.FitsBytes(text), $"{e.Key}: does not fit the bytes");
            if (raw.Length > 200 || e.Key.StartsWith("msg:", StringComparison.Ordinal))
            {
                continue;   // a card text is laid out, not drawn as one line (CodeLayoutTests)
            }
            foreach (var painter in painters)
            {
                var byString = new Canvas(new Screen());
                var byBytes = new Canvas(new Screen());
                Assert.Equal(painter.Draw(byBytes, 2, 3, raw, 15), painter.Draw(byString, 2, 3, text, 15));
                Assert.True(byBytes.Screen.Pixels.AsSpan().SequenceEqual(byString.Screen.Pixels), $"{e.Key}: pixels");
                Assert.Equal(painter.Measure(raw), painter.Measure(text));
                // and by codes (a pack session's translation path): the same
                var byCodes = new Canvas(new Screen());
                painter.DrawCodes(byCodes, 2, 3, TextCodes.OfBytes(raw), 15);
                Assert.True(byBytes.Screen.Pixels.AsSpan().SequenceEqual(byCodes.Screen.Pixels), $"{e.Key}: pixels by codes");
            }
        }
    }

    [Fact]
    public void TheMenusTextsReadFromTablesHaveTheirKeys()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var exe = files.Read("DARKLAND.EXE");
        var table = OriginalTextExtractor.Extract(files);
        var data = CreationDataDecoder.Decode(exe);
        Assert.Equal(data.Texts.Count, data.TextKeys.Count);
        var load = LoadScreenTexts.Decode(exe);
        var lists = new[] { (data.Texts, data.TextKeys), (load, LoadScreenTexts.Keys(exe)) };
        foreach (var (texts, keys) in lists)
        {
            for (var i = 0; i < texts.Count; i++)
            {
                if (texts[i].Length > 0)
                {
                    Assert.Equal(texts[i], table.Find(keys[i])?.Text);
                }
            }
        }
    }

    [Fact]
    public void APackKeepsTheMenusHotKeys()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var template = TranslatorTemplate.Build(files, []);
        var more = template.Single(m => m.Key == "exe:290E:0112");
        Assert.Equal(["M"], more.HotKeys);
        // a coloured phrase (an error message's) is no hot key: its words may change
        var message = template.Single(m => m.Key == "exe:DS:4FCE");
        Assert.Empty(message.HotKeys);
        Assert.Contains("<c12> M</c>", more.Value, StringComparison.Ordinal);
        var fewer = template.Single(m => m.Key == "exe:290E:0122");
        var folder = Path.Combine(Path.GetTempPath(), "dl-hotkeys-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            File.WriteAllText(Path.Combine(folder, "text", "de", "a.ftl"),
                more.Id + " = <c12> W</c>eitere Chargen\n" + fewer.Id + " = <c12> F</c>ünf weniger\n");
            var pack = LanguagePack.Load(folder, out _)!;
            var findings = LanguagePackCheck.Run(pack, new GameAssets(files), template, []);
            Assert.Contains(findings, f => f.Id == more.Id && f.Message.Contains("hot keys ('W')", StringComparison.Ordinal));
            Assert.DoesNotContain(findings, f => f.Id == fewer.Id);
            // the screens get the colour characters back
            Assert.Equal(TextCodes.ColourCharacter(12) + " F" + TextCodes.ColourCharacter(0x7F) + "ünf weniger", TextCodes.FromTags(pack.Localizer.Get(fewer.Key)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
