using System.Text.Json.Nodes;
using Darklands.Platform;
using Darklands.Presentation.Views;

namespace Darklands.Host.Sdl.Tests;

/// <summary>The settings screen's host side (1.2): its key, the settings file written as the launcher writes it, the packs told apart.</summary>
public sealed class SettingsScreenHostTests
{
    private static SdlNative.SdlEvent Key(uint type, int scancode, int mod)
    {
        var e = new SdlNative.SdlEvent { Type = type };
        e.Keyboard.Type = type;
        e.Keyboard.Scancode = scancode;
        e.Keyboard.Mod = (ushort)mod;
        return e;
    }

    [Fact]
    public void AltTOpensItAndCtrlOrTAloneDoNot()
    {
        Assert.True(SdlHost.IsSettingsKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeT, 0x0100)));
        Assert.True(SdlHost.IsSettingsKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeT, 0x0200)));
        Assert.False(SdlHost.IsSettingsKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeT, 0)));
        Assert.False(SdlHost.IsSettingsKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeT, 0x0100 | 0x0040)));
        Assert.False(SdlHost.IsSettingsKey(Key(SdlNative.EventKeyUp, SdlNative.ScancodeT, 0x0100)));
        Assert.False(SdlHost.IsSettingsKey(Key(SdlNative.EventKeyDown, SdlNative.ScancodeF9, 0x0100)));
        // inside the screen Alt-T is the game key it closes with
        Assert.Equal(Keys.AltT, SdlKeyMap.ToGameKey(SdlNative.ScancodeT, 't', 0x0100));
    }

    [Fact]
    public void ChangesGoIntoTheSettingsFileKeepingItsOtherLines()
    {
        var folder = Directory.CreateTempSubdirectory("dk-settings-screen").FullName;
        try
        {
            var path = Path.Combine(folder, "darklands.settings.json");
            Assert.True(SettingsFile.TryWriteTemplate(path, out _));
            var before = File.ReadAllLines(path).Length;
            var log = new List<string>();
            var screen = new SettingsScreen(null!, path, folder, log.Add);
            screen.Write(new Dictionary<string, JsonNode?> { ["interface.mode"] = "classic", ["display.scale"] = 2, ["display.resolution"] = "" });
            screen.Write(new Dictionary<string, JsonNode?> { ["rules.preset"] = "original" });
            var load = SettingsFile.Load(path);
            Assert.Equal("classic", load.Settings.InterfaceMode);
            Assert.Equal(2, load.Settings.Scale);
            Assert.Equal("original", load.Settings.RulesPreset);
            Assert.Equal(before, File.ReadAllLines(path).Length);
            Assert.All(log, line => Assert.StartsWith("Settings: saved the settings into", line, StringComparison.Ordinal));
            // no file: one is made from the template first
            var fresh = Path.Combine(folder, "new.settings.json");
            new SettingsScreen(null!, fresh, folder, log.Add).Write(new Dictionary<string, JsonNode?> { ["display.cleanDrawing"] = true });
            Assert.Equal(true, SettingsFile.Load(fresh).Settings.CleanDrawing);
            // none at all: said, nothing written
            new SettingsScreen(null!, null, folder, log.Add).Write(new Dictionary<string, JsonNode?> { ["display.cleanDrawing"] = true });
            Assert.Contains(log, line => line.Contains("no settings file", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void PacksAreToldApartAsTheGameReadsThem()
    {
        var folder = Directory.CreateTempSubdirectory("dk-settings-packs").FullName;
        try
        {
            var fonts = Directory.CreateDirectory(Path.Combine(folder, "fonts-pack")).FullName;
            File.WriteAllText(Path.Combine(fonts, "pack.json"), "{\"fonts\": true}");
            var language = Directory.CreateDirectory(Path.Combine(folder, "language-pack")).FullName;
            File.WriteAllText(Path.Combine(language, "pack.json"), "{\"language\": \"de\"}");
            var empty = Directory.CreateDirectory(Path.Combine(folder, "empty")).FullName;
            var screen = new SettingsScreen(null!, null, folder, _ => { });
            Assert.Equal(SettingsPackKind.Fonts, screen.KindOf(fonts));
            Assert.Equal(SettingsPackKind.Language, screen.KindOf(language));
            Assert.Equal(SettingsPackKind.None, screen.KindOf(empty));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
