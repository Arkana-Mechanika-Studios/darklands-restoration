using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago05": a proud knight who means to slay the dragon himself, met again and again in
/// the blighted land (the dragon quest's fact 4/3 counts the meetings): first haughty, then
/// beaten and bleeding, then humbled (he may join the party), then in league with the witches.
/// </summary>
// orig: R082+0x0EA0 (1922:0000) matched; tests: CardSmokeTests
// orig: R082+0x16A4 (1922:0804) matched; R082+0x16DC (1922:083C) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 4..19 (never offered).
public sealed class RivalDragonSlayer : Card
{
    public const int CardNumber = 0x9B;

    private const int DragonQuest = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        var meeting = c.Events.FactValueAnywhere(DragonQuest, 3);
        var picture = meeting switch
        {
            1 => 287,
            0 or 2 or 4 => 117,
            3 => 118,
            5 => 286,
            _ => 119,
        };
        Show(c, picture);
        c.Next = CardNumber;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3);
        c.RandomName(c.DragonDetail() + 0x25, 0, 0);
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 1);
        c.RandomName(c.DragonDetail() + 0x26, 0, 2);
        // UNCERTAIN: options_block keeps an earlier card's value for meetings outside 0..5 (the card then leaves anyway).
        var block = 0;
        meeting = c.Events.FactValueAnywhere(DragonQuest, 3);
        switch (meeting)
        {
            case 0:
                menu[1] = OptionState.Hidden;
                block = 0;
                break;
            case 1:
                c.Screen.Text(1);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            case 2:
                menu[1] = OptionState.Hidden;
                menu[2] = OptionState.Hidden;
                block = 2;
                break;
            case 3:
                menu[1] = OptionState.Hidden;
                block = 3;
                break;
            case 4:
                if (c.PartySize >= 4)
                {
                    menu[1] = OptionState.Hidden;
                }
                block = 4;
                break;
            case 5:
                c.Screen.Text(5);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            default:
                // After the fifth meeting the knight is gone: the blighted land again.
                c.Next = 0x97;
                break;
        }
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next == CardNumber)
        {
            menu.Block = block;
            menu.Run(
                // orig: R082+0x114C (1922:02AC) matched; R082+0x126A (1922:03CA) matched; tests: CardSmokeTests
                new MenuLine(() => LaughAtHim(c, picture), OptionHint.Saying(SuccessAssured)),
                new MenuLine(() => TakeHimAlong(c), OptionHint.Saying("He'll be glad to join you")),
                new MenuLine(() => Attack(c), OptionHint.Saying(CardTexts.EnterCombat)),
                // orig: R082+0x1654 (1922:07B4) matched; R082+0x166C (1922:07CC) matched; tests: CardSmokeTests
                new MenuLine(() => { c.PassHours(1); c.State.CurrentLocation = -1; c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)));
        }
        // Every passage spreads the blight and counts the meeting.
        // C: the growth is drawn as an argument of event_update.
        var growth = c.Random(2);
        c.Events.Update(-1, -1, 0x1C, 1, growth, DragonQuest, EventUpdate.Add);
        c.Events.Update(-1, -1, 0x1C, 3, 1, DragonQuest, EventUpdate.Add);
        return c.Next;
    }

    private static void LaughAtHim(CardContext c, int picture)
    {
        switch (c.Events.FactValueAnywhere(DragonQuest, 3))
        {
            case 0:
                ShowPicture(c, 119);
                c.Screen.Text(6);
                break;
            case 1:
            case 2:
                ShowPicture(c, 120);
                c.Screen.Text(7);
                c.Screen.Text(8);
                break;
            case 3:
                ShowPicture(c, 120);
                c.Screen.Text(9);
                break;
            default:
                ShowPicture(c, 120);
                c.Screen.Text(7);
                c.Screen.Text(8);
                ShowPicture(c, picture);
                c.Next = CardNumber;
                return;
        }
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    // orig: R082+0x12A2 (1922:0402) matched; R082+0x12F6 (1922:0456) matched; tests: CardSmokeTests
    private static void TakeHimAlong(CardContext c)
    {
        c.RandomName(c.Random(1000), 0, 0);
        // The knight joins for good (shield picture "F01").
        c.AddMember(c.Variables.Named[0], "Knight", 9999, 7);
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    // orig: R082+0x1324 (1922:0484) matched; R082+0x161C (1922:077C) matched; tests: CardSmokeTests
    private static void Attack(CardContext c)
    {
        if (c.Events.FactValueAnywhere(DragonQuest, 3) == 3)
        {
            // Allied with the witches, the knight's magic breaks the party's weapons.
            ShowPicture(c, 189);
            c.Screen.Text(0x0E);
            for (var m = 0; m < 5; m++)
            {
                if (c.Status(m) == CharacterStatus.Empty)
                {
                    continue;
                }
                var weapon = c.EquippedItem(m, 0);
                if (weapon == -1)
                {
                    weapon = c.EquippedItem(m, 4);
                }
                // C: the missile weapon is asked for twice.
                if (weapon == -1)
                {
                    weapon = c.EquippedItem(m, 4);
                }
                // C: R031+0x4CC4 takes a pack index, but gets the weapon's item code: entry <code> of the member's 64 (a stale one
                // past the last stack, or the memory around the packs for −1 and codes from 64 on: Pack's flat model) loses one, and
                // when its count reaches 0 it is unequipped and deleted (R031+0x4C56 lowers the stack count whatever the index)
                // orig: R082+0x1324 (1922:0484) matched; tests: OriginalMemoryTests
                c.TakeItemAt(m, weapon);
            }
            c.PassHours(1);
            c.Next = 0x0C;
            return;
        }
        // C: arguments are evaluated right to left: rand_mod(3) + 1, then rand_mod(5), then the first group.
        var third = c.Random(3) + 1;
        var second = c.Random(5) + 4;
        var first = c.Random(3) + c.PartyLoreLevel() / 4 + 1;
        switch (c.RunBattle(0x40, -1, 3, first, second, 0, third, 1, -1, 0, 0))
        {
            case 0:
                ShowPicture(c, 118);
                c.Screen.Text(0x0A);
                c.PassHours(0);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            case 1:
                c.RejoinAway();
                ShowPicture(c, 287);
                c.Screen.Text(0x0B);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            case 2:
                c.DropAway();
                ShowPicture(c, 287);
                c.Screen.Text(0x0C);
                c.PassHours(1);
                c.State.CurrentLocation = -1;
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                // Taken to the nearest city's dungeon.
                ShowPicture(c, 13);
                c.Screen.Text(0x0D);
                // C: nearest_city(*(long far *)(locations[cur_loc] + 4)): in the wilderness (cur_loc -1) locations[-1] is the last four
                // C: bytes of enemy_items (2E38:10BA), 0 in play, so the position read is the INT 1 vector at 0000:0004 (NullPage): a
                // C: point far beyond the map's north-west corner, whose nearest city is the party's prison wherever it was met.
                var (x, y) = c.Here >= 0 ? (c.State.Locations[c.Here].Position.X, c.State.Locations[c.Here].Position.Y)
                    : (Core.World.NullPage.Int1Offset, Core.World.NullPage.Int1Segment);
                c.State.CurrentLocation = c.NearestCity(x, y);
                // C: the script reads the city's records from darkland.cty and darkland.dsc itself.
                c.LoadCity(c.Here);
                c.PassHours(0x24);
                c.Next = 0x0D;
                break;
            default:
                // DORMANT: other battle results leave the party with the knight.
                break;
        }
    }
}
