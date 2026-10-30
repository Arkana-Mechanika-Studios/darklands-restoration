using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Items;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Revol11": the rebels of a city are in the dungeons, and its lord rewards the party with
/// the key to the city, Rhinegeld, and a boon of their choice: a formula, a saint, a horse,
/// plate armour, or letters of recommendation to the nearest city.
/// </summary>
// orig: R089+0x096A (18CE:000A) matched; tests: CardSmokeTests
public sealed class CityLordsReward : Card
{
    private const int Picture = 0;
    private const int Horse = 0x43;
    private const int Breastplate = 0x2C;
    private const int LimbPlate = 0x35;

    public override int Number => 0xB6;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        c.RandomName(c.City.Ordinal + 0x16, 2, 0);
        c.Variables.Money[0] = Money.From(c.HereQuery(2) + c.SideStrength(-1) / 4 + 5, 0, 0);
        // C: $Money1 is the reward named in text 1; the original never adds it to the purse.
        c.ConflictOutcome(1, 1, 1);
        c.Screen.Text(0, TextFrame.BlankPage);
        c.Screen.Text(1, TextFrame.BlankPage);
        // orig: R089+0x1088 (18CE:0728) matched (reset, and the pop-up hints 0802..08b6, 08f2..0a90)
        c.SetCardItems(FormulaOfTheCity(c));
        // orig: R089+0x0C5C (18CE:02FC) matched
        // BUG: the original looks for a saint nobody in the party knows, starting from the city's own,
        // but has no return statement: AX holds the last member-flag-bit answer, 0 (checked against asm). So
        // saint 0.
        // DIVERGENCE: if the party knows all 0x88 saints the original's search never ends (the game hangs); saint 0 here too.
        c.SetCardSaints(0);

        // C: DS:5204 = 1 for the menu loop (back to 0 after): the pop-ups list the formula and the saint for every member.
        c.PopupListsAll = true;
        var menu = Menu(c, 2);
        // C: the pop-up handlers (R089+0x1162.., R089+0x1252..) name the member picked in the box: the formula box says "Success is
        // C: assured." beside the member, the saint box tells the prayer's chance for the party.
        menu.ItemBox = () => c.MemberPopupHint();
        menu.SaintBox = c.SaintPopupHint;
        menu.Enable(0, 1, 2, 3, 4);
        var next = menu.Run(
            // orig: R089+0x0C9C (18CE:033C); hint R089+0x0CE6 matched
            new MenuLine(() => LearnTheFormula(c), OptionHint.Saying(CardTexts.SelectPotion)),
            // orig: R089+0x0D1E (18CE:03BE); hint R089+0x0D88 matched
            new MenuLine(() => LearnAboutTheSaint(c), OptionHint.Saying(CardTexts.SelectSaint)),
            // orig: R089+0x0DC0 (18CE:0460); hint R089+0x0DFC matched
            new MenuLine(() => TakeTheHorse(c), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R089+0x0E36 (18CE:04D6); hint R089+0x0F28 matched
            new MenuLine(() => TakeTheArmour(c), () => OptionHint.Say(SuccessAssured, c.Leader)),
            // orig: R089+0x0F62 (18CE:0602); hint R089+0x0FBA matched
            new MenuLine(() => TakeTheLetters(c), OptionHint.Saying(SuccessAssured)),
            // orig: R089+0x0FF2; R089+0x1000; R089+0x1022; R089+0x1044; R089+0x1066 matched
            // DORMANT: lines 5..9 are hidden (flags 0); they would let no time pass and stay (line 5's
            // hint is the empty function 069e: whatever is in its registers; UNCERTAIN, shown as "screwed up."),
            // lines 6..9 with hints of a 100 % chance.
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(CardTexts.ScrewedUp)),
            new MenuLine(() => c.PassHours(0), () => OptionHint.ChanceOf(100, Who.Everybody)),
            new MenuLine(() => c.PassHours(0), () => OptionHint.ChanceOf(100, Who.Everybody)),
            new MenuLine(() => c.PassHours(0), () => OptionHint.ChanceOf(100, Who.Everybody)),
            new MenuLine(() => c.PassHours(0), () => OptionHint.ChanceOf(100, Who.Everybody)));
        c.PopupListsAll = false;
        return next;
    }

    /// <summary>The potion whose formula the city's alchemist offers, chosen by the city.</summary>
    // orig: R089+0x0C08 (18CE:02A8) matched: card-item-lines[0] = it
    // C: location-now-query(0x21) % 6 is a signed remainder: a negative value falls to the default (0x74), as here.
    private static int FormulaOfTheCity(CardContext c)
    {
        var potion = (c.HereQuery(0x21) % 6) switch
        {
            0 => 0x66,
            1 => 0x67,
            2 => 0x64,
            3 => 0x6D,
            4 => 0x73,
            _ => 0x74,
        };
        c.NameToSlot(2, potion, 1);
        return potion;
    }

    private static int ToTheSquare(CardContext c) => c.IsDaylight ? 0x12 : 0x19;

    /// <summary>
    /// Original bug: the member is given the formulas of the potion group numbered by the
    /// <em>saint</em> list's slot (group 0: three formulas), not the potion offered.
    /// </summary>
    // BUG: see the summary; the formula table's byte is set to 7 (bits 0-2: the group's three formulas).
    private static void LearnTheFormula(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        LearnFormulaGroup(c, c.MenuMember, c.CardSaints[c.MenuEntrySlot]);
        c.Screen.Text(3);
        c.PassHours(1);
        c.Next = ToTheSquare(c);
    }

    /// <summary>
    /// All three formulas of a potion group (a byte of the members' formula tables, which follow
    /// each other: group −1 is the previous member's last group).
    /// </summary>
    // C: known-recipes[member][slot] = 7 writes byte member × 22 + slot of the five members' tables;
    // slot −1 reaches the previous member's last byte. DIVERGENCE: for member 0 and slot −1 the original
    // writes the byte before the table; here nothing is written.
    private static void LearnFormulaGroup(CardContext c, int member, int group)
    {
        var index = member * Potions.Count + group;
        if (index < 0 || index >= Party.MaxMembers * Potions.Count)
        {
            return;
        }
        var learner = c.State.Party.Slot(index / Potions.Count);
        var first = index % Potions.Count * 3;
        learner.Formulas.UnionWith([first, first + 1, first + 2]);
    }

    private static void LearnAboutTheSaint(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var saint = c.CardSaints[c.MenuEntrySlot];
        // C: for saint −1 the bit is 0x80 >> −1 % 8 = 0x80 >> −1, a shift count masked to 31: no bit is set.
        if (saint >= 0)
        {
            c.LearnSaint(c.MenuMember, saint);
        }
        c.Screen.Text(4);
        c.PassHours(2);
        c.Next = ToTheSquare(c);
    }

    private static void TakeTheHorse(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.AddItem(c.Leader, Horse);
        c.PassHours(1);
        c.Screen.Text(5);
        c.Next = c.IsDaylight ? 6 : 8;
    }

    /// <summary>A breastplate and limb plates of quality 34–38 plus the city's size.</summary>
    private static void TakeTheArmour(CardContext c)
    {
        c.Variables.Chosen[0] = c.Leader;
        c.AddItemWithQuality(c.Leader, Breastplate, c.Random(5) + c.HereQuery(2) + 0x22);
        c.AddItemWithQuality(c.Leader, LimbPlate, c.Random(5) + c.HereQuery(2) + 0x22);
        c.PassHours(1);
        c.Screen.Text(6);
        c.Next = ToTheSquare(c);
    }

    /// <summary>The letters raise the party's standing in the city nearest to this one.</summary>
    // C: the nearest city to the location's map position (+4, x and y as one long).
    private static void TakeTheLetters(CardContext c)
    {
        var here = c.State.Here;
        c.AdjustReputation(c.NearestCity(here?.X ?? 0, here?.Y ?? 0), 10, 0x21);
        c.PassHours(1);
        c.Screen.Text(7);
        c.Next = ToTheSquare(c);
    }
}
