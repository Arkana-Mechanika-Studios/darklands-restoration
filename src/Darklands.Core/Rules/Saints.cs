using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

/// <summary>
/// What a saint does when a prayer succeeds (the saint functions' "help" part: temporary
/// boosts, healing, standing, money, events): <see cref="SaintEffects"/>.
/// </summary>
public interface ISaintEffects
{
    /// <summary>Saint <paramref name="saint"/> helps member <paramref name="who"/>, who spent <paramref name="favor"/> divine favor.</summary>
    void Help(GameState state, int who, int saint, int favor);
}

/// <summary>No saint effects: a prayer's success changes nothing (for tests and tools).</summary>
public sealed class NoSaintEffects : ISaintEffects
{
    public static readonly NoSaintEffects Instance = new();

    public void Help(GameState state, int who, int saint, int favor)
    {
    }
}

/// <summary>Saints the members know, and the chance that a prayer reaches a saint.</summary>
public static class Saints
{
    /// <summary>
    /// The member (−1 leader, −2 any present member) knows saint <paramref name="saint"/>
    /// (<c>member_bit</c>: the 160 knowledge bits, saints first).
    /// </summary>
    // orig: member_bit R029+0x1260 (0E76:1260) draft; tests: PracticeAndItemsTests
    // C: checked against the asm (1260 differs in its loop set-up only); mask 0x80 >> (bit & 7) of saints_known[who][bit >> 3].
    // DIVERGENCE: who >= 5 returns whatever AX holds in the original; here that slot is empty (false).
    // C: the bits are MSB-first: saint b is byte b >> 3, mask 0x80 >> (b & 7) of the member's 0x14 bytes.
    public static bool Knows(GameState s, int who, int saint)
    {
        if (who == Who.Everybody)
        {
            return s.Party.Members.Any(m => m.IsPresent && m.Saints.Contains(saint));
        }
        return s.Party.Slot(who).Saints.Contains(saint);
    }

    /// <summary>
    /// The member learns saint <paramref name="saint"/> (<c>set_member_bit</c>). <paramref name="who"/>
    /// is not resolved: −1 ("the leader" to the callers) writes the 20 bytes before the saint table,
    /// which are bytes 2..21 of member 4's formula table, so the leader learns nothing and member 4's
    /// formula byte 2 + saint / 8 gets bit 0x80 &gt;&gt; (saint % 8).
    /// </summary>
    // orig: set_member_bit R029+0x1302 (0E76:1302) matched; tests: OriginalMemoryTests
    // C: `saints_known[who][bit >> 3] |= 0x80 >> (bit & 7)` at 2E38:082C + who * 0x14: saints_known[-1] = 2E38:0818..082B, the
    // C: formula bytes 2..21 of member 4 (2E38:07BE + 4 * 0x16 = 0816), absent or not (Party.SetMemoryByte).
    // BUG: "learn the saint of this place" (R056 19c4) and the teaching saints pass -1 and teach nobody (FixSaintLearnedByLeader).
    public static void Learn(GameState s, int who, int saint)
    {
        if (who == Who.Leader && s.Behaviour.FixSaintLearnedByLeader)
        {
            who = s.Party.LeaderIndex;
        }
        var at = Party.SaintByteOffset(who, saint >> 3);
        s.Party.SetMemoryByte(at, s.Party.MemoryByte(at) | 0x80 >> (saint & 7));
    }

    /// <summary>
    /// The chance (percent) that a prayer spending <paramref name="favor"/> reaches the saint:
    /// 0 unless Virtue and favor reach the saint's minimums, else (Virtue − minimum) / 2 +
    /// base chance + favor − least favor. Saint 2 also needs an able woman in the party.
    /// </summary>
    // orig: R039+0x1FA0 (165C:0000) matched; tests: PracticeAndItemsTests; also SaintBookTests, Oracle:saints.json
    // C: every saint's mode 7 is R039+0x1FA0(t5, virtue +0x60, t4, x, t2, 0) (checked over all 138 saint functions); only saint 2 (R039+0x21EC) first asks party_count_able().
    // C: party[who].f60 is read without resolving -1 (CardContext resolves it first).
    // C: (virtue - minimum) / 2 is a signed division of a non-negative difference; Virtue is the signed char at
    //   record +0x60. All 136 functions pass their own t5/t4/t2 and a bonus of 0 (the bonus p_10 is always 0).
    public static int Chance(GameState s, int who, int saint, int favor)
    {
        var t = SaintTable.Of(saint);
        if (saint == SaintTable.NeedsAWoman && PartyQueries.AbleWomen(s) == 0)
        {
            return 0;
        }
        var virtue = s.Party.Slot(who)[Skill.Virtue];
        return t.MinVirtue <= virtue && t.MinFavor <= favor ? (virtue - t.MinVirtue) / 2 - t.MinFavor + t.BaseChance + favor : 0;
    }

    /// <summary>The chance when the least favor is spent; 0 if the member's Divine Favor is lower (<c>saint_chance_lo</c>).</summary>
    // orig: saint_chance_lo R029+0x20C6 (0E76:20C6) matched; tests: PracticeAndItemsTests
    // C: cost = saint_fns[s](who, 0, 2) (t2), Divine Favor byte +0x4F compared signed; who is not resolved here.
    // DIVERGENCE: the original indexes the records with who as given (−1 reads before the party); callers resolve it.
    public static int ChanceLow(GameState s, int who, int saint)
    {
        var cost = SaintTable.Of(saint).MinFavor;
        return s.Party.Slot(who)[Attribute.DivineFavor] < cost ? 0 : Chance(s, who, saint, cost);
    }

    /// <summary>The chance when the most favor is spent, as much as the member has (<c>saint_chance_hi</c>).</summary>
    // orig: saint_chance_hi R029+0x2116 (0E76:2116) matched
    // C: declared void; the chance comes back in AX from saint_fns[s](who, cost, 7).
    public static int ChanceHigh(GameState s, int who, int saint)
    {
        var cost = Math.Min(SaintTable.Of(saint).MaxFavor, s.Party.Slot(who)[Attribute.DivineFavor]);
        return Chance(s, who, saint, cost);
    }
}
