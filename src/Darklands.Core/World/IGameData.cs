using Darklands.Core.Items;

namespace Darklands.Core.World;

/// <summary>
/// The fixed game data the rules need (Core reads no files): the item list and the city
/// records. Darklands.Content provides it from the player's game files.
/// </summary>
public interface IGameData
{
    /// <summary>The item list, by item code.</summary>
    IReadOnlyList<ItemDefinition> ItemDefinitions { get; }

    /// <summary>A fresh copy of a city's DARKLAND.CTY record (<c>city-read</c>).</summary>
    CityRecord LoadCity(int city);

    /// <summary>
    /// What a read of <paramref name="length"/> bytes at <paramref name="offset"/> of DARKLAND.CTY
    /// returns (shorter at the end of the file, empty past it); null without the file.
    /// </summary>
    byte[]? CityFileBytes(long offset, int length) => null;
}

/// <summary>No game data: items are unknown (weight 0, no flags) and cities empty.</summary>
public sealed class NoGameData : IGameData
{
    public static readonly NoGameData Instance = new();

    public IReadOnlyList<ItemDefinition> ItemDefinitions { get; } = [];

    public CityRecord LoadCity(int city) => new(city, new byte[CityRecord.Size]);
}
