using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Drago08": signs of the thief who stole the dragon's golden cup (the dragon quest's fact
/// 4/7 counts them): a plundered village, a horseman snatched by the dragon, a giggling madman,
/// a dying witness, and at last the thief himself, to track, waylay, find with a saint's help
/// or run down. The cup (item 0x95) can calm the dragon.
/// </summary>
// orig: R083+0x146E (197E:000E) matched; tests: CardSmokeTests
// orig: R083+0x1CB6 (197E:0856) matched; R083+0x1D22 (197E:08C2) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 5..19 (never offered).
public sealed class ThiefOfTheDragonsHoard : Card
{
    public const int CardNumber = 0x9E;

    private const int Picture = 286;
    private const int WastePicture = 237;
    private const int DragonQuest = 4;
    private const int TheDragonsCup = 0x95;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        if (c.Events.FactAnywhere(DragonQuest, 8))
        {
            // The cup was recovered before: only the blighted land.
            c.Next = 0x97;
        }
        c.State.CurrentLocation = -1;
        // C: name kind 6 (the dragons' names) is read from the EXE's table (PersonalNames, NameToSlot).
        c.NameToSlot(6, c.DragonDetail(), 1);
        switch (c.Events.FactValueAnywhere(DragonQuest, 7))
        {
            case 0:
                ShowPicture(c, 128);
                c.Screen.Text(0);
                c.Next = 0x0C;
                break;
            case 1:
            case 2:
            case 3:
                ShowPicture(c, WastePicture);
                c.Screen.Text(c.Events.FactValueAnywhere(DragonQuest, 7));
                c.Next = 0x0C;
                break;
            case 4:
                ShowPicture(c, Picture);
                // orig: R083+0x161E: options_block = 4 (DS:EE53, which a later card may read); the other cases leave it.
                c.OptionsBlock = 4;
                break;
            default:
                c.Next = 0x97;
                break;
        }
        // C: the growth is drawn as an argument of event_update.
        var growth = c.Random(2);
        c.Events.Update(-1, -1, 0x1C, 1, growth, DragonQuest, EventUpdate.Add);
        c.Events.Update(-1, -1, 0x1C, 7, 1, DragonQuest, EventUpdate.Add);
        c.SetCardItems();
        c.SetCardSaints(0x25, 0x42, 0x43, 0x5D);
        var menu = Menu(c, 4);
        menu.Enable(0, 1, 3, 4);
        menu[2] = c.SaintsUsable();
        if (c.Next != CardNumber)
        {
            return c.Next;
        }
        return menu.Run(
            // orig: R083+0x1780 (197E:0320) matched; R083+0x1892 (197E:0432) matched; tests: CardSmokeTests
            new MenuLine(chance => TrackHim(c, chance), hint: () => PerceptionChance(c)),
            // orig: R083+0x18B4 (197E:0454) matched; R083+0x19BE (197E:055E) matched; tests: CardSmokeTests
            new MenuLine(chance => WaylayHim(c, chance), hint: () => WoodLoreChance(c)),
            // orig: R083+0x19E0 (line 2); R083+0x1D5A (197E:08FA) matched; R083+0x1DE4 (197E:0984) matched; R083+0x1E6E (197E:0A0E) matched;
            // R083+0x1EF8 (197E:0A98) matched; tests: CardSmokeTests
            new MenuLine(() => InvokeASaint(c), OptionHint.Saying(CardTexts.SelectSaint), c.SaintPopupHint),
            // orig: R083+0x1B4A (197E:06EA) matched; R083+0x1C46 (197E:07E6) matched; tests: CardSmokeTests
            new MenuLine(chance => RunHimDown(c, chance), hint: () => RidingChance(c)),
            // orig: R083+0x1C70 (197E:0810) matched; R083+0x1C7E (197E:081E) matched; tests: CardSmokeTests
            new MenuLine(() => { c.State.CurrentLocation = -1; c.Next = 0x0C; }, OptionHint.Saying(SuccessAssured)));
    }

    private static OptionHint Best(CardContext c, int chance)
    {
        c.Variables.Chosen[0] = (byte)c.BestMember;
        return OptionHint.ChanceOf(chance, (byte)c.BestMember);
    }

    private static OptionHint PerceptionChance(CardContext c) => Best(c, c.PartyMaxAttribute(3));

    private static OptionHint WoodLoreChance(CardContext c) => Best(c, c.PartyMaxSkill(0x12));

    private static OptionHint RidingChance(CardContext c) => Best(c, Math.Clamp(c.PartyMaxMountedAgility(), 0, 99));

    /// <summary>The cup is the leader's; the quest notes it for three years.</summary>
    private static void TheCupIsRecovered(CardContext c)
    {
        c.Screen.Text(7);
        c.Screen.Text(8);
        c.Events.Create(-2, c.Here, 0, 0x1C, 8, 0, 0, 26280, 0, DragonQuest);
        c.AddItem(c.Leader, TheDragonsCup);
    }

    private static void GoOn(CardContext c)
    {
        c.State.CurrentLocation = -1;
        c.Next = 0x0C;
    }

    // C: here a roll below the chance (<) succeeds.
    private static void TrackHim(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.PassHours(c.Random(3));
            ShowPicture(c, 287);
            c.Screen.Text(5);
            ShowPicture(c, WastePicture);
            TheCupIsRecovered(c);
        }
        else
        {
            c.PassHours(c.Random(6));
            ShowPicture(c, Picture);
            c.Screen.Text(6);
        }
        GoOn(c);
    }

    private static void WaylayHim(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            c.PassHours(2);
            ShowPicture(c, 287);
            c.Screen.Text(9);
            ShowPicture(c, WastePicture);
            TheCupIsRecovered(c);
        }
        else
        {
            c.PassHours(c.Random(4));
            ShowPicture(c, Picture);
            c.Screen.Text(6);
        }
        GoOn(c);
    }

    // orig: R083+0x19E0 (197E:0580) matched; R083+0x1B12 (197E:06B2) matched; tests: CardSmokeTests
    // C: the saint brings the party to the thief, but the script then neither records nor gives the cup.
    private static void InvokeASaint(CardContext c)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer != 0)
        {
            int[] saints = [0x25, 0x42, 0x43, 0x5D];
            if (c.MenuSlot is >= 0 and < 4)
            {
                c.NameToSlot(1, saints[c.MenuSlot], 0);
            }
            c.PassHours(2);
            ShowPicture(c, Picture);
            c.Screen.Text(0x0A);
            ShowPicture(c, WastePicture);
            c.Screen.Text(7, TextFrame.BlankPage);
            c.Screen.Text(8, TextFrame.BlankPage);
            GoOn(c);
            return;
        }
        ShowPicture(c, WastePicture);
        c.Screen.Text(0x0B);
        ShowPicture(c, Picture);
        c.Next = CardNumber;
    }

    private static void RunHimDown(CardContext c, int chance)
    {
        if (c.Random(100) < chance)
        {
            ShowPicture(c, WastePicture);
            if (c.PartyGet(5) != 0)
            {
                c.PassHours(4);
                c.Screen.Text(0x0C);
            }
            else
            {
                c.PassHours(1);
                c.Screen.Text(0x0D);
            }
            TheCupIsRecovered(c);
        }
        else
        {
            c.PassHours(3);
            ShowPicture(c, Picture);
            c.Screen.Text(0x0E);
        }
        GoOn(c);
    }
}
