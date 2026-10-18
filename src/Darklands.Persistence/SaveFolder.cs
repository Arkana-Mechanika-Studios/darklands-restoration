using Darklands.Core;
using Darklands.Core.World;
using static Darklands.Persistence.RecordBytes;

namespace Darklands.Persistence;

/// <summary>What the load screen shows of a saved game: the start of its header.</summary>
/// <param name="FileName">The file's name in the folder ("DKSAVE3.SAV").</param>
/// <param name="Place">The place name (0x15 bytes).</param>
/// <param name="Comment">The comment (0x4F bytes).</param>
public sealed record SaveSummary(string FileName, string Place, string Comment, int Year, int Month, int Day, int Hour);

/// <summary>
/// The SAVES folder: the saved games (*.sav) with their battle files (*.bsv), the next free name,
/// deleting one.
/// </summary>
// orig: the folder as the save dialog (R037+0x2ACE: the first free saves\dksaveN.sav), the load screen's list
// (R037+0x8896, count R037+0x9C10) and its delete (R037+0x9B76) use it,
// matched; tests: SaveFolderTests
public sealed class SaveFolder(string path)
{
    public string Path { get; } = path;

    /// <summary>The saved games, as _dos_findfirst("saves\\*.sav") / _dos_findnext list them.</summary>
    // DIVERGENCE: DOS lists the files in directory order (the order they were made in, holes reused); here they are in name order.
    // C: *.sav in DOS matches 8.3 names with the extension SAV; longer extensions ("DKSAVE0.SAV.bak") are not listed.
    public IReadOnlyList<string> Names() =>
        Directory.Exists(Path)
            ? Directory.EnumerateFiles(Path)
                .Select(f => System.IO.Path.GetFileName(f))
                .Where(n => string.Equals(System.IO.Path.GetExtension(n), ".sav", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    /// <summary>How many saved games there are.</summary>
    // orig: R037+0x9C10 (1C7D:1A60) matched
    public int Count() => Names().Count;

    /// <summary>The header fields the list shows.</summary>
    // orig: the _dos_read sequence of R037+0x8896: name 0x15, desc 0x4F, seed, a word, year, month, day, hour
    public SaveSummary Summary(string fileName)
    {
        using var file = File.OpenRead(Find(fileName) ?? throw new FileNotFoundException(fileName));
        var h = new byte[0x70];
        file.ReadAtLeast(h, h.Length, throwOnEndOfStream: false);
        return new SaveSummary(fileName, Text(h, 0, 0x15), Text(h, 0x15, 0x4F), Word(h, 0x68), Word(h, 0x6A), Word(h, 0x6C), Word(h, 0x6E));
    }

    /// <summary>The name the next save takes: dksaveN.sav for the first N without a file.</summary>
    // orig: `do { strcpy(pathh, "saves\\dksave"); itoa(kh++, numk, 10); strcat(pathh, numk); strcat(pathh, ".sav"); } while (!_dos_findfirst(pathh, 0, &ffc));`
    // C: the name kept in DS:A680 is lower case ("dksave3.sav"); DOS stores it upper case.
    public string NextName()
    {
        for (var n = 0; ; n++)
        {
            var name = $"dksave{n}.sav";
            if (Find(name) is null)
            {
                return name;
            }
        }
    }

    /// <summary>Reads a saved game and its battle file.</summary>
    public LoadedGame Load(string fileName, IGameData data, GameState? running = null)
    {
        var sav = File.ReadAllBytes(Find(fileName) ?? throw new FileNotFoundException(fileName));
        var bsv = Find(BattleFileOf(fileName)) is { } battle ? File.ReadAllBytes(battle) : [];
        return OriginalSaves.Read(sav, bsv, fileName, data, running);
    }

    /// <summary>Saves the game under the next free name and returns that name.</summary>
    // C: the battle-state save R003+0x00DC creates (truncates) the .bsv of the same name every time (saves\<name>.bsv, the name less ".sav").
    /// <param name="inBattle">The battle running when the game is saved inside one (its combatants are the party's records).</param>
    public string Save(GameState state, string comment, bool music, bool soundEffects, Core.Battles.Battle? inBattle = null)
    {
        Directory.CreateDirectory(Path);
        var name = NextName();
        var (sav, bsv) = OriginalSaves.Write(state, comment, music, soundEffects, inBattle);
        File.WriteAllBytes(System.IO.Path.Combine(Path, name.ToUpperInvariant()), sav);
        var battle = BattleFileOf(name);
        File.WriteAllBytes(Find(battle) ?? System.IO.Path.Combine(Path, battle.ToUpperInvariant()), bsv);
        OriginalSaves.Saved(state, name, sav);
        return name;
    }

    /// <summary>
    /// Deletes a saved game and its battle file, unless it is the one being played
    /// (<paramref name="playing"/>, compared as the original compares the names: case matters).
    /// Returns whether it deleted.
    /// </summary>
    // orig: R037+0x9B76 (1C7D:19C6) matched: `if (_fstrcmp(DS:A680, file)) { remove(.sav); remove(.bsv); DS:8C4C--; }`
    // C: _fstrcmp is case-sensitive: a game saved in this session is named "dksave3.sav" in DS:A680 but listed as "DKSAVE3.SAV", so it can be deleted.
    public bool Delete(string fileName, string? playing)
    {
        if (string.Equals(fileName, playing, StringComparison.Ordinal))
        {
            return false;
        }
        if (Find(fileName) is { } sav)
        {
            File.Delete(sav);
        }
        if (Find(BattleFileOf(fileName)) is { } bsv)
        {
            File.Delete(bsv);
        }
        return true;
    }

    // C: `path[strlen(path) - 3] = 0; strcat(path, "bsv");` (the delete) and sprintf("saves\\%s.bsv", name less its last 4 characters) (battle_save).
    private static string BattleFileOf(string fileName) => fileName.Length >= 3 ? fileName[..^3] + "bsv" : fileName + ".bsv";

    /// <summary>The file of that name in the folder, whatever the case of its letters (DOS names are case-blind).</summary>
    private string? Find(string fileName) =>
        Directory.Exists(Path)
            ? Directory.EnumerateFiles(Path).FirstOrDefault(f => string.Equals(System.IO.Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
            : null;
}
