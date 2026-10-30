namespace Darklands.Core.Rules;

/// <summary>
/// What happens at the start of each month: birthdays, a ruined robber-knight castle is
/// taken over again every third month, and the monthly quests may start.
/// </summary>
public static class MonthlyUpdate
{
    /// <summary>
    /// The month <paramref name="month"/> begins (as the clock reports it: 12 for January
    /// before the year rolls over). The member whose slot is (party seed + month) mod 12 has
    /// a birthday; every third month the ruined robber-knight castle farthest within reach of
    /// here becomes a robber-knight castle again; then the dragon quest starts with chance
    /// 1/4 and the mine quest with 1/3 (when none is under way), and the sabbat quest always.
    /// In the wilderness the castle search starts from the party's map position (the original
    /// reads an undefined location record there).
    /// </summary>
    // orig: R029+0x281C (0E76:281C) matched; tests: PracticeAndItemsTests (birthdays castle sabbat)
    // C: the birthday test uses (unsigned)(party-random-seed + month-now) % 12 and the castle test months % 3: both the incremented month (12 = January before the year rolls).
    // DIVERGENCE: in the wilderness the original reads locations[-1] (an undefined record) for the castle search; here the party's map position.
    // C: `!open-quest-exists(4) && !roll-below(4)` short-circuits: no draw while a dragon quest is under way (the same for the mine).
    public static void Run(GameState s, int month)
    {
        s.ReportSkillChanges = true;
        var party = s.Party;
        for (var i = 0; i < party.Members.Count; i++)
        {
            if (party.Members[i].IsPresent && (ushort)(party.Seed + month) % 12 == i)
            {
                party.Members[i].Age++;
            }
        }
        if (month % 3 == 0)
        {
            var from = s.Here?.Position ?? s.MapPosition;
            var castle = LocationRules.FarOfType(s, from.X, from.Y, Icons.RuinedRaubritterCastle);
            if (castle != s.CurrentLocation && castle != -1)
            {
                var place = s.Locations[castle];
                place.Icon = Icons.RaubritterCastle;
                place.Flags = 0;
                place.ArrivalCard = 0x93;
                place.EncounterCard = 0x92;
                place.NoticeRange = 4;
            }
        }
        if (!s.Events.QuestExists(Quests.Dragon) && s.Random.Below(4) == 0)
        {
            Quests.StartDragon(s);
        }
        if (!s.Events.QuestExists(Quests.Mine) && s.Random.Below(3) == 0)
        {
            Quests.StartMine(s);
        }
        if (!s.Events.QuestExists(Quests.Sabbat))
        {
            Quests.StartSabbat(s);
        }
    }
}
