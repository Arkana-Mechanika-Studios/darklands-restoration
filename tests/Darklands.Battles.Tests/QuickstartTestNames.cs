using Darklands.Core.Characters;

namespace Darklands.Battles.Tests;

/// <summary>The Quickstart members' names as the EXE holds them (test data: the game hands in OriginalWords.Quickstart).</summary>
internal static class QuickstartTestNames
{
    public static IReadOnlyList<QuickstartName> All { get; } =
        [new("Gretchen Wilburg", "Gretch"), new("Gunther Langer", "Gunther"), new("Hans Muller", "Hans"), new("Ebhard of Achdorf", "Ebhard")];
}
