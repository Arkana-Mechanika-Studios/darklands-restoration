using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$NunsH00": the silent nuns of a convent nurse the party. Their clerk asks for a contribution
/// each day; the party may pay, plead for charity or leave. Once everybody's Strength is back
/// within five points of its maximum, the abbess sends them on their way.
/// </summary>
// orig: R089+0x0000 (1838:0000) matched; tests: CardSmokeTests
public sealed class ConventInfirmary : Card
{
    private const int CellPicture = 290;
    private const int ClerkPicture = 295;
    private const int KindClerkPicture = 299;

    /// <summary>The convent's care is remembered here for 2,688 hours (four months).</summary>
    private const int NursedHere = 0x31;

    public override int Number => 0xB4;

    public override int Run(CardContext c)
    {
        Show(c, CellPicture);
        c.Next = Number;
        // The clerk asks for a pfennig per thousand the party carries, plus one.
        // C: purse-in-pfennigs is a long cast to unsigned 16 bits before the division (wraps above 65,535).
        var contribution = Money.FromPfennigs((ushort)c.State.Party.Purse.TotalPfennigs / 1000 + 1);
        c.Variables.Money[0] = contribution;
        if (!c.EventAtHere(NursedHere))
        {
            c.EventHere(NursedHere, 0xA80);
            c.Screen.Text(0);
            c.PassHours(2);
            NurseEverybody(c);
        }
        ShowPicture(c, ClerkPicture);
        var request = c.Random(3) switch
        {
            1 => 7,
            2 => 8,
            _ => 2,
        };
        // orig: R089+0x0488 (1838:0488) matched
        c.SetCardSaints();
        c.SetCardItems();

        var menu = Menu(c);
        menu.Enable(1, 2);
        menu.EachRound = () =>
        {
            // C: tested before each menu round; leaving shows text 3 with the cell picture.
            if (EverybodyRecovered(c))
            {
                ShowPicture(c, CellPicture);
                c.Screen.Text(3);
                c.Next = c.ReturnCard;
                return;
            }
            menu.Block = request;
            menu[0] = c.State.Party.Purse < contribution ? OptionState.Shown : OptionState.Enabled;
        };
        return menu.Run(
            // orig: R089+0x02A2 (1838:02A2); hint R089+0x02F4 matched
            new MenuLine(() => Pay(c, contribution), () => OptionHint.Say(NoRisk, c.Leader)),
            // orig: R089+0x032E (1838:032E); hint R089+0x03E8 matched
            new MenuLine(() => PleadForCharity(c, CharityChance(c, contribution)),
                () => OptionHint.ChanceOf(CharityChance(c, contribution), c.Leader)),
            // orig: R089+0x043C (1838:043C); hint R089+0x044E matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.ReturnCard; }, () => OptionHint.Say(NoRisk, c.Leader)));
    }

    /// <summary>A day's rest in the nuns' care: every member in a slot gains up to two points of Strength.</summary>
    // orig: R089+0x022E (1838:022E) matched
    // C: the slot test is status != 0 (any member in the slot, also unconscious or dead).
    private static void NurseEverybody(CardContext c)
    {
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty)
            {
                c.AddAttribute(i, (int)Attribute.Strength, c.Random(3));
            }
        }
    }

    /// <summary>Nobody (of all the slots in use) lacks more than five points of Strength.</summary>
    // orig: R089+0x0264 (1838:0264) matched
    private static bool EverybodyRecovered(CardContext c)
    {
        for (var i = 0; i < c.PartySize; i++)
        {
            if (c.AttributeMaximum(i, (int)Attribute.Strength) - 5 >= c.Attribute(i, (int)Attribute.Strength))
            {
                return false;
            }
        }
        return true;
    }

    private void Pay(CardContext c, Money contribution)
    {
        c.Screen.Text(4);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(contribution);
        NurseEverybody(c);
        c.PassHours(c.HoursUntil(5) + 2);
        c.Next = Number;
    }

    /// <summary>Virtue and the leader's Charisma; a party that truly cannot pay is believed nine times in ten.</summary>
    private static int CharityChance(CardContext c, Money contribution)
    {
        var chance = c.PartyAverageSkill((int)Skill.Virtue) + c.Attribute(Who.Leader, (int)Attribute.Charisma);
        if (c.State.Party.Purse < contribution)
        {
            chance = 90;
        }
        return Math.Clamp(chance, 0, 100);
    }

    private void PleadForCharity(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            ShowPicture(c, KindClerkPicture);
            c.Screen.Text(5);
            ShowPicture(c, ClerkPicture);
            NurseEverybody(c);
            c.WaitUntilHour(7);
            c.Next = Number;
            return;
        }
        ShowPicture(c, CellPicture);
        c.Screen.Text(6);
        ShowPicture(c, ClerkPicture);
        // Thrown out: to the city church, unless the party was brought here from card 0xF6.
        c.Next = c.State.SavedCard != 0xF6 ? 0x13 : c.State.SavedCard;
    }
}
