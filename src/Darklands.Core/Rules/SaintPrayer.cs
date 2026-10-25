using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>From where a prayer is made: which of the saint's use bits it tests, and what it returns.</summary>
public enum PrayerMode
{
    /// <summary>
    /// For one member (the character sheet's saints list: <c>saint_pray</c>): uses bit 0, so every
    /// saint lets the player pick whom the prayer is for; shows "Saint Call Successful/Fails".
    /// </summary>
    ForMember = 0,

    /// <summary>
    /// For the party (a card's saint pop-up through <c>invoke_saint</c>: <c>saint_pray_party</c>):
    /// uses bit 1, which only St.Charity has; no message; reports success.
    /// </summary>
    ForParty = 1,
}

/// <summary>How a prayer ended (<c>saint_pray_party</c>'s result; <c>saint_pray</c> reports success as <see cref="Failed"/>).</summary>
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
// orig: saint_screen R034+0x022C (1462:022C) matched; tests: SaintPrayerTests
public sealed class SaintPrayer
{
    private readonly GameState _state;

    // orig: saint_screen R034+0x022C: DS:89EC = saint(ch, 0, 6) & (party ? 2 : 1); DS:887C = saint(ch, 0, 2), 0 if maxdf < it
    // C: "maxdf" (pchar +0x4f) is the member's current Divine Favor (attribute 6), a signed char.
    public SaintPrayer(GameState state, int member, int saint, PrayerMode mode)
    {
        _state = state;
        Member = member;
        Saint = saint;
        Mode = mode;
        var info = SaintTable.Of(saint);
        ChoosesMember = (info.Use & (mode == PrayerMode.ForParty ? SaintUse.Party : SaintUse.Self)) != 0;
        Favor = DivineFavor < info.MinFavor ? 0 : info.MinFavor;
    }

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
    // orig: saint_fns[saint](ch, DS:887C, 7) -> R039+0x1FA0 (via Saints.Chance)
    public int Chance => Saints.Chance(_state, Member, Saint, Favor);

    /// <summary>"Success: n", kept in 0..99.</summary>
    // orig: saint_draw_df R034+0x09B2: sprintf("Success: %d", clamp_range(0, 99, chance))
    public int ShownChance => Math.Clamp(Chance, 0, 99);

    /// <summary>"DF Remaining: nn": the Divine Favor less the offer.</summary>
    // orig: saint_draw_df R034+0x09B2: maxdf - DS:887C
    public int FavorRemaining => DivineFavor - Favor;

    /// <summary>The prayer line can be chosen: the chance is not 0.</summary>
    public bool CanPray => Chance != 0;

    /// <summary>
    /// M / + : one more point, while the chance is below 99, up to the Divine Favor. Returns true
    /// when the status is redrawn (also when the Divine Favor holds the offer where it is).
    /// </summary>
    // orig: saint_key R034+0x08D2 keys 0x0d2b 0x324d 0x326d 0x4e2b: if (chance < 99) { if (++DS:887C > maxdf) DS:887C = maxdf; }
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
    // orig: saint_key R034+0x08D2 keys 0x0c2d 0x264c 0x266c 0x4a2d: if (--DS:887C < 0) { DS:887C = 0; return; }
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
    // orig: saint_pray_party R034+0x0000 (1462:0000) and saint_pray R034+0x0102 (1462:0102),
    //   with saint_finish R034+0x06E2 (the chance) matched; tests: SaintPrayerTests
    // C: rand_mod(100) <= r: a chance of 0 still succeeds on a roll of 0 (the screen only lets a prayer start when
    //   the chance is not 0, but a member chosen from the list is prayed for whatever the chance is now).
    // C: DS:887C / 2 is a signed division of a non-negative offer; add_attr keeps Divine Favor in 1..99.
    // orig: saint_pray R034+0x0166..0x01ED: the message (root 0000:24BC) comes after the roll and before the help on success
    //   (0x0179, then the saint's function at 0x0195), after the extra loss on failure (0x01D0, then 0x01E8); the favor spent after both
    public bool Pray(int target, ISaintEffects effects, Action<bool>? announce = null)
    {
        var chance = Chance;
        var success = _state.Random.Below(100) <= chance;
        if (success)
        {
            announce?.Invoke(true);
            effects.Help(_state, target, Saint, Favor);
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
    // orig: saint_pray_party returns 1/0; saint_pray returns 0 either way (its caller learns nothing)
    public PrayerOutcome Outcome(bool success) =>
        Mode == PrayerMode.ForParty && success ? PrayerOutcome.Succeeded : PrayerOutcome.Failed;
}
