using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// What the offices of the Fuggers, the Medici and the Hanseatic League have in common:
/// letters of credit, commissions and their rewards. Each house is its own card; the owner
/// code (Fuggers 8, Medici 6, Hanse 7) marks its quests and its "busy" events.
/// </summary>
// orig: shared by the three bank cards, whose code is the same apart from texts, pictures and the owner code:
// R060+0x1FEC (Fuggers, owner 8), R061+0x0978 (Medici, owner 6),
// R061+0x2278 (Hanseatic League, owner 7); each member below cites the functions it reconstructs.
// All three: matched; tests: CardSmokeTests
internal static class BankingHouses
{
    /// <summary>The commission card that carries a sealed letter to a raubritter castle.</summary>
    public const int DeliveryCard = 0x90;

    /// <summary>The card of a reward that is a further errand to a castle.</summary>
    public const int ErrandRewardCard = 0x91;

    /// <summary>The card of a commission to a far site (quest 10).</summary>
    public const int FarCommissionCard = 0x151;

    /// <summary>The card of the reward for a quest-10 commission.</summary>
    public const int FarCommissionRewardCard = 0x152;

    /// <summary>Quest tags of the houses' commissions.</summary>
    public const int DeliveryQuest = 3;

    public const int FarQuest = 0xA;

    /// <summary>"Come back later": the house will not see the party for three days.</summary>
    public const int BusyKind = 7;

    /// <summary>
    /// Redeems the letters of credit: the florins come back, less a fee of six pfennigs per
    /// florin ($Money2).
    /// </summary>
    // orig: R060+0x2378 (1A36:0398) and R061+0x0CE8 (18CF:0378), the money part:
    // purse-add-parts(party-purse, DS:9072, 0, 0); purse-assign(text-slot-amount-two, 0, 0, (DS:9072 << 1) + DS:9072 << 1); DS:9072 = 0;
    // purse-difference(party-purse, text-slot-amount-two). The Hanseatic League has no such option.
    // C: the fee expression parses as (notes * 2 + notes) << 1 = 6 * notes in a 16-bit int: it wraps past 5461 florins
    // of notes (pfennigs is an unsigned parameter of purse-assign); DS:9072 = 0 clears only the florins word.
    public static void RedeemLetters(CardContext c)
    {
        var party = c.State.Party;
        var notes = party.BankNotes.Florins;
        party.Purse = party.Purse.Add(notes, 0, 0);
        c.Variables.Money[1] = Money.From(0, 0, unchecked((ushort)((((short)(notes << 1) + notes) << 1))));
        party.BankNotes = party.BankNotes with { Florins = 0 };
        party.Purse = party.Purse.Subtract(c.Variables.Money[1]);
    }

    /// <summary>
    /// Asks how many florins to deposit (up to 500 and the florins in the purse) and writes a
    /// letter of credit for them; returns whether anything was deposited.
    /// </summary>
    // orig: R060+0x2444 (1A36:0464) and R061+0x0DA6 (18CF:0436), after their pop-up pictures:
    // itoa(party-purse[0]) preset in the prompt "Deposit how many Florins?" (thunk 09C0:20B7), v = limit-to-range(0, 500, atoi(answer)),
    // v = min(v, party-purse[0]); if v != 0: card-option-flag-0 = 1, purse-subtract-parts(party-purse, v, 0, 0), DS:9072 += v, purse-assign(text-slot-amount-one, DS:9072, 0, 0).
    // C: DS:9072 is a 16-bit int; the sum of deposits is not limited and wraps past 32767 florins (negative notes then hide the withdrawal option).
    public static bool DepositFlorins(CardContext c)
    {
        var party = c.State.Party;
        var answer = c.AskText("Deposit how many Florins?", party.Purse.Florins.ToString(System.Globalization.CultureInfo.InvariantCulture), 10);
        var florins = Math.Clamp(CardContext.ParseNumber(answer), 0, 500);
        if (party.Purse.Florins < florins)
        {
            florins = party.Purse.Florins;
        }
        if (florins == 0)
        {
            return false;
        }
        party.Purse = party.Purse.Subtract(florins, 0, 0);
        party.BankNotes = party.BankNotes with { Florins = unchecked((short)(party.BankNotes.Florins + florins)) };
        c.Variables.Money[0] = Money.From(party.BankNotes.Florins, 0, 0);
        return true;
    }

    /// <summary>
    /// A commission the house gives: with <paramref name="deliveryPercent"/>% a letter to the
    /// nearest raubritter castle for twice the city size in florins, otherwise an errand to a
    /// far site for the city size in florins.
    /// </summary>
    // orig: R060+0x254E (1A36:056E) (50%, picture 225, text 1 on the picture), R061+0x0EAE
    // (18CF:053E) (50%, picture 195, text 1 on the picture), R061+0x260A (1A5F:039A) and R061+0x2812
    // (30%, picture 194, text 6): the part after the first roll succeeded.
    // C: text-slot-place = closest-place-of-kind(...) is stored after the text is shown (a $location variable in it is the previous one);
    // the quest event is thunk 09C0:2175(owner, castle, city ordinal + owner, 0xC, 2, 0), the far errand thunk 09C0:21B1(owner) (StartQuest10).
    // The hour is passed after the quest event in the castle branch and before the money in the far branch, as here.
    public static void GiveACommission(CardContext c, int owner, int houseCard, int deliveryPercent, int picture, int text, TextFrame frame)
    {
        if (c.Random(100) < deliveryPercent)
        {
            c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2) << 1, 0, 0);
            c.Screen.ShowPicture(PictureNames.Of(picture));
            c.Screen.Text(text, frame);
            var castle = NearestCastle(c);
            c.Variables.Location = castle;
            c.StartQuest3(owner, castle, c.City.Ordinal + owner, 0xC, 2, 0);
            c.PassHours(1);
            c.SavedCard = houseCard;
            c.State.SavedCard2 = castle;
            c.Next = DeliveryCard;
            return;
        }
        c.PassHours(1);
        c.Variables.Money[0] = Money.From(c.LocationQuery(c.Here, 2), 0, 0);
        c.Screen.ShowPicture(PictureNames.Of(picture));
        c.Screen.Text(text, frame);
        c.SavedCard = c.StartQuest10(owner);
        c.State.SavedCard2 = houseCard;
        c.Next = FarCommissionCard;
    }

    /// <summary>The raubritter castle nearest to the current place (map icon 2).</summary>
    // orig: closest-place-of-kind(locations[location-now]->x/y, 2), repeated inline in R060+0x2750, R061+0x1056, R061+0x2A00/0a7c
    // (R060+0x2750, R061+0x1056, R061+0x2A00, R061+0x2CEC); the delivery castle for the commission and for the reward errand.
    public static int NearestCastle(CardContext c)
    {
        var here = c.State.Locations[c.Here];
        return c.NearestLocationOfType(here.X, here.Y, 2);
    }

    /// <summary>The house is busy for the party for 72 hours.</summary>
    // orig: event-create(-2, location-now, 0x5f, 7, owner, 0, 0, 0x48, 0, 0) in each bank's refusal and reward branches (72 hours).
    public static void BusyForThreeDays(CardContext c, int owner) =>
        c.Events.Create(-2, c.Here, 0x5F, BusyKind, owner, 0, 0, 0x48, 0, 0);

    /// <summary>The house has a commission running for the party, or will not see it now.</summary>
    // orig: open-quest-owner-value(0xA, owner, location-now) || open-quest-owner-value(3, owner, location-now) || event-kind-at(7, owner, location-now) in the main
    // functions R060+0x1FEC, R061+0x0978, R061+0x2278 (the callers add their own reputation and favour tests).
    public static bool CommissionPending(CardContext c, int owner) =>
        c.Events.QuestWithValue(FarQuest, owner, c.Here) || c.Events.QuestWithValue(DeliveryQuest, owner, c.Here)
        || c.EventTypeAtHere(BusyKind, owner);

    /// <summary>A commission of the house has been carried out.</summary>
    // orig: resolved-quest-at-owner(0xA, owner, location-now) || resolved-quest-at-owner(3, owner, location-now) in the same three main functions.
    public static bool CommissionDone(CardContext c, int owner) =>
        c.Events.ResolvedQuestAt(FarQuest, owner, c.Here) || c.Events.ResolvedQuestAt(DeliveryQuest, owner, c.Here);
}
