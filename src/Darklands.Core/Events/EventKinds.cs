namespace Darklands.Core.Events;

/// <summary>
/// Event kinds the rules themselves use. Card scripts use many more, by number; a "(?)" marks
/// a meaning that is a guess from how the kind is used.
/// </summary>
public static class EventKinds
{
    /// <summary>A quest under way (tag = quest number, Location = its site, Value = a city).</summary>
    public const int Quest = 0x08;

    /// <summary>Hostile location (with reputation; <c>place-hostile</c>).</summary>
    public const int Hostile = 0x11;

    /// <summary>Prices +20% here (?).</summary>
    public const int Expensive = 0x12;

    /// <summary>Prices +20% here; also lowers the hostility threshold (?): set after a conflict.</summary>
    public const int Unrest = 0x13;

    /// <summary>A fact about a location (Type = fact number, Tag = fact group, Value = its value).</summary>
    public const int LocationFact = 0x1C;

    /// <summary>
    /// A brew under way (Owner = the alchemist, Type = the formula, Value = the chance in %,
    /// Extra = batches, Data1 = the camp mode, Data2 = the formula's risk); handler 1 ends it.
    /// </summary>
    public const int Brew = 0x1D;

    /// <summary>A teacher in a town (Type = the skill taught, Value = the daily fee in pfennigs, Extra = the teacher's level).</summary>
    public const int Teacher = 0x28;

    /// <summary>Lodgers here are turned out: "spend a day" at other lodging ends the stay (?).</summary>
    public const int TurnedOut = 0x3D;

    /// <summary>Started for two hours when the watch rousts the party sleeping rough in town.</summary>
    public const int RoustedSleepingRough = 0x52;

    /// <summary>Started for two hours when the camp in the wild is discovered (overland encounters go to the camp cards).</summary>
    public const int CampDiscovered = 0x5F;

    /// <summary>While started, camping in the wild carries no risk (?).</summary>
    public const int SafeCamping = 0x60;

    /// <summary>The risk of the last camp in the wild, remembered for a while (Value = the risk).</summary>
    public const int CampRisk = 0x61;

    /// <summary>A quest that has been resolved (made from a <see cref="Quest"/> event).</summary>
    public const int ResolvedQuest = 0x24;

    /// <summary>Started by the "follow-up" expiry handler for one day (?).</summary>
    public const int FollowUp = 0x3B;

    /// <summary>A hired member's term (Type = member, Value = term).</summary>
    public const int MemberTerm = 0x43;

    /// <summary>"Member has left" note for the party (Type = member).</summary>
    public const int MemberLeft = 0x47;

    /// <summary>A temporary attribute boost (Owner = member, Type = attribute, Value = points).</summary>
    public const int AttributeBoost = 0x48;

    /// <summary>A temporary skill boost (Owner = member, Type = skill, Value = points).</summary>
    public const int SkillBoost = 0x49;

    /// <summary>A potion's weapon bonus (never undone at expiry: an original bug).</summary>
    public const int PotionBonus = 0x4A;

    /// <summary>A saint's travel blessing, renewed every 6 hours while on water (?).</summary>
    public const int TravelBlessing = 0x4B;

    /// <summary>
    /// A character the inn's roster took into the party here (Type = roster entry, a week): while it runs,
    /// "Add to the Party" on that character at this place takes him or her out again (?).
    /// </summary>
    // orig: R038+0x02F8 (1462:02F8) key 0x1E41: R029+0x2A32 (0x4C, entry, 0xA8) when the character joins, R029+0x392C (0x4C, entry, DS:907E) to leave
    public const int JoinedAtInn = 0x4C;

    /// <summary>A member who went away (Type = member).</summary>
    public const int MemberAway = 0x4D;

    /// <summary>After a failed brew (outside cities) (?).</summary>
    public const int AlchemyMishap = 0x51;
}

/// <summary>What happens when an event with <see cref="EventFlags.CallExpiryHandler"/> ends (the original's table of ten).</summary>
// orig: event-expiry-handlers R136+0xAF9F
// C: read from the EXE: vectors 1E2D..1E87 = R029+0x3FB0 (0, empty), 3fb2, 4180, 4378, 4436, 4442, 448a, 450a, 4534, 4626 (1..9).
public enum ExpiryHandler
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A brew is done: the potions, or "Alchemy Failure".</summary>
    AlchemyResult = 1,

    /// <summary>A city's daily politics: shifts of the two sides, unrest.</summary>
    DailyCityPolitics = 2,

    /// <summary>The quests at the event's location are resolved.</summary>
    ResolveQuestsHere = 3,

    /// <summary>Starts a one-day <see cref="EventKinds.FollowUp"/> event here.</summary>
    FollowUp = 4,

    /// <summary>A hired member's term ends: a <see cref="EventKinds.MemberLeft"/> note.</summary>
    MemberTermEnds = 5,

    /// <summary>A temporary attribute or skill boost wears off.</summary>
    UndoTemporaryBoost = 6,

    /// <summary>The witches' sabbat at a site is over (its card changes back).</summary>
    SabbatEnds = 7,

    /// <summary>A witches' sabbat begins at a site; the next one is planned.</summary>
    SabbatBegins = 8,

    /// <summary>The travel blessing is renewed while the party is on water.</summary>
    RenewTravelBlessing = 9,
}

/// <summary>How <see cref="EventLog.Update"/> changes the value (<c>event-advance</c> op).</summary>
public enum EventUpdate
{
    Set = 0,
    Add = 1,
    And = 2,
    Or = 3,
}
