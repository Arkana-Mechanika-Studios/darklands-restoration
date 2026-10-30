namespace Darklands.Core;

/// <summary>
/// A mod a game was played with: its id (the mod's folder name), its name and its version. Not part of the game's state
/// nor of the original save format: a saved game's companion file records the marks (Darklands.Persistence.ModdedSaves).
/// </summary>
public sealed record ModMark(string Id, string Name, string Version)
{
    public override string ToString() => Version.Length > 0 ? $"{Name} {Version}" : Name;
}
