using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Randomness;
using Darklands.Core.Time;
using Darklands.Core.World;

namespace Darklands.Core.Tests;

/// <summary>Small game states for rule tests: the Quickstart party, 414 places, a few items.</summary>
internal sealed class TestData : IGameData
{
    public List<ItemDefinition> Items { get; } = [];

    public byte[] CityBytes { get; } = new byte[CityRecord.Size];

    public IReadOnlyList<ItemDefinition> ItemDefinitions => Items;

    public CityRecord LoadCity(int city) => new(city, CityBytes);
}

internal static class TestGame
{
    public const int Potion = Potions.FirstPotion + 2;

    public static GameState Create(uint seed = 1, int location = 5)
    {
        var data = new TestData();
        for (var code = 0; code < 200; code++)
        {
            data.Items.Add(new ItemDefinition(code, $"item {code}", "", code, 0, 1, 20, 1));
        }
        data.Items[3] = data.Items[3] with { Flags = 0x1, Weight = 4 };          // a hand weapon
        data.Items[40] = data.Items[40] with { Flags = ItemFlags.Mount, Quality = 30 };
        data.Items[Potion] = data.Items[Potion] with { Type = 0x90, Quality = 15, Weight = 1 };
        var state = new GameState(new MscRandom(seed))
        {
            Data = data,
            Party = DefaultParty.Create(7),
            Date = new GameDate(9, 10, 5, 1402),
            CurrentLocation = location,
        };
        for (var i = 0; i < 414; i++)
        {
            state.Locations.Add(new Location { Index = i, X = i % 300, Y = i * 2, Icon = i < Location.CityCount ? 0 : 8 });
        }
        return state;
    }

    /// <summary>A generator in the same state as <paramref name="state"/>'s, to predict its draws.</summary>
    public static MscRandom Twin(GameState state) => new(((MscRandom)state.Random).State);
}
