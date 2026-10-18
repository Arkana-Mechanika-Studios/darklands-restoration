using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// What the witches' High Sabbat cards ($Witcd28 … $Witcd35) remember about the party's day on
/// the sabbat field: location facts of group 1 (kind 0x1C).
/// </summary>
internal static class SabbatFieldFacts
{
    private const int Group = 1;

    /// <summary>How suspicious the witches are of the party (a value: 5 and 10 are thresholds).</summary>
    public const int Suspicion = 7;

    /// <summary>How many of the field's sights the party has visited today (a value).</summary>
    public const int SightsVisited = 8;

    /// <summary>The feast was spoiled (the innocents saved, or the cooking ruined).</summary>
    public const int FeastSpoiled = 9;

    /// <summary>The night before the great ceremony has passed.</summary>
    public const int NightPassed = 0x0B;

    /// <summary>The party took the cap of the man who leads the initiations.</summary>
    public const int InitiatorsCap = 0x0C;

    /// <summary>A vial of the flying ointment was stolen.</summary>
    public const int FlyingOintment = 0x0D;

    /// <summary>A saint purified the altar.</summary>
    public const int AltarPurified = 0x0E;

    /// <summary>The altar was sabotaged with Scripture and holy symbols.</summary>
    public const int AltarSabotaged = 0x0F;

    /// <summary>The party fought the demon of the summoning.</summary>
    public const int DemonFought = 0x10;

    /// <summary>The initiations were ended by an earthquake.</summary>
    public const int InitiationsEnded = 0x1E;

    /// <summary>The card the sabbat site leads to once the party has left it for good.</summary>
    public const int AbandonedSiteCard = 0x13D;

    public static bool Known(CardContext c, int fact) => c.Events.FactAnywhere(Group, fact);

    public static int Value(CardContext c, int fact) => c.Events.FactValueAnywhere(Group, fact);

    /// <summary>Adds to a fact's value (the fact is created when missing).</summary>
    public static void Add(CardContext c, int fact, int amount) =>
        c.Events.Update(-1, -1, EventKinds.LocationFact, fact, amount, Group, EventUpdate.Add);

    /// <summary>Clears a fact's value.</summary>
    public static void Clear(CardContext c, int fact) =>
        c.Events.Update(-1, -1, EventKinds.LocationFact, fact, 0, Group, EventUpdate.And);

    /// <summary>Notes a fact here for a number of hours.</summary>
    public static void Note(CardContext c, int fact, int hours) =>
        c.Events.Create(0, c.Here, 0, EventKinds.LocationFact, fact, 0, 0, hours, 0, Group);

    /// <summary>The sabbat site no longer holds the sabbat: arriving there leads to the abandoned site.</summary>
    // orig: R126+0x0000 (1838:0000) matched (the write `*(int far *)(locations[cur_loc] + 0xc) = 0x13d` is inlined in each sabbat card that abandons the site; also R126 seg 199D, R127+0x0AC2)
    // DIVERGENCE: the C writes word 6 of locations[cur_loc] without a check; the C# skips it when the party is not at
    //   a place (the wilderness), where the C would write outside the location table.
    public static void AbandonSite(CardContext c)
    {
        if (c.HerePlace is { } here)
        {
            here.ArrivalCard = AbandonedSiteCard;
        }
    }

    /// <summary>The usual walk away from one of the field's sights back to the field (card 0x142).</summary>
    public static void BackToTheField(CardContext c, int suspicion, int sights)
    {
        Add(c, Suspicion, suspicion);
        ReturnToTheField(c, sights);
    }

    /// <summary>The walk back to the field (text 2) without a change of suspicion.</summary>
    public static void ReturnToTheField(CardContext c, int sights)
    {
        c.Screen.Text(2);
        Add(c, SightsVisited, sights);
        c.Next = 0x142;
    }
}
