using Darklands.Audio;
using Darklands.Audio.Mt32;
using Darklands.Content;
using Darklands.Platform;

namespace Darklands.Host.Sdl;

/// <summary>
/// DarklandsRP --sound-test: the sound checked step by step without the game window, each step with a plain verdict. SDL's audio
/// drivers, the default device and its own format; then a tone through each of the three ways the program can feed SDL
/// (<see cref="SdlAudioMode"/>: one beep through the callback, two through the queue, three in the device's native format); then a
/// little of the game's music as the game plays it. For each: what SDL gave when opened, how much it took and the loudest sample,
/// so silence on our side (nothing taken, only zeros, a driver fault, a format mix-up) is told apart from sound that left the
/// program but was not heard (the system's mixer, volume, output).
/// </summary>
internal static class SoundCheck
{
    public static int Run(string? gameFolder, ResolvedSettings settings, TextWriter log)
    {
        var verdicts = new List<string>();
        log.WriteLine("Darklands sound test");
        try
        {
            var (version, compat) = SdlNative.LinkedVersion();
            log.WriteLine($"SDL library: {version}{(compat ? " (sdl2-compat: SDL2 on top of SDL3)" : "")}");
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            log.WriteLine("RESULT: the SDL2 library could not be loaded (" + e.Message + "). Install it (Arch: sudo pacman -S sdl2-compat).");
            return 2;
        }
        foreach (var name in new[] { "SDL_AUDIODRIVER", "SDL_AUDIO_DRIVER", "DARKLANDS_AUDIO_MODE", "PULSE_SERVER", "XDG_RUNTIME_DIR" })
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                log.WriteLine($"  {name}={value}");
            }
        }
        var drivers = Enumerable.Range(0, Math.Max(0, SdlNative.SDL_GetNumAudioDrivers())).Select(i => SdlNative.Text(SdlNative.SDL_GetAudioDriver(i)) ?? "?");
        log.WriteLine("SDL audio drivers built in: " + string.Join(", ", drivers));
        if (SdlNative.SDL_InitSubSystem(SdlNative.InitAudio) != 0)
        {
            log.WriteLine("RESULT: SDL could not start its audio: " + SdlNative.GetErrorText());
            log.WriteLine("        Try: SDL_AUDIODRIVER=pulseaudio SDL_AUDIO_DRIVER=pulseaudio ./DarklandsRP --sound-test");
            return 2;
        }
        log.WriteLine("SDL audio driver in use: " + (SdlNative.Text(SdlNative.SDL_GetCurrentAudioDriver()) ?? "none"));
        var devices = Enumerable.Range(0, Math.Max(0, SdlNative.SDL_GetNumAudioDevices(0))).Select(i => SdlNative.Text(SdlNative.SDL_GetAudioDeviceName(i, 0)) ?? "?").ToList();
        log.WriteLine($"Output devices ({devices.Count}): " + (devices.Count == 0 ? "none listed" : string.Join(" | ", devices)));
        log.WriteLine("Default output device: " + DefaultDevice());
        log.WriteLine("The sound goes to the system's default output device.");

        var results = new Dictionary<SdlAudioMode, bool>
        {
            [SdlAudioMode.Callback] = Tone(log, 1, SdlAudioMode.Callback, verdicts),
            [SdlAudioMode.Queue] = Tone(log, 2, SdlAudioMode.Queue, verdicts),
            [SdlAudioMode.Native] = Tone(log, 3, SdlAudioMode.Native, verdicts),
        };
        var music = Music(log, gameFolder, settings, verdicts);
        SdlNative.SDL_QuitSubSystem(SdlNative.InitAudio);

        log.WriteLine();
        foreach (var verdict in verdicts)
        {
            log.WriteLine(verdict);
        }
        log.WriteLine();
        if (results.Values.All(r => r) && music != false)
        {
            log.WriteLine("RESULT: every step handed sound to SDL normally. What did you hear?");
            log.WriteLine("  - 1 beep, 2 beeps, 3 beeps" + (music is null ? "" : " and the music") + ": sound works.");
            log.WriteLine("  - not the 1 beep: play with the way you did hear, e.g. DARKLANDS_AUDIO_MODE=queue ./DarklandsRP ~/Games/DARKLAND (2 beeps)");
            log.WriteLine("    or DARKLANDS_AUDIO_MODE=native ./DarklandsRP ~/Games/DARKLAND (3 beeps), and send this log.");
            log.WriteLine("  - nothing at all: the system did not play it: check the volume and the default output (wpctl status), and send this log.");
            return 0;
        }
        var working = results.Where(r => r.Value).Select(r => r.Key.ToString().ToLowerInvariant()).ToList();
        log.WriteLine(working.Count > 0
            ? $"RESULT: a step failed (see its line). The way(s) that worked: {string.Join(", ", working)}: try DARKLANDS_AUDIO_MODE={working[0]} ./DarklandsRP ..., and send this whole output."
            : "RESULT: a step failed (see its line): send this whole output.");
        return 1;
    }

    /// <summary>The default output device's name and own format (SDL 2.24 and later), or why not known.</summary>
    private static string DefaultDevice()
    {
        try
        {
            if (SdlNative.SDL_GetDefaultAudioInfo(out var name, out var spec, 0) != 0)
            {
                return "not known (" + SdlNative.GetErrorText() + ")";
            }
            return $"{SdlNative.TakeString(name) ?? "?"}: {spec.Freq} Hz, {spec.Channels} channels, format 0x{spec.Format:X4}";
        }
        catch (EntryPointNotFoundException)
        {
            return "not known (SDL older than 2.24)";
        }
    }

    /// <summary>Beeps through SDL in one mode: true when SDL took about the right amount of sound, not silence.</summary>
    private static bool Tone(TextWriter log, int beeps, SdlAudioMode mode, List<string> verdicts)
    {
        var name = mode.ToString().ToLowerInvariant();
        var what = beeps == 1 ? "1 beep" : $"{beeps} beeps";
        log.WriteLine();
        log.WriteLine($"Step {beeps}: {what} through SDL's {name} way. Listen now.");
        var output = SdlAudioOutput.TryOpen(out var error, mode);
        if (output is null)
        {
            verdicts.Add($"Step {beeps} ({name}): FAILED, SDL could not open the audio device: {error}");
            log.WriteLine("  could not open the audio device: " + error);
            return false;
        }
        using (output)
        {
            log.WriteLine("  opened: " + output.Description);
            output.Start(new Beeps(output.SampleRate, beeps));
            Thread.Sleep(2500);
            var frames = output.FramesPulled;
            var peak = output.TakePeak();
            var status = output.Status;
            var expected = (int)(output.SampleRate * 2.5);
            log.WriteLine($"  SDL took {frames} frames in 2.5 s (about {expected} expected), loudest sample written {peak} of 32767, device {status}");
            if (frames < expected / 4)
            {
                verdicts.Add($"Step {beeps} ({name}): FAILED, SDL took {(frames == 0 ? "no" : "too little")} sound ({frames} frames, device {status}).");
                return false;
            }
            verdicts.Add($"Step {beeps} ({name}): sound handed to SDL ({output.Driver}) normally: did you hear {what}?");
            return true;
        }
    }

    /// <summary>The game's music as the game would play it: true / false (sound or silence), null when not tried (no game folder).</summary>
    private static bool? Music(TextWriter log, string? gameFolder, ResolvedSettings settings, List<string> verdicts)
    {
        log.WriteLine();
        log.WriteLine($"Step 4: the game's own music (8 seconds), as the game plays it ({SdlAudioOutput.ModeAsked.ToString().ToLowerInvariant()} way).");
        if (gameFolder is null)
        {
            log.WriteLine("  skipped: no game folder (give it: ./DarklandsRP --sound-test ~/Games/DARKLAND).");
            verdicts.Add("Step 4 (game music): not tried, no game folder given or found.");
            return null;
        }
        if (settings.NoAudio)
        {
            verdicts.Add("Step 4 (game music): FAILED, the settings turn the sound off (sound.card \"none\" in darklands.settings.json, or --no-audio).");
            return false;
        }
        var output = SdlAudioOutput.TryOpen(out var error);
        if (output is null)
        {
            verdicts.Add("Step 4 (game music): FAILED, SDL could not open the audio device: " + error);
            return false;
        }
        using (output)
        {
            log.WriteLine("  opened: " + output.Description);
            log.WriteLine("  game folder: " + gameFolder);
            var files = new GameFiles(gameFolder);
            if (settings.ConfigDrk is { } config)
            {
                files.OverrideConfig(config.Card, config.Port, config.Irq, config.Dma);
            }
            SelectedSound? sound;
            try
            {
                sound = SoundSelection.Select(files, settings.SoundChoice, settings.Mt32Roms, output.SampleRate, line => log.WriteLine("  " + line), settings.CommandLine);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                verdicts.Add("Step 4 (game music): FAILED, the sound drivers could not be read: " + e.Message);
                return false;
            }
            if (sound is null)
            {
                verdicts.Add("Step 4 (game music): FAILED, no sound chosen (see the lines of step 4).");
                return false;
            }
            using (sound)
            {
                SynthAudio audio;
                try
                {
                    audio = sound.Start(files, output);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException)
                {
                    verdicts.Add("Step 4 (game music): FAILED, the sound drivers did not start: " + e.Message);
                    return false;
                }
                audio.PlayMusic(GameSounds.Ready);
                audio.PlayMusic(0x14);
                for (var second = 2; second <= 8; second += 2)
                {
                    Thread.Sleep(2000);
                    log.WriteLine($"  {second} s: SDL took {output.FramesPulled} frames, loudest sample written {output.TakePeak()} of 32767{(audio.Fault is { } f ? ", driver fault: " + f : "")}");
                }
                var frames = output.FramesPulled;
                audio.PlayMusic(GameSounds.Stop);
                if (frames < output.SampleRate * 2)
                {
                    verdicts.Add($"Step 4 (game music): FAILED, SDL took too little sound ({frames} frames).");
                    return false;
                }
                if (audio.Fault is { } fault)
                {
                    verdicts.Add("Step 4 (game music): FAILED, the sound driver stopped: " + fault + " (a fault on our side: send this log).");
                    return false;
                }
                verdicts.Add($"Step 4 (game music): music handed to SDL ({audio.Setup.Describe()}): did you hear it? (a loudest sample of 0 above would be silence on our side)");
                return true;
            }
        }
    }

    /// <summary>A number of short beeps (0.25 s at 440 Hz, a quarter of full scale), then silence.</summary>
    private sealed class Beeps(int rate, int count) : IPcmSource
    {
        private long _frame;

        public void Render(Span<short> interleavedStereo)
        {
            for (var i = 0; i + 1 < interleavedStereo.Length; i += 2)
            {
                var t = (double)_frame++ / rate;
                var slot = (int)(t / 0.5);
                var on = slot < count && t - slot * 0.5 < 0.25;
                var v = on ? (short)(8000 * Math.Sin(2 * Math.PI * 440 * t)) : (short)0;
                interleavedStereo[i] = v;
                interleavedStereo[i + 1] = v;
            }
        }
    }
}

/// <summary>
/// The game's sound watched as it plays: a line on the error output when SDL takes no sound in the first seconds and when the
/// drivers fault, and a summary 20 seconds in (how much sound went out, its loudest sample), so a silent game says why in its log.
/// </summary>
internal sealed class SoundWatch : IDisposable
{
    private readonly SdlAudioOutput _output;
    private readonly SynthAudio _audio;
    private readonly Timer _timer;
    private int _seconds;
    private int _peak;
    private bool _faultSaid;

    public SoundWatch(SdlAudioOutput output, SynthAudio audio)
    {
        _output = output;
        _audio = audio;
        _timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void Check()
    {
        _seconds += 2;
        _peak = Math.Max(_peak, _output.TakePeak());
        if (_audio.Fault is { } fault && !_faultSaid)
        {
            _faultSaid = true;
            Console.Error.WriteLine("Sound: the sound driver stopped (" + fault + "); silence follows. Please send this log.");
        }
        if (_seconds == 4 && _output.FramesPulled == 0)
        {
            Console.Error.WriteLine($"Sound: SDL has taken no sound in 4 seconds ({_output.Driver}, device {_output.Status}): nothing will be heard. "
                + "Try DARKLANDS_AUDIO_MODE=queue or native, and ./DarklandsRP --sound-test.");
        }
        if (_seconds == 20)
        {
            Console.WriteLine($"Sound: in the first 20 s {(double)_output.FramesPulled / _output.SampleRate:0.0} s of sound went to SDL ({_output.Driver}), "
                + $"loudest sample {_peak} of 32767" + (_peak == 0 ? " (only silence: music off with Alt+M? else please send this log)." : "."));
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void Dispose() => _timer.Dispose();
}
