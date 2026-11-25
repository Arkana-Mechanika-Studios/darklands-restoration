using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Randomness;

namespace Darklands.Core.Tests;

/// <summary>
/// The rules' numbers (<see cref="RuleParameters"/>): the original's by default, immutable, given to every game state, and at their
/// defaults every site reads the original's number (the full replay shows the sessions unchanged; these show the reads).
/// </summary>
public sealed class RuleParametersTests
{
    private static readonly QuickstartName[] Names = [new("Gretchen", "Gretchen"), new("Gunther", "Gunther"), new("Hans", "Hans"), new("Ebhard", "Ebhard")];

    [Fact]
    public void TheDefaultsAreTheOriginals()
    {
        var p = RuleParameters.Original;
        Assert.True(p.IsOriginal);
        Assert.True(new RuleParameters().IsOriginal);
        Assert.Equal(RuleScale.One, p.StepMinutes);
        Assert.Equal(RuleScale.One, p.LodgingPrice);
        Assert.Equal(RuleScale.One, p.DamageToEnemies);
        Assert.Equal(RuleScale.One, p.DamageToParty);
        Assert.Equal(RuleScale.One, p.PracticeChance);
        Assert.Equal(1, p.BattleSkillTries);
        Assert.Null(p.QuickstartAttributes);
        Assert.Null(p.QuickstartSkills);
        Assert.Null(p.StartingMoney);
        Assert.False((p with { BattleSkillTries = 2 }).IsOriginal);
        Assert.True(p.IsOriginal);   // with gives a new object: the original's stays as it is
    }

    [Fact]
    public void AGameStateGetsTheOriginals() => Assert.Same(RuleParameters.Original, new GameState(new MscRandom(1)).Parameters);

    [Fact]
    public void AScaleOfOneGivesTheNumberItselfAndOthersRoundUp()
    {
        foreach (var value in new[] { int.MinValue, -129, -1, 0, 1, 7, 9999, int.MaxValue })
        {
            Assert.Equal(value, RuleScale.One.Apply(value));
            Assert.Equal(value, new RuleScale(3, 3).Apply(value));
        }
        Assert.Equal(3, new RuleScale(1, 3).Apply(9));
        Assert.Equal(4, new RuleScale(1, 3).Apply(10));
        Assert.Equal(1, new RuleScale(1, 3).Apply(1));
        Assert.Equal(14, new RuleScale(2, 1).Apply(7));
        Assert.Equal(-3, new RuleScale(1, 3).Apply(-10));
    }

    [Fact]
    public void TheQuickstartPartyIsTheOriginalsAtTheDefaults()
    {
        var plain = DefaultParty.Create(0x1234, Names);
        var original = DefaultParty.Create(0x1234, Names, RuleParameters.Original);
        for (var i = 0; i < plain.Slots.Count; i++)
        {
            Assert.Equal(plain.Slots[i].Attributes, original.Slots[i].Attributes);
            Assert.Equal(plain.Slots[i].AttributeMaximums, original.Slots[i].AttributeMaximums);
            Assert.Equal(plain.Slots[i].Skills, original.Slots[i].Skills);
        }
        // the reads work: a set value reaches every member
        var strong = DefaultParty.Create(0x1234, Names, RuleParameters.Original with { QuickstartAttributes = 60, QuickstartSkills = 75 });
        Assert.All(strong.Slots, m => Assert.All(m.Attributes, a => Assert.Equal(60, a)));
        Assert.All(strong.Slots, m => Assert.All(m.Skills, s => Assert.Equal(75, s)));
    }

    [Fact]
    public void StartingMoneyIsNoneByDefault() =>
        Assert.Equal(Money.From(5000, 0, 0), (RuleParameters.Original with { StartingMoney = Money.From(5000, 0, 0) }).StartingMoney);
}
