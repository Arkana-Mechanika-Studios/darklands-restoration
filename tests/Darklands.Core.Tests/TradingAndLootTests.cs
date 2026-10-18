using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;
using Darklands.Core.World;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

public sealed class TradingAndLootTests
{
    /// <summary>
    /// A game in city 5 of size 4 (ordinal 17, goods merchant quality 25), leader Charisma 25
    /// (no Charisma term), no standing, no fame. Item 10: type 10, kind bit 2, value 100,
    /// quality 25, rarity 3. Item 11 cannot be traded.
    /// </summary>
    private static GameState Make(int location = 5, int citySize = 4, uint seed = 1)
    {
        var s = TestGame.Create(seed, location);
        var data = (TestData)s.Data;
        data.CityBytes[0x40] = (byte)citySize;
        data.CityBytes[0x56] = 17;
        data.CityBytes[0x62 + 1] = 25;
        s.Party.Members[0][Attribute.Charisma] = 25;
        data.Items[10] = data.Items[10] with { Type = 10, Flags = 2, Value = 100, Quality = 25, Rarity = 3 };
        data.Items[11] = data.Items[11] with { Type = 11, Flags = 2, Value = 100, Quality = 25, TradeFlags = 0x80 };
        return s;
    }

    private static void SetItem(GameState s, int code, Func<ItemDefinition, ItemDefinition> change)
    {
        var items = ((TestData)s.Data).Items;
        items[code] = change(items[code]);
    }

    private static Shop Goods(GameState s) => Trading.Open(s, 1, null, 2);

    private static uint FindSeed(Func<MscRandom, bool> test)
    {
        for (uint seed = 1; ; seed++)
        {
            if (test(new MscRandom(seed)))
            {
                return seed;
            }
        }
    }

    private static uint State(GameState s) => ((MscRandom)s.Random).State;

    // ---- shop -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 25)]
    [InlineData(3, 40)]
    [InlineData(8, -10)]      // byte 0xF6 read as a signed byte
    [InlineData(-2, 10)]      // the pawnshop
    [InlineData(9, 25)]       // a trader without a shop
    [InlineData(-1, 25)]
    public void ShopQualityReadsTheCityRecord(int shop, int expected)
    {
        var s = Make();
        var bytes = ((TestData)s.Data).CityBytes;
        bytes[0x62] = 7;
        bytes[0x62 + 3] = 40;
        bytes[0x62 + 8] = 0xF6;
        Assert.Equal(expected, Trading.ShopQuality(s, shop));
    }

    [Fact]
    public void OpenNamesCityShopsByDefault()
    {
        var s = Make();
        var shop = Trading.Open(s, 1, null, 0x2);
        Assert.Equal("Goods Merchant", shop.Name);
        Assert.Equal(1, shop.Number);
        Assert.Equal(0x2, shop.Categories);
        Assert.Equal(25, shop.Quality);
        Assert.Equal("Swordsmith", Trading.Open(s, 2, null, 0).Name);
        Assert.Equal("Toms Wares", Trading.Open(s, 1, "Toms Wares", 0).Name);
        Assert.Equal("", Trading.Open(s, 1, "", 0).Name);                      // an empty name is a name (only null falls back)
        Assert.Equal("Physician", Trading.Open(s, 9, null, 0).Name);           // neighbouring entries of the name table
        Assert.Equal("Ull", Trading.Open(s, Trading.Pawnshop, null, 0).Name);
        Assert.Equal("", Trading.Open(s, 40, null, 0).Name);
        Assert.Equal(10, Trading.Open(s, Trading.Pawnshop, "Pawn", 0).Quality);
    }

    // ---- offered quality ------------------------------------------------------------

    [Theory]
    [InlineData(0x17)]
    [InlineData(0x18)]
    [InlineData(0x19)]
    [InlineData(0x40)]
    [InlineData(0x41)]
    [InlineData(0x42)]
    public void OfferedQualityIs25ForTheseTypes(int type)
    {
        var s = Make();
        SetItem(s, 20, d => d with { Type = type, Quality = 90, Flags = 2 });
        Assert.Equal(25, Trading.OfferedQuality(s, new Shop(1, "x", 2, 40), 20));
    }

    [Fact]
    public void OfferedQualityIs25ForCategory0x400Alone()
    {
        var s = Make();
        SetItem(s, 20, d => d with { Type = 5, Quality = 90, Flags = 0x400 });
        SetItem(s, 21, d => d with { Type = 5, Quality = 90, Flags = 0x401 });
        var shop = new Shop(1, "x", 0x401, 40);
        Assert.Equal(25, Trading.OfferedQuality(s, shop, 20));
        Assert.Equal((90 + 40) / 2, Trading.OfferedQuality(s, shop, 21));   // 65
    }

    [Fact]
    public void OfferedQualityIsTheTruncatedMeanOtherwise()
    {
        var s = Make();
        var shop = new Shop(1, "x", 2, 40);
        SetItem(s, 20, d => d with { Type = 5, Quality = 30 });
        Assert.Equal(35, Trading.OfferedQuality(s, shop, 20));               // (30 + 40) / 2
        SetItem(s, 20, d => d with { Quality = 31 });
        Assert.Equal(35, Trading.OfferedQuality(s, shop, 20));               // 71 / 2 = 35
        // item quality 0xF7 is the signed byte -9; shop quality 4: (-9 + 4) / 2 = -5 / 2 = -2 (toward zero, not -3)
        SetItem(s, 20, d => d with { Quality = 0xF7 });
        Assert.Equal(-2, Trading.OfferedQuality(s, new Shop(1, "x", 2, 4), 20));
        // (-9 + -20) / 2 = -29 / 2 = -14
        Assert.Equal(-14, Trading.OfferedQuality(s, new Shop(1, "x", 2, -20), 20));
    }

    // ---- buy price ------------------------------------------------------------------

    [Fact]
    public void BuyPriceInACityWithCommonGoods()
    {
        var s = Make();
        // quality (25 + 25) / 2 = 25; price = 100 * 25 * 2 / 25 = 200
        // rarity 3 <= 5: 200 + 4 * 200 * 2 / -100 = 200 - 16 = 184; Charisma 25: 0; no standing, no fame
        Assert.Equal(184, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Fact]
    public void BuyPriceInACityWithRareGoods()
    {
        var s = Make();
        SetItem(s, 10, d => d with { Rarity = 8 });
        // rarity 8 > 5: 200 + 4 * 200 * 2 / 100 = 216
        Assert.Equal(216, Trading.BuyPrice(s, Goods(s), 10));
        SetItem(s, 10, d => d with { Rarity = 5 });   // the boundary still counts as common
        Assert.Equal(184, Trading.BuyPrice(s, Goods(s), 10));
        SetItem(s, 10, d => d with { Rarity = 6 });
        Assert.Equal(216, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Fact]
    public void BuyPriceDependsOnTheCitySize()
    {
        var s = Make(citySize: 8);
        // 200 + 8 * 200 * 2 / -100 = 200 - 32 = 168
        Assert.Equal(168, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Fact]
    public void BuyPriceInTheWildernessHasNoCityNorStandingTerm()
    {
        var s = Make(location: -1);
        Assert.Equal(200, Trading.BuyPrice(s, Goods(s), 10));    // just 100 * 25 * 2 / 25
        s.Party.Members[0][Attribute.Charisma] = 15;
        // + 200 * (25 - 15) / 100 = + 20
        Assert.Equal(220, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Theory]
    [InlineData(15, 202)]   // 184 + 184 * 10 / 100 = 184 + 18
    [InlineData(25, 184)]
    [InlineData(35, 166)]   // 184 + 184 * -10 / 100 = 184 - 18
    public void BuyPriceDependsOnTheLeadersCharisma(int charisma, int expected)
    {
        var s = Make();
        s.Party.Members[0][Attribute.Charisma] = charisma;
        Assert.Equal(expected, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Fact]
    public void BuyPriceUsesTheLeadersCharismaNotTheFirstMembers()
    {
        var s = Make();
        s.Party.LeaderIndex = 1;
        s.Party.Members[1][Attribute.Charisma] = 15;
        Assert.Equal(202, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Theory]
    [InlineData(-20, 200)]   // -1000 clamps to -999; -999 / 100 = -9; 184 * -9 / -100 = 16
    [InlineData(-2, 185)]    // -100 / 100 = -1; 184 * -1 / -100 = 1
    [InlineData(-1, 184)]    // -50 / 100 = 0
    [InlineData(0, 184)]
    [InlineData(1, 184)]     // 50 / 100 = 0
    [InlineData(50, 184)]    // 2500 clamps to 99; 99 / 100 = 0: a good name never gives a discount
    [InlineData(99, 184)]
    public void BuyPriceStandingOnlySurcharges(int reputation, int expected)
    {
        var s = Make();
        s.Locations[5].Reputation = reputation;
        Assert.Equal(expected, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(40)]
    [InlineData(300)]
    public void BuyPriceIgnoresFame(int fame)
    {
        var s = Make();
        s.Party.Fame = fame;   // fame * 100 is clamped to 500 and / 1000 = 0
        Assert.Equal(184, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Fact]
    public void BuyPriceIsAtLeastOne()
    {
        var s = Make();
        SetItem(s, 10, d => d with { Value = 0 });
        Assert.Equal(1, Trading.BuyPrice(s, Goods(s), 10));
    }

    [Fact]
    public void LocationFlag2MakesGoodsThreeRarer()
    {
        var s = Make();
        s.Locations[5].Flags = 2;
        var before = State(s);
        // rarity 3 + 3 = 6 > 5: 200 + 4 * 200 * 2 / 100 = 216
        Assert.Equal(216, Trading.BuyPrice(s, Goods(s), 10));
        Assert.Equal(before, State(s));
        // flag 8 beside flag 2 draws nothing
        s.Locations[5].Flags = 2 | 8;
        Assert.Equal(216, Trading.BuyPrice(s, Goods(s), 10));
        Assert.Equal(before, State(s));
    }

    [Fact]
    public void LocationFlag8AddsOneDrawToTheQuality()
    {
        var seed = FindSeed(r => r.Below(10) >= 3);
        var s = Make(seed: seed);
        s.Locations[5].Flags = 8;
        var twin = TestGame.Twin(s);
        var draw = twin.Below(10);

        var price = Trading.BuyPrice(s, Goods(s), 10);

        // quality 25 + draw; price = 100 * (25 + draw) * 2 / 25; rarity 3: price + 4 * price * 2 / -100
        long raw = 100L * (25 + draw) * 2 / 25;
        raw += 4 * raw * 2 / -100;
        Assert.Equal((int)raw, price);
        Assert.NotEqual(184, price);
        Assert.Equal(twin.State, State(s));   // exactly one Below(10)
    }

    // ---- sell price -----------------------------------------------------------------

    [Fact]
    public void SellPriceWorkedExample()
    {
        var s = Make();
        // value 100 / 4 = 25; price = 25 * 25 * 2 / 25 = 50
        // rarity 3: 50 + 50 * 4 * 2 / -100 = 50 - 4 = 46
        Assert.Equal(46, Trading.SellPrice(s, 10, 25));
        SetItem(s, 10, d => d with { Rarity = 8 });
        Assert.Equal(54, Trading.SellPrice(s, 10, 25));    // 50 + 4
    }

    [Fact]
    public void SellPriceCharismaAndStandingAndWilderness()
    {
        var s = Make();
        s.Party.Members[0][Attribute.Charisma] = 15;
        Assert.Equal(42, Trading.SellPrice(s, 10, 25));    // 46 + 46 * 10 / -100 = 46 - 4
        s.Party.Members[0][Attribute.Charisma] = 25;
        s.Locations[5].Reputation = 50;                     // clamped to 75 / 100 = 0
        Assert.Equal(46, Trading.SellPrice(s, 10, 25));
        s.Locations[5].Reputation = -20;                    // -999 / 100 = -9; 46 * -9 / 100 = -4
        Assert.Equal(42, Trading.SellPrice(s, 10, 25));

        var w = Make(location: -1);
        Assert.Equal(50, Trading.SellPrice(w, 10, 25));
    }

    [Fact]
    public void SellPriceIsZeroForUntradableOrWorthless()
    {
        var s = Make();
        Assert.Equal(0, Trading.SellPrice(s, 11, 25));
        SetItem(s, 10, d => d with { Value = 3 });          // 3 / 4 = 0
        Assert.Equal(0, Trading.SellPrice(s, 10, 25));
    }

    // ---- shop stock -----------------------------------------------------------------

    [Fact]
    public void ShopHasItemRejectsOtherCategoriesAndUntradableWithoutADraw()
    {
        var s = Make();
        var before = State(s);
        var stale = Trading.StaleCodeSegment;
        Assert.False(Trading.ShopHasItem(s, new Shop(1, "x", 4, 25), 10, ref stale));   // kind bit 2 not in 4
        Assert.False(Trading.ShopHasItem(s, new Shop(1, "x", 2, 25), 11, ref stale));   // cannot be traded
        Assert.Equal(before, State(s));
        Assert.Equal(Trading.StaleCodeSegment, stale);
    }

    [Theory]
    [InlineData(0x40)]
    [InlineData(0x41)]
    [InlineData(0x42)]
    public void ShopHasItemAlwaysStocksTheseTypesWithoutADraw(int type)
    {
        var s = Make();
        SetItem(s, 20, d => d with { Type = type, Flags = 2 });
        var before = State(s);
        var stale = 77;
        Assert.True(Trading.ShopHasItem(s, new Shop(1, "x", 2, 25), 20, ref stale));
        Assert.Equal(before, State(s));
        Assert.Equal(77, stale);
    }

    [Fact]
    public void ShopHasItemTestsTheTableOnceWithOneDraw()
    {
        var s = Make(citySize: 8);   // row 7 (rarity 0) is 100 %
        SetItem(s, 20, d => d with { Type = 5, Flags = 2, Rarity = 0, Quality = 25 });
        var twin = TestGame.Twin(s);
        var stale = 5;
        Assert.True(Trading.ShopHasItem(s, new Shop(1, "x", 2, 25), 20, ref stale));
        twin.Scaled(100);
        Assert.Equal(twin.State, State(s));
        Assert.Equal(7, stale);      // the row used (city size - 1) is left in the slot
    }

    [Fact]
    public void StaleSizeSelectsTheTableRow()
    {
        // item rarity 11, quality 25: row 1 holds 0 %, row 7 holds 20 %, row 9 holds 109 %
        var seed = FindSeed(r => r.Scaled(100) is >= 5 and <= 15);
        bool Has(GameState s, ref int stale) => Trading.ShopHasItem(s, new Shop(1, "x", 2, 25), 20, ref stale);
        GameState New(int citySize)
        {
            var s = Make(citySize: citySize, seed: seed);
            SetItem(s, 20, d => d with { Type = 5, Flags = 2, Rarity = 11, Quality = 25 });
            return s;
        }

        // the stale code segment (>= 0x5C): row 1, so the draw (5..15) is above 0 %
        var stale = Trading.StaleCodeSegment;
        Assert.False(Has(New(8), ref stale));
        Assert.Equal(1, stale);

        // -1 and 0x5C also give row 1
        stale = -1;
        Assert.False(Has(New(8), ref stale));
        Assert.Equal(1, stale);
        stale = 0x5C;
        Assert.False(Has(New(8), ref stale));

        // 1 (< 0x5C): row city size - 1 = 7, 20 % >= the draw
        stale = 1;
        Assert.True(Has(New(8), ref stale));
        Assert.Equal(7, stale);
        stale = 0x5B;
        Assert.True(Has(New(8), ref stale));

        // clamped to row 9
        stale = 1;
        Assert.True(Has(New(12), ref stale));
        Assert.Equal(9, stale);
    }

    [Fact]
    public void FixShopStockSizeAlwaysUsesTheCitySizeRow()
    {
        var seed = FindSeed(r => r.Scaled(100) is >= 5 and <= 15);
        var s = Make(citySize: 8, seed: seed);
        SetItem(s, 20, d => d with { Type = 5, Flags = 2, Rarity = 11, Quality = 25 });
        s.Behaviour.FixShopStockSize = true;
        var stale = Trading.StaleCodeSegment;
        Assert.True(Trading.ShopHasItem(s, new Shop(1, "x", 2, 25), 20, ref stale));   // row 7: 20 %
        Assert.Equal(7, stale);
    }

    [Fact]
    public void ShopQualityAdjustsTheRarityColumn()
    {
        // city size 9 -> row 8: rarity 11 = 30 %, rarity 9 = 99 %.
        // Offered quality (31 + 31) / 2 = 31 > 0x1D lowers the rarity by 2: 11 -> 9
        var seed = FindSeed(r => r.Scaled(100) is >= 40 and <= 90);
        var s = Make(citySize: 9, seed: seed);
        SetItem(s, 20, d => d with { Type = 5, Flags = 2, Rarity = 11, Quality = 31 });
        var stale = 1;
        Assert.True(Trading.ShopHasItem(s, new Shop(1, "x", 2, 31), 20, ref stale));
        // quality (25 + 25) / 2 = 25 changes nothing: 30 % < the draw
        var t = Make(citySize: 9, seed: seed);
        SetItem(t, 20, d => d with { Type = 5, Flags = 2, Rarity = 11, Quality = 25 });
        stale = 1;
        Assert.False(Trading.ShopHasItem(t, new Shop(1, "x", 2, 25), 20, ref stale));
    }

    private static GameState StockedGame(uint seed)
    {
        var s = Make(seed: seed);
        for (var code = 0; code < 200; code++)
        {
            SetItem(s, code, d => d with { Flags = 2 });
        }
        return s;
    }

    [Fact]
    public void StockIsDeterministicAndIndependentOfTheGeneratorState()
    {
        var a = StockedGame(1);
        var b = StockedGame(999);
        var stockA = Trading.Stock(a, Goods(a));
        var stockB = Trading.Stock(b, Goods(b));
        Assert.Equal(stockA.Codes, stockB.Codes);
        Assert.Equal(stockA.ListedPrices, stockB.ListedPrices);
        Assert.Equal(State(a), State(b));       // both end in the scan's state
        Assert.True(stockA.Codes.Count > 4);

        // the party seed is part of the generator seed (ordinal 17 + party seed)
        var c = StockedGame(1);
        c.Party.Seed = 9;
        Assert.NotEqual(stockA.Codes, Trading.Stock(c, Goods(c)).Codes);
    }

    [Fact]
    public void StockReseedsFromTheClockAfterwards()
    {
        var s = Make();
        s.ClockSeed = () => 4242u;
        Trading.Stock(s, Goods(s));
        Assert.Equal(4242u, State(s));
    }

    [Fact]
    public void ListedPricesCoverTheFirstFourGoods()
    {
        var s = StockedGame(1);
        var shop = Goods(s);
        var stock = Trading.Stock(s, shop);
        Assert.Equal(4, stock.ListedPrices.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(Trading.BuyPrice(s, shop, stock.Codes[i]), stock.ListedPrices[i]);
        }
        Assert.Equal(stock.Codes.OrderBy(c => c), stock.Codes);   // list order = code order
    }

    [Fact]
    public void StockWithFewGoodsListsThemAll()
    {
        var s = Make();
        SetItem(s, 10, d => d with { Flags = 0 });
        foreach (var code in new[] { 64, 65, 66 })     // types 0x40..0x42 are always stocked
        {
            SetItem(s, code, d => d with { Flags = 2 });
        }
        var shop = Goods(s);
        var stock = Trading.Stock(s, shop);
        Assert.Equal([64, 65, 66], stock.Codes);
        Assert.Equal(3, stock.ListedPrices.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(Trading.BuyPrice(s, shop, stock.Codes[i]), stock.ListedPrices[i]);
        }
    }

    // ---- buy and sell ---------------------------------------------------------------

    [Fact]
    public void BuyWithTooLittleMoneyChangesNothing()
    {
        var s = Make();
        s.Party.Purse = Money.FromPfennigs(183);   // price is 184
        var packBefore = s.Party.Members[0].Pack.ToList();
        Assert.Equal(-1, Trading.Buy(s, Goods(s), 0, 10));
        Assert.Equal(Money.FromPfennigs(183), s.Party.Purse);
        Assert.Equal(packBefore, s.Party.Members[0].Pack);
    }

    [Fact]
    public void BuyPaysAndAddsAStackOfTheOfferedQuality()
    {
        var s = Make();
        s.Party.Purse = Money.FromPfennigs(1000);
        var shop = new Shop(1, "x", 2, 40);
        // quality (25 + 40) / 2 = 32; price = 100 * 32 * 2 / 25 = 256; 256 + 4 * 256 * 2 / -100 = 256 - 20 = 236
        Assert.Equal(236, Trading.BuyPrice(s, shop, 10));

        var count = Trading.Buy(s, shop, 0, 10);

        Assert.Equal(4, count);   // Gretchen carried 3 stacks
        Assert.Equal(1000 - 236, s.Party.Purse.TotalPfennigs);
        Assert.Equal(new ItemStack(10, 10, 32, 1, 1), s.Party.Members[0].Pack[3]);

        Assert.Equal(4, Trading.Buy(s, shop, 0, 10));   // joins the stack
        Assert.Equal(2, s.Party.Members[0].Pack[3].Count);
        Assert.Equal(1000 - 2 * 236, s.Party.Purse.TotalPfennigs);
    }

    [Fact]
    public void BuyWithExactlyThePriceEmptiesThePurse()
    {
        var s = Make();
        s.Party.Purse = Money.FromPfennigs(184);
        Assert.Equal(4, Trading.Buy(s, Goods(s), 0, 10));
        Assert.Equal(0, s.Party.Purse.TotalPfennigs);
    }

    [Fact]
    public void SellRemovesOneItemAndPays()
    {
        var s = Make();
        var pack = s.Party.Members[0].Pack;
        pack.Add(new ItemStack(10, 10, 25, 2, 1));

        Assert.Equal(4, Trading.Sell(s, 0, 3));
        Assert.Equal(46, s.Party.Purse.TotalPfennigs);
        Assert.Equal(1, pack[3].Count);

        Assert.Equal(3, Trading.Sell(s, 0, 3));          // last one: the stack goes
        Assert.Equal(92, s.Party.Purse.TotalPfennigs);
        Assert.Equal(3, pack.Count);
    }

    [Fact]
    public void SellRefusesUntradableItemsAndSellsAStaleEntryPastTheEnd()
    {
        var s = Make();
        var pack = s.Party.Members[0].Pack;
        pack.Add(new ItemStack(11, 11, 25, 1, 1));
        Assert.Equal(-1, Trading.Sell(s, 0, 3));
        Assert.Equal(4, pack.Count);
        Assert.Equal(0, s.Party.Purse.TotalPfennigs);
        // a lit row past the end: entry 4 was never written (item 0, quality 0, count 0)
        var price = Trading.SellPrice(s, 0, 0);
        Assert.Equal(4, Trading.Sell(s, 0, 4));
        Assert.Equal(price, s.Party.Purse.TotalPfennigs);
        Assert.Equal(255, pack[4].Count);                 // count 0 wrapped: nothing removed
        Assert.Equal(4, pack.Count);
    }

    // ---- storing --------------------------------------------------------------------

    [Fact]
    public void AddOneStacksEqualCodeAndQuality()
    {
        var store = new StoreTable();
        Storing.AddOne(store, 5, 25);
        Storing.AddOne(store, 5, 25);
        Storing.AddOne(store, 5, 30);
        Storing.AddOne(store, 6, 25);
        Assert.Equal([new StoredItem(5, 25, 2), new StoredItem(5, 30, 1), new StoredItem(6, 25, 1)], store);
    }

    [Fact]
    public void AddOneWrapsTheByteCount()
    {
        var store = StoreTable.Of([new(5, 25, 255)]);
        Storing.AddOne(store, 5, 25);
        Assert.Equal([new StoredItem(5, 25, 0)], store);
    }

    [Fact]
    public void TakeOneRemovesAtOneAndWrapsZero()
    {
        var store = StoreTable.Of([new(5, 25, 1), new(6, 25, 2), new(7, 25, 0)]);
        Storing.TakeOne(store, 1);
        Assert.Equal(new StoredItem(6, 25, 1), store[1]);
        Storing.TakeOne(store, 2);                       // 0 - 1 wraps to 255
        Assert.Equal(new StoredItem(7, 25, 255), store[2]);
        Storing.TakeOne(store, 0);
        Assert.Equal([new StoredItem(6, 25, 1), new StoredItem(7, 25, 255)], store);
        Assert.Equal(StoreTable.Zero, store[2]);          // the gap closed by copying entry 3 (never written) down
    }

    [Fact]
    public void PutMovesOneItemToTheStore()
    {
        var s = Make();
        var store = new StoreTable();
        Assert.Equal(2, Storing.Put(s, store, 0, 0));      // Gretchen's item 5, quality 25, was a stack of one
        Assert.Equal([new StoredItem(5, 25, 1)], store);
        Assert.Equal(2, s.Party.Members[0].Pack.Count);
    }

    [Fact]
    public void PutPastThePacksEndStoresTheStaleEntry()
    {
        var s = Make();
        var store = new StoreTable();
        var pack = s.Party.Members[0].Pack;
        var stale = pack[3];                               // past Gretchen's three stacks: never written, the zero entry
        Assert.Equal(3, Storing.Put(s, store, 0, 3));      // item 0, quality 0 goes into the store
        Assert.Equal([new StoredItem(stale.Code & 0xFFF, stale.Quality, 1)], store);
        Assert.Equal(255, pack[3].Count);                  // its count 0 wrapped: nothing removed
        Assert.Equal(3, pack.Stacks);
    }

    [Fact]
    public void GetMovesOneItemToThePack()
    {
        var s = Make();
        var store = StoreTable.Of([new(7, 30, 2)]);
        Assert.Equal(4, Storing.Get(s, store, 0, 0));
        Assert.Equal(new ItemStack(7, 7, 30, 1, 1), s.Party.Members[0].Pack[3]);
        Assert.Equal([new StoredItem(7, 30, 1)], store);

        Assert.Equal(4, Storing.Get(s, store, 0, 0));      // joins the stack
        Assert.Equal(2, s.Party.Members[0].Pack[3].Count);
        Assert.Empty(store);
    }

    [Fact]
    public void GetPastTheStoresEndHandsOutWhatAnEarlierStoreLeft()
    {
        var s = Make();
        var stores = new ItemStores();
        var a = new Location();
        var b = new Location();
        stores.Close(a, [new StoredItem(7, 30, 2), new StoredItem(8, 20, 1)]);
        stores.Close(b, [new StoredItem(9, 25, 1)]);
        stores.Open(a.CacheHandle, s.StoreTable);
        var table = stores.Open(b.CacheHandle, s.StoreTable);
        Assert.Equal([new StoredItem(9, 25, 1)], table);
        Assert.Equal(new StoredItem(8, 20, 1), table[1]);  // left by the first store
        Assert.Equal(4, Storing.Get(s, table, 0, 1));      // row 1 past the end: item 8 for Gretchen
        Assert.Equal(8, s.Party.Members[0].Pack[3].Code);
        Assert.Empty(table);                               // its count reached 0: cache_take shifts nothing and drops the count to 0
        Assert.Equal(new StoredItem(9, 25, 1), table[0]);  // item 9 is still in its entry, but the store now looks empty
    }

    // ---- item stores ----------------------------------------------------------------

    [Fact]
    public void OpenOfNothingIsEmpty()
    {
        var stores = new ItemStores();
        Assert.Empty(stores.Open(-1, new StoreTable()));
        Assert.Empty(stores.Open(0, new StoreTable()));
        Assert.Empty(stores.Open(3, new StoreTable()));
    }

    [Fact]
    public void ClosingAPlaceWithoutAStoreAppendsOne()
    {
        var stores = new ItemStores();
        var a = new Location();
        var b = new Location();
        var items = new List<StoredItem> { new(5, 25, 3) };
        stores.Close(a, items);
        Assert.Equal(1, a.CacheHandle);
        Assert.Single(stores.All);
        stores.Close(b, [new StoredItem(6, 20, 1)]);
        Assert.Equal(2, b.CacheHandle);

        items.Add(new StoredItem(9, 9, 9));                // the table holds a copy
        Assert.Equal([new StoredItem(5, 25, 3)], stores.Open(1, new StoreTable()));
        var copy = stores.Open(1, new StoreTable());
        copy.Empty();
        Assert.Single(stores.Open(1, new StoreTable()));   // and Open copies into the working table
    }

    [Fact]
    public void ClosingEmptyForgetsTheHandleButKeepsTheTableEntry()
    {
        var stores = new ItemStores();
        var a = new Location();
        stores.Close(a, [new StoredItem(5, 25, 3)]);
        stores.Close(a, []);
        Assert.Equal(-1, a.CacheHandle);
        Assert.Single(stores.All);
        Assert.Equal([new StoredItem(5, 25, 3)], stores.Open(1, new StoreTable()));

        var fresh = new Location();
        stores.Close(fresh, []);                           // nothing to write for a place without a store
        Assert.Equal(-1, fresh.CacheHandle);
        Assert.Single(stores.All);
    }

    [Fact]
    public void ClosingWithAnExistingHandleReplacesTheStore()
    {
        var stores = new ItemStores();
        var a = new Location();
        var b = new Location();
        stores.Close(a, [new StoredItem(5, 25, 3)]);
        stores.Close(b, [new StoredItem(6, 25, 1)]);
        stores.Close(a, [new StoredItem(8, 10, 2), new StoredItem(9, 10, 1)]);
        Assert.Equal(1, a.CacheHandle);
        Assert.Equal(2, stores.All.Count);
        Assert.Equal([new StoredItem(8, 10, 2), new StoredItem(9, 10, 1)], stores.Open(1, new StoreTable()));
        Assert.Equal([new StoredItem(6, 25, 1)], stores.Open(2, new StoreTable()));
    }

    // ---- loot -----------------------------------------------------------------------

    [Fact]
    public void FoundItemsGetCountsAndQualities()
    {
        var found = new List<ItemStack>
        {
            new(1, 0x70, 50, 0, 1),     // count 0 -> 1; type above 0x61: untouched
            new(2, 0x62, 200, 3, 1),    // 0x62 is above 0x61: untouched
            new(3, 0x40, 77, 1, 1),     // -> 25
            new(4, 0x41, 77, 1, 1),
            new(5, 0x42, 77, 1, 1),
            new(6, 0x17, 3, 1, 1),
            new(7, 0x18, 99, 1, 1),
            new(8, 0x19, 0, 1, 1),      // kept, clamped up to 1
            new(9, 0x19, 150, 1, 1),    // clamped down to 99
            new(10, 0x1A, 60, 1, 1),    // kept
            new(11, 0x30, 50, 1, 1),    // 50 - 10 = 40
            new(12, 0x30, 5, 1, 1),     // -5 clamped to 1
            new(13, 0x30, 200, 1, 1),   // 190 clamped to 99
            new(14, 0x61, 30, 1, 1),    // 20
        };

        LootSharing.PrepareFoundItems(found);

        Assert.Equal([1, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1], found.Select(i => i.Count));
        Assert.Equal([50, 200, 25, 25, 25, 25, 25, 1, 99, 60, 40, 1, 99, 20], found.Select(i => i.Quality));
    }

    [Fact]
    public void TakeCashDropsFlorinsAndCapsTheRest()
    {
        var s = Make();
        s.Party.Purse = new Money(1, 19, 11);
        var found = new Money(5, 15, 25);
        var added = LootSharing.TakeCash(s, ref found);
        Assert.Equal(new Money(0, 10, 10), added);
        Assert.Equal(new Money(0, 10, 10), found);      // written back to the caller's cash
        // 1 fl 19 gr 11 pf + 10 gr 10 pf: pf 21 -> 9 carry 1 -> gr 20 + 10 = 30 -> 10 carry 1 -> 2 fl
        Assert.Equal(new Money(2, 10, 9), s.Party.Purse);

        var t = Make();
        var small = new Money(0, 3, 4);
        Assert.Equal(new Money(0, 3, 4), LootSharing.TakeCash(t, ref small));
        Assert.Equal(new Money(0, 3, 4), t.Party.Purse);
        var negative = new Money(0, -5, -1);
        Assert.Equal(Money.Zero, LootSharing.TakeCash(t, ref negative));   // negatives count as nothing
    }

    [Fact]
    public void LootPutStacksOnEqualEntries()
    {
        var s = Make();
        var pile = new List<ItemStack>();
        Assert.Equal(2, LootSharing.Put(s, pile, 0, 0));       // item 5 (stack of one)
        Assert.Equal([new ItemStack(5, 5, 25, 1, 1)], pile);   // type and weight come from the item list

        Inventory.AddWithQuality(s, 0, 5, 25);
        Inventory.AddWithQuality(s, 0, 5, 30);
        var pack = s.Party.Members[0].Pack;
        LootSharing.Put(s, pile, 0, pack.FindIndex(i => i.Code == 5 && i.Quality == 25));
        Assert.Equal(2, pile[0].Count);
        LootSharing.Put(s, pile, 0, pack.FindIndex(i => i.Code == 5 && i.Quality == 30));
        Assert.Equal(2, pile.Count);
        // a lit row past the end puts the stale entry there: item 0 at quality 0, count 0 -> 255 (nothing leaves the pack)
        var stacks = pack.Stacks;
        Assert.Equal(stacks, LootSharing.Put(s, pile, 0, 50));
        Assert.Equal(new ItemStack(0, 0, 0, 1, 1), pile[2]);
        Assert.Equal(255, pack[50].Count);
    }

    [Fact]
    public void LootPileHoldsAtMost45EntriesButTheItemStillLeavesThePack()
    {
        var s = Make();
        var pile = Enumerable.Range(100, 45).Select(c => new ItemStack(c, c, 25, 1, 1)).ToList();
        var before = s.Party.Members[0].Pack.Count;
        Assert.Equal(before - 1, LootSharing.Put(s, pile, 0, 0));
        Assert.Equal(45, pile.Count);
        Assert.DoesNotContain(pile, i => i.Code == 5);
        Assert.Equal(before - 1, s.Party.Members[0].Pack.Count);
    }

    [Fact]
    public void LootPutStartsANewEntryWhenTheEqualOneIsFull()
    {
        var s = Make();
        var pile = new List<ItemStack> { new(5, 5, 25, 255, 1) };
        LootSharing.Put(s, pile, 0, 0);
        Assert.Equal([new ItemStack(5, 5, 25, 255, 1), new ItemStack(5, 5, 25, 1, 1)], pile);
    }

    [Fact]
    public void LootGetMovesOneItemAndRemovesEmptyEntries()
    {
        var s = Make();
        var pile = new List<ItemStack> { new(7, 7, 30, 2, 1) };
        Assert.Equal(4, LootSharing.Get(s, pile, 0, 0));
        Assert.Equal(new ItemStack(7, 7, 30, 1, 1), s.Party.Members[0].Pack[3]);
        Assert.Equal(1, pile[0].Count);

        Assert.Equal(4, LootSharing.Get(s, pile, 0, 0));
        Assert.Empty(pile);
        Assert.Equal(2, s.Party.Members[0].Pack[3].Count);

        Assert.Equal(-1, LootSharing.Get(s, pile, 0, 0));
        Assert.Equal(-1, LootSharing.Get(s, [new ItemStack(7, 7, 30, 1, 1)], 0, -1));
    }

    [Fact]
    public void DeadLeaderIsReplacedByTheFirstMemberNotDead()
    {
        var s = Make();
        s.Party.Members[0].Status = CharacterStatus.Dead;
        LootSharing.ReplaceFallenLeader(s);
        Assert.Equal(1, s.Party.LeaderIndex);

        var t = Make();
        t.Party.Members[0].Status = CharacterStatus.Dead;
        t.Party.Members[1].Status = CharacterStatus.Dead;
        LootSharing.ReplaceFallenLeader(t);
        Assert.Equal(2, t.Party.LeaderIndex);
    }

    [Fact]
    public void AbleOrUnconsciousLeaderStays()
    {
        var s = Make();
        s.Party.LeaderIndex = 2;
        LootSharing.ReplaceFallenLeader(s);
        Assert.Equal(2, s.Party.LeaderIndex);
        s.Party.Members[2].Status = CharacterStatus.Unconscious;
        LootSharing.ReplaceFallenLeader(s);
        Assert.Equal(2, s.Party.LeaderIndex);
    }

    [Fact]
    public void LeaderWithAnEmptySlotIsNeverReplaced()
    {
        // the original tests the leader's slot instead of the candidate's
        var s = Make();
        s.Party.Members[0].Status = CharacterStatus.Empty;
        LootSharing.ReplaceFallenLeader(s);
        Assert.Equal(0, s.Party.LeaderIndex);
    }
}
