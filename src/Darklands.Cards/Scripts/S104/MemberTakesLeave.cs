namespace Darklands.Cards.Scripts;

/// <summary>
/// "$NPCEx00": a hired companion's term is over. The member says goodbye and leaves the
/// party for good; then the party is back on the map.
/// </summary>
// orig: R104+0x0D62 (190E:0002) matched; tests: CardSmokeTests
// DORMANT: the file also holds ten option handler pairs (R104+0x0E2E..0320, each "let no
// time pass, go to card 0x62") and a registration function (R104+0x10AE) that the card
// never installs or calls: it has no menu. They are not ported.
public sealed class MemberTakesLeave : Card
{
    private const int Picture = 193;

    public override int Number => 241;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        // The original sets following-card to its own number first; nothing reads it before it is
        // overwritten below.
        c.Next = Number;
        var note = c.Events.Find(0x47, -1, -1, -1, -1, -1);
        if (note != -1)
        {
            // C: text-slot-chosen-one = *events[i]: the record's first word, the event type (the member slot).
            var member = c.Events[note]!.Type;
            c.Variables.Chosen[0] = member;
            c.Screen.Text(0, TextFrame.BlankPage);
            c.Events.Free(note);
            c.DeleteMember(member);
            // The original then also clears the slot's status byte; DeleteMember already did.
            c.SetStatus(member, Core.Characters.CharacterStatus.Empty);
            c.PassHours(0);
        }
        c.LeaveForTheMap();
        return c.Next;
    }
}
