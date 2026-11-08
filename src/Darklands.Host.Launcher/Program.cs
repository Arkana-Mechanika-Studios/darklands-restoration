using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Darklands.Host.Launcher;

/// <summary>
/// DarklandsRP Launcher: the launcher (the settings screen) as a program of its own. It starts DarklandsRP.exe, beside it, with
/// --setup and whatever it was given, and ends: DarklandsRP shows the launcher, and its Play starts the game in the same window.
/// DarklandsRP.exe started directly goes straight to the game (unless the launcher is needed: a first run, no game found ...).
/// </summary>
internal static class Program
{
    private const string Title = "Darklands Restoration Project";

    [STAThread]
    private static int Main(string[] args)
    {
        var program = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DarklandsRP.exe");
        if (!File.Exists(program))
        {
            return Fail(program + " was not found. DarklandsRP Launcher starts it, so the two must stay side by side in the program's folder.");
        }
        var start = new ProcessStartInfo(program, string.Join(" ", new[] { "--setup" }.Concat(args).Select(Quoted)))
        {
            UseShellExecute = false,
        };
        try
        {
            using (Process.Start(start))
            {
                return 0;
            }
        }
        catch (Exception e) when (e is Win32Exception || e is IOException || e is InvalidOperationException)
        {
            return Fail(program + " could not be started: " + e.Message);
        }
    }

    /// <summary>An argument as Windows programs read their command line back (quotes and the backslashes before them escaped).</summary>
    internal static string Quoted(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
        {
            return argument;
        }
        var quoted = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static int Fail(string message)
    {
        MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return 1;
    }
}
