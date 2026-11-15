using Darklands.Presentation.Battles;

namespace Darklands.Presentation;

/// <summary>
/// The tester's traces and the oracle's feeds (tools/oracle sets them in the environment), in the rules and in the screens. Neither
/// reads the environment itself (tests/Darklands.Architecture.Tests): each host calls <see cref="Apply"/> with its environment
/// reader before the game starts, and every hook gets what its variable gives (null, or off, in play).
/// </summary>
public static class TesterHooks
{
    public static void Apply(Func<string, string?> environment)
    {
        // the rules' traces (to standard output) and feeds
        Core.RulesTesterHooks.Apply(environment, Console.WriteLine);
        // the screens: the battle loop's traces and the oracle's timings, the map's trace, the battle tester's arguments
        BattleScreen.FromEnvironment(environment);
        OverlandScreen.FromEnvironment(environment);
        BattleScreens.RequestOverride = environment("DARKLANDS_BATTLE_REQUEST");
    }
}
