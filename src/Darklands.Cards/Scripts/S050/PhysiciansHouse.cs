using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$physi00": the house of a city's physician (his skill is a city event, kind 0x37). The
/// party judges his skill, asks him to look at its wounds (and then pays for treatment), begs
/// to study under him, offers him alchemical components, or leaves; by night he must be
/// roused, which may cost standing (or two groschen of apology). A party in bad standing is
/// turned away at the door. (Ported from the assembly: the original has no C.)
/// </summary>
// orig: R050+0x0000 (1838:0000) asm; tests: CardSmokeTests
// DORMANT: R050+0x0BEE (1838:0BEE), the init, closes all 20 lines and gives them the
//   "screwed up." handler R050+0x0C10; lines 7..19 stay closed.
public sealed class PhysiciansHouse : Card
{
    public const int CardNumber = 0x18;

    private const string MsgName = "$physi00";

    private const int Picture = 12, ConsultingPicture = 197, DoorPicture = 347;

    private const int Business = 0x14, CraftsQuarter = 0x1A;

    /// <summary>City events: the physician's skill (0x37, its value), treated lately (0x36), asked to teach lately (0x38), learning under a master (0x28, type 13 = Healing).</summary>
    private const int PhysicianSkill = 0x37, Treated = 0x36, AskedToTeach = 0x38, Apprenticed = 0x28;

    /// <summary>The menu ends to be offered again (its text block follows the time of day).</summary>
    private const int Redraw = int.MinValue;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        // C: arrived at night (DS:8DFA), kept for the whole visit.
        var arrivedAtNight = !c.IsDaylight;
        if (c.HereQuery(0) > -40)
        {
            // C: an daylight-test() whose result is thrown away.
            _ = c.IsDaylight;
        }
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Hidden, OptionState.Enabled, OptionState.Hidden };
        c.RandomName(c.HereQuery(0x21) + 0x320, 0, 0);
        c.PartyMaxSkill(0x0D);
        var healer = (byte)c.BestMember;
        c.Variables.Chosen[0] = healer;
        c.Variables.Chosen[1] = healer;
        if (!c.IsDaylight)
        {
            states[0] = OptionState.Hidden;
            states[2] = OptionState.Hidden;
            states[6] = OptionState.Enabled;
        }
        if (c.EventAtHere(Treated))
        {
            states[4] = OptionState.Shown;
        }
        int skill;
        if (!c.EventAtHere(PhysicianSkill))
        {
            // C: (unsigned) place-query 33 % 10, then the city size and a draw: the size first, then the draw.
            var digit = (int)((ushort)c.HereQuery(0x21) % 10);
            var size = c.HereQuery(2);
            skill = (short)((c.Random(4) + size - 3) * digit);
            c.Events.Create(-2, c.Here, 0x14, PhysicianSkill, 0, Math.Clamp(skill, 1, 99), 0, 168, 0, 0);
            // C: the price below uses the product before clamping.
        }
        else
        {
            skill = c.EventValueHere(PhysicianSkill);
        }
        if (c.EventAtHere(AskedToTeach))
        {
            states[2] = OptionState.Shown;
        }
        var wounded = WoundedMembers(c);
        c.Variables.Money[0] = Money.FromPfennigs((short)((skill / 10 + 12) * wounded));
        if (wounded == 0)
        {
            states[1] = OptionState.Shown;
        }
        c.StandingSeen = c.HereQuery(0);

        while (true)
        {
            if (c.StandingSeen <= -40)
            {
                c.Screen.Show(MsgName, PictureNames.Of(DoorPicture));
                c.Screen.Text(3);
                return Business;
            }
            var menu = Menu(c, c.IsDaylight ? 0 : 1);
            menu.Enable(0, 1, 2, 3, 4, 5, 6);
            for (var k = 0; k < states.Length; k++)
            {
                menu[k] = states[k];
            }
            c.Next = Number;
            var next = menu.Run(
                // orig: R050+0x051C (1838:051C) + R050+0x03B6 (1838:03B6) R050 seg 1838 asm
                Stay(c, chance => JudgeHisSkill(c, chance), () => JudgeChance(c)),
                // orig: R050+0x0642 (1838:0642) + R050+0x0568 (1838:0568) R050 seg 1838 asm
                Stay(c, _ => HaveTheWoundsExamined(c, states), () => OptionHint.Say(SuccessAssured, c.Leader)),
                // orig: R050+0x079A (1838:079A) + R050+0x067C (1838:067C) R050 seg 1838 asm
                Stay(c, () => BegToStudy(c), () => OptionHint.Say(CardTexts.HopeForTheBest, c.Variables.Chosen[0])),
                // orig: R050+0x083C (1838:083C) + R050+0x07C8 (1838:07C8) R050 seg 1838 asm
                Stay(c, chance => OfferComponents(c, chance), () => TradeChance(c)),
                // orig: R050+0x0A26 (1838:0A26) + R050+0x08A0 (1838:08A0) R050 seg 1838 asm
                Stay(c, chance => PayForTreatment(c, chance, states), () => TreatmentHint(c)),
                // orig: R050+0x0B5E (1838:0B5E) + R050+0x0AE2 (1838:0AE2) R050 seg 1838 asm
                Stay(c, () => Leave(c, arrivedAtNight), OptionHint.Saying(SuccessAssured)),
                // orig: R050+0x0BB6 (1838:0BB6) + R050+0x0B96 (1838:0B96) R050 seg 1838 asm
                Stay(c, () => LeaveWithAnApology(c), OptionHint.Saying(SuccessAssured)));
            if (next != Redraw)
            {
                return next;
            }
        }
    }

    /// <summary>An action that stays at the physician's offers the menu again (by day or by night text).</summary>
    private static MenuLine Stay(CardContext c, Action act, Func<OptionHint> hint) =>
        new(() =>
            {
                act();
                if (c.Next == CardNumber)
                {
                    c.Next = Redraw;
                }
            },
            hint);

    /// <summary>Like the other <c>Stay</c>, for an action that takes the chance of its hint.</summary>
    private static MenuLine Stay(CardContext c, Action<int> act, Func<OptionHint> hint) =>
        new(chance =>
            {
                act(chance);
                if (c.Next == CardNumber)
                {
                    c.Next = Redraw;
                }
            },
            hint);

    /// <summary>Members present whose Strength is below its maximum (wounded).</summary>
    private static int WoundedMembers(CardContext c)
    {
        var count = 0;
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty && c.Attribute(i, 1) < c.AttributeMaximum(i, 1))
            {
                count++;
            }
        }
        return count;
    }

    private void ShowTheRoom(CardContext c) => c.Screen.Show(MsgName, PictureNames.Of(ConsultingPicture));

    private void BackToTheHouse(CardContext c) => c.Screen.Show(MsgName, PictureNames.Of(Picture));

    /// <summary>The healer's Intelligence plus half his Charisma plus the physician's skill (not clamped).</summary>
    private static OptionHint JudgeChance(CardContext c)
    {
        var skill = c.EventValueHere(PhysicianSkill);
        var healer = c.Variables.Chosen[1];
        var chance = c.Attribute(healer, 4) + c.Attribute(healer, 5) / 2 + skill;
        return OptionHint.ChanceOf(chance, healer);
    }

    /// <summary>Judges his skill: the verdict ("Poor" .. "Excellent") into $Text1 and $Number1; a quack (skill 1) sends the party off.</summary>
    private void JudgeHisSkill(CardContext c, int chance)
    {
        var skill = c.EventValueHere(PhysicianSkill);
        if (c.Random(100) > chance)
        {
            c.PassHours(1);
            ShowTheRoom(c);
            c.Screen.Text(9);
            BackToTheHouse(c);
            c.Next = CardNumber;
            return;
        }
        if (skill <= 1)
        {
            ShowTheRoom(c);
            c.Screen.Text(0x0A);
            c.PassHours(1);
            c.Next = Business;
            return;
        }
        c.Variables.Numbers[0] = skill;
        c.Variables.Texts[0] = skill switch
        {
            < 20 => "Poor",
            < 40 => "Modest",
            < 60 => "Good",
            < 80 => "Very Good",
            _ => "Excellent",
        };
        c.PassHours(1);
        ShowTheRoom(c);
        c.Screen.Text(8);
        BackToTheHouse(c);
        c.Next = CardNumber;
    }

    /// <summary>He examines the wounds and names his price; the treatment line opens.</summary>
    private void HaveTheWoundsExamined(CardContext c, OptionState[] states)
    {
        c.PassHours(1);
        var price = c.EventValueHere(PhysicianSkill) / 10 + 12;
        var wounded = WoundedMembers(c);
        c.Variables.Money[0] = Money.FromPfennigs((short)(wounded * price));
        c.Variables.Numbers[0] = wounded;
        ShowTheRoom(c);
        c.Screen.Text(2);
        BackToTheHouse(c);
        states[4] = OptionState.Enabled;
        c.Next = CardNumber;
    }

    /// <summary>
    /// Begs to study: a physician better than the party's best healer refuses outright; one
    /// asked lately refuses again; else, in two of three years (by the city's seed), a good
    /// physician takes the party for a week (a training event of Healing) for a fee in $Money1;
    /// otherwise he puts it off for some days (and is not to be asked for 30 hours).
    /// </summary>
    private void BegToStudy(CardContext c)
    {
        var skill = c.EventValueHere(PhysicianSkill);
        ShowTheRoom(c);
        if (c.PartyMaxSkill(0x0D) > skill && skill > 1)
        {
            c.Screen.Text(6);
        }
        else if (c.EventAtHere(AskedToTeach))
        {
            c.Screen.Text(0x0B);
        }
        else if ((c.HereQuery(0x21) + c.State.Date.Year) % 3 != 0 && skill > 1)
        {
            c.Variables.Money[0] = Money.FromPfennigs(skill / 5 + 10);
            if (!c.EventTypeAtHere(Apprenticed, 0x0D))
            {
                c.Events.Create(-2, c.Here, 0x32, Apprenticed, 0x0D, 0x3C, 0, 168, 0, 0);
            }
            c.Screen.Text(4);
        }
        else
        {
            c.Variables.Numbers[0] = c.Random(4) + 1;
            c.Screen.Text(5);
            c.EventHere(AskedToTeach, 30);
        }
        BackToTheHouse(c);
        c.Next = CardNumber;
    }

    /// <summary>The leader's Speak Common and Charisma, the standing, and (city seed + month) mod 30; 0..75.</summary>
    private static OptionHint TradeChance(CardContext c)
    {
        var speech = c.Skill(c.Leader, 0x0A);
        var charisma = c.Attribute(c.Leader, 5);
        // C: an unsigned division: (unsigned)(place-query 33 + month) % 30.
        var luck = (int)((ushort)(c.HereQuery(0x21) + c.State.Date.Month) % 30);
        var chance = speech + charisma + luck + c.StandingSeen;
        return OptionHint.ChanceOf(Math.Clamp(chance, 0, 0x4B), c.Leader);
    }

    private void OfferComponents(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.Barter(9, 0x400);
            c.PassHours(1);
        }
        else
        {
            ShowTheRoom(c);
            c.Screen.Text(0x0C);
            BackToTheHouse(c);
        }
        c.Next = CardNumber;
    }

    /// <summary>"Not enough money" when the party cannot pay (−1), else 0.</summary>
    private static OptionHint TreatmentHint(CardContext c)
    {
        var price = c.EventValueHere(PhysicianSkill) / 10 + 12;
        var cost = Money.FromPfennigs((short)(price * WoundedMembers(c)));
        c.Variables.Money[0] = cost;
        return c.State.Party.Purse < cost
            ? OptionHint.Say(CardTexts.NotEnoughMoney)
            : OptionHint.ChanceOf(0, OptionHint.WholeParty);
    }

    /// <summary>
    /// Pays for the treatment: each wounded member regains a thirtieth of the physician's skill
    /// in Strength (1..99), or, from a quack, loses 1 or 2; then no treatment for 20 hours.
    /// </summary>
    private void PayForTreatment(CardContext c, int chance, OptionState[] states)
    {
        ShowTheRoom(c);
        if (chance == 0)
        {
            var skill = c.EventValueHere(PhysicianSkill);
            var price = skill / 10 + 12;
            c.Variables.Money[0] = Money.FromPfennigs((short)(price * WoundedMembers(c)));
            c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
            c.PassHours(1);
            for (var i = 0; i < c.PartySize; i++)
            {
                if (c.Status(i) != CharacterStatus.Empty && c.Attribute(i, 1) < c.AttributeMaximum(i, 1))
                {
                    var change = skill > 1 ? Math.Clamp(skill / 30, 1, 99) : c.Random(2) - 2;
                    c.AddAttribute(i, 1, change);
                }
            }
            c.EventHere(Treated, 20);
            c.Screen.Text(0x0D);
        }
        else
        {
            c.Screen.Text(0x0E);
        }
        BackToTheHouse(c);
        c.Next = CardNumber;
    }

    /// <summary>Leaves to the crafts quarter; having roused him at night, the party may lose 1..4 standing (one chance in two).</summary>
    private void Leave(CardContext c, bool arrivedAtNight)
    {
        if (arrivedAtNight && c.Random(100) <= 50)
        {
            // C: the draw comes before the standing is read.
            var loss = c.Random(4);
            c.LocationSet(c.Here, 0, c.HereQuery(0) - loss - 1);
            c.Screen.Show(MsgName, PictureNames.Of(DoorPicture));
            c.Screen.Text(7);
        }
        c.Next = CraftsQuarter;
    }

    /// <summary>Leaves to the business district, paying two groschen at night.</summary>
    private static void LeaveWithAnApology(CardContext c)
    {
        if (!c.IsDaylight)
        {
            c.State.Party.Purse = c.State.Party.Purse.Subtract(0, 2, 0);
        }
        c.Next = Business;
    }
}
