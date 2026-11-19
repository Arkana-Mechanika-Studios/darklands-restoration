using System.Security.Cryptography;
using System.Text;
using Darklands.Content.Formats.Exe;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Rules;

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
        Assert.Equal(76, keys.Count);
        foreach (var key in keys)
        {
            var entry = table.Find(key);
            Assert.True(entry is { Origin: StringOrigin.Known }, $"{key} is not a known string of the extractor");
            Assert.Equal(entry!.Text, text.Text(GameText.Of(key)));
        }
        // SHA-256 of "key=text" (the game's bytes) over the sorted keys: each text was checked against the literal it replaced
        // (152 sites, scratch check of 2026-11-19; the 36 words the scripts fill in and the two deposit questions, CardWordsTests), so this pins the 1.0 card texts.
        var joined = string.Join('\n', keys.Select(k => k + "=" + Encoding.Latin1.GetString(GameTextCodec.Encode(text.Text(GameText.Of(k))))));
        Assert.Equal("A4AAE3280FDDE27797AD6FA7DCFD77FB9B159A03B039AAAF1343B826BDFC522B",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(joined))));
    }

    [Fact]
    public void TheScreensKeysAreTheExtractorsAndSayWhatTheLiteralsSaid()
    {
        if (Text.Value is not { } text || Table.Value is not { } table)
        {
            return;
        }
        var keys = KeysIn("Darklands.Presentation");
        Assert.Equal(353, keys.Count);
        foreach (var key in keys.Concat([SaintPrayer.CallSuccessful, SaintPrayer.CallFailsWithLoss, SaintPrayer.CallFails]))
        {
            var entry = table.Find(key);
            Assert.True(entry is { Origin: StringOrigin.Known }, $"{key} is not a known string of the extractor");
            Assert.Equal(entry!.Text, text.Text(GameText.Of(key)));
        }
        // SHA-256 of "key=text" (the game's bytes) over the sorted keys: each text was read back against the literal it replaced (L-6)
        var joined = string.Join('\n', keys.Select(k => k + "=" + Encoding.Latin1.GetString(GameTextCodec.Encode(text.Text(GameText.Of(k))))));
        Assert.Equal("3BC82A6EC1FE3D170A7FC7B674261349B132507E56BCBE302CEC68D43848AEBD",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(joined))));
        Assert.Equal(text.Text(GameText.Of(SaintPrayer.CallFails)), text.Text(GameText.Of(SaintPrayer.CallFailsWithLoss)));
    }

    [Fact]
    public void TheScreensFormatsPrintWhat1Point0Printed()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        string F(string key, params object[] args) => text.Text(GameText.Format(key, args));
        var all = new List<string>();
        foreach (var (place, y, m, d, h) in new[] { ("Koeln", 1415, 3, 17, 9), ("Ulm", 1402, 12, 1, 23) })
        {
            all.Add(F("exe:DS:4815", place, y, m, d, h));
        }
        foreach (var v in new[] { 0, 5, 42, 99 })
        {
            all.Add(F("exe:DS:3566", v));
            all.Add(F("exe:DS:3579", v));
        }
        foreach (var (fl, gr, pf) in new (short, short, short)[] { (0, 0, 0), (12, 19, 11), (-3, 4, 5), (32767, 0, 1) })
        {
            var purse = new Darklands.Core.Economy.Money(fl, gr, pf);
            all.Add(F("exe:DS:4CC8", fl, gr, pf));
            all.Add(F("exe:DS:383B", "Anna", fl, gr, pf));
            all.Add(F("exe:DS:2F71", "Anna", fl, gr, pf, purse.TotalPfennigs));
        }
        foreach (var (name, count, q) in new[] { ("Longsword", 1, 25), ("Arrows", 120, 7), ("Ale", -56, -3) })
        {
            all.Add(F("exe:DS:4D1B", name, count, q));
            all.Add(F("exe:DS:3988", name, count, q));
        }
        all.Add(F("exe:DS:4D07", "Anna"));
        all.Add(F("exe:DS:4D93", "Anna"));
        foreach (var n in new[] { 0, 7, 31 })
        {
            all.Add(F("exe:DS:33B8", n));
            all.Add(F("exe:DS:3355", n));
        }
        all.Add(F("exe:DS:33E2", 3, "Sulphur", 1));
        all.Add(F("exe:DS:32AA", "Anna", 4));
        all.Add(F("exe:DS:32CC", 20, 0, 30, 25, 12, 63));
        all.Add(F("exe:DS:330E", "Medium", 12));
        all.Add(F("exe:DS:2FB8", "She", "Hans", "Blacksmith"));
        all.Add(F("exe:DS:2FD5", "Anna", "herself", "Blacksmith"));
        all.Add(F("exe:DS:3018", "Blacksmith"));
        foreach (var price in new[] { 0, 99, 40000, -5 })
        {
            all.Add(F("exe:DS:303E", price, "Longsword", 1, 25));
            all.Add(F("exe:DS:3055", price, "Longsword", 30, 8));
            all.Add(F("exe:DS:31E9", price, "Longsword", 30, -2));
        }
        // SHA-256 of the 55 lines as 1.0's interpolations printed the same samples (scratch formats_hash.py mirrors each one)
        Assert.Equal(55, all.Count);
        Assert.Equal("53B8C32F4BA86E2680A9F5D335A64C7CEC8F3FBBA9B3E50C0EC24DD714EE65AF",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(string.Join('\n', all)))));
    }

    [Fact]
    public void TheSecondSetsFormatsPrintWhat1Point0Printed()
    {
        if (Text.Value is not { } text)
        {
            return;
        }
        string F(string key, params object[] args) => text.Text(GameText.Format(key, args));
        var all = new List<string>();
        foreach (var price in new[] { 0, 1, 1000 })
        {
            all.Add(F("exe:DS:3DEE", price / 12, price % 12));
            foreach (var (fl, gr, pf) in new[] { (0, 0, 0), (12, 19, 11) })
            {
                all.Add(F("exe:DS:3E5A", price / 12, price % 12, fl, gr, pf));
                all.Add(F("exe:DS:3ECE", price / 12, price % 12, fl, gr, pf));
            }
        }
        foreach (var (days, risk) in new[] { (1, 0), (12, 47) })
        {
            all.Add(F("exe:DS:3F4C", days, risk / 5));
        }
        all.Add(F("exe:DS:3FE7", "Anna"));
        foreach (var gain in new[] { 0, 3, 9 })
        {
            all.Add(F("exe:DS:3A2C", gain, "x"));
            all.Add(F("exe:DS:3A34", gain, "x"));
            all.Add(F("exe:DS:3A4F", gain, "x"));
            all.Add(F("exe:DS:3A70", gain, "potions"));
            all.Add(F("exe:DS:3A7B", gain, "Day Laborer"));
            all.Add(F("exe:DS:3A90", gain, "x"));
        }
        all.Add(F("exe:DS:3AA4", "40pfs"));
        foreach (var v in new[] { 0, 7, 32767 })
        {
            all.Add(F("exe:DS:375A", v));
            all.Add(F("exe:DS:3765", v));
            all.Add(F("exe:DS:3771", v));
            all.Add(F("exe:DS:377E", v * 240));
            all.Add(F("exe:DS:3795", v));
            all.Add(F("exe:DS:37A0", v));
        }
        all.Add(F("exe:DS:3752", "north"));
        foreach (var (name, q, count) in new[] { ("Longsword", 25, 1), ("Arrows", 7, 120) })
        {
            all.Add(F("exe:DS:2DC2", name, q, count));
        }
        foreach (var (a, b) in new[] { (0, 0), (5, 12), (25, 30) })
        {
            all.Add(F("exe:DS:2E21", a, b));
            all.Add(F("exe:DS:2E73", a));
        }
        all.Add(F("exe:DS:23DC", "the second"));
        // SHA-256 of the 65 lines as 1.0's interpolations printed the same samples (scratch formats2_hash.py mirrors each one)
        Assert.Equal(65, all.Count);
        Assert.Equal("4280A8EDBAA71AD56FF90A27491061B0E26600DC9569E29635A8C677B61EF39D",
            Convert.ToHexString(SHA256.HashData(Encoding.Latin1.GetBytes(string.Join('\n', all)))));
        // the original's "%02d" prints a negative value with its sign and one digit ("-5"; 1.0's C# printed "-05" for a negative load)
        Assert.Equal("-5/40", F("exe:DS:2E21", -5, 40));
    }

    private static List<string> KeysIn(string project) =>
        Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "\"(exe:(?:DS|[0-9A-F]{4}):[0-9A-F]{4})\"")
                .Select(m => m.Groups[1].Value))
            .Distinct().Order(StringComparer.Ordinal).ToList();

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
            var words = text.Text(GameText.Of(id, ("name", "X"), ("number", 7), ("count", 2)));
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
