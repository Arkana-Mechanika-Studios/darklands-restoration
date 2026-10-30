using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Rules;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

/// <summary>Branches and C arithmetic the fidelity review restored (see docs/source-map/map.csv).</summary>
public sealed class FidelityFixTests
{
    [Fact]
    public void TheOvernightHealingTestLeavesTheBestHealerInTheLastQuery()
    {
        // hours-elapse asks party-ability-top(13), which sets top-value-so-far/top-scoring-member as a side effect.
        var s = TestGame.Create();
        var able = s.Party.Members.Select((m, i) => (m, i)).Where(x => x.m.IsAble).ToList();
        var best = able.Max(x => x.m[Skill.Healing]);
        var healer = able.First(x => x.m[Skill.Healing] == best).i;
        s.Party.Last.Value = 123;
        s.Party.Last.Member = 4;
        TimePassage.Advance(s, 24);                 // 09:00 + 24 h crosses midnight
        Assert.Equal(best, s.Party.Last.Value);
        Assert.Equal(healer, s.Party.Last.Member);
    }

    [Fact]
    public void EncumbranceLivesInASignedByte()
    {
        var s = TestGame.Create();
        MemberRules.SetEncumbrance(s, 0, 120);
        MemberRules.AddEncumbrance(s, 0, 10);       // 130 wraps to -126 in the byte: "< 0" -> 0
        Assert.Equal(0, MemberRules.Encumbrance(s, 0));
        MemberRules.SetEncumbrance(s, 0, 200);      // stored as -56
        Assert.Equal(-56, MemberRules.Encumbrance(s, 0));
        // compared as unsigned with Strength + Endurance: the heaviest band, Agility 1
        Assert.Equal(3, MemberRules.EncumbranceBand(s, 0));
        Assert.Equal(1, MemberRules.EffectiveAgility(s, 0));
        MemberRules.SetEncumbrance(s, 0, 10);
        Assert.Equal(0, MemberRules.EncumbranceBand(s, 0));
    }

    [Fact]
    public void TheSeventhDayTestOfCityPoliticsWrapsIn16Bits()
    {
        var s = TestGame.Create(location: 5);
        ((TestData)s.Data).CityBytes[0x56] = 40;     // city ordinal: place-query 0x21 = 40 + party seed
        s.Party.Seed = 32720;                        // 32760 + day 9 = 32769 = -32767 (short) = -7 * 4681
        s.Date = s.Date with { Day = 9 };
        var e = new TimedEvent { Kind = EventKinds.LocationFact, Owner = -2, Location = 5, Type = 0xC, Tag = 2 };
        CityPolitics.DailyPolitics(s, e);
        Assert.Equal(1, s.Events.All().Count(x => x.Event.Kind == EventKinds.LocationFact && x.Event.Type is 0x15 or 0x16));
    }

    [Fact]
    public void AQuestOfTag3ReturnsWhatTheOriginalLeavesInAx()
    {
        var s = TestGame.Create();
        var r = Quests.StartQuest3(s, -2, 200, 1, 10, 2, 7, 6);
        Assert.Equal(s.Events.Last, r);
        Assert.Equal(0x2B, s.Events[r]!.Kind);
        Assert.Equal(1, Quests.StartQuest3(s, -2, 200, 1, 10, 2, 7, 6));   // the existing quest: 1
    }

    [Fact]
    public void TheAttributeQueriesKeepTheirOwnMember()
    {
        var s = TestGame.Create();
        PartyQueries.MaxAttributeOfWomen(s, (int)Attribute.Charisma);
        var woman = s.Party.Last.Member;
        Assert.True(s.Party.Members[woman].IsFemale);
        Assert.Equal(woman, s.Party.Last.AttributeMember);
        PartyQueries.MinSkill(s, (int)Skill.Healing);
        PartyQueries.MaxAttributeSum(s, 0, 1);       // party-stat-top-sum does not write DS:9A44
        Assert.Equal(woman, s.Party.Last.AttributeMember);
    }

    [Fact]
    public void TheFixedBestWeaponSkillReportsSkillZeroWhenNothingIsAboveZero()
    {
        var s = TestGame.Create();
        s.Behaviour = new OriginalBehaviour { FixBestWeaponSkillNumber = true };
        Array.Clear(s.Party.Members[0].Skills, 0, 7);
        s.Party.Last.Skill = 5;
        Assert.Equal(0, MemberRules.BestWeaponSkill(s, 0));
        Assert.Equal(0, s.Party.Last.Skill);
    }

    [Fact]
    public void TheFormulaPageCountsTheIngredientsOfEverySlotBelowThePartySizeTheBenchOnlyOccupiedOnes()
    {
        var s = TestGame.Create();
        var (bench, page) = (Brewing.IngredientsCarried(s)[0x50], Brewing.IngredientsOnTheFormulaPage(s)[0x50]);
        s.Party.Members[1].Pack.Add(new ItemStack(0x50, 0, 25, 3, 1));
        s.Party.Members[1].Status = CharacterStatus.Empty;   // left the party: the slot keeps its pack
        s.Party.Members[2].Pack.Add(new ItemStack(0x50, 0, 25, 2, 1));
        Assert.Equal(bench + 2, Brewing.IngredientsCarried(s)[0x50]);
        Assert.Equal(page + 5, Brewing.IngredientsOnTheFormulaPage(s)[0x50]);
    }

    [Fact]
    public void AMemberReportedAwayIsNamedByHisRosterEntryAndLeavesForTheRoster()
    {
        var s = TestGame.Create(location: 7);
        s.Roster.Reset(s.Party);
        s.Roster.Characters.Insert(0, new Character { Name = "left at an inn" });   // Hans (slot 2) is roster entry 3 now
        for (var slot = 0; slot < 4; slot++)
        {
            s.Roster.PartySlots[slot]++;
        }
        var hans = s.Party.Members[2];
        hans.Status = CharacterStatus.Away;
        Assert.Equal(1, PartyRules.ReportAway(s));
        var note = s.Events.All().Single(x => x.Event.Kind == EventKinds.MemberAway).Event;
        Assert.Equal(3, note.Type);                     // character-in-slot(2), not the slot
        Assert.Equal(-1, s.Roster.PartySlots[2]);
        Assert.Equal(CharacterStatus.Away, hans.Status);              // the roster record is written with status 5
        Assert.Equal(CharacterStatus.Empty, s.Party.Slot(2).Status);
        Assert.Equal(7, hans.RosterLocation);
    }

    [Fact]
    public void ASlotLeftEmptyKeepsItsLastOccupantsBytes()
    {
        var s = TestGame.Create();
        var ebhard = s.Party.Members[3];
        PartyRules.RemoveFromParty(s, 3);
        Assert.Equal(3, s.Party.Members.Count);
        Assert.Equal(ebhard.Name, s.Party.Slot(3).Name);  // party-leave clears the slot's status byte only
        Assert.Equal(ebhard.Pack.Entries, s.Party.Slot(3).Pack.Entries);
        Assert.Equal(CharacterStatus.Empty, s.Party.Slot(3).Status);
    }
}
