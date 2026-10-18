namespace Darklands.Cards.Scripts;

/// <summary>"$MineR09": the kobolds' rusty iron door. They greet a party that promised to help them; they attack one that refused.</summary>
// orig: R103+0x0344 (186C:0004) matched; tests: CardSmokeTests
// DORMANT: R103+0x058C, R103+0x05D2 (fight handlers) and the reset
// R103+0x0618 are in the file but never called (the card reads the choice directly).
public sealed class RustyIronDoor_MineR09 : MineRealmDoor
{
    public override int Number => 0xED;

    protected override int Welcome => PromisedTheKobolds;

    protected override int Enmity => RefusedTheKobolds;

    protected override int FolkPicture => 212;

    protected override (int First, int Second) EnvoyPictures => (203, 207);
}
