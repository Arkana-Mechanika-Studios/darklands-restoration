using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf04": a doorway halfway up a cliff, a dwarf's audience chamber. When an item quest
/// (tag 10) leads here the dwarf holds the item and names a price — the party's gold florins,
/// or its most valuable item — and the party can pay, attack him (card 253), or leave. Those
/// who attacked before find him angry: his price is a member's beauty. Without the quest the
/// cave is deserted.
/// </summary>
// orig: R107+0x0000 (1838:0000) matched; tests: CardSmokeTests
// orig: R107+0x0BC4 (1838:0BC4) matched (registration: item pop-ups R107+0x0C86.. say "unknown"; saint pop-ups 0d66/0dee do not name the member; saint_fn[2..3] are left from an earlier card)
public sealed class DwarfInTheCliff : Card
{
    private const int CliffPicture = 331;
    private const int ChamberPicture = 248;
    private const int FallPicture = 65;

    // The dwarf's price, kept in the game's saved-card word (its value is also the text block of the demand).
    private const int PriceRope = 7;
    private const int PriceGold = 8;
    private const int PricePotions = 9;
    private const int PriceBestItem = 10;
    private const int PriceBeauty = 15;

    public override int Number => 252;

    public override int Run(CardContext c)
    {
        Show(c, CliffPicture);
        c.Next = Number;
        if (!c.Events.QuestAt(10, c.Here))
        {
            // The cave is deserted; the original returns 0x0C without changing next_card.
            c.Screen.Text(21);
            return CardContext.MapCard;
        }
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 9);
        c.NameToSlot(0, c.EventDetail(c.Events.Find(8, -1, c.Here, -1, 10, -1)), 1);
        c.PartyMaxEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        if (c.CountItems(Who.Everybody, 0x3B) == 0)
        {
            menu[1] = OptionState.Shown;
        }
        SetThePrice(c);
        if (c.EventAtHere(0x53))
        {
            c.PartyMaxAttribute((int)Attribute.Charisma);
            c.Variables.Chosen[0] = c.BestMember;
            menu.Set(OptionState.Hidden, 0, 1, 2, 3, 7, 8);
            menu.Set(OptionState.Enabled, 4, 5, 6, 9);
            menu.Block = 15;
            c.State.SavedCard = PriceBeauty;
        }
        c.SetCardItems(0x5F, 0x60, 0x65, 0x66);
        c.SetCardSaints(0x11, 0x44);
        if (menu[3] != OptionState.Hidden)
        {
            menu[3] = c.SaintsUsable();
        }
        menu.Popup = () => c.SaintPopupChanceHint();

        return menu.Run(
            // orig: R107+0x0410 (1838:0410) matched; hint R107+0x042E
            new MenuLine(_ => { c.Screen.Text(3); c.PassHours(3); c.Next = 252; }, () => OptionHint.Say(NoRisk)),
            // orig: R107+0x05D6 (1838:05D6) matched (hint)
            new MenuLine(p => ClimbWithTheRope(c, menu, p),
                () => OptionHint.ChanceOf(Math.Clamp(c.EffectiveAgility(c.Variables.Chosen[0]) * 2, 1, 99), c.Variables.Chosen[0])),
            // orig: R107+0x0768 (1838:0768) matched (hint)
            new MenuLine(p => AllClimb(c, menu, p), () => WorstClimberHint(c)),
            // orig: R107+0x0886 (1838:0886) matched (hint)
            new MenuLine(_ => PrayForHelp(c, menu), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R107+0x09FA (1838:09FA) matched (hint)
            new MenuLine(_ => PayThePrice(c), Unknown),
            // orig: R107+0x0A32 (1838:0A32) matched; hint R107+0x0A46
            new MenuLine(_ => { c.EventHere(0x53, 720); c.Next = 253; }, Unknown),
            // orig: R107+0x0A7E (1838:0A7E) matched; hint R107+0x0AA8
            new MenuLine(_ => { c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]); c.EventHere(0x53, 720); c.Next = 253; }, Unknown, () => OptionHint.Say(UnknownText)),
            // orig: R107+0x0AE0 (1838:0AE0) matched; hint R107+0x0AEC
            // DORMANT: lines 7 and 8 are never offered; each lets no time pass.
            new MenuLine(_ => c.PassHours(0), () => OptionHint.Say(SuccessAssured)),
            // orig: R107+0x0B24 (1838:0B24) matched; hint R107+0x0B30
            new MenuLine(_ => c.PassHours(0), () => OptionHint.Say(SuccessAssured)),
            // orig: R107+0x0B68 (1838:0B68) matched; hint R107+0x0B8C (leaves without clearing cur_loc)
            new MenuLine(_ => RefuseAndLeave(c, menu), Unknown));
    }

    private const string UnknownText = "You can always encounter the unknown";

    private static OptionHint Unknown() => OptionHint.Say(UnknownText);

    /// <summary>
    /// The dwarf's price by the place's number (modulo 3). The original's switch has no breaks:
    /// every case falls through to the gold, and from it, when the party has three florins or
    /// fewer, to the most valuable item.
    /// </summary>
    // C: switch (cur_loc % 3) without break: cases 0 and 1 set the rope (7) and potions (9) prices
    // and name item 0, but fall through and are overwritten.
    // DORMANT: so the prices 7 (rope) and 9 (an alchemist's potions) are never asked.
    private static void SetThePrice(CardContext c)
    {
        var fallThrough = c.Here % 3;
        if (fallThrough == 0)
        {
            c.State.SavedCard = PriceRope;
            c.State.SavedCard2 = 0;
            c.NameToSlot(0, 0, 0);
        }
        if (fallThrough is 0 or 1)
        {
            c.State.SavedCard2 = 0;
            c.NameToSlot(0, 0, 0);
            c.State.SavedCard = PricePotions;
        }
        if (fallThrough is 0 or 1 or 2)
        {
            c.State.SavedCard = PriceGold;
            c.Variables.Money[0] = Money.From(c.State.Party.Purse.Florins, 0, 0);
            c.Variables.Named[0] = "gold coins";
            if (c.State.Party.Purse.Florins > 3)
            {
                return;
            }
        }
        // C: the value is quality × rarity of the item kind, two signed bytes of the item
        // definition (+0x26, +0x27); the first kind with the highest value wins.
        var best = 0;
        var bestValue = 0;
        for (var kind = 0; kind < 0x43; kind++)
        {
            if (c.CountItems(Who.Everybody, kind) != 0)
            {
                var d = c.World.Items.Items[kind];
                var value = (sbyte)d.Quality * (sbyte)d.Rarity;
                if (value > bestValue)
                {
                    bestValue = value;
                    best = kind;
                }
            }
        }
        for (var i = 0; i < Party.MaxMembers; i++)
        {
            if (c.Status(i) != CharacterStatus.Empty && c.CountItems(i, best) != 0)
            {
                c.Variables.Chosen[0] = i;
            }
        }
        c.State.SavedCard2 = best;
        c.NameToSlot(0, best, 0);
        c.State.SavedCard = PriceBestItem;
    }

    /// <summary>Up at the chamber, the dwarf names his price (the text block of the price).</summary>
    private static void FaceTheDwarf(CardContext c, CardMenu menu)
    {
        menu.Set(OptionState.Hidden, 0, 1, 2, 3, 7, 8);
        menu.Set(OptionState.Enabled, 4, 5, 9);
        menu[6] = c.ItemsUsable();
        menu.Block = c.State.SavedCard;
    }

    /// <summary>The weakest climber falls: Endurance halved and Strength down to 1..4, a night lost.</summary>
    private static void AfterTheFall(CardContext c, CardMenu menu)
    {
        c.Screen.Text(17);
        c.PassHours(c.HoursUntil(5));
        c.SetCardSaints(0x59, 0x61);
        menu[3] = c.SaintsUsable();
    }

    // orig: R107+0x0466 (1838:0466) matched; tests: CardSmokeTests
    private static void ClimbWithTheRope(CardContext c, CardMenu menu, int chance)
    {
        var climber = c.Variables.Chosen[0];
        if (c.Random(100) <= chance)
        {
            c.PassHours(2);
            ShowPicture(c, ChamberPicture);
            c.Screen.Text(5);
            ShowPicture(c, CliffPicture);
            FaceTheDwarf(c, menu);
        }
        else
        {
            ShowPicture(c, FallPicture);
            c.Screen.Text(4);
            ShowPicture(c, CliffPicture);
            c.AddAttribute(climber, (int)Attribute.Endurance, -(c.Attribute(climber, (int)Attribute.Endurance) / 2));
            c.AddAttribute(climber, (int)Attribute.Strength, c.Random(4) - c.Attribute(climber, (int)Attribute.Strength) + 1);
            c.PassHours(1);
            AfterTheFall(c, menu);
        }
        c.Next = 252;
    }

    // C: a side effect of the hint: the weakest climber becomes $ChosenOneName.
    private static OptionHint WorstClimberHint(CardContext c)
    {
        c.PartyMinEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
        return OptionHint.ChanceOf(Math.Clamp(c.EffectiveAgility(c.BestMember) * 2, 1, 99), OptionHint.WholeParty);
    }

    /// <summary>All climb at once; every member whose Agility (doubled) is under the roll falls.</summary>
    // orig: R107+0x0604 (1838:0604) matched; tests: CardSmokeTests
    private static void AllClimb(CardContext c, CardMenu menu, int chance)
    {
        // C: the roll is drawn before the hour passes and is kept for the falls below.
        var roll = c.Random(100);
        c.PassHours(1);
        if (chance >= roll)
        {
            c.Screen.Text(16);
            FaceTheDwarf(c, menu);
        }
        else
        {
            for (var i = 0; i < Party.MaxMembers; i++)
            {
                if (c.Status(i) != CharacterStatus.Empty && c.EffectiveAgility(i) * 2 < roll)
                {
                    c.Variables.Chosen[0] = i;
                    ShowPicture(c, FallPicture);
                    c.Screen.Text(4);
                    ShowPicture(c, CliffPicture);
                    c.AddAttribute(i, (int)Attribute.Endurance, -(c.Attribute(i, (int)Attribute.Endurance) / 2));
                    c.AddAttribute(i, (int)Attribute.Strength, c.Random(6) - c.Attribute(i, (int)Attribute.Strength) + 1);
                }
            }
            AfterTheFall(c, menu);
        }
        c.Next = 252;
    }

    /// <summary>St. Reinold or St. Zita lift the party up; other saints only tell of the dwarf; unanswered, new saints are offered.</summary>
    // orig: R107+0x0798 (1838:0798) matched; tests: CardSmokeTests
    private static void PrayForHelp(CardContext c, CardMenu menu)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var price = c.State.SavedCard;
        var saint = c.CardSaints[c.MenuSlot];
        var answer = c.InvokeSaint(c.Variables.Chosen[0], saint);
        // C: the price is saved around the prayer and put back (the prayer may change saved_card).
        c.State.SavedCard = price;
        if (answer == -1)
        {
            c.PartyMaxEffectiveAgility();
            c.Variables.Chosen[0] = c.BestMember;
            return;
        }
        if (answer != 0)
        {
            if (saint is 0x59 or 0x61)
            {
                c.Screen.Text(18);
                FaceTheDwarf(c, menu);
            }
            else
            {
                c.Screen.Text(1);
            }
        }
        else
        {
            c.Screen.Text(2);
            c.SetCardSaints(0x59, 0x61);
            menu.Block = 20;
            menu[3] = c.SaintsUsable();
        }
        c.Next = 252;
        c.PartyMaxEffectiveAgility();
        c.Variables.Chosen[0] = c.BestMember;
    }

    /// <summary>The price is paid and the item taken; then the party climbs down.</summary>
    // orig: R107+0x08BE (1838:08BE) matched; tests: CardSmokeTests
    private static void PayThePrice(CardContext c)
    {
        switch (c.State.SavedCard)
        {
            case PriceRope:
                // DORMANT: never the price (see SetThePrice).
                c.RemoveItemStack(Who.Everybody, c.State.SavedCard2);
                c.FinishQuest10(c.Here);
                c.Screen.Text(13);
                break;
            case PriceGold:
                c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
                c.FinishQuest10(c.Here);
                c.Screen.Text(12);
                break;
            case PricePotions:
                // DORMANT: never the price (see SetThePrice).
                c.PartyMaxSkill((int)Skill.Alchemy);
                c.RemoveItemStack(c.BestMember, c.State.SavedCard2);
                c.FinishQuest10(c.Here);
                c.Screen.Text(12);
                break;
            case PriceBestItem:
                c.TakeItem(Who.Everybody, c.State.SavedCard2);
                c.FinishQuest10(c.Here);
                c.Screen.Text(13);
                break;
            case PriceBeauty:
                c.FinishQuest10(c.Here);
                c.Screen.Text(19);
                c.PartyMaxAttribute((int)Attribute.Charisma);
                var victim = c.BestMember;
                c.Variables.Chosen[0] = victim;
                c.SetAttributeMaximum(victim, (int)Attribute.Charisma, c.Random(4) + 4);
                c.SetAttribute(victim, (int)Attribute.Charisma, c.AttributeMaximum(victim, (int)Attribute.Charisma));
                break;
        }
        ShowPicture(c, ChamberPicture);
        c.Screen.Text(6);
        c.PassHours(1);
        c.Next = CardContext.MapCard;
    }

    /// <summary>Refusing the dwarf costs an hour and a mocking rhyme; leaving from below costs nothing.</summary>
    // orig: R107+0x0B68 (1838:0B68) matched; tests: CardSmokeTests
    private static void RefuseAndLeave(CardContext c, CardMenu menu)
    {
        if (menu.Block != 0)
        {
            c.PassHours(1);
            c.Screen.Text(14);
        }
        c.Next = CardContext.MapCard;
    }
}
