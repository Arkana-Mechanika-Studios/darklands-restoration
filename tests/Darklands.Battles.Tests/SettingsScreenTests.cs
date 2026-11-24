using System.Text.Json.Nodes;
using Darklands.Content;
using Darklands.Content.Formats.Text.Strings;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Views;

namespace Darklands.Battles.Tests;

/// <summary>
/// The in-game settings screen (1.2, S-1): its rows, its keys and clicks (commands, never a write of its own), the changes the model
/// carries out (at once, for new games, at the next start; fixed rows untouched; the settings file's keys), its page in the game's
/// style over the game's frame darkened, its line in the Comfort Game menu, and its texts.
/// </summary>
public sealed class SettingsScreenTests
{
    private static SettingRow Row(string key, int current, string? fixedBy = null, params string[] values) =>
        new(key, key, [.. values.Select(v => new SettingChoice(v, v.ToUpperInvariant()))], current, SettingApplies.AtOnce, fixedBy);

    [Fact]
    public void ARowStepsRoundItsChoicesUnlessItIsFixed()
    {
        var row = Row("k", 0, null, "a", "b", "c");
        Assert.Equal("b", row.Stepped(1)!.Value);
        Assert.Equal("c", row.Stepped(-1)!.Value);
        Assert.Equal("A", row.ValueLabel);
        Assert.Null(Row("k", 0, "--x", "a", "b").Stepped(1));
        Assert.Null(Row("k", 0, null, "a").Stepped(1));
    }

    // the view's input alone: its look is not used
    private static (SettingsView View, List<SettingChange> Changes) View(params SettingRow[] rows)
    {
        var changes = new List<SettingChange>();
        return (new SettingsView(null!, () => rows, changes.Add, Keys.AltT), changes);
    }

    private static ViewInput Key(int key) => new(key, default, false);

    [Fact]
    public void KeysChooseARowAndChangeItAsCommands()
    {
        var (view, changes) = View(Row("first", 0, null, "a", "b"), Row("second", 1, null, "x", "y", "z"), Row("fixed", 0, "--fixed", "p", "q"));
        Assert.Equal(ViewResult.Stay, view.Handle(Key(Keys.Right)));
        Assert.Equal(ViewResult.Stay, view.Handle(Key(Keys.Down)));
        view.Handle(Key(Keys.Left));
        view.Handle(Key(Keys.Enter));
        view.Handle(Key(Keys.Down));
        view.Handle(Key(Keys.Right));   // the fixed row: nothing
        view.Handle(Key(Keys.Down));    // round to the first
        view.Handle(Key(Keys.Space));
        Assert.Equal([new("first", "b"), new("second", "x"), new("second", "z"), new("first", "b")], changes);
        Assert.Equal(0, view.Chosen);
        view.Handle(Key(Keys.Up));
        Assert.Equal(2, view.Chosen);
    }

    [Fact]
    public void EscAndTheKeyThatOpenedItClose()
    {
        Assert.Equal(ViewResult.Close, View(Row("k", 0, null, "a", "b")).View.Handle(Key(Keys.Escape)));
        Assert.Equal(ViewResult.Close, View(Row("k", 0, null, "a", "b")).View.Handle(Key(Keys.AltT)));
        Assert.Equal(ViewResult.Stay, View(Row("k", 0, null, "a", "b")).View.Handle(Key(Keys.F6)));
    }

    [Fact]
    public void AClickOnARowChangesItAndOnCloseClosesWhereTheLayoutSays()
    {
        var (view, changes) = View(Row("first", 0, null, "a", "b"), Row("second", 0, null, "x", "y"));
        var list = SettingsLayout.Rows(2);
        var (x, y, _, _) = list.Rectangle!.Value;
        var second = new MouseState(x + 5, y + list.RowHeight!.Value + 2, MouseButtons.Left);
        Assert.Equal(ViewResult.Stay, view.Handle(new ViewInput(null, second, true)));
        Assert.Equal(1, view.Chosen);
        Assert.Equal([new SettingChange("second", "y")], changes);
        // the pointer over the first row chooses it, without a change
        view.Handle(new ViewInput(null, new MouseState(x + 5, y + 2, MouseButtons.None), false));
        Assert.Equal(0, view.Chosen);
        Assert.Single(changes);
        var (cx, cy, _, _) = SettingsLayout.Close.Rectangle!.Value;
        Assert.Equal(ViewResult.Close, view.Handle(new ViewInput(null, new MouseState(cx + 2, cy + 2, MouseButtons.Left), true)));
    }

    [Fact]
    public void TheLayoutKeepsTheRowsNoteKeysAndButtonApartOnThePage()
    {
        var rows = SettingsLayout.Rows(13).Rectangle!.Value;
        var close = SettingsLayout.Close.Rectangle!.Value;
        var note = SettingsLayout.Note;
        var keys = SettingsLayout.KeysLine;
        Assert.True(rows.Y + rows.Height <= note.Y, "the note under the rows");
        Assert.True(note.Y + (2 * 7) <= keys.Y, "the keys under the note");
        Assert.True(keys.Y + 7 <= close.Y, "the button under the keys");
        Assert.True(close.Y + close.Height <= 194, "the button above the rope's foot");
        Assert.True(SettingsLayout.PageX >= 0 && SettingsLayout.PageX + SettingsLayout.PageWidth <= 320);
    }

    /// <summary>Every text of the screen is a key with English words of its own (a language pack translates it).</summary>
    [Fact]
    public void EveryTextOfTheScreenHasWords()
    {
        var ids = typeof(SettingsTexts).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)
            .Concat(PictureFilters.Names.Select(SettingsTexts.Filter)).ToList();
        Assert.True(ids.Count > 30);
        Assert.All(ids, id => Assert.StartsWith("id:rw.settings.", id, StringComparison.Ordinal));
        Assert.All(ids, id => Assert.Contains(id, OriginalMessageText.OwnIds));
        Assert.Equal("Set by --rules at the start: it cannot change here.",
            OriginalMessageText.OwnWords(Darklands.Core.GameText.Of(SettingsTexts.FixedBy, ("by", "--rules"))));
    }

    [Fact]
    public void TheSettingsScreenIsTheCataloguesFirstRegistryView()
    {
        var entry = ViewCatalogue.Find(SettingsLayout.Screen);
        Assert.NotNull(entry);
        Assert.Equal(ViewKind.RegistryView, entry.Kind);
        Assert.Equal(SettingsLayout.Screen, new SettingsView(null!, () => [], _ => { }).Id);
    }

    // ---- with the game's files ----

    private sealed class FakeHost : ISettingsHost
    {
        public List<string> Done { get; } = [];

        public List<IReadOnlyDictionary<string, JsonNode?>> Written { get; } = [];

        public void SetFilter(PictureFilter filter) => Done.Add("filter " + PictureFilters.Name(filter));

        public void SetWindow(int width, int height, bool fullscreen) => Done.Add($"window {width}x{height} {fullscreen}");

        public void SetCurvature(bool on) => Done.Add("curvature " + on);

        public (int Width, int Height)? UsableScreen => (1600, 900);

        public (IReadOnlyList<string> Packs, IReadOnlyList<string> SoundFonts) Found() => (["packs/languages/de", "packs/fonts/hd-fonts", "packs/audio/remix"], ["SOUNDFONT/a.sf2"]);

        public SettingsPackKind KindOf(string folder) => folder.Contains("languages", StringComparison.Ordinal) ? SettingsPackKind.Language
            : folder.Contains("fonts", StringComparison.Ordinal) ? SettingsPackKind.Fonts : SettingsPackKind.Sounds;

        public void Write(IReadOnlyDictionary<string, JsonNode?> values) => Written.Add(values);
    }

    private static (SettingsModel Model, FakeHost Host, GameUi Ui, List<Darklands.Core.OriginalBehaviour> Rules)? Model(IReadOnlyDictionary<string, string>? fixedKeys = null,
        GameSettings? file = null)
    {
        if (GameFolder.Files is not { } files)
        {
            return null;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var host = new FakeHost();
        var rules = new List<Darklands.Core.OriginalBehaviour>();
        var running = ResolvedSettings.Resolve(new CommandLineOptions(), _ => null, file ?? new GameSettings());
        var model = new SettingsModel(host, ui, rules.Add, running, file ?? new GameSettings(), fixedKeys ?? new Dictionary<string, string>());
        return (model, host, ui, rules);
    }

    [Fact]
    public void TheRowsAreTheDesignsInItsOrderWithWhenEachApplies()
    {
        if (Model() is not var (model, _, _, _))
        {
            return;
        }
        var rows = model.Rows();
        Assert.Equal(["display.filter", "display.window", "display.fullscreen", "display.crtCurvature", "interface.mode", "rules.preset", "text.languagePack",
            "text.fontPack", "sound.card", "sound.mt32Roms", "sound.audioPack", "sound.soundFont", "display.cleanDrawing"], rows.Select(r => r.Key));
        Assert.Equal(SettingApplies.NewGames, rows.Single(r => r.Key == "rules.preset").Applies);
        Assert.All(rows.Where(r => r.Key.StartsWith("text.", StringComparison.Ordinal) || r.Key.StartsWith("sound.", StringComparison.Ordinal)),
            r => Assert.Equal(SettingApplies.NextStart, r.Applies));
        Assert.Equal("Picture", rows[0].Label);
        Assert.Equal("Fixed", rows.Single(r => r.Key == "rules.preset").ValueLabel);
        // the packs found, by kind, after None; the window sizes that fit the screen
        Assert.Equal(["None", "de"], rows.Single(r => r.Key == "text.languagePack").Choices.Select(c => c.Label));
        Assert.Equal(["Original", "hd-fonts"], rows.Single(r => r.Key == "text.fontPack").Choices.Select(c => c.Label));
        Assert.Equal(["None", "remix"], rows.Single(r => r.Key == "sound.audioPack").Choices.Select(c => c.Label));
        Assert.Equal(["Default", "a.sf2"], rows.Single(r => r.Key == "sound.soundFont").Choices.Select(c => c.Label));
        Assert.Equal(["720x400", "1080x600", "1440x800"], rows.Single(r => r.Key == "display.window").Choices.Select(c => c.Value));
    }

    [Fact]
    public void ChangesAreCarriedOutAndWrittenWithTheLaunchersKeys()
    {
        if (Model() is not var (model, host, ui, rules))
        {
            return;
        }
        model.Carry(new("display.filter", "crt"));
        model.Carry(new("display.window", "1440x800"));
        model.Carry(new("display.window", "1080x600"));
        model.Carry(new("display.fullscreen", "on"));
        model.Carry(new("interface.mode", "comfort"));
        model.Carry(new("display.cleanDrawing", "on"));
        model.Carry(new("rules.preset", "original"));
        model.Carry(new("text.languagePack", "packs/languages/de"));
        Assert.Equal(["filter crt", "window 1440x800 False", "window 1080x600 False", "window 1080x600 True"], host.Done);
        Assert.True(ui.ComfortInterface);
        Assert.True(ui.Options.CleanDrawing);
        Assert.Equal(Darklands.Core.RulePreset.Original, Assert.Single(rules).Preset);
        string Of(int i) => string.Join(", ", host.Written[i].Select(v => $"{v.Key}={v.Value?.ToJsonString()}"));
        Assert.Equal(8, host.Written.Count);
        Assert.Equal("display.filter=\"crt\"", Of(0));
        Assert.Equal("display.scale=2, display.resolution=\"\"", Of(1));
        Assert.Equal("display.resolution=\"1080x600\"", Of(2));
        Assert.Equal("display.fullscreen=true", Of(3));
        Assert.Equal("interface.mode=\"comfort\"", Of(4));
        Assert.Equal("display.cleanDrawing=true", Of(5));
        Assert.Equal("rules.preset=\"original\"", Of(6));
        Assert.Equal("text.languagePack=\"packs/languages/de\"", Of(7));
        // the rows show the values now
        Assert.Equal("de", model.Rows().Single(r => r.Key == "text.languagePack").ValueLabel);
        Assert.Equal("Original", model.Rows().Single(r => r.Key == "rules.preset").ValueLabel);
    }

    [Fact]
    public void ARowTheCommandLineFixesIsShownFixedAndNeverChanges()
    {
        if (Model(new Dictionary<string, string> { ["rules.preset"] = "--rules", ["display.scale"] = "--scale" }) is not var (model, host, _, rules))
        {
            return;
        }
        var rows = model.Rows();
        Assert.Equal("--rules", rows.Single(r => r.Key == "rules.preset").FixedBy);
        Assert.Equal("--scale", rows.Single(r => r.Key == "display.window").FixedBy);
        model.Carry(new("rules.preset", "original"));
        model.Carry(new("display.window", "1440x800"));
        Assert.Empty(rules);
        Assert.Empty(host.Done);
        Assert.Empty(host.Written);
    }

    [Fact]
    public void TheNextStartRowsShowTheFilesValues()
    {
        if (Model(file: new GameSettings { SoundCard = "mt32", AudioPack = "elsewhere/mypack" }) is not var (model, _, _, _))
        {
            return;
        }
        var rows = model.Rows();
        Assert.Equal("Roland MT-32", rows.Single(r => r.Key == "sound.card").ValueLabel);
        var audio = rows.Single(r => r.Key == "sound.audioPack");
        Assert.Equal("mypack", audio.ValueLabel);
        Assert.Equal(["None", "remix", "mypack"], audio.Choices.Select(c => c.Label));
    }

    /// <summary>
    /// The page in the game's style: the rope frame on parchment with the card pictures' colours, over the game's frame darkened, whose
    /// pixels never use a colour the page draws with.
    /// </summary>
    [Fact]
    public void ThePageIsDrawnOverTheGamesFrameDarkened()
    {
        if (Model() is not var (model, _, ui, _))
        {
            return;
        }
        var frame = new Screen();
        var canvas = new Darklands.Presentation.Graphics.Canvas(frame);
        foreach (var (i, c) in Darklands.Presentation.Graphics.Palette.StartupColors.Select((c, i) => (i, c)))
        {
            canvas.SetColor(i, c);
        }
        canvas.DrawPicture(ui.Assets.Picture("startscr"));
        var under = new Screen();
        frame.Pixels.CopyTo(under.Pixels, 0);
        frame.Palette.CopyTo(under.Palette, 0);
        var view = model.View();
        view.Draw(frame);
        var loadscrn = ui.Assets.Picture("loadscrn").EmbeddedPalette!.Entries[0xFF]!.Value;
        Assert.Equal(0xFF000000u | (uint)loadscrn.R << 16 | (uint)loadscrn.G << 8 | loadscrn.B, frame.Palette[0xFF]);
        int[] page = [.. Enumerable.Range(0, 16), 0x81, 0x82, 0x83, 0x84, 0x86, 0x8A, 0x8E, 0xFF];
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                var p = frame.Pixels[(y * Screen.Width) + x];
                if (x < SettingsLayout.PageX || x >= SettingsLayout.PageX + SettingsLayout.PageWidth)
                {
                    // the darkened frame: never a page colour, every colour at a third of its light
                    Assert.DoesNotContain((int)p, page);
                    var c = frame.Palette[p];
                    Assert.True(((c >> 16) & 0xFF) <= 85 && ((c >> 8) & 0xFF) <= 85 && (c & 0xFF) <= 85, $"pixel ({x}, {y}) colour {c:X8}");
                }
            }
        }
        // the rope on the page's left edge, the parchment inside, the title's ink
        Assert.Contains(frame.Pixels.Skip(100 * Screen.Width).Take(Screen.Width).Skip(SettingsLayout.PageX).Take(7), p => p is 0x81 or 0x82 or 0x83 or 0x84 or 0x86 or 0x8A or 0x8E);
        Assert.Equal(0xFF, frame.Pixels[(100 * Screen.Width) + SettingsLayout.PageX + 100]);
        Assert.Contains(frame.Pixels.Skip(9 * Screen.Width).Take(8 * Screen.Width), p => p == 4);
        // drawing again over the same frame gives the same picture
        var first = (byte[])frame.Pixels.Clone();
        under.Pixels.CopyTo(frame.Pixels, 0);
        under.Palette.CopyTo(frame.Palette, 0);
        view.Draw(frame);
        Assert.Equal(first, frame.Pixels);
    }

    [Fact]
    public void TheGameMenuEndsWithTheSettingsLineWhenAHostOffersTheScreenInBothInterfaces()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files)) { SettingsKeyLabel = "\x0fT" };
        var items = new List<MenuBar.Item> { new(() => "\x02one", true, "", () => { }), new(() => "\x02two", true, "", () => { }) };
        var game = MenuBar.WithSettingsLine(ui, items);
        Assert.Equal(2, game.Count);   // no host offers the screen (the oracle, the replays)
        var opened = 0;
        ui.OpenSettings = () => opened++;
        Assert.Equal(3, game.Count);
        ui.ComfortInterface = true;
        Assert.Equal(3, game.Count);
        Assert.Equal("\x02Settings", game[2].Text());
        Assert.Equal("\x0fT", game[2].Key);
        game[2].Run();
        Assert.Equal(1, opened);
        // the original's lines first, in their places
        Assert.Equal(["\x02one", "\x02two", "\x02Settings"], game.Select(i => i.Text()));
        ui.OpenSettings = null;
        Assert.Equal(2, game.Count);
    }
}
