using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Items;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>
/// A shop the party barters at: its number (0..8 a city shop, −2 the pawnshop, other numbers
/// a trader without a shop), its name, the item categories it deals in and its quality.
/// </summary>
public sealed record Shop(int Number, string Name, long Categories, int Quality);

/// <summary>What a shop offers today: the item codes in list order, and the prices the listing showed for the first four.</summary>
public sealed record ShopStock(IReadOnlyList<int> Codes, IReadOnlyList<int> ListedPrices);

/// <summary>
/// Buying and selling (the barter screen's rules): shop quality, the quality of the goods,
/// buy and sell prices in pfennigs, and the day's stock. Prices are computed in 32-bit
/// integers with truncating division and returned as 16-bit words, as in the original.
/// </summary>
/// <remarks>
/// Faithful details: buying costs twice the list value at quality 25 and selling pays a
/// quarter of that rate (value ÷ 4, the same ×2/25); the leader's Charisma raises the buy
/// price by (25 − Cha) % and lowers the sell price by as much; standing only ever surcharges
/// (rep × 50 ÷ 100 truncates to 0 for any standing above −2) and fame never counts
/// (it is clamped to 500 and divided by 1000). In a place with location flag 8 every price
/// adds rnd(10) to the quality, so the price shown and the price paid differ.
/// </remarks>
// orig: barter overlay R032 = R032 (linked paragraph 1462, payload = offset) draft (the byte match 8/11: 192e, 0efa, 0f60, 078e, 076c, 0b30, 1770, 1870 byte-match; 0000, 1b26, 1d18 differ in registers/slots only); tests: TradingAndLootTests, Oracle:trading.json
public static class Trading
{
    /// <summary>The pawnshop's shop number.</summary>
    public const int Pawnshop = -2;

    /// <summary>The listing scans item codes 0..199.</summary>
    public const int CodesScanned = 200;

    /// <summary>The listing prices (and the stack-slot reset, see <see cref="Stock"/>) apply to the first four goods.</summary>
    public const int ListedRows = 4;

    /// <summary>The value a fresh stock scan finds in the stale stack slot: a code segment, far above every city.</summary>
    // UNCERTAIN: the real value is the R032 overlay's run-time CS, assumed in 0x005C..0x7FFF (any DOS load address the game fits at); the oracle runs at the link-time CS 1462.
    public const int StaleCodeSegment = 0x2000;

    /// <summary>Default names of the nine city shops (when a card gives none).</summary>
    public static readonly IReadOnlyList<string> ShopNames =
    [
        "Blacksmith", "Goods Merchant", "Swordsmith", "Armorer", "Gunsmith", "Bowyer", "Artificier", "Jeweler", "Clothmaker",
    ];

    /// <summary>
    /// The chance in % (row: city size − 1, column: rarity) that a shop stocks an item. Row 9
    /// lies past the end of the game's table and holds the bytes that follow it.
    /// </summary>
    // orig: R136+0xC02B DS-resident table 290E:399B [10][12] bytes (data); read by shop_has_item
    private static readonly byte[,] StockChances =
    {
        { 50, 20, 15, 10, 5, 1, 1, 0, 0, 0, 0, 0 },
        { 75, 50, 35, 25, 15, 10, 5, 1, 1, 0, 0, 0 },
        { 99, 75, 65, 60, 45, 30, 20, 10, 7, 4, 2, 1 },
        { 99, 99, 90, 80, 65, 50, 35, 20, 15, 7, 4, 2 },
        { 100, 99, 99, 99, 85, 70, 50, 35, 25, 15, 7, 3 },
        { 100, 100, 99, 99, 90, 80, 65, 55, 40, 30, 15, 6 },
        { 100, 100, 100, 100, 99, 99, 85, 75, 60, 50, 20, 12 },
        { 100, 100, 100, 100, 100, 100, 100, 100, 85, 75, 30, 20 },
        { 100, 100, 100, 100, 100, 100, 100, 100, 99, 99, 50, 30 },
        { 0, 0, 0, 0, 0, 0, 0, 0, 0, 99, 111, 109 },
    };

    /// <summary>
    /// The twelve bytes in front of the table, which row −1 (city size 0, as an empty city
    /// record has) reads.
    /// </summary>
    private static readonly byte[] BytesBeforeStockChances = [255, 254, 254, 255, 255, 254, 254, 255, 255, 255, 0, 0];

    private static int StockChance(int row, int rarity) => row switch
    {
        -1 => BytesBeforeStockChances[rarity],
        // DIVERGENCE: rows below −1 (a negative city size word, never in DARKLAND.CTY) read further before the table in the original; taken as row 0 here.
        < -1 => StockChances[0, rarity],
        _ => StockChances[row, rarity],
    };

    // ---- the shop -------------------------------------------------------------------

    /// <summary>
    /// The shop's quality: the city record's quality byte for shops 0..8 (blacksmith, goods
    /// merchant, swordsmith, armorer, gunsmith, bowyer, artificer, jeweler, clothmaker), 10 for
    /// the pawnshop, 25 for any other trader.
    /// </summary>
    // orig: barter_screen R032+0x0000 (1462:0000) draft (unmatched: registers), the lines setting DS:8834; tests: TradingAndLootTests
    // C: city_rec->quality[] is a signed char; shops 0..8 read it even outside a city (the last city record).
    public static int ShopQuality(GameState s, int shop) => shop switch
    {
        Pawnshop => 10,
        >= 0 and < 9 => (sbyte)s.City.Byte(0x62 + shop),
        _ => 25,
    };

    /// <summary>Opens a shop; without a name a city shop takes its default name.</summary>
    // orig: barter_screen R032+0x0000 (1462:0000) draft: DS:869C = name ? name : R136+0xA70F[shop]
    // DORMANT: cards reach barter through R029+0x21AA, which already substitutes the table name for a null name; the fallback here only runs for direct callers.
    public static Shop Open(GameState s, int shop, string? name, long categories) =>
        new(shop, name ?? DefaultName(shop), categories, ShopQuality(s, shop));

    /// <summary>
    /// The name table entry for a shop without a name. Only 0..8 are shop names; the original
    /// indexes the far-pointer table with any shop number, and the neighbouring entries are
    /// other names (−3..−1: Tope, Ull, Volund; 9..11: Physician, Hospital, Nobility).
    /// </summary>
    // orig: R136+0xA70F DS-resident table 290E:207F (data, read by barter_screen and R029+0x21AA)
    // UNCERTAIN: entries outside −3..11 are not reproduced (empty name); no card is known to pass them.
    private static string DefaultName(int shop) => shop switch
    {
        >= 0 and < 9 => ShopNames[shop],
        -3 => "Tope",
        -2 => "Ull",
        -1 => "Volund",
        9 => "Physician",
        10 => "Hospital",
        11 => "Nobility",
        _ => "",
    };

    /// <summary>
    /// The quality of the goods a shop sells: 25 for item types 0x17..0x19 and 0x40..0x42 and
    /// for items of category 0x400 alone; otherwise the mean of the item's quality and the shop's.
    /// </summary>
    // orig: item_quality R032+0x0EFA (1462:0EFA) draft (byte-matched function); tests: TradingAndLootTests, Oracle:trading.json
    // C: the 0x400 test is a 32-bit compare of the category long; (quality + shop quality) / 2 is a signed division (cwd; sub; sar) truncating toward 0.
    public static int OfferedQuality(GameState s, Shop shop, int code)
    {
        var d = s.Item(code);
        if (d.Type is 0x40 or 0x41 or 0x42 or 0x17 or 0x18 or 0x19 || d.Flags == 0x400)
        {
            return 25;
        }
        return ((sbyte)d.Quality + shop.Quality) / 2;
    }

    // ---- prices ---------------------------------------------------------------------

    /// <summary>What the shop asks for one item of <paramref name="code"/>, in pfennigs (at least 1).</summary>
    // orig: buy_price R032+0x192E (1462:192E) draft (byte-matched function); tests: TradingAndLootTests, Oracle:trading.json
    public static int BuyPrice(GameState s, Shop shop, int code)
    {
        var d = s.Item(code);
        // C: all terms are 32-bit longs with C's truncating division; value is the signed list word (+0x2C), rarity a signed char (+0x27).
        long value = (short)d.Value;
        long quality = OfferedQuality(s, shop, code);
        var (rarity, extra) = LocalAdjustments(s, (sbyte)d.Rarity);
        quality += extra;
        var price = value * quality * 2 / 25;
        if (s.CurrentLocation != GameState.Wilderness)
        {
            price += rarity <= 5 ? CitySize(s) * price * 2 / -100 : CitySize(s) * price * 2 / 100;
        }
        price += price * (25 - LeaderCharisma(s)) / 100;
        if (s.CurrentLocation != GameState.Wilderness)
        {
            // C: clamp(−999, 99, rep × 50) / 100 truncates: 0 for rep ≥ −1, so standing only surcharges (at most +9 %: −999 / 100 = −9).
            price += price * (Math.Clamp(StandingTimes50(s), -999, 99) / 100) / -100;
        }
        // C: clamp(0, 500, fame × 100) / 1000 is always 0, so fame never changes the price (fame × 100 wraps at 16 bits first).
        price += price * (Math.Clamp(FameTimes100(s), 0, 500) / 1000) / -100;
        // C: the long is returned as a 16-bit int: "(int)price < 1" tests the low word.
        return (short)price < 1 ? 1 : (short)price;
    }

    /// <summary>What the shop pays for one item of <paramref name="code"/> and <paramref name="quality"/>, in pfennigs (0 when it will not buy it).</summary>
    // orig: sell_price R032+0x1B26 (1462:1B26) draft (unmatched: register choice only); tests: TradingAndLootTests, Oracle:trading.json
    public static int SellPrice(GameState s, int code, int quality)
    {
        var d = s.Item(code);
        if (d.CannotBeTraded)
        {
            return 0;
        }
        // C: value / 4 is a 16-bit int division (truncating) before widening.
        long value = (short)d.Value / 4;
        var (rarity, extra) = LocalAdjustments(s, (sbyte)d.Rarity);
        quality += extra;
        var price = quality * value * 2 / 25;
        if (s.CurrentLocation != GameState.Wilderness)
        {
            price += rarity <= 5 ? price * CitySize(s) * 2 / -100 : price * CitySize(s) * 2 / 100;
        }
        price += price * (25 - LeaderCharisma(s)) / -100;
        if (s.CurrentLocation != GameState.Wilderness)
        {
            price += price * (Math.Clamp(StandingTimes50(s), -999, 75) / 100) / 100;
        }
        price += price * (Math.Clamp(FameTimes100(s), 0, 500) / 1000) / 100;
        // C: as buy_price, the low word; below 1 pays nothing.
        return (short)price < 1 ? 0 : (short)price;
    }

    /// <summary>
    /// The location's effect on a price: flags 2 or 0x41 make the item 3 rarer; otherwise
    /// flag 8 adds rnd(10) to the quality (one random draw).
    /// </summary>
    // orig: the location block buy_price R032+0x192E and sell_price R032+0x1B26 each contain: flags & 2, else flags & 0x41, else flags & 8 → q += rand_mod(10)
    // C: the two first tests are separate else-if arms with the same effect (rarity += 3); kept as one condition.
    private static (long Rarity, int ExtraQuality) LocalAdjustments(GameState s, long rarity)
    {
        if (s.CurrentLocation == GameState.Wilderness)
        {
            return (rarity, 0);
        }
        var flags = s.Locations[s.CurrentLocation].Flags;
        if ((flags & 2) != 0 || (flags & 0x41) != 0)
        {
            return (rarity + 3, 0);
        }
        return (flags & 8) != 0 ? (rarity, s.Random.Below(10)) : (rarity, 0);
    }

    /// <summary>The city size in a city, 2 elsewhere.</summary>
    private static long CitySize(GameState s) => s.InCity ? s.City.CitySize : 2;

    // C: member_attr(leader, 5): the current Charisma of whoever is leader now (Ctrl-F1..F5 on the barter screen change it).
    private static int LeaderCharisma(GameState s) => s.Party.Slot(s.Party.LeaderIndex)[Attribute.Charisma];

    // C: cur_loc_query(0) * 50 in 16 bits (|rep| ≤ 99: no wrap).
    private static int StandingTimes50(GameState s) => (short)(LocationRules.Query(s, s.CurrentLocation, 0) * 50);

    private static int FameTimes100(GameState s) => (short)(PartyQueries.Get(s, 4) * 100);

    // ---- stock ----------------------------------------------------------------------

    /// <summary>
    /// Whether the shop offers <paramref name="code"/> today (one draw of <c>random_n(100)</c>
    /// for items that pass the category test). <paramref name="staleSize"/> is the original's
    /// uninitialised stack slot: the city size − 1 is used only while it holds a value below
    /// 0x5C other than −1, otherwise the stock table's row 1; the row used is left in it.
    /// </summary>
    // orig: shop_has_item R032+0x1D18 (1462:1D18) draft (unmatched: slot/register; checked against asm); tests: TradingAndLootTests, Oracle:trading.json
    // C: the category test is a 32-bit AND of the item's long (+0x20) with the shop's mask; type 0x40..0x42 (+0x1E word) is offered without a draw and without touching the slot.
    public static bool ShopHasItem(GameState s, Shop shop, int code, ref int staleSize)
    {
        var d = s.Item(code);
        if ((d.Flags & (uint)shop.Categories) == 0 || d.CannotBeTraded)
        {
            return false;
        }
        if (d.Type is 0x40 or 0x41 or 0x42)
        {
            return true;
        }
        // BUG: the original compares its uninitialised size slot where cur_loc was meant (checked against asm 1D72: mov dx,[bp-2]; cmp dx,5Ch; inc dx); OriginalBehaviour.FixShopStockSize uses the city size always.
        // C: signed 16-bit compare; city_rec is the last city loaded, even outside cities.
        staleSize = s.Behaviour.FixShopStockSize || (staleSize < 0x5C && staleSize != -1) ? (short)(s.City.CitySize - 1) : 1;
        if (staleSize > 9)
        {
            staleSize = 9;
        }
        int rarity = (sbyte)d.Rarity;
        var quality = OfferedQuality(s, shop, code);
        if (quality < 0x17)
        {
            rarity++;
        }
        else if (quality > 0x1D)
        {
            rarity -= 2;
        }
        else if (quality > 0x1A)
        {
            rarity--;
        }
        rarity = Math.Clamp(rarity, 0, 11);
        // C: random_n(100) = rand() * 100 / 32768 (scaled, not a remainder); "<=" so chance 0 still passes on a 0 draw; the table bytes are unsigned.
        return s.Random.Scaled(100) <= StockChance(staleSize, rarity);
    }

    /// <summary>
    /// The day's stock: the generator is seeded with the city's ordinal + the party seed in a
    /// city (party seed + location elsewhere), item codes 0..199 are tested in order, and the
    /// clock reseeds the generator afterwards. The first four goods are priced while listing
    /// (their draws count), and drawing their line leaves a code segment in the stale slot
    /// <see cref="ShopHasItem"/> reads, so the item after each of them is tested with row 1.
    /// </summary>
    // orig: draw_barter_screen R032+0x078E (1462:078E) draft (byte-matched function), the stock loop only (the drawing is BarterScreen.DrawScreen); tests: TradingAndLootTests, Oracle:trading.json
    public static ShopStock Stock(GameState s, Shop shop)
    {
        // C: srand(unsigned): the 16-bit sum of the city's seed word (CTY +0x56) and party_seed, or party_seed + cur_loc outside cities (−1 in the wilderness).
        var seed = s.InCity ? s.City.Ordinal + s.Party.Seed : s.Party.Seed + s.CurrentLocation;
        s.Random.Seed((ushort)seed);
        var codes = new List<int>();
        var prices = new List<int>();
        var slot = StaleCodeSegment;
        for (var code = 0; code < CodesScanned; code++)
        {
            if (!ShopHasItem(s, shop, code, ref slot))
            {
                continue;
            }
            if (codes.Count < ListedRows)
            {
                // C: the listing's sprintf arguments are evaluated right to left: weight, item_quality (no draw), name, then buy_price (a flag-8 draw), all under the fixed seed.
                prices.Add(BuyPrice(s, shop, code));
                // C: the far call to gfx_text that prints the line pushes its return CS onto the stack word shop_has_item later reads as its uninitialised [bp-2] (checked against asm 078e: 0A4F..0AC4); a code segment is far above 0x5C.
                slot = StaleCodeSegment;
            }
            codes.Add(code);
        }
        // C: srand((unsigned)time(0)) after the listing; the clock is injected (no clock: the generator keeps its state).
        if (s.ClockSeed is { } clock)
        {
            s.Random.Seed(clock());
        }
        return new ShopStock(codes, prices);
    }

    // ---- buying and selling ---------------------------------------------------------

    /// <summary>
    /// Buys one item for <paramref name="member"/>: nothing if the purse holds less than the
    /// price; otherwise the price (computed again, so a flag-8 place may charge another) is
    /// paid and an item of the shop's quality is added. Returns the member's stack count, or
    /// −1 when nothing was bought.
    /// </summary>
    // orig: barter_key R032+0x0F60 (1462:0F60) draft (byte-matched function), case P (0x1950/0x1970); tests: TradingAndLootTests
    public static int Buy(GameState s, Shop shop, int member, int code)
    {
        // C: money_set(&price, 0, 0, buy_price(..)) then money_cmp == −1; buy_price is called again for the payment (two draws in a flag-8 place).
        // UNCERTAIN: for a price word ≥ 0x8000 (never reached with the game's values) money_set's sign handling is assumed to match Money.From.
        if (s.Party.Purse < Money.From(0, 0, BuyPrice(s, shop, code)))
        {
            return -1;
        }
        var d = s.Item(code);
        var stack = new ItemStack(code, (byte)d.Type, (byte)OfferedQuality(s, shop, code), 1, (byte)d.Weight);
        s.Party.Purse = s.Party.Purse.Subtract(0, 0, BuyPrice(s, shop, code));
        return Inventory.AddStack(s, member, stack);
    }

    /// <summary>
    /// Sells one item of the member's pack entry <paramref name="index"/> for its sell price; items
    /// shops do not deal in stay. Returns the member's stack count, or −1 when nothing was sold.
    /// As in the original the entry may be past the last stack (a lit row past the end): its stale
    /// item is sold (item 0 at quality 0 for an entry never written) and loses an item.
    /// </summary>
    // orig: barter_key R032+0x0F60 (1462:0F60) draft (byte-matched function), case S (0x1F53/0x1F73); tests: TradingAndLootTests
    public static int Sell(GameState s, int member, int index)
    {
        var pack = s.Party.Slot(member).Pack;
        // C: the screen checks only DS:8838[who] (its stack count) != 0; the entry is read whatever the count (Pack).
        // DIVERGENCE: the original tests the 0x80 flag and prices item_defs[id] with the unmasked code (marker bits above 0xFFF read outside the item table); the code is masked here.
        var code = pack[index].Code & 0xFFF;
        if (s.Item(code).CannotBeTraded)
        {
            return -1;
        }
        s.Party.Purse = s.Party.Purse.Add(0, 0, SellPrice(s, code, pack[index].Quality));
        return Inventory.TakeAt(s, member, index);
    }
}
