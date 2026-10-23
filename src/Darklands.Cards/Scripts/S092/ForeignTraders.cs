using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Forei03": among the foreign traders of the city. The party may trade with them, sound out
/// their politics, ask to meet their leader (0xC0), or go on to the market, the street or the
/// main street.
/// </summary>
// orig: R092+0x0E80 (1920:0000) matched; tests: CardSmokeTests
public sealed class ForeignTraders : Card
{
    private const int Picture = 0;

    public override int Number => 0xC2;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // (The original also takes its own copy of the reputation here, which nothing reads.)
        // orig: R092+0x13C6 (1920:0546) matched
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 3, 7, 8, 9);
        return menu.Run(
            new MenuLine(() => Trade(c)),
            new MenuLine(() => SoundOutTheirPolitics(c), OptionHint.Saying(NoRisk)),
            // orig: R092+0x1178 (1920:02F8) matched; hint R092+0x117A (r.a = 0, r.b = -2)
            // DORMANT: hidden line 2, no action, hint 0 %.
            new MenuLine(() => { }, () => OptionHint.ChanceOf(0, Who.Everybody)),
            new MenuLine(chance => AskForTheirLeader(c, chance), () => OptionHint.ChanceOf(MeetingChance(c), Who.Everybody)),
            // orig: R092+0x1244 (1920:03C4) matched
            // DORMANT: hidden line 4, no action.
            new MenuLine(() => { }, () => OptionHint.ChanceOf(100, Who.Everybody)),
            // orig: R092+0x125C (1920:03DC); R092+0x1290 (1920:0410) matched
            // DORMANT: hidden lines 5 and 6 would end the game (0x62).
            new MenuLine(() => TheEnd(c), () => OptionHint.ChanceOf(100, Who.Everybody)),
            new MenuLine(() => TheEnd(c), () => OptionHint.ChanceOf(100, Who.Everybody)),
            // orig: R092+0x12C4; R092+0x131A; R092+0x1370 matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 0x15 : 0x16; }),
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 9 : 10; }),
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 6 : 8; }));
    }

    /// <summary>The foreign traders' wares (categories 0x3843F); the party stays among them afterwards.</summary>
    // orig: R092+0x10A4 (1920:0224) matched
    // C: the category mask is pushed as two words, −0x7BC1 and 3: 0x0003843F.
    private static void Trade(CardContext c)
    {
        // C: _fstrcpy(2E38:1A96, the far pointer at 290E:2117) (R092+0x10A4, segment word DS:810A): $NamedOneName is the fifth
        //   title, "foreign trader" (without the EXE's tables the slot keeps its text).
        c.Variables.Named[0] = c.PersonalNames.Count(NameTable.Titles) > 0 ? c.PersonalNames.Name(NameTable.Titles, 4) : c.Variables.Named[0];
        c.Variables.Chosen[0] = c.Leader;
        c.Screens.Barter(-1, "Foreign Trader", 0x3843F);
    }

    // orig: R092+0x1114 (1920:0294) matched
    private static void SoundOutTheirPolitics(CardContext c)
    {
        c.Support[0] = c.SideOf(0x1000);
        c.Screen.Text(3, TextFrame.BlankPage);
        c.PassHours(1);
        c.Next = 0xC2;
    }

    // orig: R092+0x11EA (1920:036A) matched
    private static int MeetingChance(CardContext c) =>
        Math.Clamp(c.ReputationHere + c.PartyGet(4) / 0x19 + c.PartyAverageAttribute((int)Attribute.Charisma) / 2, 0, 99);

    // orig: R092+0x1190 (1920:0310) matched
    private static void AskForTheirLeader(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 2, 2);
            c.Screen.Text(1);
            c.State.SavedCard = 0xC2;
            c.Next = 0xC0;
            return;
        }
        c.AdjustReputation(c.Here, -2, -2);
        c.Screen.Text(2);
        c.Next = 0xC2;
    }

    private static void TheEnd(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
