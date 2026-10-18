using Darklands.Content;

namespace Darklands.Audio.Mt32;

/// <summary>The drivers to run and, for the Roland driver, the MT-32 / CM-32L that plays their MIDI.</summary>
public sealed record SelectedSound(SoundSetup Setup, IMidiSynth? Midi) : IDisposable
{
    /// <summary>Starts the drivers, rendering for <paramref name="output"/> (any host's PCM sink).</summary>
    public SynthAudio Start(GameFiles files, IPcmOutput output, Action<AudioEvent>? traced = null) =>
        new(files, output, Setup, Midi, traced);

    public void Dispose() => Midi?.Dispose();
}

/// <summary>
/// The game's sound for any host: CONFIG.DRK's choice as the game makes it (or the player's
/// --sound), the Roland driver's MIDI played by mt32emu with the player's ROMs, and the FM drivers
/// of the same card when the ROMs are missing. Every step is reported in a line for the player.
/// </summary>
public static class SoundSelection
{
    /// <summary>The start of a reported line that is a warning (hosts show it as one).</summary>
    public const string WarningPrefix = "warning: ";

    /// <summary>True when <paramref name="line"/> is a warning.</summary>
    public static bool IsWarning(string line) => line.StartsWith(WarningPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The ROM folder: the host's choice (settings file, command line), else DARKLANDS_MT32_ROMS, else MT32_ROMS next to the
    /// program; when that folder does not exist, a developer's local/paths.json "mt32roms". The game folder is not searched.
    /// </summary>
    public static string RomFolder(string? chosen)
    {
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            return chosen;
        }
        var variable = Environment.GetEnvironmentVariable("DARKLANDS_MT32_ROMS");
        if (!string.IsNullOrWhiteSpace(variable))
        {
            return variable;
        }
        var besideProgram = Path.Combine(AppContext.BaseDirectory, "MT32_ROMS");
        return Directory.Exists(besideProgram) ? besideProgram : Darklands.Platform.LocalPaths.Get("mt32roms", "DARKLANDS_MT32_ROMS") ?? besideProgram;
    }

    /// <summary>
    /// What will play at <paramref name="sampleRate"/>; null for no sound (the reason is reported).
    /// <paramref name="choice"/>: null for CONFIG.DRK and the game's DOS command line (<paramref name="commandLine"/>: its -A&lt;letter&gt;),
    /// else adlib, sb, sbpro2, mt32, cm32l, speaker or none.
    /// </summary>
    public static SelectedSound? Select(GameFiles files, string? choice, string? romFolder, int sampleRate, Action<string> report,
        IReadOnlyList<string>? commandLine = null)
    {
        if (choice == "none")
        {
            report("no sound (sound card \"none\").");
            return null;
        }
        SoundSetup setup;
        try
        {
            setup = choice is null ? SoundSetup.FromCommandLine(files, commandLine ?? []) : SoundSetup.ForChoice(files, choice);
        }
        catch (Exception e) when (e is ArgumentException or FileNotFoundException)
        {
            report(e.Message + "; no sound.");
            return null;
        }
        IMidiSynth? midi = null;
        if (setup.Kind == DriverKind.Roland)
        {
            var synth = Mt32Synth.TryOpen(RomFolder(romFolder), setup.Module, sampleRate, out var message);
            if (synth is null)
            {
                report($"{WarningPrefix}{setup.TownsDriverName} is the Roland driver, but {message}; the OPL3 FM drivers play instead.");
                if (FmFallback(files, setup.Card, report) is not { } fm)
                {
                    return null;
                }
                setup = fm;
            }
            else
            {
                midi = synth;
                report("MIDI by " + synth.Description);
            }
        }
        if (setup.Kind is not (DriverKind.Fm or DriverKind.Roland or DriverKind.Speaker))
        {
            report(setup.Describe() + ".");
            return null;
        }
        report(setup.Describe() + ".");
        return new SelectedSound(setup, midi);
    }

    /// <summary>
    /// The FM drivers instead of the Roland driver: an OPL3 card keeps its own; any other card (the MT-32 alone included) gets the
    /// Sound Blaster Pro 2's OPL3 drivers with its port, IRQ and DMA, else the Sound Blaster's OPL2 drivers when those files are
    /// missing; null (reported) when neither is there.
    /// </summary>
    private static SoundSetup? FmFallback(GameFiles files, SoundCardSettings card, Action<string> report)
    {
        var opl3 = card.Card is SoundCard.SoundBlasterPro2 or SoundCard.ProAudioSpectrum16 ? card : card with { Card = SoundCard.SoundBlasterPro2 };
        try
        {
            return SoundSetup.ForFmCard(files, opl3);
        }
        catch (FileNotFoundException e)
        {
            report($"{WarningPrefix}{e.Message}; trying the Sound Blaster's OPL2 drivers.");
        }
        try
        {
            return SoundSetup.ForFmCard(files, card with { Card = SoundCard.SoundBlaster });
        }
        catch (FileNotFoundException e)
        {
            report($"{WarningPrefix}{e.Message}; no sound.");
            return null;
        }
    }
}
