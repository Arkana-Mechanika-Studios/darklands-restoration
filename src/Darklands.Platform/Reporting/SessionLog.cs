using System.Globalization;
using System.Text;

namespace Darklands.Platform.Reporting;

/// <summary>
/// The log of one run: a file <c>darklands-yyyyMMdd-HHmmss.log</c> in the logs folder that gets everything written to the
/// console's output and error (and, by <see cref="WriteLine"/>, anything else), each line stamped with the time and flushed at
/// once, so a crash leaves the last lines. The newest <see cref="Keep"/> logs are kept, older ones deleted at the start.
/// New code (host feature); nothing is sent anywhere.
/// </summary>
public sealed class SessionLog : IDisposable
{
    public const int Keep = 5;

    private readonly object _lock = new();
    private StreamWriter? _writer;
    private TextWriter? _oldOut;
    private TextWriter? _oldError;

    private SessionLog(string path, StreamWriter writer)
    {
        Path = path;
        _writer = writer;
    }

    /// <summary>The log file.</summary>
    public string Path { get; }

    /// <summary>
    /// Starts a log in <paramref name="folder"/> (made if missing) with <paramref name="header"/> as its first lines, and keeps the
    /// newest <see cref="Keep"/> logs. Null when no file can be made (nothing is logged then, the program goes on).
    /// </summary>
    public static SessionLog? Start(string folder, IEnumerable<string> header, DateTime? now = null)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            for (var n = 1; n < 100; n++)
            {
                var path = System.IO.Path.Combine(folder, "darklands-" + stamp + (n == 1 ? "" : "-" + n) + ".log");
                try
                {
                    var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    var log = new SessionLog(path, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
                    foreach (var line in header)
                    {
                        log.WriteLine(line);
                    }
                    Prune(folder, Keep);
                    return log;
                }
                catch (IOException) when (File.Exists(path))
                {
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return null;
    }

    /// <summary>The logs in a folder, newest first (the names carry the time).</summary>
    public static IReadOnlyList<string> Logs(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? [.. Directory.EnumerateFiles(folder, "darklands-*.log").OrderByDescending(f => System.IO.Path.GetFileName(f), StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Deletes all but the newest <paramref name="keep"/> logs of the folder.</summary>
    public static void Prune(string folder, int keep)
    {
        foreach (var old in Logs(folder).Skip(keep))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Writes a line (or several) to the log with the time; never throws.</summary>
    public void WriteLine(string text, bool error = false)
    {
        lock (_lock)
        {
            if (_writer is null)
            {
                return;
            }
            try
            {
                var time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
                foreach (var line in text.Replace("\r", "").Split('\n'))
                {
                    _writer.WriteLine(error ? time + " ! " + line : time + "   " + line);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>Makes <see cref="Console.Out"/> and <see cref="Console.Error"/> write here as well as where they wrote.</summary>
    public void CaptureConsole()
    {
        _oldOut = Console.Out;
        _oldError = Console.Error;
        Console.SetOut(TextWriter.Synchronized(new Tee(_oldOut, this, error: false)));
        Console.SetError(TextWriter.Synchronized(new Tee(_oldError, this, error: true)));
    }

    public void Dispose()
    {
        if (_oldOut is not null)
        {
            Console.SetOut(_oldOut);
            Console.SetError(_oldError!);
            _oldOut = _oldError = null;
        }
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    /// <summary>A writer that passes everything on and logs each finished line.</summary>
    private sealed class Tee(TextWriter inner, SessionLog log, bool error) : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            Safely(() => inner.Write(value));
            Collect(value);
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }
            Safely(() => inner.Write(value));
            foreach (var c in value)
            {
                Collect(c);
            }
        }

        public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

        public override void Flush() => Safely(inner.Flush);

        private void Collect(char c)
        {
            if (c == '\n')
            {
                log.WriteLine(_line.ToString().TrimEnd('\r'), error);
                _line.Clear();
            }
            else
            {
                _line.Append(c);
            }
        }

        // the real console may be missing or closed (a program without a console): the log still gets the text
        private static void Safely(Action write)
        {
            try
            {
                write();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }
}
