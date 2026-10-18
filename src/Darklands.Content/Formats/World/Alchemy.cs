namespace Darklands.Content.Formats.World;

/// <summary>
/// Decoded `DARKLAND.ALC` alchemical formula table.
/// </summary>
/// <remarks>
/// The stock file contains one count byte followed by fixed 0x68-byte formula
/// records. The records are ordered as 22 formula families with three variants
/// each, matching the formula-name order used by the character sheet.
/// </remarks>
public sealed record Alchemy(
    string AssetName,
    IReadOnlyList<AlchemyFormula> Formulas);

public sealed record AlchemyFormula(
    int Index,
    string Description,
    int MysticalNumber,
    int RiskFactor,
    IReadOnlyList<AlchemyIngredient> Ingredients)
{
    /// <summary>
    /// The five physical four-byte ingredient slots from the formula record.
    /// Unlike <see cref="Ingredients"/>, this retains zero-filled slots needed
    /// by original owners that iterate the fixed record rather than the
    /// logical ingredient list.
    /// </summary>
    public IReadOnlyList<AlchemyIngredient> RawIngredientSlots { get; init; } =
        Ingredients
            .Concat(Enumerable.Repeat(new AlchemyIngredient(0, 0), 5))
            .Take(5)
            .ToArray();
}

public sealed record AlchemyIngredient(
    int Quantity,
    int ItemCode);

