namespace Darklands.Cards;

/// <summary>
/// Values the original keeps in global variables that <see cref="Core.GameState"/> does not
/// hold yet; until it does they live as long as the game state object.
/// </summary>
public sealed class CardScriptMemory(Core.Battles.BattleOrders orders)
{
    // The bank notes (DS:9072) are Party.BankNotes, saved with the game.

    // The bank favours (DS:4BB6, 4BB8, 4BBA) are GameState.FuggerStanding, MediciStanding and HanseStanding.

    /// <summary>
    /// The battle a card starts by returning −2..−6: the main loop passes these words to the
    /// battle (root 03A1: <c>thunk 09C0:18BF(A766, A768, A76A, −1, A76C, 0, −1, A76E, 0, −1, A770, 0)</c>).
    /// A card sets only some of them; the others keep what an earlier card left.
    /// </summary>
    public BattleWords Battle { get; } = new(orders);
}

/// <summary>The battle words of DGROUP that a card returning −2..−6 hands to the main loop: a view of the game's one <see cref="Core.Battles.BattleOrders"/>.</summary>
// orig: main loop of root seg 03A1 reading DS:A766..DS:A770 when a card returns −2..−6
public sealed class BattleWords(Core.Battles.BattleOrders orders)
{
    /// <summary>DS:A766 <c>battle_mode</c>.</summary>
    public int Mode { get => orders.Mode; set => orders.Mode = value; }

    /// <summary>DS:A768 <c>battle_enemy</c>.</summary>
    public int Enemy { get => orders.Enemy; set => orders.Enemy = value; }

    /// <summary>DS:A76A <c>battle_seed</c>.</summary>
    public int Seed { get => orders.Seed; set => orders.Seed = value; }

    /// <summary>DS:A76C, DS:A76E, DS:A770: the fifth, eighth and eleventh arguments of the battle call.</summary>
    public int Word76C { get => orders.EnemyTypes[0]; set => orders.EnemyTypes[0] = value; }

    public int Word76E { get => orders.EnemyTypes[1]; set => orders.EnemyTypes[1] = value; }

    public int Word770 { get => orders.EnemyTypes[2]; set => orders.EnemyTypes[2] = value; }
}

/// <summary>The personal-name tables of the game program (R029 <c>random_name</c> and <c>name_to_slot</c>).</summary>
public enum NameTable
{
    /// <summary>108 men's first names (R136+0xA9EF).</summary>
    MenFirstNames,

    /// <summary>88 women's first names (R136+0xABA7).</summary>
    WomenFirstNames,

    /// <summary>146 surnames (R136+0xAD0F).</summary>
    Surnames,

    /// <summary>16 merchant family names (R136+0xAF5B).</summary>
    MerchantFamilies,

    /// <summary>16 dragon names (R136+0xA637; <c>name_to_slot</c> kind 6).</summary>
    Dragons,

    /// <summary>38 dwarf names (R136+0xA677; kind 7).</summary>
    Dwarfs,

    /// <summary>31 merchant trades, houses and skill names (R136+0xA797; kind 8).</summary>
    Titles,
}

/// <summary>The game program's personal-name tables.</summary>
public interface IPersonalNames
{
    /// <summary>The string at <paramref name="index"/> of a table, "" when the table has no such entry.</summary>
    string Name(NameTable table, int index);

    /// <summary>The number of entries of a table (0 when none are loaded).</summary>
    int Count(NameTable table);
}

/// <summary>No name tables loaded: every name is empty.</summary>
public sealed class NoPersonalNames : IPersonalNames
{
    public static readonly NoPersonalNames Instance = new();

    public string Name(NameTable table, int index) => "";

    public int Count(NameTable table) => 0;
}

/// <summary>The name tables read from DARKLAND.EXE (see <c>PersonalNamesDecoder</c>).</summary>
// orig: far pointer tables R136+0xA9EF, R136+0xABA7, R136+0xAD0F, R136+0xAF5B, R136+0xA637, R136+0xA677, R136+0xA797 (record R136) read by random_name R029+0x5CC4 and name_to_slot R029+0x1FB8; tests: PersonalNamesTests
// DIVERGENCE: an index outside a table gives "" (the original reads whatever follows the table in memory).
public sealed class TablePersonalNames(Darklands.Content.Formats.Characters.PersonalNameData data) : IPersonalNames
{
    private IReadOnlyList<string> Table(NameTable table) => table switch
    {
        NameTable.MenFirstNames => data.People.MaleNames,
        NameTable.WomenFirstNames => data.People.FemaleNames,
        NameTable.Surnames => data.People.Surnames,
        NameTable.MerchantFamilies => data.People.PatricianSurnames,
        NameTable.Dragons => data.Dragons,
        NameTable.Dwarfs => data.Dwarfs,
        _ => data.Titles,
    };

    public string Name(NameTable table, int index)
    {
        var list = Table(table);
        if (index >= 0 && index < list.Count)
        {
            return list[index];
        }
        // C: name_to_slot reads its three tables unchecked: an index outside one reads the far pointer next to it
        // (index -1 of the dragons is the last fame title, of the dwarfs the last dragon), as long as it points into the data image.
        var start = table switch
        {
            NameTable.Dragons => Darklands.Content.Formats.Characters.PersonalNamesDecoder.DragonTable,
            NameTable.Dwarfs => Darklands.Content.Formats.Characters.PersonalNamesDecoder.DwarfTable,
            NameTable.Titles => Darklands.Content.Formats.Characters.PersonalNamesDecoder.TitleTable,
            _ => -1,
        };
        // DIVERGENCE: a pointer that leads outside the data image (an index far outside) gives "".
        return start >= 0 && data.PointedString is { } pointed && start + 4 * index is >= 0 and < 0x10000
            ? pointed(start + 4 * index) ?? "" : "";
    }

    public int Count(NameTable table) => Table(table).Count;
}

/// <summary>Helpers the card scripts of overlays R059..R073 share.</summary>
public sealed partial class CardContext
{
    /// <summary>A roll against a chance as the scripts make it: <c>rand_mod(100) &lt;= chance</c>.</summary>
    public bool Succeeds(int chance) => Random(100) <= chance;

    /// <summary>
    /// A member is gone for good (R037 <c>party_delete</c>): the member's term and "has left"
    /// events are cancelled, the slot is emptied, an able member takes over as leader if needed,
    /// and the party ends after its last occupied slot; the character leaves the roster
    /// (CHARACTR.TMP) for good.
    /// </summary>
    // orig: R037+0x3F30 party_delete R037+0x3F30 (1462:3F30) draft
    // DIVERGENCE: the shield picture is the party panel's; no able member left keeps the old leader slot.
    public void DeleteMember(int who)
    {
        foreach (var kind in new[] { Core.Events.EventKinds.MemberTerm, Core.Events.EventKinds.MemberLeft })
        {
            var slot = Events.Find(kind, who, -1, -1, -1, -1);
            if (slot != -1)
            {
                Events.Free(slot);
            }
        }
        var party = State.Party;
        if (who < 0 || who >= party.Members.Count)
        {
            return;
        }
        party.Members[who].Status = Core.Characters.CharacterStatus.Empty;
        // C: R037+0x4048(ids[p]) removes the character's record from charactr.tmp.
        State.Roster.Forget(party.Members[who]);
        if (party.LeaderIndex == who)
        {
            var able = party.Members.FindIndex(m => m.Status == Core.Characters.CharacterStatus.Able);
            if (able >= 0)
            {
                party.LeaderIndex = able;
            }
        }
        // The roster record (CHARACTR.TMP) goes through the presentation's hook (CardContext.Services.cs).
        RosterLeave?.Invoke(who);
        party.TrimToSize();
    }

    /// <summary>Starts a piece of music: the same as <see cref="PlayMusic"/> (one music hook, <see cref="Music"/>).</summary>
    // orig: R043+0x09AE (1462:09AE) (file 4/6 matched; this function not checked)
    public void PlaySong(int song) => PlayMusic(song);

    /// <summary>Plays a sound or tune directly (root <c>sound_play</c>, whatever the music option says).</summary>
    // orig: root 047C:078A sound_play (root 047C:078A) (resident code)
    public void PlaySound(int sound) => SoundPlayer?.Invoke(sound);

    /// <summary>The direct sound player, supplied by the presentation.</summary>
    public Action<int>? SoundPlayer { get; set; }

    // ---- things the original keeps in globals the game state does not model yet ---------

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Core.GameState, CardScriptMemory> MemoryOf = new();

    /// <summary>What the card scripts remember between visits (see <see cref="CardScriptMemory"/>).</summary>
    public CardScriptMemory Memory => MemoryOf.GetValue(State, s => new CardScriptMemory(s.BattleOrders));

    // ---- text input, names ------------------------------------------------------------

    /// <summary>The text box (R037 <c>text_input</c>: prompt, text to edit, longest length → the text entered), supplied by the presentation.</summary>
    public Func<string, string, int, string>? TextInput { get; set; }

    /// <summary>Asks the player to edit a short text; without a text box the text is taken as it stands (Enter).</summary>
    // orig: thunk 09C0:20B7 text_input (R037 1462:43E6) R037+0x43E6 draft
    public string AskText(string prompt, string initial, int maxLength) => TextInput?.Invoke(prompt, initial, maxLength) ?? initial;

    /// <summary>C's <c>atoi</c>: an optional sign and the leading digits, 0 when there are none.</summary>
    // C: MSC atoi works on 16-bit int: digits accumulate with wrap-around, kept here by the final (short).
    public static int ParseNumber(string text)
    {
        var s = text.TrimStart(' ', '\t');
        var i = 0;
        var negative = false;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            negative = s[i] == '-';
            i++;
        }
        var value = 0;
        for (; i < s.Length && char.IsAsciiDigit(s[i]); i++)
        {
            value = unchecked(value * 10 + (s[i] - '0'));
        }
        return (short)(negative ? -value : value);
    }

    /// <summary>The personal-name tables of DARKLAND.EXE, supplied by the content layer (until then names stay empty).</summary>
    public IPersonalNames PersonalNames { get; set; } = NoPersonalNames.Instance;

    /// <summary>
    /// A random personal name into $Named1..5 (R029 <c>random_name</c>): the generator is
    /// seeded with the party seed + <paramref name="seed"/> so the same person keeps the same
    /// name, kind 0..5 picks the tables (men's or women's first names, with a surname, a
    /// merchant family name or alone), and the generator is then reseeded from the clock.
    /// </summary>
    // orig: R029+0x5CC4 random_name R029+0x5CC4 (1367:0DB4) matched
    // C: srand takes an unsigned 16-bit seed; strcpy/strcat order draws the first name before the surname.
    // DIVERGENCE: srand(time(0)) at the end is the injected ClockSeed (no reseed when there is none); the name tables come from PersonalNames (TablePersonalNames over the EXE's tables; none loaded: empty names).
    public void RandomName(int seed, int kind, int slot)
    {
        var random = State.Random;
        random.Seed((ushort)(State.Party.Seed + seed));
        random.Scaled(1000);
        string Pick(NameTable table, int count) => PersonalNames.Name(table, random.Scaled(count));
        var name = kind switch
        {
            0 => Pick(NameTable.MenFirstNames, 0x6C) + " " + Pick(NameTable.Surnames, 0x92),
            1 => Pick(NameTable.WomenFirstNames, 0x58) + " " + Pick(NameTable.Surnames, 0x92),
            2 => Pick(NameTable.MenFirstNames, 0x6C) + " " + Pick(NameTable.MerchantFamilies, 0x10),
            3 => Pick(NameTable.WomenFirstNames, 0x58) + " " + Pick(NameTable.MerchantFamilies, 0x10),
            4 => Pick(NameTable.MenFirstNames, 0x6C),
            5 => Pick(NameTable.WomenFirstNames, 0x58),
            _ => slot >= 0 && slot < Variables.Named.Length ? Variables.Named[slot] : "",
        };
        // DIVERGENCE: a slot past $NamedFiveName ($Snuck00 asks for slot 0x27) makes the original write
        // beyond var_named; here the name is drawn but not stored.
        if (slot >= 0 && slot < Variables.Named.Length)
        {
            Variables.Named[slot] = name;
        }
        if (State.ClockSeed is { } clock)
        {
            random.Seed(clock());
        }
    }

    /// <summary>
    /// R030 27aa: the city nearest to a map position; a city standing exactly there is passed
    /// over while its third local value (record byte 0x17) is not 0.
    /// </summary>
    // orig: R030+0x27AA thunk 09C0:1FC7) (1462:27AA) draft (function matched per file header)
    // UNCERTAIN: record byte 0x17 (unk17) is read as Location.LocalValues[2] (bytes 0x15..0x17 are the three local values).
    public int NearestCityPassingOverOneHere(int x, int y)
    {
        var best = -1;
        var min = 9999;
        for (var i = 0; i < Core.World.Location.CityCount && i < State.Locations.Count; i++)
        {
            var p = State.Locations[i];
            if (p.X == x && p.Y == y && p.LocalValues[2] != 0 && p.Icon == 0)
            {
                continue;
            }
            var d = Core.Rules.LocationRules.Distance(p.X, p.Y, x, y);
            if (d < min)
            {
                min = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>The name of a location (its record's name), "" for none.</summary>
    public string LocationName(int loc) => loc >= 0 && loc < State.Locations.Count ? State.Locations[loc].Name : "";

    /// <summary>The full name of a saint (DARKLAND.LST), "" for none.</summary>
    public string SaintName(int saint) => saint >= 0 && saint < World.Items.Saints.Count ? World.Items.Saints[saint].FullName : "";

    /// <summary>
    /// The guild a guild card hands over to the next card ($Guild00, $Snuck00 ...): the original
    /// stores it in <c>party_x</c>, the party's map column (DS:E3D0), which is free while the
    /// party is in a city; it is kept in the same place here.
    /// </summary>
    // orig: party_x DS:E3D0 written by the guild cards (e.g. R062+0x217A) and read by the cards they lead to
    public int GuildCode
    {
        get => State.MapPosition.X;
        set => State.MapPosition = State.MapPosition with { X = value };
    }

    /// <summary>
    /// The guild's politics fact a card hands over with <see cref="GuildCode"/>: the original keeps
    /// it in <c>party_y</c>, the party's map row (DS:E3D2).
    /// </summary>
    // orig: party_y DS:E3D2 read as a location-fact type by $Polit00 (R070+0x2D1A)
    // UNCERTAIN: which card sets it before $Polit00 is not established.
    public int GuildFact
    {
        get => State.MapPosition.Y;
        set => State.MapPosition = State.MapPosition with { Y = value };
    }

    /// <summary>$Support .. $Support6 (<c>var_support0..6</c>, DS:ED5E): a side −1/0/1 per text (the same array as <see cref="CardVariables.Support"/>, which <see cref="TextVariables"/> reads).</summary>
    // orig: var_support0..var_support6 R043+0x1316 case 59..65: R136+0xB1EB[var_support] with R136+0xB1EB = the middle entry of the three-string table at 290E:2B57, so −1 oppose, 0 are undecided about, 1 support
    public int[] Support => Variables.Support;

    // ShopNames is shared: see CardContext.Services.cs.

    /// <summary>
    /// Opens a shop (R029 21aa): the barter screen for shop <paramref name="shop"/> under its
    /// name (or <paramref name="name"/>), with the item categories <paramref name="low"/> |
    /// <paramref name="high"/> &lt;&lt; 16. The original then reloads the card with its main
    /// picture (thunk 09C0:1EA5): callers show that picture again.
    /// </summary>
    // orig: R029+0x21AA (0E76:21AA) draft
    // C: the (x, y) words go to the barter screen as one long category mask, low word first.
    public void OpenShop(int shop, string? name, int low, int high)
    {
        Screens.Barter(shop, name ?? (shop >= 0 && shop < ShopNames.Length ? ShopNames[shop] : ""), (uint)(ushort)low | (long)(ushort)high << 16);
    }

    /// <summary>The card that was shown before this one (<c>prev_screen</c>).</summary>
    public int PreviousCard => State.PreviousCard;

    /// <summary>The card a script remembered to return to (<c>saved_card</c>).</summary>
    public int SavedCard
    {
        get => State.SavedCard;
        set => State.SavedCard = value;
    }
}

/// <summary>Words of DGROUP that a single card keeps between its visits.</summary>
public sealed partial class CardContext
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Core.GameState, Dictionary<int, int>> DataWordsOf = new();

    /// <summary>
    /// A word of the data segment (<c>D_xxxx</c>) that only one card uses and never resets, so it
    /// keeps its value for later visits; 0 until written (the words used so far start as 0 in the
    /// program image). Not saved with the game, like the original.
    /// </summary>
    // orig: DGROUP words such as DS:5DF8/DS:5DFA of
    public int DataWord(int address) => DataWordsOf.GetValue(State, _ => []).GetValueOrDefault(address);

    /// <summary>Writes a word read back by <see cref="DataWord"/>.</summary>
    public void SetDataWord(int address, int value) => DataWordsOf.GetValue(State, _ => [])[address] = (short)value;
}
