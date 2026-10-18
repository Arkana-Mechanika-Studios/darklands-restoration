namespace Darklands.Core.Rules;

/// <summary>
/// "Change Marching Order" (Party menu, Alt-O): every able member is marked; the player picks the first, second ... of them
/// (one prompt fewer than there are able members), each pick going to the end of the marching order; the members still marked
/// then follow in slot order. The order is the game's (<see cref="GameState.MarchingOrder"/>, the battle's own copy in a battle).
/// </summary>
// orig: R022+0x051C (1EF8:051C) draft (the C differs from the original's bytes only in two block-copy and slot-order codegen points per
// its header), R022+0x07AA (1EF8:07AA) draft (bytes match) (the pick); tests: MarchingOrderTests
// C: the marked flags are the battle's selection bytes (DS:9064, five): the screen keeps them aside and puts them back at the end.
// C: an F1..F5 key picks its slot whether it is marked or not, and again if already picked (only a click tests the mark and the status):
// C: the order can name a member twice or one who is not able, and grow past five entries, where the sixth byte is the length itself.
public sealed class MarchingOrderChoice
{
    private readonly byte[] _order;

    /// <param name="order">The marching order: five entries and the length (six bytes, written in place).</param>
    /// <param name="able">Whether the slot's status is 1 (the battle's combatant record, the party's record outside a battle).</param>
    // orig: R022+0x051C: the selection bytes cleared, then 1 for each slot of status 1 (counted), the length 0
    public MarchingOrderChoice(byte[] order, Func<int, bool> able)
    {
        _order = order;
        for (var i = 0; i < Selected.Length; i++)
        {
            Able[i] = able(i);
            if (Able[i])
            {
                Selected[i] = true;
                Count++;
            }
        }
        _order[LengthAt] = 0;
    }

    /// <summary>The length's byte, right after the five entries.</summary>
    public const int LengthAt = 5;

    /// <summary>The entries of a six-byte order, up to its length (the sixth entry is the length's own byte).</summary>
    // C: readers loop `j < marching_len` over the bytes from DS:C624: the sixth is DS:C629 itself.
    // DIVERGENCE: a length above 6 (a saved game's bytes) would read the globals after the order; here the entries stop at the sixth.
    public static IReadOnlyList<int> Entries(byte[] order) =>
        [.. Enumerable.Range(0, Math.Min((int)order[LengthAt], LengthAt + 1)).Select(k => (int)order[k])];

    /// <summary>A battle's order written back into the six bytes: the first five entries and the length (entries past the length stay).</summary>
    // orig: the battle works on DS:C624 / DS:C629 themselves (R016+0x0E2E, R022+0x07AA): the game's order is the battle's when it ends or saves
    public static void Store(IReadOnlyList<int> entries, byte[] order)
    {
        for (var k = 0; k < entries.Count && k < LengthAt; k++)
        {
            order[k] = (byte)entries[k];
        }
        order[LengthAt] = (byte)entries.Count;
    }

    /// <summary>Slot by slot: still to be placed (lit frame).</summary>
    public bool[] Selected { get; } = new bool[5];

    /// <summary>Slot by slot: of status 1 when the choice began.</summary>
    public bool[] Able { get; } = new bool[5];

    /// <summary>The able members counted.</summary>
    public int Count { get; }

    /// <summary>The prompts ("Select first" ...): one fewer than the able members (none for one or none).</summary>
    // orig: R022+0x051C: `for (k = 0; k < cnt - 1; k++)`
    public int Prompts => Math.Max(0, Count - 1);

    /// <summary>A click on slot <paramref name="slot"/>'s row picks it only if it is able and still marked.</summary>
    // orig: R022+0x051C: a release on the party column (x < 60) picks row y / 40 if that combatant's status byte is 1 and its selection byte 1
    public bool CanClick(int slot, bool ableNow) => slot is >= 0 and < 5 && ableNow && Selected[slot];

    /// <summary>The pick (R022+0x07AA): the slot goes to the end of the order and is no longer marked.</summary>
    // orig: R022+0x07AA (1EF8:07AA) draft (bytes match): the order's entry at the length (DS:C624 + DS:C629) = the slot, its selection byte 0, its
    // panel frame (root 0000:1588), the length + 1, the prompt answered
    // C: with the length at 5 the entry written is the length's own byte: the length becomes the slot + 1.
    // DIVERGENCE: a length above 5 (only from a saved game's bytes) writes past the order into the next globals (DS:C62A, the mouse
    // release count of this loop); here the entry is dropped and the length still counted.
    public void Take(int slot)
    {
        var length = _order[LengthAt];
        if (length <= LengthAt)
        {
            _order[length] = (byte)slot;
        }
        if (slot is >= 0 and < 5)
        {
            Selected[slot] = false;
        }
        _order[LengthAt]++;
    }

    /// <summary>After the prompts the members still marked follow in slot order.</summary>
    // orig: R022+0x051C: for each of the five slots whose selection byte is 1, R022+0x07AA
    public IReadOnlyList<int> Finish()
    {
        var taken = new List<int>();
        for (var i = 0; i < Selected.Length; i++)
        {
            if (Selected[i])
            {
                Take(i);
                taken.Add(i);
            }
        }
        return taken;
    }
}
