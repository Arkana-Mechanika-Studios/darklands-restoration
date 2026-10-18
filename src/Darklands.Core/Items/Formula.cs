namespace Darklands.Core.Items;

/// <summary>One ingredient slot of a formula: how many of which item (an empty slot is 0 of item 0).</summary>
public readonly record struct Ingredient(int Count, int Item);

// orig: struct formula of DARKLAND.ALC as read by R033+0x0000 / R033+0x17F6 (0x68-byte records); names from DARKLAND.LST
/// <summary>
/// An alchemical formula (DARKLAND.ALC with its names from DARKLAND.LST). Formula f makes
/// potion <see cref="Potions.FirstPotion"/> + f / 3 of grade f % 3.
/// </summary>
/// <param name="Slots">The five ingredient slots as stored; the ingredients end at the first empty slot.</param>
/// <param name="Risk">0 low, 1 medium, 2 high.</param>
public sealed record Formula(int Index, string Name, string ShortName, string Description, int MysticNumber, int Risk,
    IReadOnlyList<Ingredient> Slots)
{
    /// <summary>The ingredients: the slots up to the first empty one.</summary>
    public IEnumerable<Ingredient> Ingredients => Slots.TakeWhile(i => i.Count != 0);

    // C: R033+0x094A has cases 0, 1, 2 only; another risk leaves the buffer uninitialised ("High" here).
    /// <summary>"Low", "Medium" or "High".</summary>
    public string RiskName => Risk switch { 0 => "Low", 1 => "Medium", _ => "High" };
}
