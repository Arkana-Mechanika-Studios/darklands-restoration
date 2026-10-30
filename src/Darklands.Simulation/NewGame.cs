using Darklands.Content;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Core.Time;

namespace Darklands.Simulation;

/// <summary>Starting a new game.</summary>
// orig: the start of the main function root 03A1:000A matched + the start screen's "default party" path R037+0x9DB2 (1E26:0172) matched; tests: CardSmokeTests (every card starts from NewGame.Quickstart), TextAndOpeningTests
public static class NewGame
{
    /// <summary>The card a new game opens with: the party at an inn ("$Party02").</summary>
    // orig: DS:A772 = 0x29 in root 03A1:000A (DS:A772 is the screen the main loop dispatches through R136+0xD090[])
    public const int FirstCard = 0x29;

    /// <summary>The starting city is one of the first 90 cities.</summary>
    // orig: DS:907E = roll-below(0x5a) (location-now) in root 03A1:000A; roll-below = root 0000:0C70 (rand() % n)
    // C: roll-below(n) is rand() % n with n below 2 counted as 2; 0x5A = 90, so the last two cities (90 and 91) are never the start.
    public const int StartingCities = 0x5A;

    /// <summary>The date the program starts with, before a game sets its own (9:00 on day 26 of month 8, 1400; the first card sets day and month).</summary>
    // orig: the initial words of hour, day, month, year at DS:00E0..00E7 (read in the original at the creation menu, local/batch/evid1o creation-menu.steps);
    // only R054+0x0A7D..0x0AA0 (the first card) and the date functions write them
    public static readonly GameDate StartUpDate = new(9, 26, 8, 1400);

    /// <summary>
    /// A Quickstart game: the four ready-made adventurers at an inn in a random city.
    /// <paramref name="seed"/> seeds the random generator (the game uses the clock); <paramref name="partySeed"/> is the BIOS tick word
    /// the party seed is (none: the low word of <paramref name="seed"/>).
    /// </summary>
    public static GameState Quickstart(WorldData world, uint seed, Func<uint>? clockSeed = null, int? partySeed = null)
    {
        // orig: srand((unsigned)time(0)) in root 03A1:000A seeds the generator (here: seed); the first draw is the starting city.
        var random = new MscRandom(seed);
        var city = random.Below(StartingCities);
        // orig: party-random-seed = the BIOS timer word at 0040:006C, saved-game-read("default"), party-defaults-overlay-entry(), party-headcount = 4 (R037+0x9DB2, key Q)
        // DIVERGENCE: the original seeds the default party from the tick count and the generator from time(); here both come from the one seed unless the host gives the tick word (partySeed: the game host and the lockstep tester do). It also writes charactr.tmp (ids 0..3 and the shield letters) and loads the shield pictures; the party panel loads those on demand.
        return Start(world, random, city, DefaultParty.Create(seed: partySeed ?? PartySeed(seed)), clockSeed);
    }

    /// <summary>
    /// The program's start (<c>root 03A1:000A</c>): srand(time(0)), location-now = roll-below(0x5A) and world-read, so city-record
    /// holds that random city's record before the start screen; a game loaded from the start screen reads its city over it.
    /// </summary>
    // orig: root 03A1:000A (03a1:000a) matched: srand, DS:907E = roll-below(0x5a), DS:A772 = 0x29, thunk 09C0:18E7("darkland.loc") = world-read
    public static GameState StartUp(Core.World.IGameData world, uint seed)
    {
        var random = new MscRandom(seed);
        var city = random.Below(StartingCities);
        var state = new GameState(random) { Data = world, CurrentLocation = city };
        state.City = world.LoadCity(city);
        return state;
    }

    /// <summary>The party seed taken when the player picks a start (the BIOS tick count, 16 bits).</summary>
    // C: party-random-seed is a 16-bit int (the low word of the BIOS tick count), hence the mask.
    public static int PartySeed(uint seed) => (int)(seed & 0xFFFF);

    /// <summary>
    /// The start every new game shares: the world's places, the party at <paramref name="city"/>
    /// and the opening card, which sets the date and the purse.
    /// </summary>
    // orig: root 03A1:000A main loop (unmatched per the byte match): srand(time(0)); location-now = roll-below(0x5A); screen = 0x29
    // C: the city is the first draw after the start-up srand, before the start screen runs.
    internal static GameState Start(WorldData world, IRandomSource random, int city, Party party, Func<uint>? clockSeed)
    {
        var state = new GameState(random) { ClockSeed = clockSeed };
        // orig: thunk 09C0:18E7("darkland.loc") = world-read R043+0x0100 (1462:0100): the location records
        state.Locations.AddRange(world.CreateLocations());
        state.CurrentLocation = city;
        state.Party = party;
        // C: Quickstart's charactr.tmp holds the four default records in slots 0..3; "Create characters" replaces this roster with its own (CreationRoster.CopyTo).
        state.Roster.Reset(party);
        state.CurrentCard = FirstCard;
        return state;
    }
}
