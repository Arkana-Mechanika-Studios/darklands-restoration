using Darklands.Content.Formats.Characters;

namespace Darklands.Content.Formats.Exe;

/// <summary>
/// The check, before the game starts, that the player's DARKLAND.EXE is the build whose tables this program reads (the English
/// 483.07). Another build (the German release ...) has them elsewhere, and the game would end at the first screen that reads them.
/// </summary>
public static class ExeVersion
{
    public const string NotSupported = "DARKLAND.EXE is not the English version 483.07 (an older version, or a German release?). "
        + "Only that version is supported: install Darklands in English (GOG: Configure, Language) and choose that folder.";

    /// <summary>Null when every table this program takes from the EXE reads, else why not, for the player.</summary>
    public static string? Problem(byte[] exe)
    {
        try
        {
            CreationDataDecoder.Decode(exe);
            PersonalNamesDecoder.Decode(exe);
            CombatTablesReader.Read(DarklandsExecutable.Decode(exe));
            return null;
        }
        catch (Exception)
        {
            // a build with other tables fails anywhere in the decoders (a string not where expected, an offset past the file ...)
            return NotSupported;
        }
    }
}
