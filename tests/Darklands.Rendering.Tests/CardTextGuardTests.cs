using System.Security.Cryptography;
using Darklands.Cards;
using Darklands.Content;
using Darklands.Content.Formats.Text;
using Darklands.Core;
using Darklands.Core.World;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;
using Darklands.Simulation;

namespace Darklands.Rendering.Tests;

/// <summary>
/// The guard of what a player reads without a pack (GitHub issue 7): every card's text blocks, their $variables expanded in a
/// Quickstart game in three cities (Groningen, city 0; the Quickstart city; a third) by day and by night, laid out and drawn in the
/// card window with the game's own font and with the HD fonts, must give the same expanded bytes and the same pixels as 1.1.3 gave.
/// The hashes of 1.1.3's output are in CardTextGolden.txt (no game text in it); DARKLANDS_CARDTEXT_GOLDEN=&lt;file&gt; writes them
/// instead of comparing (made once with 1.1.3's code). Any change in a card's words, a variable's value or the drawing fails it.
/// </summary>
public sealed class CardTextGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Darklands.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? ".";
    }

    private static string HdFontsPack()
    {
        var root = RepoRoot();
        var folder = Path.Combine(Path.GetTempPath(), "dl-guardfonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "fonts"));
        File.Copy(Path.Combine(root, "docs", "hd-fonts-pack", "pack.json"), Path.Combine(folder, "pack.json"));
        foreach (var slot in Directory.GetFiles(Path.Combine(root, "docs", "hd-fonts-pack", "fonts")))
        {
            File.Copy(slot, Path.Combine(folder, "fonts", Path.GetFileName(slot)));
        }
        File.Copy(Path.Combine(root, "src", "Darklands.Host.Sdl", "Resources", "DejaVuSansMono.ttf"), Path.Combine(folder, "fonts", "DejaVuSansMono.ttf"));
        File.Copy(Path.Combine(root, "src", "Darklands.Launcher", "Resources", "EBGaramond12-Regular.ttf"), Path.Combine(folder, "fonts", "EBGaramond12-Regular.ttf"));
        return folder;
    }

    private sealed class SilentHost : IHost, IVideo, IInput, IClock
    {
        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(0, 0, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => 0;

        public void Present(Screen screen) { }

        public void Pump() { }

        public void Sleep(int milliseconds) { }

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];

    private static string PixelHash(TextPainter painter, MessageBlock b, byte[] expanded, bool codes)
    {
        var screen = new Screen();
        var canvas = new Canvas(screen);
        var layout = new CardTextLayout(painter);
        var settings = new CardTextLayout.Settings { Color = 15 };
        if (codes)
        {
            layout.Draw(canvas, TextCodes.OfBytes(expanded), CardScreen.WindowX + b.X + 5, b.Y + 5, b.Width - 10, settings);
        }
        else
        {
            layout.Draw(canvas, expanded, CardScreen.WindowX + b.X + 5, b.Y + 5, b.Width - 10, settings);
        }
        return Hash(screen.Pixels);
    }

    /// <summary>The lines of the guard: a scenario, a card's block, the expanded bytes' hash, the pixels' hashes (game font, HD fonts).</summary>
    private static List<string> Lines(GameFiles files)
    {
        var world = WorldData.Load(files);
        var assets = new GameAssets(files);
        var plain = new GameUi(new SilentHost(), assets);
        var hd = new GameUi(new SilentHost(), new GameAssets(files));
        FontPack.LoadScalable = json => (TtfFont.Load(json, out var why), why);
        var pack = HdFontsPack();
        try
        {
            Assert.True(hd.UseFontPack(pack, _ => { }));
            var names = new MsgFilesArchiveDecoder().Decode(files.Read("MSGFILES")).Entries
                .Select(e => e.Name.EndsWith(".MSG", StringComparison.OrdinalIgnoreCase) ? e.Name[..^4] : e.Name).Order(StringComparer.Ordinal).ToList();
            var lines = new List<string>();
            var quickstart = NewGame.Quickstart(world, 1, () => 1).CurrentLocation;
            foreach (var city in new[] { 0, quickstart, 20 }.Distinct())
            {
                foreach (var hour in new[] { 12, 23 })
                {
                    var state = NewGame.Quickstart(world, 1, () => 1);
                    state.Data = world;
                    state.CurrentLocation = city;
                    state.Date = state.Date with { Hour = (short)hour };
                    var c = new CardContext(state, world, null!);
                    foreach (var name in names)
                    {
                        MessageFile file;
                        try
                        {
                            file = assets.Messages(name);
                        }
                        catch (Exception e) when (e is InvalidDataException or ArgumentException or KeyNotFoundException)
                        {
                            continue;
                        }
                        for (var k = 0; k < file.Blocks.Count; k++)
                        {
                            var b = file.Blocks[k];
                            var expanded = TextVariables.Expand(b.Text, c);
                            lines.Add($"{city}/{hour}|{name}#{k}|{Hash(expanded)}|{PixelHash(plain.CardText, b, expanded, false)}|{PixelHash(hd.CardText, b, expanded, true)}");
                        }
                    }
                }
            }
            return lines;
        }
        finally
        {
            Directory.Delete(pack, recursive: true);
        }
    }

    [Fact]
    public void EveryCardTextReadsAndDrawsAs113Did()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var lines = Lines(files);
        if (Environment.GetEnvironmentVariable("DARKLANDS_CARDTEXT_GOLDEN") is { Length: > 0 } write)
        {
            File.WriteAllLines(write, lines);
            return;
        }
        var golden = File.ReadAllLines(Path.Combine(RepoRoot(), "tests", "Darklands.Rendering.Tests", "CardTextGolden.txt"));
        Assert.True(lines.Count > 1000, $"only {lines.Count} blocks");
        Assert.Equal(golden.Length, lines.Count);
        var differ = golden.Zip(lines).Where(p => p.First != p.Second).Select(p => $"1.1.3 {p.First}\nnow   {p.Second}").Take(10).ToList();
        Assert.True(differ.Count == 0, "card texts that read or draw differently from 1.1.3:\n" + string.Join("\n", differ));
    }

    /// <summary>
    /// The other half of the guard (issue 7: 1.1.3 had it wrong, so its hashes cannot catch it): in every city, by day and by night, every
    /// card text reads the same on arriving after a game loaded in the wilderness (the load's city record is the file's start, read
    /// through CityFile.Read as the load does) as on arriving in a fresh game.
    /// </summary>
    [Fact]
    public void EveryCardTextReadsTheSameAfterAWildernessLoad()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var world = WorldData.Load(files);
        var assets = new GameAssets(files);
        var blocks = new MsgFilesArchiveDecoder().Decode(files.Read("MSGFILES")).Entries
            .Select(e => e.Name.EndsWith(".MSG", StringComparison.OrdinalIgnoreCase) ? e.Name[..^4] : e.Name).Order(StringComparer.Ordinal)
            .SelectMany(name =>
            {
                try
                {
                    return assets.Messages(name).Blocks.Select((b, k) => (Name: $"{name}#{k}", b.Text)).ToList();
                }
                catch (Exception e) when (e is InvalidDataException or ArgumentException or KeyNotFoundException)
                {
                    return [];
                }
            }).ToList();
        string Read(int city, int hour, bool afterTheWilderness, string name, byte[] text)
        {
            var state = NewGame.Quickstart(world, 1, () => 1);
            state.Data = world;
            state.Date = state.Date with { Hour = (short)hour };
            if (afterTheWilderness)
            {
                state.CurrentLocation = GameState.Wilderness;
                state.City = CityFile.Read(world, GameState.Wilderness, state.City) ?? state.City;
            }
            state.CurrentLocation = city;
            return $"{city}/{hour}|{name}|{Hash(TextVariables.Expand(text, new CardContext(state, world, null!)))}";
        }
        var differ = new List<string>();
        for (var city = 0; city < Location.CityCount && city < world.CreateLocations().Count; city++)
        {
            foreach (var hour in new[] { 12, 23 })
            {
                foreach (var (name, text) in blocks.Where(b => b.Text.Contains((byte)'$')))
                {
                    if (Read(city, hour, false, name, text) is var fresh && Read(city, hour, true, name, text) != fresh)
                    {
                        differ.Add(fresh);
                    }
                }
            }
        }
        Assert.True(differ.Count == 0, $"{differ.Count} card texts read differently after a wilderness load, first: " + string.Join(", ", differ.Take(10)));
    }
}
