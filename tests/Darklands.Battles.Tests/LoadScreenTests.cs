using Darklands.Content;
using Darklands.Core;
using Darklands.Core.World;
using Darklands.Persistence;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// The load screen and the save dialog on the player's files, driven by keys: the list, the
/// cursor, deleting and loading, and saving under the next free name. The saved games are copies
/// of the player's SAVES folder in a temporary folder.
/// </summary>
public sealed class LoadScreenTests
{
    [Fact]
    public void TheLoadScreensButtonsComeFromTheProgram()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var texts = Content.Formats.Saves.LoadScreenTexts.Decode(files.Read("DARKLAND.EXE"));
        Assert.Contains("oad Saved Game", texts[0]);
        Assert.Contains("elete Saved Game", texts[1]);
        Assert.Contains("eturn to Main Menu", texts[2]);
    }

    [Fact]
    public void DownDeleteAndLoadPickTheGamesInTheirRows()
    {
        if (GameFolder.Files is not { } files || Copy(files, "DKSAVE1.SAV", "DKSAVE2.SAV", "DKSAVE3.SAV") is not { } folder)
        {
            return;
        }
        // Down to the second row, delete it (y), then load the second row (now the third game).
        var host = new KeyHost(Keys.Down, Key('d'), Key('y'), Key('l'));
        var ui = new GameUi(host, new GameAssets(files));
        var screen = new LoadGameScreen(ui, folder, Content.Formats.Saves.LoadScreenTexts.Decode(files.Read("DARKLAND.EXE")));
        string? loadedName = null;
        var loaded = screen.Run(fromStartScreen: true, name =>
        {
            loadedName = name;
            return folder.Load(name, NoGameData.Instance);
        });
        Assert.NotNull(loaded);
        Assert.Equal("DKSAVE3.SAV", loadedName);
        Assert.Equal(["DKSAVE1.SAV", "DKSAVE3.SAV"], folder.Names());
        Assert.Equal("side", loaded.Comment);
        Assert.Equal(10, loaded.State.CurrentLocation);
    }

    [Fact]
    public void TheGameBeingPlayedCannotBeDeletedAndEscLeaves()
    {
        if (GameFolder.Files is not { } files || Copy(files, "DKSAVE1.SAV", "DKSAVE2.SAV") is not { } folder)
        {
            return;
        }
        var host = new KeyHost(Key('d'), Key('y'), Keys.Escape);
        var ui = new GameUi(host, new GameAssets(files));
        var screen = new LoadGameScreen(ui, folder, ["", "", ""]) { Playing = "DKSAVE1.SAV" };
        Assert.Null(screen.Run(fromStartScreen: false, _ => throw new InvalidOperationException("no load")));
        Assert.Equal(["DKSAVE1.SAV", "DKSAVE2.SAV"], folder.Names());
    }

    [Fact]
    public void TheSaveDialogSavesUnderTheNextFreeName()
    {
        if (GameFolder.Files is not { } files || Copy(files, "DKSAVE0.SAV", "DKSAVE1.SAV", "DKSAVE12.SAV", "DKSAVE12.BSV") is not { } folder)
        {
            return;
        }
        var state = folder.Load("DKSAVE12.SAV", NoGameData.Instance).State;
        // Enter keeps the offered comment, "Darklands".
        var ui = new GameUi(new KeyHost(Keys.Backspace, Key('X'), Keys.Enter), new GameAssets(files));
        Assert.Equal("dksave2.sav", new SaveGameDialog(ui, folder).Run(state));
        Assert.Equal("DarklandX", folder.Summary("DKSAVE2.SAV").Comment);
        // The game had not changed: the file is the one it was read from but for the comment, and its battle file is copied.
        var before = File.ReadAllBytes(Path.Combine(folder.Path, "DKSAVE12.SAV"));
        var after = File.ReadAllBytes(Path.Combine(folder.Path, "DKSAVE2.SAV"));
        Assert.Equal(before.Length, after.Length);
        Assert.Equal(before[0x64..], after[0x64..]);
        Assert.Equal(File.ReadAllBytes(Path.Combine(folder.Path, "DKSAVE12.BSV")), File.ReadAllBytes(Path.Combine(folder.Path, "DKSAVE2.BSV")));
        Assert.Equal("dksave2.sav", OriginalSaves.FileNameOf(state));

        // Esc empties the comment: nothing is saved.
        var esc = new GameUi(new KeyHost(Keys.Escape), new GameAssets(files));
        Assert.Null(new SaveGameDialog(esc, folder).Run(state));
        Assert.DoesNotContain("DKSAVE3.SAV", folder.Names());
    }

    /// <summary>
    /// A saved game of a modded game (its own companion file) carries the mod mark on the load screen only where the screens are drawn
    /// clean (GameOptions.CleanDrawing): Classic draws the very picture of a plain saved game; a companion file of an earlier save is no mark.
    /// </summary>
    [Fact]
    public void AModdedGamesSaveIsMarkedOnlyWhereTheScreensAreDrawnClean()
    {
        if (GameFolder.Files is not { } files || Copy(files, "DKSAVE1.SAV") is not { } plain || Copy(files, "DKSAVE1.SAV") is not { } modded)
        {
            return;
        }
        var state = modded.Load("DKSAVE1.SAV", NoGameData.Instance).State;
        ModdedSaves.MarkUsed(state, [new ModMark("wayside-shrine", "Wayside shrine", "1")]);
        var name = modded.Save(state, "marked", true, true);
        File.Copy(Path.Combine(modded.Path, name.ToUpperInvariant()), Path.Combine(plain.Path, name.ToUpperInvariant()));
        Assert.True(modded.IsModded(name.ToUpperInvariant()));
        Assert.False(modded.IsModded("DKSAVE1.SAV"));
        Assert.False(plain.IsModded(name.ToUpperInvariant()));

        byte[] Shown(SaveFolder folder, bool clean)
        {
            var host = new KeyHost(Keys.Escape);
            var ui = new GameUi(host, new GameAssets(files));
            ui.Options.CleanDrawing = clean;
            Assert.Null(new LoadGameScreen(ui, folder, ["", "", ""]).Run(fromStartScreen: false, _ => null));
            return host.Last!;
        }
        Assert.Equal(Shown(plain, clean: false), Shown(modded, clean: false));
        var withMark = Shown(modded, clean: true);
        var without = Shown(plain, clean: true);
        // the mark is on the new save's row, at the right end of its first line (row r at y = (r + 1) * 16 + 6): nothing else changed
        var top = (modded.Names().ToList().IndexOf(name.ToUpperInvariant()) + 1) * 16 + 6;
        var changed = Enumerable.Range(0, withMark.Length).Where(i => withMark[i] != without[i]).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, i => Assert.True(i % Screen.Width >= 0x130 && i / Screen.Width >= top && i / Screen.Width < top + 9, $"pixel ({i % Screen.Width}, {i / Screen.Width})"));

        // the companion file of an earlier save of that name: the .SAV replaced, no mark
        File.Copy(Path.Combine(plain.Path, "DKSAVE1.SAV"), Path.Combine(modded.Path, name.ToUpperInvariant()), overwrite: true);
        Assert.False(modded.IsModded(name.ToUpperInvariant()));
    }

    /// <summary>
    /// Opened from a battle (Alt-L), the load screen stops the battle's palette cycles before its picture sets the colours, as the
    /// original (R037+0x8400..0x8426): the list shows the load screen's own colours, not the battle's water and fire turning over
    /// them (reported as flashing neon colours on the saved games list, rc1).
    /// </summary>
    [Fact]
    public void FromABattleTheListShowsItsOwnColoursNotTheBattlesCycles()
    {
        if (GameFolder.Files is not { } files || Copy(files, "DKSAVE1.SAV", "DKSAVE2.SAV") is not { } folder)
        {
            return;
        }
        uint[] Shown(bool battleCycles)
        {
            // a few keys' frames on the list, then Esc
            var host = new KeyHost(Keys.None, Keys.None, Keys.None, Keys.None, Keys.None, Keys.None);
            var ui = new GameUi(host, new GameAssets(files));
            if (battleCycles)
            {
                // the battle's cycles as BattleScreen starts them, over colours unlike the load screen's (the battlefield's palette)
                for (var i = 0x10; i <= 0x1E; i++)
                {
                    ui.Canvas.SetColor(i, new Content.Formats.Graphics.Rgb24(0xFF, (byte)(i * 8), 0xFF));
                }
                ui.TakeCycle(1, 4, 0x10, 0x15);
                ui.TakeCycle(2, 6, 0x18, 0x1E);
                ui.TakeCycle(4, 5, 0x16, 0x17);
                ui.Cycles.Start(1);
                ui.Cycles.Start(-2);
                ui.Cycles.Start(4);
                ui.RunCycles(true);
                // the battle has been on screen for a while: the cycles have written their ranges
                host.Sleep(500);
                ui.Present();
            }
            Assert.Null(new LoadGameScreen(ui, folder, ["", "", ""]).Run(fromStartScreen: false, _ => null));
            Assert.False(ui.Cycles.Running);
            return host.LastPalette!;
        }
        Assert.Equal(Shown(battleCycles: false), Shown(battleCycles: true));
    }

    private static int Key(char c) => Keys.ForCharacter(c);

    /// <summary>A copy of some of the player's saved games in a new temporary folder.</summary>
    private static SaveFolder? Copy(GameFiles files, params string[] names)
    {
        var saves = Path.Combine(files.Root, "SAVES");
        if (!names.All(n => File.Exists(Path.Combine(saves, n))))
        {
            return null;
        }
        var copy = TempCopies.Create("darklands-load-screen-");
        foreach (var n in names)
        {
            File.Copy(Path.Combine(saves, n), Path.Combine(copy, n));
        }
        return new SaveFolder(copy);
    }

    /// <summary>A host that types the given keys, one per frame, then Esc forever.</summary>
    private sealed class KeyHost(params int[] keys) : IHost, IVideo, IInput, IClock, IAudio
    {
        private readonly Queue<int> _keys = new(keys);
        private long _now;
        private int _frames;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio => this;
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(0, 0, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;

        /// <summary>The pixels last shown.</summary>
        public byte[]? Last { get; private set; }

        /// <summary>The colours last shown.</summary>
        public uint[]? LastPalette { get; private set; }

        public void Present(Screen screen)
        {
            Last = (byte[])screen.Pixels.Clone();
            LastPalette = (uint[])screen.Palette.Clone();
        }

        public void Pump()
        {
        }

        public bool TryReadKey(out int key)
        {
            // A key every few frames, so that the timed boxes run out first.
            if (++_frames % 3 != 0)
            {
                key = 0;
                return false;
            }
            key = _keys.Count > 0 ? _keys.Dequeue() : Keys.Escape;
            return true;
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
