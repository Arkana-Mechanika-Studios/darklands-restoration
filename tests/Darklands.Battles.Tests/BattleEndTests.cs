using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Battles.Tests;

public sealed class BattleEndTests
{
    private sealed class Actions : IBattleEndActions
    {
        public int Loots;
        public int Opened = -1;
        public readonly List<string> Calls = [];

        public void LootAfterFight() => Loots++;

        public void OpenLoot(int member) => Opened = member;

        public void SwitchLevel(int floor) => Calls.Add($"switch {floor}");

        public void LeftCombatMode() => Calls.Add("leave");

        public void RedrawAround(int member) => Calls.Add($"draw {member}");

        public void PlaceWokenMember(int member) { }

        public void VictoryCleanUp() { }
    }

    private static Battle NewBattle(params CharacterStatus[] enemies)
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1)) { RealCombat = true };
        for (var i = 0; i < 2; i++)
        {
            battle[i].Status = CharacterStatus.Able;
            battle[i].Maximums[0] = 20;
            battle[i].Endurance = 5;
        }
        for (var k = 0; k < enemies.Length; k++)
        {
            battle[5 + k].Status = enemies[k];
        }
        battle.ExitCards = new LevelExitCards(100, 101, 102, 103, 104);
        return battle;
    }

    [Fact]
    public void BeatingTheEnemiesWinsAfterAPause()
    {
        var battle = NewBattle(CharacterStatus.Dead, CharacterStatus.Unconscious);
        var actions = new Actions();
        battle.Clock = 1000;
        Assert.False(BattleEnd.IsOver(battle, actions));
        Assert.Equal(BattleResult.Won, battle.Result);
        Assert.Equal(1060, battle.EndTime);
        Assert.False(battle.RealCombat);
        battle.Clock = 1061;
        Assert.True(BattleEnd.IsOver(battle, actions));
        Assert.Equal(100, battle.ResultCard);
        Assert.Equal(1, actions.Loots);
        Assert.Equal(0, actions.Opened);
        Assert.Equal(15, battle[0].Endurance);
        Assert.Equal(CharacterStatus.Empty, battle[6].Status);
    }

    [Fact]
    public void RecoveringSetsEveryRecoveryTimeAndClearsTheHitsShown()
    {
        // R014+0x0E05: DS:DA55 = clock for slots 0..4 whatever their status; R014+0x0E15/0x0E19: DS:D4E5 and DS:D4F2 cleared for the able and unconscious
        var battle = NewBattle(CharacterStatus.Able);
        battle[2].Status = CharacterStatus.Dead;
        battle.Clock = 519;
        for (var i = 0; i < 3; i++)
        {
            battle[i].RecoverAt = 3212;
            battle[i].DamageNumber = 2;
            battle[i].WasHit = true;
        }
        BattleEnd.Recover(battle, new Actions());
        for (var i = 0; i < Combatant.PartySlots; i++)
        {
            Assert.Equal(519, battle[i].RecoverAt);
        }
        Assert.Equal(0, battle[0].DamageNumber);
        Assert.False(battle[1].WasHit);
        Assert.Equal(2, battle[2].DamageNumber);
        Assert.True(battle[2].WasHit);
        Assert.Equal(0, battle[5].RecoverAt);
    }

    [Fact]
    public void APersistentBattlefieldStaysOpenAfterVictory()
    {
        var battle = NewBattle(CharacterStatus.Dead);
        battle.Persistent = true;
        var actions = new Actions();
        battle.Clock = 10;
        Assert.False(BattleEnd.IsOver(battle, actions));
        battle.Clock = 100;
        Assert.False(BattleEnd.IsOver(battle, actions));
        Assert.Equal(BattleResult.Running, battle.Result);
    }

    [Fact]
    public void AnUnconsciousSurvivorMeansDefeat()
    {
        var battle = NewBattle(CharacterStatus.Able);
        battle[0].Status = CharacterStatus.Unconscious;
        battle[1].Status = CharacterStatus.Dead;
        battle.Clock = 5;
        Assert.False(BattleEnd.IsOver(battle, new Actions()));
        Assert.Equal(BattleResult.Lost, battle.Result);
        Assert.Equal(103, battle.ResultCard);
    }

    [Fact]
    public void EverybodyDownEndsTheGame()
    {
        var battle = NewBattle(CharacterStatus.Able);
        battle[0].Status = CharacterStatus.Dead;
        battle[1].Status = CharacterStatus.Dead;
        battle.Clock = 5;
        Assert.False(BattleEnd.IsOver(battle, new Actions()));
        Assert.Equal(BattleResult.AllDown, battle.Result);
        battle.Clock = 246;
        Assert.True(BattleEnd.IsOver(battle, new Actions()));
    }

    [Fact]
    public void LeavingThroughAnExitIsFleeingInRealCombat()
    {
        var battle = NewBattle(CharacterStatus.Able);
        battle[0].Floor = BattleEnd.LeftThroughExit;
        battle[1].Status = CharacterStatus.Unconscious;
        Assert.True(BattleEnd.IsOver(battle, new Actions()));
        Assert.Equal(BattleResult.Fled, battle.Result);
        Assert.Equal(102, battle.ResultCard);
        Assert.Equal(CharacterStatus.Away, battle[1].Status);
    }

    /// <summary>
    /// R014+0x0A54..0x0AC3: nobody able left on the enemies' floor, somebody able on another: the party's floor is shown, and before the end test
    /// returns the view is drawn around si (floor_view_draw, 09C0:1C2F) and a frame is drawn (combat_draw, 14F7:0008): si is the first able member,
    /// or the new leader when the leader went Away.
    /// </summary>
    [Fact]
    public void SwitchingToThePartysOtherFloorDrawsAFrameBeforeTheTestReturns()
    {
        foreach (var (leader, drawnAround) in new[] { (0, 1), (2, 1) })
        {
            var battle = NewBattle(CharacterStatus.Able);
            battle[0].Status = CharacterStatus.Unconscious;
            battle[1].Floor = 1;
            battle[2].Status = CharacterStatus.Able;
            battle[2].Floor = 1;
            battle.Leader = leader;
            var actions = new Actions();
            Assert.False(BattleEnd.IsOver(battle, actions));
            Assert.Equal(["switch 1", "leave", $"draw {drawnAround}"], actions.Calls);
            Assert.Equal(CharacterStatus.Away, battle[0].Status);
            Assert.Equal(CharacterStatus.Empty, battle[5].Status);
            Assert.Equal(leader == 0 ? 1 : leader, battle.Leader);
            Assert.False(battle.RealCombat);
        }
    }

    [Fact]
    public void NonLethalDamageStopsAtOne()
    {
        var battle = NewBattle(CharacterStatus.Able);
        battle.RealCombat = false;
        battle[5].Endurance = 3;
        battle[5].Strength = 2;
        battle[5].QueuedEndurance = 10;
        battle[5].QueuedStrength = 10;
        Attacks.ApplyDamage(battle, 5);
        Assert.Equal(CharacterStatus.Able, battle[5].Status);
        Assert.Equal(1, battle[5].Endurance);
        Assert.Equal(1, battle[5].Strength);
    }
    /// <summary>
    /// R040+0x0DB3..0x0DBC: the loot screen's D reads the floor bytes as words at twice the slot (the original's results from direct
    /// calls of R040+0x0C22 with DS:8DC4 = 0: floor byte 1 = 0x9D lands on slot 1 without the message; bytes 2, 3 = 0x9D, 0xFF skip slot 1).
    /// </summary>
    [Fact]
    public void TheLootScreensExitTestReadsTheFloorBytesAsWords()
    {
        var b = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1));
        b[1].Floor = BattleEnd.LeftThroughExit;
        Assert.False(BattleEnd.LootSkips(b, 1));
        b[1].Floor = 0;
        b[2].Floor = BattleEnd.LeftThroughExit;
        b[3].Floor = -1;
        Assert.True(BattleEnd.LootSkips(b, 1));
        Assert.False(BattleEnd.LootSkips(b, 2));
        b.Behaviour = new Darklands.Core.OriginalBehaviour { FixLootExitTestReadsWords = true };
        Assert.False(BattleEnd.LootSkips(b, 1));
        Assert.True(BattleEnd.LootSkips(b, 2));
    }
}
