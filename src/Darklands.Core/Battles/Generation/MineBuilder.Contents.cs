namespace Darklands.Core.Battles.Generation;

/// <summary>An enemy group a section's table asks for: up to two kinds (type, quality, count) and their reinforcements.</summary>
/// <param name="Type1">Enemy type of the first kind (−1 none).</param>
/// <param name="Quality1">Quality (variant) of the first kind.</param>
/// <param name="Count1">How many of the first kind.</param>
/// <param name="Reinforcement1">Reinforcements of the first kind: batch size + 10 × pool (0 none).</param>
/// <param name="Leader1">Kill byte (+0x0C) of the first kind's record.</param>
/// <param name="Type2">Enemy type of the second kind (−1 none).</param>
/// <param name="Quality2">Quality of the second kind.</param>
/// <param name="Count2">How many of the second kind.</param>
/// <param name="Reinforcement2">Reinforcements of the second kind.</param>
/// <param name="Leader2">Kill byte (+0x0C) of the second kind's record.</param>
/// <param name="Posted">The group is posted: wide spread and (in mines that are not fixed layout) its position stored negated.</param>
internal readonly record struct MineGroupPlan(int Type1, int Quality1, int Count1, int Reinforcement1, int Leader1,
    int Type2, int Quality2, int Count2, int Reinforcement2, int Leader2, bool Posted);

/// <summary>What the sections hold: enemy groups, chest and pile contents, loot and decoration tables.</summary>
internal sealed partial class MineBuilder
{
    /// <summary>
    /// The enemy group of room kind <paramref name="k"/> (0..5 the six groups of a section; 10 the guards of a carved passage) of a
    /// section of a mine type.
    /// </summary>
    // orig: R009+0x5200 (182C:1260) draft (unmatched draft); tests: Oracle:mine.json
    // C: arms that assign a kind twice keep the last; `R(*c = 1, 2)` stores the 1 before the draw and the draw's result after it; case 0 of
    // 0x0b section 13 falls into case 1 (the reinforcement 0x4F7 = batch 7, pool 127).
    private MineGroupPlan GroupPlanFor(int t, int section, int k)
    {
        int a = -1, b = 1, c = 0, d = 0, f = -1, g = 1, h = 0, i = 0, e = 0, j = 0, l = 0;
        switch (t)
        {
            case 0x0A:
                break;
            case 0x0B or 0x82:
                switch (section)
                {
                    case 1:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x3A, Roll(2) + 1);
                                break;
                            case 1 or 2:
                                (a, c) = (0x3A, Roll(2));
                                break;
                            case 3:
                                (a, c) = (0x3A, 1);
                                break;
                        }
                        break;
                    case 2:
                        switch (k)
                        {
                            case 0 or 1:
                                (a, c) = (0x3B, Roll(2));
                                break;
                            case 2 or 3:
                                (a, c) = (0x3B, 1);
                                break;
                        }
                        break;
                    case 3 or 5:
                        switch (k)
                        {
                            case 0:
                                (a, f, c, b) = (0x2C, 0x30, 1, 2);
                                h = Roll(2) + 1;
                                break;
                            case 1:
                                (a, f, c) = (0x2C, 0x30, 1);
                                h = Roll(2) + 1;
                                break;
                            case 2 or 3:
                                (a, c) = (0x30, Roll(2) + 1);
                                break;
                            case 10 when section == 3:
                                (a, f, c) = (0x2E, 0x30, 1);
                                h = Roll(2) + 2;
                                l = 1;
                                break;
                        }
                        break;
                    case 4:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x39, Roll(2));
                                break;
                            case 1 or 2 or 3:
                                (a, c) = (0x39, 1);
                                break;
                        }
                        break;
                    case 6:
                        switch (k)
                        {
                            case 0:
                                (f, a, c, b) = (0x2C, 0x2C, 1, 2);
                                h = Roll(2);
                                break;
                            case 1:
                                (a, c) = (0x2C, Roll(2) + 1);
                                break;
                            case 2:
                                (f, h) = (0x30, Roll(3) + 1);
                                break;
                            case 3:
                                (a, c) = (0x2C, 1);
                                break;
                        }
                        break;
                    case 7:
                        switch (k)
                        {
                            case 0:
                                (a, f, h, c) = (0x2C, 0x2E, 1, 1);
                                break;
                            case 1:
                                (a, b, c) = (0x2C, 2, 1);
                                break;
                            case 2 or 3:
                                (a, c) = (0x2C, 1);
                                break;
                        }
                        break;
                    case 8:
                        switch (k)
                        {
                            case 0 or 1:
                                (f, a, c, b) = (0x2C, 0x2C, 2, 2);
                                h = Roll(2) + 1;
                                break;
                            case 2:
                                (a, c) = (0x2C, Roll(3));
                                break;
                            case 3:
                                (a, c) = (0x2C, Roll(2));
                                break;
                        }
                        break;
                    case 9:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x39, Roll(2) + 1);
                                break;
                            case 1 or 2 or 3:
                                (a, c) = (0x39, 1);
                                break;
                        }
                        break;
                    case 10:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x30, Roll(3) + 2);
                                break;
                            case 1:
                                (a, c) = (0x2E, Roll(2));
                                break;
                            case 2 or 3:
                                (a, c) = (0x2E, 1);
                                break;
                            case 10:
                                (a, f, c) = (0x2E, 0x30, 1);
                                h = Roll(2) + 2;
                                l = 1;
                                break;
                        }
                        break;
                    case 11 or 14:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x2E, 1);
                        }
                        break;
                    case 13:
                        if (k is 0 or 1)
                        {
                            // C: case 0 sets kind and count, then falls into case 1, which also sets the reinforcement.
                            (d, a, c) = (0x4F7, 0x2E, 1);
                        }
                        break;
                }
                break;
            case 0x0D or 0x83:
                switch (section)
                {
                    case 1:
                        switch (k)
                        {
                            case 10:
                                (a, b, c, l) = (0x2C, 1, 3, 1);
                                break;
                            case >= 0 and <= 3:
                                (a, c) = (0x3A, Roll(2));
                                break;
                        }
                        break;
                    case 2:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x39, Roll(2));
                        }
                        break;
                    case 3:
                        switch (k)
                        {
                            case 0 or 1:
                                (a, c) = (0x2E, Roll(2));
                                break;
                            case 2 or 3:
                                (a, c) = (0x2E, 1);
                                break;
                        }
                        break;
                    case 4 or 5:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x3D, 1);
                        }
                        break;
                    case 6 or 10:
                        switch (k)
                        {
                            case 0 or 1 or 2:
                                (a, c) = (0x2F, Roll(2));
                                break;
                            case 3:
                                (a, c) = (0x2F, 1);
                                break;
                        }
                        break;
                    case 7:
                        switch (k)
                        {
                            case 10:
                                (a, c, l) = (0x2C, 3, 1);
                                break;
                            case 0 or 1:
                                (a, c) = (0x3B, Roll(2));
                                break;
                            case 2 or 3:
                                (a, c) = (0x3B, 1);
                                break;
                        }
                        break;
                    case 8:
                        switch (k)
                        {
                            case 0 or 1:
                                (f, a, c, b) = (0x2C, 0x2C, 1, 2);
                                h = Roll(2) + 1;
                                break;
                            case 2:
                                (a, c) = (0x2C, Roll(2) + 1);
                                break;
                            case 3:
                                (b, f, a, h, c) = (2, 0x2C, 0x2C, 1, 1);
                                break;
                        }
                        break;
                    case 11:
                        switch (k)
                        {
                            case 0 or 3:
                                (a, c) = (0x2E, Roll(2));
                                break;
                            case 1 or 2:
                                (a, c) = (0x3B, Roll(2));
                                break;
                            case 10:
                                (a, c, l) = (0x30, 8, 1);
                                break;
                        }
                        break;
                    case 12:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x30, Roll(2) + 6);
                        }
                        break;
                }
                break;
            case 0x0E or 0x84 or 0x0F or 0x85 or 0x10 or 0x86:
                (a, b, c, d, e, f, g, h, i, j, l) = DeepMinePlan(t, section, k);
                break;
            case 0x6C:
                switch (k)
                {
                    case >= 0 and <= 4:
                        (a, c) = (0x2C, 1);
                        break;
                    case 5:
                        (a, c, e) = (0x33, 1, 1);
                        break;
                }
                break;
            case 0x6D:
                switch (k)
                {
                    case 0:
                        (a, c, d) = (0x30, 7, 0xDD);
                        break;
                    case 1:
                        (a, b, c, e) = (0x19, 2, 1, 1);
                        break;
                }
                break;
        }
        return new MineGroupPlan(a, b, c, d, e, f, g, h, i, j, l != 0);
    }

    /// <summary>The group tables of mines 0x0e, 0x0f and 0x10 (and 0x84..0x86) as (a, b, c, d, e, f, g, h, i, j, l) of the original.</summary>
    private (int, int, int, int, int, int, int, int, int, int, int) DeepMinePlan(int t, int section, int k)
    {
        int a = -1, b = 1, c = 0, d = 0, f = -1, g = 1, h = 0, i = 0, e = 0, j = 0, l = 0;
        var kind = t switch { 0x0E or 0x84 => 0, 0x0F or 0x85 => 1, _ => 2 };
        if (section == 1)
        {
            // The first section is the same in all three mines.
            switch (k)
            {
                case 0:
                    (a, c) = (0x2E, Roll(2));
                    break;
                case 1:
                    c = 1;
                    (a, c) = (0x30, Roll(2));
                    break;
                case 2:
                    (a, c) = (0x30, Roll(2) + 2);
                    break;
                case 3:
                    (a, c) = (0x30, Roll(2) + 1);
                    break;
            }
            return (a, b, c, d, e, f, g, h, i, j, l);
        }
        switch (kind)
        {
            case 0:
                switch (section)
                {
                    case 2 or 3:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x3D, 1);
                        }
                        break;
                    case 4:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x2E, Roll(3));
                                break;
                            case 1 or 2 or 3:
                                (a, c) = (0x2E, Roll(2));
                                break;
                        }
                        break;
                    case 5:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x31, Roll(2) + 1);
                                break;
                            case 1 or 2 or 3:
                                (a, c) = (0x31, Roll(2));
                                break;
                        }
                        break;
                    case 6:
                        switch (k)
                        {
                            case 0 or 1:
                                (a, c) = (0x31, Roll(2));
                                break;
                            case 2 or 3:
                                (a, c) = (0x31, 1);
                                break;
                        }
                        break;
                    case 7:
                        switch (k)
                        {
                            case 0 or 1 or 2:
                                (a, c) = (0x2E, Roll(3));
                                break;
                            case 3:
                                (a, c) = (0x2E, Roll(2));
                                break;
                        }
                        break;
                    case 8:
                        switch (k)
                        {
                            case 0 or 1 or 2:
                                (a, c) = (0x3D, Roll(2));
                                break;
                            case 3:
                                (a, c) = (0x3D, 1);
                                break;
                        }
                        break;
                    case 9 or 10 or 11 or 12:
                        switch (k)
                        {
                            case 0 or 1:
                                (a, c) = (0x31, 2);
                                break;
                            case 2 or 3:
                                (a, c) = (0x31, 1);
                                break;
                        }
                        break;
                    case 13:
                        switch (k)
                        {
                            case >= 0 and <= 3:
                                (a, c) = (0x31, Roll(2));
                                break;
                            case 4:
                                (a, c) = (0x31, Forces.RollDice(2, 2));
                                l = 1;
                                break;
                        }
                        break;
                }
                break;
            case 1:
                switch (section)
                {
                    case 2 or 3 or 4 or 6:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x2F, Roll(2));
                        }
                        break;
                    case 5 or 7 or 9:
                        switch (k)
                        {
                            case 0:
                                (a, f, c) = (0x27, 0x29, 1);
                                h = Roll(3);
                                break;
                            case 1 or 2:
                                (a, f, c) = (0x27, 0x29, 1);
                                h = Roll(2);
                                break;
                            case 3:
                                (a, c) = (0x29, Roll(4));
                                break;
                        }
                        break;
                    case 8 or 10 or 12:
                        switch (k)
                        {
                            case 0 or 1 or 2:
                                (a, c) = (0x2F, Roll(3));
                                break;
                            case 3:
                                (a, c) = (0x2F, Roll(2));
                                break;
                        }
                        break;
                    case 11:
                        switch (k)
                        {
                            case 0:
                                (a, c) = (0x35, Roll(2));
                                break;
                            case 1 or 2 or 3:
                                (a, c) = (0x35, 1);
                                break;
                        }
                        break;
                    case 13:
                        switch (k)
                        {
                            case >= 0 and <= 3:
                                (a, b, c) = (0x35, 2, 1);
                                break;
                            case 4:
                                (a, c, b, l) = (0x35, 2, 2, 1);
                                break;
                        }
                        break;
                }
                break;
            default:
                switch (section)
                {
                    case 2 or 6:
                        switch (k)
                        {
                            case 0 or 1:
                                (a, c) = (0x3B, Roll(2));
                                break;
                            case 2 or 3:
                                (a, c) = (0x3B, 1);
                                break;
                        }
                        break;
                    case 3:
                        if (k is >= 0 and <= 3)
                        {
                            (a, c) = (0x39, Roll(2));
                        }
                        break;
                    case 4:
                        switch (k)
                        {
                            case 0 or 1:
                                (a, c) = (0x2D, Roll(2) + 1);
                                break;
                            case 2 or 3:
                                (a, c) = (0x2D, Roll(2));
                                break;
                        }
                        break;
                    case 5 or 7 or 8 or 9:
                        switch (k)
                        {
                            case 0:
                                b = 2;
                                c = Roll(2);
                                (f, a) = (0x2D, 0x2D);
                                h = Roll(2);
                                break;
                            case 1:
                                (b, f, a, h, c) = (2, 0x2D, 0x2D, 1, 1);
                                break;
                            case 2 or 3:
                                b = 2;
                                c = Roll(2) - 1;
                                (f, a, h) = (0x2D, 0x2D, 1);
                                break;
                        }
                        break;
                    case 10 or 11 or 12:
                        switch (k)
                        {
                            case 0:
                                b = 2;
                                (a, c) = (0x2D, Roll(2) + 1);
                                break;
                            case 1:
                                (a, c, b) = (0x2D, 2, 2);
                                break;
                            case 2 or 3:
                                b = 2;
                                (a, c) = (0x2D, Roll(2));
                                break;
                        }
                        break;
                    case 13:
                        switch (k)
                        {
                            case >= 0 and <= 3:
                                (c, b) = (1, 2);
                                (f, a) = (0x2D, 0x2D);
                                h = Roll(2) - 1;
                                break;
                            case 4:
                                b = 2;
                                c = Roll(2);
                                (f, a) = (0x2D, 0x2D);
                                h = Roll(2);
                                l = 1;
                                break;
                        }
                        break;
                }
                break;
        }
        return (a, b, c, d, e, f, g, h, i, j, l);
    }

    /// <summary>
    /// The contents of a chest or floor pile of a section: item type, quality and a grade; the table has no entry for some
    /// sections, which keep the values passed in.
    /// </summary>
    // orig: R009+0x4954 (182C:09B4) draft (unmatched draft); tests: Oracle:mine.json
    // C: the kind (a, b) is set by every arm, the grade (c, 3) only by the two-kind arms; the one draw 1..4 is made first, always.
    private (int Item, int Quality, int Grade) ChestContents(int t, int section, int item, int quality, int grade)
    {
        var r = Roll(4);
        (int A, int B)? pair = null;
        var withGrade = true;
        switch (t)
        {
            case 0x0B or 0x82:
                switch (section)
                {
                    case 1 or 2:
                        pair = r switch { 1 => (0x18, 0x19), 2 => (0x18, 0x1E), 3 => (0x1D, 0x19), _ => (0x1D, 0x1E) };
                        break;
                    case 3:
                        (pair, withGrade) = r switch
                        {
                            1 => ((0x18, 0x1E), true), 2 => ((0x88, 0x14), false), 3 => ((0x8B, 0x14), false), _ => ((0x1D, 0x1E), true),
                        };
                        break;
                    case 4:
                        pair = r switch { 1 => (0x1F, 0x1E), 2 => (0x1F, 0x28), 3 => (0x1F, 0x1E), _ => (0x1F, 0x28) };
                        break;
                    case 5 or 6 or 7:
                        (pair, withGrade) = r switch
                        {
                            1 => ((0x1F, 0x1E), true), 2 => ((0x88, 0x14), false), 3 => ((0x8B, 0x14), false), _ => ((0x1F, 0x1E), true),
                        };
                        break;
                    case 8:
                        (pair, withGrade) = r switch
                        {
                            1 => ((0x1F, 0x23), true), 2 => ((0x88, 0x1E), false), 3 => ((0x8B, 0x1E), false), _ => ((0x1F, 0x23), true),
                        };
                        break;
                    case 9 or 10:
                        pair = r switch { 1 => (0x1F, 0x19), 2 => (0x1F, 0x2D), 3 => (0x1F, 0x19), _ => (0x1F, 0x28) };
                        break;
                    case 11 or 14:
                        (pair, withGrade) = ((0x8B, 0x14), false);
                        break;
                }
                break;
            case 0x0D or 0x83:
                switch (section)
                {
                    case 1:
                        (pair, withGrade) = r switch { 1 => ((0x18, 0x19), true), 2 or 3 => ((0x1D, 0x23), true), _ => ((0x8C, 0x14), false) };
                        break;
                    case 2:
                        (pair, withGrade) = r switch { 1 => ((0x18, 0x19), true), 2 or 3 => ((0x1D, 0x1E), true), _ => ((0x8B, 0x14), false) };
                        break;
                    case 3 or 6 or 11:
                        (pair, withGrade) = r switch { 1 => ((0x1F, 0x19), true), 2 or 3 => ((0x1F, 0x1E), true), _ => ((0x89, 0x14), false) };
                        break;
                    case 4:
                        (pair, withGrade) = r switch { 1 => ((0x1F, 0x1E), true), 2 or 3 => ((0x1F, 0x23), true), _ => ((0x8C, 0x14), false) };
                        break;
                    case 5 or 10:
                        (pair, withGrade) = r switch { 1 => ((0x1F, 0x19), true), 2 or 3 => ((0x1F, 0x1E), true), _ => ((0x8B, 0x14), false) };
                        break;
                    case 7:
                        (pair, withGrade) = r switch { 1 => ((0x1F, 0x19), true), 2 or 3 => ((0x1F, 0x23), true), _ => ((0x8C, 0x14), false) };
                        break;
                    case 8:
                        (pair, withGrade) = ((0x8B, 0x14), false);
                        break;
                    case 12:
                        (pair, withGrade) = r switch { 1 => ((0x1E, 0x19), true), 2 or 3 => ((0x1E, 0x23), true), _ => ((0x8C, 0x14), false) };
                        break;
                }
                break;
            case 0x0E or 0x84:
                if (section == 1)
                {
                    pair = r switch { 1 => (0x18, 0x19), 2 or 3 => (0x1D, 0x1E), _ => (0x18, 0x1E) };
                }
                else if (section is >= 2 and <= 13)
                {
                    // C: only the kind (a) and the second value (b) are set, no grade.
                    (pair, withGrade) = ((0x8C, r <= 2 ? 0x14 : 0x1E), false);
                }
                break;
            case 0x0F or 0x85:
                if (section == 1)
                {
                    pair = r switch { 1 => (0x18, 0x19), 2 or 3 => (0x1D, 0x1E), _ => (0x18, 0x1E) };
                }
                else if (section is >= 2 and <= 13)
                {
                    (pair, withGrade) = (r switch { 1 => (0x8C, 0x14), 2 => (0x89, 0x14), 3 => (0x8B, 0x14), _ => (0x88, 0x14) }, false);
                }
                break;
            case 0x10 or 0x86:
                switch (section)
                {
                    case 1:
                        pair = r switch { 1 => (0x18, 0x19), 2 or 3 => (0x1D, 0x1E), _ => (0x18, 0x1E) };
                        break;
                    case >= 2 and <= 5:
                        pair = r switch { 1 => (0x18, 0x1E), 2 => (0x18, 0x2D), 3 => (0x1D, 0x1E), _ => (0x1D, 0x2D) };
                        break;
                    case >= 6 and <= 9:
                        pair = r switch { 1 => (0x1F, 0x1E), 2 => (0x1F, 0x2D), 3 => (0x1F, 0x1E), _ => (0x1F, 0x2D) };
                        break;
                    case >= 10 and <= 13:
                        pair = r switch { 1 => (0x1E, 0x1E), 2 => (0x1E, 0x2D), 3 => (0x1E, 0x1E), _ => (0x1E, 0x2D) };
                        break;
                }
                break;
            default:
                pair = r is 2 or 3 ? (0x18, 0x19) : (0x1C, 0x23);
                break;
        }
        if (pair is { } p)
        {
            (item, quality) = p;
            if (withGrade)
            {
                grade = 3;
            }
        }
        return (item, quality, grade);
    }

    /// <summary>Fills a loot pile with up to four entries; returns 0 (dropped), 1 (a pile with loot) or 2 (made a locked door instead).</summary>
    /// <remarks>The first pile of a section (<paramref name="index"/> 0) of a few deep mines is a set piece.</remarks>
    // orig: R009+0x4D56 (182C:0DB6) draft (close per the draft notes: only its jump table differs);
    // checked against asm (dispatch, draw order, the kind list thinned from one past its end); tests: Oracle:mine.json
    // C: the list of ten kinds is thinned with `idx[k] = idx[n]` starting at n = 10, one past its end: the first pick is replaced by a stale
    // stack word, and the kind that should have gone last is never offered again.
    private int FillPile(MineLevel level, PileRecord pile, int t, int section, int index)
    {
        if (index == 0)
        {
            var special = SetPiece(level, pile, t, section);
            if (special != 0)
            {
                return special;
            }
        }
        if (!_specialVariant && Roll(10) > 5)
        {
            return 0;
        }
        pile.Amount = Roll(10);
        var kinds = new int[12];
        for (var n = 0; n < 10; n++)
        {
            kinds[n] = n;
        }
        // The two words after the list are the frame's stale words; nothing reads the second.
        kinds[10] = StaleStackWord;
        var left = 10;
        var pointer = 10;
        int itemA = StaleStackWord, itemB = 0;
        while (pile.Entries < 4)
        {
            var pick = Roll(left) - 1;
            var chosen = (ushort)kinds[pick];
            if (chosen <= 9)
            {
                (itemA, itemB) = chosen switch
                {
                    0 => (0x48, 0x6D), 1 => (0x4F, 0x74), 2 => (0x49, 0x6E), 3 => (0x4D, 0x72), 4 => (0x4E, 0x73),
                    5 => (0x50, 0x75), 6 => (0x57, 0x7C), 7 => (0x58, 0x7D), 8 => (0x59, 0x7E), _ => (0x5A, 0x7F),
                };
            }
            AddRandomLoot(pile, t, section, itemA, itemB);
            kinds[pick] = kinds[pointer--];
            if (--left <= 0)
            {
                break;
            }
        }
        return 1;
    }

    /// <summary>The set pieces of the first pile: the stairs-down guard piles of the deep mines and the locked doors.</summary>
    private int SetPiece(MineLevel level, PileRecord pile, int t, int section)
    {
        switch (t)
        {
            case 0x0B or 0x82 when section == 14:
                AddLockedDoor(level, pile, 0xE4);
                return 2;
            case 0x0E or 0x84 or 0x0F or 0x85 or 0x10 or 0x86:
                switch (section)
                {
                    case 10:
                        var (first, quality) = t switch { 0x0E or 0x84 => (0x74, 0x99), 0x0F or 0x85 => (0x73, 0x98), _ => (0x6D, 0x92) };
                        AddPileEntry(pile, first, quality, 0x2D, 3);
                        AddPileEntry(pile, 0x70, 0x95, 0x2D, 2);
                        AddPileEntry(pile, 0x66, 0x8B, 0x2D, 1);
                        return 1;
                    case 11:
                        AddLockedDoor(level, pile, 0xE8);
                        return 2;
                    case 12:
                        AddPileEntry(pile, RandomSpecialKind(), 0x9F, 0x23, 1);
                        return 1;
                }
                break;
        }
        return 0;
    }

    /// <summary>A random special monster kind 0x96..0xAB.</summary>
    // orig: R009+0x4F86 (182C:0FE6) draft (close per the draft notes: jump table); tests:
    // Oracle:mine.json
    private int RandomSpecialKind() => 0x96 + Roll(22) - 1;

    /// <summary>Adds a random number of items of a kind to a pile (none when the draw is low).</summary>
    // orig: R009+0x5048 (182C:10A8) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    private void AddRandomLoot(PileRecord pile, int t, int section, int type, int x)
    {
        if (pile.Entries >= 4)
        {
            return;
        }
        int m, s;
        if (pile.Entries == 0)
        {
            (m, s) = (1, 0);
        }
        else if (type == 0x48)
        {
            (m, s) = (10, 9);
        }
        else if (type == 0x49)
        {
            (m, s) = (4, 3);
        }
        else if (section > 3 && (t == 0x0B || t == 0x0D))
        {
            (m, s) = (8, 6);
        }
        else
        {
            (m, s) = (8, 7);
        }
        var n = Roll(m) - s;
        if (n > 0)
        {
            AddPileEntry(pile, type, x, 0x23, Roll(3) + n - 1);
        }
    }

    /// <summary>Appends an entry (item type, two quality bytes, count and the item's weight) to a pile.</summary>
    // orig: R009+0x50E6 (182C:1146) draft (byte-matched per the draft notes); tests:
    // Oracle:mine.json
    // DIVERGENCE: an item type outside the item list weighs 0 here (the original reads the bytes beyond the list).
    private void AddPileEntry(PileRecord pile, int type, int byte2, int byte3, int count)
    {
        if (pile.Entries >= 4 || count <= 0)
        {
            return;
        }
        var items = _g.Context.Items;
        var weight = type >= 0 && type < items.Count ? items[type].Weight : 0;
        pile.SetEntry(pile.Entries, type, byte2, byte3, count, weight);
        pile.Entries++;
    }

    // orig: R009+0x5152 (182C:11B2) draft (an empty switch (6.00A deletes it)); tests:
    // Oracle:mine.json
    // DORMANT: an empty switch over the mine type and section (its code was removed in the original; nothing calls it). Nothing is ported.

    /// <summary>A random special item or monster code for a wall gap of a section (0x5C when the table has none).</summary>
    // orig: R009+0x5C54 (182C:1CB4) draft (unmatched draft); tests: Oracle:mine.json
    private int ItemOfGap(int t, int section)
    {
        var value = 0x5C;
        if (section < 0 || section >= 16)
        {
            return value;
        }
        int[] firstTwelve = [0x5C, 0x71, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xCE];
        switch (t)
        {
            case 0x0B or 0x82:
                {
                    var r = Roll(12);
                    switch (section)
                    {
                        case 3:
                            value = firstTwelve[r - 1];
                            break;
                        case 10:
                            value = 0xCF + r - 1;
                            break;
                        case 8:
                            int[] eighth = [0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE0, 0xE0, 0xE2, 0x153, 0x154, 0x155, 0x156];
                            value = eighth[r - 1];
                            break;
                    }
                    break;
                }
            case 0x0D or 0x83:
                {
                    var r = Roll(18);
                    switch (section)
                    {
                        case 1:
                            value = r <= 12 ? firstTwelve[r - 1] : 0xCF + r - 13;
                            break;
                        case 2:
                            int[] second = [0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE0, 0xE2, 0xE2, 0x153, 0x154, 0x155, 0x156];
                            value = second[r - 1];
                            break;
                        case 7:
                            value = 0xEC;
                            break;
                        case 8:
                            value = 0xEE;
                            break;
                        case 11:
                            value = 0xED;
                            break;
                        case 12:
                            value = 0xEF;
                            break;
                    }
                    break;
                }
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                {
                    var r = Roll(7);
                    switch (section)
                    {
                        case 1:
                            value = firstTwelve[r - 1];
                            break;
                        case 2:
                            value = 0xCA + r - 1;
                            break;
                        case 3:
                            value = 0xD1 + r - 1;
                            break;
                        case 4:
                            value = 0xD8 + r - 1;
                            break;
                        case 5:
                            int[] fifth = [0xDF, 0xE0, 0x153, 0xE2, 0x153, 0x154, 0x155, 0x156];
                            value = fifth[Roll(8) - 1];
                            break;
                    }
                    break;
                }
        }
        return value;
    }

    /// <summary>The odds 1 in n per free cell of a floor decoration, by mine type and section (0 none).</summary>
    // orig: R009+0x6044 (182C:20A4) draft (close per the draft notes: last copy of return 6);
    // tests: Oracle:mine.json
    private static int DecorationOdds(int t, int section)
    {
        switch (t)
        {
            case 0x0B or 0x82:
                return section switch { 1 or 2 or 3 or 11 or 13 or 14 => 12, 12 => 5, _ => 6 };
            case 0x0D or 0x83:
                return section switch { 1 or 8 or 9 or 12 => 12, 2 => 6, _ => 8 };
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                return section is 2 or 3 or 4 ? 6 : 12;
            case 0x0A or 0x0C or (>= 0x66 and <= 0x69) or 0x6C or 0x6D or 0x7F or 0x80 or 0x81:
                return 12;
            default:
                return 0;
        }
    }

    /// <summary>A random floor decoration kind (2..15) for a free cell, by mine type and section; 0 = none.</summary>
    // orig: R009+0x613E (182C:219E) draft (close per the draft notes: jump tables); tests:
    // Oracle:mine.json
    // C: the 0x0d sections 10 and 11 draw 1..14 but their table ends at 12: a 13 or 14 falls out of the inner switch into section 8's `return 8`.
    private int RandomDecoration(int t, int section)
    {
        switch (t)
        {
            case 0x0A:
                return Table(Roll(13), [3, 3, 2, 2, 11, 11, 4, 4, 4, 12, 12, 13, 14]);
            case >= 0x66 and <= 0x69:
                return Table(Roll(9), [11, 11, 4, 4, 4, 12, 12, 13, 14]);
            case 0x0B or 0x82:
                switch (section)
                {
                    case 1 or 2 or 3:
                        return Table(Roll(2), [2, 3]);
                    case 8 or 10:
                        return Table(Roll(9), [11, 11, 4, 4, 4, 12, 12, 13, 14]);
                    case 11:
                        return Table(Roll(9), [7, 15, 11, 4, 4, 4, 12, 13, 14]);
                    case 13:
                        return Table(Roll(10), [8, 7, 15, 11, 4, 4, 4, 12, 13, 14]);
                    case 14:
                        return Table(Roll(9), [8, 7, 11, 4, 4, 4, 12, 13, 14]);
                    default:
                        return Table(Roll(10), [2, 3, 11, 11, 4, 4, 12, 12, 13, 14]);
                }
            case 0x0C or 0x7F or 0x80 or 0x81:
                return Table(Roll(10), [15, 11, 11, 4, 4, 4, 12, 12, 13, 14]);
            case 0x0D or 0x83:
                switch (section)
                {
                    case 1:
                        return Table(Roll(2), [2, 3]);
                    case 2:
                        return Table(Roll(11), [2, 2, 3, 3, 11, 4, 4, 4, 12, 13, 14]);
                    case 6 or 7:
                        return Table(Roll(12), [8, 7, 2, 3, 11, 11, 4, 4, 12, 12, 13, 14]);
                    case 10 or 11:
                        {
                            var value = Table(Roll(14), [7, 15, 2, 3, 11, 11, 4, 4, 12, 12, 13, 14]);
                            // C: a draw of 13 or 14 is not in the inner table and falls into the `case 8` return.
                            return value != 0 ? value : 8;
                        }
                    case 8:
                        return 8;
                    case 12:
                        return Table(Roll(2), [15, 7]);
                    default:
                        return Table(Roll(14), [8, 7, 15, 2, 3, 11, 11, 4, 4, 4, 12, 12, 13, 14]);
                }
            case 0x6C:
                return 8;
            case 0x6D:
                return Table(Roll(2), [15, 7]);
            case 0x0E or 0x0F or 0x10 or 0x84 or 0x85 or 0x86:
                switch (section)
                {
                    case 1:
                        return Table(Roll(2), [2, 3]);
                    case 2 or 3 or 4:
                        return Table(Roll(13), [2, 2, 3, 3, 11, 11, 4, 4, 4, 12, 12, 13, 14]);
                    case 6 or 10:
                        return Table(Roll(8), [7, 15, 11, 4, 4, 12, 13, 14]);
                    default:
                        return Table(Roll(10), [8, 7, 15, 11, 4, 4, 4, 12, 13, 14]);
                }
        }
        return 0;
    }

    /// <summary>The entry for a die roll (1-based); 0 when the roll is past the table.</summary>
    private static int Table(int roll, int[] values) => roll >= 1 && roll <= values.Length ? values[roll - 1] : 0;
}
