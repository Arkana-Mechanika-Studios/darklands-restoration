using Darklands.Core.Randomness;

namespace Darklands.Core.Characters.Creation;

/// <summary>The kinds of name <c>random_name</c> makes.</summary>
public enum NameKind
{
    Man = 0,
    Woman = 1,
    PatricianMan = 2,
    PatricianWoman = 3,
    ManFirstNameOnly = 4,
    WomanFirstNameOnly = 5,
}

/// <summary>
/// Random personal names (<c>random_name</c>): the generator is reseeded with the party seed
/// plus <paramref name="seed"/>, one draw is thrown away, then the first name and the family
/// name are drawn; afterwards the generator is reseeded from the clock.
/// </summary>
public static class RandomNames
{
    // orig: random_name = R029+0x5CC4 (1367:0DB4) matched; draws: random_n = root 0410:0008; tests: CharacterCreationTests
    // C: srand(party_seed + seed) adds two 16-bit ints (wraps) and srand takes the low 16 bits; the
    // closing srand((unsigned)time(0)) also keeps only the low 16 bits of the clock.
    // C: kinds 2..5 are never asked for by the generator (it passes the sex, 0 or 1); kept for card scripts.
    public static string Make(IRandomSource random, NameTables names, int partySeed, int seed, NameKind kind, uint clockSeed)
    {
        random.Seed((ushort)(partySeed + seed));
        random.Scaled(1000);
        var men = names.MaleNames;
        var women = names.FemaleNames;
        var name = kind switch
        {
            NameKind.Man => Pick(random, men) + " " + Pick(random, names.Surnames),
            NameKind.Woman => Pick(random, women) + " " + Pick(random, names.Surnames),
            NameKind.PatricianMan => Pick(random, men) + " " + Pick(random, names.PatricianSurnames),
            NameKind.PatricianWoman => Pick(random, women) + " " + Pick(random, names.PatricianSurnames),
            NameKind.ManFirstNameOnly => Pick(random, men),
            _ => Pick(random, women),
        };
        random.Seed((ushort)clockSeed);
        return name;
    }

    private static string Pick(IRandomSource random, IReadOnlyList<string> list) => list[random.Scaled(list.Count)];

    /// <summary>The nickname taken from a full name: its first word, at most nine letters.</summary>
    // orig: R037+0x675A / R037+0x4BEE cases 2,3 R037+0x675A (18C8:20FA) matched: strncpy(nick, name, clamp_range(0, 9, root 06A1:2824(name, " "))); tests: CharacterCreationTests
    // C: root 06A1:2824 is the CRT strcspn (checked against asm): the length up to the first space, the whole name without one.
    public static string Nickname(string fullName)
    {
        var space = fullName.IndexOf(' ', StringComparison.Ordinal);
        var length = Math.Clamp(space < 0 ? fullName.Length : space, 0, 9);
        return fullName[..length];
    }
}
