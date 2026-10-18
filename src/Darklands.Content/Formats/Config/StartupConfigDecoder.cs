namespace Darklands.Content.Formats.Config;

/// <summary>
/// Decoder for the current 8-byte CONFIG.DRK startup record.
/// </summary>
public sealed class StartupConfigDecoder
{
    public const int ConfigByteLength = 8;
    public const string DefaultResourceName = "config.drk";

    /// <summary>
    /// Decodes a CONFIG.DRK stream as the current 8-byte startup configuration record.
    /// </summary>
    /// <remarks>
    /// This is format provenance. Engine startup code records where CONFIG.DRK is opened.
    /// </remarks>
    public StartupConfig Decode(Stream stream, string resourceName = DefaultResourceName)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        var bytes = new byte[ConfigByteLength];
        var totalRead = 0;
        while (totalRead < bytes.Length)
        {
            var read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead != ConfigByteLength)
        {
            throw new StartupConfigDecodeException(
                $"{resourceName} must contain exactly {ConfigByteLength} startup config bytes; read {totalRead}.");
        }

        if (stream.ReadByte() >= 0)
        {
            throw new StartupConfigDecodeException(
                $"{resourceName} contains trailing bytes after the {ConfigByteLength}-byte startup config.");
        }

        return DecodeBytes(bytes, resourceName);
    }

    /// <summary>
    /// Decodes the four little-endian CONFIG.DRK words into the startup config model.
    /// </summary>
    /// <remarks>
    /// This method models the file format contract; it is not itself an original engine routine.
    /// </remarks>
    public static StartupConfig DecodeBytes(ReadOnlySpan<byte> bytes, string resourceName = DefaultResourceName)
    {
        if (bytes.Length != ConfigByteLength)
        {
            throw new StartupConfigDecodeException(
                $"{resourceName} must contain exactly {ConfigByteLength} startup config bytes; found {bytes.Length}.");
        }

        var words = new[]
        {
            ReadWord(bytes, 0),
            ReadWord(bytes, 2),
            ReadWord(bytes, 4),
            ReadWord(bytes, 6)
        };
        var selection = SelectSoundDriver(words[0]);
        var hardware = new StartupSoundDriverHardwareSettings(
            DeviceOrDriverWord: words[0],
            BasePort: words[1],
            Irq: words[2],
            DmaChannel: words[3]);

        return new StartupConfig(
            resourceName,
            FormatHexBytes(bytes),
            words,
            selection,
            hardware);
    }

    private static int ReadWord(ReadOnlySpan<byte> bytes, int offset)
    {
        return bytes[offset] | (bytes[offset + 1] << 8);
    }

    /// <summary>The letter of the sound driver the game picks for a CONFIG.DRK card code (first word).</summary>
    // orig: R000+0x0114 (0E76:0114) draft, bytes matched, checked against asm: `switch (DS:A86E)` 0..3 "a", 4..6 "p", 7 "r",
    // 0x7F "i", anything else "n", then strcat "sound.dlc"; without the file the name is "nsound.dlc"
    public static char DriverLetter(int configValue) => configValue switch
    {
        >= 0 and <= 3 => 'a',
        >= 4 and <= 6 => 'p',
        7 => 'r',
        0x7F => 'i',
        _ => 'n',
    };

    private static StartupSoundDriverSelection SelectSoundDriver(int configValue)
    {
        var letter = DriverLetter(configValue);
        return BuildSelection(configValue, letter.ToString(), letter + "sound.dlc");
    }

    private static StartupSoundDriverSelection BuildSelection(
        int configValue,
        string prefix,
        string driverFileName,
        string? evidence = null)
    {
        return new StartupSoundDriverSelection(
            configValue,
            prefix,
            driverFileName,
            evidence ?? "CONFIG.DRK sound-card code selects this driver filename in the recovered startup/JUKEBOX selector model; backend semantics are not inferred from the filename.");
    }

    private static string FormatHexBytes(ReadOnlySpan<byte> bytes)
    {
        return string.Join(" ", bytes.ToArray().Select(value => value.ToString("X2")));
    }
}

/// <summary>
/// Raised when CONFIG.DRK does not match the expected startup record.
/// </summary>
public sealed class StartupConfigDecodeException : Exception
{
    public StartupConfigDecodeException(string message)
        : base(message)
    {
    }
}

