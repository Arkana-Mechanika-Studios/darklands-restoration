using Darklands.Core.Characters;
using Attribute = Darklands.Core.Characters.Attribute;

namespace Darklands.Core.Rules;

// orig: R031+0x43B2 (1462:43B2) draft (code identical; an earlier port's check matched the whole file, jump table included);
//   tests: CampAndSheetRulesTests
/// <summary>
/// Drinking a potion from the pack (the character sheet's P key). The strength of most
/// potions comes in bands of quality: up to 25, up to 35, up to 45, above. Original bugs,
/// reproduced: a potion above quality 45 falls into the last arm and is the <em>weakest</em>
/// (5) where there are four bands; the weapon potions add to the bonus byte without limit
/// (nibbles carry into each other) and record a bonus event that is never undone; Strongedge
/// and Great Power record the pack index instead of the potion.
/// </summary>
public static class PotionEffects
{
    private const uint PotionFlag = 0x0000_0800;
    private const uint ThrownFlag = 0x0100_0000;

    private const int DeadlyBlade = 0x69;
    private const int Strongedge = 0x6A;
    private const int GreatPower = 0x6B;
    private const int Trueflight = 0x6C;
    private const int Hardarmor = 0x6D;
    private const int Transformation = 0x6E;
    private const int Truesight = 0x6F;
    private const int NewWind = 0x70;
    private const int Ironarm = 0x71;
    private const int Quickmove = 0x72;
    private const int EssenceOfGrace = 0x73;
    private const int Firewall = 0x74;

    // orig: the P case of R031+0x10AE (1462:10AE): item flags 0x800 set and 0x1000000 clear
    /// <summary>A potion that is drunk, not thrown.</summary>
    public static bool CanDrink(GameState s, int code)
    {
        var flags = s.Item(code & 0xFFF).Flags;
        return (flags & PotionFlag) != 0 && (flags & ThrownFlag) == 0;
    }

    // orig: the P case of R031+0x10AE: R031+0x43B2 then item_take_one (R031+0x4CC4)
    /// <summary>Drinks one potion of stack <paramref name="index"/> (if drinkable) and takes it from the pack.</summary>
    public static void DrinkFromPack(GameState s, int member, int index)
    {
        if (!CanDrink(s, s.Party.Slot(member).Pack[index].Code))
        {
            return;
        }
        Drink(s, member, index);
        Inventory.TakeAt(s, member, index);
    }

    /// <summary>The effect of the potion in stack <paramref name="index"/>; the potion stays in the pack.</summary>
    public static void Drink(GameState s, int member, int index)
    {
        var m = s.Party.Slot(member);
        var stack = m.Pack[index];
        var q = stack.Quality;
        var r = s.Random;
        int Band(int low, int middle, int high, int best) => q <= 25 ? low : q <= 35 ? middle : q <= 45 ? high : best;
        int ThreeBands(int low, int middle, int high) => q <= 25 ? low : q <= 35 ? middle : high;
        int Raise(int lowBase, int lowRange, int midBase, int midRange, int highBase, int highRange) =>
            q <= 25 ? r.Below(lowRange) + lowBase : q <= 35 ? r.Below(midRange) + midBase : q <= 45 ? r.Below(highRange) + highBase : 5;
        // C: record +0x34 is an unsigned char: the nibble sums are stored back as a byte (wrapping), the low nibble may carry into the high one.
        // C: each rand_mod is drawn only in the band taken, before raise_attr_temp draws its own rnd(3) for the event's hours.
        int hi = m.WeaponPotions >> 4, lo = m.WeaponPotions & 0xF;
        int t;
        switch (stack.Code & 0xFFF)
        {
            case DeadlyBlade:
                t = ThreeBands(3, 4, 5);
                MemberRules.RecordPotionBonus(s, member, DeadlyBlade, t, 24);
                m.WeaponPotions = (byte)(((t + hi) << 4) + lo);
                break;
            // BUG: Strongedge and Great Power record the pack index as the bonus event's type (Deadly Blade records the potion code).
            case Strongedge:
                t = ThreeBands(3, 4, 5);
                MemberRules.RecordPotionBonus(s, member, index, t, 24);
                m.WeaponPotions = (byte)((hi << 4) + t + lo);
                break;
            case GreatPower:
                t = ThreeBands(14, 22, 30);
                MemberRules.RecordPotionBonus(s, member, index, t, 24);
                m.WeaponPotions = (byte)(m.WeaponPotions + t);
                break;
            case Trueflight:
                t = Band(14, 22, 30, 5);
                MemberRules.RaiseSkillTemporarily(s, member, (int)Skill.Bows, t, 24);
                MemberRules.RaiseSkillTemporarily(s, member, (int)Skill.ThrownWeapons, t, 24);
                MemberRules.RaiseSkillTemporarily(s, member, (int)Skill.MissileDevices, t, 24);
                break;
            case Hardarmor:
                t = Band(1, 2, 3, 5);
                m.ArmourPotions = (byte)((m.ArmourPotions >> 4 << 4) + t + (m.ArmourPotions & 0xF));
                break;
            // C: party_money++ increments the florin word alone (16-bit, no normalisation).
            // UNCERTAIN: what Transformation is meant to do; the code adds one florin.
            case Transformation:
                s.Party.Purse = s.Party.Purse with { Florins = (short)(s.Party.Purse.Florins + 1) };
                break;
            case Truesight:
                MemberRules.RaiseAttributeTemporarily(s, member, (int)Attribute.Perception, Raise(9, 6, 15, 7, 22, 8), 24);
                break;
            case NewWind:
                MemberRules.RaiseAttributeTemporarily(s, member, (int)Attribute.Endurance, Raise(6, 6, 8, 6, 10, 6), 24);
                break;
            case Ironarm:
                MemberRules.RaiseAttributeTemporarily(s, member, (int)Attribute.Strength, Raise(6, 6, 8, 6, 10, 6), 24);
                break;
            case Quickmove:
                MemberRules.RaiseAttributeTemporarily(s, member, (int)Attribute.Agility, Raise(6, 6, 8, 8, 10, 8), 24);
                break;
            case EssenceOfGrace:
                t = Raise(9, 6, 15, 7, 22, 8);
                MemberRules.HealAttribute(s, member, (int)Attribute.Strength, q * 2);
                MemberRules.RaiseAttributeTemporarily(s, member, (int)Attribute.Endurance, t, 34);
                break;
            case Firewall:
                t = ThreeBands(50, 70, 90);
                m.ArmourPotions = (byte)((((m.ArmourPotions >> 4) + 1) << 4) + (m.ArmourPotions & 0xF));
                m.FirewallStrength = t;
                break;
        }
    }
}
