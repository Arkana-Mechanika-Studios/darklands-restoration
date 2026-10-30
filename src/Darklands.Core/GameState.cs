using Darklands.Core.Characters;
using Darklands.Core.Events;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Core;

/// <summary>Difficulty levels (Alt-D cycles them).</summary>
public enum Difficulty
{
    Basic = 0,
    Standard = 1,
    Expert = 2,
}

/// <summary>Everything that makes up a game in progress.</summary>
public sealed class GameState
{
    /// <summary>No location: the party is in the open country.</summary>
    public const int Wilderness = -1;

    public GameState(IRandomSource random)
    {
        Random = random;
        Events = new EventLog(this);
    }

    public Party Party { get; set; } = new();

    /// <summary>Every character of the game, the party's and those left at inns (CHARACTR.TMP).</summary>
    public CharacterRoster Roster { get; } = new();

    /// <summary>The clock. A new game starts in 1400.</summary>
    public GameDate Date { get; set; } = new(0, 0, 0, 1400);

    public Difficulty Difficulty { get; set; } = Difficulty.Standard;

    public List<Location> Locations { get; } = [];

    /// <summary>Index of the location the party is at, or <see cref="Wilderness"/>.</summary>
    public int CurrentLocation { get; set; } = Wilderness;

    /// <summary>Where the party stands on the world map.</summary>
    public MapPosition MapPosition { get; set; }

    /// <summary>The card being shown and the one before it.</summary>
    public int CurrentCard { get; set; }

    public int PreviousCard { get; set; }

    /// <summary>Cards remembered by scripts to return to later (saved with the game).</summary>
    public int SavedCard { get; set; }

    public int SavedCard2 { get; set; }

    public IRandomSource Random { get; }

    /// <summary>Seed source used to reseed the generator after time passes (the game uses the real-time clock).</summary>
    public Func<uint>? ClockSeed { get; set; }

    public Location? Here => CurrentLocation >= 0 && CurrentLocation < Locations.Count ? Locations[CurrentLocation] : null;

    public bool InCity => CurrentLocation is >= 0 and < Location.CityCount;

    /// <summary>Which of the original's bugs are fixed (none by default).</summary>
    public OriginalBehaviour Behaviour { get; set; } = OriginalBehaviour.Faithful;

    /// <summary>The battle words cards hand to the main loop (DS:A766..A770, 9080, A893).</summary>
    public Battles.BattleOrders BattleOrders { get; } = new();

    /// <summary>
    /// The marching order: five slot numbers, then the length (the sixth byte). Chosen with "Change Marching Order"
    /// (<see cref="Rules.MarchingOrderChoice"/>), kept by a battle when it still lists every able member, saved with the game.
    /// </summary>
    // orig: DS:C624 (five bytes) and DS:C629 (the length), the save header's +0x9B..+0xA0 (written and read as six bytes); 0 at start-up
    // (the default party's load does not read it); R022+0x051C writes it, R016+0x0DA6 checks it when a battle starts
    public byte[] MarchingOrder { get; } = new byte[6];

    /// <summary>The 300 timed events.</summary>
    public EventLog Events { get; }

    /// <summary>The items left at places (CACHE.TMP), referred to by <see cref="Location.CacheHandle"/>.</summary>
    public ItemStores ItemStores { get; } = new();

    /// <summary>
    /// The item store screen's working table (2E38:37F4): not saved, it keeps what the stores
    /// opened in this run left in it (a loaded game takes the running game's).
    /// </summary>
    public StoreTable StoreTable { get; set; } = new();

    /// <summary>
    /// The card engine's ten option flags (<c>card-option-flag-0..9</c>, DS:EE76..EE88): one set for all
    /// cards, which copy them into their lines' states every round; a card that leaves one
    /// unassigned shows what the card before left. Not saved (a load keeps the running game's).
    /// </summary>
    // orig: card-option-flag-0 DS:EE76 (declared char) .. card-option-flag-9 DS:EE88 (words), BSS: 0 at start-up
    public int[] OptionFlags { get; set; } = new int[10];

    /// <summary>
    /// The hirelings counted when one last joined (<c>DS:A67C</c>): event-total(0x43) after
    /// <c>join-party-member</c>, 0 for the default party, and the saved word on loading. A term that ends or
    /// is removed later does not change it.
    /// </summary>
    // orig: DS:A67C: join-party-member R029+0x4650 (end), R037 seg 1E26 key Q (= 0), saved-game-read R037 seg 1C7D (fread), the save (R037 seg 1462 fwrite); read by R081 1962
    public int HirelingCount { get; set; }

    /// <summary>
    /// The last castle's progress (DS:160C): bits 1, 2, 4, 8, 0x10, 0x20, 0x40 the Horsemen and
    /// trials overcome, 0x1010 / 0x2020 further steps of the ending. Not in the .SAV: a load keeps
    /// the running game's value unless the game was saved with a battle file, whose variables
    /// hold it (battle-store/battle-reload at +0x1974).
    /// </summary>
    // orig: DS:160C (R003 seg 0E76 battle-store/battle-reload; set and tested by the LastC cards R130..R135)
    public int LastCastleFlags { get; set; }

    /// <summary>The item list and city records (from the game files).</summary>
    public IGameData Data { get; set; } = NoGameData.Instance;

    /// <summary>Where the rules' messages go.</summary>
    public IGameMessages Messages { get; set; } = new GameMessageLog();

    /// <summary>
    /// The working copy of the city record (<c>city-record</c>): the current city's, or the last
    /// city's while the party is elsewhere. Loaded on first use.
    /// </summary>
    public CityRecord City
    {
        get
        {
            if (_city is null || (InCity && _city.City != CurrentLocation))
            {
                _city = Data.LoadCity(InCity ? CurrentLocation : 0);
            }
            return _city;
        }
        set => _city = value;
    }

    private CityRecord? _city;

    /// <summary>Reports skill changes beside the portraits (the original's flag at DS:26BC, initially on).</summary>
    public bool ReportSkillChanges { get; set; } = true;

    /// <summary>Option "saintly changes" (Alt-C): report the changes saints and boosts make.</summary>
    public bool ShowSaintlyChanges { get; set; } = true;

    /// <summary>The terrain code under the party on the world map (1 ocean, 2 major river, ...).</summary>
    public int PartyTerrain { get; set; }

    /// <summary>The party's standing with the Fugger bankers (DS:4BB6, read by the $fugge00 card; St.Matthew raises it).</summary>
    public int FuggerStanding { get; set; }

    /// <summary>The party's standing with the Medici bankers (DS:4BB8, read by the $Medic00 card; St.Matthew raises it).</summary>
    public int MediciStanding { get; set; }

    /// <summary>The party's standing with the Hanseatic League (DS:4BBA, read by the $Hansa00 card; 0 at the start).</summary>
    // C: the saved game does not hold DS:4BB6/4BB8/4BBA: they keep their values across a load (OriginalSaves.Read carries them over from the running game).
    // BUG: so the party's standing with the Fugger, the Medici and the Hanse is lost by saving: a load keeps the last game's (or the start's 0).
    public int HanseStanding { get; set; }

    /// <summary>
    /// How the party last left the world map (DS:A891, saved with the game): 1 while it waited,
    /// 3 after travelling; the main loop sets 0 after any card but the map.
    /// </summary>
    // orig: DS:A891 (root seg 03A1: `if (DS:A772 != 0xc) DS:A891 = 0;`; saved at +0xA4 by R037+0x2ACE)
    public int MapLeaveMode { get; set; }

    /// <summary>What persists between battles at one place: the last battle's location (DS:DB27) and its cleared levels (DS:DA34).</summary>
    public Battles.Generation.BattlefieldMemory Battlefield { get; } = new();

    /// <summary>
    /// The battle file the program holds (the last battle's variables and battlefield, or the last load's): every save
    /// writes it as the .BSV; null before any battle or battle file (a new game).
    /// </summary>
    // orig: the globals and work files of the battle-state save R003+0x00DC and restore R003+0x0CE8 (they outlive a load: a saved game
    // without a battle file leaves them as they were)
    public Battles.BattleFile? BattleFile { get; set; }

    /// <summary>
    /// DS:9081 bit 0 (combat flag 0x100): the game was saved in a battle and is taken up inside it: the main loop skips the
    /// card and the battle starts again from <see cref="BattleFile"/>; cleared when that battle ends.
    /// </summary>
    // orig: R037+0x8ED4 (1C7D:0D24): set when bit 7 of the saved difficulty is (which is then cleared); R002+0x0000 clears it at the
    // battle's end (the combat flags are masked with 0xFE10)
    public bool ResumeBattle { get; set; }

    /// <summary>
    /// What the persistence layer keeps of the saved game this state was read from (the bytes the
    /// model does not name, the battle file); opaque to the rules, null for a new game.
    /// </summary>
    public object? OriginalSave { get; set; }

    /// <summary>The definition of an item code (an unknown item when the list does not have it).</summary>
    public ItemDefinition Item(int code) =>
        code >= 0 && code < Data.ItemDefinitions.Count ? Data.ItemDefinitions[code] : new ItemDefinition(code, "", "", 0, 0, 0, 0, 0);
}
