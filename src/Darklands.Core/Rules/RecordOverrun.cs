using Darklands.Core.Battles;
using Darklands.Core.Characters;

namespace Darklands.Core.Rules;

/// <summary>
/// A member number past the five party records. The original computes the record's address (DS:9C69 + n × 0x80) without a
/// limit, so its status byte and skill bytes are bytes of other variables. The one such record the model holds is number 20:
/// its status byte is the second letter of party slot 0's figure image (member_shield, DS:A669) and its skill bytes 4..16 are
/// the place actions of the combatants of the last battle (DS:A6C4, kept in <see cref="BattleFile.Variables"/> and written
/// into every saved game's battle file). The jewelers' guild at night reaches it with a party whose lowest Stealth is 20.
/// </summary>
// orig: member_skill R029+0x01A0 reads DS:9CC0 + n*0x80 + skill (R029+0x01B8); add_skill R029+0x02EA tests DS:9C69 + n*0x80 != 0 (R029+0x0491)
// and writes the skill byte (R029+0x0540); tests: RecordOverrunTests; direct calls j_prac.py (local/batch/evid1o): R039+0x0132(20, 0xF, 0, 10, 0)
// from city state banks-markt writes DS:A6CF and the .BSV variables' +0x1680 (Place + 11), nothing in the .SAV
// DIVERGENCE: the other records past the party (combatant records 5..12, battle tables and buffers: program memory the model does not hold)
// read as 0 with status 0, so nothing is written there; the original writes a skill byte there when the status byte is not 0. None of those
// bytes is in the .SAV (each of n = 5..99 poked and the original's writer run: j_call.py saved); of the battle file's variables only the
// written bytes of n = 78, 83 and 86 (DS:C3CF initial spot, DS:C64F member door, DS:C7CF member dissolve slot), whose status bytes (DS:C3E9,
// DS:C669, DS:C769) are not in it and were 0 in the states looked at (j_bsv.py).
public static class RecordOverrun
{
    /// <summary>The record whose bytes are modelled: DS:9C69 + 20 × 0x80 = DS:A669.</summary>
    public const int PlaceRecord = 20;

    /// <summary>Its skill byte 4 is DS:A6C4, the place action of combatant 0.</summary>
    private const int FirstPlaceSkill = 4;

    /// <summary>Whether <paramref name="who"/> (already resolved) is past the five party records.</summary>
    public static bool Outside(int who) => who >= Party.MaxMembers;

    /// <summary>The record's status byte (add_skill goes on when it is not 0).</summary>
    public static int Status(GameState s, int who)
    {
        if (who != PlaceRecord)
        {
            return 0;
        }
        var image = s.Party.Slot(0).Image;
        return image.Length > 1 ? (byte)image[1] : 0;
    }

    /// <summary>The record's skill byte, signed as member_skill reads it (cwde).</summary>
    public static int Skill(GameState s, int who, int skill) =>
        PlaceIndex(who, skill) is var place and >= 0 && s.BattleFile is { } held ? (sbyte)held.Variables[BattleVariables.Place + place] : 0;

    /// <summary>Writes the record's skill byte (the low byte, as add_skill stores it).</summary>
    public static void SetSkill(GameState s, int who, int skill, int value)
    {
        if (PlaceIndex(who, skill) is var place and >= 0)
        {
            // C: a program that has had no battle holds zeros there (the data segment's cleared tail), as a new battle file
            s.BattleFile ??= new BattleFile();
            s.BattleFile.Variables[BattleVariables.Place + place] = (byte)value;
        }
    }

    private static int PlaceIndex(int who, int skill) =>
        who == PlaceRecord && skill >= FirstPlaceSkill && skill < FirstPlaceSkill + 13 ? skill - FirstPlaceSkill : -1;
}
