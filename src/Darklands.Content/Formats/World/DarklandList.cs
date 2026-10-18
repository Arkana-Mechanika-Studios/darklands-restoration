namespace Darklands.Content.Formats.World;

/// <summary>
/// Decoded `DARKLAND.LST` item, saint-name, and formula-name tables.
/// </summary>
/// <remarks>
/// The original file begins with item/saint/formula counts, then stores 200
/// fixed 0x2E-byte item slots, 136 full saint names, 136 short saint names,
/// 66 full formula names, and 66 short formula names. The saint order is the
/// definitive index order shared with `DARKLAND.SNT` and character saint
/// knowledge bits.
/// </remarks>
public sealed record DarklandList(
    string AssetName,
    IReadOnlyList<DarklandItemDefinition> Items,
    IReadOnlyList<DarklandSaintName> Saints,
    IReadOnlyList<DarklandFormulaName> Formulae);

public sealed record DarklandItemDefinition(
    int Code,
    string Name,
    string ShortName,
    int Type,
    IReadOnlyList<byte> Flags,
    int Weight,
    int Quality,
    int Rarity,
    int Unknown1,
    int Unknown2,
    int Value);

public sealed record DarklandSaintName(
    int Index,
    string FullName,
    string ShortName);

public sealed record DarklandFormulaName(
    int Index,
    string FullName,
    string ShortName);

