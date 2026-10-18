using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Dunge02": in a city's prison, before the guards' table, to free an imprisoned companion
/// (kind 0x4D event here). The party can bribe the guards (5 florins), attack, throw a potion,
/// pray, or leave. A freed companion rejoins; a fight sets the city against the party.
/// </summary>
// orig: R115+0x0C9A (1901:000A) matched; tests: CardSmokeTests
// orig: R115+0x1488 (1901:07F8) matched (registration: standard item and saint pop-ups that name the member)
public sealed class PrisonCellsRescue : Card
{
    private const int CellsPicture = 279;

    public override int Number => 286;

    public override int Run(CardContext c)
    {
        Show(c, CellsPicture);
        c.Next = Number;
        var menu = Menu(c, 0);
        menu.Set(OptionState.Enabled, 0, 1, 2, 3, 4);
        c.SetCardItems(0x5F, 0x60, 0x65, 0x61);
        c.SetCardSaints(0, 0x59, 0x61, 0x80);
        menu[2] = c.ItemsUsable();
        menu[3] = c.SaintsUsable();

        menu.Popup = () => c.MemberPopupHint();

        return menu.Run(
            // orig: R115+0x1000 (1901:0370) matched; hint R115+0x1058
            new MenuLine(_ => BribeTheGuards(c)),
            // orig: R115+0x1090 (1901:0400) matched; hint R115+0x109A
            // C: the friend is freed first, then the fight is fought: the original calls the rescue, then the battle.
            new MenuLine(_ => { FreeTheFriend(c); FightTheGuards(c); }),
            // orig: R115+0x1196 (1901:0506) matched (hint)
            new MenuLine(_ => ThrowAPotion(c), () => OptionHint.Say("Select a person and a potion.")),
            // orig: R115+0x12A8 (1901:0618) matched (hint)
            new MenuLine(_ => CallOnASaint(c), () => OptionHint.Say("Select a person and a saint.")),
            // orig: R115+0x12E0 (1901:0650) matched; hint R115+0x12F4
            new MenuLine(_ => c.Next = c.IsDaylight ? 0x12 : 0x19),
            // DORMANT: lines 5..9 are never offered; 5, 6, 8, 9 let no time pass, 7 (R115+0x13B4) leaves like line 4.
            // orig: R115+0x132C (1901:069C) matched; hint R115+0x1338
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)),
            // orig: R115+0x13B4 (1901:0724) matched; hint R115+0x13C8
            new MenuLine(_ => c.Next = c.IsDaylight ? 0x12 : 0x19),
            new MenuLine(_ => c.PassHours(0)),
            new MenuLine(_ => c.PassHours(0)));
    }

    /// <summary>
    /// The imprisoned friend (the character in the kind 0x4D event here) rejoins the party as
    /// $ChosenTwoName and is named in $Named1; then the party is out in the street.
    /// </summary>
    // orig: R115+0x0F8A (1901:02FA) matched; tests: CardSmokeTests
    // C: var_chosen2 = party_add(*events[v = event_find(0x4D, ...)]): the event's first word (its type) is the character.
    // DIVERGENCE: without such an event the original reads events[-1]; here nobody joins (-1).
    // UNCERTAIN: when nobody can join (-1), the original copies the name at party[-1] + 0x11; the leader's name is used.
    private static void FreeTheFriend(CardContext c)
    {
        var note = c.Events.Find(0x4D, -1, c.Here, -1, -1, -1);
        c.Variables.Chosen[1] = c.Events[note] is { } e ? c.CharacterJoins(e.Type) : -1;
        if (note != -1)
        {
            c.Events.Free(note);
        }
        c.Variables.Named[0] = c.MemberName(c.Variables.Chosen[1]);
        c.PassHours(0);
        c.Next = c.IsDaylight ? 0x12 : 0x19;
    }

    // orig: R115+0x1000 (1901:0370) matched; tests: CardSmokeTests
    // C: $Money1 in the texts is not set by this card (it shows whatever the last card left).
    private static void BribeTheGuards(CardContext c)
    {
        var bribe = Money.From(5, 0, 0);
        if (c.State.Party.Purse < bribe)
        {
            c.Screen.Text(2);
            FightTheGuards(c);
            return;
        }
        c.State.Party.Purse = c.State.Party.Purse.Subtract(bribe);
        c.Screen.Text(1);
        FreeTheFriend(c);
    }

    /// <summary>The potion (used up) blinds the guards by its quality; failing, they attack. The city's goodwill suffers a little either way.</summary>
    // orig: R115+0x10D2 (1901:0442) matched; tests: CardSmokeTests
    // C: the chance is the potion's quality, read (with var_chosen1 = menu_member) before it is taken.
    private static void ThrowAPotion(CardContext c)
    {
        c.Variables.Chosen[0] = c.MenuMember;
        var quality = c.ItemQuality(c.MenuMember, c.CardItems[c.MenuSlot]);
        c.TakeItem(c.MenuMember, c.CardItems[c.MenuSlot]);
        if (c.Random(100) <= quality)
        {
            FreeTheFriend(c);
            ShowPicture(c, 288);
            c.Screen.Text(3);
        }
        else
        {
            ShowPicture(c, 288);
            c.Screen.Text(4);
            FightTheGuards(c);
        }
        c.AdjustReputation(c.Here, -1, -4);
    }

    // orig: R115+0x11CE (1901:053E) matched; tests: CardSmokeTests
    // C: the rescue writes the friend's name into $Named1, so the saint's name is written again after it.
    private static void CallOnASaint(CardContext c)
    {
        var saint = c.CardSaints[c.MenuSlot];
        var answer = c.InvokeSaint(c.MenuMember, saint);
        if (answer == -1)
        {
            return;
        }
        c.Variables.Chosen[0] = c.MenuMember;
        c.NameToSlot(1, saint, 0);
        if (answer != 0)
        {
            FreeTheFriend(c);
            c.NameToSlot(1, saint, 0);
            ShowPicture(c, 289);
            c.Screen.Text(5);
        }
        else
        {
            ShowPicture(c, 288);
            c.Screen.Text(6);
            FightTheGuards(c);
        }
        ShowPicture(c, CellsPicture);
    }

    /// <summary>
    /// The guards (with this prison's fixed battle seed): won or fled, those away are lost and the
    /// city stays on alert; beaten, prison (card 0x0D). Then the city's goodwill falls and it
    /// remembers the attack (kind 0x11) for five or ten days.
    /// </summary>
    // orig: R115+0x0EEC (1901:025C) matched; tests: CardSmokeTests
    // C: the seed argument is cur_loc + 0x6F (a fixed seed per city); rand_mod(5) + 3 is the only draw.
    // DORMANT: the switch's explicit cases -0x40..-1 (and results above 4) skip to the reputation change.
    private static void FightTheGuards(CardContext c)
    {
        var guards = c.Random(5) + 3;
        switch (c.RunBattle(8, c.Here + 0x6F, 3, 1, guards, 0, 2, 1, -1, 0, 0))
        {
            case 0:
            case 1:
            case 2:
                c.ReportAway();
                c.ExtendEventHere(0x12, 30);
                c.Next = c.IsDaylight ? 0x12 : 0x19;
                break;
            case 3:
            case 4:
                c.Next = 0x0D;
                break;
        }
        c.AdjustReputation(c.Here, -25, -15);
        c.EventHere(0x11, c.HereQuery(0) <= -75 ? 240 : 120);
    }
}
