using Darklands.Cards;
using Darklands.Content;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;
using Xunit.Abstractions;

namespace Darklands.Battles.Tests;

/// <summary>The battle screen end to end on the player's files: the battlefield is built, the party placed, and the real-time loop runs.</summary>
public sealed class BattleScreenTests
{
    [Fact]
    public void ThePartyIsPlacedAndTheEnemiesComeOut()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (screen, tactics) = Start(files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]));
        var b = tactics.Battle;
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(CharacterStatus.Able, b[i].Status);
            Assert.NotEqual(new MapPoint(0, 0), b[i].Position);
            Assert.True(tactics.Map.Collides(i, b[i].Position.X, b[i].Position.Y).IsFree, $"member {i} stands on a free spot");
            Assert.True(tactics.Slots[i].FileName != "", $"member {i}: {tactics.SlotCodes[i]} {tactics.SlotWeapons[i]} pose {b[i].Pose}");
        }
        // The hidden encounters are checked from the battle's first moment (DS:D6F0 = 1): the run stops once one is out, before a
        // lost fight ends in the skill messages that wait for a key.
        for (var t = 0; t < 60 * 60 && b.Result == BattleResult.Running
            && !Enumerable.Range(Combatant.PartySlots, 8).Any(i => b[i].Status != CharacterStatus.Empty); t += 30)
        {
            RunFor(screen, tactics, 30);
        }
        Assert.True(Enumerable.Range(Combatant.PartySlots, 8).Any(i => b[i].Status != CharacterStatus.Empty) || b.Result != BattleResult.Running,
            "within a minute the hidden enemies come out (or the battle is over)");
    }

    [Fact]
    public void ThePartyBeatsALoneEnemyAndItsBelongingsAreLeft()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var (screen, tactics) = Start(files, new BattleRequest(0x11, 60256, [(18, 0, 1), (-1, 0, 0), (-1, 0, 0)]));
        var b = tactics.Battle;
        var placed = false;
        var piles = 0;
        for (var t = 0; t < 60 * 60 * 5; t++)
        {
            b.Clock++;
            var over = tactics.Iterate();
            placed |= Enumerable.Range(Combatant.PartySlots, 8).Any(i => b[i].Status != CharacterStatus.Empty);
            piles = Math.Max(piles, tactics.LootPiles.Count);
            if (over || (placed && piles > 0 && Enumerable.Range(Combatant.PartySlots, 8).All(i => !b[i].IsAble)))
            {
                break;
            }
        }
        Assert.True(placed, "the enemy came out");
        Assert.True(b.Result == BattleResult.Won || Enumerable.Range(Combatant.PartySlots, 8).All(i => !b[i].IsAble), $"result {b.Result}");
        Assert.True(piles > 0, "the fallen left a pile");
    }

    [Fact]
    public void AHirelingInASlotNeverHeldFightsAndIsSavedInTheBattle()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        Core.GameState? state = null;
        var (_, tactics) = Start(files, new BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]), s =>
        {
            state = s;
            Assert.Equal(4, Core.Rules.PartyRules.AddMember(s, "Wolf Bauer", "Wolf", "F01", 100, 1));
        });
        var b = tactics.Battle;
        var member = state!.Party.Members[4];
        Assert.Equal(CharacterStatus.Able, b[4].Status);
        Assert.Equal(member.Attributes.Select(v => (sbyte)v), b[4].Attributes);
        Assert.NotEqual(new MapPoint(0, 0), b[4].Position);
        BattleSaveTests.Run(tactics, 120);
        var (sav, bsv) = BattleSaveTests.SaveInBattle(state, tactics);
        var loaded = Persistence.OriginalSaves.Read(sav, bsv, "dksave1.sav", WorldData.Load(files)).State.Party.Members[4];
        var record = loaded.OriginalRecord!;
        // C: the record is the combatant record: where the hireling stands, and join-party-member's +0x1C (the slot) the battle keeps
        Assert.Equal(b[4].Position, new MapPoint((short)(record[4] | record[5] << 8), (short)(record[6] | record[7] << 8)));
        Assert.Equal(4, record[0x1C]);
        Assert.Equal(member.Attributes, loaded.Attributes);
    }

    internal static (BattleScreen Screen, TacticalBattle Tactics) Start(GameFiles files, BattleRequest request,
        Action<Core.GameState>? prepare = null)
    {
        var ui = new GameUi(new SilentHost(), new GameAssets(files));
        var state = Simulation.NewGame.Quickstart(WorldData.Load(files), 7);
        prepare?.Invoke(state);
        var screens = new BattleScreens(ui, () => state);
        var screen = new BattleScreen(ui, new BattleAssets(files), state, screens);
        screen.Start(request);
        return (screen, screen.Tactics!);
    }

    internal static Core.Battles.Generation.GeneratedBattlefield Build(GameFiles files, int battlefield, int seed)
    {
        var assets = new BattleAssets(files);
        var context = new Core.Battles.Generation.BattlefieldContext
        {
            Enemies = assets.Enemies, Items = assets.Items, Tables = assets.Tables, Maps = assets.Maps, Citadel = assets.Citadel,
            Random = new Core.Randomness.MscRandom(1), Location = 3,
        };
        return Core.Battles.Generation.BattlefieldBuilder.Build(context, battlefield, seed, [(34, 4, 2), (-1, 0, 0), (-1, 0, 0)]);
    }

    internal static void RunFor(BattleScreen screen, TacticalBattle tactics, int clockTicks)
    {
        for (var t = 0; t < clockTicks; t++)
        {
            tactics.Battle.Clock++;
            if (tactics.Iterate())
            {
                return;
            }
            if (t % 30 == 0)
            {
                screen.Draw();
                if (tactics.Paused)
                {
                    tactics.TogglePause();   // the battle opens in the leader's order mode: Space resumes it
                }
            }
        }
    }

    internal sealed class SilentHost : IHost, IVideo, IInput, IClock, IAudio
    {
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void Present(Screen screen)
        {
        }

        public void Pump()
        {
        }

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Sleep(int milliseconds) => _now += milliseconds;

        public void PlayMusic(int tune)
        {
        }

        public void StopMusic()
        {
        }

        public void PlaySound(int effect)
        {
        }
    }
}

public sealed class BattleDiagnostics(ITestOutputHelper output)
{
    [Fact]
    public void Trace()
    {
        if (GameFolder.Files is not { } files || Environment.GetEnvironmentVariable("BATTLE_TRACE") is null)
        {
            return;
        }
        var (screen, tactics) = BattleScreenTests.Start(files, new Darklands.Cards.BattleRequest(0x14, 863, [(34, 4, 8), (18, 5, 2), (34, 1, 2)]));
        var b = tactics.Battle;
        for (var t = 0; t < 60 * 60; t++)
        {
            b.Clock++;
            if (tactics.Iterate())
            {
                break;
            }
            if (t % 120 == 0)
            {
                var line = string.Join(" | ", Enumerable.Range(0, 13).Where(i => b[i].Status != Darklands.Core.Characters.CharacterStatus.Empty)
                    .Select(i => $"{i}:{b[i].Status.ToString()[0]} e{b[i].Endurance} a{(int)b[i].Action:X} s{(int)b[i].State} t{b[i].Target} x{(int)b[i].Exchange} p{b[i].Position.X},{b[i].Position.Y}"));
                output.WriteLine($"{b.Clock} rc{(b.RealCombat ? 1 : 0)} {line}");
            }
        }
        output.WriteLine($"result {b.Result}");
    }
}
