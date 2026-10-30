using Darklands.Core.Items;
using Darklands.Core.Randomness;

namespace Darklands.Core.Characters.Creation;

/// <summary>The steps of the character generator (the original's screen state).</summary>
public enum CreationStage
{
    /// <summary>Name, nickname and sex; then "Begin childhood".</summary>
    NameAndSex = 0,

    /// <summary>The list of family backgrounds.</summary>
    FamilyBackground = 1,

    /// <summary>Experience points of the childhood spent on attributes.</summary>
    ChildhoodAttributes = 2,

    /// <summary>The list of the occupations open to the character.</summary>
    Occupation = 3,

    /// <summary>Experience points of the term spent on skills.</summary>
    OccupationSkills = 4,
}

/// <summary>What a generator command asks the screen to do next.</summary>
public enum CreationOutcome
{
    /// <summary>Nothing changed.</summary>
    None,

    /// <summary>The values changed; redraw the panels.</summary>
    Changed,

    /// <summary>A new step: redraw the whole screen.</summary>
    NewStage,

    /// <summary>The player wants to type a new name.</summary>
    EnterName,

    /// <summary>The player wants to type a nickname.</summary>
    EnterNickname,

    /// <summary>The character is finished (<see cref="CharacterCreation.Finished"/>).</summary>
    Finished,

    /// <summary>The player left the generator without a character.</summary>
    Left,
}

/// <summary>
/// The character generator (overlay R037, <c>character-creation-entry</c>): a person's name and sex,
/// a family background (15 years of childhood with experience points for attributes), then
/// 5-year occupations (experience points for skills, aging, items, saints and formulas) until
/// the player begins adventuring. Commands are numbered like the original's key handler:
/// 1..15 are the rows of the left-hand list (the letter keys give rows 1..6).
/// </summary>
// orig: character-creation-entry = R037+0x4664 (18C8:0004) matched (main loop, Esc); R037+0x4BEE (18C8:058E) draft, same length (key handler); tests: CharacterCreationTests
// C: the character lives in DS globals (DS:E80A.. record, DS:8BFA/DS:8C48 points, DS:8BD2 list, DS:8BFC cursor); the class holds them.
public sealed class CharacterCreation
{
    public const int OccupationCount = 37;
    public const int ChildhoodYears = 15;
    public const int TermYears = 5;
    public const int MaxNameLength = 23;
    public const int MaxNicknameLength = 9;

    /// <summary>Starting attributes (Endurance..Charisma) of a man and of a woman.</summary>
    private static readonly int[] ManAttributes = [12, 15, 12, 12, 12, 11];
    private static readonly int[] WomanAttributes = [14, 12, 12, 12, 12, 12];

    /// <summary>The weapon a new adventurer gets for the best of the seven weapon skills.</summary>
    private static readonly int[] WeaponForSkill = [5, 0x0F, 0x11, 0x15, 1, 0x1C, 0x1E];

    private readonly CreationTables _tables;
    private readonly IReadOnlyList<ItemDefinition> _items;
    private readonly IRandomSource _random;
    private readonly int _partySeed;
    private readonly Func<uint> _clock;
    private readonly bool[] _taken = new bool[OccupationCount];

    /// <param name="random">The game's generator (<c>rand</c>); names reseed it.</param>
    /// <param name="partySeed">The party seed names are drawn from.</param>
    /// <param name="clock">The clock (<c>time(0)</c>): name seeds and the reseed after a name.</param>
    public CharacterCreation(CreationTables tables, IReadOnlyList<ItemDefinition> items, IRandomSource random, int partySeed, Func<uint> clock)
    {
        _tables = tables;
        _items = items;
        _random = random;
        _partySeed = partySeed;
        _clock = clock;
        // orig: the screen draws the first name when the generator opens (R037+0x675A), not when the party screen starts
        Reset(drawName: false);
    }

    public CreationTables Tables => _tables;

    /// <summary>The generator the creation draws from (tests read its state).</summary>
    internal IRandomSource Random => _random;

    public CreationStage Stage { get; private set; }

    public string Name { get; private set; } = "";

    public string Nickname { get; private set; } = "";

    public bool IsFemale { get; internal set; }

    /// <summary>The name was drawn at random (a change of sex then draws a new one).</summary>
    public bool NameIsRandom { get; private set; }

    /// <summary>Age in years (0 before childhood).</summary>
    public int Age { get; internal set; }

    /// <summary>Endurance..Charisma and Divine Favor (99).</summary>
    public int[] Attributes { get; } = new int[Character.AttributeCount];

    public int[] Skills { get; } = new int[Character.SkillCount];

    /// <summary>The family background chosen (−1 before childhood).</summary>
    public int Background { get; internal set; }

    /// <summary>The last, the one before and the third-last occupation (−1: none).</summary>
    public int LastOccupation { get; internal set; }

    public int PreviousOccupation { get; internal set; }

    public int ThirdLastOccupation { get; internal set; }

    /// <summary>Experience points left in this term, and those the term gave.</summary>
    public int Experience { get; internal set; }

    public int TermExperience { get; internal set; }

    /// <summary>Points spent this step on each attribute (childhood) and each skill (occupation).</summary>
    public int[] AttributePointsSpent { get; } = new int[6];

    public int[] SkillPointsSpent { get; } = new int[Character.SkillCount];

    /// <summary>The rows of the left-hand list: background or occupation numbers, −1 for none.</summary>
    public IReadOnlyList<int> Choices => _choices;

    private int[] _choices = [];

    /// <summary>The selected attribute row (childhood) or skill row (occupation, 0..20 with gaps at 7 and 14).</summary>
    public int Cursor { get; internal set; }

    /// <summary>Saints learned (game numbering).</summary>
    public SortedSet<int> Saints { get; } = [];

    /// <summary>Per potion, the known-formula bits (1, 2, 4 = the three grades); terms add one to a byte.</summary>
    public byte[] FormulaBytes { get; } = new byte[Potions.Count];

    /// <summary>The items of the last occupation (each adult term replaces them).</summary>
    public List<ItemStack> Kit { get; } = [];

    /// <summary>The character made by "Begin adventuring".</summary>
    public Character? Finished { get; private set; }

    public bool HasBeen(int occupation) => _taken[occupation];

    /// <summary>Marks an occupation as held before (tests set up characters with it).</summary>
    internal void SetTaken(int occupation, bool taken) => _taken[occupation] = taken;

    /// <summary>Starts again, drawing the first name.</summary>
    public void Reset() => Reset(drawName: true);

    /// <summary>Starts again (<c>R037+0x675A</c>): a random man's name, age 0, base attributes, nothing learned.</summary>
    // orig: R037+0x675A (18C8:20FA) matched; tests: CharacterCreationTests
    // C: the record bytes the reset also writes that the clean model does not read are kept in the made character's record (ResetRecord, under the
    // Character fields CharacterRecords.Write puts over it): +0x04 = 0x110, +0x06 = 0x50, +0x18 = -1, +0x4A = -1, +0x49 = 10, the shield letter
    // 'A', the equipment types -1 and qualities 0 (nothing equipped); +0x0E, +0x16 and +0x19..+0x1C = 0 are the zeros of a new record.
    // Tests: CharacterCreationTests.TheMadeCharacterKeepsTheResetRecordsBytes, OriginalSaveTests.AMadeCharacterIsWrittenWithTheResetRecordsBytes
    // C: the cursor DS:8BFC is not reset: after "Kill character" it keeps the last skill row (see Raise).
    private void Reset(bool drawName)
    {
        Background = -1;
        ThirdLastOccupation = -1;
        PreviousOccupation = -1;
        LastOccupation = -1;
        if (drawName)
        {
            NewRandomName(NameKind.Man);
        }

        Age = 0;
        IsFemale = false;
        ManAttributes.CopyTo(Attributes, 0);
        Attributes[(int)Attribute.DivineFavor] = 99;
        Array.Clear(Skills);
        Saints.Clear();
        Array.Clear(FormulaBytes);
        Kit.Clear();
        _choices = [0, 1, 2, 3, 4, 5];
        Array.Clear(_taken);
        Array.Clear(SkillPointsSpent);
        Array.Clear(AttributePointsSpent);
        Finished = null;
        Stage = CreationStage.NameAndSex;
    }

    /// <summary>Draws a new random name for the character's sex.</summary>
    public void NewRandomName() => NewRandomName(IsFemale ? NameKind.Woman : NameKind.Man);

    // orig: made-up-name(time(0), sex, 0) in R037+0x675A and R037+0x4BEE cases 2 and 3
    // C: time(0) is passed as an int (its low 16 bits); made-up-name reads the clock again to reseed.
    // DIVERGENCE: the two reads are one here (the same second: the seed is party seed + time and the reseed is that time again, see lockstep roster-create), so a scripted clock gives one value a name.
    private void NewRandomName(NameKind kind)
    {
        NameIsRandom = true;
        var now = _clock();
        Name = RandomNames.Make(_random, _tables.Names, _partySeed, (short)now, kind, now);
        Nickname = RandomNames.Nickname(Name);
        Keep(s_nameBuffer, Name, MaxNameLength);
        Keep(s_nickBuffer, Nickname, MaxNicknameLength);
    }

    // orig: R037+0x4BEE case 4 (E, "Enter a new name"): text-prompt of at most 23 letters; an Esc leaves the name empty
    public void SetName(string name)
    {
        NameIsRandom = false;
        Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name;
        Keep(s_nameBuffer, Name, MaxNameLength);
    }

    // orig: R037+0x4BEE case 5 (C, "Create a nickname"): text-prompt of at most 9 letters
    public void SetNickname(string nickname)
    {
        NameIsRandom = false;
        Nickname = nickname.Length > MaxNicknameLength ? nickname[..MaxNicknameLength] : nickname;
        Keep(s_nickBuffer, Nickname, MaxNicknameLength);
    }

    /// <summary>"Make him a woman" / "Make her a man": base attributes of the new sex, a new random name if it was random.</summary>
    // orig: R037+0x4BEE case 2 in state 0 (M); tests: CharacterCreationTests
    // C: the base attributes overwrite the current ones (only state 0 offers it, before any term).
    public void ToggleSex()
    {
        IsFemale = !IsFemale;
        (IsFemale ? WomanAttributes : ManAttributes).CopyTo(Attributes, 0);
        if (NameIsRandom)
        {
            NewRandomName();
        }
    }

    /// <summary>
    /// A numbered command (<c>R037+0x4BEE</c> cases 1..15): list row
    /// <paramref name="row"/> + 1, or the menu entry of the stage.
    /// </summary>
    // orig: R037+0x4BEE cases 1..15 (by state DS:8C1E); tests: CharacterCreationTests
    // C: the letter keys reach the same cases: B/G 1, M 2, S 3, E 4, C 5, R 6 (the screen maps them, and N/B/K to 1/2/3
    // in state 4 first), so in the lists the letters pick rows 1..6 and in state 4 M begins the adventure and S restarts.
    // DORMANT: state 2 answers only command 1; cases 7..15 only state 3; other states ignore the rest.
    public CreationOutcome Choose(int command)
    {
        switch (Stage)
        {
            case CreationStage.NameAndSex:
                return command switch
                {
                    1 => Enter(CreationStage.FamilyBackground),
                    2 => Do(ToggleSex),
                    3 => Do(NewRandomName),
                    4 => CreationOutcome.EnterName,
                    5 => CreationOutcome.EnterNickname,
                    6 => CreationOutcome.Left,
                    _ => CreationOutcome.None,
                };
            case CreationStage.FamilyBackground:
            case CreationStage.Occupation:
                return ChooseRow(command - 1);
            case CreationStage.ChildhoodAttributes:
                return command == 1 ? EnterOccupationList() : CreationOutcome.None;
            case CreationStage.OccupationSkills:
                return command switch
                {
                    1 => NextOccupation(),
                    2 => BeginAdventuring(),
                    3 => Restart(),
                    _ => CreationOutcome.None,
                };
        }
        return CreationOutcome.None;
    }

    /// <summary>Escape: from spending points back to the list (points stay spent), otherwise leave.</summary>
    // orig: character-creation-entry (R037+0x4664) on 0x011B: state 2 -> 1, 4 -> 3 and redraw, otherwise leave
    // C: the Esc code then also goes to R037+0x4BEE, which has no case for it.
    // BUG: Esc from the childhood attributes goes back to the background list at age 15; a background row then
    // serves the adult occupation of that number (Recruit..Knight), as in the original.
    public CreationOutcome Back()
    {
        switch (Stage)
        {
            case CreationStage.ChildhoodAttributes:
                Stage = CreationStage.FamilyBackground;
                return CreationOutcome.NewStage;
            case CreationStage.OccupationSkills:
                return EnterOccupationList();
            default:
                return CreationOutcome.Left;
        }
    }

    // orig: R037+0x4BEE cases 1..6 (states 1/3: DS:8C02 = row when younger than 15) and 7..15 (state 3 only)
    // C: the original serves DS:8BD2[row] where row is fixed by the case, so key and click agree.
    private CreationOutcome ChooseRow(int row)
    {
        if (row < 0 || row >= _choices.Length || _choices[row] == -1)
        {
            return CreationOutcome.None;
        }
        // Rows past the sixth are only offered in the occupation list (no childhood test there).
        if (row < 6 && Age < ChildhoodYears)
        {
            Background = row;
            Stage = CreationStage.ChildhoodAttributes;
        }
        else
        {
            Stage = CreationStage.OccupationSkills;
        }
        ServeTerm(_choices[row]);
        return CreationOutcome.NewStage;
    }

    private CreationOutcome EnterOccupationList()
    {
        Stage = CreationStage.Occupation;
        RefreshChoices();
        return CreationOutcome.NewStage;
    }

    /// <summary>"Go to next occupation": the points of this term are given up.</summary>
    // orig: R037+0x4BEE case 1 in state 4 (G/N): the spent counters DS:8C22/DS:8C06 are cleared, state 3
    private CreationOutcome NextOccupation()
    {
        Array.Clear(SkillPointsSpent);
        Array.Clear(AttributePointsSpent);
        return EnterOccupationList();
    }

    /// <summary>"Kill character": a fresh start.</summary>
    // orig: R037+0x4BEE case 3 in state 4 (K/S): R037+0x675A, state 0
    private CreationOutcome Restart()
    {
        Reset();
        return CreationOutcome.NewStage;
    }

    private CreationOutcome Enter(CreationStage stage)
    {
        Stage = stage;
        return CreationOutcome.NewStage;
    }

    private static CreationOutcome Do(Action action)
    {
        action();
        return CreationOutcome.Changed;
    }

    /// <summary>Rebuilds the occupation list (and puts the cursor on the first row), as every draw of the list does.</summary>
    // orig: R037+0x573A state 3 calls R037+0x691E on every draw of the list
    public void RefreshChoices()
    {
        Cursor = 0;
        var available = OccupationEligibility.Available(this);
        _choices = [.. available];
    }

    /// <summary>
    /// One term (<c>R037+0x7BCC</c>): the background's (before 15) or the occupation's
    /// attribute and skill changes (after 40 only Agility..Charisma change; the first adult term
    /// gives +2 to every skill), the occupation's events, the term's experience points
    /// (+20 for the first adult term, +5 for the second), the years, and from age 30 the aging
    /// penalties.
    /// </summary>
    // orig: R037+0x7BCC (18C8:356C) draft (w in SI instead of BX: no behaviour); Oracle:chargen.json; tests: CharacterCreationTests
    // C: attributes and skills are signed chars; the sums stay below 128 (99 + 6 + 2), so nothing wraps.
    // C: "Age < 19" is DS:E81C < 0x13 (only the term served at 15); the +20 and +5 test the age before the term.
    public void ServeTerm(int index)
    {
        var childhood = Age < ChildhoodYears;
        var entry = childhood ? _tables.Backgrounds[index] : _tables.Occupations[index];
        for (var i = 0; i < 6; i++)
        {
            if (childhood || Age <= 40 || i >= 2)
            {
                Attributes[i] += entry.AttributeChanges[i];
            }
            Attributes[i] = Math.Clamp(Attributes[i], 1, 99);
        }
        for (var i = 0; i < Character.SkillCount; i++)
        {
            Skills[i] += entry.SkillChanges[i] + (!childhood && Age < 19 ? 2 : 0);
            Skills[i] = Math.Clamp(Skills[i], 0, 99);
        }
        if (!childhood)
        {
            ThirdLastOccupation = PreviousOccupation;
            PreviousOccupation = LastOccupation;
            LastOccupation = index;
            _taken[index] = true;
            CareerEvents(index);
        }
        var experience = entry.Experience + (Age == 15 ? 20 : 0) + (Age == 20 ? 5 : 0);
        TermExperience = experience;
        Age += childhood ? ChildhoodYears : TermYears;
        Experience = experience;
        if (Age >= 30)
        {
            var penalty = _tables.AgingPenalties[Math.Clamp((Age - 30) / 5, 0, 8)];
            for (var i = 0; i < 6; i++)
            {
                Attributes[i] += penalty[i];
            }
        }
        for (var i = 0; i < 6; i++)
        {
            Attributes[i] = Math.Clamp(Attributes[i], 1, 99);
        }
    }

    /// <summary>
    /// What an occupation brings besides its tables (<c>R037+0x7D8A</c>): the kit is
    /// replaced (soldiers, nobles, craftsmen ... get items 0x26..0x33), clerics learn one or
    /// two random saints, alchemists and scholars one or two formulas.
    /// </summary>
    // orig: R037+0x7D8A (18C8:372A) draft (jump-table layout only); Oracle:chargen.json; tests: CharacterCreationTests
    // C: occupations without a case (13, 19, 21..23, 30..32, 34) leave the kit empty after it is cleared.
    // C: occupations 9 and 15 draw the saint b first, then a (two statements, no argument-order question).
    internal void CareerEvents(int occupation)
    {
        Kit.Clear();
        switch (occupation)
        {
            case 0: AddItems(0x28, 0x2F); break;
            case 1: AddItems(0x2A, 0x30); break;
            case 2: AddItems(0x2B, 0x31); break;
            case 3: AddItems(0x2B, 0x30); break;
            case 4: AddItems(0x27, 0x2F); break;
            case 5: AddItems(0x2B, 0x33); break;
            case 6 or 0x10 or 0x11: AddItems(0x26); break;
            case 7 or 0x12: AddItems(0x2A, 0x2F); break;
            case 8 or 0x0A or 0x0B or 0x0E: LearnRandomSaint(); break;
            case 9 or 0x0F: LearnRandomSaint(); LearnRandomSaint(); break;
            case 0x0C or 0x18 or 0x19: LearnRandomFormula(); break;
            case 0x14: AddItems(0x28, 0x30); break;
            case 0x1A: LearnRandomFormula(); LearnRandomFormula(); break;
            case 0x1B or 0x1C or 0x1D: AddItems(0x26); break;
            case 0x21: AddItems(0x26, 0x2F); break;
            case 0x23 or 0x24: AddItems(0x27, 0x2F); break;
        }
    }

    private void AddItems(params int[] codes)
    {
        foreach (var code in codes)
        {
            AddItem(code);
        }
    }

    /// <summary>
    /// A saint from the first 136 (<c>rnd(0x88)</c>). The generator sets bit <c>1 &lt;&lt; (n % 8)</c>
    /// while the game reads bit <c>0x80 &gt;&gt; (n % 8)</c>, so the saint learned is n mirrored within its byte.
    /// </summary>
    // orig: a = roll-below(0x88); R136+0x1144A[a / 8] |= 1 << a % 8 (R037+0x7D8A); the game's member-flag-bit reads 0x80 >> (bit & 7)
    // C: roll-below = root 0000:0C70, rand() % n.
    private void LearnRandomSaint()
    {
        var n = _random.Below(0x88);
        Saints.Add((n & ~7) | (7 - (n & 7)));
    }

    /// <summary>
    /// One more formula: the potion's byte of known grades is incremented (0 → grade 1 known,
    /// 1 → grade 2 only, 2 → grades 1 and 2, ...), as in the original.
    /// </summary>
    // orig: R136+0x11434[roll-below(0x16)]++ (R037+0x7D8A)
    // C: a byte increment, not a bit set: a second formula of the same potion replaces grade 1 by grade 2.
    private void LearnRandomFormula() => FormulaBytes[_random.Below(Potions.Count)]++;

    /// <summary>Adds one item to the kit (<c>R037+0x80FE</c>): a stack of the same item, type and quality grows, at most 64 stacks.</summary>
    // orig: R037+0x80FE (18C8:3A9E) matched; tests: CharacterCreationTests
    // C: the entry is (id, item-definition-table[id]->f1e type, f26 quality, count 1, f25 weight); stacks compare id, type and quality.
    private void AddItem(int code)
    {
        var definition = code < _items.Count ? _items[code] : null;
        var type = (byte)(definition?.Type ?? 0);
        var quality = (byte)(definition?.Quality ?? 0);
        var weight = (byte)(definition?.Weight ?? 0);
        var same = Kit.FindIndex(s => s.Code == code && s.Type == type && s.Quality == quality);
        if (same >= 0)
        {
            Kit[same] = Kit[same] with { Count = Kit[same].Count + 1 };
        }
        else if (Kit.Count < Character.PackSize)
        {
            Kit.Add(new ItemStack(code, type, quality, 1, weight));
        }
    }

    // ----- experience points -----

    /// <summary>The skill a skill row stands for (rows 7 and 14 are gaps).</summary>
    // orig: R037+0x7EF0 (18C8:3890) matched
    public static int SkillOfRow(int row)
    {
        if (row >= 14)
        {
            row--;
        }
        if (row >= 8)
        {
            row--;
        }
        return row;
    }

    /// <summary>Arrow up (−1) or down (+1) over the attribute or skill rows.</summary>
    // orig: R037+0x4BEE cases 0x4800/0x4B00/0x4D00 (up, left, right: -1) and 0x5000 (down: +1)
    // DORMANT: the arrows do nothing in states 0, 1 and 3.
    public void MoveCursor(int step)
    {
        if (Stage == CreationStage.ChildhoodAttributes)
        {
            Cursor += step;
            if (step < 0 && (Cursor < 0 || Cursor >= 6))
            {
                Cursor = 5;
            }
            else if (step > 0 && Cursor >= 6)
            {
                Cursor = 0;
            }
        }
        else if (Stage == CreationStage.OccupationSkills)
        {
            Cursor += step;
            if (Cursor is 7 or 14)
            {
                Cursor += step;
            }
            if (Cursor < 0)
            {
                Cursor = 20;
            }
            else if (Cursor > 20)
            {
                Cursor = 0;
            }
        }
    }

    /// <summary>Puts the cursor on a row the player clicked.</summary>
    public void SelectRow(int row) => Cursor = row;

    /// <summary>Points one more point of an attribute costs: 1, 2 from 29, 3 from 39.</summary>
    public static int AttributeCost(int value) => value >= 39 ? 3 : value >= 29 ? 2 : 1;

    /// <summary>Points one more point of a skill costs: 1, 2 from 49, 3 from 79.</summary>
    public static int SkillCost(int value) => value >= 79 ? 3 : value >= 49 ? 2 : 1;

    /// <summary>'+': one point more for the row under the cursor, if the points, the limits and the occupation allow.</summary>
    // orig: R037+0x4BEE cases 0x0D2B/0x0D3D/0x4E2B ('+'); tests: CharacterCreationTests
    // C: the cost is taken from the value before the raise; an attribute stops at 40 (and 40 points), a skill at 89
    // and at the occupation's limit for that skill.
    // C: in the childhood attributes the cursor indexes DS:8C06[c] and DS:E86E[c] unchecked; after "Kill character" it keeps the skill row
    // C: it had (DS:8BFC is reset only by the profession list), so c can be 6, 8..13 or 15..20: see SpentWord and ByteAt.
    public bool Raise()
    {
        if (Stage == CreationStage.ChildhoodAttributes)
        {
            var c = Cursor;
            var value = ByteAt(c);
            var cost = AttributeCost(value);
            if (Experience - cost < 0 || SpentWord(c) >= 40 || value >= 40)
            {
                return false;
            }
            SetSpentWord(c, SpentWord(c) + 1);
            SetByteAt(c, value + 1);
            Experience -= cost;
            return true;
        }
        if (Stage == CreationStage.OccupationSkills)
        {
            var s = SkillOfRow(Cursor);
            var cost = SkillCost(Skills[s]);
            if (Experience - cost < 0 || _tables.Occupations[LastOccupation].SkillLimits[s] <= SkillPointsSpent[s] || Skills[s] >= 89)
            {
                return false;
            }
            SkillPointsSpent[s]++;
            Skills[s]++;
            Experience -= cost;
            return true;
        }
        return false;
    }

    /// <summary>'−': takes back a point spent in this step.</summary>
    // orig: R037+0x4BEE cases 0x0C2D/0x4A2D ('-'): the refund is taken from the value before the change
    // (40+: 3, 30+: 2 for attributes; 80+: 3, 50+: 2 for skills), only while points of this step are spent.
    // C: a stale cursor indexes past the six attributes as in Raise.
    public bool Lower()
    {
        if (Stage == CreationStage.ChildhoodAttributes)
        {
            var c = Cursor;
            if (Experience >= TermExperience || SpentWord(c) == 0)
            {
                return false;
            }
            var value = ByteAt(c);
            Experience += value >= 40 ? 3 : value >= 30 ? 2 : 1;
            SetSpentWord(c, SpentWord(c) - 1);
            SetByteAt(c, value - 1);
            return true;
        }
        if (Stage == CreationStage.OccupationSkills)
        {
            var s = SkillOfRow(Cursor);
            if (Experience >= TermExperience || SkillPointsSpent[s] == 0)
            {
                return false;
            }
            Experience += Skills[s] >= 80 ? 3 : Skills[s] >= 50 ? 2 : 1;
            SkillPointsSpent[s]--;
            Skills[s]--;
            return true;
        }
        return false;
    }

    /// <summary>
    /// <c>DS:8C06[c]</c>: the points spent on attribute c for c &lt; 6; past the array the words that follow it in
    /// DGROUP: 8C12 the third-last occupation, 8C14..8C1C and 8C20 the handles of the bubble pictures, 8C1E the
    /// screen state, 8C22.. the points spent on each skill.
    /// </summary>
    // orig: DS:8C06 (int[6]), DS:8C12, DS:8C14[5], DS:8C1E, DS:8C20, DS:8C22[0x13] (R037 seg 18C8 externs; image-read at 18C8:0277..0282)
    // C: a picture handle is the DOS segment image-read returned, taken as at least 0x28 (so '+' stops there) and kept as a counter here.
    private int SpentWord(int c) => c switch
    {
        < 6 => AttributePointsSpent[c],
        6 => ThirdLastOccupation,
        12 => (int)Stage,
        >= 14 and < 14 + Character.SkillCount => SkillPointsSpent[c - 14],
        _ => _pictureHandles[c is 13 ? 5 : c - 7],
    };

    private void SetSpentWord(int c, int value)
    {
        switch (c)
        {
            case < 6:
                AttributePointsSpent[c] = value;
                break;
            case 6:
                ThirdLastOccupation = value;
                break;
            case 12:
                // C: DS:8C1E is the screen state: '+' on this row moves the screen on to state 3 (the list keeps the rows it has), '-' back to 1.
                Stage = (CreationStage)value;
                break;
            case >= 14 and < 14 + Character.SkillCount:
                SkillPointsSpent[c - 14] = value;
                break;
            default:
                _pictureHandles[c is 13 ? 5 : c - 7] = value;
                break;
        }
    }

    /// <summary>The bubble pictures' handles DS:8C14..DS:8C1C and DS:8C20 as words the stale cursor can change (see <see cref="SpentWord"/>).</summary>
    private readonly int[] _pictureHandles = [0x1000, 0x1000, 0x1000, 0x1000, 0x1000, 0x1000];

    /// <summary><c>DS:E86E[c]</c>: the seven attributes, then (<c>DS:E875</c>) the skills, signed chars.</summary>
    private int ByteAt(int c) => c < Character.AttributeCount ? Attributes[c] : Skills[c - Character.AttributeCount];

    private void SetByteAt(int c, int value)
    {
        if (c < Character.AttributeCount)
        {
            Attributes[c] = (sbyte)value;
        }
        else
        {
            Skills[c - Character.AttributeCount] = (sbyte)value;
        }
    }

    /// <summary>How much of the term's points is spent, 0..50 (the bubble empties from the top).</summary>
    // orig: character-creation-entry (R037+0x4664): DS:43F8 = (DS:8BFA * -100 / DS:8C48 + 100) / 2 in states 2 and 4, else 0
    // C: 16-bit signed division truncating toward zero, as C#'s.
    public int SpentGauge =>
        Stage is CreationStage.ChildhoodAttributes or CreationStage.OccupationSkills && TermExperience != 0
            ? (Experience * -100 / TermExperience + 100) / 2
            : 0;

    // ----- what the right-hand panel shows -----

    /// <summary>The background or occupation the right-hand panel describes for a hovered list row.</summary>
    // orig: R037+0x5D98 (18C8:1738) matched: prn = states 1/3 ? DS:8BD2[DS:43EE] : state 2 ? DS:8C02 : DS:4428, -1 -> 0
    // C: with no row under the mouse (DS:43EE = -1, its initial value) the list's word before it is read: DS:8BD2[-1] = DS:8BD0, the last word
    // C: of the record buffer DS:8B52 (RecordBufferWord); the entry it names may lie past the tables (BackgroundEntry, OccupationEntry).
    public int ShownEntry(int hoveredRow)
    {
        var entry = Stage switch
        {
            CreationStage.FamilyBackground or CreationStage.Occupation =>
                hoveredRow == -1 ? RecordBufferWord : hoveredRow >= 0 && hoveredRow < _choices.Length ? _choices[hoveredRow] : -1,
            CreationStage.ChildhoodAttributes => Background,
            _ => LastOccupation,
        };
        return entry == -1 ? 0 : entry;
    }

    /// <summary>
    /// The word at DS:8BD0 (<c>DS:8BD2[-1]</c>): the last word of the 0x80-byte record buffer <c>DS:8B52</c>, which the
    /// "Create a new party" list fills with each roster record it draws: record +0x7E (the item count) and +0x7F of the
    /// last character listed; 0 until a list with a character was drawn. The screen that runs the generator sets it.
    /// </summary>
    // orig: DS:8B52 (struct chr, 0x80 bytes) filled by R037+0x2760 (1462:2760) matched
    public int RecordBufferWord { get; set; }

    /// <summary>Background entry <paramref name="b"/> as the panel reads it: past the six, the occupations' table that follows.</summary>
    private Occupation BackgroundEntry(int b) => b is >= 0 and < 6 ? _tables.Backgrounds[b] : OccupationEntry(b - 6);

    /// <summary>Occupation entry <paramref name="k"/> as the panel reads it: past the 37, the bytes after the table (CreationTables.EntryAt).</summary>
    private Occupation OccupationEntry(int k) =>
        k >= 0 && k < _tables.Occupations.Count ? _tables.Occupations[k]
        : _tables.EntryAt?.Invoke(k) ?? new Occupation(k, "", 0, new int[6], new int[Character.SkillCount], new int[Character.SkillCount], 0);

    /// <summary>The "EP" number: the points the hovered entry would give, or the points left.</summary>
    // orig: R037+0x5D98, the "EP" field (%02d)
    public int? ShownExperience(int entry) => Stage switch
    {
        CreationStage.NameAndSex => null,
        CreationStage.FamilyBackground => BackgroundEntry(entry).Experience,
        CreationStage.Occupation => OccupationEntry(entry).Experience + (Age >= 25 ? 0 : Age >= 20 ? 5 : 20),
        _ => Experience,
    };

    /// <summary>An attribute as the panel shows it: with the hovered entry's change and the aging to come while choosing.</summary>
    // orig: R037+0x5D98, the attribute column: agk = (int)(age - 25) / 5, below 0 -> 0
    // C: the preview is not clamped to 1..99.
    public int ShownAttribute(int entry, int attribute)
    {
        if (Stage is CreationStage.ChildhoodAttributes or CreationStage.OccupationSkills)
        {
            return Attributes[attribute];
        }
        if (Age < ChildhoodYears)
        {
            return Attributes[attribute] + BackgroundEntry(entry).AttributeChanges[attribute];
        }
        // C: agk = (int)(age - 25) / 5, below 0 -> 0, not limited above: from age 70 on the row is past the nine of the table (the bytes
        // C: after it, CreationTables.AgingPenalties); the rows read reach age 225.
        var aging = _tables.AgingPenalties[Math.Clamp((Age - 25) / 5, 0, _tables.AgingPenalties.Count - 1)][attribute];
        return Age <= 40 || attribute >= 2
            ? Attributes[attribute] + aging + OccupationEntry(entry).AttributeChanges[attribute]
            : Attributes[attribute] + aging;
    }

    /// <summary>The "change:limit" column of a skill ("0:00" before childhood).</summary>
    // orig: R037+0x5D98, the "%2d:%02d" skill columns (change and points left for the skill)
    public string ShownSkillChange(int entry, int skill)
    {
        var limitLeft = OccupationEntry(entry).SkillLimits[skill] - SkillPointsSpent[skill];
        if (Age >= 25 - (Stage == CreationStage.Occupation ? 5 : 0) && Stage != CreationStage.ChildhoodAttributes)
        {
            return $"{OccupationEntry(entry).SkillChanges[skill],2}:{limitLeft:00}";
        }
        if (Age >= ChildhoodYears && Stage != CreationStage.ChildhoodAttributes)
        {
            return $"{OccupationEntry(entry).SkillChanges[skill] + 2,2}:{limitLeft:00}";
        }
        return Stage == CreationStage.NameAndSex ? "0:00" : $"{BackgroundEntry(entry).SkillChanges[skill],2}:00";
    }

    // ----- the end -----

    /// <summary>The record bytes the generator's reset leaves that the model does not name.</summary>
    // orig: R037+0x675A (reset): DS:E80E = 0x110, DS:E810 = 0x50 (record +4, +6), DS:E822 = 0xFF (+0x18), DS:E854 = 0xFF (+0x4A), the encumbrance
    // DS:E853 = 0x0A; the record is the buffer DS:E80A, written by R037+0x38AA (the first word = DS:907E, then 0x80 bytes from there)
    // C: status +0x14 is 1 after the reset (DS:E81E) but 0 in the file the character is appended to (lockstep roster-create2): "A" in the roster makes it 1.
    // orig: the name (record +0x25, DS:E82F) and the nickname (+0x3E) are strcpy'd or typed over the same buffers each time, so the bytes after the zero are what
    // the previous text left there (lockstep roster-create2: "zed", zero, then the rest of the random name it replaced)
    // C: the buffers are shared by the generators of one process, as the original's globals are; a new game does not clear them.
    private static readonly byte[] s_nameBuffer = new byte[0x19];
    private static readonly byte[] s_nickBuffer = new byte[0x0B];

    private static void Keep(byte[] buffer, string text, int max)
    {
        var bytes = System.Text.Encoding.Latin1.GetBytes(text);
        var n = Math.Min(Math.Min(bytes.Length, max), buffer.Length - 1);
        Array.Copy(bytes, buffer, n);
        buffer[n] = 0;
    }

    private static byte[] ResetRecord()
    {
        var r = new byte[Character.MemberRecordSize];
        s_nameBuffer.CopyTo(r, 0x25);
        s_nickBuffer.CopyTo(r, 0x3E);
        r[0x04] = 0x10;
        r[0x05] = 0x01;
        r[0x06] = 0x50;
        r[0x18] = 0xFF;
        r[0x4A] = 0xFF;
        return r;
    }

    /// <summary>
    /// "Begin adventuring": the attributes become the maximums, a weapon for the best weapon
    /// skill and a potion for every potion with formula bits join the kit, and the character
    /// is made.
    /// </summary>
    // orig: R037+0x4BEE case 2 in state 4 (B/M): DS:E867 = DS:E86E, a weapon for the first best of skills 0..6
    // (items 5, 0x0F, 0x11, 0x15, 1, 0x1C, 0x1E), item 0x5F + i for every potion byte that is not 0, status 0, then
    // R037+0x38AA appends the record to charactr.tmp (CreationRoster.Add here); tests: CharacterCreationTests
    // C: the copy loop runs over 7 bytes, so Divine Favor (99) is copied too; the best-skill test is a strict >.
    private CreationOutcome BeginAdventuring()
    {
        var best = 0;
        for (var i = 0; i < 7; i++)
        {
            if (Skills[i] > Skills[best])
            {
                best = i;
            }
        }
        AddItem(WeaponForSkill[best]);
        for (var i = 0; i < Potions.Count; i++)
        {
            if (FormulaBytes[i] != 0)
            {
                AddItem(Potions.FirstPotion + i);
            }
        }
        var c = new Character
        {
            Name = Name,
            ShortName = Nickname,
            Age = Age,
            IsFemale = IsFemale,
            Status = CharacterStatus.Empty,
            ShieldLetter = 'A',
            Encumbrance = 0x0A,
            Weapon = new EquippedItem(-1, 0),
            MissileWeapon = new EquippedItem(-1, 0),
            VitalArmor = new EquippedItem(-1, 0),
            LimbArmor = new EquippedItem(-1, 0),
            Shield = new EquippedItem(-1, 0),
            OriginalRecord = ResetRecord(),
        };
        Attributes.CopyTo(c.Attributes, 0);
        Attributes.CopyTo(c.AttributeMaximums, 0);
        Skills.CopyTo(c.Skills, 0);
        c.Pack.AddRange(Kit);
        c.Saints.UnionWith(Saints);
        // C: the formula bytes are copied whole (a potion learned twice holds 2, grade 1).
        FormulaBytes.CopyTo(c.Formulas.Bytes, 0);
        Finished = c;
        return CreationOutcome.Finished;
    }
}
