using Darklands.Core.Economy;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Learn00": the library of a church or monastery. For a gift the party can study the lives
/// of one of four saints (drawn from the party's seed and the place) and one person learns that
/// saint; the party leaves in the evening.
/// </summary>
// orig: R058+0x0928 (18CA:0008) matched; tests: CardSmokeTests
public sealed class LibraryOfSaints : Card
{
    public const int CardNumber = 0x39;

    private const int Picture = 310;

    private const int EveningHour = 0x12;

    public override int Number => CardNumber;

    // orig: R058+0x0928 (18CA:0008) matched (entry: the gift, the four saints, the two-line menu loop)
    // orig: R058+0x0BB0 (18CA:0290) matched (init helper: disables all 20 lines, installs "screwed up." and the saint pop-up hints "no risk", clears the item and saint slots)
    // DORMANT: R058+0x0C1C (18CA:02FC): the "screwed up." handler the init installs on all 20 lines; lines 0 and 1 get their own, 2..19 are disabled, so it never runs.
    // DORMANT: R058+0x0C54 (18CA:0334), R058+0x0C90 (18CA:0370), R058+0x0CCC (18CA:03AC) and R058+0x0D08 (18CA:03E8):
    //   "success is assured" handlers (they also set var_chosen1 = menu_member) that nothing in this file installs.
    // orig: R058+0x0D44 (18CA:0424), R058+0x0D7C (18CA:045C), R058+0x0DB4 (18CA:0494) and R058+0x0DEC (18CA:04CC) matched:
    //   the saint box's handlers for slots 0..3, all "No risk." for the whole party (CardMenu.SaintBox): a Shift hint in the box shows them.
    // C: this card's loop calls opt_fn[k] even for a choice in the box (never DS:EB0A[menu_slot]); opt_fn[0] is then the box's handler for
    // C: slot 0 (the box's layout is the menu's), the same "No risk." with no side effect, and the action does not use it.
    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.SetCardItems();
        c.SetCardSaints();

        // C: (unsigned)money_pfennigs is the low 16 bits of the purse in pfennigs; /30 is an unsigned division.
        var pfennigs = unchecked((ushort)c.State.Party.Purse.TotalPfennigs);
        var gift = Money.From(0, Math.Clamp(pfennigs / 0x1E + 0x0C, 0, 0xF0) / 0x0C, 0);
        c.Variables.Money[0] = gift;
        var learn = c.State.Party.Purse < gift ? OptionState.Shown : OptionState.Enabled;

        // C: the four saints are drawn with the generator seeded from the party and the place, which is then reseeded from the clock.
        var random = c.State.Random;
        random.Seed((uint)(ushort)(c.State.Party.Seed + c.Here));
        var first = c.Random(0x87);
        var second = c.Random(0x87);
        var third = c.Random(0x87);
        var fourth = c.Random(0x87);
        c.SetCardSaints(first, second, third, fourth);
        if (c.State.ClockSeed is { } clock)
        {
            random.Seed(clock());
        }

        // C: DS:5204 = 1 for the menu loop (back to 0 at the end): the saint pop-up lists every saint of the card for every member, not only those the member knows.
        c.PopupListsAll = true;
        var menu = Menu(c, 0);
        menu.SaintBox = () => OptionHint.Say(NoRisk);
        menu.Enable(0, 1);
        menu[0] = learn;
        c.Next = Number;
        var next = menu.Run(
            // orig: R058+0x0AD2 (18CA:01B2) matched (option 0 action, see LearnTheSaint); hint R058+0x0B2C (18CA:020C) "Select a saint"
            // C: unlike other cards the loop always calls the line handler, even when the choice came from the pop-up; it has no side effect.
            new MenuLine(() => LearnTheSaint(c), () => OptionHint.Say(CardTexts.SelectSaint, c.MenuMember)),
            // orig: R058+0x0B64 (18CA:0244) matched (option 1 action: an hour passes, back to the saved card); hint R058+0x0B76 (18CA:0256) "No risk." for the leader
            new MenuLine(() => { c.PassHours(1); c.Next = c.State.SavedCard; }, () => OptionHint.Say(NoRisk, c.Leader)));
        c.PopupListsAll = false;
        return next;
    }

    /// <summary>Pays the gift, studies until evening and the chosen member learns the chosen saint.</summary>
    // orig: R058+0x0AD2 (18CA:01B2) matched
    private static void LearnTheSaint(CardContext c)
    {
        c.WaitUntilHour(EveningHour);
        c.State.Party.Purse = c.State.Party.Purse.Subtract(c.Variables.Money[0]);
        c.LearnSaint(c.MenuMember, c.CardSaints[c.MenuSlot]);
        c.Next = c.State.SavedCard;
    }
}
