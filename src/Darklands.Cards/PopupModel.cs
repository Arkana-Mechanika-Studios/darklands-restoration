using Darklands.Core.Characters;

namespace Darklands.Cards;

/// <summary>How a line of an item or saint box is offered (its colour in the classic box: OptionPopupText).</summary>
public enum PopupLineKind : byte
{
    /// <summary>No line: an empty slot, a member who is not there, or an item or saint the member lacks.</summary>
    Blank = 0,

    /// <summary>The leader at Basic who lacks it: listed, greyed, not choosable.</summary>
    LeaderOnly = 1,

    /// <summary>A formula the member knows (not at Expert): listed, greyed, not choosable.</summary>
    KnownFormula = 2,

    /// <summary>Listed and choosable.</summary>
    Usable = 3,

    /// <summary>Listed, not choosable (an item the member holds in another way).</summary>
    NotUsable = 4,
}

/// <summary>One line of a box: its kind, the card's item or saint number, and the count the member carries (−1 for none shown).</summary>
public readonly record struct PopupLine(PopupLineKind Kind, int Entry, int Count)
{
    public static PopupLine Blank { get; } = new(PopupLineKind.Blank, -1, -1);

    /// <summary>The option state the line gives its entry.</summary>
    public OptionState State => Kind switch
    {
        PopupLineKind.LeaderOnly or PopupLineKind.KnownFormula => OptionState.Shown,
        PopupLineKind.Usable => OptionState.Enabled,
        _ => OptionState.Hidden,
    };
}

/// <summary>A member's part of a box: the member's status and short name, and the four lines of the card's slots.</summary>
public sealed record PopupMember(CharacterStatus Status, string ShortName, IReadOnlyList<PopupLine> Lines)
{
    /// <summary>The member has a line to show (the box shows the name above them).</summary>
    public bool Listed => Lines.Any(l => l.Kind != PopupLineKind.Blank);
}

/// <summary>
/// The item or saint box of an option line as the rules decide it (<see cref="CardContext.PopupLines"/>): which member may choose
/// which of the card's four items or saints. The screen lays it out; entry member × 4 + slot is option member × 4 + slot.
/// </summary>
public sealed record PopupModel(bool Saints, IReadOnlyList<PopupMember> Members)
{
    /// <summary>The 20 option states (five members × four slots).</summary>
    public OptionState[] States
    {
        get
        {
            var states = new OptionState[CardMenu.MaxLines];
            for (var i = 0; i < Members.Count && i < 5; i++)
            {
                for (var j = 0; j < 4; j++)
                {
                    states[i * 4 + j] = Members[i].Lines[j].State;
                }
            }
            return states;
        }
    }
}
