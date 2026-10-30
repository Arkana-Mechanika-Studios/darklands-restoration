using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineT00": a dark iron door in a mine, with three metal faces (lead, gold, silver). Which of
/// them speaks the truth? Touching the gold face springs a curse that coarsens the leader's
/// looks (Charisma and its maximum drop by one); the silver face opens the door, and the party
/// goes on to the card the mine quest's kind chooses; turning away ends in a fight.
/// </summary>
// orig: R067+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: handler setup R067+0x02AE; unused-line handler R067+0x02E6 ("screwed up.")
public sealed class DwarfDoorOfThreeFaces : Card
{
    private const int ClosedDoorPicture = 215;
    private const int OpenDoorPicture = 216;

    public override int Number => 0xC6;

    public override int Run(CardContext c)
    {
        Show(c, ClosedDoorPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The handler setup clears the item and saint lists.
        c.SetCardItems();
        c.SetCardSaints();
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2);

        return menu.Run(
            // orig: R067+0x0148 (line 0); hint R067+0x01BA ("You can always encounter the unknown", the leader)
            new MenuLine(_ => TouchTheGoldFace(c), () => new OptionHint(-1, CardTexts.Unknown, c.Leader)),
            // orig: R067+0x01F4 (line 1); hint R067+0x022E (same)
            new MenuLine(_ => TouchTheSilverFace(c), () => new OptionHint(-1, CardTexts.Unknown, c.Leader)),
            new MenuLine(_ => TurnAndLeave(c), OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>Wrong: the curse takes a little of the leader's charisma for good, and the door stays shut.</summary>
    // orig: R067+0x0148
    private void TouchTheGoldFace(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        ShowPicture(c, ClosedDoorPicture);
        c.Screen.Text(2);
        ShowPicture(c, ClosedDoorPicture);
        c.AddAttribute(c.Leader, (int)Attribute.Charisma, -1);
        c.AddAttributeMaximum(c.Leader, (int)Attribute.Charisma, -1);
        c.Next = Number;
    }

    /// <summary>Right: the door opens and a dwarf steps out; where the party goes then depends on the mine quest.</summary>
    // orig: R067+0x01F4, the choice of the next card R067+0x0126 (thunk 09C0:2193 is the mine quest's sort)
    private void TouchTheSilverFace(CardContext c)
    {
        ShowPicture(c, OpenDoorPicture);
        c.Screen.Text(1);
        c.Next = c.MineSort() switch
        {
            1 => 0xE7,
            2 => 0xEA,
            _ => 0xE3,
        };
    }

    /// <summary>Turning away from the door: a fight (battle kind 1; the other battle words stay as they were).</summary>
    // orig: R067+0x0268
    // C: only battle-kind (DS:A766) is set; battle-enemy-group, encounter-seed and DS:A76C/E/770 keep whatever an earlier card left.
    private static void TurnAndLeave(CardContext c)
    {
        c.Memory.Battle.Mode = 1;
        c.Next = -2;
    }
}
