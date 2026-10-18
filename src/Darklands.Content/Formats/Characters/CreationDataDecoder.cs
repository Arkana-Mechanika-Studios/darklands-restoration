using Darklands.Content.Formats.Executable;
using Darklands.Core.Characters.Creation;

namespace Darklands.Content.Formats.Characters;

/// <summary>
/// What "Create a new party" reads from DARKLAND.EXE: the generator's tables, its texts and the
/// party slots' starting figure colours.
/// </summary>
/// <param name="Tables">Backgrounds, occupations, aging penalties, names.</param>
/// <param name="Texts">The menu texts (<c>R136+0x8690.s</c>, 0x41 entries) with their colour codes.</param>
/// <param name="SlotPalettes">The five slots' figure colours (24 bytes each) before any change.</param>
public sealed record CreationData(CreationTables Tables, IReadOnlyList<string> Texts, IReadOnlyList<byte[]> SlotPalettes)
{
    // Indexes of Texts the two menus use.
    public const int AddToParty = 0x00;
    public const int CreateCharacter = 0x01;
    public const int DeleteFromParty = 0x03;
    public const int BeginAdventure = 0x04;
    public const int ReturnToMainMenu = 0x05;
    public const int BeginChildhood = 0x06;
    public const int MakeHimAWoman = 0x07;
    public const int MakeHerAMan = 0x08;
    public const int SelectNewName = 0x09;
    public const int EnterNewName = 0x0A;
    public const int CreateNickname = 0x0B;
    public const int ReturnToGameOptions = 0x0C;
    public const int SelectFamilyBackground = 0x0D;
    public const int DoneChangingAttributes = 0x0E;
    public const int SelectOccupation = 0x10;
    public const int NextOccupation = 0x11;
    public const int BeginAdventuring = 0x12;
    public const int KillCharacter = 0x13;
    public const int SelectImage = 0x1A;
    public const int ReturnToAdventure = 0x3C;
    public const int FirstColour = 0x3D;
    public const int SecondColour = 0x3E;
    public const int ThirdColour = 0x3F;
    public const int Heraldry = 0x40;
}

/// <summary>Reads <see cref="CreationData"/> from the data section of DARKLAND.EXE 483.07.</summary>
// orig: R136+0x8690 (texts at 290E:1DDF), R136+0xA487, R136+0xA73B, R136+0xA577, R136+0xB1F5, R136+0xB351, R136+0xBBB1,
// R136+0xA9EF, R136+0xABA7, R136+0xAD0F, R136+0xAF5B, member_palette = R136+0x33FC (data record R136) declared in
//,,; tests: CreationOracleTests
// C: R136+0xA487 is the same pointer table as R136+0x8690.s from entry 6 on (1DF7 = 1DDF + 6 x 4).
public static class CreationDataDecoder
{
    private const int Segment = 0x290E;
    private const int EntrySize = 0x3A;

    public static CreationData Decode(byte[] exe)
    {
        var data = ProgramData.Load(exe, Segment);
        var backgroundNames = data.StringTable(Segment, 0x20AB, 6);
        var occupationNames = data.StringTable(Segment, 0x1EE7, CharacterCreation.OccupationCount);
        if (backgroundNames[0] != "Nobility" || occupationNames[0] != "Recruit")
        {
            throw new InvalidDataException("DARKLAND.EXE is not version 483.07: the character generator's tables are not where expected.");
        }
        var backgrounds = Enumerable.Range(0, 6).Select(i => Entry(data, 0x2B65, i, backgroundNames[i])).ToList();
        var occupations = Enumerable.Range(0, CharacterCreation.OccupationCount).Select(i => Entry(data, 0x2CC1, i, occupationNames[i])).ToList();
        // C: nine rows; the panel's preview reads rows past them from age 70 on: the bytes that follow (CreationTables.AgingPenalties).
        var aging = Enumerable.Range(0, CreationTables.AgingRowsRead)
            .Select(k => (IReadOnlyList<int>)Enumerable.Range(0, 6).Select(a => (int)data.SByte(Segment, 0x3521 + 6 * k + a)).ToArray())
            .ToList();
        var names = new NameTables(
            data.StringTable(Segment, 0x235F, 0x6C),
            data.StringTable(Segment, 0x2517, 0x58),
            data.StringTable(Segment, 0x267F, 0x92),
            data.StringTable(Segment, 0x28CB, 0x10));
        var texts = data.StringTable(Segment, 0x1DDF, 0x41);
        var palettes = Enumerable.Range(0, 5).Select(i => data.Bytes(0x20A5, 0x33FC + 24 * i, 24).ToArray()).ToList();
        return new CreationData(new CreationTables(backgrounds, occupations, aging, names) { EntryAt = k => EntryAnywhere(data, k) },
            texts, palettes);
    }

    /// <summary>The entry at 2CC1 + k × 0x3A as a 16-bit offset in the segment (the C index wraps); zeros where the image ends.</summary>
    private static Occupation EntryAnywhere(ProgramData data, int k)
    {
        var at = (0x2CC1 + k * EntrySize) & 0xFFFF;
        try
        {
            return Entry(data, at, 0, "") with { Index = k };
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException)
        {
            return new Occupation(k, "", 0, new int[6], new int[19], new int[19], 0);
        }
    }

    /// <summary>One 0x3A-byte entry: int years; char attr[6]; char skill[19]; u8 maxsk[19]; 12 more bytes (savings word at +0x38).</summary>
    // C: attr and skill are signed chars (the tables hold −1), the limits unsigned.
    private static Occupation Entry(ProgramData data, int table, int index, string name)
    {
        var at = table + index * EntrySize;
        var attributes = Enumerable.Range(0, 6).Select(i => (int)data.SByte(Segment, at + 2 + i)).ToArray();
        var skills = Enumerable.Range(0, 19).Select(i => (int)data.SByte(Segment, at + 8 + i)).ToArray();
        var limits = data.Bytes(Segment, at + 27, 19).ToArray().Select(b => (int)b).ToArray();
        return new Occupation(index, name, data.Int16(Segment, at), attributes, skills, limits, data.Int16(Segment, at + 0x38));
    }
}
