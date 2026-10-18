namespace Darklands.Cards.Scripts;

/// <summary>"$Pharm01": an apothecary's second card, never finished (see <see cref="PlaceholderScene"/>).</summary>
// orig: R060+0x1674 (199F:0004) matched; tests: CardSmokeTests
public sealed class ApothecaryPlaceholder_Pharm01 : PlaceholderScene
{
    public override int Number => 0x42;

    protected override string MessageFile => "$Pharm01";
}
