using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Time;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$CityC00": the city church by day. The party attends Mass (at the canonical hours, by the
/// city's size), confesses to a priest, asks when the next Mass is (where the city's politics
/// stir, a priest may draw the party aside), gives a tenth of its money to restore Divine
/// Favor, asks for sanctuary, or leaves for the main street or the side streets.
/// (Ported from the assembly: the original has no C.)
/// </summary>
// orig: R057+0x0000 (1838:0000) asm; tests: CardSmokeTests
// DORMANT: R057+0x098E (1838:098E), the init, enables all 20 lines with the "screwed up."
//   handler R057+0x09C6 and clears card_items/card_saints; lines 7..19 have no action (and
//   no text line).
public sealed class CityChurchByDay : Card
{
    public const int CardNumber = 0x34;

    private const string MsgName = "$CityC00";

    private const int Picture = 28, AltarPicture = 290, NavePicture = 155;

    private const int MainStreet = 0x06, SideStreet = 0x09, ChurchAtNight = 0x35, PoliticsCalls = 0x6F, Sanctuary = 0x81;

    private const int Politics = 2;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        c.Screen.Show(MsgName, PictureNames.Of(Picture));
        var states = new[] { OptionState.Enabled, OptionState.Enabled, OptionState.Enabled, OptionState.Enabled,
            OptionState.Enabled, OptionState.Enabled, OptionState.Enabled };
        c.StandingSeen = c.Reputation(c.Here);
        var purse = c.State.Party.Purse.TotalPfennigs;
        // BUG: only the low word of the purse in pfennigs is tested (unsigned): a rich party whose low word is under 10 cannot give.
        if ((ushort)purse < 10)
        {
            states[3] = OptionState.Shown;
        }
        c.Variables.Money[0] = Money.FromPfennigs((short)(purse / 10));
        c.Variables.Money[1] = c.State.Party.Purse;
        if (!c.Hostile() && c.StandingSeen > -10)
        {
            states[4] = OptionState.Shown;
        }
        c.SetCardItems();
        c.SetCardSaints();
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 5, 6);
        for (var k = 0; k < states.Length; k++)
        {
            menu[k] = states[k];
        }
        c.Next = Number;
        return menu.Run(
            // orig: R057+0x0394 (1838:0394) + R057+0x0214 (1838:0214) R057 seg 1838 asm
            new MenuLine(() => AttendMass(c), OptionHint.Saying(NoRisk)),
            // orig: R057+0x04C4 (1838:04C4) + R057+0x03CC (1838:03CC) R057 seg 1838 asm
            new MenuLine(_ => Confess(c), () => LeaderAssured(c)),
            // orig: R057+0x0644 (1838:0644) + R057+0x0500 (1838:0500) R057 seg 1838 asm
            new MenuLine(() => AskForTheNextMass(c), OptionHint.Saying(NoRisk)),
            // orig: R057+0x0852 (1838:0852) + R057+0x067C (1838:067C) R057 seg 1838 asm
            new MenuLine(_ => GiveATenth(c), () => LeaderAssured(c)),
            // orig: R057+0x08CA (1838:08CA) + R057+0x088E (1838:088E) R057 seg 1838 asm
            new MenuLine(() => { ShowPicture(c, NavePicture); c.Screen.Text(8); c.Next = Sanctuary; }, OptionHint.Saying(SuccessAssured)),
            // orig: R057+0x0910 (1838:0910) + R057+0x0902 (1838:0902) R057 seg 1838 asm
            // C: is_daylight() is called and its answer thrown away: the main street by day (card 6) even at night.
            new MenuLine(() => { _ = c.IsDaylight; c.Next = MainStreet; }, OptionHint.Saying(SuccessAssured)),
            // orig: R057+0x0956 (1838:0956) + R057+0x0948 (1838:0948) R057 seg 1838 asm
            new MenuLine(() => { _ = c.IsDaylight; c.Next = SideStreet; }, OptionHint.Saying(SuccessAssured)));
    }

    private static int ChurchForTheHour(CardContext c) => c.IsDaylight ? CardNumber : ChurchAtNight;

    /// <summary>"Success is assured" for the leader (the hint names him).</summary>
    private static OptionHint LeaderAssured(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        return OptionHint.Say(SuccessAssured, c.Leader);
    }

    /// <summary>
    /// Mass at the canonical hours: early always, later only in cities big enough. Each member
    /// regains Divine Favor (an eighth of Religion, a thirty-fifth of Latin, +1); else the
    /// priest names the next Mass's hour.
    /// </summary>
    private static void AttendMass(CardContext c)
    {
        var size = c.HereQuery(2);
        var nextMass = 6;
        var held = false;
        var band = TimeOfDay.BandFromOne(c.State.Date.Hour);
        switch (band)
        {
            case 2:
                held = true;
                break;
            case 3 or 4 or 5:
                var needed = band == 3 ? 5 : band == 4 ? 6 : 7;
                if (size >= needed)
                {
                    held = true;
                }
                else if (size > 3)
                {
                    nextMass = 0x12;
                }
                break;
            case 6:
                held = size >= 4;
                break;
            case 7:
                held = size >= 6;
                break;
        }
        if (held)
        {
            for (var i = 0; i < c.PartySize; i++)
            {
                c.AddAttribute(i, 6, c.Skill(i, 8) / 8 + c.Skill(i, 0x0B) / 35 + 1);
            }
            c.WaitUntilHour((TimeOfDay.BandFromOne(c.State.Date.Hour) + 1) * 3);
            ShowPicture(c, AltarPicture);
            c.Screen.Text(2);
            ShowPicture(c, Picture);
            c.Next = ChurchForTheHour(c);
            return;
        }
        c.PassHours(0);
        c.NameToSlot(3, nextMass, 0);
        ShowPicture(c, NavePicture);
        c.Screen.Text(1);
        c.Next = CardNumber;
    }

    /// <summary>
    /// Confession: it takes longer the worse the standing ($Number1 hours). A religious leader
    /// may practise Virtue; he regains 2..6 Divine Favor plus a tenth of his Religion.
    /// </summary>
    private static void Confess(CardContext c)
    {
        var standing = c.StandingSeen;
        // C: signed divisions: standing / −10 + 11 in good standing, standing / 20 + 12 in bad.
        // BUG: standing / -10 + 11 is 0 at standing 110..119 and negative from 120 (reachable: a quest's resolution adds
        // standing without a limit, R029+0x3E06); PassHours then turns the clock back, and the hour can become negative (R057+0x03CC).
        var hours = standing >= 0 ? standing / -10 + 11 : standing / 20 + 12;
        c.Variables.Numbers[0] = hours;
        ShowPicture(c, AltarPicture);
        c.Screen.Text(3);
        ShowPicture(c, Picture);
        c.PassHours(hours);
        var religion = c.Skill(Who.Leader, 8);
        // C: the && draws the second roll only when the first is within the Religion.
        if (c.Random(100) <= religion && c.Random(100) <= 25)
        {
            c.PracticeSkill(Who.Leader, 9, 1, 10, -1);
        }
        var favour = c.Random(5);
        favour += c.Skill(Who.Leader, 8) / 10 + 2;
        c.AddAttribute(c.Leader, 6, favour);
        c.Next = ChurchForTheHour(c);
    }

    /// <summary>
    /// The next Mass's hour by the time of day and the city's size; where the city's politics
    /// have taken a side, a priest takes the party aside instead (card 0x6F).
    /// </summary>
    // UNCERTAIN: in the small and late hours (band 0 and 1) the original names the hour of an
    //   uninitialised local (whatever its stack slot held); the C# names hour 0.
    private static void AskForTheNextMass(CardContext c)
    {
        var size = c.HereQuery(2);
        var hour = TimeOfDay.BandFromOne(c.State.Date.Hour) switch
        {
            2 => 6,
            3 => size >= 5 ? 9 : size == 4 ? 0x12 : 6,
            4 => size >= 6 ? 0x0C : size >= 4 ? 0x12 : 6,
            5 => size >= 7 ? 0x0F : size != 3 ? 0x12 : 6,
            6 => size >= 4 ? 0x12 : 6,
            7 => size >= 6 ? 0x15 : 6,
            _ => 0,
        };
        c.NameToSlot(3, hour, 0);
        if (c.Events.QuestAt(Politics, c.Here) && c.Events.FactValue(Politics, 0, c.Here) != 0)
        {
            ShowPicture(c, NavePicture);
            c.Screen.Text(9);
            c.State.SavedCard = CardNumber;
            c.Next = PoliticsCalls;
            return;
        }
        ShowPicture(c, NavePicture);
        c.Screen.Text(4);
        c.Next = CardNumber;
    }

    /// <summary>
    /// Gives a tenth of the money. The gift, shared out (a sixth per member present), first
    /// restores the Divine Favor of the member whose number is the party's best Religion
    /// value (original bug), then of the able members after him in turn, while the gift
    /// lasts; a large gift (over 600 pfennigs) also teaches Virtue (a thirtieth of Religion).
    /// </summary>
    // BUG: party_max_skill(8) returns the skill value, which the original then uses as a member number.
    // C: all values are 16-bit words: the gift is the low word of the tenth, compared signed.
    private static void GiveATenth(CardContext c)
    {
        var gift = (short)(c.State.Party.Purse.TotalPfennigs / 10);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(0, 0, gift);
        var share = gift / (c.PresentCount() * 6);
        var first = c.PartyMaxSkill(8);
        var lack = c.AttributeMaximum(first, 6) - c.Attribute(first, 6);
        c.AddAttribute(first, 6, Math.Clamp(lack, 0, 99));
        var given = 1;
        share -= Math.Clamp(lack, 0, 99);
        if (share > 0)
        {
            var remaining = share;
            var member = first;
            do
            {
                if (c.PresentCount() <= given++)
                {
                    break;
                }
                member++;
                if (c.PresentCount() < member)
                {
                    member = 0;
                }
                if (c.Status(member) == CharacterStatus.Able)
                {
                    lack = c.AttributeMaximum(member, 6) - c.Attribute(member, 6);
                    c.AddAttribute(member, 6, Math.Clamp(lack, 0, 99));
                    remaining -= Math.Clamp(lack, 0, 99);
                }
            }
            while (remaining > 0);
        }
        if (gift > 0x258)
        {
            for (var i = 0; i < 5; i++)
            {
                if (c.Status(i) != CharacterStatus.Empty)
                {
                    c.AddSkill(i, 9, c.Skill(i, 8) / 30);
                }
            }
        }
        ShowPicture(c, NavePicture);
        c.Screen.Text(gift < 0x78 ? 5 : gift < 0x258 ? 6 : 7);
        ShowPicture(c, Picture);
        c.PassHours(1);
        c.Next = ChurchForTheHour(c);
    }
}
