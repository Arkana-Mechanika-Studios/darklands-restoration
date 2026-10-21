using Darklands.Audio.Mt32;
using Darklands.Content;
using Darklands.Launcher;

namespace Darklands.Host.Sdl;

/// <summary>
/// The launcher's sound test: a few seconds of the game's music (the character creation's piece) through the drivers the game
/// would use, on the card, game folder and ROMs the launcher shows. It starts in the background (opening the audio device and
/// the MT-32 takes a moment), so the launcher answers at once. One test plays at a time; the next, or closing, stops it.
/// </summary>
/// <param name="commandLine">The game's DOS command line (its -A&lt;letter&gt; chooses the driver files, as in the game).</param>
/// <param name="changed">Called when the test starts playing, fails or stops (from any thread): the host wakes the launcher.</param>
internal sealed class SoundTest(IReadOnlyList<string> commandLine, Action changed) : ISoundTest, IDisposable
{
    /// <summary>The piece played: the character creation's (no sampled clips, so every card plays it alike).</summary>
    private const int Piece = 0x14;

    /// <summary>The piece ends itself after this.</summary>
    private static readonly TimeSpan Length = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private int _generation;
    private bool _starting;
    private string? _status;
    private (SdlAudioOutput Output, SelectedSound Sound, Timer Stop)? _playing;

    public bool Playing
    {
        get
        {
            lock (_lock)
            {
                return _starting || _playing is not null;
            }
        }
    }

    public string? Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public void Play(SoundTestRequest request)
    {
        Stop(notify: false);
        int generation;
        lock (_lock)
        {
            generation = ++_generation;
            _starting = true;
            _status = "Starting the music…";
        }
        if (request.Card == "none")
        {
            Finish(generation, null, "No sound: the sound card is \"No sound\".");
            return;
        }
        Task.Run(() => Start(request, generation));
    }

    private void Start(SoundTestRequest request, int generation)
    {
        var output = SdlAudioOutput.TryOpen(out var error);
        if (output is null)
        {
            Finish(generation, null, "No audio device: " + error);
            return;
        }
        var report = new List<string>();
        try
        {
            var files = new GameFiles(request.GameFolder);
            var sound = SoundSelection.Select(files, request.Card == "config" ? null : request.Card, request.RomFolder, output.SampleRate,
                report.Add, commandLine);
            if (sound is null)
            {
                output.Dispose();
                Finish(generation, null, "No sound: " + (report.LastOrDefault() ?? "nothing to play."));
                return;
            }
            var audio = sound.Start(files, output);
            var warning = report.FirstOrDefault(SoundSelection.IsWarning);
            var status = warning is null ? "Playing a little of the game's music." : "Playing, but " + warning[SoundSelection.WarningPrefix.Length..];
            Finish(generation, (output, sound, audio), status);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            output.Dispose();
            Finish(generation, null, "No sound: " + e.Message);
        }
    }

    /// <summary>The start's outcome: playing from now, or why not; dropped when the test was stopped (or started again) meanwhile.</summary>
    private void Finish(int generation, (SdlAudioOutput Output, SelectedSound Sound, Darklands.Audio.SynthAudio Audio)? started, string status)
    {
        lock (_lock)
        {
            if (generation != _generation)
            {
                started?.Output.Dispose();
                started?.Sound.Dispose();
                return;
            }
            _starting = false;
            _status = status;
            if (started is { } s)
            {
                s.Audio.PlayMusic(Darklands.Audio.GameSounds.Ready);
                s.Audio.PlayMusic(Piece);
                _playing = (s.Output, s.Sound, new Timer(_ => Stop(), null, Length, Timeout.InfiniteTimeSpan));
            }
        }
        changed();
    }

    /// <summary>Stops the test (starting or playing), if any.</summary>
    public void Stop() => Stop(notify: true);

    private void Stop(bool notify)
    {
        lock (_lock)
        {
            if (!_starting && _playing is null && _status is null)
            {
                return;
            }
            _generation++;
            _starting = false;
            _status = null;
            if (_playing is { } playing)
            {
                playing.Stop.Dispose();
                playing.Output.Dispose();
                playing.Sound.Dispose();
                _playing = null;
            }
        }
        if (notify)
        {
            changed();
        }
    }

    public void Dispose() => Stop(notify: false);
}
