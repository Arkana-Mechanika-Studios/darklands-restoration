using System.Text.Json;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Oracle.Tests;

/// <summary>
/// The barter rules against the original program: every case in Corpus/trading.json is the
/// result of executing item-grade, price-to-buy, price-to-sell or shop-stocks-item (overlay R032)
/// from DARKLAND.EXE with a generated item, place, city and party
/// (tools/oracle/build_trading_corpus.py).
/// </summary>
public sealed class TradingOracleTests
{
    private const int Code = 7;

    private static readonly Lazy<Corpus> Data = new(() => Corpus.Load("trading.json"));

    [Theory]
    [InlineData("thunk 09C0:205D")]
    [InlineData("R032+0x192E")]
    [InlineData("R032+0x1B26")]
    [InlineData("R032+0x1D18")]
    public void MatchesTheOriginalFunction(string function)
    {
        var cases = Raw(function);
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var i = c.GetProperty("inputs");
            var (s, shop) = Setup(i);
            var slot = Int(i, "slot");
            var result = function switch
            {
                "thunk 09C0:205D" => Trading.OfferedQuality(s, shop, Code),
                "R032+0x192E" => Trading.BuyPrice(s, shop, Code),
                "R032+0x1B26" => Trading.SellPrice(s, Code, Int(i, "quality")),
                _ => Trading.ShopHasItem(s, shop, Code, ref slot) ? 1 : 0,
            };
            var expected = (short)Corpus.Hex(c.GetProperty("ax").GetString()!);
            Assert.True(expected == (short)result, $"{function}: original {expected}, port {result}; {i}");
            Assert.True(c.GetProperty("holdrand").GetUInt32() == ((MscRandom)s.Random).State, $"{function}: random state; {i}");
            if (function == "R032+0x1D18")
            {
                Assert.True(c.GetProperty("slot").GetInt32() == (short)slot, $"stale slot: original {c.GetProperty("slot").GetInt32()}, port {slot}; {i}");
            }
        }
    }

    /// <summary>
    /// The whole stock listing (trade-screen-paint with its drawing calls skipped but leaving
    /// their return addresses on the stack, as the stock test's stale size slot depends on them).
    /// </summary>
    [Fact]
    public void StockListingMatchesTheOriginal()
    {
        var cases = Raw("stock");
        Assert.NotEmpty(cases);
        var differsWhenFixed = 0;
        foreach (var c in cases)
        {
            var i = c.GetProperty("inputs");
            var items = i.GetProperty("items").EnumerateArray().Select((it, k) => new ItemDefinition(k, "", "", Int(it, "type"),
                it.GetProperty("categories").GetUInt32(), Int(it, "weight"), Int(it, "quality"), Int(it, "value"), Int(it, "rarity"),
                Int(it, "tradeFlags"))).ToList();
            var expected = c.GetProperty("codes").EnumerateArray().Select(e => e.GetInt32()).ToList();
            var actual = Listing(i, items, fixedSize: false, out var state);
            Assert.True(expected.SequenceEqual(actual), $"stock: original [{string.Join(",", expected)}], port [{string.Join(",", actual)}]");
            Assert.Equal(c.GetProperty("holdrand").GetUInt32(), state);
            if (!Listing(i, items, fixedSize: true, out _).SequenceEqual(expected))
            {
                differsWhenFixed++;
            }
        }
        Assert.True(differsWhenFixed > 0, "the stale size slot should matter in some listings");
    }

    private static List<int> Listing(JsonElement i, List<ItemDefinition> items, bool fixedSize, out uint state)
    {
        var s = new GameState(new MscRandom(i.GetProperty("state").GetUInt32())) { Data = new Items(items), ClockSeed = () => 0 };
        s.Behaviour.FixShopStockSize = fixedSize;
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
        BitConverter.GetBytes((short)Int(i, "citySeed")).CopyTo(city, 0x56);
        s.City = new CityRecord(cur is >= 0 and < Location.CityCount ? cur : 0, city);
        for (var m = 0; m < 5; m++)
        {
            var member = new Character { Status = CharacterStatus.Able };
            member[Core.Characters.Attribute.Charisma] = Int(i, "charisma");
            s.Party.Members.Add(member);
        }
        s.Party.Fame = Int(i, "fame");
        s.Party.Seed = Int(i, "partySeed");
        var shop = new Shop(1, "", (long)i.GetProperty("categories").GetUInt32(), Int(i, "shopQuality"));
        var codes = Trading.Stock(s, shop).Codes.ToList();
        state = ((MscRandom)s.Random).State;
        return codes;
    }

    private sealed class Items(List<ItemDefinition> items) : IGameData
    {
        public IReadOnlyList<ItemDefinition> ItemDefinitions => items;

        public CityRecord LoadCity(int city) => new(city, new byte[CityRecord.Size]);
    }

    private static List<JsonElement> Raw(string function)
    {
        _ = Data.Value;   // validates the corpus header
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "trading.json");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("cases").EnumerateArray()
            .Where(c => c.GetProperty("function").GetString() == function)
            .Select(c => c.Clone())
            .ToList();
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
