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
        // Restored is Fixed and the restoration flags (class restoration), no more
        var restored = OriginalBehaviour.Of(RulePreset.Restored);
        Assert.False(OriginalBehaviour.Of(RulePreset.Fixed).SameFlags(restored));
        Assert.Equal(RuleTable.Flags.Where(r => r.Class == "restoration").Select(r => r.Id), restored.On.Except(OriginalBehaviour.Of(RulePreset.Fixed).On));
        Assert.Empty(OriginalBehaviour.Of(RulePreset.Fixed).On.Except(restored.On));
    }

    /// <summary>Since rules set A3 Fixed plays as itself and is the preset of new games by default.</summary>
    [Fact]
    public void FixedPlaysAsItselfAndIsTheDefault()
    {
        Assert.True(RulePresets.FixedPlayable);
        Assert.Equal(RulePreset.Fixed, RulePresets.Default);
        Assert.Equal(RulePreset.Fixed, RulePresets.ForNewGames(RulePreset.Fixed));
        Assert.Equal(RulePreset.Original, RulePresets.ForNewGames(RulePreset.Original));
        Assert.Equal(RulePreset.Faithful, RulePresets.ForNewGames(RulePreset.Faithful));
        // every gameplay fix is on in a new game by default, none in Original
        var newGame = OriginalBehaviour.Of(RulePresets.Default);
        Assert.All(RuleTable.Flags.Where(r => r.Class == "gameplay"), r => Assert.True(newGame.IsOn(r.Id) && !OriginalBehaviour.Original.IsOn(r.Id), r.Id));
    }
}
