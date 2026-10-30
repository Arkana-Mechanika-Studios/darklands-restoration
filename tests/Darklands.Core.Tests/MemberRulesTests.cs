using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Rules;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Tests;

public sealed class MemberRulesTests
{
    private const int Str = (int)Attribute.Strength;
    private const int Religion = (int)Skill.Religion;

    [Fact]
    public void SkillsStayIn0To99AndChangesAreReported()
    {
        var s = TestGame.Create();
        var messages = new GameMessageLog();
        s.Messages = messages;
        MemberRules.AddSkill(s, 0, Religion, 200);
        MemberRules.AddSkill(s, 1, Religion, -200);

        Assert.Equal(99, s.Party.Members[0][Skill.Religion]);
        Assert.Equal(0, s.Party.Members[1][Skill.Religion]);
        Assert.Equal(["0: Religion changed from 13 to 99", "1: Religion changed from 9 to 0"], messages.Lines);
    }

    [Fact]
    public void EverybodyMeansEveryPresentMember()
    {
        var s = TestGame.Create();
        s.Party.Members[2].Status = CharacterStatus.Empty;
        MemberRules.AddSkill(s, Who.Everybody, Religion, 5);

        Assert.Equal([18, 14, 10, 45], s.Party.Members.Select(m => m[Skill.Religion]));
    }

    [Fact]
    public void AttributesStayIn1To99AndUnderMaximumPlusBoost()
    {
        var s = TestGame.Create();
        MemberRules.AddAttribute(s, 0, Str, 20);
        Assert.Equal(30, s.Party.Members[0][Attribute.Strength]);   // the maximum

        s.Events.Create(0, 5, 0x63, EventKinds.AttributeBoost, Str, 6, 6, 24, 1, 0);
        s.Events.Create(0, 5, 0x63, EventKinds.AttributeBoost, Str, 4, 6, 24, 1, 0);
        MemberRules.AddAttribute(s, 0, Str, 20);
        Assert.Equal(36, s.Party.Members[0][Attribute.Strength]);   // + the largest boost

        MemberRules.AddAttribute(s, 0, Str, -100);
        Assert.Equal(1, s.Party.Members[0][Attribute.Strength]);
        MemberRules.SetAttribute(s, 0, Str, 0);
        Assert.Equal(0, s.Party.Members[0][Attribute.Strength]);    // stat-assign allows 0
    }

    [Fact]
    public void SettingAMaximumIgnoresItsLimitUnlessFixed()
    {
        var s = TestGame.Create();
        MemberRules.SetAttributeMaximum(s, 0, Str, 120);
        Assert.Equal(120, s.Party.Members[0].Maximum(Attribute.Strength));

        s.Behaviour.FixSetAttributeMaximumLimit = true;
        MemberRules.SetAttributeMaximum(s, 0, Str, 120);
        Assert.Equal(99, s.Party.Members[0].Maximum(Attribute.Strength));
    }

    [Fact]
    public void ATemporaryAttributeBoostWearsOff()
    {
        var s = TestGame.Create();
        MemberRules.RaiseAttributeTemporarily(s, 0, Str, 5, 24);
        Assert.Equal(35, s.Party.Members[0][Attribute.Strength]);
        var e = s.Events[s.Events.Last]!;
        Assert.Equal((EventKinds.AttributeBoost, 0, 5, ExpiryHandler.UndoTemporaryBoost), (e.Kind, e.Owner, e.Value, e.ExpiryHandler));

        TimePassage.Advance(s, 23);   // 6 + rnd 3 + 24 - 5 = 19..21 hours
        Assert.Equal(30, s.Party.Members[0][Attribute.Strength]);
        Assert.Equal(0, s.Events.Count(EventKinds.AttributeBoost));
    }

    [Fact]
    public void AWholePartyAttributeBoostStopsAtAMemberAboveTheBoost()
    {
        var s = TestGame.Create();
        s.Party.Members[1][Attribute.Strength] = 50;   // 10 above the maximum of 40
        MemberRules.RaiseAttributeTemporarily(s, Who.Everybody, Str, 5, 24);

        Assert.Equal(35, s.Party.Members[0][Attribute.Strength]);
        Assert.Equal(25, s.Party.Members[2][Attribute.Strength]);   // never reached
        Assert.Equal(1, s.Events.Count(EventKinds.AttributeBoost));
    }

    [Fact]
    public void AWholePartySkillBoostGivesEachMemberTheBoostOncePerMember()
    {
        var s = TestGame.Create();
        MemberRules.RaiseSkillTemporarily(s, Who.Everybody, Religion, 2, 24);

        // four present members: everybody gains 4 x 2, recorded as four events owned by -2
        Assert.Equal([21, 17, 18, 48], s.Party.Members.Select(m => m[Skill.Religion]));
        Assert.Equal(4, s.Events.Count(EventKinds.SkillBoost));
        Assert.All(s.Events.All(), x => Assert.Equal(Who.Everybody, x.Event.Owner));

        // the expiry's presence test reads party[-2][0] (DS:9B69, 0): the boosts are never taken back
        TimePassage.Advance(s, 25);
        Assert.Equal(0, s.Events.Count(EventKinds.SkillBoost));
        Assert.Equal([21, 17, 18, 48], s.Party.Members.Select(m => m[Skill.Religion]));
    }

    [Fact]
    public void WholePartyBoostsAreTakenBackWithTheFix()
    {
        var s = TestGame.Create();
        s.Behaviour.FixEverybodyBoostsUndone = true;
        MemberRules.RaiseSkillTemporarily(s, Who.Everybody, Religion, 2, 24);
        TimePassage.Advance(s, 25);
        Assert.Equal([13, 9, 10, 40], s.Party.Members.Select(m => m[Skill.Religion]));
    }

    [Fact]
    public void TheWholePartyBoostsCanBeFixed()
    {
        var s = TestGame.Create();
        s.Behaviour.FixWholePartyBoosts = true;
        MemberRules.RaiseSkillTemporarily(s, Who.Everybody, Religion, 2, 24);
        Assert.Equal([15, 11, 12, 42], s.Party.Members.Select(m => m[Skill.Religion]));
    }

    [Fact]
    public void BestWeaponSkillAlwaysReportsSkillSix()
    {
        var s = TestGame.Create();
        Assert.Equal(35, MemberRules.BestWeaponSkill(s, 0));
        Assert.Equal(6, s.Party.Last.Skill);
    }

    [Fact]
    public void AttributeSumQueriesRecordTwiceTheFirstAttribute()
    {
        var s = TestGame.Create();
        // Strength + Agility: 30+29, 40+26, 25+28, 25+24 -> highest is Gunther's 66, recorded as 2 x 40
        Assert.Equal(80, PartyQueries.MaxAttributeSum(s, Str, (int)Attribute.Agility));
        Assert.Equal(1, s.Party.Last.Member);
        s.Behaviour.FixAttributeSums = true;
        Assert.Equal(66, PartyQueries.MaxAttributeSum(s, Str, (int)Attribute.Agility));
    }

    [Fact]
    public void PartyQueriesUseAbleMembersAndRememberWho()
    {
        var s = TestGame.Create();
        s.Party.Members[3].Status = CharacterStatus.Unconscious;
        Assert.Equal(13, PartyQueries.MaxSkill(s, Religion));   // Ebhard (40) is not able
        Assert.Equal(0, s.Party.Last.Member);
        Assert.Equal(9, PartyQueries.MinSkill(s, Religion));
        Assert.Equal(1, s.Party.Last.Member);
        Assert.Equal(10, PartyQueries.AverageSkill(s, Religion));
        Assert.False(PartyQueries.AllWell(s));
        Assert.Equal(1, PartyQueries.AbleWomen(s));
    }

    [Fact]
    public void InjuryNeverTakesAttributesBelowOne()
    {
        var s = TestGame.Create(seed: 3);
        MemberRules.Injure(s, 0, 50, 50, 1);
        Assert.Equal(1, s.Party.Members[0][Attribute.Strength]);
        Assert.Equal(1, s.Party.Members[0][Attribute.Endurance]);
    }

    [Fact]
    public void FameRewardsScaleWithDifficulty()
    {
        var s = TestGame.Create();
        Assert.Equal(25, Practice.GiveReward(s, 3));
        s.Difficulty = Difficulty.Basic;
        Assert.Equal(31, Practice.GiveReward(s, 2));   // 10 x 2/3 = 6
        s.Difficulty = Difficulty.Expert;
        Assert.Equal(331, Practice.GiveReward(s, 9));  // 200 x 3/2
        Assert.Equal(331, s.Party.Fame);
        Assert.Equal(15 - 331, Practice.ChargeFame(s, 4));   // the fame cost bug
    }
}
