using Darklands.Cards;
using Darklands.Content;
using Darklands.Content.Formats.Battles;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests;

/// <summary>
/// The battle screen driven like the player drives it (the real-time loop, the pointer, the keys): the move order's ghost under the click,
/// an enemy targeted by a click, and the frames between a won fight's loot screen and the next screen.
/// </summary>
public sealed class BattleUiTests
{
    /// <summary>The fight of the BATTLES.md example: the party walks into three enemies (member 1 walks first).</summary>
    private static readonly BattleRequest Corridor = new(40, 1234, [(34, 4, 3), (-1, 0, 0), (-1, 0, 0)]);

    /// <summary>A click on the floor in order mode: the spot is the pixel's ground point and the ghost stands on it.</summary>
    [Fact]
    public void TheMoveGhostAppearsUnderTheClick()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.Play("wait:10", "key:space", "wait:5", "key:1", "click:180,140", "wait:2");
        var tb = run.Screen.Tactics!;
        Assert.Equal(0, tb.Selected);
        Assert.True(tb.CursorTarget is -2 or -3);
        // the view's origin is the one the screen drew with
        var view = new BattlefieldView { CentreColumn = run.Screen.CameraX / 32, CentreRow = run.Screen.CameraY / 16 };
        Assert.Equal(view.ToMap(180, 140), (tb.CursorPoint.X, tb.CursorPoint.Y));
        // the ghost (11 × 26) is drawn centred on x 180 with its bottom row at y 140, and its pixels are those on the screen
        var (left, top) = view.CursorTopLeft(tb.CursorPoint.X, tb.CursorPoint.Y, 0x0B, 0x1A);
        Assert.Equal((180 - 0x0B / 2, 140 - 0x1A), (left, top));
        var ghost = run.Assets.Images[tb.Cursor == OrderCursor.Ground ? BattleImages.GroundFree : BattleImages.GroundBlocked];
        var a = new Presentation.Graphics.Canvas(new Screen());
        var b = new Presentation.Graphics.Canvas(new Screen());
        a.Clear(0x11);
        b.Clear(0x22);
        Presentation.Graphics.SpriteDrawing.Draw(a, ghost, left, top);
        Presentation.Graphics.SpriteDrawing.Draw(b, ghost, left, top);
        var painted = 0;
        for (var i = 0; i < 64000; i++)
        {
            if (a.Screen.Pixels[i] == b.Screen.Pixels[i])
            {
                painted++;
                Assert.Equal(a.Screen.Pixels[i], run.LastFrame![i]);
            }
        }
        Assert.True(painted > 20, $"{painted} ghost pixels");
    }

    /// <summary>
    /// With an attack chosen (A), a click on an enemy's hot spot makes it the target, and leaving order mode gives the order; with no
    /// order chosen and the member idle or walking, the original reads the same click as a spot on the floor (R014 19CD:0DCC).
    /// </summary>
    [Fact]
    public void AClickOnAnEnemyTargetsIt()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.Play("wait:10", "key:space", "wait:5", "key:1", "click:234,180", "key:space", "wait:150", "key:1", "wait:3");
        var tb = run.Screen.Tactics!;
        var enemy = Enumerable.Range(Combatant.PartySlots, 8).First(i => tb.Battle[i].IsAble && run.Screen.HotSpot(i) is not null);
        var (ex, ey) = run.Screen.HotSpotCentre(enemy)!.Value;

        // member 1 walks (action 1), no order chosen: the click is a spot on the floor under the pointer
        run.Play($"click:{ex},{ey}", "wait:2");
        Assert.Equal(0, tb.Selected);
        Assert.NotEqual(enemy, tb.CursorTarget);
        Assert.True(tb.CursorTarget is -2 or -3);

        run.Play("key:a", "wait:2", $"click:{ex},{ey}", "wait:2");
        Assert.Equal(0x12, tb.OrderAction);
        Assert.Equal(enemy, tb.CursorTarget);
        Assert.Equal(OrderCursor.Target, tb.Cursor);

        run.Play("key:enter", "wait:2");
        Assert.Equal(-1, tb.Selected);
        Assert.Equal(enemy, tb.Battle[0].Target);
    }

    /// <summary>
    /// A won fight: the loot screen, then the battle view again in the battle's colours (palette-reload, floor-view-paint, fight-draw), the
    /// "BattleResults - victory" box over it, the box taken away. The picture of before the battle never comes back in the battle's colours.
    /// </summary>
    [Fact]
    public void AfterAVictoryTheResultsBoxIsShownOverTheBattleInItsColours()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var steps = new List<string> { "wait:5", "key:space" };
        for (var k = 0; k < 30; k++)
        {
            steps.Add("wait:600");
            steps.Add("key:esc");
        }
        uint[]? battlePalette = null;
        var lootEnd = -1;
        Session? session = null;
        session = new Session(files, new BattleRequest(0x11, 60256, [(18, 0, 1), (-1, 0, 0), (-1, 0, 0)]), picture: "APROCH1", loot: (screens, items, money) =>
        {
            battlePalette = (uint[])session!.Ui.Canvas.Screen.Palette.Clone();
            screens.Loot(items, money);
            lootEnd = session.Frames.Count;
        });
        var picture = (byte[])session.LastFrame!.Clone();
        var result = session.Fight(steps);
        Assert.Equal(0, result);
        Assert.NotNull(battlePalette);
        var after = session.Frames.Skip(lootEnd).ToList();
        Assert.True(after.Count >= 2, $"{after.Count} frames after the loot screen");
        foreach (var (pixels, palette) in after)
        {
            Assert.Equal(battlePalette, palette);
            var same = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                same += pixels[i] == picture[i] ? 1 : 0;
            }
            Assert.True(same < pixels.Length / 2, "the picture of before the battle is not shown again");
        }
        // the last frame is the battle view; the one before it differs only inside the results box, mid-view
        var last = after[^1].Pixels;
        var boxed = after[^2].Pixels;
        int x0 = 320, y0 = 200, x1 = -1, y1 = -1;
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                if (last[y * 320 + x] != boxed[y * 320 + x])
                {
                    (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
                }
            }
        }
        Assert.True(x1 >= 0, "the results box was shown");
        Assert.InRange(x0, 60, 190);
        Assert.InRange(x1, 190, 319);
        Assert.InRange(y0, 80, 100);
        Assert.InRange(y1, 100, 120);
        // the box's ground is colour 0xFF (notice-display's f0e)
        var ground = 0;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                ground += boxed[y * 320 + x] == 0xFF ? 1 : 0;
            }
        }
        Assert.True(ground * 2 > (x1 - x0 + 1) * (y1 - y0 + 1), $"box ({x0}, {y0})-({x1}, {y1}): {ground} pixels of colour 0xFF");
    }

    /// <summary>
    /// The click is the release: the button pressed on one member and let go on another, the member under the release enters order
    /// mode (while it is held, the member under the pointer is the selected one, order mode not yet entered).
    /// </summary>
    [Fact]
    public void TheReleaseNotThePressIsTheClick()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.Play("wait:10", "key:space", "wait:5", "ldown:cbt:0", "wait:3");
        var tb = run.Screen.Tactics!;
        Assert.Equal(0, tb.Selected);
        Assert.False(tb.InOrderMode);
        // members 0 and 2 stand close together: member 3's hot spot is apart from member 0's
        run.Play("move:cbt:3", "wait:3");
        Assert.Equal(3, tb.Selected);
        Assert.False(tb.InOrderMode);
        run.Play("lup", "wait:3");
        Assert.True(tb.InOrderMode);
        Assert.Equal(3, tb.Selected);
    }

    /// <summary>
    /// In order mode a held direction key moves the pointer once a combat tick (1 pixel at first), the hover following it; letting the
    /// key go is a click at the pointer, which there picks a spot on the floor.
    /// </summary>
    [Fact]
    public void TheCursorKeysMoveThePointerAndLettingGoClicks()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.Play("wait:10", "key:space", "wait:5", "key:1", "move:180,140", "wait:3");
        var tb = run.Screen.Tactics!;
        Assert.True(tb.InOrderMode);
        Assert.True(tb.CursorKeysOn);
        var x0 = run.Ui.Mouse.X;
        run.Play("kdown:4D", "wait:40");
        var x1 = run.Ui.Mouse.X;
        Assert.True(x1 > x0, $"the pointer stayed at x {x0}");
        Assert.True(x1 - x0 <= 41, $"the pointer moved {x1 - x0} pixels in 41 frames");
        run.Play("kup:4D", "wait:3");
        Assert.True(tb.CursorTarget is -2 or -3);
        var view = new BattlefieldView { CentreColumn = run.Screen.CameraX / 32, CentreRow = run.Screen.CameraY / 16 };
        Assert.Equal(view.ToMap(run.Ui.Mouse.X, run.Ui.Mouse.Y), (tb.CursorPoint.X, tb.CursorPoint.Y));
    }

    /// <summary>
    /// DS:07D6 is a program global the timer counts outside the battles too: a direction key already held when a battle begins (in order mode) moves the
    /// pointer at the speed of the count since the last let-go, read as a signed word (R014+0x36C4: 0x8000 and up is slow), and the time
    /// since the last battle screen ended counts in it (root 047C:000A at every timer callback, cleared only by R014+0x38AE).
    /// </summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0x200, true)]
    public void AKeyHeldIntoABattleTakesItsSpeedFromTheProgramsCount(int ticksOutside, bool slow)
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.CursorKeys.HeldCallbacks = 0x7F00;
        run.CursorKeys.TimerTicksAtLeave = run.Ui.Timer.Ticks - ticksOutside;
        // the battle begins in order mode (its first combat tick pauses it): the key is held from before its start
        run.Play("move:120,140", "kdown:4D", "wait:10");
        var tb = run.Screen.Tactics!;
        Assert.True(tb.CursorKeysOn);
        var x0 = run.Ui.Mouse.X;
        run.Play("wait:40");
        var moved = run.Ui.Mouse.X - x0;
        // 1 pixel a combat tick against 10
        Assert.True(slow ? moved is > 0 and <= 12 : moved >= 30, $"the pointer moved {moved} pixels");
        run.Play("kup:4D", "wait:3");
        Assert.Equal(0, run.CursorKeys.HeldCallbacks & 0xFFF0);
    }

    /// <summary>F10 opens the menu bar from the keyboard: the battle stands still while it is up; Esc closes it with nothing chosen.</summary>
    [Fact]
    public void F10OpensTheKeyboardMenuAndTheBattleWaits()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        // the battle starts in order mode after its first combat tick (order mode pauses it): Space lets the clock run
        run.Play("wait:10", "key:space", "wait:5", "key:f10", "wait:2");
        var tb = run.Screen.Tactics!;
        var clock = tb.Battle.Clock;
        run.Play("wait:30", "key:right", "wait:2", "key:down", "wait:30");
        Assert.Equal(clock, tb.Battle.Clock);
        run.Play("key:esc", "wait:30");
        Assert.True(clock != tb.Battle.Clock, $"clock {clock}, paused {tb.Paused}");
        Assert.False(tb.InOrderMode);
    }

    /// <summary>
    /// T (the Throw list) only sets the bar's flags (R016+0x068A..0x06A1: DS:9C26, 9C2F, 9C3C, 9C41, A394): the bar comes up unfolded at the
    /// next pass's root 0000:0000, not in the pass that read the key (lockstep session orders-potion).
    /// </summary>
    [Fact]
    public void TheThrowKeyOpensTheBarAtTheNextPass()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.Play("wait:10", "key:space", "wait:5");
        var tb = run.Screen.Tactics!;
        tb.Battle[0].Items.Add(new ItemStack(0x5F, 0, 30, 3, 1));
        run.Play("key:1", "wait:3", "key:t", "wait:5");
        Assert.True(run.Screen.ThrowKeyPass >= 0);
        Assert.Equal(run.Screen.ThrowKeyPass + 1, run.Screen.MenuPass);
        run.Play("key:esc", "wait:3");
    }

    /// <summary>
    /// The combat sheet holds the combat clock (R022+0x0066 / 0x03C6, DS:07D3) but the timer callbacks go on counting DS:07D1 (root 047C:000A):
    /// a running battle's clock does not move over a long sheet, and a paused battle ticks (draws) at once after it when 07D1 reached 8
    /// (R014+0x0F7B), unless the byte wrapped back below 8 (256 callbacks).
    /// </summary>
    [Fact]
    public void TheCombatSheetHoldsTheClockButTheTimerCallbacksGoOn()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var sheetMs = 0;
        using var run = new Session(files, Corridor, sheet: (ui, _) => ui.Clock.Sleep(sheetMs));
        run.Play("wait:10", "key:space", "wait:5");
        var tb = run.Screen.Tactics!;
        Assert.False(tb.Paused);
        // a running battle: two seconds on the sheet (120 callbacks) do not move the clock
        sheetMs = 2000;
        var clock = tb.Battle.Clock;
        run.Play("key:f1");
        Assert.InRange(tb.Battle.Clock - clock, 0, 5);

        // paused: wait for a paused tick (07D1 back to 0), then a second on the sheet: the next tick comes in the sheet's own pass
        run.Play("key:space", "wait:2");
        Assert.True(tb.Paused);
        int SyncToTick()
        {
            var ticks = tb.Ticks;
            for (var n = 0; n < 20 && tb.Ticks == ticks; n++)
            {
                run.Play("wait:0");
            }
            return tb.Ticks;
        }
        sheetMs = 1000;
        var t0 = SyncToTick();
        run.Play("key:f1");
        Assert.Equal(t0 + 1, tb.Ticks);
        // 256 callbacks on the sheet: the byte is back where it was, no tick before the cadence's 8
        sheetMs = 256 * 1000 / 60 + 1;
        t0 = SyncToTick();
        run.Play("key:f1");
        Assert.Equal(t0, tb.Ticks);
    }

    /// <summary>
    /// A release over a member's row of the party panel opens the combat sheet (R014+0x3198); a press on the member's own row closes it
    /// at once (R031+0x0635), and its release, which in the original falls inside R022's reload and is forgotten at R022+0x03BE, does not
    /// open the sheet again: the battle goes on (player report on 0.9.2; lockstep session orders-sheetclick).
    /// </summary>
    [Fact]
    public void AClickOnTheOwnRowClosesTheCombatSheetBackToTheBattle()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var opened = 0;
        using var run = new Session(files, Corridor, sheet: (ui, _) =>
        {
            opened++;
            // the sheet: the opening click's button waited out, then it returns on the next press (the member's own row)
            ui.WaitForButtonRelease();
            while (!ui.Mouse.Left)
            {
                ui.Present();
                ui.Clock.Sleep(16);
            }
        });
        run.Play("wait:10", "click:30,20", "wait:5");
        Assert.Equal(1, opened);
        run.Play("click:30,20", "wait:20");
        Assert.Equal(1, opened);
        // the battle reads the panel again: a new click there opens the sheet a second time
        run.Play("click:30,20", "wait:5", "click:30,20", "wait:20");
        Assert.Equal(2, opened);
    }

    /// <summary>
    /// notice-display (root 0000:24BC) over a battle: the caller waits in wait-ticks (root 0410:0302) for the message's ticks while the timer
    /// callback (root 047C:000A) counts DS:07D1 and, unless the clock stands, the combat clock; no tick of the battle runs meanwhile and the box is
    /// gone when the call returns ("USE STAIRS..." DS:064A from R014+0xBF8C, R014+0xC3E0, R016+0x00F0; the direct calls of R014+0xBCA8 with the
    /// party split: DS:07D1 +150 at the delay's return, the clock +150 in castle-p600, unmoved in the paused states). The message is shown
    /// from inside the battle's thread (the sheet's hook, F1), the rest of the pass going on after it.
    /// </summary>
    [Fact]
    public void AMessageWaitsItsTicksWithTheTimerRunning()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        BattleScreen? screen = null;
        var seen = new List<(int Callbacks, ushort Clock, int Ticks, bool Paused)>();
        using var run = new Session(files, Corridor, sheet: (_, _) =>
        {
            var tb = screen!.Tactics!;
            seen.Add((tb.TimerCallbacks, tb.Battle.Clock, tb.Ticks, tb.Paused));
            screen.Message(TacticalBattle.UseStairsText, 0x96);
            seen.Add((tb.TimerCallbacks, tb.Battle.Clock, tb.Ticks, tb.Paused));
        });
        run.Play("wait:10", "key:space", "wait:5");
        screen = run.Screen;
        Assert.False(run.Screen.Tactics!.Paused);
        run.Play("key:f1", "wait:200");
        run.Play("key:space", "wait:2");
        Assert.True(run.Screen.Tactics!.Paused);
        run.Play("key:f1", "wait:200");
        Assert.Equal(4, seen.Count);
        // running: 150 callbacks and 150 clock ticks inside the call, no battle tick
        Assert.False(seen[0].Paused);
        Assert.Equal((seen[0].Callbacks + 0x96) & 0xFF, seen[1].Callbacks);
        Assert.Equal((ushort)(seen[0].Clock + 0x96), seen[1].Clock);
        Assert.Equal(seen[0].Ticks, seen[1].Ticks);
        // paused: the callbacks count, the clock stands
        Assert.True(seen[2].Paused);
        Assert.Equal((seen[2].Callbacks + 0x96) & 0xFF, seen[3].Callbacks);
        Assert.Equal(seen[2].Clock, seen[3].Clock);
        Assert.Equal(seen[2].Ticks, seen[3].Ticks);
    }

    /// <summary>
    /// The original hides the mouse pointer (pointer-hide root 047C:12C3) before the box of notice-display (root 0000:24BC) and shows it again
    /// (root 047C:1293) after the wait: while a message waits, the frame does not depend on where the pointer is.
    /// </summary>
    [Fact]
    public void NoPointerIsDrawnWhileAMessageWaits()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        Session? run = null;
        var shown = new List<byte[]>();
        run = new Session(files, Corridor, sheet: (ui, _) =>
        {
            ui.PointerVisible = false;
            var frames = run!.Frames;
            run.Screen.Message(TacticalBattle.UseStairsText, 0x96);
            shown.Add(frames[^1].Pixels);
        });
        using (run)
        {
            run.Recording = true;
            run.Play("wait:10", "key:space", "wait:5");
            run.MovePointer(100, 60);
            run.Play("key:f1", "wait:200");
            run.MovePointer(250, 120);
            run.Play("key:f1", "wait:200");
        }
        Assert.Equal(2, shown.Count);
        // (the rest of the picture may move: animated ground and sprites) the two places the pointer stood in are the same in both frames
        foreach (var (px, py) in new[] { (100, 60), (250, 120) })
        {
            for (var y = py; y < py + 16; y++)
            {
                Assert.True(shown[0].AsSpan(y * 320 + px, 16).SequenceEqual(shown[1].AsSpan(y * 320 + px, 16)), $"the pointer is drawn near {px},{py}");
            }
        }
    }

    /// <summary>
    /// The battle bar's Game > Difficulty > Expert (R000+0x083E: DS:99EC[2] = difficulty-hard-set, root 0000:1428) sets the difficulty and
    /// shows difficulty-display(1)'s box (root 0000:1438: notice-display(text, 0x3C), root 0000:24BC) in the card window's font that menu-bar-loop
    /// put back before the sub-item (root 0000:019D..0x01A9: font 3), with the pointer hidden meanwhile (root 047C:12C3); the original's
    /// screen: vis10 session tleads-battle-difficulty (the box's pixels identical).
    /// </summary>
    [Fact]
    public void TheBattleBarsDifficultyChoiceShowsItsBox()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        using var run = new Session(files, Corridor);
        run.Play("wait:10", "key:space", "wait:5");
        run.State.Difficulty = Difficulty.Standard;
        run.Recording = true;
        // the right button held on Game, down to Difficulty, right onto its sub-menu's Expert, let go (lockstep session gen-bhurtk-02)
        run.Play("rdown:79,4", "wait:5", "move:99,32", "wait:25", "move:189,48", "wait:25", "rup", "wait:20");
        Assert.Equal(Difficulty.Expert, run.State.Difficulty);
        const string text = "Difficulty is now set to Expert";
        var font = run.Ui.CardText;
        var width = font.Measure(text);
        var x = 60 + (260 - width) / 2;
        var y = 100 - font.LineHeight / 2;
        // the text on the box's fill as the box draws it (font 3; font 1 would stand a row lower)
        var expected = new Screen();
        var canvas = new Darklands.Presentation.Graphics.Canvas(expected);
        canvas.Fill(0, 0, Screen.Width, Screen.Height, 0xFF);
        font.Draw(canvas, x, y, text, MessageBox.TextColor);
        bool Boxed(byte[] f) => Enumerable.Range(y - 1, font.LineHeight + 1)
            .All(row => Enumerable.Range(x - 2, width + 4).All(px => f[row * Screen.Width + px] == expected.Pixels[row * Screen.Width + px]));
        var boxed = run.Frames.FindIndex(f => Boxed(f.Pixels));
        Assert.True(boxed >= 0, "no frame shows the box");
        // no pointer in the box's frame: the pointer drawn over it at the mouse changes it
        var shown = new Screen();
        run.Frames[boxed].Pixels.CopyTo(shown.Pixels, 0);
        Darklands.Presentation.Graphics.SpriteDrawing.Draw(new Darklands.Presentation.Graphics.Canvas(shown), run.Ui.Assets.CommonSprites.Pointer, 189, 48);
        Assert.False(shown.Pixels.AsSpan().SequenceEqual(run.Frames[boxed].Pixels), "the pointer is drawn while the box waits");
    }

    /// <summary>A battle on the player's files with a host that replays the headless host's steps.</summary>
    internal sealed class Session : IDisposable
    {
        private readonly StepHost _host;
        private readonly BattleScreens _screens;
        private readonly BattleRequest _request;
        private readonly GameState _state;

        public Session(GameFiles files, BattleRequest request, string? picture = null,
            Action<GameScreens, IList<ItemStack>, Money>? loot = null, Action<GameUi, int>? sheet = null)
        {
            _host = new StepHost();
            Ui = new GameUi(_host, new GameAssets(files));
            _state = Simulation.NewGame.Quickstart(WorldData.Load(files), 1, () => 1);
            var world = WorldData.Load(files);
            _state.Data = world;
            var lootScreens = new GameScreens(Ui, new PartyPanel(Ui.Assets), _state, new NoScreens());
            _screens = new BattleScreens(Ui, () => _state, loot is null ? null : (items, money) => loot(lootScreens, items, money));
            if (sheet is not null)
            {
                _screens.CharacterSheet = member => sheet(Ui, member);
            }
            Assets = new BattleAssets(files);
            _host.Locate = who => _screens.Current?.HotSpotCentre(who);
            _request = request;
            if (picture is not null)
            {
                Ui.Canvas.DrawPicture(Ui.Assets.Picture(picture));
            }
            Ui.Present();
        }

        /// <summary>Ends a battle left waiting for steps.</summary>
        public void Dispose() => _host.Stop();

        public GameUi Ui { get; }

        public BattleAssets Assets { get; }

        public BattleScreen Screen => _screens.Current!;

        public GameState State => _state;

        /// <summary>The program's cursor keys' state the battles share (DS:D7FE, DS:D945, DS:07D6 ...).</summary>
        public CursorKeyState CursorKeys => _screens.CursorKeys;

        public byte[]? LastFrame => _host.Last?.Pixels;

        public List<(byte[] Pixels, uint[] Palette)> Frames => _host.Frames;

        public bool Recording { set => _host.Recording = value; }

        public void MovePointer(int x, int y) => _host.MovePointer(x, y);

        private bool _started;

        /// <summary>Plays steps in the battle (starting it the first time) and stops it afterwards; the battle can go on with more steps.</summary>
        public void Play(params string[] steps)
        {
            _host.Queue(steps);
            if (_started)
            {
                _host.Resume();
                Rethrow();
                return;
            }
            _started = true;
            _host.Active = true;
            var thread = new Thread(() =>
            {
                try
                {
                    _screens.Battle(_request);
                }
                catch (QuitGameException)
                {
                }
                catch (Exception e)
                {
                    _error = e;
                }
                finally
                {
                    _host.Finished();
                }
            }, 16 * 1024 * 1024) { IsBackground = true };
            thread.Start();
            _host.WaitIdle();
            Rethrow();
        }

        private Exception? _error;

        private void Rethrow()
        {
            if (_error is not null)
            {
                throw new InvalidOperationException("the battle failed", _error);
            }
        }

        /// <summary>Plays the steps until the battle ends; returns battle-outcome.</summary>
        public int Fight(IEnumerable<string> steps)
        {
            _host.Queue(steps);
            _host.Recording = true;
            _host.EndWhenDone = true;
            _host.Active = true;
            return _screens.Battle(_request);
        }
    }

    /// <summary>
    /// The headless host's step semantics (key, click, wait; a click is held over two frames; cbt:i points at a combatant's hot spot). Run on
    /// its own thread by <see cref="Session.Play"/>, it pauses when its steps are done until more are queued.
    /// </summary>
    private sealed class StepHost : IHost, IVideo, IInput, IClock, IAudio
    {
        private readonly Queue<string> _steps = new();
        private readonly Queue<int> _keys = new();
        private readonly Queue<(int, bool)> _transitions = new();
        private bool _held;
        private readonly SemaphoreSlim _idle = new(0), _go = new(0);
        private int _idleFrames, _clickPumps;
        private long _now;
        private bool _finished;

        public Func<int, (int X, int Y)?>? Locate { get; set; }
        public Screen? Last { get; private set; }
        public List<(byte[] Pixels, uint[] Palette)> Frames { get; } = [];
        public bool Recording { get; set; }
        public bool EndWhenDone { get; set; }

        /// <summary>A battle is being played (before it, a frame with no steps left just passes).</summary>
        public bool Active { get; set; }

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested { get; private set; }
        public MouseState Mouse { get; private set; } = new(160, 100, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        public void MovePointer(int x, int y) => Mouse = Mouse with { X = x, Y = y };

        public bool TryReadKeyTransition(out int scanCode, out bool released)
        {
            if (_transitions.TryDequeue(out var t))
            {
                (scanCode, released) = t;
                return true;
            }
            (scanCode, released) = (0, false);
            return false;
        }

        public void Queue(IEnumerable<string> steps)
        {
            foreach (var s in steps)
            {
                _steps.Enqueue(s);
            }
        }

        public void Resume()
        {
            _go.Release();
            WaitIdle();
        }

        public void WaitIdle() => _idle.Wait();

        public void Stop()
        {
            EndWhenDone = true;
            _go.Release();
        }

        public void Finished()
        {
            _finished = true;
            _idle.Release();
        }

        public void Present(Screen screen)
        {
            Last = screen;
            if (Recording)
            {
                Frames.Add(((byte[])screen.Pixels.Clone(), (uint[])screen.Palette.Clone()));
            }
        }

        public void Pump()
        {
            _now += 16;
            if (Mouse.Buttons != MouseButtons.None && !_held)
            {
                if (++_clickPumps < 2)
                {
                    return;
                }
                _clickPumps = 0;
                Mouse = Mouse with { Buttons = MouseButtons.None };
                return;
            }
            if (_idleFrames > 0)
            {
                _idleFrames--;
                return;
            }
            if (!_steps.TryDequeue(out var step))
            {
                if (!Active)
                {
                    return;
                }
                if (EndWhenDone || _finished)
                {
                    QuitRequested = true;
                    return;
                }
                // the steps are done: let the test look, then go on with the next ones
                _idle.Release();
                _go.Wait();
                if (!_steps.TryDequeue(out step))
                {
                    QuitRequested = true;
                    return;
                }
            }
            var (kind, value) = (step.Split(':', 2)[0], step.Contains(':') ? step.Split(':', 2)[1] : "");
            switch (kind)
            {
                case "key":
                    _keys.Enqueue(value switch
                    {
                        "space" => Keys.Space,
                        "enter" => Keys.Enter,
                        "esc" => Keys.Escape,
                        "f10" => Keys.F10,
                        "f1" => Keys.F1,
                        "down" => Keys.Down,
                        "up" => Keys.Up,
                        "right" => Keys.Right,
                        "left" => Keys.Left,
                        _ => Keys.ForCharacter(value[0]),
                    });
                    _idleFrames = 2;
                    break;
                case "click":
                    var (x, y) = Point(value);
                    Mouse = new MouseState(x, y, MouseButtons.Left);
                    _idleFrames = 2;
                    break;
                case "rclick":
                    var (rx, ry) = Point(value);
                    Mouse = new MouseState(rx, ry, MouseButtons.Right);
                    _idleFrames = 2;
                    break;
                case "wait":
                    _idleFrames = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "ldown":
                case "rdown":
                    // a button held until lup / rup (move: the pointer moves with it held)
                    var (hx, hy) = Point(value);
                    _held = true;
                    Mouse = new MouseState(hx, hy, kind == "rdown" ? MouseButtons.Right : MouseButtons.Left);
                    _idleFrames = 2;
                    break;
                case "move":
                    var (mx, my) = Point(value);
                    Mouse = Mouse with { X = mx, Y = my };
                    _idleFrames = 2;
                    break;
                case "lup":
                case "rup":
                    _held = false;
                    Mouse = Mouse with { Buttons = MouseButtons.None };
                    _idleFrames = 2;
                    break;
                case "kdown":
                case "kup":
                    _transitions.Enqueue((Convert.ToInt32(value, 16), kind == "kup"));
                    _idleFrames = 1;
                    break;
            }
        }

        private (int, int) Point(string value)
        {
            if (value.StartsWith("cbt:", StringComparison.Ordinal))
            {
                return Locate?.Invoke(int.Parse(value[4..], System.Globalization.CultureInfo.InvariantCulture)) ?? (0, 0);
            }
            var parts = value.Split(',');
            return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
        }

        public bool TryReadKey(out int key) => _keys.TryDequeue(out key);

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public void PresentText(TextScreen screen)
        {
        }

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

    private sealed class NoScreens : IGameScreens
    {
        public void Barter(int shop, string name, long categories) { }
        public void Camp(int mode) { }
        public void Alchemy(int member, int place) { }
        public void ItemStore() { }
        public void CharacterSheet(int member) { }
        public void PartyInfo() { }
        public void Loot(IList<ItemStack> items, Money money) { }
        public int Battle(BattleRequest request) => 0;
    }
}
