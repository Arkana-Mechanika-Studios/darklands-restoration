using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Simulation;

/// <summary>Starting a new game with a party made in "Create a new party".</summary>
public static class NewParty
{
    /// <summary>
    /// The game "Begin the Adventure" starts: the created <paramref name="party"/> at an inn in a
    /// random city, like Quickstart. The city is drawn as the original draws it at start-up, from
    /// <paramref name="seed"/>; the game then goes on with <paramref name="random"/>, the generator
    /// character creation used (its names reseed it from the clock), or a fresh one from the seed.
    /// </summary>
    // orig: R037+0x9DB2 (1E26:0172) matched, case 0x2E43 (C): party-random-seed = BIOS ticks;
    // R037+0x0000 returns 1 after "Begin the Adventure", and the main loop (root 03A1:000A) shows card 0x29
    // C: unlike Quickstart there is no load of SAVES\DEFAULT's party; the menu already read its characters into charactr.tmp.
    public static GameState Create(WorldData world, uint seed, Party party, IRandomSource? random = null, Func<uint>? clockSeed = null)
    {
        var startup = new MscRandom(seed);
        var city = startup.Below(NewGame.StartingCities);
        return NewGame.Start(world, random ?? startup, city, party, clockSeed);
    }
}
