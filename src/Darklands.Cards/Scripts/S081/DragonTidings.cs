using Darklands.Core.Characters;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago00": tidings of a dragon. Before the dragon quest, a Schulz, a castle lord or a
/// city's lord tells of strange ravages (and the quest begins); after the dragon's death, they
/// thank the party, and a castle lord or a city's ruler may offer to make one member his heir.
/// </summary>
// orig: R081+0x12AC (1962:000C) matched; tests: CardSmokeTests
// orig: R081+0x17D8 (1962:0538) matched; R081+0x1810 (1962:0570) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
public sealed class DragonTidings : Card
{
    public const int CardNumber = 0x96;

    private const int DragonQuest = 4;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, 128);
        c.Next = CardNumber;
        var block = 0x0C;
        var menu = Menu(c);
        menu.Enable(0, 1, 2, 3, 4);
        var previous = c.State.PreviousCard;
        if (!c.Events.ResolvedQuestExists(DragonQuest))
        {
            if (c.Events.QuestExists(DragonQuest))
            {
                return c.Next = previous;
            }
            // the original passes (9 / 10 / 11, location-now) to thunk 09C0:1F09; the rule ignores them (R039+0x11FC reads no argument: no [bp+n] operand in the asm; 24 of 28 blocks hit in the card oracle).
            switch (previous)
            {
                case 0x86:
                    c.StartDragonQuest();
                    c.Screen.Text(c.Random(3), TextFrame.BlankPage);
                    c.Next = 0x86;
                    break;
                case 0x2B:
                case 0x2C:
                    c.StartDragonQuest();
                    c.Screen.Text(c.Random(3) + 6, TextFrame.BlankPage);
                    c.Next = c.IsDaylight ? 0x2B : 0x2C;
                    break;
                case 0x89:
                    c.Screen.Text(9, TextFrame.BlankPage);
                    c.Next = 0x89;
                    break;
                case 0x1D:
                case 0x1E:
                    c.Screen.Text(0x0A, TextFrame.BlankPage);
                    c.Next = c.IsDaylight ? 0x1D : 0x1E;
                    break;
                default:
                    c.StartDragonQuest();
                    c.Screen.Text(c.Random(3) + 3, TextFrame.BlankPage);
                    c.Next = previous;
                    break;
            }
        }
        else if (previous == 0x86)
        {
            c.Screen.Text(0x0B, TextFrame.BlankPage);
            c.Next = previous;
        }
        else
        {
            NameTheCandidates(c);
            int count;
            int thanks;
            if (previous != 0x2B && previous != 0x2C)
            {
                // A castle lord.
                count = c.PartySize - c.HirelingCount;
                thanks = 0x0D;
            }
            else
            {
                // A city's ruler: the city's standing rises by 50 (at most 100).
                var standing = c.HereQuery(0);
                c.LocationSet(c.Here, 0, Math.Clamp(standing + 50, 0, 100));
                // C: the city's choice counts the hirelings (DS:A67C), not the members.
                count = c.HirelingCount;
                thanks = 0x0E;
            }
            // C: the cases fall through: 2 hides lines 3 and 2, 3 hides line 2; 2..4 pick the heir text.
            switch (count)
            {
                case 1:
                    c.Screen.Text(thanks, TextFrame.BlankPage);
                    break;
                case 2:
                    menu[3] = OptionState.Hidden;
                    menu[2] = OptionState.Hidden;
                    block = thanks == 0x0D ? 0x0C : 0x0F;
                    break;
                case 3:
                    menu[2] = OptionState.Hidden;
                    block = thanks == 0x0D ? 0x0C : 0x0F;
                    break;
                case 4:
                    block = thanks == 0x0D ? 0x0C : 0x0F;
                    break;
                default:
                    // Nothing shown: the heir list (block 0x0C) follows.
                    break;
            }
        }
        c.SetCardSaints();
        c.SetCardItems();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }

        MenuLine Heir(int slot) => new(() =>
        {
            // The member retires as the lord's heir.
            if (slot < c.PartySize)
            {
                c.SetStatus(slot, CharacterStatus.Empty);
            }
            c.Next = previous;
        }, () => OptionHint.Say(SuccessAssured, slot == 0 ? c.Variables.Chosen[0] : OptionHint.WholeParty));
        menu.Block = block;
        return menu.Run(
            // orig: R081+0x169A (1962:03FA) matched; R081+0x16A6 (1962:0406) matched; tests: CardSmokeTests
            Heir(0),
            // orig: R081+0x16DE (1962:043E) matched; R081+0x16EA (1962:044A) matched; tests: CardSmokeTests
            Heir(1),
            // orig: R081+0x171C (1962:047C) matched; R081+0x1728 (1962:0488) matched; tests: CardSmokeTests
            Heir(2),
            // orig: R081+0x175A (1962:04BA) matched; R081+0x1766 (1962:04C6) matched; tests: CardSmokeTests
            // UNCERTAIN: the hints of lines 1..3 (R081+0x16EA/0x1728/0x1766) return their member word [bp-2] unset (enter 4,0; only
            //   line 0's sets it, to $ChosenOneName): a stack leftover of the caller's frame (card-mouse-handler R043+0x26C4 under Shift; the card
            //   oracle's call gives 0). The whole party is shown here.
            Heir(3),
            // orig: R081+0x1798 (1962:04F8) matched; R081+0x17A0 (1962:0500) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = previous, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>$ChosenOneName .. $ChosenFourName: the members of slots 0..3 when able, else slot 0.</summary>
    private static void NameTheCandidates(CardContext c)
    {
        var chosen = c.Variables.Chosen;
        // C: the first is 0 either way (party == 1 ? 0 : 0).
        chosen[0] = 0;
        chosen[1] = c.Status(1) == CharacterStatus.Able ? 1 : 0;
        chosen[2] = c.Status(2) == CharacterStatus.Able ? 2 : 0;
        chosen[3] = c.Status(3) == CharacterStatus.Able ? 3 : 0;
    }
}
