using Darklands.Content.Formats.Executable;
using Darklands.Core.Characters.Creation;

namespace Darklands.Content.Formats.Characters;

/// <summary>
/// The name tables of DARKLAND.EXE that the card engine reads (<c>made-up-name</c> and <c>label-to-slot</c>).
/// </summary>
/// <param name="People">The four personal-name tables (first names, women's, surnames, merchant families).</param>
/// <param name="Dragons">The 16 dragon names (<c>label-to-slot</c> kind 6).</param>
/// <param name="Dwarfs">The 38 names of the mine dwarfs (kind 7).</param>
/// <param name="Titles">The 31 strings of the table at 290E:2107 (kind 8): merchant trades and houses, then the skill names.</param>
/// <param name="PointedString">The string a far pointer at 290E:offset points to (null when it points outside the data image):
/// label-to-slot reads its tables unchecked, so an index outside a table reads the neighbouring pointers.</param>
public sealed record PersonalNameData(
    NameTables People,
    IReadOnlyList<string> Dragons,
    IReadOnlyList<string> Dwarfs,
    IReadOnlyList<string> Titles,
    Func<int, string?>? PointedString = null);

/// <summary>Reads <see cref="PersonalNameData"/> from the data section of DARKLAND.EXE 483.07.</summary>
// orig: made-up-name = R029+0x5CC4 (1367:0DB4) matched (R136+0xA9EF 0x6C, R136+0xABA7 0x58, R136+0xAD0F 0x92, R136+0xAF5B 0x10: draw counts in the C);
// label-to-slot = R029+0x1FB8 (0E76:1FB8) matched (R136+0xA637 kind 6, R136+0xA677 kind 7, R136+0xA797 kind 8; checked against asm: the code reads far pointers from those addresses);
// all four are tables of far pointers (offset, segment) in the initialised data image R136 (payload = (seg - 0x20A5) * 16 + off), strings next to the other 290E texts; tests: PersonalNamesTests
// C: the tables are NOT filled at run time (contrary to what the C comments suggest): the file holds the pointers, already pointing at the strings in 290E.
// C: the extents are the gaps between consecutive tables: 1FA7 + 16 x 4 = 1FE7, 1FE7 + 38 x 4 = 207F (the shop names), 2107 + 31 x 4 = 2183 (the standing words).
public static class PersonalNamesDecoder
{
    private const int Segment = 0x290E;

    /// <summary>Where the three label-to-slot tables start in 290E (kinds 6, 7, 8).</summary>
    public const int DragonTable = 0x1FA7, DwarfTable = 0x1FE7, TitleTable = 0x2107;

    public const int DragonCount = 16;
    public const int DwarfCount = 38;
    public const int TitleCount = 31;

    public static PersonalNameData Decode(byte[] exe)
    {
        var data = ProgramData.Load(exe, Segment);
        // DORMANT: each table holds more names than the draw (R029+0x5CC4) reaches: 0x235F has 0x6E (Wiprecht, Wolfgang never
        // drawn), 0x2517 has 0x5A (Suzanne, Viktoria), 0x267F has 0x93 (of Zähringen), 0x28CB has 0x11 (Welser).
        var people = new NameTables(
            data.StringTable(Segment, 0x235F, 0x6C),
            data.StringTable(Segment, 0x2517, 0x58),
            data.StringTable(Segment, 0x267F, 0x92),
            data.StringTable(Segment, 0x28CB, 0x10));
        var dragons = data.StringTable(Segment, DragonTable, DragonCount);
        var dwarfs = data.StringTable(Segment, DwarfTable, DwarfCount);
        var titles = data.StringTable(Segment, TitleTable, TitleCount);
        if (people.MaleNames[0] != "Adalbert" || dragons[0] != "Enitharmon" || dwarfs[0] != "Alveig" || titles[0] != "goods merchant")
        {
            throw new InvalidDataException("DARKLAND.EXE is not version 483.07: the name tables are not where expected.");
        }
        string? Pointed(int offset)
        {
            try
            {
                return data.String((ushort)data.Int16(Segment, offset + 2), (ushort)data.Int16(Segment, offset));
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
        return new PersonalNameData(people, dragons, dwarfs, titles, Pointed);
    }
}
