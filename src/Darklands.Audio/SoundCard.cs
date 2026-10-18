using Darklands.Audio.Machine;
using Darklands.Content;
using Darklands.Content.Formats.Config;

namespace Darklands.Audio;

/// <summary>
/// The sound cards of the install program, by the number it writes as the first word of
/// CONFIG.DRK and the game passes to its sound driver (INSTALL.EXE's list).
/// </summary>
public enum SoundCard
{
    /// <summary>AdLib: OPL2 music and effects, no samples.</summary>
    AdLib = 0,

    CovoxSoundMaster = 1,

    /// <summary>Sound Blaster: OPL2 plus the sampled speech and effects of the opening and ending.</summary>
    SoundBlaster = 2,

    /// <summary>Sound Blaster Pro (early model).</summary>
    SoundBlasterPro = 3,

    /// <summary>Sound Blaster Pro (later model): OPL3 plus samples.</summary>
    SoundBlasterPro2 = 4,

    ProAudioSpectrum = 5,

    ProAudioSpectrum16 = 6,

    /// <summary>Roland MT-32 / LAPC-I alone (no samples).</summary>
    Roland = 7,

    /// <summary>The PC speaker.</summary>
    PcSpeaker = 0x7F,
}

/// <summary>The card's resources, as CONFIG.DRK holds them after the card number.</summary>
public sealed record SoundCardSettings(SoundCard Card, int Port = 0x220, int Irq = 7, int Dma = 1)
{
    /// <summary>The player's choice from CONFIG.DRK (card, port, IRQ, DMA as four words); null without the file.</summary>
    // orig: R000+0x0114 (0E76:0114) draft - the start-up configuration: fread(DS:A86E, 8, 1, f): A86E card, A870 port, A872 IRQ, A874 DMA.
    public static SoundCardSettings? FromConfig(GameFiles files)
    {
        if (!files.Exists("CONFIG.DRK"))
        {
            return null;
        }
        var bytes = files.Read("CONFIG.DRK");
        if (bytes.Length < 8)
        {
            return null;
        }
        int Word(int at) => bytes[at] | (bytes[at + 1] << 8);
        return new SoundCardSettings((SoundCard)Word(0), Word(2), Word(4), Word(6));
    }

    /// <summary>The letter of the driver files the game loads for this card ("a" for asound.dlc ...).</summary>
    // orig: R000+0x0114 (0E76:0114) draft - the switch on the card: 0..3 'a', 4..6 'p', 7 'r', 7Fh 'i', others 'n'.
    public char DriverLetter => StartupConfigDecoder.DriverLetter((int)Card);

    /// <summary>True for the cards the game gives the sampled sounds of the opening and the ending (letters a and p).</summary>
    // orig: root 03A1:000A and R133+0x186C: `if (DS:A876 == 'a' || DS:A876 == 'p')` before loading opendark/enddark.
    public bool PlaysSamples => DriverLetter is 'a' or 'p';
}

/// <summary>What a sound driver drives, by the name in its image.</summary>
public enum DriverKind
{
    /// <summary>An FM chip (AdLib, Sound Blaster, Sound Blaster Pro): ASOUND/PSOUND .DC/.DB, or .DLC/.DLB in the original install.</summary>
    Fm,

    /// <summary>A Roland MT-32 / CM-32L / LAPC-I through an MPU-401 (and a Sound Blaster for the samples): RSOUND.DLC/.DLB.</summary>
    Roland,

    /// <summary>The PC speaker (ISOUND .DLC/.DLB): counter 2 of the timer and port 61h.</summary>
    Speaker,

    /// <summary>NSOUND.DLC, or no driver.</summary>
    None,
}

/// <summary>The Roland module the MIDI goes to (the same driver plays both).</summary>
public enum RolandModule
{
    Mt32,
    Cm32l,
}

/// <summary>
/// The sound the game will make: the card it passes to its drivers, the two drivers (towns and
/// map; battles) and what they drive. <see cref="FromConfig"/> picks them as the game does (the
/// letter of CONFIG.DRK's card + "sound.dlc"/".dlb"); <see cref="ForChoice"/> is the host's
/// override (--sound).
/// </summary>
public sealed record SoundSetup(
    SoundCardSettings Card,
    DriverKind Kind,
    string TownsDriverName,
    byte[] TownsDriver,
    string BattleDriverName,
    byte[] BattleDriver,
    RolandModule Module = RolandModule.Mt32)
{
    /// <summary>The towns-and-map and battle drivers' names and dates (offset 10h of their images).</summary>
    public string Signature => DriverImage.Signature(TownsDriver).TrimEnd();

    /// <summary>The game's choice: CONFIG.DRK's card (none: "n") and the driver files of its letter.</summary>
    // orig: R000+0x0114 (0E76:0114) draft - the towns-and-map driver letter + "sound.dlc" (no CONFIG.DRK: "nsound.dlc");
    //   R002+0x0000 draft - the battle loads "%csound.dlb" with the same letter.
    public static SoundSetup FromConfig(GameFiles files) => FromCommandLine(files, []);

    /// <summary>
    /// The game's choice with its DOS command line: CONFIG.DRK's letter, unless an option -A&lt;letter&gt; (or /A, any case)
    /// names a driver whose file exists; the card number and resources stay CONFIG.DRK's (zero without the file).
    /// </summary>
    // orig: R000+0x0114 (0E76:0114) draft, bytes match: every argument upper-cased in place (root 044A:000A), then for an option
    // 'A': name = the next character + "sound.dlc"; fopen(name, "r") found: the name is copied to DS:A876 (root 044A:002C),
    // else "Sound driver not found" and the program stops (StartUp); the card word DS:A86E is not touched; tests: SpeakerTests
    // C: without CONFIG.DRK DS:A86E..A875 keep their start-up zeros: card 0, port 0, IRQ 0, DMA 0 for a driver named by -A.
    public static SoundSetup FromCommandLine(GameFiles files, IReadOnlyList<string> commandLine)
    {
        var config = SoundCardSettings.FromConfig(files);
        var letter = config is null ? 'N' : char.ToUpperInvariant(config.DriverLetter);
        foreach (var argument in commandLine)
        {
            var option = argument.ToUpperInvariant();
            if (option.Length >= 2 && option[0] is '-' or '/' && option[1] == 'A')
            {
                var named = option.Length > 2 ? option[2] : '\0';
                if (named != '\0' && files.Exists($"{named}SOUND.DLC"))
                {
                    letter = named;
                }
            }
        }
        var card = config ?? (letter == 'N' ? new SoundCardSettings((SoundCard)(-1)) : new SoundCardSettings(SoundCard.AdLib, 0, 0, 0));
        return Load(files, card, $"{letter}SOUND.DLC", $"{letter}SOUND.DLB");
    }

    /// <summary>
    /// A card the player names: adlib, sb, sbpro2 (the FM drivers), mt32 or cm32l (the Roland driver,
    /// with CONFIG.DRK's Sound Blaster for the samples when it names one, as the game's README describes
    /// for "simultaneous Roland and Sound Blaster", else a Sound Blaster at 220h, IRQ 7, DMA 1).
    /// </summary>
    // DIVERGENCE: the original only knows the files of CONFIG.DRK's letter; the GOG release keeps the FM drivers as
    //   .DC/.DB (renamed for the dual-board setup), so a choice is found by the driver's name rather than its file name.
    public static SoundSetup ForChoice(GameFiles files, string choice)
    {
        var config = SoundCardSettings.FromConfig(files);
        var card = choice switch
        {
            "adlib" => new SoundCardSettings(SoundCard.AdLib),
            "sb" => new SoundCardSettings(SoundCard.SoundBlaster, config?.Port ?? 0x220, config?.Irq ?? 7, config?.Dma ?? 1),
            "sbpro2" => new SoundCardSettings(SoundCard.SoundBlasterPro2, config?.Port ?? 0x220, config?.Irq ?? 7, config?.Dma ?? 1),
            "mt32" or "cm32l" => config is { Card: >= SoundCard.SoundBlaster and <= SoundCard.ProAudioSpectrum16 }
                ? config
                : new SoundCardSettings(SoundCard.SoundBlaster),
            "speaker" => new SoundCardSettings(SoundCard.PcSpeaker),
            _ => throw new ArgumentException($"unknown sound card \"{choice}\" (adlib, sb, sbpro2, mt32, cm32l, speaker)", nameof(choice)),
        };
        if (choice == "speaker")
        {
            return Load(files, card, "ISOUND.DLC", "ISOUND.DLB");
        }
        if (choice is "mt32" or "cm32l")
        {
            var (townsName, towns) = Find(files, "RLND DrkLand", "DLC", "DC") ;
            var (battleName, battle) = Find(files, "RLND DrkLand", "DLB", "DB");
            return new SoundSetup(card, DriverKind.Roland, townsName, towns, battleName, battle,
                choice == "cm32l" ? RolandModule.Cm32l : RolandModule.Mt32);
        }
        return ForFmCard(files, card);
    }

    /// <summary>The FM drivers for an FM card (OPL3 drivers for the later Sound Blaster Pro and the Pro Audio Spectrum).</summary>
    // DIVERGENCE: cards 1 (Covox) and 3 (early SB Pro) are played as a Sound Blaster, 5 and 6 (Pro Audio Spectrum, Gold)
    //   as a later SB Pro: the drivers' code for those cards' own mixers is not emulated.
    public static SoundSetup ForFmCard(GameFiles files, SoundCardSettings card)
    {
        var opl3 = card.Card is SoundCard.SoundBlasterPro2 or SoundCard.ProAudioSpectrum or SoundCard.ProAudioSpectrum16;
        var played = card with
        {
            Card = card.Card switch
            {
                SoundCard.AdLib => SoundCard.AdLib,
                _ when opl3 => SoundCard.SoundBlasterPro2,
                _ => SoundCard.SoundBlaster,
            },
        };
        var (townsName, towns) = Find(files, opl3 ? "P Dark Cards" : "DrkCardAdLib", "DC", "DLC");
        var (battleName, battle) = Find(files, opl3 ? "P DarkBattle" : "DKBttleAdLib", "DB", "DLB");
        return new SoundSetup(played, DriverKind.Fm, townsName, towns, battleName, battle);
    }

    /// <summary>A line for the player: what plays, through which files.</summary>
    public string Describe()
    {
        var card = Card.Card switch
        {
            SoundCard.AdLib => "AdLib",
            SoundCard.CovoxSoundMaster => "Covox Sound Master",
            SoundCard.SoundBlaster => "Sound Blaster",
            SoundCard.SoundBlasterPro => "Sound Blaster Pro",
            SoundCard.SoundBlasterPro2 => "Sound Blaster Pro 2",
            SoundCard.ProAudioSpectrum => "Pro Audio Spectrum",
            SoundCard.ProAudioSpectrum16 => "Pro Audio Spectrum 16",
            SoundCard.Roland => "Roland",
            SoundCard.PcSpeaker => "PC speaker",
            _ => "no sound card",
        };
        var resources = Card.Card is >= SoundCard.CovoxSoundMaster and <= SoundCard.ProAudioSpectrum16
            ? $" at {Card.Port:X}h, IRQ {Card.Irq}, DMA {Card.Dma}"
            : "";
        var samples = Card.PlaysSamples && Card.Card != SoundCard.AdLib ? $"; speech and effects of the opening/ending: {card} DSP{resources}" : "";
        return Kind switch
        {
            DriverKind.Fm => $"music and effects: {card} FM ({TownsDriverName} \"{Signature}\", {BattleDriverName}){samples}",
            DriverKind.Roland => $"music and effects: Roland {(Module == RolandModule.Cm32l ? "CM-32L" : "MT-32")} by MPU-401 at 330h ({TownsDriverName} \"{Signature}\", {BattleDriverName}){samples}",
            DriverKind.Speaker => $"music and effects: PC speaker ({TownsDriverName} \"{Signature}\", {BattleDriverName})",
            _ => "no sound driver: no sound",
        };
    }

    private static SoundSetup Load(GameFiles files, SoundCardSettings card, string townsName, string battleName)
    {
        if (!files.Exists(townsName))
        {
            return new SoundSetup(card, DriverKind.None, townsName, [], battleName, []);
        }
        var towns = files.Read(townsName);
        var battle = files.Exists(battleName) ? files.Read(battleName) : [];
        return new SoundSetup(card, KindOf(towns), townsName, towns, battleName, battle);
    }

    /// <summary>What a driver image drives, by its name.</summary>
    public static DriverKind KindOf(byte[] driver)
    {
        var name = DriverImage.Signature(driver);
        return name switch
        {
            _ when name.StartsWith("RLND", StringComparison.Ordinal) => DriverKind.Roland,
            _ when name.StartsWith("DrkCardAdLib", StringComparison.Ordinal) || name.StartsWith("DKBttleAdLib", StringComparison.Ordinal)
                || name.StartsWith("P Dark", StringComparison.Ordinal) => DriverKind.Fm,
            _ when name.Contains("IBM", StringComparison.Ordinal) => DriverKind.Speaker,
            _ => DriverKind.None,
        };
    }

    /// <summary>The first driver file with the name <paramref name="signature"/>, among A/P/R SOUND with the extensions.</summary>
    private static (string Name, byte[] Image) Find(GameFiles files, string signature, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            foreach (var letter in "APRI")
            {
                var name = $"{letter}SOUND.{extension}";
                if (files.Exists(name))
                {
                    var image = files.Read(name);
                    if (DriverImage.Signature(image).StartsWith(signature, StringComparison.Ordinal))
                    {
                        return (name, image);
                    }
                }
            }
        }
        throw new FileNotFoundException($"no \"{signature}\" sound driver in the game folder");
    }
}
