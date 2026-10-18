using Darklands.Core.Characters;
using Darklands.Core.World;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$LastC15": the end of the game. Baphomet's head looses six plagues and the six Keys of the
/// Apocalypse (taken from the party) undo them one by one; for the seventh he offers a bargain.
/// Accepting brings wealth, better attributes and good will everywhere (then the map, card 0x0C);
/// refusing defeats him by the party's hope: the closing film plays, the party wakes in the inn
/// of the nearest city with great fame (card 0x1D). Either way the castle moves away: this place
/// becomes an empty site and a neighbouring one becomes the last castle again.
/// </summary>
// orig: R133+0x139A (1971:000A) matched; tests: CardSmokeTests
//   option 0: action R133+0x169E; hint R133+0x1834
//   option 1: action R133+0x186C; hint R133+0x1BD2
//   other: R133+0x1C0A, R133+0x1C42
// DORMANT (generated): option slots 10..19 keep the default handler R133+0x1C42 ("screwed up.") and are never shown.
// C (generated): opt_flag_2/3/4/5/6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class BaphometsPlagues : Card
{
    public override int Number => 359;

    private const int Picture = 265;
    private const int Head = 264;
    private const int TreeOfLife = 291;
    private const int BurningSea = 276;
    private const int Wormwood = 282;
    private const int Darkness = 288;
    private const int Locusts = 335;
    private const int DemonLancers = 334;
    private const int Inn = 342;

    /// <summary>The six Keys of the Apocalypse.</summary>
    private static readonly int[] Keys = [0x8C, 0x8D, 0x8E, 0x8F, 0x90, 0x91];

    /// <summary>The map shows an empty site with icon 4; the last castle has icon 0x17.</summary>
    private const int EmptySiteIcon = 4;
    private const int LastCastleIcon = 0x17;

    /// <summary>The last castle's arrival card ($LastC00) and the "nothing here" card ($NoGo00).</summary>
    private const int LastCastleArrival = 0x158;
    private const int NothingHere = 0x62;

    /// <summary>Where the party goes when no city is found near it.</summary>
    private const int FallbackCity = 0x22;

    /// <summary>The cities and towns whose standing the ending improves.</summary>
    private const int Cities = 0x5C;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.PassHours(6);
        foreach (var key in Keys)
        {
            c.TakeItem(Who.Everybody, key);
        }
        c.Variables.Chosen[0] = c.Leader;
        var plagues = new[] { Head, TreeOfLife, BurningSea, Wormwood, Darkness, Locusts, DemonLancers, Picture };
        for (var block = 0; block < plagues.Length; block++)
        {
            ShowPicture(c, plagues[block]);
            c.Screen.Text(block);
        }

        c.Next = Number;
        var menu = Menu(c, 8);
        menu.Enable(0, 1);
        c.SetCardItems();
        c.SetCardSaints();
        return menu.Run(
            // orig: R133+0x169E / R133+0x1834 matched
            new MenuLine(_ => AcceptTheGift(c), OptionHint.Saying(SuccessAssured)),
            // orig: R133+0x186C / R133+0x1BD2 matched
            new MenuLine(_ => Refuse(c), OptionHint.Saying(CardTexts.Unknown)));
    }

    /// <summary>+5 to five attributes and their maxima, 50 florins, and better standing everywhere.</summary>
    // orig: R133+0x169E matched
    private static void AcceptTheGift(CardContext c)
    {
        Attribute[] gifts = [Attribute.Strength, Attribute.Endurance, Attribute.Perception, Attribute.Agility, Attribute.Intelligence];
        foreach (var attribute in gifts)
        {
            c.AddAttribute(Who.Everybody, (int)attribute, 5);
        }
        foreach (var attribute in gifts)
        {
            c.AddAttributeMaximum(Who.Everybody, (int)attribute, 5);
        }
        c.State.Party.Purse = c.State.Party.Purse.Add(50, 0, 0);
        if (Place(c, c.Here) is { } here)
        {
            here.ArrivalCard = NothingHere;
        }
        // C: the original also calls nearest_city here and ignores the answer (no side effect).
        // DIVERGENCE: the loops over the cities and the location writes are bounded by the location list here; the C
        //   indexes the 92 cities and locations[cur_loc ± k] without a check.
        for (var city = 0; city < Math.Min(Cities, c.State.Locations.Count); city++)
        {
            c.AdjustReputation(city, 20, 20);
        }
        c.Screen.Text(11);
        TheCastleMoves(c);
        c.PassHours(2);
        var nearest = GoToTheNearestCity(c);
        // DIVERGENCE: the C calls loc_reputation_adj(v_si, 50, 50) even when no city was found (v_si = −1), on the
        //   record before the location table (and with its random draws); the C# skips it then.
        if (nearest != -1)
        {
            c.AdjustReputation(nearest, 50, 50);
        }
        c.Next = 0x0C;
    }

    /// <summary>Hope, the seventh key: the closing film, then the inn of the nearest city.</summary>
    // orig: R133+0x186C matched
    // C: the film runs only while films are on (DS:0BF2 clear); it reloads the card (card_load with pic_buf, the
    //   card's picture) afterwards, as Show does here.
    private void Refuse(CardContext c)
    {
        c.Screen.Text(10, TextFrame.BlankPage);
        if (c.Film(Films.Ending))
        {
            Show(c, Picture);
        }
        TheCastleMoves(c);
        var nearest = GoToTheNearestCity(c);
        // DIVERGENCE: as in AcceptTheGift, the C adjusts "city −1" (outside the table, drawing randoms) when no city
        //   was found; the C# skips it.
        if (nearest != -1)
        {
            c.AdjustReputation(nearest, 50, 50);
        }
        // C: 40/40 and 20/20 are positive, so loc_reputation_adj draws its "will it change" roll and the amount.
        for (var city = 0; city < Math.Min(Cities, c.State.Locations.Count); city++)
        {
            c.AdjustReputation(city, 40, 40);
        }
        c.GiveReward(99);
        // C: with no city found the C reads the record at (−1) × 0x26E + 1 of darkland.cty and the description at (−1) × 0x50 + 1 of
        //   darkland.dsc: the runtime's fseek takes the position past the end, the reads return nothing and both keep what they held,
        //   as here (and nearest_city never returns −1).
        if (nearest != -1)
        {
            // The city's record (and with it its description) of the city the party woke in.
            c.LoadCity(nearest);
        }
        ShowPicture(c, Inn);
        c.Screen.Text(9);
        c.Next = 0x1D;
        c.PassHours(2);
    }

    /// <summary>
    /// This place becomes an empty site; the next location (or, failing that, the one four
    /// places before) becomes the last castle if it is an empty site.
    /// </summary>
    // orig: the location writes repeated inline in R133+0x169E / _04dc matched
    private static void TheCastleMoves(CardContext c)
    {
        if (Place(c, c.Here) is { } here)
        {
            here.ArrivalCard = NothingHere;
            here.Icon = EmptySiteIcon;
        }
        var site = Place(c, c.Here + 1) is { Icon: EmptySiteIcon } next ? next
            : Place(c, c.Here - 4) is { Icon: EmptySiteIcon } before ? before
            : null;
        if (site is not null)
        {
            site.Icon = LastCastleIcon;
            site.ArrivalCard = LastCastleArrival;
            site.EncounterCard = NothingHere;
        }
    }

    /// <summary>The party is now at the city nearest to its map position (city 0x22 if none); returns that city or −1.</summary>
    // orig: the cur_loc choice repeated inline in R133+0x169E / _04dc matched
    private static int GoToTheNearestCity(CardContext c)
    {
        var nearest = c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y);
        c.State.CurrentLocation = nearest == -1 ? FallbackCity : nearest;
        return nearest;
    }

    // DIVERGENCE: a bounds guard the C does not have (it would read/write outside the location table).
    /// <summary>A location record (null outside the table: the original would write elsewhere).</summary>
    private static Location? Place(CardContext c, int loc) =>
        loc >= 0 && loc < c.State.Locations.Count ? c.State.Locations[loc] : null;
}
