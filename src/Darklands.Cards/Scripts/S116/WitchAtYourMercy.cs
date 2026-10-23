using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Witch00": the blonde witch (card 290) is at the party's mercy and offers, for her life:
/// unnatural strength (a deadly trick: the leader dies), three alchemical formulas each, the
/// next High Sabbat, the cult's calendar, or her penance.
/// </summary>
// orig: R116+0x1E4A (1A1C:000A) matched; tests: CardSmokeTests
// orig: R116+0x237E (1A1C:053E) matched (registration: an empty function; lines 10..19 keep an earlier card's handlers, all hidden)
public sealed class WitchAtYourMercy : Card
{
    public override int Number => 291;

    public override int Run(CardContext c)
    {
        Show(c, 162);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        c.StartQuest7();
        c.Variables.Chosen[0] = c.Leader;

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R116+0x206E (1A1C:022E) matched; hint R116+0x20C6
            new MenuLine(_ => TheStrengthRoots(c), Unknown),
            // orig: R116+0x20FE (1A1C:02BE) matched; hint R116+0x21EE
            new MenuLine(_ => HerFormulas(c), Unknown),
            // orig: R116+0x2226 (1A1C:03E6) matched; hint R116+0x2252
            new MenuLine(_ => { c.Variables.Location = c.SabbatSite(); c.Screen.Text(3); c.PassHours(1); c.LeaveForTheMap(); }, Unknown),
            // orig: R116+0x228A (1A1C:044A) matched; hint R116+0x22AE
            new MenuLine(_ => { c.Screen.Text(4); c.PassHours(1); c.LeaveForTheMap(); }, Unknown),
            // orig: R116+0x22E6 (1A1C:04A6) matched; hint R116+0x2332
            new MenuLine(_ => HerPenance(c), Unknown),
            // DORMANT: lines 5..9 are never offered; actions R116+0x236A.. and hints R116+0x236C.. are empty
            // C: the hints return AX/DX; neutral hints are used.
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }),
            new MenuLine(_ => { }));
    }

    private static OptionHint Unknown() => OptionHint.Say("You can always encounter the unknown");

    /// <summary>The leader eats her roots, stiffens and dies; she flies off laughing.</summary>
    // orig: R116+0x206E (1A1C:022E) matched; tests: CardSmokeTests
    private static void TheStrengthRoots(CardContext c)
    {
        var leader = c.Leader;
        c.Variables.Chosen[0] = leader;
        c.PassHours(2);
        c.Screen.Text(1);
        c.SetStatus(leader, CharacterStatus.Empty);
        c.DeleteMember(leader);
        c.PassHours(0);
        c.LeaveForTheMap();
    }

    /// <summary>Three random "formulas" (0..197) for each of the first four members.</summary>
    // orig: R116+0x20FE (1A1C:02BE) matched; tests: CardSmokeTests
    // C: only members 0..3 are looked at (i < 4). The formula table has 22 bytes per member, but a / 3 goes up to 65.
    private static void HerFormulas(CardContext c)
    {
        var a = c.Random(198);
        var b = c.Random(198);
        var d = c.Random(198);
        for (var i = 0; i < 4; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                SetFormulaBit(c, i, a);
                SetFormulaBit(c, i, b);
                SetFormulaBit(c, i, d);
            }
        }
        c.Screen.Text(2);
        c.PassHours(c.HoursUntil(18));
        c.LeaveForTheMap();
    }

    /// <summary>
    /// formulas_known[member][n / 3] |= 1 &lt;&lt; n % 3 as the original computes it, overflow included:
    /// byte n / 3 of a 22-byte row runs into the next members' rows and, past the fifth member's,
    /// into the saints table that follows (20 bytes per member, bit 0x80 &gt;&gt; saint % 8).
    /// </summary>
    // C: flat byte t = member × 0x16 + n / 3; t < 0x6E: formula (t % 0x16) × 3 + n % 3 of member t / 0x16;
    // otherwise byte u = t − 0x6E of saints_known (R136+0xE15C): saint (u % 20) × 8 + 7 − n % 3 of member u / 20.
    private static void SetFormulaBit(CardContext c, int member, int n)
    {
        var t = member * 0x16 + n / 3;
        var bit = n % 3;
        if (t < 5 * 0x16)
        {
            c.State.Party.Slot(t / 0x16).Formulas.Add(t % 0x16 * 3 + bit);
            return;
        }
        var u = t - 5 * 0x16;
        c.State.Party.Slot(u / 20).Saints.Add(u % 20 * 8 + 7 - bit);
    }

    // orig: R116+0x22E6 (1A1C:04A6) matched; tests: CardSmokeTests
    private static void HerPenance(CardContext c)
    {
        c.Screen.Text(5);
        c.PassHours(c.HoursUntil(5));
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 1, 20, 1);
        c.AddAttribute(Who.Everybody, (int)Attribute.DivineFavor, 35);
        c.LeaveForTheMap();
    }
}
