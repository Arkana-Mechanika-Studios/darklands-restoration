using Darklands.Core.Randomness;
using Darklands.Persistence;

namespace Darklands.Cards.Tests.Oracle;

/// <summary>
/// The party seed (header word +0x64) across the original's own save and load: the files DARKLANDS_PARTY_SEED_FILES lists
/// (path=seed, separated by ';'; local/batch/discordq: states the original's writer R037 1462:2ACE wrote from a loaded game)
/// are read by the C# and written back; the seed must be the one the original kept, and the C# file must hold it again.
/// </summary>
public sealed class PartySeedSaveLoadTests
{
    [Fact]
    public void SeedSurvivesTheOriginalsOwnSaveAndTheCSharpReadAndWrite()
    {
        if (!CardHarness.Available || Environment.GetEnvironmentVariable("DARKLANDS_PARTY_SEED_FILES") is not { Length: > 0 } list)
        {
            return;
        }
        foreach (var entry in list.Split(';'))
        {
            var (path, seed) = (entry.Split('=')[0], int.Parse(entry.Split('=')[1]));
            var data = CardHarness.World!;
            var running = Simulation.NewGame.StartUp(data, 7);
            var state = OriginalSaves.Read(File.ReadAllBytes(path), [], Path.GetFileName(path), data, running, new MscRandom(7)).State;
            Assert.Equal(seed, state.Party.Seed);
            var (written, _) = OriginalSaves.Write(state);
            Assert.Equal(seed, BitConverter.ToUInt16(written, 0x64));
        }
    }
}
