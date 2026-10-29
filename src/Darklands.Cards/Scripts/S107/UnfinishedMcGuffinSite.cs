using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf07": an item-quest site whose texts were never written (the MSG file holds only empty
/// option lines). The script offers to look about (the party's lore decides what it learns),
/// to pray, to do something sinful (Virtue and Divine Favour fall) or to leave.
/// </summary>
// orig: R107+0x2D28 (1B0A:0008) matched; tests: CardSmokeTests
// orig: R107+0x31C6 (1B0A:04A6) matched (registration: saint pop-ups only, they do not name the member)
// C: no card of the game names 0xFF; the place may reach it as its arrival card. The scene's meaning is guessed from the script alone.
// DORMANT: R107+0x3260, R107+0x3262, R107+0x3264, R107+0x3266 are empty functions nothing calls.
public sealed class UnfinishedMcGuffinSite : Card
{
    public override int Number => 255;

    public override int Run(CardContext c)
    {
        Show(c, 0);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3);
        c.SetCardSaints(0x5D, 0x25, 0x11, 0x42);
        menu[1] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R107+0x2F60 (line 0); hint R107+0x2FFC (1B0A:02DC) matched
            new MenuLine(_ => LookAbout(c), Unknown),
            // orig: R107+0x3034 (line 1); hint R107+0x3078 (1B0A:0358) matched
            new MenuLine(_ => Pray(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R107+0x30B0 (1B0A:0390) matched; hint R107+0x30F0
            new MenuLine(_ => DoTheSinfulThing(c), Unknown),
            // orig: R107+0x3128 (1B0A:0408) matched; hint R107+0x3136
            new MenuLine(_ => c.LeaveForTheMap(), Unknown),
            // orig: R107+0x316E (1B0A:044E) matched; hint R107+0x317A
            // DORMANT: line 4 is never offered; it lets no time pass.
            new MenuLine(_ => c.PassHours(0), () => OptionHint.Say(SuccessAssured)),
            // DORMANT: lines 5..9 are never offered; actions R107+0x31B2.. and hints R107+0x31B4.. are empty
            // C: the empty hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>
    /// The place's number (modulo 4) chooses what is told, the lore which version. The
    /// original's switch has no breaks: places 0 and 1 hear all three tellings, place 2 two.
    /// </summary>
    // orig: R107+0x2F60 (1B0A:0240) matched; tests: CardSmokeTests
    // C: cur_loc % 4 is C's signed remainder: -1 in the wilderness matches no case and nothing is told.
    private static void LookAbout(CardContext c)
    {
        c.PassHours(1);
        var place = c.Here % 4;
        if (place is 0 or 1)
        {
            c.Screen.Text(c.PartyLoreLevel() <= 3 ? 4 : 5);
        }
        if (place is 0 or 1 or 2)
        {
            c.Screen.Text(c.PartyLoreLevel() <= 4 ? 8 : 9);
        }
        if (place is 0 or 1 or 2 or 3)
        {
            c.Screen.Text(c.PartyLoreLevel() <= 3 ? 6 : 7);
        }
        c.PassHours(1);
        c.Screen.Text(10);
        c.LeaveForTheMap();
    }

    // orig: R107+0x3034 (1B0A:0314) matched; tests: CardSmokeTests
    private static void Pray(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        c.Screen.Text(answer == 1 ? 1 : 2);
        c.Next = 255;
    }

    // orig: R107+0x30B0 (1B0A:0390) matched; tests: CardSmokeTests
    private static void DoTheSinfulThing(CardContext c)
    {
        c.PassHours(1);
        c.Screen.Text(3);
        c.AddSkill(Who.Everybody, (int)Skill.Virtue, -1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, -10);
        c.LeaveForTheMap();
    }
}
