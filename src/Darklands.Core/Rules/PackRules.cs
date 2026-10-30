using Darklands.Core.Characters;
using Darklands.Core.Items;

namespace Darklands.Core.Rules;

/// <summary>
/// What the character sheet does with a member's pack: readying an item, handing items to
/// another member and reordering the pack. Members are slots 0..4.
/// </summary>
public static class PackRules
{
    // orig: R031+0x5A02 (18E7:11B2) draft (bytes match); tests: CampAndSheetRulesTests
    // C: the original tests the flags of item code & 0xFFF but stores the stack's own type and quality bytes.
    /// <summary>
    /// Readies stack <paramref name="index"/>: a hand weapon (kind bits 0–3), else a missile
    /// weapon, else vital armour (types 0x43..0x4B), limb armour (0x4C..0x5E) or a shield
    /// (0x5F..0x61); anything else changes nothing. The equipment weight is recomputed.
    /// </summary>
    public static void Equip(GameState s, int member, int index)
    {
        var m = s.Party.Slot(member);
        var stack = m.Pack[index];
        var flags = s.Item(stack.Code & 0xFFF).Flags;
        var item = new EquippedItem(stack.Type, stack.Quality);
        if ((flags & ItemFlags.HandWeapon) != 0)
        {
            m.Weapon = item;
        }
        else if ((flags & ItemFlags.MissileWeapon) != 0)
        {
            m.MissileWeapon = item;
        }
        else if (stack.Type is >= 0x43 and < 0x4C)
        {
            m.VitalArmor = item;
        }
        else if (stack.Type is >= 0x4C and < 0x5F)
        {
            m.LimbArmor = item;
        }
        else if (stack.Type is >= 0x5F and <= 0x61)
        {
            m.Shield = item;
        }
        Inventory.EquipmentWeight(s, member);
    }

    // orig: R031+0x4850 (hand-over-one) R031+0x4850 (18E7:0000) draft (bytes match); tests: CampAndSheetRulesTests
    // DORMANT: the original first maps idx through worn-item-type(from, idx) when slot == 6 (the equipment box), which is no list and never gives.
    /// <summary>Gives one item of stack <paramref name="index"/> to another member whose pack has room.</summary>
    public static void GiveOne(GameState s, int from, int to, int index)
    {
        if (to != from && s.Party.Slot(to).Pack.Stacks < Inventory.MaxStacks)
        {
            Inventory.AddStack(s, to, s.Party.Slot(from).Pack[index] with { Count = 1 });
            Inventory.TakeAt(s, from, index);
        }
    }

    // orig: R031+0x48CC (hand-over-stack) R031+0x48CC (18E7:007C) draft (bytes match); tests: CampAndSheetRulesTests
    // DORMANT: the slot == 6 mapping as for GiveOne.
    /// <summary>Gives the whole stack <paramref name="index"/> to another member whose pack has room.</summary>
    public static void GiveStack(GameState s, int from, int to, int index)
    {
        if (to != from && s.Party.Slot(to).Pack.Stacks < Inventory.MaxStacks)
        {
            Inventory.AddStack(s, to, s.Party.Slot(from).Pack[index]);
            Inventory.RemoveStackAt(s, from, index);
        }
    }

    // orig: R031+0x5922 (item-relocate) R031+0x5922 (18E7:10D2) draft (bytes match); tests: CampAndSheetRulesTests
    /// <summary>Moves stack <paramref name="from"/> to position <paramref name="to"/>, the stacks between shifting over.</summary>
    public static void Move(GameState s, int member, int from, int to)
    {
        var pack = s.Party.Slot(member).Pack;
        // C: `nitems > from && nitems > to` with nitems a signed char; the entries between shift one place towards from.
        if (to != from && pack.Stacks > from && pack.Stacks > to)
        {
            var stack = pack[from];
            do
            {
                if (to < from)
                {
                    pack[from] = pack[from - 1];
                    from--;
                }
                else
                {
                    pack[from] = pack[from + 1];
                    from++;
                }
            }
            while (to != from);
            pack[to] = stack;
        }
    }
}
