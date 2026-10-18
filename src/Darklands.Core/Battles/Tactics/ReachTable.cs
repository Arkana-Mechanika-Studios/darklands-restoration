namespace Darklands.Core.Battles.Tactics;

/// <summary>
/// The AI's far tables at 20A5:787E: the path costs between combatants (words) and, right after
/// them at 20A5:7900, who can reach whom (13 × 13 bytes). The cost rows overlap: row i starts at
/// word i·5 but 13 words are written per row, so the last rows run into the reach bytes. The
/// shared memory is kept so the clobbering is the original's.
/// </summary>
// orig: R136+0x787E / R136+0x7900, written by R014+0xFDBE and R014+0xF438 (spec D §5.0, asm-verified)
// C: cost[i*5 + j] is a word index into a 0x82-byte area followed by reach[13][13]: writing row 12 overwrites
// reach[0][0..12] and reach[1][0..2].
public sealed class ReachTable
{
    /// <summary>The cost of an unreachable combatant.</summary>
    public const int Unreachable = 32000;

    private const int ReachOffset = 0x82;
    private readonly byte[] _memory = new byte[ReachOffset + Combatant.Count * Combatant.Count + 16];

    /// <summary>reach[i][j]: i and j are within striking reach.</summary>
    public bool this[int i, int j]
    {
        get => _memory[ReachOffset + i * Combatant.Count + j] != 0;
        set => _memory[ReachOffset + i * Combatant.Count + j] = (byte)(value ? 1 : 0);
    }

    /// <summary>cost[i*5 + j] (also the R018 distance rows).</summary>
    public int Cost(int index) => (short)(_memory[2 * index] | _memory[2 * index + 1] << 8);

    public void SetCost(int index, int value)
    {
        _memory[2 * index] = (byte)value;
        _memory[2 * index + 1] = (byte)(value >> 8);
    }

    /// <summary>Clears the reach bytes.</summary>
    public void ClearReach() => Array.Clear(_memory, ReachOffset, Combatant.Count * Combatant.Count);
}
