using System.Reflection;
using Darklands.Cards;
using Darklands.Platform;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The battle's screen flags of F6 and Alt-O: Party Info (DS:987F) is 2 for the whole battle (R002+0x0258), and the menu titles and
/// Change Marching Order (DS:9893) are 2 while the combat sheet (R022+0x009D..0x02B7) or the loot screen (R014+0xE42D..0xE72B) is up.
/// </summary>
public sealed class HotKeyFlagsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void PartyInfoIsRefusedForTheWholeBattle()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (screen, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        Assert.Equal(2, tactics.Menus.PartyInfo);
        // F6 in the battle: taken (the hot key), the screen not opened
        var opened = 0;
        var owner = (Darklands.Presentation.BattleScreens)typeof(BattleScreen).GetField("_owner", Any)!.GetValue(screen)!;
        owner.PartyInfo = () => opened++;
        Assert.True((bool)typeof(BattleScreen).GetMethod("BattleHotKey", Any)!.Invoke(screen, [Keys.F6])!);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void TheMenusAreGreyedWhileTheSheetOrTheLootScreenIsUp()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (screen, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x5E, 4545, [(-1, 0, 0), (-1, 0, 0), (-1, 0, 0)]));
        var m = tactics.Menus;
        var before = (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder);
        Assert.Equal(1, m.MarchingOrder);
        var restore = (Action)typeof(BattleScreen).GetMethod("GreyMenusForAScreen", Any)!.Invoke(screen, [])!;
        Assert.Equal(((byte)2, (byte)2, (byte)2, (byte)2), (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder));
        // Alt-O while the sheet is up: refused (left for the screen)
        Assert.False((bool)typeof(BattleScreen).GetMethod("BattleHotKey", Any)!.Invoke(screen, [Keys.AltO])!);
        restore();
        Assert.Equal(before, (m.OrdersMenu, m.AttackMenu, m.PartyMenu, m.MarchingOrder));
    }
}
