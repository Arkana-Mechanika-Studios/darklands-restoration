using Darklands.Core.Characters;
using Darklands.Core.Items;
using Darklands.Core.Randomness;

namespace Darklands.Core.Battles;

/// <summary>
/// The enemies a battlefield generator lays out: per level up to 12 kinds of enemy (each built
/// from an enemy type and a quality) and up to 10 groups of them; then the fix-ups the loader
/// applies to every level (colour variants from the location, merged duplicates, group
/// bonuses, the special troops' training, group money).
/// </summary>
// orig: R004 0E76 (enemy buffer DS:DADD: monster-prototype, battlefield-monster-table, battlefield-group-table, monster-total, group-total)
public sealed class BattlefieldForces
{
    public const int MaxLevels = 16;
    public const int MaxKinds = 12;
    public const int MaxGroups = 10;

    private readonly EnemyCatalogue _catalogue;
    private readonly IReadOnlyList<ItemDefinition> _items;
    private readonly CombatTables _tables;
    private readonly IRandomSource _random;

    // orig: monsters-reset R004+0x04CC (0E76:04CC) draft; groups-reset R004+0x1060 (0E76:1060) draft
    public BattlefieldForces(EnemyCatalogue catalogue, IReadOnlyList<ItemDefinition> items, CombatTables tables, IRandomSource random)
    {
        _catalogue = catalogue;
        _items = items;
        _tables = tables;
        _random = random;
        for (var l = 0; l < MaxLevels; l++)
        {
            Kinds[l] = [];
            Templates[l] = [];
            Groups[l] = [];
        }
    }

    /// <summary>Which of the original's bugs are fixed.</summary>
    public OriginalBehaviour Behaviour { get; set; } = OriginalBehaviour.Faithful;

    /// <summary>battlefield-monster-table / monster-total: the kinds of each level.</summary>
    public List<EnemyKind>[] Kinds { get; } = new List<EnemyKind>[MaxLevels];

    /// <summary>monster-prototype: the template of each kind (the variant chosen).</summary>
    public List<int>[] Templates { get; } = new List<int>[MaxLevels];

    /// <summary>battlefield-group-table / group-total: the groups of each level.</summary>
    public List<EnemyGroup>[] Groups { get; } = new List<EnemyGroup>[MaxLevels];

    /// <summary><c>dice-roll</c>: n dice of d sides; d = 1 gives n without a draw; nothing below 1.</summary>
    // orig: dice-roll R004+0x20F6 (0E76:20F6) draft (byte-matched per the draft notes)
    public int RollDice(int n, int d)
    {
        if (d < 1 || n < 1)
        {
            return 0;
        }
        if (d == 1)
        {
            return n;
        }
        var sum = 0;
        for (var i = 0; i < n; i++)
        {
            // C: rand() % d + 1 with plain rand() (not roll-below: no "below 2 counts as 2").
            sum += _random.NextRaw() % d + 1;
        }
        return sum;
    }

    /// <summary>
    /// Adds a kind of enemy type <paramref name="type"/> (at most variant <paramref name="quality"/>)
    /// to level <paramref name="level"/>, or finds the same kind already there; returns its index
    /// (−1 for an unknown type).
    /// </summary>
    // orig: monster-type-lookup R004+0x06AA (0E76:06AA) draft; checked against draft R004+0x06AA..0x0AB6 (type walk, variant, palettes, weapon choice, weapon item); tests: Oracle:enemies.json
    public int AddKind(int level, int type, int quality)
    {
        if (type < 0 || type >= 0x52)
        {
            return -1;
        }
        // An enemy type without weapon bits borrows the bits of the type before it.
        var typeIndex = type;
        var enemyType = _catalogue.Types[typeIndex];
        if ((enemyType.Flags & 0x3F) == 0)
        {
            while (typeIndex > 0)
            {
                // C: the walk compares far pointers: it stops at the first type (index 0) without testing it.
                if ((_catalogue.Types[typeIndex].Flags & 0x3F) != 0)
                {
                    break;
                }
                typeIndex--;
            }
            enemyType = _catalogue.Types[typeIndex];
        }
        var first = (int)enemyType.Template;
        while (_catalogue.Templates[first].VariantCount < 0)
        {
            if (first <= 0)
            {
                break;
            }
            first--;
        }
        var variants = Math.Max(Math.Min((int)_catalogue.Templates[first].VariantCount, quality), 1);
        var chosen = first + variants - 1;
        var kinds = Kinds[level];
        var templates = Templates[level];
        for (var k = 0; k < kinds.Count; k++)
        {
            if (templates[k] == chosen)
            {
                return k;
            }
        }
        var template = _catalogue.Templates[chosen];
        var kind = new EnemyKind
        {
            // C: (first & 0xFF) << 8 (only the low byte of the template index goes into the high byte).
            Identity = (ushort)((first & 0xFF) << 8),
            Appearance = (ushort)((first & 0xFF) << 8),
            LevelMask = (ushort)(1 << level),
            BaseLoad = template.Load,
        };
        var scheme = RollDice(1, template.ColourSchemes);
        if (type == 0x4D)
        {
            scheme = 3;
        }
        var palette = template.FirstPalette + (scheme - 1) * template.PalettesPerScheme;
        for (var k = 0; k < template.PalettesPerScheme; k++)
        {
            // C: (char)palette + (char)k, stored in a char (wraps).
            kind.Palettes[k] = (sbyte)(palette + k);
        }
        kind.Image = template.Image;
        kind.Name = enemyType.Name;
        kind.Byte26 = template.Byte13;
        kind.Byte27 = template.Byte12;
        // Which weapons the type may carry, preferring ones the same kind does not already use on this level.
        int weaponCount;
        byte allowed = 0, preferred = 0;
        for (weaponCount = 0; weaponCount < 6 && template.WeaponTypes[weaponCount] >= 0; weaponCount++)
        {
            if ((enemyType.Flags & (1 << weaponCount)) == 0)
            {
                continue;
            }
            allowed |= (byte)(1 << weaponCount);
            for (var k = 0; k < kinds.Count; k++)
            {
                var other = templates[k];
                if (other >= first && other <= _catalogue.Templates[first].VariantCount + first - 1
                    && kinds[k].WeaponType != template.WeaponTypes[weaponCount])
                {
                    preferred |= (byte)(1 << weaponCount);
                }
            }
        }
        if (preferred == 0)
        {
            preferred = allowed;
        }
        var weapon = 0;
        if (allowed != 0)
        {
            do
            {
                weapon = RollDice(1, weaponCount) - 1;
            }
            while ((preferred & (1 << weapon)) == 0);
        }
        kind.WeaponType = template.WeaponTypes[weapon];
        if (enemyType.CastsSpells)
        {
            kind.Identity |= 0x84;
            kind.Class = 2;
            kind.MissileFlag = 0x80;
            kind.Byte27 = 2;
        }
        else if (enemyType.UsesMissiles)
        {
            kind.Identity |= (byte)template.MissileType;
            kind.Class = 1;
            kind.MissileFlag = 0x80;
            kind.Byte27 = 2;
        }
        else
        {
            kind.Identity |= (byte)kind.WeaponType;
            kind.Class = 0;
        }
        kind.Appearance |= (byte)kind.WeaponType;
        kind.WeaponQuality = template.WeaponQuality;
        AddDrop(kind, template.WeaponDrop, kind.WeaponType, template.WeaponItems[weapon], template.WeaponQuality);
        if (kind.Class == 1)
        {
            kind.MissileType = template.MissileType;
            kind.MissileQuality = template.MissileQuality;
            AddDrop(kind, template.MissileDrop, kind.MissileType, template.MissileItem, template.MissileQuality);
        }
        if (template.AmmunitionType >= 0 && template.AmmunitionItem >= 0)
        {
            var weight = WeightOf(template.AmmunitionItem) * template.AmmunitionCount;
            kind.Items.Add(new ItemStack(template.AmmunitionItem, template.AmmunitionType, 25, template.AmmunitionCount, (byte)weight));
            // C: the weight byte (weight × count, truncated to 8 bits) is added to the load byte.
            kind.Load = (byte)(kind.Load + (byte)weight);
        }
        for (var piece = 0; piece < 2; piece++)
        {
            kind.ArmourTypes[piece] = template.ArmourTypes[piece];
            kind.ArmourQualities[piece] = template.ArmourQuality;
            var (dropType, dropQuality) = template.ArmourDrops[piece];
            if (dropType == -1)
            {
                continue;
            }
            int itemType = dropType >= 0 ? dropType : kind.ArmourTypes[piece];
            if (itemType < 0)
            {
                continue;
            }
            int code = template.ArmourItems[piece];
            var quality2 = dropQuality >= 0 ? dropQuality : template.ArmourQuality;
            // Only real armour weighs: vital types up to 0x4B, limb types up to 0x54.
            var weighs = (piece == 0 && (byte)itemType <= 0x4B) || (piece == 1 && (byte)itemType <= 0x54);
            var weight = weighs ? WeightOf(code) : 0;
            kind.Items.Add(new ItemStack(code, (byte)itemType, quality2, 1, weight));
            if (weighs)
            {
                kind.Load = (byte)(kind.Load + weight);
            }
        }
        kind.ShieldType = template.ShieldType;
        kind.ShieldQuality = template.ShieldQuality;
        if (template.ShieldDrop.Type != -1 && template.ShieldQuality != -1)
        {
            AddDrop(kind, template.ShieldDrop, kind.ShieldType, template.ShieldItem, template.ShieldQuality);
        }
        for (var k = 0; k < 7; k++)
        {
            kind.Maximums[k] = template.Attributes[k];
            kind.Attributes[k] = template.Attributes[k];
        }
        template.Skills.CopyTo(kind.Skills, 0);
        kind.Byte57 = template.ByteB9;
        for (var k = 0; k < 2; k++)
        {
            kind.StrengthBonusDice[k] = template.StrengthBonusDice[k];
            kind.EnduranceBonusDice[k] = template.EnduranceBonusDice[k];
            kind.SkillBonusDice[k] = template.SkillBonusDice[k];
        }
        kind.EffectImmunity = template.EffectImmunity;
        for (var k = 0; k < 10; k++)
        {
            var dice = template.ComponentDice[k];
            var n = RollDice(1, dice.Sides) + dice.Add;
            if (n > 0)
            {
                kind.Components[k] = (sbyte)n;
                kind.ComponentQualities[k] = dice.Quality;
            }
        }
        for (var k = 0; kind.Items.Count < EnemyKind.MaxItems; k++)
        {
            if (k >= 0x16)
            {
                break;
            }
            var dice = template.PotionDice[k];
            var n = RollDice(1, dice.Sides) + dice.Add;
            if (n > 0)
            {
                // Potion k: item code 0x5F + k, item type (k − 0x7C) as a byte.
                var code = k + Potions.FirstPotion;
                var weight = WeightOf(code);
                kind.Items.Add(new ItemStack(code, (byte)(k - 0x7C), dice.Quality, (byte)n, weight));
                // C: (char)n * weight added to the load byte.
                kind.Load = (byte)(kind.Load + (sbyte)n * weight);
            }
        }
        if (first == 0x3D)
        {
            // The kind with template 0x3D always carries item 0x73 (type 0x98, quality 40).
            var weight = WeightOf(0x73);
            kind.Items.Add(new ItemStack(0x73, 0x98, 0x28, 1, weight));
            kind.Load = (byte)(kind.Load + weight);
        }
        kinds.Add(kind);
        templates.Add(chosen);
        return kinds.Count - 1;
    }

    /// <summary>Adds the item a kind leaves for a piece of equipment: the drop type (−1 none, below 0 the worn type) and quality (below 0 the worn quality).</summary>
    private void AddDrop(EnemyKind kind, (sbyte Type, sbyte Quality) drop, int wornType, int code, int wornQuality)
    {
        if (drop.Type == -1)
        {
            return;
        }
        var itemType = drop.Type >= 0 ? drop.Type : wornType;
        if (itemType < 0)
        {
            return;
        }
        var weight = WeightOf(code);
        kind.Items.Add(new ItemStack(code, (byte)itemType, drop.Quality >= 0 ? drop.Quality : wornQuality, 1, weight));
        kind.Load = (byte)(kind.Load + weight);
    }

    /// <summary>Weight of one item (DARKLAND.LST +0x25).</summary>
    // DIVERGENCE: an item code outside the list (−1 from an empty template entry) reads the bytes before the list in the
    // original; it weighs 0 here.
    private int WeightOf(int code) => code >= 0 && code < _items.Count ? _items[code].Weight : 0;

    /// <summary>Adds a group to a level and returns it.</summary>
    public EnemyGroup AddGroup(int level)
    {
        var group = new EnemyGroup();
        Groups[level].Add(group);
        return group;
    }

    /// <summary>
    /// The loader's pass over the kinds of every level: colour schemes from the location's seed,
    /// missile users demoted when mixed with others, duplicates merged, group bonuses, special
    /// troops' training, and the levels each kind appears on.
    /// </summary>
    /// <param name="variantSeed">rand() after srand(location-now): the same location always gives the same colours.</param>
    // orig: monsters-repair R004+0x122C (0E76:122C) draft (163 differences in the
    // draft's byte comparison: jump-table addresses and register choice); tests: Oracle:enemies.json (120 chained scenarios)
    public void FixKinds(int levels, int variantSeed)
    {
        for (var level = 0; level < levels; level++)
        {
            var kinds = Kinds[level];
            var templates = Templates[level];
            var groups = Groups[level];
            var firstTemplate = new int[MaxKinds];
            Array.Fill(firstTemplate, -1);
            var members = new int[MaxKinds];
            var total = new int[0x47];
            var distinct = 0;
            for (var k = 0; k < kinds.Count; k++)
            {
                var template = _catalogue.Templates[templates[k]];
                // C: an int modulo the template's scheme count; a count of 0 would fault.
                var scheme = variantSeed % template.ColourSchemes + 1;
                var palette = template.FirstPalette + (scheme - 1) * template.PalettesPerScheme;
                for (var j = 0; j < template.PalettesPerScheme; j++)
                {
                    kinds[k].Palettes[j] = (sbyte)(j + palette);
                }
            }
            foreach (var g in groups)
            {
                for (var j = 0; j < 3; j++)
                {
                    int kind = g.Kinds[j];
                    if (kind >= 0 && kind < kinds.Count)
                    {
                        members[kind] += g.Counts[j] < 0 ? 0 : g.Counts[j];
                    }
                }
            }
            for (var k = 0; k < kinds.Count; k++)
            {
                var t = templates[k];
                while (t > 0 && _catalogue.Templates[t].VariantCount < 0)
                {
                    t--;
                }
                var seen = false;
                for (var x = 0; x < k; x++)
                {
                    if (firstTemplate[x] == t)
                    {
                        seen = true;
                        break;
                    }
                }
                if (!seen)
                {
                    distinct++;
                }
                firstTemplate[k] = t;
                total[t] += members[k];
            }
            var anyMissile = false;
            for (var k = 0; k < kinds.Count; k++)
            {
                var kind = kinds[k];
                if (kind.Class is not (1 or 2))
                {
                    continue;
                }
                anyMissile = true;
                if (distinct <= 1 || total[firstTemplate[k]] <= 1)
                {
                    continue;
                }
                switch (firstTemplate[k])
                {
                    case 0x15 or 0x16 or 0x19 or 0x1A or 0x1B or 0x1C or 0x1D or 0x23 or 0x2D or 0x33 or 0x35:
                        // These troops keep their missiles, but at most one of them per group.
                        foreach (var g in groups)
                        {
                            for (var j = 0; j < 3; j++)
                            {
                                if (g.Kinds[j] == k)
                                {
                                    g.Counts[j] = g.Counts[j] > 1 ? (sbyte)1 : g.Counts[j];
                                }
                            }
                        }
                        break;
                    default:
                        DemoteToMelee(kind, templates[k]);
                        break;
                }
            }
            MergeDuplicates(kinds, firstTemplate, anyMissile);
            ApplyGroupBonuses(kinds, templates, groups);
            TrainSpecialTroops(kinds, templates);
        }
        for (var level = 0; level < levels; level++)
        {
            foreach (var kind in Kinds[level])
            {
                for (var other = 0; other < levels; other++)
                {
                    foreach (var k2 in Kinds[other])
                    {
                        if (k2.Identity == kind.Identity && k2.Appearance == kind.Appearance)
                        {
                            kind.LevelMask |= (ushort)(1 << other);
                        }
                    }
                }
            }
        }
    }

    private void DemoteToMelee(EnemyKind kind, int template)
    {
        kind.Class = 0;
        kind.MissileFlag = 0;
        kind.Byte27 = _catalogue.Templates[template].Byte12;
        kind.Identity = (ushort)((kind.Identity & 0xFF00) | (byte)kind.WeaponType);
    }

    /// <summary>A kind whose template family appeared before takes over the earlier kind's weapon and colours.</summary>
    private static void MergeDuplicates(List<EnemyKind> kinds, int[] firstTemplate, bool anyMissile)
    {
        var remaining = kinds.Count;
        for (var k = 0; k < kinds.Count; k++)
        {
            for (var x = 0; x < k; x++)
            {
                if (firstTemplate[x] != firstTemplate[k])
                {
                    continue;
                }
                var kind = kinds[k];
                var earlier = kinds[x];
                if (remaining > 2 || anyMissile)
                {
                    // The first item that was its weapon becomes the earlier kind's weapon.
                    for (var j = 0; j < kind.Items.Count; j++)
                    {
                        // C: the item's type byte is unsigned, the weapon type a signed char (0xFF never equals −1).
                        if ((kind.Items[j].Type & 0xFF) == kind.WeaponType)
                        {
                            kind.Items[j] = kind.Items[j] with { Type = (byte)earlier.WeaponType, Quality = earlier.WeaponQuality };
                            break;
                        }
                    }
                    kind.WeaponType = earlier.WeaponType;
                    kind.Identity = earlier.Identity;
                    kind.Appearance = earlier.Appearance;
                    kind.Class = earlier.Class;
                    kind.MissileFlag = earlier.MissileFlag;
                    remaining--;
                }
                for (var j = 0; j < 3; j++)
                {
                    kind.Palettes[j] = earlier.Palettes[j];
                }
                break;
            }
        }
    }

    /// <summary>A group's leaders raise the strength, endurance and hand-weapon skills of every kind on the level by the best roll.</summary>
    private void ApplyGroupBonuses(List<EnemyKind> kinds, List<int> templates, List<EnemyGroup> groups)
    {
        int strength = 0, endurance = 0, skill = 0;
        foreach (var g in groups)
        {
            for (var j = 0; j < 3; j++)
            {
                int k = g.Kinds[j];
                if (k < 0 || k >= kinds.Count)
                {
                    continue;
                }
                var t = _catalogue.Templates[templates[k]];
                if (t.StrengthBonusDice[0] <= 0 && t.StrengthBonusDice[1] <= 0 && t.EnduranceBonusDice[0] <= 0
                    && t.EnduranceBonusDice[1] <= 0 && t.SkillBonusDice[0] <= 0 && t.SkillBonusDice[1] <= 0)
                {
                    continue;
                }
                for (var n = 0; n < g.Counts[j]; n++)
                {
                    strength = Math.Max(RollDice(1, t.StrengthBonusDice[0]) + t.StrengthBonusDice[1], strength);
                    endurance = Math.Max(RollDice(1, t.EnduranceBonusDice[0]) + t.EnduranceBonusDice[1], endurance);
                    skill = Math.Max(RollDice(1, t.SkillBonusDice[0]) + t.SkillBonusDice[1], skill);
                }
            }
        }
        foreach (var kind in kinds)
        {
            // C: CL(a) caps at 0x7F only; the sum is stored in a char. Only the current values change.
            kind.Attributes[1] = (sbyte)Math.Min(kind.Attributes[1] + strength, 0x7F);
            kind.Attributes[0] = (sbyte)Math.Min(kind.Attributes[0] + endurance, 0x7F);
            for (var s = 0; s < 4; s++)
            {
                kind.Skills[s] = (sbyte)Math.Min(kind.Skills[s] + skill, 0x7F);
            }
        }
    }

    /// <summary>The special troops (templates 0x15, 0x16, 0x1B..0x1D, 0x2D, 0x35) train every kind on their level.</summary>
    private void TrainSpecialTroops(List<EnemyKind> kinds, List<int> templates)
    {
        foreach (var t in templates.ToList())
        {
            int missileChance, missileSkill, qualityBonus, damageBonus, penetrationChance, penetrationBonus, damageChance,
                qualityChance, protectionChance, protection, armourBonus;
            switch (t)
            {
                case 0x15:
                    (missileChance, missileSkill, qualityBonus, damageBonus, penetrationChance) = (3, 0x1E, 0x16, 4, 1);
                    (penetrationBonus, damageChance, qualityChance) = (2, 2, 2);
                    (armourBonus, protection, protectionChance) = (0, 0, 0);
                    break;
                case 0x16:
                    (missileChance, missileSkill, qualityBonus, damageBonus) = (4, 0x28, 0x1E, 5);
                    (penetrationBonus, damageChance, qualityChance) = (3, 3, 3);
                    (protectionChance, penetrationChance) = (2, 2);
                    protection = 1;
                    armourBonus = 0xB;
                    break;
                case 0x1B:
                    (missileSkill, qualityBonus, damageChance, qualityChance, missileChance, damageBonus) = (0x14, 0xE, 1, 1, 1, 3);
                    (penetrationBonus, penetrationChance) = (0, 0);
                    (armourBonus, protection, protectionChance) = (0, 0, 0);
                    break;
                case 0x1C:
                    (missileSkill, qualityBonus, damageChance, qualityChance, missileChance, damageBonus) = (0x14, 0xE, 2, 2, 2, 3);
                    (penetrationBonus, penetrationChance) = (0, 0);
                    (armourBonus, protection, protectionChance) = (0, 0, 0);
                    break;
                case 0x1D:
                    (missileSkill, qualityBonus) = (0x14, 0xE);
                    (damageBonus, damageChance, qualityChance, missileChance) = (3, 3, 3, 3);
                    (penetrationBonus, penetrationChance) = (0, 0);
                    (armourBonus, protection, protectionChance) = (0, 0, 0);
                    break;
                case 0x2D or 0x35:
                    (qualityBonus, qualityChance, missileSkill, missileChance) = (0, 0, 0, 0);
                    (protectionChance, penetrationChance, damageChance) = (2, 2, 2);
                    (protection, penetrationBonus, damageBonus) = (1, 1, 1);
                    armourBonus = 9;
                    break;
                default:
                    continue;
            }
            foreach (var kind in kinds)
            {
                if (kind.Class == 1 && RollDice(1, 5) <= missileChance)
                {
                    // C: char additions without a cap.
                    for (var s = 4; s <= 6; s++)
                    {
                        kind.Skills[s] = (sbyte)(kind.Skills[s] + missileSkill);
                    }
                }
                // C: the weapon type indexes the category table without a check (−1 reads the byte before it).
                switch (_tables.SkillCategory(kind.WeaponType))
                {
                    case 1 or 2 or 6:
                        if (RollDice(1, 5) <= qualityChance)
                        {
                            if (_tables.SkillCategory(kind.WeaponType) == 6)
                            {
                                kind.MissileQuality = (sbyte)(kind.MissileQuality + qualityBonus);
                            }
                            else
                            {
                                kind.WeaponQuality = (sbyte)(kind.WeaponQuality + qualityBonus);
                            }
                        }
                        break;
                    case 0 or 3:
                        if (RollDice(1, 5) <= penetrationChance)
                        {
                            kind.WeaponBonus |= (byte)(penetrationBonus & 0x0F);
                        }
                        // C: case 0/3 falls through into the default arm.
                        if (RollDice(1, 5) <= damageChance)
                        {
                            kind.WeaponBonus |= (byte)(damageBonus << 4);
                        }
                        break;
                    default:
                        if (RollDice(1, 5) <= damageChance)
                        {
                            kind.WeaponBonus |= (byte)(damageBonus << 4);
                        }
                        break;
                }
                if (RollDice(1, 5) <= protectionChance)
                {
                    kind.Protection = (byte)protection;
                    kind.ArmourQualities[0] = (sbyte)(kind.ArmourQualities[0] + armourBonus);
                    kind.ArmourQualities[1] = (sbyte)(kind.ArmourQualities[1] + armourBonus);
                }
            }
        }
    }

    /// <summary>
    /// The loader's pass over the groups: empty entries and groups dropped, leaders that shoot or
    /// cast made to fight hand to hand (a casting leader is no leader), group money rolled.
    /// </summary>
    // orig: groups-repair R004+0x1D90 (0E76:1D90) draft (2 differences in the draft's byte comparison); tests: Oracle:enemies.json
    public void FixGroups(int levels)
    {
        for (var level = 0; level < levels; level++)
        {
            var kinds = Kinds[level];
            var groups = Groups[level];
            var used = new int[groups.Count];
            for (var k = 0; k < groups.Count; k++)
            {
                var g = groups[k];
                for (var j = 0; j < 3; j++)
                {
                    if (g.Kinds[j] >= 0 && g.Kinds[j] < kinds.Count && g.Counts[j] > 0)
                    {
                        used[k]++;
                    }
                    else
                    {
                        g.Kinds[j] = -1;
                        g.Counts[j] = 0;
                    }
                }
            }
            // BUG: the original removes an empty group by shifting the later groups down but keeps testing the same
            // (stale, zero) counter, so every group after the first empty one is removed too.
            var firstEmpty = Array.FindIndex(used, n => n == 0);
            if (Behaviour.FixEnemyGroupCompaction)
            {
                for (var k = used.Length - 1; k >= 0; k--)
                {
                    if (used[k] == 0)
                    {
                        groups.RemoveAt(k);
                    }
                }
            }
            else if (firstEmpty >= 0)
            {
                groups.RemoveRange(firstEmpty, groups.Count - firstEmpty);
            }
            foreach (var g in groups)
            {
                if (g.HasReinforcements == 0)
                {
                    continue;
                }
                int leader = g.ReinforcementKind;
                if (leader < 0 || leader >= kinds.Count || kinds[leader].Class == 2)
                {
                    g.HasReinforcements = 0;
                    continue;
                }
                if (kinds[leader].Class == 1)
                {
                    DemoteToMelee(kinds[leader], Templates[level][leader]);
                }
            }
            foreach (var g in groups)
            {
                g.Florins = 0;
                g.Groschen = 0;
                g.Pfennigs = 0;
                for (var j = 0; j < 3; j++)
                {
                    if (g.Kinds[j] < 0)
                    {
                        continue;
                    }
                    var t = _catalogue.Templates[Templates[level][g.Kinds[j]]];
                    for (var n = 0; n < g.Counts[j]; n++)
                    {
                        var v = RollDice(1, t.MoneyDiceSides[0]) + t.MoneyDiceAdd[0];
                        if (v > 0)
                        {
                            g.Florins = (short)(g.Florins + v);
                        }
                        v = RollDice(1, t.MoneyDiceSides[1]) + t.MoneyDiceAdd[1];
                        if (v > 0)
                        {
                            g.Groschen = (short)(g.Groschen + v);
                        }
                        v = RollDice(1, t.MoneyDiceSides[2]) + t.MoneyDiceAdd[2];
                        if (v > 0)
                        {
                            g.Pfennigs = (short)(g.Pfennigs + v);
                        }
                    }
                }
            }
        }
    }
}
