using System.Reflection;
using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;

namespace Darklands.Battles.Tests;

/// <summary>
/// OriginalBehaviour.FixPlaceActionsOnOwnLevel (split audit row 1): member B's Use Door on the party's level carried out while the lone
/// member's level is shown. Faithful: door B.Door of the level SHOWN opens (fighter-goto-action R014+0x2170, door-flip R023+0x00B8 on the
/// image of DS:11CA) and B's own stays shut; fixed: B's own door opens and the view comes back to the level that was shown.
/// </summary>
public sealed class PlaceActionsOnOwnLevelTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int DoorCountAt = 0x1F52, DoorOpenAt = 0x33D3;
    private const int Lone = 1;

    private static (TacticalBattle T, int Party, int His, int B) Split(OriginalBehaviour behaviour)
    {
        var (_, t) = BattleScreenTests.Start(GameFolder.Files!, new BattleRequest(0x7C, 3434, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]), s => s.Behaviour = behaviour);
        var party = t.Battle.LevelShown;
        typeof(TacticalBattle).GetMethod("TakeStairs", Any)!.Invoke(t, [0, Lone]);
        var his = t.Battle[Lone].Level;
        Assert.NotEqual(party, his);
        Assert.True(t.LevelAt(party).GetByte(DoorCountAt) > 0 && t.LevelAt(his).GetByte(DoorCountAt) > 0);
        var b = Enumerable.Range(0, Combatant.PartySlots).First(i => i != Lone && t.Battle[i].IsAble);
        var c = t.Battle[b];
        c.Door = 0;
        c.DoorOpens = true;
        c.Place = PlaceAction.UseDoor;
        Assert.Equal(0, t.LevelAt(party).GetByte(DoorOpenAt));
        Assert.Equal(0, t.LevelAt(his).GetByte(DoorOpenAt));
        t.SwitchLevel(his);
        typeof(TacticalBattle).GetMethod("DoPlaceAction", Any)!.Invoke(t, [b]);
        return (t, party, his, b);
    }

    [Fact]
    public void FaithfulUsesTheShownLevelsDoor()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, his, _) = Split(OriginalBehaviour.Faithful);
        Assert.Equal(1, t.LevelAt(his).GetByte(DoorOpenAt));
        Assert.Equal(0, t.LevelAt(party).GetByte(DoorOpenAt));
    }

    [Fact]
    public void TheFixUsesHisOwnDoor()
    {
        if (GameFolder.Files is null)
        {
            return;
        }
        var (t, party, his, _) = Split(OriginalBehaviour.DataSafe);
        Assert.Equal(0, t.LevelAt(his).GetByte(DoorOpenAt));
        Assert.Equal(1, t.LevelAt(party).GetByte(DoorOpenAt));
        Assert.Equal(his, t.Battle.LevelShown);
    }
}
