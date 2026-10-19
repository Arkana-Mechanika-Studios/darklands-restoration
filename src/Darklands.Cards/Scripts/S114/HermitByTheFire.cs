using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$MeetH00": an old hermit by his fire in a remote valley. If the leader draws him out, the
/// party can stay a day to learn about the faith, pray with him for Divine Favour, or hear of
/// a saint new to one of the first four members.
/// </summary>
// orig: R114+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R114+0x097A (1838:097A) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
public sealed class HermitByTheFire : Card
{
    private const int HermitPicture = 155;

    public override int Number => 280;

    public override int Run(CardContext c)
    {
        Show(c, 286);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1);
        NameTheHermit(c);
        c.Variables.Chosen[0] = 0;
        c.Variables.Chosen[1] = 1;
        c.Variables.Chosen[2] = 2;
        c.Variables.Chosen[3] = 3;

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R114+0x02A6 (line 0); hint R114+0x0356 (1838:0356) matched
            new MenuLine(p => GoAndMeetHim(c, menu, p), () => MeetingHint(c)),
            // orig: R114+0x03E4 (1838:03E4) matched; hint R114+0x03F2
            new MenuLine(_ => c.LeaveForTheMap(), () => OptionHint.Say(SuccessAssured)),
            // orig: R114+0x042A (1838:042A) matched; hint R114+0x0490
            new MenuLine(_ => LearnAboutTheFaith(c), NoRiskForAll),
            // orig: R114+0x04C8 (1838:04C8) matched; hint R114+0x052E
            new MenuLine(_ => PrayWithHim(c), NoRiskForAll),
            // orig: R114+0x0566 (1838:0566) matched; hint R114+0x0622
            new MenuLine(_ => HearOfASaint(c, 0), NoRiskForAll),
            // orig: R114+0x065A (1838:065A) matched; hint R114+0x0716
            new MenuLine(_ => HearOfASaint(c, 1), NoRiskForAll),
            // orig: R114+0x074E (1838:074E) matched; hint R114+0x080A
            new MenuLine(_ => HearOfASaint(c, 2), NoRiskForAll),
            // orig: R114+0x0842 (1838:0842) matched; hint R114+0x08FE
            new MenuLine(_ => HearOfASaint(c, 3), NoRiskForAll),
            // orig: R114+0x0936 (1838:0936) matched; hint R114+0x093E (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard, NoRiskForAll),
            // orig: R114+0x0976 (1838:0976) matched (empty); hint R114+0x0978 (empty)
            // DORMANT: line 9 is never offered. UNCERTAIN: its hint returns AX/DX; a neutral hint is used.
            new MenuLine(_ => { }));
    }

    private static OptionHint NoRiskForAll() => OptionHint.Say(NoRisk);

    /// <summary>
    /// The hermit's name: a random personal name in $Named2, and its first word (at most nine
    /// letters) in $Named1.
    /// </summary>
    // C: random_name(time(0), 0, 1), then _fstrncpy(named[0], named[1], clamp(0, 9, strcspn(named[1], " "))) and a terminator.
    // DIVERGENCE: the seed is the wall clock (time(0)); the game's injected clock seed is used, 0 without one.
    private static void NameTheHermit(CardContext c)
    {
        var seed = unchecked((int)(c.State.ClockSeed?.Invoke() ?? 0));
        c.RandomName(seed, 0, 1);
        var full = c.Variables.Named[1];
        var space = full.IndexOf(' ', StringComparison.Ordinal);
        var length = Math.Clamp(space < 0 ? full.Length : space, 0, 9);
        c.Variables.Named[0] = full[..Math.Min(length, full.Length)];
    }

    // C: no side effect; the leader's speech, Virtue, Religion and Perception.
    private static OptionHint MeetingHint(CardContext c)
    {
        var l = c.Leader;
        var chance = c.Skill(l, (int)Skill.SpeakCommon) / 3 + c.Skill(l, (int)Skill.Virtue) / 3 + c.Skill(l, (int)Skill.Religion) / 4
            + c.Attribute(l, (int)Attribute.Perception) / 2;
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 99), l);
    }

    /// <summary>Drawn out, he offers a day of teaching (a saint only for members who are there); otherwise the party moves on.</summary>
    // orig: R114+0x02A6 (1838:02A6) matched; tests: CardSmokeTests
    private static void GoAndMeetHim(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            for (var k = 0; k < 4; k++)
            {
                menu[4 + k] = c.Status(k) != CharacterStatus.Empty ? OptionState.Enabled : OptionState.Shown;
            }
            menu.Set(OptionState.Enabled, 2, 3, 8);
            menu.Set(OptionState.Hidden, 0, 1, 9);
            ShowPicture(c, HermitPicture);
            menu.Block = 2;
            c.Next = 280;
            return;
        }
        c.Screen.Text(1);
        c.LeaveForTheMap();
    }

    // orig: R114+0x042A (1838:042A) matched; tests: CardSmokeTests
    private static void LearnAboutTheFaith(CardContext c)
    {
        ShowPicture(c, HermitPicture);
        c.Screen.Text(3);
        c.PracticeSkill(Who.Everybody, (int)Skill.Religion, 1, 15, 1);
        c.PassHours(c.HoursUntil(12));
        c.LeaveForTheMap();
    }

    // orig: R114+0x04C8 (1838:04C8) matched; tests: CardSmokeTests
    private static void PrayWithHim(CardContext c)
    {
        ShowPicture(c, HermitPicture);
        c.Screen.Text(4);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 25);
        c.PassHours(c.HoursUntil(12) + 24);
        c.LeaveForTheMap();
    }

    /// <summary>A random saint (0..135): new to the member, it is learnt; known already, only good stories.</summary>
    // orig: R114+0x0566 (1838:0566) matched (twins 065a, 074e, 0842 for members 1..3); tests: CardSmokeTests
    // C: the member's 20-byte saint bit set at R136+0xE15C + 0x14 × member, bit 0x80 >> (saint % 8) of byte saint / 8.
    // C: (hours_until(12) + 24) % 25: a day and the hours to noon, folded into 0..24.
    private static void HearOfASaint(CardContext c, int member)
    {
        var saint = c.Random(0x88);
        ShowPicture(c, HermitPicture);
        if (c.KnowsSaint(member, saint))
        {
            c.Screen.Text(6);
        }
        else
        {
            c.LearnSaint(member, saint);
            c.Screen.Text(5);
        }
        c.PassHours((c.HoursUntil(12) + 24) % 25);
        c.LeaveForTheMap();
    }
}
