namespace Darklands.Battles.Tests;

/// <summary>
/// FixCampEmptyRowCleanUp (B-2a4a58, F-1 set 1): a stay left by a click on an empty party row skips its clean-up, Save and Load stay
/// off; fixed, the clean-up runs and they come back. The Quickstart party has four members: the fifth row is empty.
/// </summary>
public sealed class CampEmptyRowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClickOnAnEmptyRowLeavesWithOrWithoutTheCleanUp(bool fixedGame)
    {
        var host = new MusicTests.StepHost(new MusicTests.MusicRecorder(), "move:20,170", "wait:3", "down:", "wait:3", "up:", "wait:5");
        if (MusicTests.Start(host) is not var (ui, state, panel, world))
        {
            return;
        }
        Assert.True(state.Party.Slots.Count(m => m.OccupiesSlot) < 5);
        state.Behaviour.FixCampEmptyRowCleanUp = fixedGame;
        var saveAndLoad = new List<int>();
        var camp = MusicTests.Camp(ui, panel, world);
        camp.SaveAndLoad = saveAndLoad.Add;
        camp.Stay(state, 0);
        // the stay turns them off (2) as it opens; the clean-up turns them on (1)
        Assert.Equal(fixedGame ? [2, 1] : [2], saveAndLoad);
    }
}
