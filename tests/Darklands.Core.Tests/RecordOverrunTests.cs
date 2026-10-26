using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;
using Darklands.Core.Rules;

namespace Darklands.Core.Tests;

/// <summary>
/// A member number past the party records (the jewelers' guild at night passes its lowest Stealth value): the original's direct
/// call R039+0x0132(v, 0xF, 0, 10, 0) from the city state banks-markt with the generator at 0x13 (local/batch/evid1o j_prac.py).
/// </summary>
public sealed class RecordOverrunTests
{
    private static GameState Game()
    {
        var s = TestGame.Create(seed: 0x13);
        s.Party.Members[0].Image = "F01";
        return s;
    }

    [Fact]
    public void RecordTwentyIsTheLastBattlesPlaceActionOfCombatantEleven()
    {
        // original: draws at 0x13, 0x64AA8A, 0xBBE34725; DS:A6CF 00 -> 03; .BSV variables +0x1680 00 -> 03; the .SAV unchanged
        var s = Game();
        Practice.PracticeSkill(s, RecordOverrun.PlaceRecord, (int)Skill.Stealth, 0, 10, 0);
        Assert.Equal(3, s.BattleFile!.Variables[BattleVariables.Place + 11]);
        Assert.Equal(0x1680, BattleVariables.Place + 11);
        var twin = new MscRandom(0xBBE34725);
        twin.NextRaw();
        Assert.Equal(twin.State, ((MscRandom)s.Random).State);
        Assert.Contains("20: Stealth changed from 0 to 3", ((GameMessageLog)s.Messages).Lines);
    }

    [Fact]
    public void RecordTwentyIsLeftAloneWithoutAFigureImageInSlotZero()
    {
        var s = Game();
        s.Party.Members[0].Image = "";
        Practice.PracticeSkill(s, RecordOverrun.PlaceRecord, (int)Skill.Stealth, 0, 10, 0);
        Assert.Null(s.BattleFile);
    }

    [Fact]
    public void TheHeldPlaceActionIsReadSignedAndAddedTo()
    {
        var s = Game();
        s.BattleFile = new BattleFile();
        s.BattleFile.Variables[BattleVariables.Place + 11] = 0xFF;
        Assert.Equal(-1, MemberRules.Skill(s, RecordOverrun.PlaceRecord, (int)Skill.Stealth));
        MemberRules.AddSkill(s, RecordOverrun.PlaceRecord, (int)Skill.Stealth, 3);
        Assert.Equal(2, s.BattleFile.Variables[BattleVariables.Place + 11]);
    }

    [Fact]
    public void OtherRecordsPastThePartyAreNotModelled()
    {
        // original (v = 5, status byte 0): the same three draws, nothing written
        var s = Game();
        Practice.PracticeSkill(s, 5, (int)Skill.Stealth, 0, 10, 0);
        Assert.Null(s.BattleFile);
        var twin = new MscRandom(0xBBE34725);
        twin.NextRaw();
        Assert.Equal(twin.State, ((MscRandom)s.Random).State);
    }
}
