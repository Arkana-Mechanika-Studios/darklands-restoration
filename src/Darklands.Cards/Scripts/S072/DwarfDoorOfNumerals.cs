using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MineT01": a grim iron door in a mine, with a sum in letters (elf + elf = fool) and five knobs
/// numbered 5 to 9: which numeral does the letter "e" stand for? Only the knob 7 opens the door; each
/// wrong knob springs a trap that makes the leader permanently a little less intelligent (Intelligence
/// and its maximum drop by one); turning away ends in a fight.
/// </summary>
// orig: R072+0x05DE (1895:000E) matched; tests: CardSmokeTests
// orig: handler setup R072+0x0AFA; unused-line handler R072+0x0B32 ("screwed up.")
public sealed class DwarfDoorOfNumerals : Card
{
    private const int ClosedDoorPicture = 215;
    private const int OpenDoorPicture = 216;
    private const int DwarfPicture = 217;

    public override int Number => 0xC7;

    public override int Run(CardContext c)
    {
        Show(c, ClosedDoorPicture);
        // C: DS:EE72 = 7 and DS:EE70 = 0 are set here and never read by this card.
        c.Next = Number;
        // The handler setup clears the item and saint lists.
        c.SetCardItems();
        c.SetCardSaints();
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5);

        // Hints: R072+0x0802, 02de, 0352, 03fe, 04aa ("You can always encounter the unknown", the leader), 04f2 ("Success is assured.").
        return menu.Run(
            new MenuLine(_ => WrongKnob(c), Unknown),
            new MenuLine(_ => WrongKnob(c), Unknown),
            new MenuLine(_ => RightKnob(c), Unknown),
            new MenuLine(_ => WrongKnob(c), Unknown),
            new MenuLine(_ => WrongKnob(c), Unknown),
            new MenuLine(_ => TurnAndLeave(c), OptionHint.Saying(SuccessAssured)));

        OptionHint Unknown() => new OptionHint(-1, CardTexts.Unknown, c.Leader);
    }

    /// <summary>The trap: the leader loses a point of Intelligence, for good.</summary>
    // orig: R072+0x0790 (knob 5), 026c R072+0x083C (6), 038c R072+0x095C (8), 0438 R072+0x0A08 (9)
    private void WrongKnob(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        ShowPicture(c, DwarfPicture);
        c.Screen.Text(1);
        ShowPicture(c, ClosedDoorPicture);
        c.AddAttribute(c.Leader, (int)Attribute.Intelligence, -1);
        c.AddAttributeMaximum(c.Leader, (int)Attribute.Intelligence, -1);
        c.Next = Number;
    }

    /// <summary>The door opens and a dwarf steps out; where the party goes then depends on the mine quest.</summary>
    // orig: R072+0x08E8 (knob 7), the choice of the next card R072+0x076E (thunk 09C0:2193 is the mine quest's sort)
    private void RightKnob(CardContext c)
    {
        ShowPicture(c, OpenDoorPicture);
        c.Screen.Text(2);
        c.Next = c.MineSort() switch
        {
            1 => 0xE7,
            2 => 0xEA,
            _ => 0xE3,
        };
    }

    /// <summary>Turning away from the door: a fight (battle kind 1; the other battle words stay as they were).</summary>
    // orig: R072+0x0AB4
    // C: only battle-kind (DS:A766) is set; battle-enemy-group, encounter-seed and DS:A76C/E/770 keep whatever an earlier card left.
    private static void TurnAndLeave(CardContext c)
    {
        c.Memory.Battle.Mode = 1;
        c.Next = -2;
    }
}
