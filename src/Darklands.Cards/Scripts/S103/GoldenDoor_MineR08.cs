namespace Darklands.Cards.Scripts;

/// <summary>"$MineR08": the dwarfs' golden door. They greet a party that refused the kobolds; they attack one that promised to help them.</summary>
// orig: R103+0x0000 (1838:0000) matched; tests: CardSmokeTests
// DORMANT: R103+0x0248, R103+0x028E (fight handlers) and the reset
// R103+0x02D4 are in the file but never called (the card reads the choice directly).
public sealed class GoldenDoor_MineR08 : MineRealmDoor
{
    public override int Number => 0xEC;

    protected override int Welcome => RefusedTheKobolds;

    protected override int Enmity => PromisedTheKobolds;

    protected override int FolkPicture => 214;

    protected override (int First, int Second) EnvoyPictures => (212, 201);
}
