namespace Darklands.Content.Formats.Config;

/// <summary>
/// Decoded CONFIG.DRK startup configuration.
/// </summary>
public sealed record StartupConfig(
    string ResourceName,
    string HexBytes,
    IReadOnlyList<int> Words,
    StartupSoundDriverSelection SoundDriverSelection,
    StartupSoundDriverHardwareSettings SoundHardwareSettings)
{
    public string SelectedSoundDriver => SoundDriverSelection.DriverFileName;

    public string SelectedSoundBank => SoundDriverSelection.CompanionBankFileName;
}

/// <summary>
/// Sound driver selected from CONFIG.DRK and startup switch evidence.
/// </summary>
public sealed record StartupSoundDriverSelection(
    int ConfigValue,
    string DriverPrefix,
    string DriverFileName,
    string Evidence)
{
    public string CompanionBankFileName
    {
        get
        {
            var dot = DriverFileName.LastIndexOf('.');
            return dot >= 0
                ? DriverFileName[..dot] + ".dlb"
                : DriverFileName + ".dlb";
        }
    }
}

/// <summary>
/// DOS-era sound hardware settings decoded from CONFIG.DRK.
/// </summary>
public sealed record StartupSoundDriverHardwareSettings(
    int DeviceOrDriverWord,
    int BasePort,
    int Irq,
    int DmaChannel);

