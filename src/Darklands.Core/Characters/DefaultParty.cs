using Darklands.Core.Economy;

namespace Darklands.Core.Characters;

/// <summary>
/// The four ready-made adventurers of "Quickstart" (Gretchen, Gunther, Hans and Ebhard),
/// as the game sets them up.
/// </summary>
// Images: member_shield (DS:A668) as SAVES\DEFAULT stores it for the four (F60, F01, A00, C00).
// DIVERGENCE: the original reads the images from SAVES\DEFAULT when Quickstart begins (the load R037+0x4184, whose
// open is not checked); without that file member_shield keeps its start-up contents. Here the images come with the
// party, so a folder without SAVES\DEFAULT still plays. The colours need no such choice (see Colours).
// C: what the original then does is read from the code, not run: the battle builds the figure's file name
// from member_shield (R002, "pics\<code>short.pic"), finds no such picture and stops.
public static class DefaultParty
{
    // orig: party_default R001+0x0000 (0E76:0000) draft
    // C: the four records are taken from the original's Quickstart party (R001 party_default).
    public static Party Create(int seed)
    {
        var party = new Party { Seed = seed, Purse = Money.Zero };

        var gretchen = new Character
        {
            Name = "Gretchen Wilburg", ShortName = "Gretch", Age = 35, IsFemale = true,
            Image = "F60", ImagePalette = [.. Colours[0]], ShieldLetter = 'A', Encumbrance = 57,
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
            Image = "F01", ImagePalette = [.. Colours[1]], ShieldLetter = 'B', Encumbrance = 40,
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
            Image = "A00", ImagePalette = [.. Colours[2]], ShieldLetter = 'C', Encumbrance = 36,
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
            Image = "C00", ImagePalette = [.. Colours[3]], ShieldLetter = 'D', Encumbrance = 58,
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

    /// <summary>
    /// The figure colours (member_palette, 24 bytes per party slot) the Quickstart party is drawn
    /// with: Gretchen, Gunther, Hans, Ebhard and the empty fifth slot.
    /// </summary>
    // orig: party_default R001+0x0000 (0E76:0000) draft, its last block R001+0x0797..0x09CD: immediate stores into member_palette
    // (checked against asm and the bytes of DARKLAND.EXE); the same 120 bytes as SAVES\DEFAULT at offset 0x111, which
    // the Quickstart load reads into a local and drops, so the colours never depend on that file.
    // tests: DefaultPartyTests, OriginalSaveTests, QuickstartColoursTests
    public static IReadOnlyList<byte[]> Colours { get; } =
    [
        [0x3F, 0x28, 0x19, 0x15, 0x15, 0x15, 0x0E, 0x0E, 0x0E, 0x09, 0x09, 0x09, 0x00, 0x00, 0x00, 0x22, 0x00, 0x00, 0x28, 0x00, 0x00, 0x2E, 0x00, 0x00],
        [0x39, 0x36, 0x00, 0x3D, 0x38, 0x15, 0x00, 0x18, 0x28, 0x00, 0x11, 0x25, 0x00, 0x15, 0x25, 0x1C, 0x00, 0x00, 0x17, 0x00, 0x00, 0x11, 0x00, 0x00],
        [0x00, 0x20, 0x34, 0x00, 0x1C, 0x2E, 0x00, 0x18, 0x28, 0x00, 0x14, 0x22, 0x00, 0x11, 0x1D, 0x00, 0x0D, 0x17, 0x00, 0x0A, 0x11, 0x09, 0x09, 0x09],
        [0x3F, 0x2A, 0x1D, 0x13, 0x0F, 0x07, 0x2B, 0x1B, 0x11, 0x25, 0x18, 0x0E, 0x1C, 0x13, 0x08, 0x18, 0x11, 0x09, 0x16, 0x10, 0x08, 0x13, 0x0F, 0x07],
        new byte[24],
    ];

    /// <summary>Endurance, Strength, Agility, Perception, Intelligence, Charisma, Divine Favor; current = maximum.</summary>
    private static void SetAttributes(Character c, params int[] values)
    {
        values.CopyTo(c.AttributeMaximums, 0);
        values.CopyTo(c.Attributes, 0);
    }

    /// <summary>The 19 skills in <see cref="Skill"/> order.</summary>
    private static void SetSkills(Character c, params int[] values) => values.CopyTo(c.Skills, 0);
}
