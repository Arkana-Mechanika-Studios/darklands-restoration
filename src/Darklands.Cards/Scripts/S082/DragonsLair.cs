using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago03": the dragon on its bed of gold in its cavern. Attack it, reason with it (a
/// virtuous leader may lay it to rest), or leave at once.
/// </summary>
// Reconstructed from the disassembly (no C):
// orig: R082+0x00D8 (1845:0008) asm; tests: CardSmokeTests
// orig: R082+0x06AC (1845:05DC) asm; R082+0x06E4 (1845:0614) asm; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 3..19 (never offered).
public sealed class DragonsLair : Card
{
    public const int CardNumber = 0x99;

    /// <summary>The card's MSG name (the card table has none for this card).</summary>
    private const string MsgName = "$Drago03";

    private const int BeatenPicture = 110;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(109));
        c.Next = CardNumber;
        // C: name_to_slot(6, thunk 09C0:212F(), slot): the slot argument is pushed before the call.
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 0);
        c.NameToSlot(6, c.DragonDetail(), 1);
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 2);
        return menu.Run(
            // orig: R082+0x04E6 (1845:0416) asm; R082+0x04EC (1845:041C) asm; tests: CardSmokeTests
            new MenuLine(() => Fight(c), OptionHint.Saying(CardTexts.EnterCombat)),
            // orig: R082+0x0524 (1845:0454) asm; R082+0x0604 (1845:0534) asm; tests: CardSmokeTests
            new MenuLine(() => Reason(c), hint: () => OptionHint.Say("Only high Virtue can subdue the dragon", (sbyte)c.Leader)),
            // orig: R082+0x0634 (1845:0564) asm; R082+0x0682 (1845:05B2) asm; tests: CardSmokeTests
            new MenuLine(() =>
            {
                c.Injure((sbyte)c.Leader, 10, 15, 1);
                ShowPicture(c, 332);
                c.Screen.Text(6);
                c.Next = 0x0C;
            }, OptionHint.Saying("Nothing is safe")));
    }

    /// <summary>The fight with the dragon: victory brings its hoard and the region's thanks.</summary>
    // orig: R082+0x0222 (1845:0152) asm; tests: CardSmokeTests
    private static void Fight(CardContext c)
    {
        // C: the dragon's sort is asked for and discarded.
        c.DragonSort();
        var result = c.RunBattle(0x60, -1, 0x3F, 5, 1, -1, 0, 0, -1, 0, 0);
        switch (result)
        {
            case 0:
                c.Variables.Money[0] = Money.From(c.Random(10) + 50, 0, 0);
                switch (c.Random(4))
                {
                    case 0:
                        ShowPicture(c, 306);
                        c.Screen.Text(7);
                        break;
                    case 1:
                        ShowPicture(c, 307);
                        c.Screen.Text(8);
                        break;
                    case 2:
                        ShowPicture(c, 323);
                        c.Screen.Text(9);
                        break;
                    case 3:
                        ShowPicture(c, 189);
                        c.Screen.Text(0x0A);
                        break;
                }
                c.State.Party.Purse = c.State.Party.Purse.Add(c.Variables.Money[0]);
                var lair = c.DragonLair();
                // DIVERGENCE: without a dragon quest (lair -1) the original writes through index -1; skipped here.
                if (lair >= 0 && lair < c.State.Locations.Count)
                {
                    c.State.Locations[lair].ArrivalCard = 0x62;
                }
                c.ResolveQuest(4, c.Here);
                // C: party_x, party_y are passed to nearest_city.
                c.AdjustReputation(c.NearestCity(c.State.MapPosition.X, c.State.MapPosition.Y), 0x28, 0x32);
                c.PassHours(4);
                c.Next = 0x0C;
                break;
            case 1:
            case 2:
                c.DropAway();
                ShowPicture(c, BeatenPicture);
                c.Screen.Text(0x0B);
                c.Next = 0x0C;
                break;
            case 3:
            case 4:
                TheDragonTakesOne(c);
                c.Next = 0x0C;
                break;
            default:
                // DORMANT: other battle results leave the party before the dragon.
                break;
        }
    }

    /// <summary>The victor seizes the most pious able woman, or else anyone, who leaves the party for good.</summary>
    private static void TheDragonTakesOne(CardContext c)
    {
        int victim;
        int text;
        if (c.AbleWomen() != 0)
        {
            c.PartyMaxSkillOfWomen(9);
            victim = (byte)c.BestMember;
            text = 0x0C;
        }
        else
        {
            victim = c.Random(c.PartySize);
            while (c.Status(victim) == CharacterStatus.Empty)
            {
                victim = (victim + 1) % 5;
            }
            text = 0x0D;
        }
        c.Variables.Chosen[0] = victim;
        if (victim < c.PartySize)
        {
            c.SetStatus(victim, CharacterStatus.Empty);
        }
        ShowPicture(c, BeatenPicture);
        c.Screen.Text(text);
        c.DeleteMember(victim);
    }

    /// <summary>A roll of 300 against the leader's virtue: the right words (by the dragon's sort) send it to sleep.</summary>
    private static void Reason(CardContext c)
    {
        var leader = (sbyte)c.Leader;
        c.Variables.Chosen[0] = leader;
        var virtue = c.Skill(Who.Leader, 9);
        if (c.Random(300) <= virtue)
        {
            ShowPicture(c, 331);
            switch (c.DragonSort())
            {
                case 0:
                    c.Screen.Text(2);
                    break;
                case 1:
                    c.Screen.Text(4);
                    break;
                case 2:
                    c.Screen.Text(1);
                    break;
                case 3:
                    c.Screen.Text(3);
                    break;
                default:
                    // DORMANT: no dragon sort beyond 3: no text.
                    break;
            }
            // the original passes 1 to thunk 09C0:216B; the rule takes no argument (R039+0x159A uses no argument: no bp in the asm).
            c.DragonSlain();
            c.Next = 0x0C;
            return;
        }
        c.Injure(leader, 10, 15, 1);
        ShowPicture(c, BeatenPicture);
        c.Screen.Text(5);
        Fight(c);
    }
}
