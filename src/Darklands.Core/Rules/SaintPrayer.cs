using Darklands.Core.Characters;
using Darklands.Core.Events;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>From where a prayer is made: which of the saint's use bits it tests, and what it returns.</summary>
public enum PrayerMode
{
    /// <summary>
    /// For one member (the character sheet's saints list: <c>saint-prayer</c>): uses bit 0, so every
    /// saint lets the player pick whom the prayer is for; shows "Saint Call Successful/Fails".
    /// </summary>
    ForMember = 0,

    /// <summary>
    /// For the party (a card's saint pop-up through <c>call-on-saint</c>: <c>saint-prayer-party</c>):
    /// uses bit 1, which only St.Charity has; no message; reports success.
    /// </summary>
    ForParty = 1,
}

/// <summary>How a prayer ended (<c>saint-prayer-party</c>'s result; <c>saint-prayer</c> reports success as <see cref="Failed"/>).</summary>
public enum PrayerOutcome
{
    Cancelled = -1,
    Failed = 0,
    Succeeded = 1,
}

/// <summary>
/// One prayer on the prayer screen, as the rules see it: the divine favor offered (starting at the
/// saint's least, or 0 when the member has less), the chance, and the prayer itself: a roll
/// rnd(100) ≤ chance brings the saint's help; on a failure, rnd(99) &lt; chance − 66 costs half the
/// offer again; the offer is always spent.
/// </summary>
/// <remarks>Faithful: the screen does not hold the offer to the saint's most (<see cref="SaintInfo.MaxFavor"/>, used only
/// by <see cref="Saints.ChanceHigh"/>); "more" stops once the chance reaches 99 or at the member's Divine Favor.</remarks>
// orig: saint-screen-entry R034+0x022C (1462:022C) matched; tests: SaintPrayerTests
public sealed class SaintPrayer
{
    private readonly GameState _state;

    // orig: saint-screen-entry R034+0x022C: DS:89EC = saint(ch, 0, 6) & (party ? 2 : 1); DS:887C = saint(ch, 0, 2), 0 if maxdf < it
    // C: "maxdf" (pchar +0x4f) is the member's current Divine Favor (attribute 6), a signed char.
    public SaintPrayer(GameState state, int member, int saint, PrayerMode mode)
    {
        _state = state;
        Member = member;
        Saint = saint;
        Mode = mode;
        if (!Listed)
        {
            // orig: R034+0x022C 0257..0299: the "saint function" of an unlisted slot answers what 6 and what 2 with the AX it was called
            //   with, 0 both times (sub ax, ax; then 0 & 2): no member list, offer 0.
            return;
        }
        var info = SaintTable.Of(saint);
        ChoosesMember = (info.Use & (mode == PrayerMode.ForParty ? SaintUse.Party : SaintUse.Self)) != 0;
        Favor = DivineFavor < info.MinFavor ? 0 : info.MinFavor;
    }

    /// <summary>
    /// The saint is one of the 136. A card can pass the empty saint slot −1 (Enter on a saint line whose box was not entered keeps
    /// the slot a Shift hint left, <c>card-saint-lines[3]</c> = −1, see SabbatMockBaptism): the screen then reads the saint tables at −1.
    /// </summary>
    // orig: saint-handler-tables 290E:2937[-1] = thunk 09C0:1E87 -> R029+0x4626 (the travel blessing's expiry handler): on land a bare retf,
    //   so every "what" it is asked returns the AX of the call. Reproduced: local/batch/b32/a6-saint (b32-saint-stale-slot, b32-saint-stale-slot-fail).
    // BUG: an unlisted slot gets a prayer screen: the last formula's name, a garbage feast day and picture (SaintPrayerScreen), "Success: 99",
    //   no Divine Favor offered, and P prays with the screen's font word as the chance (UnlistedChance).
    // DIVERGENCE (undefined-register): on terrain 1 or 2 every call of that function (what 6, 2, 1, 7 at each paint and key, 8) creates a
    //   travel-blessing event and returns its slot, which then becomes the member-list flag, the offer and the chances; only the help's
    //   event (what 8) is made here, the other calls are taken as on land.
    public bool Listed => Saint is >= 0 and < SaintTable.Count;

    /// <summary>
    /// The chance an unlisted slot's prayer rolls against: saint-wrap-up returns the AX of its what-7 call, DS:E7A0, the font word DS:2804 that
    /// saint-screen-entry saved; every screen that changes DS:2804 (R031, R032, R033, R035..R037, this one) puts it back, so from a card it is
    /// its initial 2.
    /// </summary>
    // orig: R034+0x06E2 06E5..0719 (DS:887A is 0: the member list never opens); DGROUP DS:2804 = 2
    public const int UnlistedChance = 2;

    /// <summary>The praying member (a slot 0..4): the chance uses their Virtue, the offer their Divine Favor.</summary>
    public int Member { get; }

    public int Saint { get; }

    public PrayerMode Mode { get; }

    /// <summary>The prayer can be for a member chosen from a list (else for −1: the leader).</summary>
    public bool ChoosesMember { get; }

    /// <summary>The divine favor offered (<c>DS:887C</c>).</summary>
    public int Favor { get; private set; }

    private int DivineFavor => _state.Party.Slot(Member)[Attribute.DivineFavor];

    /// <summary>The chance of success (saint function, what 7).</summary>
    // orig: saint-handler-tables[saint](ch, DS:887C, 7) -> R039+0x1FA0 (via Saints.Chance)
    // C: an unlisted slot answers what 7 with the AX of the call: 0 from the mouse's Pray line and the M key (R034+0x064C, 0x0942), the key
    //   code from P (R034+0x065D), DS:E7A0 in saint-wrap-up. Chance is the mouse's and M's value; the prayer itself uses UnlistedChance.
    public int Chance => Listed ? Saints.Chance(_state, Member, Saint, Favor) : 0;

    /// <summary>"Success: n", kept in 0..99.</summary>
    // orig: saint-odds-line-paint R034+0x09B2: sprintf("Success: %d", limit-to-range(0, 99, chance))
    // DIVERGENCE (undefined-register): for an unlisted slot the chance shown is the AX the text drawing before it (root 047C:055A) leaves; the
    //   original's screen showed 99 (local/batch/b32/a6-saint/orig-saint.png), kept here.
    public int ShownChance => Listed ? Math.Clamp(Chance, 0, 99) : 99;

    /// <summary>"DF Remaining: nn": the Divine Favor less the offer.</summary>
    // orig: saint-odds-line-paint R034+0x09B2: maxdf - DS:887C
    public int FavorRemaining => DivineFavor - Favor;

    /// <summary>The prayer line can be chosen: the chance is not 0.</summary>
    // C: for an unlisted slot the menu's what-7 call (saint-menu-paint R034+0x0B52) returns a drawing call's AX, not 0 on the original's
    //   screen (the line was not grey); the P key then prays (its key code is the nonzero answer), the mouse does not (PraysByMouse).
    public bool CanPray => !Listed || Chance != 0;

    /// <summary>The Pray line chosen with the mouse prays: the original asks what 7 with AX = 0 there, which an unlisted slot answers with 0.</summary>
    // orig: R034+0x064C..0x0692: di = 0x1950 from the mouse, key-waiting-test leaves AX = 0, the call returns 0, no prayer
    public bool PraysByMouse => Listed && CanPray;

    /// <summary>
    /// M / + : one more point, while the chance is below 99, up to the Divine Favor. Returns true
    /// when the status is redrawn (also when the Divine Favor holds the offer where it is).
    /// </summary>
    // orig: saint-screen-keys R034+0x08D2 keys 0x0d2b 0x324d 0x326d 0x4e2b: if (chance < 99) { if (++DS:887C > maxdf) DS:887C = maxdf; }
    public bool OfferMore()
    {
        if (Chance >= 99)
        {
            return false;
        }
        Favor = Math.Min(Favor + 1, DivineFavor);
        return true;
    }

    /// <summary>L / − : one point less, not below 0 (at 0 nothing is redrawn).</summary>
    // orig: saint-screen-keys R034+0x08D2 keys 0x0c2d 0x264c 0x266c 0x4a2d: if (--DS:887C < 0) { DS:887C = 0; return; }
    public bool OfferLess()
    {
        if (Favor - 1 < 0)
        {
            Favor = 0;
            return false;
        }
        Favor--;
        return true;
    }

    /// <summary>
    /// Prays for <paramref name="target"/> (a slot, or −1 the leader): the chance is the praying
    /// member's at the time of praying; success brings the saint's help; the favor is spent.
    /// </summary>
    // orig: saint-prayer-party R034+0x0000 (1462:0000) and saint-prayer R034+0x0102 (1462:0102),
    //   with saint-wrap-up R034+0x06E2 (the chance) matched; tests: SaintPrayerTests
    // C: roll-below(100) <= r: a chance of 0 still succeeds on a roll of 0 (the screen only lets a prayer start when
    //   the chance is not 0, but a member chosen from the list is prayed for whatever the chance is now).
    // C: DS:887C / 2 is a signed division of a non-negative offer; raise-attribute-by keeps Divine Favor in 1..99.
    // orig: saint-prayer R034+0x0166..0x01ED: the message (root 0000:24BC) comes after the roll and before the help on success
    //   (0x0179, then the saint's function at 0x0195), after the extra loss on failure (0x01D0, then 0x01E8); the favor spent after both
    public bool Pray(int target, ISaintEffects effects, Action<bool>? announce = null)
    {
        var chance = Listed ? Chance : UnlistedChance;
        var success = _state.Random.Below(100) <= chance;
        if (success)
        {
            announce?.Invoke(true);
            if (Listed)
            {
                effects.Help(_state, target, Saint, Favor);
            }
            else
            {
                // orig: R034+0x0060..0x0074: saint-handler-tables[-1](target, offer, 8) = R029+0x4626
                EventExpiry.RenewTravelBlessing(_state);
            }
        }
        else if (_state.Random.Below(99) < chance - 66)
        {
            MemberRules.AddAttribute(_state, Member, (int)Attribute.DivineFavor, -(Favor / 2));
            announce?.Invoke(false);
        }
        else
        {
            announce?.Invoke(false);
        }
        MemberRules.AddAttribute(_state, Member, (int)Attribute.DivineFavor, -Favor);
        return success;
    }

    /// <summary>What the prayer screen returns to its caller after praying.</summary>
    // orig: saint-prayer-party returns 1/0; saint-prayer returns 0 either way (its caller learns nothing)
    public PrayerOutcome Outcome(bool success) =>
        Mode == PrayerMode.ForParty && success ? PrayerOutcome.Succeeded : PrayerOutcome.Failed;
}
