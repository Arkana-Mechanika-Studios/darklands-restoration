namespace Darklands.Platform;

/// <summary>
/// The project's addresses, one source for the launcher and the Workshop: its website, its repository on GitHub (the source,
/// the releases, the problem reports, the guides) and its Discord.
/// </summary>
// Not in the original: the restoration's own links.
public static class ProjectLinks
{
    /// <summary>The project's website.</summary>
    public const string Website = "https://arkana-mechanika-labs.github.io/";

    /// <summary>The repository on GitHub.</summary>
    public const string Repository = "https://github.com/Arkana-Mechanika-Studios/darklands-restoration";

    /// <summary>The releases page (the release notes and the downloads).</summary>
    public const string Releases = Repository + "/releases";

    /// <summary>The guide to making a pack of pictures, music and sounds.</summary>
    public const string PackGuide = Repository + "/blob/master/docs/PACKS.md";

    /// <summary>The translator's guide (the Workshop, step by step; a copy is in the Workshop's docs folder).</summary>
    public const string TranslatorGuide = Repository + "/blob/master/docs/TRANSLATING.md";

    /// <summary>The project's Discord (questions, help, ideas).</summary>
    public const string Discord = "https://discord.gg/HjzWvmHhqZ";
}
