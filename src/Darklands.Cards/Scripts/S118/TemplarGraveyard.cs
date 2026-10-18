namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witcd00": the graveyard outside the Templars' fortress monastery. Speaking the right name
/// (Reinhard of OGronigen, on the dark stone) opens a door into the fortress; a wrong name, or
/// digging, calls up the guardians. A saint may point out the right or a wrong name.
/// Every way on is a battle started by the main loop (the card returns −2).
/// </summary>
// orig: R118+0x08AA (18C2:000A) matched; tests: CardSmokeTests
// orig: R118+0x10D8 (18C2:0838) matched (registration: item pop-ups name the member, saint pop-ups do not)
// DORMANT: text 4 (after the guardians fall) is never shown by this card.
public sealed class TemplarGraveyard : Card
{
    private const string TheRightName = "Reinhard of OGronigen";

    public override int Number => 297;

    public override int Run(CardContext c)
    {
        Show(c, 182);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 2, 3);
        // C: random_name(party_seed + cur_loc + 0x314.., 0, slot): the stones' names are fixed per place.
        var seed = c.State.Party.Seed + c.Here;
        c.RandomName(seed + 0x314, 0, 0);
        c.RandomName(seed + 0x315, 0, 1);
        c.Variables.Named[2] = TheRightName;
        c.RandomName(seed + 0x317, 0, 3);
        c.RandomName(seed + 0x318, 0, 4);
        var darkStoneKnown = c.Events.FactAnywhere(1, 0x12);
        menu[0] = darkStoneKnown ? OptionState.Enabled : OptionState.Hidden;
        menu[1] = darkStoneKnown ? OptionState.Hidden : OptionState.Enabled;
        c.SetCardSaints(0x11, 0x2B, 0x35, 0x6F);
        menu[4] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R118+0x0B7E (18C2:02DE) matched; hint R118+0x0BD4
            new MenuLine(_ => TheDoorOpens(c), Unknown),
            // orig: R118+0x0C0C (18C2:036C) matched; hint R118+0x0C36
            new MenuLine(_ => ReadTheStones(menu), Unknown),
            // orig: R118+0x0C6E (18C2:03CE) matched; hint R118+0x0C98
            new MenuLine(_ => DigUpAGrave(c), Unknown),
            // orig: R118+0x0CD0 (18C2:0430) matched; hint R118+0x0CF2
            new MenuLine(_ => StartBattle(c, 3, 0), Unknown),
            // orig: R118+0x0D2A (18C2:048A) matched; hint R118+0x0DCA
            new MenuLine(_ => CallOnASaint(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R118+0x0E02 (18C2:0562) matched; hint R118+0x0E5C
            new MenuLine(_ => TheGuardiansAppear(c), Unknown),
            // orig: R118+0x0E94 (18C2:05F4) matched; hint R118+0x0EEE
            new MenuLine(_ => TheGuardiansAppear(c), Unknown),
            // orig: R118+0x0F26 (18C2:0686) matched; hint R118+0x0F7C
            // The third stone ($NamedThreeName) is the right name.
            new MenuLine(_ => TheDoorOpens(c), Unknown),
            // orig: R118+0x0FB4 (18C2:0714) matched; hint R118+0x100E
            new MenuLine(_ => TheGuardiansAppear(c), Unknown),
            // orig: R118+0x1046 (18C2:07A6) matched; hint R118+0x10A0
            new MenuLine(_ => TheGuardiansAppear(c), Unknown));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>Sets up the battle the main loop fights when the card returns −2.</summary>
    private static void StartBattle(CardContext c, int mode, int enemy)
    {
        c.CardBattle.Mode = mode;
        c.CardBattle.Enemy = enemy;
        c.CardBattle.Seed = c.Random(1000);
        c.Next = -2;
    }

    /// <summary>The right name: a door opens in the fortress wall (battle mode 3, enemy 2).</summary>
    // orig: R118+0x0B7E (18C2:02DE) matched (twin R118+0x0F26); tests: CardSmokeTests
    private static void TheDoorOpens(CardContext c)
    {
        ShowPicture(c, 179);
        c.Screen.Text(5);
        StartBattle(c, 3, 2);
    }

    /// <summary>A wrong name: an alarm, and the guardians appear (battle mode 1, flag 0x80).</summary>
    // orig: R118+0x0E02 (18C2:0562) matched (twins 05f4, 0714, 07a6); tests: CardSmokeTests
    // C: DS:9080 |= -0x80 on a char: bit 0x80 of the main loop's battle flags.
    private static void TheGuardiansAppear(CardContext c)
    {
        ShowPicture(c, 183);
        c.Screen.Text(3);
        c.CardBattle.Flags |= 0x80;
        StartBattle(c, 1, 0);
    }

    /// <summary>The five readable names: speak one aloud.</summary>
    // orig: R118+0x0C0C (18C2:036C) matched; tests: CardSmokeTests
    private static void ReadTheStones(CardMenu menu)
    {
        menu.Block = 2;
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 4);
        menu.Set(OptionState.Enabled, 5, 6, 7, 8, 9);
    }

    // orig: R118+0x0C6E (18C2:03CE) matched; tests: CardSmokeTests
    private static void DigUpAGrave(CardContext c)
    {
        c.Screen.Text(6);
        c.AddSkill(Core.Characters.Who.Everybody, (int)Core.Characters.Skill.Virtue, -3);
        c.AddAttribute(Core.Characters.Who.Everybody, (int)Core.Characters.Attribute.DivineFavor, -10);
    }

    /// <summary>
    /// The first saint warns of $NamedOneName, the third of $NamedFiveName; the second and fourth
    /// light every stone but the dark one, which can then be read.
    /// </summary>
    // orig: R118+0x0D2A (18C2:048A) matched; tests: CardSmokeTests
    // C: any non-zero answer counts as answered; the prayer does not make the member $ChosenOneName.
    private static void CallOnASaint(CardContext c, CardMenu menu)
    {
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 0)
        {
            c.Screen.Text(10);
            return;
        }
        switch (c.MenuSlot)
        {
            case 0:
                c.Screen.Text(7);
                break;
            case 1:
            case 3:
                ShowPicture(c, 181);
                c.Screen.Text(8);
                menu.Set(OptionState.Hidden, 1, 4);
                menu.Set(OptionState.Enabled, 0, 2, 3);
                menu.Block = 1;
                break;
            case 2:
                c.Screen.Text(9);
                break;
        }
    }
}
