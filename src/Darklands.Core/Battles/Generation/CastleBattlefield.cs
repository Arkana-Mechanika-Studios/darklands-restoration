namespace Darklands.Core.Battles.Generation;

/// <summary>
/// Castle and keep battlefields (overlays R005, R006 and R007): a stack of up to five levels (a cellar, the ground floor with the
/// keep in its yard, upper floors, a battlement level) planned room by room, then drawn as floor images with doors, stairs,
/// furniture, chests and the enemy groups the battlefield type asks for. The planner is <see cref="CastleGenerator"/>.
/// </summary>
// orig: layout-battlefield-entry R005+0x0000 (1432:0000) draft and castle-builder-entry
// R007+0x0000 (15AF:0000) draft (drafts "COMPLETE" but mostly unmatched; checked against the asm
// where the oracle disagreed); tests: Oracle:castle.json
public static class CastleBattlefield
{
    /// <summary>Builds the battlefield; returns the number of levels and sets the level the party starts on.</summary>
    public static int Build(GenerationState g, int battlefield, out int start, (int Type, int Quality, int Count)[] specs)
    {
        var generator = new CastleGenerator(g);
        var levels = generator.Plan(battlefield);
        var groups = specs.Select(s => new GroupSpec(s.Type, s.Quality, s.Count)).ToArray();
        start = generator.BuildLevels(battlefield, groups);
        return levels;
    }
}
