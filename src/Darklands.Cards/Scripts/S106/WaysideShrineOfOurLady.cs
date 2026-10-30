using Darklands.Core.Characters;
using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$McGuf03": a wayside shrine of St. Mary kept by an old monk. When an item quest (tag 10)
/// leads here, the monk keeps the item: the party can demand it, buy it with a generous
/// offering, or just give a smaller offering and share a meal, or leave.
/// </summary>
// orig: R106+0x21EA (1A56:000A) matched; tests: CardSmokeTests
// orig: R106+0x26D6 (1A56:04F6) matched (registration: no item or saint lines)
public sealed class WaysideShrineOfOurLady : Card
{
    private const int ShrinePicture = 155;

    public override int Number => 251;

    public override int Run(CardContext c)
    {
        Show(c, ShrinePicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3);
        if (c.Events.QuestAt(10, c.Here))
        {
            c.NameToSlot(0, c.EventDetail(c.Events.Find(8, -1, c.Here, -1, 10, -1)), 0);
        }
        else
        {
            menu.Set(OptionState.Hidden, 0, 1);
        }
        c.Variables.Chosen[0] = c.Leader;
        var generous = Offering(c, 6, 25);
        var modest = Offering(c, 4, 50);
        c.Variables.Money[0] = generous;
        c.Variables.Money[1] = modest;
        if (menu[1] == OptionState.Enabled && c.State.Party.Purse < generous)
        {
            menu[1] = OptionState.Shown;
        }
        if (c.State.Party.Purse < modest)
        {
            menu[2] = OptionState.Shown;
        }

        // C: after every choice, before its handler, the loop sets text-slot-chosen-one = leader; each
        // action below does that first (the hints here have no side effects).
        menu.Popup = () => c.MemberPopupHint();
        return menu.Run(
            // orig: R106+0x250A (1A56:032A) matched; hint R106+0x2534
            new MenuLine(_ => { LeaderSpeaks(c); DemandTheItem(c); }, NoRiskForAll),
            // orig: R106+0x256C (1A56:038C) matched; hint R106+0x25BC
            new MenuLine(_ => { LeaderSpeaks(c); BuyTheItem(c, generous); }, NoRiskForAll),
            // orig: R106+0x25F4 (1A56:0414) matched; hint R106+0x263A
            new MenuLine(_ => { LeaderSpeaks(c); MakeAnOffering(c, modest); }, NoRiskForAll),
            // orig: R106+0x2672 (1A56:0492) matched; hint R106+0x2686
            new MenuLine(_ => { LeaderSpeaks(c); c.Screen.Text(4); c.Next = CardContext.MapCard; }, NoRiskForAll),
            // DORMANT: lines 4..9 are never offered; their actions R106+0x26BE.. and hints
            // R106+0x26C0.. are empty functions.
            // C: those hints return whatever is in AX/DX; a neutral hint is used.
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)),
            new MenuLine(_ => LeaderSpeaks(c)));
    }

    private static OptionHint NoRiskForAll() => OptionHint.Say(NoRisk);

    private static void LeaderSpeaks(CardContext c) => c.Variables.Chosen[0] = c.Leader;

    /// <summary>A few groschen a head plus a share of the purse, 1..24 groschen.</summary>
    // C: (unsigned)purse-in-pfennigs(purse) keeps the low 16 bits of the long, unsigned, before the division.
    private static Money Offering(CardContext c, int perHead, int share) =>
        Money.From(0, Math.Clamp(c.PresentCount() * perHead + (ushort)c.State.Party.Purse.TotalPfennigs / share, 1, 24), 0);

    // orig: R106+0x250A (1A56:032A) matched; tests: CardSmokeTests
    private static void DemandTheItem(CardContext c)
    {
        c.FinishQuest10(c.Here);
        c.Screen.Text(1);
        c.PassHours(1);
        c.Next = CardContext.MapCard;
    }

    // orig: R106+0x256C (1A56:038C) matched; tests: CardSmokeTests
    private static void BuyTheItem(CardContext c, Money offering)
    {
        c.FinishQuest10(c.Here);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(offering);
        c.Variables.Chosen[0] = c.Leader;
        c.Screen.Text(2);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 5, -1);
        c.PassHours(1);
        c.Next = CardContext.MapCard;
    }

    /// <summary>The offering and a meal with the monk; then on to card 0x6E, returning to the map.</summary>
    // orig: R106+0x25F4 (1A56:0414) matched; tests: CardSmokeTests
    private static void MakeAnOffering(CardContext c, Money offering)
    {
        c.State.Party.Purse = c.State.Party.Purse.Subtract(offering);
        c.Screen.Text(3);
        c.PracticeSkill(Who.Everybody, (int)Skill.Virtue, 7, 5, -1);
        c.PassHours(1);
        c.State.SavedCard = CardContext.MapCard;
        c.State.SavedCard2 = CardContext.MapCard;
        c.Next = 0x6E;
    }
}
