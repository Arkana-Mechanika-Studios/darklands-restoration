using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Content.Formats.Exe;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Randomness;

namespace Darklands.Oracle.Tests;

/// <summary>
/// FixEnemyGroupCompaction (a gameplay fix of the Fixed rule set): groups-repair drops every enemy group after the first empty one;
/// fixed, only the empty groups go. Both paths on the same groups, with the player's DARKLAND.ENM (skipped without the game).
/// </summary>
public sealed class EnemyGroupCompactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEmptyGroupTakesTheLaterGroupsWithItOrGoesAlone(bool fixedGame)
    {
        var folder = Darklands.Platform.LocalPaths.Get("game", "DARKLANDS_DIR") ?? "";
        if (!GameFiles.IsGameFolder(folder))
        {
            return;
        }
        var files = new GameFiles(folder);
        var catalogue = EnemyCatalogueDecoder.Decode(files.Read("DARKLAND.ENM"));
        var items = WorldData.Load(files).ItemDefinitions;
        var tables = CombatTablesReader.Read(DarklandsExecutable.Load(files));
        var forces = new BattlefieldForces(catalogue, items, tables, new MscRandom(7))
        {
            Behaviour = new OriginalBehaviour { FixEnemyGroupCompaction = fixedGame },
        };
        var kind = forces.AddKind(0, 0, 0);
        Assert.Equal(0, kind);
        EnemyGroup Group(int count)
        {
            var g = new EnemyGroup();
            g.Kinds[0] = (sbyte)(count > 0 ? kind : -1);
            g.Counts[0] = (sbyte)count;
            return g;
        }
        var (first, empty, third) = (Group(2), Group(0), Group(1));
        forces.Groups[0].AddRange([first, empty, third]);
        forces.FixGroups(1);
        Assert.Equal(fixedGame ? [first, third] : [first], forces.Groups[0]);
    }
}
