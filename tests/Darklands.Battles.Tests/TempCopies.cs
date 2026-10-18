namespace Darklands.Battles.Tests;

/// <summary>
/// Temporary folders for copies of the player's files (the original is never written); all deleted when the test run ends.
/// </summary>
internal static class TempCopies
{
    private static readonly List<string> Folders = [];

    static TempCopies() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteAll();

    /// <summary>A new empty folder under the system's temporary folder, deleted at the end of the run.</summary>
    public static string Create(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        lock (Folders)
        {
            Folders.Add(path);
        }
        return path;
    }

    private static void DeleteAll()
    {
        lock (Folders)
        {
            foreach (var folder in Folders)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // a file still held open: left for the system's cleanup
                }
            }
            Folders.Clear();
        }
    }
}
