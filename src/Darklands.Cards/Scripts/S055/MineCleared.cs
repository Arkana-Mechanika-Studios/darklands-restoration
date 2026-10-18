namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineE00": the reward after the mine. When the mine was cleared (no fact 0x59 of quest 0x0C
/// exists and the last battle was not lost, fled or surrendered) the miners give ten kinds of
/// mineral, the standing of the nearest city rises and the quest is resolved; otherwise the
/// miners trudge off. Either way the mine can not be entered again and the party returns to the map.
/// </summary>
// orig: R055+0x0D08 (1908:0008) matched; tests: CardSmokeTests
public sealed class MineCleared : Card
{
    public const int CardNumber = 0x2E;

    private const int RewardPicture = 132, DefeatPicture = 209;

    private const int MineQuest = 0x0C, MineFact = 0x59, OverlandMap = 0x0C, CannotEnter = 0x62;

    /// <summary>The ten minerals, in the order they are handed over.</summary>
    private static readonly int[] Minerals = [0x48, 0x4F, 0x49, 0x4D, 0x4E, 0x50, 0x57, 0x58, 0x59, 0x5A];

    public override int Number => CardNumber;

    // orig: R055+0x0D08 (1908:0008) matched
    // orig: R055+0x0F30 (1908:0230) matched (init helper: clears the option flags, installs the "screwed up." handler on all 20 lines, empties the item and saint slots; nothing calls it)
    // DORMANT: R055+0x0F30 and R055+0x0F68 (1908:0268, the "screwed up." handler): the card never reaches its init helper and has no menu, so neither runs.
    // DORMANT: texts 1 and 3 (the Fugger banker's reply) are not shown by this card.
    // C: the original loads the card with the "near" loader in the first branch (no card set-up call); the C# shows the card with that picture.
    public override int Run(CardContext c)
    {
        var lastBattle = c.LastBattleResult;
        if (!c.Events.FactAnywhere(MineQuest, MineFact) && lastBattle != 3 && lastBattle != 4 && lastBattle != 2)
        {
            Show(c, RewardPicture);
            c.ResolveQuest(MineQuest, c.Here);
            c.Screen.Text(0);
            c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), 0x19, 0x32);
            foreach (var mineral in Minerals)
            {
                c.AddItems(c.Leader, mineral, c.Random(5) + 10);
            }
        }
        else
        {
            Show(c, DefeatPicture);
            c.Screen.Text(2);
        }
        // The mine is closed for good. DIVERGENCE: guard for a party that is not at a location.
        if (c.Here >= 0 && c.Here < c.State.Locations.Count)
        {
            c.State.Locations[c.Here].ArrivalCard = CannotEnter;
        }
        return OverlandMap;
    }
}
