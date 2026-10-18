using Darklands.Core.Characters;
using Darklands.Core.Events;

namespace Darklands.Cards.Scripts;

/// <summary>
/// "$Party00": sitting around the inn's table the party ponders retirement and recruitment.
/// Look for somebody to join (the roster screen), let a companion retire with a fifth of the
/// party's wealth, dismiss a hireling, or finish and go back to the street (day) or the card
/// remembered for the night.
/// </summary>
// orig: R086+0x0DEE (1916:000E) matched; tests: CardSmokeTests
// orig: R086+0x18D6 (1916:0AF6) matched; R086+0x18F8 (1916:0B18) matched; tests: CardSmokeTests
// DORMANT: the "screwed up." default handler of the unlisted lines 10..19 (never offered).
public sealed class InnRetireOrRecruit : Card
{
    public const int CardNumber = 0xAA;

    private const int Picture = 337;
    private const int TalkingPicture = 193;

    /// <summary>Hirelings carry a term event of this kind.</summary>
    private const int HirelingTerm = EventKinds.MemberTerm;

    public override int Number => CardNumber;

    public override int Run(CardContext c)
    {
        Show(c, Picture);
        c.Next = CardNumber;
        // Lines: 0 recruit, 1..4 let the slot 0..3 retire, 5..8 dismiss the hireling in slot 1..4, 9 finish.
        var menu = Menu(c);
        menu.Enable(0, 9);
        if (c.PresentCount() >= 4)
        {
            // The party is full.
            menu[0] = OptionState.Shown;
        }
        if (c.PartySize >= 2)
        {
            OfferRetirements(c, menu);
        }
        for (var slot = 1; slot <= 4; slot++)
        {
            if (c.Status(slot) != CharacterStatus.Empty && c.Events.HasType(HirelingTerm, slot))
            {
                menu[4 + slot] = OptionState.Enabled;
            }
        }
        if (c.PresentCount() == 1)
        {
            DimRetirements(menu);
        }
        for (var i = 0; i < 5; i++)
        {
            c.Variables.Chosen[i] = i;
        }
        if (c.Next != CardNumber)
        {
            // DORMANT: the start-up never leaves this card.
            return c.Next;
        }
        return menu.Run(
            // orig: R086+0x123A (1916:045A) matched; R086+0x1324 (1916:0544) matched; tests: CardSmokeTests
            new MenuLine(() => FindSomebody(c, menu), OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x135C (1916:057C) matched; R086+0x13C4 (1916:05E4) matched; tests: CardSmokeTests
            new MenuLine(() => { Retire(c, menu, 0); menu[1] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x13FC (1916:061C) matched; R086+0x146A (1916:068A) matched; tests: CardSmokeTests
            new MenuLine(() => { Retire(c, menu, 1); menu[2] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x14A2 (1916:06C2) matched; R086+0x1510 (1916:0730) matched; tests: CardSmokeTests
            new MenuLine(() => { Retire(c, menu, 2); menu[3] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x1548 (1916:0768) matched; R086+0x15B6 (1916:07D6) matched; tests: CardSmokeTests
            new MenuLine(() => { Retire(c, menu, 3); menu[4] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x15EE (1916:080E) matched; R086+0x165C (1916:087C) matched; tests: CardSmokeTests
            new MenuLine(() => { Dismiss(c, menu, 1); menu[5] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x1694 (1916:08B4) matched; R086+0x1702 (1916:0922) matched; tests: CardSmokeTests
            new MenuLine(() => { Dismiss(c, menu, 2); menu[6] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x173A (1916:095A) matched; R086+0x17A8 (1916:09C8) matched; tests: CardSmokeTests
            new MenuLine(() => { Dismiss(c, menu, 3); menu[7] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x17E0 (1916:0A00) matched; R086+0x184E (1916:0A6E) matched; tests: CardSmokeTests
            new MenuLine(() => { Dismiss(c, menu, 4); menu[8] = OptionState.Hidden; }, OptionHint.Saying(SuccessAssured)),
            // orig: R086+0x1886 (1916:0AA6) matched; R086+0x189E (1916:0ABE) matched; tests: CardSmokeTests
            new MenuLine(() => c.Next = c.IsDaylight ? c.State.SavedCard : c.State.SavedCard2, OptionHint.Saying(SuccessAssured)));
    }

    /// <summary>Lines 1..4 become available for the occupied slots (hirelings cannot "retire": they are dismissed).</summary>
    private static void OfferRetirements(CardContext c, CardMenu menu)
    {
        // C: the C tests "party", the status byte of the first slot (the leader's).
        if (c.Status(0) != CharacterStatus.Empty)
        {
            menu[1] = OptionState.Enabled;
        }
        for (var slot = 1; slot <= 3; slot++)
        {
            if (c.Status(slot) != CharacterStatus.Empty && !c.Events.HasType(HirelingTerm, slot))
            {
                menu[slot + 1] = OptionState.Enabled;
            }
        }
    }

    /// <summary>With only one member present nobody can retire: the lines are shown but cannot be chosen.</summary>
    private static void DimRetirements(CardMenu menu)
    {
        for (var i = 1; i <= 4; i++)
        {
            if (menu[i] == OptionState.Enabled)
            {
                menu[i] = OptionState.Shown;
            }
        }
    }

    private static void FindSomebody(CardContext c, CardMenu menu)
    {
        var hours = c.Random(3) + 2;
        c.Variables.Numbers[0] = hours;
        ShowPicture(c, TalkingPicture);
        c.Screen.Text(6);
        ShowPicture(c, Picture);
        c.PassHours(hours);
        // C: SI holds the hours here (R086+0x123A: `mov si, ax` after root 0000:0C70(3) + 2): the roster's list reads it as a location.
        c.ShowPartyRoster(hours);
        // C: lines are only switched on again here, never off (a member who joined a slot
        // that had been dimmed stays as it was).
        OfferRetirements(c, menu);
        menu[0] = c.PresentCount() >= 4 ? OptionState.Shown : OptionState.Enabled;
        c.Next = CardNumber;
    }

    private static void Retire(CardContext c, CardMenu menu, int slot)
    {
        c.Variables.Chosen[0] = slot;
        ShowPicture(c, TalkingPicture);
        c.Screen.Text(7);
        ShowPicture(c, Picture);
        // C: the first name is the slot 0's again before the hour passes.
        c.Variables.Chosen[0] = 0;
        c.PassHours(1);
        LetMemberGo(c, menu, slot);
        c.Next = CardNumber;
    }

    private static void Dismiss(CardContext c, CardMenu menu, int slot)
    {
        c.Variables.Chosen[0] = slot;
        ShowPicture(c, TalkingPicture);
        c.Screen.Text(9);
        ShowPicture(c, Picture);
        c.Variables.Chosen[0] = 0;
        c.PassHours(1);
        LetMemberGo(c, menu, slot);
        c.Next = CardNumber;
    }

    /// <summary>
    /// A companion retires with a fifth of the purse and of the stored wealth, or a hireling is
    /// dismissed (no share). Then the lines are refreshed.
    /// </summary>
    // orig: R086+0x114C (1916:036C) matched; tests: CardSmokeTests
    private static void LetMemberGo(CardContext c, CardMenu menu, int slot)
    {
        var party = c.State.Party;
        if (!c.Events.HasType(HirelingTerm, slot))
        {
            // C: money_pfennigs is a long stored into an unsigned (16-bit) variable, then divided unsigned:
            // purses over 65535 pfennigs (273 florins) give a wrapped share.
            var purseShare = (ushort)party.Purse.TotalPfennigs / 5;
            party.Purse = party.Purse.Subtract(0, 0, purseShare);
            // C: DS:9072 is the bank notes' money triple; the same 16-bit share is taken from it.
            var storedShare = (ushort)party.BankNotes.TotalPfennigs / 5;
            party.BankNotes = party.BankNotes.Subtract(0, 0, storedShare);
            // C: the share is only taken from the purse and the notes: nothing stores it (thunk 09C0:1EC3 = party_remove writes the
            // C: member's 0x80-byte record and no money); the character leaves for the roster.
            c.RemoveMember(slot);
        }
        else
        {
            c.SetStatus(slot, CharacterStatus.Empty);
            c.Events.Remove(HirelingTerm, slot, -1, -1, -1);
            c.DeleteMember(slot);
        }
        menu[0] = c.PresentCount() >= 4 ? OptionState.Shown : OptionState.Enabled;
        if (c.PresentCount() == 1)
        {
            DimRetirements(menu);
        }
    }
}
