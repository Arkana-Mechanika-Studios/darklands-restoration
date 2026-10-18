using Darklands.Core.Economy;

namespace Darklands.Core.Characters;

/// <summary>
/// The four ready-made adventurers of "Quickstart" (Gretchen, Gunther, Hans and Ebhard),
/// as the game sets them up.
/// </summary>
// Images: member_shield (DS:A668) as SAVES\DEFAULT stores it for the four (F60, F01, A00, C00).
public static class DefaultParty
{
    // orig: party_default R001+0x0000 (0E76:0000) draft
    // UNCERTAIN: the four records are taken from the original's Quickstart party (R001 party_default).
    public static Party Create(int seed)
    {
        var party = new Party { Seed = seed, Purse = Money.Zero };

        var gretchen = new Character
        {
            Name = "Gretchen Wilburg", ShortName = "Gretch", Age = 35, IsFemale = true,
            Image = "F60", ShieldLetter = 'A', Encumbrance = 57,
            VitalArmor = new(74, 25), LimbArmor = new(82, 25),
            Weapon = new(3, 25), MissileWeapon = EquippedItem.None, Shield = EquippedItem.None,
        };
        SetAttributes(gretchen, 30, 30, 29, 18, 31, 32, 99);
        SetSkills(gretchen, 35, 20, 6, 16, 4, 30, 13, 6, 13, 25, 40, 4, 4, 3, 2, 21, 11, 13, 4);
        gretchen.Pack.Add(new ItemStack(5, 3, 25, 1, 0));
        gretchen.Pack.Add(new ItemStack(43, 74, 25, 1, 0));
        gretchen.Pack.Add(new ItemStack(51, 82, 25, 1, 0));
        party.Members.Add(gretchen);

        var gunther = new Character
        {
            Name = "Gunther Langer", ShortName = "Gunther", Age = 40, IsFemale = false,
            Image = "F01", ShieldLetter = 'B', Encumbrance = 40,
            VitalArmor = new(74, 25), LimbArmor = new(80, 25),
            Weapon = new(15, 25), MissileWeapon = EquippedItem.None, Shield = EquippedItem.None,
        };
        SetAttributes(gunther, 39, 40, 26, 24, 16, 13, 99);
        SetSkills(gunther, 29, 47, 11, 32, 20, 15, 31, 2, 9, 19, 11, 2, 5, 12, 7, 26, 19, 11, 29);
        gunther.Pack.Add(new ItemStack(15, 15, 25, 1, 0));
        gunther.Pack.Add(new ItemStack(43, 74, 25, 1, 0));
        gunther.Pack.Add(new ItemStack(49, 80, 25, 1, 0));
        party.Members.Add(gunther);

        var hans = new Character
        {
            Name = "Hans Muller", ShortName = "Hans", Age = 40, IsFemale = false,
            Image = "A00", ShieldLetter = 'C', Encumbrance = 36,
            VitalArmor = new(73, 25), LimbArmor = new(78, 25),
            Weapon = EquippedItem.None, MissileWeapon = new(31, 25), Shield = EquippedItem.None,
        };
        SetAttributes(hans, 27, 25, 28, 20, 43, 16, 99);
        SetSkills(hans, 11, 19, 3, 6, 19, 3, 20, 48, 10, 19, 27, 23, 32, 8, 33, 8, 14, 4, 7);
        hans.Pack.Add(new ItemStack(30, 31, 25, 1, 0));
        hans.Pack.Add(new ItemStack(95, 132, 20, 1, 0));
        hans.Pack.Add(new ItemStack(104, 141, 45, 1, 0));
        hans.Pack.Add(new ItemStack(102, 139, 25, 1, 0));
        hans.Pack.Add(new ItemStack(115, 152, 35, 1, 0));
        hans.Pack.Add(new ItemStack(47, 78, 25, 1, 0));
        hans.Pack.Add(new ItemStack(42, 73, 25, 1, 0));
        hans.Pack.Add(new ItemStack(34, 65, 25, 30, 0));
        hans.Formulas.UnionWith([1, 21, 28, 61]);
        party.Members.Add(hans);

        var ebhard = new Character
        {
            Name = "Ebhard of Achdorf", ShortName = "Ebhard", Age = 45, IsFemale = false,
            Image = "C00", ShieldLetter = 'D', Encumbrance = 58,
            VitalArmor = new(74, 25), LimbArmor = new(82, 25),
            Weapon = new(17, 25), MissileWeapon = EquippedItem.None, Shield = EquippedItem.None,
        };
        SetAttributes(ebhard, 26, 25, 24, 28, 31, 29, 99);
        SetSkills(ebhard, 10, 20, 34, 9, 8, 4, 27, 8, 40, 52, 19, 23, 23, 40, 8, 12, 7, 5, 3);
        ebhard.Pack.Add(new ItemStack(17, 17, 25, 1, 0));
        ebhard.Pack.Add(new ItemStack(43, 74, 25, 1, 0));
        ebhard.Pack.Add(new ItemStack(51, 82, 25, 1, 0));
        ebhard.Saints.UnionWith([3, 23, 46, 50, 113]);
        party.Members.Add(ebhard);
        return party;
    }

    /// <summary>Endurance, Strength, Agility, Perception, Intelligence, Charisma, Divine Favor; current = maximum.</summary>
    private static void SetAttributes(Character c, params int[] values)
    {
        values.CopyTo(c.AttributeMaximums, 0);
        values.CopyTo(c.Attributes, 0);
    }

    /// <summary>The 19 skills in <see cref="Skill"/> order.</summary>
    private static void SetSkills(Character c, params int[] values) => values.CopyTo(c.Skills, 0);
}
