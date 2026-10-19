using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Merch03": going from stall to stall among the city's merchants. The party may trade
/// (a merchant may reward a finished errand, or hire them for a new one), sound out their
/// politics, ask to meet their leader (0xC0), or go on to the market, the street or the
/// main street.
/// </summary>
// orig: R092+0x057A (188F:000A) matched; tests: CardSmokeTests
public sealed class MerchantsOfTheCity : Card
{
    private const int Picture = 0;

    /// <summary>"A merchant has an errand for you" (kind 7, type 0, 72 hours): no new errand meanwhile.</summary>
    private const int ErrandOffered = 7;

    public override int Number => 0xC1;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = Number;
        // orig: R092+0x0E10 (188F:08A0) matched (reset of the option handlers and pop-up lists)
        c.SetCardSaints();
        c.SetCardItems();
        var menu = Menu(c);
        menu.Enable(0, 1, 3, 7, 8, 9);
        return menu.Run(
            new MenuLine(chance => Trade(c, chance), () => TradeHint(c)),
            new MenuLine(() => SoundOutTheirPolitics(c), OptionHint.Saying(NoRisk)),
            // orig: R092+0x0BB8 (188F:0648) matched; hint R092+0x0BBA (r.a = 0, r.b = -2)
            // DORMANT: line 2 is hidden (flag 0) and does nothing; its hint is a 0 % chance.
            new MenuLine(() => { }, () => OptionHint.ChanceOf(0, Who.Everybody)),
            new MenuLine(chance => AskForTheirLeader(c, chance), () => OptionHint.ChanceOf(MeetingChance(c), Who.Everybody)),
            // orig: R092+0x0C8E (188F:071E) matched; hint R092+0x0C90 (r.a = 0, r.b = -2)
            // DORMANT: line 4 is hidden and does nothing.
            new MenuLine(() => { }, () => OptionHint.ChanceOf(0, Who.Everybody)),
            // orig: R092+0x0CA6 (188F:0736); R092+0x0CDA (188F:076A) matched
            // DORMANT: lines 5 and 6 are hidden; they would end the game (0x62).
            new MenuLine(() => TheEnd(c), () => OptionHint.ChanceOf(100, Who.Everybody)),
            new MenuLine(() => TheEnd(c), () => OptionHint.ChanceOf(100, Who.Everybody)),
            // orig: R092+0x0D0E; R092+0x0D64; R092+0x0DBA matched
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 0x15 : 0x16; }),
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 9 : 10; }),
            new MenuLine(() => { c.PassHours(1); c.Next = c.IsDaylight ? 6 : 8; }));
    }

    /// <summary>In a hostile town the merchants' price sets the risk of being thrown out.</summary>
    // orig: R092+0x0AE2 (188F:0572) matched
    // C: the hint itself draws a random number (LocalPrice) when the town is hostile; the game calls
    // it once when the line is chosen (and on each Shift hover), before the action draws its own.
    private static OptionHint TradeHint(CardContext c)
    {
        var price = c.Hostile() ? c.LocalPrice(0x1E, 3) : 0;
        // C: ((p << 1) + p << 1) / -10 + 100: 6p divided by −10, truncated toward zero.
        return price == 0 ? new OptionHint(-1, SuccessAssured, Who.Everybody) : OptionHint.ChanceOf(6 * price / -10 + 100, Who.Everybody);
    }

    // orig: R092+0x0792 (188F:0222) matched
    private static void Trade(CardContext c, int chance)
    {
        // C: LocalPrice(0x1E, 3, 4): the original passes a third argument the rule ignores.
        var price = c.Hostile() ? c.LocalPrice(0x1E, 3) : 0;
        // UNCERTAIN: $NamedOneName is set to a string of the far table seg 290E (filled at run time,
        // probably the merchant's title); that table is not read yet.
        c.Variables.Named[0] = "";
        c.Variables.Chosen[0] = c.Leader;
        // C: the roll is drawn first, whatever the price.
        if (c.Random(100) >= chance && price != 0)
        {
            c.PassHours(0);
            c.Next = 1;
            return;
        }
        var cityBonus = Money.From(c.LocationQuery(c.Here, 2) * 5, 0, 0);
        if (c.Events.ResolvedQuestAt(3, 0, c.Here))
        {
            // A finished errand for a merchant of this town is paid.
            c.State.Party.Purse = c.State.Party.Purse.Add(c.LocationQuery(c.Here, 2) * 5, 0, 0);
            c.Variables.Money[0] = cityBonus;
            c.RandomName(c.City.Ordinal + 1, 0, 0);
            c.Screen.Text(1);
            c.Variables.Location = NearestCastle(c);
            c.State.SavedCard = c.IsDaylight ? 0x15 : 0x16;
            c.State.SavedCard2 = c.Variables.Location;
            c.Next = 0x91;
            c.Events.Create(Who.Everybody, c.Here, 0x5F, ErrandOffered, 0, 0, 0, 0x48, 0, 0);
        }
        else if (c.Events.ResolvedQuestAt(0x0A, 0, c.Here))
        {
            c.Screen.Text(1);
            var standing = c.LocationQuery(c.Here, 0);
            c.LocationSet(c.Here, 0, standing + 10);
            c.GiveReward(1);
            c.Events.Create(Who.Everybody, c.Here, 0x5F, ErrandOffered, 0, 0, 0, 0x48, 0, 0);
        }
        else
        {
            // C: R029+0x21AA(1, (char far *)0, 0xc100, 2): a null name is the shop slot's own, R136+0xA70F[1] "Goods Merchant"
            // (R092 seg 188F:239; card oracle: CardOracleTests).
            c.Barter(1, 0x2C100);
        }
        // C: drawn even when a branch above already chose the next card.
        var roll = c.Random(100);
        if (c.Events.QuestWithValue(0x0A, 0, c.Here) || c.Events.QuestWithValue(3, 0, c.Here) || c.EventTypeAtHere(ErrandOffered, 0))
        {
            return;
        }
        if (roll < 0x65)
        {
            // A merchant hires the party to carry goods to the nearest castle.
            c.Variables.Money[0] = cityBonus;
            c.RandomName(c.City.Ordinal + 1, 0, 0);
            c.Screen.Text(2, TextFrame.BlankPage);
            c.Variables.Location = NearestCastle(c);
            c.StartQuest3(0, c.Variables.Location, c.City.Ordinal + 1, 0x0F, 2, 0);
            c.PassHours(1);
            c.State.SavedCard = c.IsDaylight ? 0x15 : 0x16;
            c.State.SavedCard2 = c.Variables.Location;
            c.Next = 0x90;
        }
        else if (roll < 0x5A)
        {
            // DORMANT: the roll is always below 0x65, so neither this errand (an item to fetch
            // from a random place, quest tag 0x0A) nor the last branch is ever reached.
            c.Variables.Location = c.Random(0x141) + 0x5C;
            c.Variables.Item = c.Random(0x20) + 0x8B;
            c.Events.Create(0, c.Variables.Location, 0x5F, 8, c.Variables.Item, c.Here, 0, 9999, 0, 0x0A);
            c.Screen.Text(2, TextFrame.BlankPage);
            c.PassHours(1);
            c.Next = c.IsDaylight ? 0x15 : 0x16;
        }
        else
        {
            // DORMANT: see above.
            c.Events.Create(Who.Everybody, c.Here, 0x5F, ErrandOffered, 0, 0, 0, 0x48, 0, 0);
        }
    }

    /// <summary>The castle (map icon 2) nearest to this town.</summary>
    // DIVERGENCE: the original reads the location record of cur_loc even in the wilderness
    // (index −1, out of the table); here the party's map position is used then.
    private static int NearestCastle(CardContext c)
    {
        var here = c.State.Here;
        return c.NearestLocationOfType(here?.X ?? c.State.MapPosition.X, here?.Y ?? c.State.MapPosition.Y, 2);
    }

    // orig: R092+0x0B56 (188F:05E6) matched
    private static void SoundOutTheirPolitics(CardContext c)
    {
        c.Support[0] = c.SideOf(1);
        c.Screen.Text(3, TextFrame.BlankPage);
        c.PassHours(1);
        c.Next = 0xC1;
    }

    // orig: R092+0x0C2A (188F:06BA) matched
    private static int MeetingChance(CardContext c) =>
        Math.Clamp(c.ReputationHere / 2 + (c.PartyGet(4) / 0x19 + c.PartyAverageAttribute((int)Attribute.Charisma) / 2)
            + c.State.Party.Purse.Florins, 1, 100);

    // orig: R092+0x0BD0 (188F:0660) matched
    private static void AskForTheirLeader(CardContext c, int chance)
    {
        if (c.Random(100) <= chance)
        {
            c.AdjustReputation(c.Here, 2, 2);
            c.Screen.Text(1);
            c.State.SavedCard = 0xC1;
            c.Next = 0xC0;
            return;
        }
        c.AdjustReputation(c.Here, -2, -2);
        c.Screen.Text(2);
        c.Next = 0xC1;
    }

    private static void TheEnd(CardContext c)
    {
        c.Screen.Text(0);
        c.PassHours(0);
        c.Next = 0x62;
    }
}
