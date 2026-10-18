using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Battles.Tests;

/// <summary>
/// A party member's pack in a battle is the member's own 64 entries (the original has no copy): the battle's removals
/// leave the stale entries they leave, and the member gets every entry back, the stale ones past the last stack too.
/// </summary>
public sealed class CombatantPackTests
{
    private static readonly ItemStack A = new(0x10, 0x10, 30, 1, 2);
    private static readonly ItemStack B = new(0x11, 0x11, 30, 2, 2);
    private static readonly ItemStack C = new(0x12, 0x12, 25, 1, 1);
    private static readonly ItemStack Stale = new(0x40, 0x69, 40, 1, 1);

    private static (Battle Battle, Character Member) Start()
    {
        var member = new Character();
        member.Pack.AddRange([A, B, C, Stale]);
        member.Pack.Stacks = 3;   // entry 3 is a stale entry an earlier removal left
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1));
        CombatantSetup.FromMember(battle[0], member);
        return (battle, member);
    }

    [Fact]
    public void TheStaleEntriesPastTheLastStackComeBack()
    {
        var (battle, member) = Start();
        CombatantSetup.BackToMember(battle[0]);
        Assert.Equal(3, member.Pack.Stacks);
        Assert.Equal(Stale, member.Pack[3]);
    }

    [Fact]
    public void AUsedUpLastStackStaysBehindWithItsCountDecremented()
    {
        var (battle, member) = Start();
        Assert.Equal(-1, Attacks.ConsumeSlot(battle, 0, 2));
        CombatantSetup.BackToMember(battle[0]);
        Assert.Equal(2, member.Pack.Stacks);
        Assert.Equal(C with { Count = 0 }, member.Pack[2]);
        Assert.Equal(Stale, member.Pack[3]);
    }

    [Fact]
    public void ARemovalShiftsOnlyTheStacksAfterIt()
    {
        var (battle, member) = Start();
        Assert.Equal(-1, Attacks.ConsumeItem(battle, 0, 0x10, 30));
        CombatantSetup.BackToMember(battle[0]);
        Assert.Equal(2, member.Pack.Stacks);
        Assert.Equal([B, C, C, Stale], member.Pack.Entries.Take(4));
    }

    [Fact]
    public void AStackAddedAndUsedUpInTheBattleStaysAsAStaleEntry()
    {
        var (battle, member) = Start();
        battle[0].Items.Add(Stale with { Code = 0x41 });
        Assert.Equal(-1, Attacks.ConsumeSlot(battle, 0, 3));
        CombatantSetup.BackToMember(battle[0]);
        Assert.Equal(3, member.Pack.Stacks);
        Assert.Equal(Stale with { Code = 0x41, Count = 0 }, member.Pack[3]);
    }
}
