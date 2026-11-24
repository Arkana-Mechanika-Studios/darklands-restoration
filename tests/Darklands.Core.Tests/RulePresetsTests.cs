namespace Darklands.Core.Tests;

/// <summary>The rules presets' names, the sets they make, and the preset a new game plays with.</summary>
public sealed class RulePresetsTests
{
    [Fact]
    public void NamesReadBackInAnyCase()
    {
        foreach (var preset in Enum.GetValues<RulePreset>())
        {
            Assert.Equal(preset, RulePresets.Parse(RulePresets.Name(preset)));
            Assert.Equal(preset, RulePresets.Parse(RulePresets.Title(preset).ToUpperInvariant()));
        }
        Assert.Null(RulePresets.Parse("classic"));
        Assert.Null(RulePresets.Parse(null));
        Assert.Equal([RulePreset.Original, RulePreset.Fixed], RulePresets.Offered);
    }

    [Fact]
    public void ASetRemembersItsPresetAndWhatDiffersFromIt()
    {
        Assert.Equal(RulePreset.Faithful, OriginalBehaviour.Faithful.Preset);
        Assert.Equal(RulePreset.Original, OriginalBehaviour.DataSafe.Preset);
        var fixedSet = OriginalBehaviour.Of(RulePreset.Fixed);
        Assert.Equal(RulePreset.Fixed, fixedSet.Preset);
        Assert.Empty(fixedSet.Differences());
        var gameplay = RuleTable.Flags.First(r => r.Class == "gameplay").Id;
        fixedSet.Set(gameplay, false);
        Assert.Equal([(gameplay, false)], fixedSet.Differences());
        Assert.False(fixedSet.SameFlags(OriginalBehaviour.Of(RulePreset.Fixed)));
        Assert.True(OriginalBehaviour.Of(RulePreset.Fixed).SameFlags(OriginalBehaviour.Of(RulePreset.Restored)));
    }

    /// <summary>Until the gameplay fixes are switched on for players (rules set A3), a choice of Fixed plays as Original.</summary>
    [Fact]
    public void FixedPlaysAsOriginalUntilItsFixesAreSwitchedOn()
    {
        Assert.Equal(RulePreset.Original, RulePresets.ForNewGames(RulePreset.Original));
        Assert.Equal(RulePresets.FixedPlayable ? RulePreset.Fixed : RulePreset.Original, RulePresets.ForNewGames(RulePreset.Fixed));
        Assert.Equal(RulePreset.Faithful, RulePresets.ForNewGames(RulePreset.Faithful));
        Assert.False(RulePresets.FixedPlayable);
    }
}
