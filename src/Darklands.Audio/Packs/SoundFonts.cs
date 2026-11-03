using MeltySynth;

namespace Darklands.Audio.Packs;

/// <summary>A loaded SoundFont (.sf2): the instruments General MIDI files are played with.</summary>
/// <remarks>New code (replacement sound, not part of the game). Synthesis is MeltySynth's (MIT, managed code only).</remarks>
public sealed class SoundFontBank
{
    /// <summary>The longest piece rendered (a longer file is skipped, so that one file cannot fill the memory).</summary>
    public static readonly TimeSpan LongestPiece = TimeSpan.FromMinutes(10);

    private readonly SoundFont _font;

    private SoundFontBank(string path, SoundFont font)
    {
        Path = path;
        _font = font;
    }

    public string Path { get; }

    /// <exception cref="InvalidDataException">The file is not a SoundFont this reads.</exception>
    public static SoundFontBank Load(string path)
    {
        try
        {
            return new SoundFontBank(path, new SoundFont(path));
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or EndOfStreamException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("not a SoundFont (" + e.Message + ")", e);
        }
    }

    /// <summary>Plays the whole file through the SoundFont into a stereo clip at <paramref name="sampleRate"/>; the silence after the last note is cut.</summary>
    /// <exception cref="InvalidDataException">The file is not a MIDI file, or is longer than <see cref="LongestPiece"/>.</exception>
    public AudioClip RenderMidi(string midiPath, int sampleRate)
    {
        MidiFile midi;
        try
        {
            midi = new MidiFile(midiPath);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or NotSupportedException or EndOfStreamException)
        {
            throw new InvalidDataException("not a MIDI file (" + e.Message + ")", e);
        }
        if (midi.Length > LongestPiece)
        {
            throw new InvalidDataException($"longer than {LongestPiece.TotalMinutes:0} minutes");
        }
        var synthesizer = new Synthesizer(_font, new SynthesizerSettings(sampleRate));
        var sequencer = new MidiFileSequencer(synthesizer);
        sequencer.Play(midi, false);
        // the last notes ring out (release, reverb) after the file's end
        var frames = (int)Math.Ceiling((midi.Length.TotalSeconds + 2.0) * sampleRate);
        var left = new float[frames];
        var right = new float[frames];
        sequencer.Render(left, right);
        var last = frames;
        while (last > 0 && Math.Abs(left[last - 1]) < 1e-4f && Math.Abs(right[last - 1]) < 1e-4f)
        {
            last--;
        }
        var stereo = new float[last * 2];
        for (var f = 0; f < last; f++)
        {
            stereo[2 * f] = left[f];
            stereo[2 * f + 1] = right[f];
        }
        return AudioClip.FromStereo(stereo, sampleRate);
    }
}

/// <summary>Where the SoundFont for .mid cues comes from (docs/PACKS.md): the pack's own, else the one chosen, else the default folder.</summary>
public static class SoundFontLocator
{
    /// <summary>The default folder's name, next to the program (like MT32_ROMS).</summary>
    public const string FolderName = "SOUNDFONT";

    /// <summary>The first .sf2 (by name) in the SOUNDFONT folder of <paramref name="programFolder"/>; null when there is none.</summary>
    public static string? DefaultIn(string programFolder)
    {
        var folder = Path.Combine(programFolder, FolderName);
        if (!Directory.Exists(folder))
        {
            return null;
        }
        try
        {
            return Directory.EnumerateFiles(folder, "*.sf2").Order(StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The SoundFont to use, in this order: <paramref name="packChoice"/> (pack.json "soundfont", relative to <paramref name="packFolder"/>),
    /// <paramref name="chosen"/> (--soundfont or the settings file), the default folder. A named file that does not exist is reported to
    /// <paramref name="problem"/> and the next one is tried. Null when none is found.
    /// </summary>
    public static string? Find(string? packChoice, string? packFolder, string? chosen, string programFolder, Action<string> problem)
    {
        if (!string.IsNullOrWhiteSpace(packChoice))
        {
            var path = Path.GetFullPath(Path.Combine(packFolder ?? "", packChoice));
            if (File.Exists(path))
            {
                return path;
            }
            problem($"SoundFont: the pack names {path}, which does not exist");
        }
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var path = Path.GetFullPath(chosen);
            if (File.Exists(path))
            {
                return path;
            }
            problem($"SoundFont: {path} (--soundfont or the settings file) does not exist");
        }
        return DefaultIn(programFolder);
    }
}
