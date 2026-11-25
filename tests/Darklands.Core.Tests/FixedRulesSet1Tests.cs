using Darklands.Core.Events;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// F-1 set 1 (the user's choices of 2026-11-25), the fixes of the rules: each test shows the original's behaviour with the flag off
/// and the fixed one with the flag on, on the same situation.
/// </summary>
public sealed class FixedRulesSet1Tests
{
    /// <summary>FixBattleEventsKept (B-e1b48f): an event made in a battle is lost when the table is read back; fixed, it is kept.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEventMadeInABattleIsLostOrKept(bool fixedGame)
    {
        var s = TestGame.Create();
        s.Behaviour.FixBattleEventsKept = fixedGame;
        s.Events.Create(0, 5, 0, EventKinds.Teacher, 3, 10, 0, 100, 0, 0);
        s.Events.Create(0, 5, 0, EventKinds.Teacher, 4, 10, 0, 100, 0, 0);
        // the battle: the table written out and emptied, a saint's boost made in it (slot 0, below the count of 2)
        s.Events.Shutdown();
        var boost = s.Events.Create(1, 5, 0x63, EventKinds.AttributeBoost, 2, 5, (int)ExpiryHandler.UndoTemporaryBoost, 24, (int)EventFlags.CallExpiryHandler, 0);
        Assert.Equal(0, boost);
        s.Events.Reload(s.Behaviour.FixBattleEventsKept);
        Assert.Equal(2, s.Events.Count(EventKinds.Teacher));
        Assert.Equal(fixedGame ? 1 : 0, s.Events.Count(EventKinds.AttributeBoost));
    }

    /// <summary>
    /// FixTeacherRows (B-459e15): the list counts every town's teachers, so a teacher here is left out and its row is another's;
    /// fixed, this town's teachers only, one row each.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTeacherListCountsOtherTownsOrOnlyThisOne(bool fixedGame)
    {
        var s = TestGame.Create(location: 5);
        s.Behaviour.FixTeacherRows = fixedGame;
        s.Events.Create(0, 7, 0, EventKinds.Teacher, 3, 10, 0, 100, 0, 0);    // another town's teacher, met first
        s.Events.Create(0, 5, 0, EventKinds.Teacher, 10, 10, 0, 100, 0, 0);
        s.Events.Create(0, 5, 0, EventKinds.Teacher, 12, 10, 0, 100, 0, 0);
        var stay = new CampStay(s, CampPlace.Inn);
        var list = stay.TeacherList();
        Assert.Equal(fixedGame ? [10, 12] : [10], list);
        Assert.Equal(fixedGame ? 10 : 0, stay.TeacherSkill(0));
        Assert.Equal(fixedGame ? 12 : 10, stay.TeacherSkill(1));
    }

    /// <summary>
    /// FixBoostRecordsApplied (B-6eb045): a boost cut by the cap is recorded in full, so its end takes back more than it gave;
    /// fixed, the amount added is recorded.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABoostCutByTheCapRecordsWhatItAsksOrWhatItAdds(bool fixedGame)
    {
        var s = TestGame.Create();
        s.Behaviour.FixBoostRecordsApplied = fixedGame;
        var m = s.Party.Slot(0);
        m.Attributes[0] = 98;
        m.AttributeMaximums[0] = 98;
        MemberRules.RaiseAttributeTemporarily(s, 0, 0, 5, 24);
        Assert.Equal(99, m.Attributes[0]);
        var recorded = s.Events.All().Single(e => e.Event.Kind == EventKinds.AttributeBoost).Event;
        Assert.Equal(fixedGame ? 1 : 5, recorded.Value);
        // its end takes the recorded amount back: below the start without the fix, the start with it
        MemberRules.AddAttribute(s, 0, 0, -recorded.Value);
        Assert.Equal(fixedGame ? 98 : 94, m.Attributes[0]);
    }
}
