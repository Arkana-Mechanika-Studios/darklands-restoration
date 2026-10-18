namespace Darklands.Core.Characters.Creation;

/// <summary>
/// Which occupations a character may take next (<c>R037+0x691E</c>): each of the 37
/// occupations has its own requirements on age, attributes, skills, sex, the last two
/// occupations, the family background and the occupations held before. The list is built in
/// occupation order and stops at 15 entries. The conditions are the original's, including its
/// tautologies (marked "always true").
/// </summary>
// orig: R037+0x691E (18C8:22BE) draft (conditions extracted mechanically, 22 bytes short of the original); checked against the original bytes by Oracle:chargen.json; tests: CharacterCreationTests, CreationOracleTests
public static class OccupationEligibility
{
    public const int MaxChoices = 15;

    // C: the original also resets the attribute/skill cursor (DS:8BFC = 0) and fills the 20-entry list with -1 first;
    // CharacterCreation.RefreshChoices does both around this call.
    public static List<int> Available(CharacterCreation c)
    {
        var list = new List<int>();
        for (var i = 0; i < CharacterCreation.OccupationCount; i++)
        {
            if (IsAvailable(c, i))
            {
                list.Add(i);
            }
            if (list.Count >= MaxChoices)
            {
                break;
            }
        }
        return list;
    }

    private static bool IsAvailable(CharacterCreation c, int occupation)
    {
        var age = c.Age;
        var young = age < 20;
        var bg = c.Background;
        var last = c.LastOccupation;
        var before = c.PreviousOccupation;
        // C: the taken flags are tested as (DS:4402[p] & 1); case 16 writes (!DS:4402[p] & 1), the same for flags that are only ever 0 or 1.
        bool T(params int[] occupations) => occupations.Any(c.HasBeen);
        bool Last(params int[] occupations) => occupations.Contains(last);
        bool Before(params int[] occupations) => occupations.Contains(before);
        bool LastTwo(params int[] occupations) => Before(occupations) && Last(occupations);
        int A(Attribute a) => c.Attributes[(int)a];
        int S(Skill s) => c.Skills[(int)s];
        var str = A(Attribute.Strength);
        var per = A(Attribute.Perception);
        var intl = A(Attribute.Intelligence);
        var chr = A(Attribute.Charisma);
        var man = !c.IsFemale;

        return occupation switch
        {
            // DORMANT: the second test (younger than 26 with Strength 25) is covered by the first and never decides.
            0 => (!T(0) && age < 30) || (!T(0) && age < 26 && str >= 25),
            1 => T(0, 2, 3, 5, 20, 36, 1),
            2 => T(1, 2, 5, 3),
            3 => intl >= 20 && per >= 20 && chr >= 20 && T(6, 2, 7, 5, 9, 20, 3),
            4 => T(6, 4) || bg == 0,
            5 => S(Skill.Virtue) > 15 && ((young && bg == 0) || T(7, 5) || Last(4, 6, 9, 15, 3)),
            6 => (young && (bg == 0 || bg == 1)) || T(7, 6) || Last(5, 8, 9, 15, 20, 3),
            7 => last == 7 || LastTwo(4, 6, 9, 15),
            // C: "man" is DS:E821 != 1 (the sex byte is only ever 0 or 1).
            8 => man && intl >= 20 && per >= 20 && chr >= 20
                && (T(7, 9, 15, 8) || Last(6, 14, 20, 22, 24, 4) || LastTwo(12, 13, 21, 10)),
            9 => man && intl >= 25 && per >= 25 && chr >= 25 && (Last(9, 15) || LastTwo(7, 8, 6)),
            10 => man && Last(8, 10, 11, 13, 14, 15),
            11 => S(Skill.Virtue) >= 15,
            // C: occupations 15 and 8 are tested twice, as in the original.
            // DORMANT: the second tests of 15 and 8 never decide (the first ones already left when the bit is set).
            12 => (young && bg != 4 && bg != 5) || (intl >= 12 && !T(13, 14, 15, 8, 9, 10, 15, 8, 22, 23, 24, 25)),
            13 => young || !T(13, 4, 6, 7, 14, 15, 8, 9, 22, 23, 24, 25, 10),
            14 => intl >= 15 && per >= 15 && chr >= 15 && S(Skill.Religion) >= 5
                && T(4, 6, 13, 7, 14, 15, 8, 9, 22, 23, 24, 25, 10, 21),
            // DORMANT: intl >= 12 after intl >= 20 never decides.
            15 => intl >= 20 && per >= 20 && chr >= 20 && S(Skill.Religion) >= 15 && intl >= 12
                && (Last(4, 6, 8, 7, 9, 15) || LastTwo(14, 24)),
            16 => (young && (bg == 4 || bg == 5))
                || (!Last(4, 3, 5, 6, 8, 18, 24, 25, 28) && !T(7, 15, 9, 19, 20, 26, 29)),
            17 => (young && bg != 4 && bg != 5) || (S(Skill.SpeakCommon) >= 5 && intl >= 12
                && T(3, 4, 6, 8, 9, 14, 16, 15, 18, 22, 23, 24, 25, 28, 29, 32, 17)),
            18 => (young && (bg == 0 || bg == 1)) || (S(Skill.SpeakCommon) >= 10 && intl >= 15
                && T(17, 4, 7, 19, 18, 24, 26, 29)),
            19 => S(Skill.SpeakCommon) >= 10 && intl >= 20 && Last(7, 9, 18, 19),
            20 => T(34) && T(4, 2, 3, 5, 7, 8, 15, 9, 19, 24, 29, 26, 20),
            21 => young || (S(Skill.ReadWrite) >= 6 && intl >= 12
                && Last(0, 2, 1, 10, 11, 12, 13, 14, 16, 17, 18, 21, 28, 27, 4, 32, 33)),
            22 => (young && bg == 1) || (S(Skill.ReadWrite) >= 15 && intl >= 12
                && (T(4, 6, 3, 5, 20, 8, 15, 9, 19, 24, 21, 22, 25, 26) || LastTwo(12, 14, 18))),
            23 => S(Skill.Healing) >= 15 && T(21, 22, 24, 25, 26, 23),
            24 => S(Skill.ReadWrite) >= 20 && T(9, 22, 23, 25, 26, 15, 24),
            25 => intl >= 30 && (T(8, 9, 15, 21, 24, 22, 25, 26) || LastTwo(12, 14, 10, 23)),
            26 => intl >= 35 && T(25),
            27 => (young && bg != 0) || (!young && !T(27, 7, 9, 15, 19, 26, 28, 29)
                && !Last(3, 4, 5, 6, 8, 20, 22, 18, 23, 24, 25)),
            28 => (young && (bg == 2 || bg == 3)) || T(23, 25, 26, 28, 29, 27),
            29 => intl >= 12 && T(19, 26, 28),
            30 => (young && bg != 0) || (!young && !T(7, 9, 15, 19, 26, 29)
                && !Last(3, 4, 5, 6, 8, 20, 24, 18, 25)),
            31 => (young && bg != 0) || (!young && !T(7, 9, 15, 19, 26, 28, 29)
                && !Last(3, 4, 5, 6, 8, 20, 24, 18, 25)),
            32 => (young && (bg == 0 || bg == 1 || bg == 2)) || (intl >= 25 && S(Skill.Streetwise) >= 15
                && T(1, 2, 3, 4, 8, 10, 14, 16, 17, 18, 21, 22, 24, 25, 28, 30, 33, 31)),
            // DORMANT: "background is not 3 or not 5" is always true, so every young character qualifies.
            33 => young || (S(Skill.Streetwise) >= 10
                && T(1, 2, 8, 10, 35, 16, 17, 18, 21, 22, 24, 25, 28, 30, 33, 31, 36)),
            // DORMANT: "background is not 0 or not 1" is always true for the young.
            34 => young || (!T(7, 9, 15, 19, 26, 29) && !Last(3, 4, 5, 6, 8, 28, 24, 18, 25)),
            // DORMANT: the last-occupation test ("not 7 or not 15 ...") is always true.
            35 => (young && (bg == 0 || bg == 3 || bg == 5)) || (S(Skill.Woodwise) >= 15
                && T(0, 1, 2, 3, 5, 10, 16, 11, 20, 18, 34, 35, 36)),
            36 => young || T(0, 1, 2, 3, 5, 33, 36, 35) || Last(10, 11, 20, 16, 30, 31, 32, 34),
            // DORMANT: the original's switch range check (occupations above 36) never fails: the loop runs 0..36.
            _ => false,
        };
    }
}
