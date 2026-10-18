namespace Darklands.Core.Characters;

/// <summary>Whom a saint can be called for (the seventh value of the saint table).</summary>
[Flags]
public enum SaintUse
{
    /// <summary>For the praying member.</summary>
    Self = 1,

    /// <summary>For the whole party.</summary>
    Party = 2,
}

/// <summary>
/// One saint's fixed values: feast day (day of the year), the picture class of the prayer
/// screen, the least and most divine favor a prayer may spend, the Virtue needed, the base
/// chance, and whom the saint helps.
/// </summary>
public readonly record struct SaintInfo(int FeastDay, int Background, int MinFavor, int MaxFavor, int MinVirtue, int BaseChance, SaintUse Use)
{
    public SaintInfo(int feastDay, int background, int minFavor, int maxFavor, int minVirtue, int baseChance, int use)
        : this(feastDay, background, minFavor, maxFavor, minVirtue, baseChance, (SaintUse)use)
    {
    }
}

/// <summary>
/// The 136 saints' table values, in saint order (the order of DARKLAND.LST and of the
/// members' saint bits). Derived from the game's saint functions and its saint table.
/// </summary>
// orig: what 0..6 (t[0..6]) of the saint functions R039 seg 165C named per saint in SaintBook (R039, matched); tests: Oracle:saints.json (every value of all 136 functions and of the two unlisted ones)
// C: St.Charity (R039+0x31FA) is the only saint with t6 = 3; St.Paul the Simple's table values recur in the
//   unlisted R039+0x9578.
public static class SaintTable
{
    public const int Count = 136;

    /// <summary>The saint whose chance needs an able woman in the party (saint 2, feast day 21).</summary>
    public const int NeedsAWoman = 2;

    public static SaintInfo Of(int saint) => Saints[saint];

    private static readonly SaintInfo[] Saints =
    [
        new(335, 2, 15, 99, 43, 25, 1),   // 0
        new(36, 9, 20, 80, 24, 25, 1),   // 1
        new(21, 9, 10, 99, 19, 25, 1),   // 2
        new(31, 0, 15, 99, 23, 25, 1),   // 3
        new(319, 6, 25, 99, 39, 60, 1),   // 4
        new(139, 1, 15, 99, 44, 65, 1),   // 5
        new(168, 2, 10, 99, 28, 40, 1),   // 6
        new(334, 7, 5, 50, 14, 20, 1),   // 7
        new(231, 7, 20, 99, 37, 15, 1),   // 8
        new(17, 7, 20, 99, 35, 35, 1),   // 9
        new(164, 5, 10, 99, 59, 65, 1),   // 10
        new(199, 1, 15, 99, 18, 25, 1),   // 11
        new(8, 7, 20, 50, 78, 70, 1),   // 12
        new(338, 9, 20, 50, 32, 40, 1),   // 13
        new(30, 9, 15, 60, 21, 20, 1),   // 14
        new(156, 6, 10, 90, 25, 10, 1),   // 15
        new(329, 9, 30, 70, 46, 30, 1),   // 16
        new(120, 9, 10, 99, 33, 25, 1),   // 17
        new(327, 9, 25, 99, 41, 50, 1),   // 18
        new(15, 0, 15, 99, 23, 25, 1),   // 19
        new(213, 9, 10, 50, 40, 50, 3),   // 20
        new(205, 8, 50, 99, 54, 50, 1),   // 21
        new(207, 7, 15, 99, 31, 25, 1),   // 22
        new(224, 8, 30, 70, 33, 20, 1),   // 23
        new(155, 8, 10, 99, 22, 30, 1),   // 24
        new(329, 0, 15, 99, 34, 55, 1),   // 25
        new(161, 0, 20, 99, 36, 60, 1),   // 26
        new(270, 7, 15, 75, 15, 30, 1),   // 27
        new(299, 7, 25, 99, 28, 35, 1),   // 28
        new(270, 7, 25, 99, 54, 50, 1),   // 29
        new(270, 7, 25, 80, 30, 40, 1),   // 30
        new(145, 0, 10, 75, 30, 25, 1),   // 31
        new(283, 1, 25, 99, 38, 20, 1),   // 32
        new(96, 0, 20, 60, 57, 45, 1),   // 33
        new(17, 8, 20, 65, 26, 10, 1),   // 34
        new(85, 7, 30, 99, 53, 45, 1),   // 35
        new(221, 6, 20, 99, 29, 15, 1),   // 36
        new(304, 8, 20, 99, 52, 50, 1),   // 37
        new(107, 4, 15, 99, 59, 60, 1),   // 38
        new(140, 0, 30, 60, 45, 60, 1),   // 39
        new(136, 8, 20, 75, 21, 10, 1),   // 40
        new(287, 0, 20, 99, 5, 10, 1),   // 41
        new(336, 1, 10, 60, 29, 25, 1),   // 42
        new(221, 6, 20, 99, 31, 55, 1),   // 43
        new(312, 6, 15, 99, 40, 30, 1),   // 44
        new(154, 7, 15, 50, 32, 40, 1),   // 45
        new(139, 4, 20, 99, 42, 25, 1),   // 46
        new(264, 7, 30, 99, 41, 30, 1),   // 47
        new(14, 7, 25, 99, 37, 30, 1),   // 48
        new(269, 0, 30, 99, 17, 45, 1),   // 49
        new(254, 0, 40, 99, 24, 35, 1),   // 50
        new(125, 7, 25, 99, 17, 35, 1),   // 51
        new(274, 5, 10, 60, 10, 1, 1),   // 52
        new(273, 3, 30, 75, 56, 45, 1),   // 53
        new(3, 9, 40, 99, 73, 75, 1),   // 54
        new(114, 6, 40, 99, 23, 10, 1),   // 55
        new(5, 4, 40, 75, 34, 50, 1),   // 56
        new(77, 8, 35, 99, 26, 30, 1),   // 57
        new(245, 1, 35, 99, 45, 15, 1),   // 58
        new(125, 5, 15, 99, 64, 60, 1),   // 59
        new(125, 4, 15, 99, 39, 55, 1),   // 60
        new(311, 1, 25, 99, 55, 40, 1),   // 61
        new(158, 6, 25, 50, 44, 55, 1),   // 62
        new(321, 2, 30, 99, 74, 80, 1),   // 63
        new(289, 8, 15, 75, 16, 10, 1),   // 64
        new(194, 6, 30, 99, 61, 75, 1),   // 65
        new(75, 6, 20, 99, 30, 35, 1),   // 66
        new(168, 1, 15, 99, 47, 40, 1),   // 67
        new(260, 8, 25, 99, 13, 25, 1),   // 68
        new(307, 1, 50, 99, 34, 50, 1),   // 69
        new(311, 0, 20, 99, 63, 65, 1),   // 70
        new(135, 4, 10, 99, 21, 35, 1),   // 71
        new(15, 8, 10, 99, 85, 70, 1),   // 72
        new(206, 7, 12, 50, 25, 40, 1),   // 73
        new(292, 7, 35, 75, 66, 50, 1),   // 74
        new(294, 0, 10, 99, 36, 15, 1),   // 75
        new(256, 2, 10, 50, 37, 25, 1),   // 76
        new(89, 6, 40, 99, 20, 30, 1),   // 77
        new(136, 4, 15, 60, 22, 30, 1),   // 78
        new(78, 7, 35, 99, 27, 35, 1),   // 79
        new(301, 7, 20, 90, 15, 5, 1),   // 80
        new(43, 6, 40, 99, 61, 75, 1),   // 81
        new(69, 0, 20, 99, 31, 25, 1),   // 82
        new(178, 4, 25, 99, 51, 60, 1),   // 83
        new(222, 7, 30, 99, 20, 10, 1),   // 84
        new(351, 7, 50, 99, 80, 60, 1),   // 85
        new(74, 7, 30, 99, 49, 45, 1),   // 86
        new(347, 8, 40, 99, 77, 65, 1),   // 87
        new(291, 7, 20, 99, 17, 35, 1),   // 88
        new(167, 9, 35, 99, 60, 50, 1),   // 89
        new(201, 9, 25, 75, 46, 45, 1),   // 90
        new(53, 9, 25, 99, 52, 10, 1),   // 91
        new(280, 7, 25, 99, 35, 40, 1),   // 92
        new(315, 7, 25, 99, 69, 65, 1),   // 93
        new(264, 7, 20, 80, 26, 25, 1),   // 94
        new(265, 7, 30, 50, 81, 70, 1),   // 95
        new(272, 3, 25, 99, 72, 55, 1),   // 96
        new(54, 8, 35, 99, 71, 70, 1),   // 97
        new(240, 5, 10, 99, 66, 65, 1),   // 98
        new(340, 1, 25, 99, 49, 55, 1),   // 99
        new(347, 9, 20, 99, 25, 40, 1),   // 100
        new(181, 1, 30, 99, 47, 60, 1),   // 101
        new(210, 4, 15, 99, 18, 20, 1),   // 102
        new(208, 2, 25, 99, 56, 20, 1),   // 103
        new(76, 0, 25, 99, 22, 15, 1),   // 104
        new(66, 7, 20, 80, 70, 60, 1),   // 105
        new(206, 7, 27, 52, 19, 35, 1),   // 106
        new(66, 8, 40, 99, 16, 10, 1),   // 107
        new(180, 7, 25, 75, 58, 55, 1),   // 108
        new(1, 2, 25, 99, 62, 50, 1),   // 109
        new(54, 7, 10, 75, 19, 20, 1),   // 110
        new(272, 3, 40, 60, 75, 70, 1),   // 111
        new(7, 5, 10, 99, 67, 50, 1),   // 112
        new(181, 5, 10, 99, 29, 35, 1),   // 113
        new(7, 6, 25, 99, 27, 45, 1),   // 114
        new(228, 5, 40, 90, 19, 45, 1),   // 115
        new(92, 2, 25, 99, 64, 50, 1),   // 116
        new(20, 7, 15, 90, 28, 10, 1),   // 117
        new(91, 4, 40, 99, 42, 65, 1),   // 118
        new(228, 6, 20, 99, 27, 30, 1),   // 119
        new(121, 4, 30, 99, 25, 65, 1),   // 120
        new(284, 7, 40, 99, 50, 30, 1),   // 121
        new(140, 7, 10, 99, 62, 65, 1),   // 122
        new(313, 2, 25, 99, 38, 40, 1),   // 123
        new(182, 7, 10, 65, 24, 15, 1),   // 124
        new(28, 1, 15, 99, 68, 65, 1),   // 125
        new(45, 7, 10, 50, 48, 45, 1),   // 126
        new(202, 7, 30, 80, 65, 40, 1),   // 127
        new(166, 6, 40, 99, 48, 65, 1),   // 128
        new(269, 4, 35, 99, 44, 60, 1),   // 129
        new(295, 0, 15, 65, 25, 15, 1),   // 130
        new(312, 7, 20, 99, 35, 35, 1),   // 131
        new(157, 6, 25, 99, 50, 55, 1),   // 132
        new(311, 6, 15, 50, 43, 55, 1),   // 133
        new(273, 6, 20, 99, 24, 35, 1),   // 134
        new(117, 8, 10, 65, 23, 15, 1),   // 135
    ];
}
