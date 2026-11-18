using Darklands.Core.Battles.Generation;

namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The levels of a battlefield and what goes with them: the prepared level images, each level's hidden encounters (the .atv
/// activations), the level files' round trip (what is kept, saved or lost when a level is left) and the count of switches. The level
/// SHOWN is the one image in memory, <see cref="BattleMap.Level"/>, whatever level the combatants stand on; its index is
/// <see cref="Battle.LevelShown"/> (the original's DS:11CA), which stays in the model.
/// </summary>
// Not in the original (a type of its own for the original's level buffer and its files); no rule decisions live here: the stairs rules, the
// place and trigger tests stay in TacticalBattle and read the levels through this type.
public sealed class BattleLevels(Battle battle, IReadOnlyList<BattlefieldLevel> levels, BattleMap map, BattlefieldStore store, BattlefieldForces forces)
{
    /// <summary>The number of levels.</summary>
    public int Count => levels.Count;

    /// <summary>The level image of level <paramref name="level"/>.</summary>
    public BattlefieldLevel this[int level] => levels[level];

    /// <summary>The level in memory, the one on screen.</summary>
    /// <remarks>The original's rules read THIS image for a member's action whatever level he stands on; every such read in the rules is a
    /// shown-level read (Classic), and the ten split-party fixes (<c>OriginalBehaviour.Fix*OfShownLevel</c>, <c>FixTriggersOfOtherLevels</c>,
    /// <c>FixPileOfOtherLevel</c>, <c>FixPlaceActionsOnOwnLevel</c>, <c>FixFollowersOnShownLevel</c>, <c>FixWokenOnOtherLevel</c>,
    /// <c>FixLeaderKeysOnOtherLevel</c>, <c>FixCombatAcrossLevels</c>, <c>FixWalkingOnOwnLevel</c>) read his own level instead; step 7's
    /// audit starts from the uses of this property.</remarks>
    public BattlefieldLevel Shown => map.Level;

    /// <summary>The index of the level in memory (DS:11CA, <see cref="Battle.LevelShown"/>, saved by the battle file).</summary>
    public int ShownIndex { get => battle.LevelShown; set => battle.LevelShown = value; }

    /// <summary>The level the party's leader stands on (DS:C623).</summary>
    public int PartyIndex => battle.PartyLevel;

    /// <summary>Whether combatant <paramref name="who"/> stands on the level shown (the original's rules read the image in memory whatever level
    /// the member they act for stands on; where that is the split-party bug, an <c>OriginalBehaviour.Fix*</c> switch reads his own level instead).</summary>
    public bool IsShown(int who) => battle[who].Level == ShownIndex;

    /// <summary>Whether <paramref name="who"/> stands on the level shown.</summary>
    public bool IsShown(Combatant who) => who.Level == ShownIndex;

    /// <summary>Every level image, in order.</summary>
    public IReadOnlyList<BattlefieldLevel> All => levels;

    /// <summary>The hidden encounters of each level (the .atv activation records; X &lt; 0 disabled), the battlefield's own lists:
    /// those met are removed for good.</summary>
    public List<EnemyGroup>[] Activations { get; } = Enumerable.Range(0, BattlefieldForces.MaxLevels).Select(level => forces.Groups[level] ?? []).ToArray();

    /// <summary>How many times a level was switched to (the harness of the single calls tells the original's file reloads by it).</summary>
    public int Switches { get; set; }

    /// <summary>DS:D4E4: the level in the buffer changed since it was loaded or saved (level-change and the battle's end save it only then).</summary>
    public bool Changed { get; set; }

    // stairs records' +0x07 writes of use-exit made while DS:D4E4 was clear (the level buffer's byte, the value it had)
    private readonly List<(int At, byte Before)> _unsavedBytes = [];

    /// <summary>Notes a byte of the shown level written while DS:D4E4 was clear: the value it had, to be taken back when the level is left.</summary>
    public void NoteUnsavedByte(int at, byte before) => _unsavedBytes.Add((at, before));

    /// <summary>Forgets the notes of unsaved bytes (the level was saved).</summary>
    public void ClearUnsavedBytes() => _unsavedBytes.Clear();

    /// <summary>The level in the buffer is saved when DS:D4E4 is set, else what use-exit wrote since is lost; DS:D4E4 is cleared.</summary>
    // orig: level-change R024+0x05B4 (the save of the level left) and AfterBattle R014+0x069A
    public void SaveOrLoseUnsaved()
    {
        if (Changed)
        {
            SaveFiles();
        }
        else
        {
            // the original reloads the level from its file at the next switch to it (R024+0x059A): what changed without DS:D4E4 is lost
            // (use-exit's stairs mark, a sprung trap's sign, a found trap, an encounter removed when its placement failed; split audit
            // row 18)
            var floor = Enumerable.Range(0, levels.Count).FirstOrDefault(i => ReferenceEquals(levels[i], map.Level), -1);
            if (store.FloorFiles is { } floors && floor >= 0 && floor < floors.Length && floors[floor].Length == Shown.Image.Length)
            {
                floors[floor].CopyTo(Shown.Image, 0);
            }
            else
            {
                for (var k = _unsavedBytes.Count - 1; k >= 0; k--)
                {
                    Shown.SetByte(_unsavedBytes[k].At, _unsavedBytes[k].Before);
                }
            }
        }
        _unsavedBytes.Clear();
        Changed = false;
    }

    /// <summary>The prepared levels are the level files of a battlefield that has none yet.</summary>
    public void KeepFiles()
    {
        if (store.FloorFiles is not null)
        {
            return;
        }
        store.FloorFiles = [.. levels.Select(l => (byte[])l.Image.Clone())];
        store.ActivationFiles = [.. Enumerable.Range(0, levels.Count).Select(ActivationFile)];
    }

    /// <summary>The level in the buffer and its activations are written to their files.</summary>
    public void SaveFiles()
    {
        for (var i = 0; store.FloorFiles is { } floors && i < levels.Count && i < floors.Length; i++)
        {
            if (ReferenceEquals(levels[i], map.Level))
            {
                floors[i] = (byte[])map.Level.Image.Clone();
                store.ActivationFiles![i] = ActivationFile(i);
            }
        }
    }

    /// <summary>A level's activations in the .atv form (the count, then 30 bytes each).</summary>
    public byte[] ActivationFile(int level)
    {
        var activations = level < BattlefieldForces.MaxLevels ? Activations[level] : [];
        var atv = new byte[1 + activations.Count * 30];
        atv[0] = (byte)activations.Count;
        for (var k = 0; k < activations.Count; k++)
        {
            activations[k].ToRecord().CopyTo(atv, 1 + 30 * k);
        }
        return atv;
    }
}
