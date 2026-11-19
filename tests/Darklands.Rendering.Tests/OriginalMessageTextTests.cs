using System.Security.Cryptography;
using System.Text;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Characters;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The rules' messages in the original's words, read from the player's EXE (vacuous without the game files). No game text is
/// stored here: the words are pinned by a hash of the texts the 1.0 rules built themselves, and the pieces agree with the
/// original-text extractor's keys.
/// </summary>
public sealed class OriginalMessageTextTests
{
    private static readonly Lazy<OriginalMessageText?> Text = new(
        () => GameFolder.Files is { } files ? new OriginalMessageText(DarklandsExecutable.Load(files)) : null);

    private static readonly Lazy<StringTable?> Table = new(
        () => GameFolder.Files is { } files ? OriginalTextExtractor.Extract(files) : null);

    [Fact]
    public void TheWordsAreTheOnesTheRulesWroteBefore()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        string[] all =
        [
            text.Text(GameMessage.Notice(GameMessageIds.AlchemyFailure)),
            text.Text(GameMessage.Notice(GameMessageIds.AlchemySuccess)),
            text.Text(GameMessage.Notice(GameMessageIds.EventTableFull)),
            text.Text(GameMessage.Notice(GameMessageIds.NotEnoughMoney, 0x50)),
            text.Text(GameMessage.SkillChanged(0, (int)Skill.Religion, 13, 99)),
            text.Text(GameMessage.SkillChanged(20, (int)Skill.Stealth, 0, 3)),
        ];
        // SHA-256 of the six texts joined by line feeds, as 1.0's literals in Core gave them (EventExpiry, EventLog, CampStay, MemberRules)
        Assert.Equal("C1B206CA13EAD7786B7E7623D886488FDBF8FBA68BFD6FAD5FD71A0B84C309A4",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(string.Join('\n', all)))));
    }

    [Fact]
    public void ExeIdsAreTheExtractorsKeys()
    {
        if (Text.Value is not { } text || Table.Value is not { } table)
        {
            return;
        }
        foreach (var id in new[] { GameMessageIds.AlchemyFailure, GameMessageIds.AlchemySuccess, GameMessageIds.EventTableFull, GameMessageIds.NotEnoughMoney })
        {
            Assert.Equal(table.Find(id)?.Text, text.Text(GameMessage.Notice(id)));
            Assert.Equal(StringOrigin.Known, table.Find(id)!.Origin);
        }
        Assert.Equal(StringOrigin.Known, table.Find("exe:DS:26BE")?.Origin);
        Assert.Equal(StringOrigin.Known, table.Find("exe:DS:26CD")?.Origin);
        Assert.Equal(
            table.Find("exe:290E:0C42")!.Text + table.Find("exe:DS:26BE")!.Text + "4" + table.Find("exe:DS:26CD")!.Text + "5",
            text.Text(GameMessage.SkillChanged(1, 7, 4, 5)));
    }

    [Fact]
    public void TheCardsKeysAreTheExtractorsAndSayWhatTheLiteralsSaid()
    {
        if (Text.Value is not { } text || Table.Value is not { } table)
        {
            return;
        }
        var cards = Path.Combine(FindRepositoryRoot(), "src", "Darklands.Cards");
        var keys = Directory.EnumerateFiles(cards, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"(exe:(?:DS|[0-9A-F]{4}):[0-9A-F]{4})\"")
                .Select(m => m.Groups[1].Value))
            .Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(38, keys.Count);
        foreach (var key in keys)
        {
            var entry = table.Find(key);
            Assert.True(entry is { Origin: StringOrigin.Known }, $"{key} is not a known string of the extractor");
            Assert.Equal(entry!.Text, text.Text(GameText.Of(key)));
        }
        // SHA-256 of "key=text" (the game's bytes) over the sorted keys: each text was checked against the literal it replaced
        // (152 sites, scratch check of 2026-11-19), so this pins the 1.0 card hints.
        var joined = string.Join('\n', keys.Select(k => k + "=" + Encoding.Latin1.GetString(GameTextCodec.Encode(text.Text(GameText.Of(k))))));
        Assert.Equal("424CFEE3049642C6B54375116C66C92D19B440E77B275957098B510CEFD8CAE4",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(joined))));
    }

    [Fact]
    public void TheChanceTextsAreTheOnes1Point0Formatted()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        var all = new List<string>();
        foreach (var basic in new[] { 0, 1 })
        {
            for (var pct = 0; pct <= 100; pct++)
            {
                all.Add(text.Text(GameText.Of(GameTextIds.HintChance, ("chance", pct), ("basic", basic))));
            }
        }
        foreach (var (low, high) in new[] { (0, 0), (5, 100), (37, 64), (-10, 140) })
        {
            all.Add(text.Text(GameText.Of(GameTextIds.ChanceRange, ("low", low), ("high", high), ("basic", 1))));
        }
        for (var low = -2; low <= 7; low++)
        {
            for (var high = -2; high <= 7; high++)
            {
                all.Add(text.Text(GameText.Of(GameTextIds.ChanceRange, ("low", low), ("high", high), ("basic", 0))));
            }
        }
        // SHA-256 of the 306 texts joined by line feeds, as 1.0's CardScreen.ShowHint and CardContext.ChanceText built them
        Assert.Equal("8F1CE5FD4CC0DB201F9A339E3F6B88EB48AB51B8D1B53EF45DE7E3917D0963F2",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(string.Join('\n', all)))));
    }

    [Fact]
    public void TheRewritesOwnNoticesHaveWords()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        foreach (var id in OriginalMessageText.OwnIds)
        {
            var words = text.Text(GameText.Of(id, ("name", "X"), ("number", 7)));
            Assert.NotEqual(id, words);
            Assert.Contains(" ", words, StringComparison.Ordinal);
        }
        Assert.Equal("", text.Text(GameText.None));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Darklands.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    [Fact]
    public void AnUnknownIdIsShownAsItself()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        Assert.Equal("id:no.such.message", text.Text(GameMessage.Notice("id:no.such.message")));
    }
}
