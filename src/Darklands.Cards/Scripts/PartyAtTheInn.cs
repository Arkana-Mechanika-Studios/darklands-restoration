using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Core.Events;
using Darklands.Core.Rules;
using Darklands.Core.World;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Party02": the opening scene. The party gathers at an inn, swears its pact, and the
/// adventure begins on a summer morning with a few groschen in the purse.
/// </summary>
// orig: R054+0x09B8 (18D3:0008) matched; R136+0xD090[0x29]; tests: CardSmokeTests (runs it in a city and in the wilderness), TextAndOpeningTests
public sealed class PartyAtTheInn : Card
{
    public const int CardNumber = 0x29;

    // orig: pic_names+0x0F97 (340); the lone adventurer's picture is +0x0F9B (341)
    private const int Picture = 340;
    private const int AlonePicture = 341;

    /// <summary>The copy-protection note the first map visit looks for (event kind 0x63) and how long this card makes it last: 0x2D0 hours, thirty days.</summary>
    private const int CopyProtectionEvent = 0x63;
    private const int CopyProtectionHours = 0x2D0;

    public override int Number => CardNumber;

    // orig: the body of R054+0x09B8
    public override int Run(CardContext c)
    {
        var state = c.State;
        Show(c, Picture);

        // DIVERGENCE: the card also sets DS:EE72 = 7 and DS:EE70 = 0 (flags of the pop-up boxes the menu draws: with DS:EE72 >= 0 a bordered box is filled behind a pop-up, R043+0x2ACA); nothing here uses pop-ups on this card.
        // orig: var_chosen1..4 = leader, 1, 2, 3 ($ChosenOneName ... $ChosenFourName)
        c.Variables.Chosen[0] = state.Party.LeaderIndex;
        c.Variables.Chosen[1] = 1;
        c.Variables.Chosen[2] = 2;
        c.Variables.Chosen[3] = 3;

        // C: *(int far *)(locations[cur_loc] + 0x12) = 9 sets the local reputation of the starting city; the original does not check cur_loc.
        // DIVERGENCE: with no location here (the wilderness, cur_loc = -1) nothing is written; the original would write into locations[-1].
        if (state.Here is { } inn)
        {
            inn.Reputation = 9;
        }
        StartTheAdventure(c);

        // orig: R054+0x1134 (the card's own reset): all twenty option states hidden, card_saints / card_items = -1
        c.SetCardItems();
        c.SetCardSaints();

        int menuText;
        if (state.Party.Members.Count == 1)
        {
            ShowPicture(c, AlonePicture);
            menuText = 1;   // C: v_di = 1 is both the text block and (opt_flag_1 = v_di) the option state "enabled"
        }
        else
        {
            TellThePact(c);
            menuText = 6;
        }

        // orig: opt_on[0..3] = 1, opt_on[4..9] = 0; opt_fn[0..9] = R054+0x0DC8 .. 074c (the hints), the handlers after the menu are 0410 .. 0740
        var menu = Menu(c, menuText);
        menu.Enable(0, 1, 2, 3);
        c.Next = Number;
        return menu.Run(
            new MenuLine(() => c.Next = 0x1D, () => OptionHint.Say(SuccessAssured, state.Party.LeaderIndex)),   // R054+0x0DC8 / R054+0x0DC0: "Success is assured." for the leader, on to card 0x1D
            new MenuLine(() => { c.PassHours(1); c.Next = 0x0C; }),                                                  // R054+0x0E14 / R054+0x0E02: an hour passes, then the map (card 0x0C)
            new MenuLine(() => c.Next = 0x06, OptionHint.Saying(NoRisk)),                                              // R054+0x0E54 / R054+0x0E4C: card 6
            new MenuLine(() => c.Next = 0x09, OptionHint.Saying(NoRisk)),                                              // R054+0x0E94 / R054+0x0E8C: card 9
            // DORMANT: options 4..9 are never offered (opt_flag_4..9 = 0) and nothing sets them. Option 4 is a start-up cheat: it gives the leader a set of items, puts the party at location 0xB9 (map 0xC0, 0x39F) and goes to card 0x167. 5..9 only pass 0 hours and say "Success is assured.".
            new MenuLine(StartInTheCheatPlace, OptionHint.Saying(SuccessAssured)),            // R054+0x0FA8 / R054+0x0ECC
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),            // 063c / 0630
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),            // 0680 / 0674
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),            // 06c4 / 06b8
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)),            // 0708 / 06fc
            new MenuLine(() => c.PassHours(0), OptionHint.Saying(SuccessAssured)));           // 074c / 0740

        // The start-up cheat of the hidden option 4.
        void StartInTheCheatPlace()
        {
            c.PassHours(0);
            // orig: three location facts (kind 0x1C, types 1, 0x18 and 0x16, tag 1) here, for 9999 hours, owner everybody
            foreach (var type in new[] { 1, 0x18, 0x16 })
            {
                state.Events.Create(Who.Everybody, state.CurrentLocation, 0, EventKinds.LocationFact, type, 0, 0, EventLog.Forever, 0, 1);
            }
            foreach (var item in new[] { 0x8C, 0x8F, 0x8C, 0x8E, 0x8D, 0x90, 0x91 })
            {
                c.AddItem(-1, item);
            }
            state.MapPosition = new MapPosition(0xC0, 0x39F);
            state.CurrentLocation = 0xB9;
            c.Next = 0x167;
        }
    }

    /// <summary>A new game starts at 09:00 on a day between 24 and 29 of May to August, with 8–10 groschen and 10–12 pfennigs.</summary>
    // orig: the money_set / cur_hour / cur_day / cur_month / event_here(0x63, 0x2d0) / ev8_exists(1) part of R054+0x09B8 (R029+0x4FB4 money_set, R029+0x2930 event_here, R029+0x32FE ev8_exists, thunk 09C0:1F1D)
    private static void StartTheAdventure(CardContext c)
    {
        var state = c.State;
        // C: money_set(party_money, 0, rand_mod(3) + 8, rand_mod(3) + 0xa): the arguments are evaluated right to left, so the pfennigs are drawn first.
        var pfennigs = c.Random(3) + 10;
        var groschen = c.Random(3) + 8;
        state.Party.Purse = Money.From(0, groschen, pfennigs);
        // C: hour 9; day = rand_mod(6) + 0x18 (24..29) is drawn before month = rand_mod(4) + 4 (May..August): two statements, so top to bottom.
        var day = c.Random(6) + 24;
        var month = c.Random(4) + 4;
        state.Date = state.Date with { Hour = 9, Day = (short)day, Month = (short)month };
        // The copy-protection note: for thirty days the map does not ask the protection question (the first visit to the map creates its own for 16000 hours, see DarklandsGame).
        if (!state.Events.Started(CopyProtectionEvent))
        {
            state.Events.CreateHere(CopyProtectionEvent, CopyProtectionHours);
        }
        // thunk 09C0:1F1D starts the witches' sabbat quest (tag 1) unless one exists.
        if (!state.Events.QuestExists(Quests.Sabbat))
        {
            c.StartSabbatQuest();
        }
    }

    /// <summary>The pact: one text for each member beyond the second, each with its own picture.</summary>
    // orig: the else branch of R054+0x09B8: card_load_near with pic_names+0x0F9F/+0x0FA3/+0x0F8B/+0x0F93/+0x0F8F (342, 343, 337, 339, 338) and card_text(2..5, 2)
    // C: party_present_count() is called twice (>= 3, then >= 4); no text in between changes the party, so one count serves.
    private static void TellThePact(CardContext c)
    {
        var present = c.State.Party.Present.Count();
        ShowPicture(c, 342);
        c.Screen.Text(2);
        ShowPicture(c, 343);
        c.Screen.Text(3);
        if (present >= 3)
        {
            ShowPicture(c, 337);
            c.Screen.Text(4);
        }
        if (present >= 4)
        {
            ShowPicture(c, 339);
            c.Screen.Text(5);
        }
        ShowPicture(c, 338);
    }
}
