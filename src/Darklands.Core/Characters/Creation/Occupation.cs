namespace Darklands.Core.Characters.Creation;

/// <summary>
/// One entry of the character generator's tables: a family background (the 15 years of
/// childhood) or an occupation (one 5-year term of adult life). Both tables share the layout.
/// </summary>
/// <param name="Index">Position in its table (occupations 0..36, backgrounds 0..5).</param>
/// <param name="Experience">Experience points the term gives (the table's "years" word).</param>
/// <param name="AttributeChanges">Change of Endurance..Charisma (6 values) for the term.</param>
/// <param name="SkillChanges">Change of the 19 skills for the term.</param>
/// <param name="SkillLimits">Most experience points one skill may receive in the term.</param>
/// <param name="Savings">The table's savings word; the original generator never uses it.</param>
// orig: R136+0xB1F5 (backgrounds, 6 x 0x3A) and R136+0xB351 (occupations, 37 x 0x3A) DS-far 290E:2B65 / 290E:2CC1 (data record R136) struct prof; tests: CreationOracleTests
// UNCERTAIN: Savings is the word at +0x38 of the 0x0C bytes the draft calls _p2e; its meaning is a guess from the unused text "accumulated savings were".
public sealed record Occupation(
    int Index,
    string Name,
    int Experience,
    IReadOnlyList<int> AttributeChanges,
    IReadOnlyList<int> SkillChanges,
    IReadOnlyList<int> SkillLimits,
    int Savings);

/// <summary>The personal-name tables of <c>random_name</c>.</summary>
/// <param name="MaleNames">First names for men.</param>
/// <param name="FemaleNames">First names for women.</param>
/// <param name="Surnames">Family names and bynames ("of Zollern").</param>
/// <param name="PatricianSurnames">The short list of patrician family names.</param>
// orig: R136+0xA9EF, R136+0xABA7, R136+0xAD0F, R136+0xAF5B (far string tables 290E, record R136) read by random_name; tests: CreationOracleTests
public sealed record NameTables(
    IReadOnlyList<string> MaleNames,
    IReadOnlyList<string> FemaleNames,
    IReadOnlyList<string> Surnames,
    IReadOnlyList<string> PatricianSurnames);

/// <summary>Everything the character generator reads from the game (DARKLAND.EXE data).</summary>
/// <param name="Backgrounds">The six family backgrounds (childhood, 15 years).</param>
/// <param name="Occupations">The 37 occupations (5-year terms).</param>
/// <param name="AgingPenalties">
/// Nine rows of six attribute changes applied after a term from age 30 on, followed by the bytes
/// after the table read the same way (<see cref="AgingRowsRead"/> rows in all), which the panel's
/// preview reads from age 70 on.
/// </param>
/// <param name="Names">The personal-name tables.</param>
// orig: R136+0xBBB1 (aging penalties 9 x 6 signed bytes, 290E:3521, record R136); tests: CreationOracleTests
// C: the aging table overlaps the last 2 bytes of occupation 36's unused padding (2CC1 + 37 x 0x3A = 3523).
// C: after its nine rows (3557 on) come two bytes and the character sheet's box table R136+0xBBE7 (290E:3559, 0x24 bytes a box):
// C: row 9 = 00 00 and box 0's x, y; row 10 its w, h, colour, flags; row 11 ix, iy, pw, ph; from row 12 on the bytes include the boxes'
// C: per-member list positions (top[], sel[]), which the character sheet changes at run time.
// DIVERGENCE: rows 12 and up are read from the EXE image (the sheet's list positions as at start-up); after a character sheet was used in this run the original reads the positions it left.
public sealed record CreationTables(
    IReadOnlyList<Occupation> Backgrounds,
    IReadOnlyList<Occupation> Occupations,
    IReadOnlyList<IReadOnlyList<int>> AgingPenalties,
    NameTables Names)
{
    /// <summary>The rows of six bytes read from 290E:3521: the nine of the table and those after it, to age 25 + 5 × 40.</summary>
    public const int AgingRowsRead = 40;

    /// <summary>
    /// The 0x3A-byte entry at 290E:2CC1 + k × 0x3A for any k, read from the program image: the occupations for 0..36,
    /// the backgrounds for −6..−1 (their table comes just before), the bytes around them otherwise. Null: entries
    /// outside the two tables read as zeros.
    /// </summary>
    // C: the panel indexes R136+0xB1F5[prn] / R136+0xB351[prn] unchecked (CharacterCreation.ShownEntry); the bytes after the occupations
    // C: are the aging table and the character sheet's box table (some of whose bytes the sheet changes at run time; the image's here).
    public Func<int, Occupation>? EntryAt { get; init; }
}
