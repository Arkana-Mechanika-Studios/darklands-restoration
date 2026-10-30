using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The reputation term of the shop prices (R032+0x1A94..0x1AD1 buy, R032+0x1C86..0x1CC3 sell) over reputations up to the 16-bit extremes:
/// DARKLANDS_TRADING_REP is a corpus of the format of Corpus/trading.json (local/batch/r2/rep_corpus.py). Without it the test passes vacuously.
/// </summary>
public sealed class TradingRepOracleTests
{
    private const int Code = 7;

    [Theory]
    [InlineData("R032+0x192E")]
    [InlineData("R032+0x1B26")]
    public void ReputationTermMatchesTheOriginal(string function)
    {
        if (Environment.GetEnvironmentVariable("DARKLANDS_TRADING_REP") is not { Length: > 0 } path || !File.Exists(path))
        {
            return;
        }
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var bad = new List<string>();
        var n = 0;
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("function").GetString() == function))
        {
            n++;
            var i = c.GetProperty("inputs");
            var (s, shop) = Setup(i);
            var result = function == "R032+0x192E" ? Trading.BuyPrice(s, shop, Code) : Trading.SellPrice(s, Code, Int(i, "quality"));
            var expected = (short)Corpus.Hex(c.GetProperty("ax").GetString()!);
            if (expected != (short)result || c.GetProperty("holdrand").GetUInt32() != ((MscRandom)s.Random).State)
            {
                bad.Add($"rep {Int(i, "rep")}: original {expected}, port {(short)result}");
            }
        }
        Assert.True(bad.Count == 0, $"{bad.Count} of {n} differ: {string.Join("; ", bad.Take(10))}");
        Assert.True(n > 0);
    }

    private static (GameState State, Shop Shop) Setup(JsonElement i)
    {
        var item = i.GetProperty("item");
        var data = new OneItem(new ItemDefinition(Code, "item", "", Int(item, "type"), item.GetProperty("categories").GetUInt32(),
            Int(item, "weight"), (byte)(sbyte)Int(item, "quality"), (ushort)(short)Int(item, "value"),
            (byte)(sbyte)Int(item, "rarity"), Int(item, "tradeFlags")));
        var s = new GameState(new MscRandom(i.GetProperty("state").GetUInt32())) { Data = data };
        for (var k = 0; k < 414; k++)
        {
            s.Locations.Add(new Location { Index = k });
        }
        var cur = Int(i, "curLoc");
        s.CurrentLocation = cur;
        if (cur >= 0)
        {
            s.Locations[cur].Flags = Int(i, "flags");
            s.Locations[cur].Reputation = Int(i, "rep");
        }
        var city = new byte[CityRecord.Size];
        BitConverter.GetBytes((short)Int(i, "citySize")).CopyTo(city, 0x40);
        s.City = new CityRecord(cur is >= 0 and < Location.CityCount ? cur : 0, city);
        for (var m = 0; m < 5; m++)
        {
            var member = new Character { Status = CharacterStatus.Able };
            member[Core.Characters.Attribute.Charisma] = m == Int(i, "leader") ? Int(i, "charisma") : 0;
            s.Party.Members.Add(member);
        }
        s.Party.LeaderIndex = Int(i, "leader");
        s.Party.Fame = Int(i, "fame");
        var shop = new Shop(1, "shop", (long)i.GetProperty("categories").GetUInt32(), Int(i, "shopQuality"));
        return (s, shop);
    }

    private static int Int(JsonElement e, string name) => e.GetProperty(name).GetInt32();

    private sealed class OneItem(ItemDefinition item) : IGameData
    {
        public IReadOnlyList<ItemDefinition> ItemDefinitions { get; } = Enumerable.Range(0, Code + 1)
            .Select(k => k == Code ? item : new ItemDefinition(k, "", "", 0, 0, 0, 0, 0)).ToList();

        public CityRecord LoadCity(int city) => new(city, new byte[CityRecord.Size]);
    }
}
