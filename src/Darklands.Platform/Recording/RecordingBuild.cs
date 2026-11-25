using System.Reflection;

namespace Darklands.Platform.Recording;

/// <summary>
/// The build a recording is made and played with: the program's version and the commit it was built from. A recording is played
/// only by the build that made it (another build's game may take another way at the same inputs: the replay would part, or worse,
/// seem to agree for a while); a replay with another build is refused, and says which build to use.
/// </summary>
// Not in the original: R-1.
public static class RecordingBuild
{
    private static readonly Assembly Assembly = typeof(RecordingBuild).Assembly;

    /// <summary>The program's version ("1.2.0"), without the build's suffix.</summary>
    public static string Version { get; } =
        (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown").Split('+')[0];

    /// <summary>The commit the build was made from (the build's stamp), or "unknown".</summary>
    public static string Commit { get; } =
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "Commit")?.Value is { Length: > 0 } c ? c : "unknown";

    /// <summary>
    /// Why this build may not play a recording made by <paramref name="program"/> at <paramref name="commit"/>, or null when it may (the
    /// same version, and the same commit when both know theirs).
    /// </summary>
    public static string? Refusal(string program, string commit, string version, string ownCommit)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(commit);
        var sameCommit = commit is "" or "unknown" || ownCommit is "" or "unknown" || commit == ownCommit;
        return program == version && sameCommit ? null
            : $"the recording was made by Darklands Restoration {program} (commit {(commit.Length == 0 ? "unknown" : commit)}); this is {version} (commit {ownCommit}): play it with the build that made it";
    }
}
