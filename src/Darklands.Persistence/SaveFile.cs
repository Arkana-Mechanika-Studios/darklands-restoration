using System.Buffers.Binary;

namespace Darklands.Persistence;

/// <summary>
/// The sections of a saved game, SAVES\DKSAVEn.SAV (also SAVES\DEFAULT), as bytes: the 0xEF-byte
/// header, the party block (party size, character count, the five party slots, their figure
/// images and colours), the characters' 0x22A-byte records, the events' 0x30-byte records, the
/// location table and, to the end of the file, a copy of CACHE.TMP. Reading and writing them
/// gives the file back byte for byte; <see cref="OriginalSaves"/> maps them to the game state.
/// </summary>
// orig: written by R037+0x2ACE (1462:2ACE) draft (unmatched: a loop-test operand order),
// read by R037+0x8ED4 (1C7D:0D24) matched and (header skipped, party block and records only)
// saved-game-read R037+0x4184 (1462:4184) matched; the draft's write order (2ace) checked
// against the 19 saved games and SAVES\DEFAULT of the player's folder, which all split and join back byte for byte; tests: OriginalSaveTests
// C: the loader reads the location table as one block of 0x5DCE bytes (2 + 414 x 0x3A) whatever its count says, and takes the rest of
// the file as CACHE.TMP; every file the game writes has 414 records, so the count decides here (SAVES\DEFAULT, which the game only
// reads with saved-game-read, holds 405 and round-trips too).
public sealed class SaveFile
{
    public const int HeaderSize = 0xEF;
    public const int PartySlots = 5;
    public const int ImageSize = 4;
    public const int PaletteSize = 0x18;
    public const int CharacterSize = 0x22A;
    public const int EventSize = 0x30;
    public const int LocationSize = 0x3A;

    /// <summary>The number of places every saved game holds (0x19E).</summary>
    public const int LocationCount = 0x19E;

    /// <summary>The 0xEF-byte header (place, comment, the game's global words; see <see cref="OriginalSaves"/>).</summary>
    public byte[] Header { get; set; } = new byte[HeaderSize];

    /// <summary>party-headcount (+0xEF): one more than the last party slot in use.</summary>
    public int PartySize { get; set; }

    /// <summary>The roster entry in each party slot (+0xF3, −1 empty).</summary>
    public int[] PartyIds { get; } = [-1, -1, -1, -1, -1];

    /// <summary>member-shield-picture (+0xFD): the figure image of each slot, four bytes ("F60\0").</summary>
    public byte[] Images { get; set; } = new byte[PartySlots * ImageSize];

    /// <summary>member-colours (+0x111): the figure colours of each slot, 24 bytes.</summary>
    public byte[] Palettes { get; set; } = new byte[PartySlots * PaletteSize];

    /// <summary>The characters' records (+0x189), in roster order.</summary>
    public List<byte[]> Characters { get; } = [];

    /// <summary>The record count written at +0xF1; null: the number of <see cref="Characters"/>.</summary>
    // C: the original's writer copies charactr.tmp's count, also when it writes fewer records (a party slot of status 0, OriginalSaves.Write).
    public int? CharacterCount { get; set; }

    /// <summary>The events' records, in slot order (the empty slots left out).</summary>
    public List<byte[]> Events { get; } = [];

    /// <summary>The locations' records.</summary>
    public List<byte[]> Locations { get; } = [];

    /// <summary>The rest of the file: the game's CACHE.TMP.</summary>
    public byte[] Cache { get; set; } = [];

    /// <param name="fixedLocationTable">Read the location table as the loader R037+0x8ED4 does: one block of 414 records,
    /// whatever its count word says (a file whose earlier counts do not match its records is read out of step, as in the original).</param>
    public static SaveFile Parse(ReadOnlySpan<byte> data, bool fixedLocationTable = false)
    {
        var at = 0;
        var file = new SaveFile { Header = Take(data, ref at, fixedLocationTable, HeaderSize).ToArray() };
        file.PartySize = Word(data, ref at, fixedLocationTable);
        var characters = Word(data, ref at, fixedLocationTable);
        for (var slot = 0; slot < PartySlots; slot++)
        {
            file.PartyIds[slot] = (short)Word(data, ref at, fixedLocationTable);
        }
        file.Images = Take(data, ref at, fixedLocationTable, file.Images.Length).ToArray();
        file.Palettes = Take(data, ref at, fixedLocationTable, file.Palettes.Length).ToArray();
        for (var i = 0; i < characters; i++)
        {
            file.Characters.Add(Take(data, ref at, fixedLocationTable, CharacterSize).ToArray());
        }
        var events = Word(data, ref at, fixedLocationTable);
        for (var i = 0; i < events; i++)
        {
            file.Events.Add(Take(data, ref at, fixedLocationTable, EventSize).ToArray());
        }
        var locations = Word(data, ref at, fixedLocationTable);
        // DIVERGENCE: a file too short for the 414-record block (only the tests' small worlds write one) is read by its count.
        if (fixedLocationTable && data.Length - at >= LocationCount * LocationSize)
        {
            locations = LocationCount;
        }
        for (var i = 0; i < locations; i++)
        {
            file.Locations.Add(Take(data, ref at, fixedLocationTable, LocationSize).ToArray());
        }
        file.Cache = at < data.Length ? data[at..].ToArray() : [];
        return file;
    }

    public byte[] ToBytes()
    {
        using var out_ = new MemoryStream();
        out_.Write(Fixed(Header, HeaderSize));
        PutWord(out_, PartySize);
        PutWord(out_, CharacterCount ?? Characters.Count);
        foreach (var id in PartyIds)
        {
            PutWord(out_, id);
        }
        out_.Write(Fixed(Images, PartySlots * ImageSize));
        out_.Write(Fixed(Palettes, PartySlots * PaletteSize));
        foreach (var record in Characters)
        {
            out_.Write(Fixed(record, CharacterSize));
        }
        PutWord(out_, Events.Count);
        foreach (var record in Events)
        {
            out_.Write(Fixed(record, EventSize));
        }
        PutWord(out_, Locations.Count);
        foreach (var record in Locations)
        {
            out_.Write(Fixed(record, LocationSize));
        }
        out_.Write(Cache);
        return out_.ToArray();
    }

    // C: the loader's _dos_read returns what the file still holds and the game goes on; reading as the loader, the bytes past the end
    // C: are zeros here.
    // DIVERGENCE: the original's buffers keep what they held before (a heap block's leftovers), not zeros.
    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> data, ref int at, bool lenient, int length)
    {
        if (at + length > data.Length)
        {
            if (!lenient)
            {
                throw new InvalidDataException($"the saved game ends at {data.Length} bytes, inside a section that needs {at + length}");
            }
            var padded = new byte[length];
            if (at < data.Length)
            {
                data[at..].CopyTo(padded);
            }
            at += length;
            return padded;
        }
        var slice = data.Slice(at, length);
        at += length;
        return slice;
    }

    private static int Word(ReadOnlySpan<byte> data, ref int at, bool lenient) =>
        BinaryPrimitives.ReadUInt16LittleEndian(Take(data, ref at, lenient, 2));

    private static void PutWord(Stream s, int value)
    {
        s.WriteByte((byte)value);
        s.WriteByte((byte)(value >> 8));
    }

    private static byte[] Fixed(byte[] bytes, int length) =>
        bytes.Length == length ? bytes : throw new InvalidDataException($"a section of {bytes.Length} bytes where the file has {length}");
}
