namespace Darklands.Cards.Scripts;

/// <summary>"$Pawns01": a pawnshop's second card, never finished (see <see cref="PlaceholderScene"/>).</summary>
// orig: R062+0x12F0 (1967:0000) matched; tests: CardSmokeTests
public sealed class PawnshopPlaceholder_Pawns01 : PlaceholderScene
{
    public override int Number => 0x4A;

    protected override string MessageFile => "$Pawns01";
}
