using System.Reflection;

namespace Darklands.Workshop.Text;

/// <summary>
/// The Workshop's own version (docs/LOCALISATION.md, "The Workshop's version"), apart from the game's: one source, the build's
/// <c>WorkshopVersion</c> (Directory.Build.props), put into this assembly. Every pack the Workshop saves records it in its
/// <c>pack.json</c> (<see cref="ManifestField"/>), and so does every export; the game ignores it but says it in a problem report.
/// </summary>
// Not in the original: the 1.1 Workshop.
public static class WorkshopVersion
{
    /// <summary>The field of <c>pack.json</c> that names the Workshop that last wrote the pack.</summary>
    public const string ManifestField = "workshop";

    /// <summary>The version, as <c>1.1.0-beta.1</c> ("unknown" when the build did not say it).</summary>
    public static string Current { get; } =
        typeof(WorkshopVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "WorkshopVersion")?.Value is { Length: > 0 } v
            ? v
            : "unknown";

    /// <summary>A beta: the Workshop shows its Beta badge.</summary>
    public static bool IsBeta => Current.Contains("-beta", StringComparison.Ordinal);

    /// <summary>The name and version, as a pack and an export record it: "Darklands Workshop 1.1.0-beta.1".</summary>
    public static string Label => "Darklands Workshop " + Current;

    /// <summary>
    /// The oldest game program the Workshop plays with (the build's <c>WorkshopNeedsGame</c>, 1.1.0): Play in game passes
    /// <c>--language-pack</c>, <c>--saves</c> and <c>--start-at</c>, which an older game refuses as unknown options and quits.
    /// </summary>
    public static string NeedsGame { get; } =
        typeof(WorkshopVersion).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "WorkshopNeedsGame")?.Value is { Length: > 0 } v
            ? v
            : "1.1.0";

    /// <summary>
    /// The game's version as its program says it ("1.0.0", "1.1.0-rc1", "1.1.0+abc"): older than <see cref="NeedsGame"/>?
    /// Only the numbers count (a 1.1.0 release candidate plays with a 1.1.0 Workshop); a version that cannot be read is not
    /// called older (the game is tried as it is).
    /// </summary>
    public static bool IsOlderGame(string? game) =>
        Numbers(game) is { } have && Numbers(NeedsGame) is { } need && have < need;

    /// <summary>
    /// What the Workshop says when the game beside it is older than it needs (<paramref name="game"/> as its program says it), in
    /// plain words: what is wrong, why, and what to do.
    /// </summary>
    public static string OlderGameWords(string game) =>
        $"The game beside the Workshop is version {Clean(game)}, and the Workshop needs version {NeedsGame} or later to play your "
        + $"pack: language packs and starting at a text came with {NeedsGame}, and an older game does not start with them. "
        + $"Download the game's {NeedsGame} release (or a later one) from the project's releases page and unzip it over the old "
        + "one: your saved games and settings stay. Until then the editor works as usual; only Play in game waits.";

    /// <summary>"1.0.0" from "1.0.0-rc6+abc1234".</summary>
    public static string Clean(string? version) => (version ?? "").Split('+')[0].Trim();

    private static Version? Numbers(string? version) =>
        Version.TryParse(Clean(version).Split('-')[0], out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
}
