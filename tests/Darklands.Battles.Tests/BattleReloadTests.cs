using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// A load's battle-reload (R003+0x0CE8, every load) sets the alarm trigger and the cursor keys' bytes from the loaded battle file; the
/// program's other battle globals stay as they were.
/// </summary>
public sealed class BattleReloadTests
{
    [Fact]
    public void ALoadTakesTheAlarmTriggerAndTheCursorKeysFromTheBattleFileAndKeepsTheRest()
    {
        var file = new BattleFile();
        var v = file.Variables;
        BitConverter.GetBytes((short)120).CopyTo(v, BattleVariables.AlarmTrigger);
        BitConverter.GetBytes((short)64).CopyTo(v, BattleVariables.AlarmTrigger + 2);
        v[BattleVariables.AlarmTrigger + 4] = 9;
        v[BattleVariables.AlarmTrigger + 5] = 7;
        v[BattleVariables.CursorKeys + 2] = 1;
        BitConverter.GetBytes((ushort)0x1234).CopyTo(v, BattleVariables.CursorKeys + 4);
        var memory = new GenerationProcessMemory { ActionBeforeRecords = 0x80, FortressReducedGroups = true };
        var keys = new CursorKeyState { HeldCallbacks = 77 };

        DarklandsGame.TakeBattleReload(file, memory, keys);

        Assert.Equal((120, 64, 9, 7), memory.AlarmTrigger);
        Assert.True(keys.On);
        Assert.Equal(0x1234, keys.ReleasedAt);
        Assert.Equal(77, keys.HeldCallbacks);
        Assert.Equal(0x80, memory.ActionBeforeRecords);
        Assert.True(memory.FortressReducedGroups);
    }
}
