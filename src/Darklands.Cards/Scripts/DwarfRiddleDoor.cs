using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// An iron door of the dwarves' mine with a riddle on it ($MineT02 … $MineT31). A wrong answer
/// releases a curse that costs the leader a point of an attribute and of its maximum, and the
/// party stands before the door again; the right one opens it, and the dwarf who steps out
/// is the one the mine quest calls for; turning away leads to a fight.
/// </summary>
// orig: the shared shape of the 30 riddle-door cards in R093..R100 (each RiddleDoor_MineTxx cites its own
// functions); thunk 09C0:2193 is the mine quest's sort (MineSort). All their sources are matched.
public abstract class DwarfRiddleDoor : Card
{
    private const int DoorPicture = 215;
    private const int OpenDoorPicture = 216;

    /// <summary>The cards beyond the door, by the mine quest's sort (0, 1, 2): what the dwarf does.</summary>
    protected abstract (int Sort0, int Sort1, int Sort2) Beyond { get; }

    /// <summary>Hours the way through the door takes.</summary>
    protected virtual int HoursThrough => 0;

    /// <summary>The answer lines of the riddle (text block 0).</summary>
    protected abstract MenuLine[] Answers(CardContext c);

    public override int Run(CardContext c)
    {
        Show(c, DoorPicture);
        c.SetCardItems();
        c.SetCardSaints();
        var answers = Answers(c);
        var menu = Menu(c);
        menu.Enable(Enumerable.Range(0, answers.Length).ToArray());
        c.Next = Number;
        return menu.Run(answers);
    }

    protected static OptionHint Unknown(CardContext c) => OptionHint.Say(CardTexts.Unknown, c.Leader);

    /// <summary>A wrong answer: the curse (text <paramref name="text"/>) shrinks the leader's <paramref name="attribute"/>.</summary>
    protected MenuLine Wrong(CardContext c, int text, Attribute attribute, Func<OptionHint>? hint = null) =>
        new(() => Curse(c, text, attribute), hint ?? (() => Unknown(c)));

    /// <summary>The right answer: the door opens (text <paramref name="text"/>) and the party goes through.</summary>
    protected MenuLine Right(CardContext c, int text, bool doorOpens = true, bool doorShownAgain = true, Func<OptionHint>? hint = null) =>
        new(() => GoThrough(c, text, doorOpens, doorShownAgain), hint ?? (() => Unknown(c)));

    /// <summary>Turning away from the door: the dwarves fight.</summary>
    protected MenuLine TurnAway(CardContext c, int hintMember = Who.Everybody, string hint = SuccessAssured) =>
        new(() => Fight(c), () => OptionHint.Say(hint, hintMember));

    /// <summary>A line with nothing behind it: the door stays as it is.</summary>
    protected static MenuLine Nothing(CardContext c) => new(() => { }, () => Unknown(c));

    /// <summary>
    /// A choice among statues that shift about: right while the location fact
    /// <paramref name="opensWith"/> stands (it lasts an hour after another wrong choice);
    /// otherwise the curse strikes and this choice leaves its own fact <paramref name="leaves"/>.
    /// </summary>
    // orig: the statue handlers of MineT10 (R095) and MineT17 (R097): place-flag(0x0C, k, location-now) ? right : wrong
    // + event-create(-2, location-now, 0x5A, 0x1C, j, 0x63, 0, 1, 0, 0x0C).
    protected MenuLine Shifting(CardContext c, int opensWith, int leaves, int rightText, int wrongText, Attribute attribute) =>
        new(() =>
        {
            if (c.Events.Fact(MineQuestTag, opensWith, c.Here))
            {
                GoThrough(c, rightText, doorOpens: true, doorShownAgain: true);
                return;
            }
            Curse(c, wrongText, attribute);
            c.Events.Create(Who.Everybody, c.Here, 0x5A, EventKinds.LocationFact, leaves, 0x63, 0, 1, 0, MineQuestTag);
        }, () => Unknown(c));

    /// <summary>The tag of the mine quest's location facts.</summary>
    protected const int MineQuestTag = 0x0C;

    // orig: the wrong-answer handlers: text-slot-chosen-one = leader; card-text-block(n, 0); raise-attribute-by(leader, a, -1);
    // raise-attribute-cap-by(leader, a, -1); following-card = own number.
    private void Curse(CardContext c, int text, Attribute attribute)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.Screen.Text(text);
        c.AddAttribute(c.Leader, (int)attribute, -1);
        c.AddAttributeMaximum(c.Leader, (int)attribute, -1);
        c.Next = Number;
    }

    // orig: the right-answer handlers: card-run-near(open door, +0x0DA7); card-text-block(n, 2); card-run(door, +0x0DA3)
    // (some doors skip the picture or the redraw); then the sort helper (some pass an hour first).
    // C: card-text-block(n, 2) is drawn like card-text-block(n, 0): the engine treats how 2 as 0.
    private void GoThrough(CardContext c, int text, bool doorOpens, bool doorShownAgain)
    {
        if (doorOpens)
        {
            ShowPicture(c, OpenDoorPicture);
        }
        c.Screen.Text(text);
        if (doorShownAgain)
        {
            Show(c, DoorPicture);
        }
        if (HoursThrough > 0)
        {
            c.PassHours(HoursThrough);
        }
        var (sort0, sort1, sort2) = Beyond;
        c.Next = c.MineSort() switch
        {
            1 => sort1,
            2 => sort2,
            _ => sort0,
        };
    }

    // orig: the turn-away handlers: battle-kind = 1; following-card = -2.
    private static void Fight(CardContext c)
    {
        c.BattleMode = 1;
        c.Next = -2;
    }
}
