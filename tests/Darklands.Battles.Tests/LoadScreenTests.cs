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

        public void Present(Screen screen)
        {
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
