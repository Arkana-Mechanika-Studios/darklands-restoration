using Mt32emu;

namespace Darklands.Audio.Mt32;

/// <summary>
/// A Roland MT-32 or CM-32L for the Roland driver's MIDI: Munt's mt32emu (a separate LGPL
/// library, through the Mt32emu.net binding) with the player's own ROM files.
/// </summary>
public sealed class Mt32Synth : IMidiSynth
{
    private readonly Mt32Context _context;

    private Mt32Synth(Mt32Context context, string description)
    {
        _context = context;
        Description = description;
    }

    /// <summary>The ROMs in use, as mt32emu identified them.</summary>
    public string Description { get; }

    /// <summary>
    /// Opens the module with the control and PCM ROMs of <paramref name="module"/> found under
    /// <paramref name="romFolder"/> (see <see cref="Mt32Roms.Find"/>); null (and the reason) without them.
    /// </summary>
    public static Mt32Synth? TryOpen(string romFolder, RolandModule module, int sampleRate, out string message)
    {
        var files = Mt32Roms.Find(romFolder, module, out message);
        if (files is null)
        {
            return null;
        }
        Mt32Context? context = null;
        try
        {
            context = new Mt32Context(new QuietReports());
            foreach (var file in files)
            {
                context.AddRom(file);
            }
            context.AnalogOutputMode = Mt32GlobalState.GetBestAnalogOutputMode(sampleRate);
            context.SetSampleRate(sampleRate);
            context.OpenSynth();
            var info = context.GetRomInfo();
            message = $"{info.ControlRomDescription} / {info.PcmRomDescription} ({Path.GetDirectoryName(files[0])}), mt32emu {Mt32GlobalState.LibraryVersionText}";
            return new Mt32Synth(context, message);
        }
        catch (Exception e) when (e is Mt32EmuException or IOException or DllNotFoundException or BadImageFormatException)
        {
            context?.Dispose();
            message = "MT-32 emulation unavailable: " + e.Message;
            return null;
        }
    }

    public void PlayShortMessage(uint message) => _context.PlayMessage(message);

    public void PlaySysex(ReadOnlySpan<byte> sysex) => _context.PlaySysex(sysex);

    public void Render(Span<short> interleavedStereo) => _context.Render(interleavedStereo);

    public void Dispose() => _context.Dispose();
}

/// <summary>The module's messages (its LCD, MIDI errors) are not printed.</summary>
internal sealed class QuietReports : IMt32ReportHandler
{
    public void PrintDebug(string? message) { }
    public void OnErrorControlROM() { }
    public void OnErrorPCMROM() { }
    public void ShowLCDMessage(string? message) { }
    public void OnMIDIMessagePlayed() { }
    public bool OnMIDIQueueOverflow() => false;
    public void OnMIDISystemRealtime(byte data) { }
    public void OnDeviceReset() { }
    public void OnDeviceReconfig() { }
    public void OnNewReverbMode(byte mode) { }
    public void OnNewReverbTime(byte time) { }
    public void OnNewReverbLevel(byte level) { }
    public void OnPolyStateChanged(byte partNumber) { }
    public void OnProgramChanged(byte partNumber, string? soundGroupName, string? patchName) { }
}

/// <summary>
/// Finds a module's ROM files. The folder holds them directly, or in one subfolder per module
/// (e.g. MT32, CM32L, CM32L-D); a ROM file is told apart by
/// its name (control or PCM, MT-32 or CM-32L).
/// </summary>
public static class Mt32Roms
{
    public static string[]? Find(string folder, RolandModule module, out string message)
    {
        if (!Directory.Exists(folder))
        {
            message = $"MT-32 ROM folder not found: {folder}";
            return null;
        }
        var candidates = new List<string>();
        var wanted = module == RolandModule.Cm32l ? new[] { "CM32L", "CM-32L", "CM32L-D", "LAPC" } : new[] { "MT32", "MT-32" };
        foreach (var name in wanted)
        {
            candidates.AddRange(Directory.EnumerateDirectories(folder)
                .Where(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase)));
        }
        candidates.Add(folder); // a module's own subfolder first, the folder itself last
        foreach (var directory in candidates)
        {
            var roms = Directory.EnumerateFiles(directory, "*.rom", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                .Where(f => IsFor(Path.GetFileName(f), module))
                .ToArray();
            var control = roms.FirstOrDefault(f => Path.GetFileName(f).Contains("ctrl", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(f).Contains("control", StringComparison.OrdinalIgnoreCase));
            var pcm = roms.FirstOrDefault(f => Path.GetFileName(f).Contains("pcm", StringComparison.OrdinalIgnoreCase));
            if (control is not null && pcm is not null)
            {
                message = "";
                return [control, pcm];
            }
        }
        message = $"no {(module == RolandModule.Cm32l ? "CM-32L" : "MT-32")} control and PCM ROM files under {folder}";
        return null;
    }

    private static bool IsFor(string name, RolandModule module)
    {
        var cm32l = name.Contains("cm32l", StringComparison.OrdinalIgnoreCase) || name.Contains("cm-32l", StringComparison.OrdinalIgnoreCase);
        return module == RolandModule.Cm32l ? cm32l : !cm32l;
    }
}
