using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Characters;

namespace Darklands.Cards.Tests;

/// <summary>
/// The Devil's Bridge Crossing's stale status write (R088+0x05C6..0x05CF, DS:EE8E never assigned) onto the slot DropAway just emptied:
/// the original brings the slot back with status 1 and no charactr.tmp record (ghost member, ids[slot] = -1); the data-safe game leaves it empty.
/// </summary>
public sealed class GhostPartySlotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnEmptiedSlotIsAGhostOnlyInTheFaithfulGame(bool fix)
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        state.Behaviour = new OriginalBehaviour { FixGhostPartySlot = fix };
        var party = state.Party;
        while (party.Members.Count < 4)
        {
            party.Members.Add(new Character());
        }
        for (var i = 0; i < 4; i++)
        {
            party.Members[i].Status = CharacterStatus.Able;
        }
        state.Roster.Reconcile(party);
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0));
        c.DeleteMember(1);
        c.SetStatusAbleStale(1);
        state.Roster.Reconcile(party);
        Assert.Equal(fix ? CharacterStatus.Empty : CharacterStatus.Able, party.Slot(1).Status);
        Assert.Equal(-1, state.Roster.PartySlots[1]);
    }

    [Fact]
    public void ALiveSlotIsSetToAbleInBothGames()
    {
        if (!CardHarness.Available)
        {
            return;
        }
        var state = CardHarness.NewGame(seed: 5, location: 3);
        state.Behaviour = OriginalBehaviour.DataSafe;
        var party = state.Party;
        while (party.Members.Count < 2)
        {
            party.Members.Add(new Character());
        }
        party.Members[0].Status = CharacterStatus.Able;
        party.Members[1].Status = CharacterStatus.Away;
        var c = new CardContext(state, CardHarness.World!, new ScriptedScreen(0));
        c.SetStatusAbleStale(1);
        Assert.Equal(CharacterStatus.Able, party.Slot(1).Status);
    }
}
