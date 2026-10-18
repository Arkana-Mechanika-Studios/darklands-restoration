using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// An iron door in the dwarf mine with a riddle (cards 339..342): one knob or statue opens it
/// and a stunted figure steps out (the next card depends on the mine quest); every wrong one
/// curses the leader with the permanent loss of a point of one attribute, and the party may try
/// again; the last line leaves (−2, battle mode 1).
/// </summary>
public abstract class DwarfMineRiddleDoor : Card
{
    private const int Picture = 215;
    private const int OpenDoorPicture = 216;

    /// <summary>The option line that opens the door.</summary>
    protected abstract int RightAnswer { get; }

    /// <summary>The option line that leaves; the lines before it are the answers.</summary>
    protected abstract int LeaveLine { get; }

    protected abstract int OpenedBlock { get; }

    protected abstract int CurseBlock { get; }

    /// <summary>What the curse takes from the leader.</summary>
    protected abstract Attribute Cursed { get; }

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(Enumerable.Range(0, LeaveLine + 1).ToArray());
        c.SetCardItems();
        c.SetCardSaints();

        // C: every answer's hint reads leader when it is asked, so the hint follows a change of leader.
        var unknown = () => OptionHint.Say(CardTexts.Unknown, c.Leader);
        var choices = new MenuLine[LeaveLine + 1];
        for (var line = 0; line < LeaveLine; line++)
        {
            choices[line] = line == RightAnswer
                ? new MenuLine(_ => OpenTheDoor(c), unknown)
                : new MenuLine(_ => CurseTheLeader(c), unknown);
        }
        // orig: R128+0x2068 / R129+0x04A4 / R129+0x09FE / R129+0x0FEE matched — leave.
        choices[LeaveLine] = new MenuLine(_ =>
        {
            c.BattleMode = 1;
            c.Next = -2;
        }, OptionHint.Saying(SuccessAssured));
        return menu.Run(choices);
    }

    // orig: R128+0x1FE6 + R128+0x1F4E matched (and the twins R129+0x01D4 + _01b2,
    //   R129+0x0906 + _01bc, R129+0x0E80 + _01d6)
    // C: card_load(card_name, pic_buf) reloads the whole card (Show) before the quest stage is asked.
    private void OpenTheDoor(CardContext c)
    {
        ShowPicture(c, OpenDoorPicture);
        c.Screen.Text(OpenedBlock);
        Show(c, Picture);
        c.Next = c.MineSort() switch
        {
            1 => 0xE7,
            2 => 0xEB,
            _ => 0xE6,
        };
    }

    // orig: R128+0x1F70 matched (and every other wrong answer of the four cards, identical but for the text
    //   and the attribute)
    private void CurseTheLeader(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.Screen.Text(CurseBlock);
        c.AddAttribute(c.Leader, (int)Cursed, -1);
        c.AddAttributeMaximum(c.Leader, (int)Cursed, -1);
        c.Next = Number;
    }
}

/// <summary>"$MineT32": a dwarf and a kobold statue, one always lying; pushing the kobold opens the door, the dwarf costs the leader Endurance.</summary>
// orig: R128+0x1E28 (1A1A:0008) matched; tests: CardSmokeTests
//   option 0: action R128+0x1F70; hint R128+0x1FAC
//   option 1: action R128+0x1FE6; hint R128+0x202E
//   option 2: action R128+0x2068; hint R128+0x2076
//   other: R128+0x1F4E, R128+0x20AE, R128+0x20E6
// DORMANT (generated): option slots 10..19 keep the default handler R128+0x20E6 ("screwed up.") and are never shown.
// C (generated): opt_flag_3/4/5/6/7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class DwarfAndKoboldStatues : DwarfMineRiddleDoor
{
    public override int Number => 339;

    protected override int RightAnswer => 1;

    protected override int LeaveLine => 2;

    protected override int OpenedBlock => 1;

    protected override int CurseBlock => 2;

    protected override Attribute Cursed => Attribute.Endurance;
}
