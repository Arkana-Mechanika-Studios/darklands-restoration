using Darklands.Content.Formats.World;
using Darklands.Core.Items;

namespace Darklands.Content;

/// <summary>The alchemical formulas: DARKLAND.ALC (mystic number, risk, ingredients) with their names from DARKLAND.LST.</summary>
public static class FormulaBook
{
    public static IReadOnlyList<Formula> Load(GameFiles files, WorldData world)
    {
        var alchemy = AlchemyDecoder.DecodeBytes(files.Read("DARKLAND.ALC"), "DARKLAND.ALC");
        var names = world.Items.Formulae;
        return alchemy.Formulas.Select(f => new Formula(
            f.Index,
            f.Index < names.Count ? names[f.Index].FullName : "",
            f.Index < names.Count ? names[f.Index].ShortName : "",
            f.Description,
            f.MysticalNumber,
            f.RiskFactor,
            f.RawIngredientSlots.Select(i => new Ingredient((sbyte)(i.Quantity & 0xFF), i.ItemCode)).ToList())).ToList();
    }
}
