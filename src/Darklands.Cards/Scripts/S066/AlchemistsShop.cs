using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Alche00" (card 0x59): the city's best alchemist. If he deigns to see the party (by day,
/// when the party's best alchemist impresses him), it may improve its philosopher's stone,
/// buy potions or components, ask about alchemical knowledge (card 0x53), offer a formula in
/// exchange for one of his, or ask about a task he would pay for.
/// </summary>
// orig: R066+0x0D48 (190C:0008) asm (the reference has no C for R066 seg 190C); tests: CardSmokeTests
// orig: handler setup R066+0x178C (all twenty lines on, unused-line handler 0aee "screwed up.",
// item pop-ups 0b26/0b56/0b86/0bb6, saint pop-ups 0be6/0c70/0cfa/0d84; card_items and card_saints cleared)
public sealed class AlchemistsShop : Card
{
    private const int ShopPicture = 74;
    private const int WorkshopPicture = 75;
    private const int Streets = 0x1A;
    private const int AlchemicalKnowledge = 0x53;

    /// <summary>"The alchemist will not see you again today" (60 hours).</summary>
    private const int ThrownOut = 0x44;

    /// <summary>A formula exchange has been asked for here (60 hours).</summary>
    private const int ExchangeAsked = 0x62;

    public override int Number => 0x59;

    public override int Run(CardContext c)
    {
        // The card table has no name for card 0x59; the original copies "$Alche00" (DS:5CBA).
        ShowTheShop(c);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Enable(0, 1, 2, 3, 4, 9);
        var quality = ShopQuality(c, withCapitalBonus: true);
        c.PartyMaxSkill((int)Skill.Alchemy);
        c.Variables.Chosen[0] = (byte)c.BestMember;
        c.RandomName(c.HereQuery(0x21) + 0x49, 0, 0);
        c.Variables.Texts[3] = quality >= 25 ? "Potions" : "Alchemical Components";
        if (quality < 25)
        {
            menu[2] = OptionState.Hidden;
        }
        c.Variables.Money[0] = Money.From(0, StonePrice(c, quality), 0);
        if (c.State.Party.Purse < c.Variables.Money[0])
        {
            menu[0] = OptionState.Shown;
        }
        // Whether he sees the party at all: his whim (party seed + place, 0..29) against the party's
        // best alchemist (10..99). After the knowledge card (0x53) he always does.
        // C: the result is kept in saved_card2 (DS:E896); the comparison is unsigned.
        var whim = (ushort)(c.State.Party.Seed + c.Here) % 30;
        c.State.SavedCard2 = whim < (ushort)Math.Clamp(c.PartyMaxSkill((int)Skill.Alchemy), 10, 99) ? 1 : 0;
        if (c.PreviousCard == AlchemicalKnowledge)
        {
            c.State.SavedCard2 = 1;
        }
        // C: cur_rep (loc_reputation) is read and not used.
        _ = c.Reputation(c.Here);
        c.SetCardItems();
        c.SetCardSaints();
        var offered = c.Random(21);
        c.Variables.Texts[1] = FormulaName(c, offered);
        // C: the formula number goes into card_items[0]; no line of this card offers the item pop-up.
        c.CardItems[0] = offered;
        if (c.EventAtHere(ExchangeAsked))
        {
            menu[3] = OptionState.Shown;
        }

        var rounds = 0;
        menu.EachRound = () =>
        {
            if (c.EventAtHere(ThrownOut))
            {
                LeaveWith(c, 6);
            }
            else if (c.State.SavedCard2 == 0)
            {
                // Nobody knows enough alchemy to find a true alchemist.
                LeaveWith(c, 2);
            }
            else if (!c.IsDaylight)
            {
                LeaveWith(c, 3);
            }
            else
            {
                menu.Block = rounds++ == 0 ? 0 : 1;
            }
        };
        Func<OptionHint> asLeader = () => new OptionHint(Persuasion(c), "", (sbyte)c.Leader);
        return menu.Run(
            new MenuLine(chance => ImproveTheStone(c, menu, chance), asLeader),
            new MenuLine(chance => BuyFromHim(c, chance), asLeader),
            new MenuLine(chance => AskAboutKnowledge(c, chance), asLeader),
            new MenuLine(chance => OfferAnExchange(c, menu, chance), asLeader),
            new MenuLine(chance => AskForATask(c, menu, chance), asLeader),
            // DORMANT: line 5 (opt_flag_5 = 0) has an empty action. orig: R066+0x1662 (hint 0924 R066+0x1664)
            new MenuLine(_ => { }, asLeader),
            // DORMANT: lines 6..8 (opt_flag = 0): time passes. orig: R066+0x167E/0982/09c6 (hints 094a/098e/09d2)
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            new MenuLine(_ => c.PassHours(0), OptionHint.Saying(SuccessAssured)),
            // orig: R066+0x174A (hint R066+0x1752): back to the streets.
            new MenuLine(_ => c.Next = Streets, () => new OptionHint(-1, SuccessAssured, (sbyte)c.Leader)));
    }

    private static void ShowTheShop(CardContext c) => c.Screen.Show("$Alche00", PictureNames.Of(ShopPicture));

    // DORMANT: the item pop-ups this card installs (0b26 R066+0x1866 .. 0bb6: the party's weaker of Stealth and
    // Streetwise + 25, 30, 37, 45) and its saint pop-ups (0be6 R066+0x1926 ..) belong to no option line.

    private static void LeaveWith(CardContext c, int text)
    {
        c.Screen.Text(text);
        c.Next = Streets;
    }

    private static string FormulaName(CardContext c, int formula) =>
        formula >= 0 && formula < c.World.Items.Formulae.Count ? c.World.Items.Formulae[formula].FullName : "";

    /// <summary>
    /// The shop's quality: three times the city size plus a city number remainder, and in
    /// capitals (city query 0x1B) 10..20 more.
    /// </summary>
    // C: (unsigned)(q + 4) % 41 and (q + 9) % 11 are unsigned 16-bit remainders.
    private static int ShopQuality(CardContext c, bool withCapitalBonus)
    {
        var quality = 3 * c.HereQuery(2) + (ushort)(c.HereQuery(0x21) + 4) % 0x29;
        if (withCapitalBonus && c.HereQuery(0x1B) != 0)
        {
            quality += (ushort)(c.HereQuery(0x21) + 9) % 0x0B + 0x0A;
        }
        return quality;
    }

    /// <summary>The stone's quality on offer here: 1..25.</summary>
    private static int StoneOnOffer(CardContext c) => (ushort)(c.HereQuery(0x21) + 4) % 0x19 + 1;

    /// <summary>The price of the stone ($Money1, groschen): (quality / 2 + 1) × (stone + 5) / 12, 1..50+.</summary>
    // C: signed q / 2 and / 12; the upper bound is 50 + (party seed + place) % 20 (unsigned).
    private static int StonePrice(CardContext c, int quality)
    {
        var price = (quality / 2 + 1) * (StoneOnOffer(c) + 5) / 12;
        var most = (ushort)(c.State.Party.Seed + c.Here) % 20 + 0x32;
        return Math.Clamp(price, 1, most);
    }

    /// <summary>His mood: Charisma, standing, speech, the party's alchemy and the city.</summary>
    // orig: R066+0x116C (through the hints 0570 R066+0x12B0, 060c R066+0x134C, 0662 R066+0x13A2,
    // 07d2 R066+0x1512, 0908 R066+0x1648, 0924 R066+0x1664; member: the leader as a signed byte)
    private static int Persuasion(CardContext c)
    {
        var charisma = c.Attribute(Who.Leader, (int)Attribute.Charisma);
        var standing = c.HereQuery(0);
        var city = c.HereQuery(0x21);
        var speech = c.Skill(Who.Leader, (int)Skill.SpeakCommon);
        var alchemy = c.PartyMaxSkill((int)Skill.Alchemy);
        // C: (unsigned) city % 31; the other divisions are signed.
        var value = charisma + (standing / 10 + (speech / 3 + alchemy / 2 + (ushort)city % 0x1F)) - 0x0F;
        return Math.Clamp(value, 0, 99);
    }

    private static void ShowUsTheDoor(CardContext c)
    {
        c.Screen.Text(6);
        c.EventHere(ThrownOut, 0x3C);
        c.Next = Streets;
    }

    /// <summary>A better stone ($Number1) for $Money1, if his is better than the party's.</summary>
    // orig: R066+0x11FE
    private void ImproveTheStone(CardContext c, CardMenu menu, int chance)
    {
        // C: cur_loc_query(0x21) is called once more and its value dropped.
        _ = c.HereQuery(0x21);
        c.Variables.Numbers[0] = StoneOnOffer(c);
        menu[0] = OptionState.Shown;
        if (c.Random(100) <= chance)
        {
            var stone = StoneOnOffer(c);
            if (c.PartyGet(6) < stone)
            {
                c.State.Party.PhilosophersStone = stone;
                c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
                c.Screen.Text(7);
            }
            else
            {
                c.Screen.Text(5);
            }
            c.Next = Number;
            return;
        }
        ShowUsTheDoor(c);
    }

    /// <summary>His shop: potions for a good shop, components otherwise (the capital bonus does not count here).</summary>
    // orig: R066+0x12CA
    private void BuyFromHim(CardContext c, int chance)
    {
        var quality = ShopQuality(c, withCapitalBonus: false);
        if (c.Random(100) <= chance)
        {
            c.OpenShop(-1, "Alchemist", quality > 24 ? 0x800 : 0x400, 0);
            ShowPicture(c, ShopPicture);
            return;
        }
        ShowUsTheDoor(c);
    }

    // orig: R066+0x1366
    private static void AskAboutKnowledge(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.SavedCard = Streets;
            c.Next = AlchemicalKnowledge;
            return;
        }
        ShowUsTheDoor(c);
    }

    /// <summary>
    /// An exchange of formulas: in four cities of five he wants a party better at alchemy than
    /// 30..79; then he teaches the leader a formula of a group the leader knows nothing of.
    /// </summary>
    // orig: R066+0x13BC
    private void OfferAnExchange(CardContext c, CardMenu menu, int chance)
    {
        c.EventHere(ExchangeAsked, 0x3C);
        menu[3] = OptionState.Shown;
        if (c.Random(100) > chance)
        {
            ShowUsTheDoor(c);
            return;
        }
        // C: signed remainders here (idiv).
        if (c.HereQuery(0x21) % 5 != 0)
        {
            var alchemy = c.PartyMaxSkill((int)Skill.Alchemy);
            if (c.HereQuery(0x21) % 0x32 + 0x1E >= alchemy)
            {
                c.Screen.Text(0x0C);
                c.Next = Number;
                return;
            }
        }
        TeachAFormula(c);
    }

    /// <summary>
    /// A formula for the leader: from a random start, the first formula of a group the leader
    /// knows none of (or the start again after a full round).
    /// </summary>
    // C: the test reads the whole byte of formulas_known (R136+0xE0EE, three formulas per byte), not the
    // formula's own bit; the (1 << s % 3) test beside it is always true. Checked against the asm.
    private void TeachAFormula(CardContext c)
    {
        var leader = c.State.Party.Slot(Who.Leader);
        var formula = c.Random(0x14A) % 0x42;
        var start = formula;
        while (true)
        {
            var group = formula % 0x42 / 3;
            if (!leader.Formulas.Contains(group * 3) && !leader.Formulas.Contains(group * 3 + 1) && !leader.Formulas.Contains(group * 3 + 2))
            {
                break;
            }
            if (++formula >= 0x42)
            {
                formula = 0;
            }
            if (start == formula)
            {
                break;
            }
        }
        leader.Formulas.Add(formula % 0x42);
        // C: $ChosenThreeName is the leader; the leader index is a signed byte.
        c.Variables.Chosen[2] = (sbyte)c.Leader;
        c.Variables.Texts[1] = FormulaName(c, formula % 0x42);
        c.Screen.Text(0x0B);
        c.Next = Number;
    }

    /// <summary>
    /// He offers to teach the party (a teacher of his quality for a week, his price $Money2 in
    /// pfennigs: quality / 4 + 12).
    /// </summary>
    // orig: R066+0x152C
    private void AskForATask(CardContext c, CardMenu menu, int chance)
    {
        if (c.Random(100) > chance)
        {
            c.EventHere(ThrownOut, 0x3C);
            c.Screen.Text(6);
            c.Next = Streets;
            return;
        }
        var quality = ShopQuality(c, withCapitalBonus: true);
        // C: quality / 4 rounds toward zero (the asm takes the absolute value, shifts and restores the sign).
        var price = quality / 4 + 0x0C;
        c.Events.Create(-2, c.Here, quality, 0x28, 7, price, 0, 0xA8, 0, 0);
        c.Variables.Money[1] = Money.From(0, 0, price);
        ShowPicture(c, WorkshopPicture);
        c.Screen.Text(8);
        ShowTheShop(c);
        menu[4] = OptionState.Shown;
        c.Next = Number;
    }
}
