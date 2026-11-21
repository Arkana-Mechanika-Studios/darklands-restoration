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
}
