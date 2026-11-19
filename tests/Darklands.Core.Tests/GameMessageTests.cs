namespace Darklands.Core.Tests;

/// <summary>The rules' messages are ids with arguments; a queue keeps them for a view that shows them later.</summary>
public sealed class GameMessageTests
{
    [Fact]
    public void ASkillChangeCarriesTheSkillNumberAndValuesNotText()
    {
        var m = GameMessage.SkillChanged(2, 8, 30, 45);
        Assert.Equal((GameMessageIds.SkillChanged, MessageKind.Report, 2, true), (m.Id, m.Kind, m.Member, m.Good));
        Assert.Equal((8, 30, 45), (m.Number("skill"), m.Number("from"), m.Number("to")));
        Assert.False(GameMessage.SkillChanged(2, 8, 45, 30).Good);
        Assert.Equal("id:skill.changed from=30 skill=8 to=45", m.ToString());
    }

    [Fact]
    public void ANoticeKeepsItsTicks()
    {
        Assert.Equal(GameMessage.NoticeTicks, GameMessage.Notice(GameMessageIds.AlchemySuccess).Ticks);
        Assert.Equal(0x50, GameMessage.Notice(GameMessageIds.NotEnoughMoney, 0x50).Ticks);
        Assert.Equal(MessageKind.Notice, GameMessage.Notice(GameMessageIds.NotEnoughMoney).Kind);
    }

    [Fact]
    public void AQueueGivesThePostsBackInOrderOnce()
    {
        var s = TestGame.Create();
        var queue = new GameMessageQueue();
        s.Messages = queue;
        Rules.MemberRules.AddSkill(s, 0, (int)Characters.Skill.Religion, 1);
        Rules.MemberRules.AddSkill(s, 1, (int)Characters.Skill.Religion, -1);
        Assert.Equal(2, queue.Count);
        Assert.Equal([0, 1], queue.Drain().Select(m => m.Member));
        Assert.Empty(queue.Drain());
    }
}
