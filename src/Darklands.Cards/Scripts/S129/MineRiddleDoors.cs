using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>"$MineT33": press the patriarch missing from the list; Enoch opens the door, the other names cost the leader Charisma.</summary>
// orig: R129+0x0000 (1838:0000) matched; tests: CardSmokeTests
//   option 0: action R129+0x01D4; hint R129+0x021C
//   option 1: action R129+0x0256; hint R129+0x0292
//   option 2: action R129+0x02CC; hint R129+0x0308
//   option 3: action R129+0x0342; hint R129+0x037E
//   option 4: action R129+0x03B8; hint R129+0x03F4
//   option 5: action R129+0x042E; hint R129+0x046A
//   option 6: action R129+0x04A4; hint R129+0x04B2
//   other: R129+0x01B2, R129+0x04EA, R129+0x0522
// DORMANT (generated): option slots 10..19 keep the default handler R129+0x0522 ("screwed up.") and are never shown.
// C (generated): opt_flag_7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class MissingPatriarchDoor : DwarfMineRiddleDoor
{
    public override int Number => 340;

    protected override int RightAnswer => 0;

    protected override int LeaveLine => 6;

    protected override int OpenedBlock => 1;

    protected override int CurseBlock => 2;

    protected override Attribute Cursed => Attribute.Charisma;
}

/// <summary>"$MineT34": press the realm that does not belong; Portugal opens the door, the others cost the leader Intelligence.</summary>
// orig: R129+0x055A (188D:000A) matched; tests: CardSmokeTests
//   option 0: action R129+0x072E; hint R129+0x076A
//   option 1: action R129+0x07A4; hint R129+0x07E0
//   option 2: action R129+0x081A; hint R129+0x0856
//   option 3: action R129+0x0890; hint R129+0x08CC
//   option 4: action R129+0x0906; hint R129+0x094E
//   option 5: action R129+0x0988; hint R129+0x09C4
//   option 6: action R129+0x09FE; hint R129+0x0A0C
//   other: R129+0x070C, R129+0x0A44, R129+0x0A7C
// DORMANT (generated): option slots 10..19 keep the default handler R129+0x0A7C ("screwed up.") and are never shown.
// C (generated): opt_flag_7/8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class RealmThatBelongsNotDoor : DwarfMineRiddleDoor
{
    public override int Number => 341;

    protected override int RightAnswer => 4;

    protected override int LeaveLine => 6;

    protected override int OpenedBlock => 2;

    protected override int CurseBlock => 1;

    protected override Attribute Cursed => Attribute.Intelligence;
}

/// <summary>"$MineT35": the hocus + pocus = presto sum; the knob "5" opens the door, the other numbers cost the leader Strength.</summary>
// orig: R129+0x0AB4 (18E3:0004) matched; tests: CardSmokeTests
//   option 0: action R129+0x0CA8; hint R129+0x0CE4
//   option 1: action R129+0x0D1E; hint R129+0x0D5A
//   option 2: action R129+0x0D94; hint R129+0x0DD0
//   option 3: action R129+0x0E0A; hint R129+0x0E46
//   option 4: action R129+0x0E80; hint R129+0x0EC8
//   option 5: action R129+0x0F02; hint R129+0x0F3E
//   option 6: action R129+0x0F78; hint R129+0x0FB4
//   option 7: action R129+0x0FEE; hint R129+0x0FFC
//   other: R129+0x0C86, R129+0x1034, R129+0x106C
// DORMANT (generated): option slots 10..19 keep the default handler R129+0x106C ("screwed up.") and are never shown.
// C (generated): opt_flag_8/9 are never assigned or read here: the card's init zeroes opt_on[0..19], so those lines stay hidden, as in the C#.
// DORMANT (generated): card_items stays all -1, so no line opens an item pop-up.
// DORMANT (generated): card_saints stays all -1, so no line opens a saint pop-up.
// UNCERTAIN (generated): the card-engine globals DS:EE70 / DS:EE72 the C sets (meaning unknown) are not reproduced.
// UNCERTAIN (generated): card_text(k, 2) (frame mode 2) is read as drawing like mode 0 (on the picture).
public sealed class HocusPocusDoor : DwarfMineRiddleDoor
{
    public override int Number => 342;

    protected override int RightAnswer => 4;

    protected override int LeaveLine => 7;

    protected override int OpenedBlock => 1;

    protected override int CurseBlock => 2;

    protected override Attribute Cursed => Attribute.Strength;
}
