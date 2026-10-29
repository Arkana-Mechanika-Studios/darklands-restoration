using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Count03": an old walled monastery among fields worked by monks and peasants. The party
/// can ask at the gate, talk to the people in the fields, or pray for insight. Each monastery
/// is devout, businesslike or corrupt, by the place's number.
/// </summary>
// orig: R105+0x11C8 (1954:0008) matched; tests: CardSmokeTests
// orig: R105+0x1AEC (1954:092C) matched (registration: saint pop-ups only, they do not name the member)
public sealed class WalledMonasteryApproach : Card
{
    private const int MonasteryPicture = 179;

    public override int Number => 245;

    public override int Run(CardContext c)
    {
        Show(c, MonasteryPicture);
        c.Next = Number;
        // C: cur_loc % 2 is C's signed remainder; only its non-zero-ness counts.
        var menu = Menu(c, c.Here % 2 != 0 ? 0 : 1);
        menu.Set(OptionState.Enabled, 0, 1, 2, 7);
        if (c.EventAtHere(0x5A))
        {
            menu.Set(OptionState.Shown, 0, 1);
        }
        if (c.EventAtHere(0x5B))
        {
            menu[1] = OptionState.Shown;
        }
        c.SetCardSaints(0x3D, 0x42, 0x11, 0x5D);
        menu[2] = c.SaintsUsable();
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R105+0x1444 (line 0); hint R105+0x1610 (1954:0450) matched
            new MenuLine(p => AskForAdmittance(c, menu, p), () => AdmittanceHint(c)),
            // orig: R105+0x16BC (line 1); hint R105+0x17B8 (1954:05F8) matched
            new MenuLine(p => TalkInTheFields(c, menu, p), () => OptionHint.ChanceOf(Math.Clamp(
                c.Skill(Who.Leader, (int)Skill.SpeakCommon) + c.Attribute(Who.Leader, (int)Attribute.Charisma) * 2 + c.Skill(Who.Leader, (int)Skill.Woodwise) / 5,
                1, 99), c.Leader)),
            // orig: R105+0x1814 (line 2); hint R105+0x18DC (1954:071C) matched
            new MenuLine(_ => PrayForInsight(c), () => OptionHint.Say("Select a person and a saint.")),
            // DORMANT: lines 3..6, 8 and 9 are never offered (opt_flag = 0); each lets no time pass (cited per line below).
            // orig: R105+0x1914 (line 3)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x1958 (line 4)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x199C (line 5)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x19E0 (line 6)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x1A24 (1954:0864) matched (leaves without clearing cur_loc)
            new MenuLine(_ => c.Next = CardContext.MapCard, Assured),
            // orig: R105+0x1A64 (line 8)
            new MenuLine(_ => c.PassHours(0), Assured),
            // orig: R105+0x1AA8 (line 9)
            new MenuLine(_ => c.PassHours(0), Assured));
    }

    private static OptionHint Assured() => OptionHint.Say(SuccessAssured);

    /// <summary>0 devout, 1 a business, 2 corrupt (the place's number modulo 10: 0–4, 5–7, 8–9).</summary>
    internal static int MonasteryKind(CardContext c) => (c.Here % 10) switch { < 5 => 0, < 8 => 1, _ => 2 };

    /// <summary>A well-spoken, charming leader of fair standing is always admitted.</summary>
    // C: the hint also stores loc_reputation(cur_loc) in cur_rep (DS:EE74), which this card never reads.
    private static OptionHint AdmittanceHint(CardContext c)
    {
        var standing = c.HereQuery(0);
        int chance;
        if (standing > -10 && c.Skill(Who.Leader, (int)Skill.SpeakCommon) > 25 && c.Attribute(Who.Leader, (int)Attribute.Charisma) > 25)
        {
            chance = 99;
        }
        else
        {
            chance = standing + c.Attribute(Who.Leader, (int)Attribute.Charisma) / 2 + c.Skill(Who.Leader, (int)Skill.SpeakCommon) / 3;
        }
        return OptionHint.ChanceOf(Math.Clamp(chance, 1, 99), c.Leader);
    }

    /// <summary>
    /// Admitted, the party is shown in to the abbot's clerk (card 246) — or, if anyone is badly
    /// hurt, first to the convent's care (card 180, then 246). Refused, it is escorted away.
    /// </summary>
    // orig: R105+0x1444 (1954:0284) matched; tests: CardSmokeTests
    private static void AskForAdmittance(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.PassHours(1);
            ShowPicture(c, 292);
            c.Screen.Text(2);
            var hurt = 0;
            for (var i = 0; i < c.PartySize; i++)
            {
                if (c.Status(i) != CharacterStatus.Empty
                    && c.AttributeMaximum(i, (int)Attribute.Strength) - 10 > c.Attribute(i, (int)Attribute.Strength))
                {
                    hurt++;
                }
            }
            if (hurt != 0)
            {
                ShowPicture(c, 263);
                c.Screen.Text(16);
                c.State.SavedCard = 246;
                c.Next = 180;
                return;
            }
            switch (MonasteryKind(c))
            {
                case 0:
                    ShowPicture(c, 313);
                    c.Screen.Text(13);
                    break;
                case 1:
                    ShowPicture(c, 155);
                    c.Screen.Text(14);
                    break;
                default:
                    ShowPicture(c, 290);
                    c.Screen.Text(15);
                    break;
            }
            c.Next = 246;
            return;
        }
        menu[0] = OptionState.Shown;
        c.EventHere(0x5A, 168);
        c.PassHours(1);
        c.Screen.Text(17, TextFrame.BlankPage);
        c.Next = CardContext.MapCard;
    }

    /// <summary>The people in the fields talk of a happy or a harsh life; in a corrupt monastery they only sometimes do.</summary>
    // orig: R105+0x16BC (1954:04FC) matched; tests: CardSmokeTests
    // C: `cur_loc % 10 < 8 || rand_mod(2)`: the coin is drawn only for a corrupt house (short-circuit).
    private static void TalkInTheFields(CardContext c, CardMenu menu, int chance)
    {
        var told = false;
        if (c.Random(100) <= chance)
        {
            var kind = MonasteryKind(c);
            if (kind == 0)
            {
                ShowPicture(c, 302);
                c.Screen.Text(5);
                told = true;
            }
            else if (kind == 1 || c.Random(2) != 0)
            {
                ShowPicture(c, 160);
                c.Screen.Text(6);
                told = true;
            }
        }
        if (!told)
        {
            ShowPicture(c, 302);
            c.Screen.Text(4);
        }
        menu[1] = OptionState.Shown;
        c.EventHere(0x5B, 168);
        c.PassHours(1);
        ShowPicture(c, MonasteryPicture);
        c.Next = 245;
    }

    // orig: R105+0x1814 (1954:0654) matched; tests: CardSmokeTests
    private static void PrayForInsight(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var answer = c.InvokeSaint(c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        if (answer == -1)
        {
            return;
        }
        if (answer == 1)
        {
            ShowPicture(c, MonasteryPicture);
            c.Screen.Text(7 + MonasteryKind(c));
        }
        else
        {
            ShowPicture(c, 16);
            c.Screen.Text(12);
        }
        ShowPicture(c, MonasteryPicture);
        c.Next = 245;
    }
}
