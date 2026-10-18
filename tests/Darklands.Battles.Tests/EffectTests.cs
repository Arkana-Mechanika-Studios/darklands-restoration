using Darklands.Cards;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>The effects of spells and potions in a real battle: casters' spells land and act; the party's thrown potions too.</summary>
public sealed class EffectTests
{
    /// <summary>Alchemists (enemy type 24) cast: their spells land as clouds or markers, and somebody is hit or held.</summary>
    [Fact]
    public void ACastersSpellLandsAndActs()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (screen, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x11, 60256, [(24, 5, 3), (-1, 0, 0), (-1, 0, 0)]));
        var b = tactics.Battle;
        var landed = false;
        for (var t = 0; t < 60 * 60 * 4 && !landed; t++)
        {
            b.Clock++;
            if (tactics.Iterate())
            {
                break;
            }
            if (t % 30 == 0)
            {
                screen.Draw();
                if (tactics.Paused)
                {
                    tactics.TogglePause();
                }
            }
            landed = tactics.Effects.CloudCount + tactics.Effects.MarkerCount + tactics.Effects.AreaCount > 0;
        }
        Assert.True(landed, "a spell landed within four minutes");
        for (var k = 0; k < 4; k++)
        {
            for (var t = 0; t < 16; t++)
            {
                b.Clock++;
                tactics.Iterate();
            }
            screen.Draw();
            Shot(screen, $"effect-landed-{k}");
        }
    }

    /// <summary>A member throws a potion at an enemy: it is used up, the missile flies and the effect lands at the enemy.</summary>
    [Fact]
    public void AThrownPotionLands()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (screen, tactics) = BattleScreenTests.Start(files, new BattleRequest(0x14, 863, [(34, 4, 2), (-1, 0, 0), (-1, 0, 0)]));
        var b = tactics.Battle;
        var enemy = -1;
        for (var t = 0; t < 60 * 60 * 3 && enemy < 0; t++)
        {
            b.Clock++;
            tactics.Iterate();
            if (t % 30 == 0 && tactics.Paused)
            {
                tactics.TogglePause();
            }
            enemy = Enumerable.Range(Combatant.PartySlots, 8).FirstOrDefault(i => b[i].IsAble && b[i].Floor == b.FloorShown, -1);
        }
        Assert.True(enemy >= 0, "an enemy came out");
        var member = Enumerable.Range(0, Combatant.PartySlots).First(i => b[i].IsAble);
        b[member].Items.Add(new ItemStack(0x65, 0x8A, 30, 2, 1));
        b[member].Action = CombatAction.None;
        var list = tactics.Effects.ThrowList(member);
        Assert.Contains(list, l => l.Potion == 6 && l.Quality == 30);
        Assert.True(tactics.OrderThrow(member, 6, 30, enemy, b[enemy].Position));
        if (tactics.Paused)
        {
            tactics.TogglePause();
        }
        var landed = false;
        for (var t = 0; t < 60 * 20 && !landed; t++)
        {
            b.Clock++;
            tactics.Iterate();
            landed = tactics.Effects.MarkerCount > 0;
        }
        Assert.True(landed, "the Sunburst landed");
        Assert.Equal(1, b[member].Items.Single(s => s.Code == 0x65).Count);
        screen.Draw();
        Shot(screen, "potion-thrown");
        b.Clock += 8;
        tactics.Iterate();
        screen.Draw();
        Shot(screen, "potion-thrown-2");
    }

    private static void Shot(Presentation.Battles.BattleScreen screen, string name)
    {
        if (Environment.GetEnvironmentVariable("BATTLE_SHOTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            screen.SaveScreen(Path.Combine(folder, name + ".png"));
        }
    }
}
