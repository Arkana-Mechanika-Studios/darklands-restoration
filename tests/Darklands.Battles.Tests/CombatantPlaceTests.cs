using Darklands.Core.Battles;
using Darklands.Core.Characters;
using Darklands.Core.Randomness;

namespace Darklands.Battles.Tests;

/// <summary>
/// A party member's combatant record is its character record: the place (+0x04 / +0x06) and the facings (+0x19 / +0x1A) the saved game
/// holds are the combatant's at the battle's start (R002+0x1830 leaves them; R014+0x4188 places the able members only), so a member who
/// is not able stays where the record put it.
/// </summary>
public sealed class CombatantPlaceTests
{
    private static Character Member(CharacterStatus status)
    {
        var record = new byte[0x22A];
        record[0x04] = 0x10;
        record[0x05] = 0x01;      // x 272
        record[0x06] = 0x50;      // y 80
        record[0x08] = 0x20;      // destination x 32
        record[0x19] = 3;
        record[0x1A] = 5;
        return new Character { Status = status, OriginalRecord = record };
    }

    [Theory]
    [InlineData(CharacterStatus.Unconscious)]
    [InlineData(CharacterStatus.Away)]
    [InlineData(CharacterStatus.Able)]
    public void ThePlaceAndFacingsOfTheRecordAreTheCombatants(CharacterStatus status)
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1));
        CombatantSetup.FromMember(battle[0], Member(status));
        Assert.Equal(new MapPoint(272, 80), battle[0].Position);
        Assert.Equal(new MapPoint(32, 0), battle[0].Destination);
        Assert.Equal(3, battle[0].Facing);
        Assert.Equal(5, battle[0].SpriteFacing);
    }

    [Fact]
    public void ACharacterWithoutARecordStartsAtTheOrigin()
    {
        var battle = new Battle(new CombatTables(new byte[CombatTables.Length], new byte[16]), new MscRandom(1));
        CombatantSetup.FromMember(battle[0], new Character { Status = CharacterStatus.Unconscious });
        Assert.Equal(new MapPoint(0, 0), battle[0].Position);
    }
}
