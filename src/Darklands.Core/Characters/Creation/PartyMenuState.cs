namespace Darklands.Core.Characters.Creation;

/// <summary>
/// What the two party menus ("Create a new party" and the inn's roster, its "reform your party" twin) keep
/// between keys and between visits: the highlighted row of the character list, its length, the character
/// shown on each row, the figure image chosen last and the figure colours. They are data-segment globals
/// in the original: one menu finds them as the other left them.
/// </summary>
// orig: DS:E7DA (the row), DS:E7DC (the length), DS:8D06 (the row table, 20 words before the record buffer DS:8D2E),
// DS:E7E0 (the image), DS:E753..DS:E76A and DS:E890 / DS:E892 / DS:E894 (PortraitColours), read and written by R037+0x0444
// (1462:0444) and R038+0x02F8 (1462:02F8), both draft; tests: InnRosterTests
// C: the row table is only written by the inn's list (R038+0x23EC); the creation menu declares it and never uses it.
public sealed class PartyMenuState
{
    /// <summary>Words in the row table before the record buffer that follows it.</summary>
    public const int RowTableLength = 20;

    /// <summary>The highlighted row (DS:E7DA).</summary>
    public int Selected { get; set; }

    /// <summary>The list's length as the inn's list leaves it (DS:E7DC): every record of the roster, shown or not.</summary>
    public int Count { get; set; }

    /// <summary>The roster entry each row of the inn's list shows (DS:8D06); rows past the last listing keep older entries.</summary>
    public int[] Rows { get; } = new int[RowTableLength];

    /// <summary>The figure image chosen last with S (DS:E7E0), an index into the figure list.</summary>
    public int Image { get; set; }

    /// <summary>The shared figure palette and the three colour keys' steps.</summary>
    public PortraitColours Colours { get; } = new();
}
