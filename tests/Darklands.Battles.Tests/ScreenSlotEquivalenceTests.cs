using Darklands.Content;
using Darklands.Localization;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Graphics;

namespace Darklands.Battles.Tests;

/// <summary>
/// The classic screens' declared slots (the 1.2 view registry, step 9) answer as the constants they replaced: for every pixel of the
/// screen the same hit, and the drawing from a slot the same pixels as the original's. A pack's slot of the same kind is used, any
/// other is not (the screen's own then).
/// </summary>
public sealed class ScreenSlotEquivalenceTests
{
    private static void EveryPixel(Func<int, int, int> original, SlotLayout slot)
    {
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                var expected = original(x, y);
                var got = slot.KindOrText == SlotKinds.List ? slot.RowAt(x, y) : slot.Contains(x, y) ? 0 : -1;
                if (got != expected)
                {
                    Assert.Fail($"({x}, {y}): the original's {expected}, the slot's {got}");
                }
            }
        }
    }

    [Fact]
    public void TheMarchingOrdersRowsAreItsReleaseTest()
    {
        // orig: R022+0x051C: a release at x < 0x3C picks row y / 40
        EveryPixel((x, y) => x < PartyPanel.Width ? y / PartyPanel.RowHeight : -1, MarchingOrderLayout.PartyRows.Layout);
    }

    [Fact]
    public void TheLoadScreensSlotsAreItsTests()
    {
        // orig: R037+0x84BA: buttons 0x44 < x < 0xc6, 0x2d < y < 0x5a, button (y - 0x2d) / 15; list 0xc3 < x < 0x140, 0x14 < y < 0xa5,
        //   row (y - 0x14) / 16
        EveryPixel((x, y) => x > 0x44 && x < 0xC6 && y > 0x2D && y < 0x5A ? 0 : -1, LoadGameLayout.Buttons.Layout);
        EveryPixel((x, y) => x > 0xC3 && x < 0x140 && y > 0x14 && y < 0xA5 ? 0 : -1, LoadGameLayout.List.Layout);
        for (var y = -50; y < 250; y++)
        {
            Assert.Equal((y - 0x2D) / 15, LoadGameLayout.ButtonRows.Layout.RowOf(y));
            Assert.Equal((y - 0x14) / 16, LoadGameLayout.ListRows.Layout.RowOf(y));
            Assert.Equal(Math.Max(0, y - 0x15) / 10, LoadGameLayout.ComfortRows.Layout.RowOf(Math.Max(y, 0x15)));
        }
        // the comfort list with the panel at 0xc3: 0xc3 < x < 0x13b, 0x12 < y < 0xb0
        EveryPixel((x, y) => x > 0xC3 && x < 0x13B && y > 0x12 && y < 0xB0 ? 0 : -1, LoadGameLayout.ComfortList.Layout);
    }

    [Fact]
    public void TheRostersSlotsAreItsTests()
    {
        // orig: R038+0x0000: buttons 0x46 < x < 0xc8, 0x2d < y < 0xa5, (y - 0x2d) / 15; names 0xc5 < x < 0x138, 0x14 < y < 0xb4, (y - 0x14) / 8
        EveryPixel((x, y) => x > 0x46 && x < 0xC8 && y > 0x2D && y < 0xA5 ? 0 : -1, PartyRosterLayout.Buttons.Layout);
        EveryPixel((x, y) => x > 0xC5 && x < 0x138 && y > 0x14 && y < 0xB4 ? 0 : -1, PartyRosterLayout.Names.Layout);
        for (var y = 0; y < Screen.Height; y++)
        {
            Assert.Equal((y - 0x2D) / 15, PartyRosterLayout.ButtonRows.Layout.RowOf(y));
            Assert.Equal((y - 0x14) / 8, PartyRosterLayout.NameRows.Layout.RowOf(y));
        }
    }

    [Fact]
    public void TheSaintScreensSlotsAreItsTests()
    {
        // orig: R034: the box 250 < x < 310, 142 < y < 192, (y - 142) / 9; the menu 148 < x < 318, 158 < y < 190, (y - 158) / 8;
        //   the column x < 60, y / 40
        EveryPixel((x, y) => x > 250 && x < 310 && y > 142 && y < 192 ? 0 : -1, SaintPrayerLayout.MemberBox.Layout);
        EveryPixel((x, y) => x > 148 && x < 318 && y > 158 && y < 190 ? 0 : -1, SaintPrayerLayout.Menu.Layout);
        EveryPixel((x, y) => x < 60 ? y / 40 : -1, SaintPrayerLayout.PartyRows.Layout);
        for (var y = 0; y < Screen.Height; y++)
        {
            Assert.Equal((y - 142) / 9, SaintPrayerLayout.MemberRows.Layout.RowOf(y));
            Assert.Equal((y - 158) / 8, SaintPrayerLayout.MenuRows.Layout.RowOf(y));
        }
    }

    [Fact]
    public void TheShopsSlotsAreTheirTests()
    {
        // orig: R032 / R035 / R040, the tests as written before the slots
        for (var y = 0; y < Screen.Height; y++)
        {
            for (var x = 0; x < Screen.Width; x++)
            {
                Assert.Equal(x > 0x50 && x < 0xE6 && y > 0x1B && y < 0x43 ? (y - 0x1B) / 8 : null, BuySellPage.MenuLineAt(x, y));
                Assert.Equal(x > 0x52 && x < 0x10B && y > 0x55 && y < 0x75 ? (y - 0x55) >> 3 : null, BuySellPage.UpperRowAt(x, y));
                Assert.Equal(x > 0x72 && x < 0x11C && y > 0x93 && y < 0xB3 ? (y - 0x93) >> 3 : null, BuySellPage.LowerRowAt(x, y));
                Assert.Equal(y > 0x4C && y < 0x55 && x > 0x52 && x < 0x10B, BuySellPage.OnUpperUpArrow(x, y));
                Assert.Equal(y > 0x75 && y < 0x7E && x > 0x52 && x < 0x10B, BuySellPage.OnUpperDownArrow(x, y));
                Assert.Equal(y > 0x8A && y < 0x93 && x > 0x72 && x < 0x12B, BuySellPage.OnLowerUpArrow(x, y));
                Assert.Equal(y > 0xB3 && y < 0xBC && x > 0x72 && x < 0x12B, BuySellPage.OnLowerDownArrow(x, y));
                Assert.Equal(x < 0x3C ? y / 40 : null, BuySellPage.MemberAt(x, y));
            }
        }
        for (var y = -20; y < 220; y++)
        {
            Assert.Equal((y - BuySellPage.MenuY) / 8, BuySellLayout.MenuRows.Layout.RowOf(y));
        }
    }

    [Fact]
    public void TheCharacterSheetsSlotsAreItsTests()
    {
        // orig: R031+0x0124 / 0x1E42: x > 60 is the sheet (else row y / 40); a box contains x, y when X <= x <= X + W and Y <= y <= Y + H;
        //   a list's lines PanelY + 9 <= y <= PanelY + PanelH - 14, its up arrow PanelY <= y <= PanelY + 9, its down arrow
        //   PanelY + PanelH - 14 <= y <= PanelY + PanelH (all within PanelX <= x <= PanelX + PanelW); line (y - PanelY - 9) / 8
        var slots = CharacterSheetScreen.DeclaredSlots.ToDictionary(s => s.Name, s => s.Layout);
        EveryPixel((x, y) => x > 60 ? -1 : y / 40, slots["party.rows"]);
        var table = CharacterSheetScreen.BoxTable;
        for (var i = 0; i < table.Count; i++)
        {
            var b = table[i];
            EveryPixel((x, y) => b.X <= x && b.W + b.X >= x && b.Y <= y && b.Y + b.H >= y ? 0 : -1, slots[$"box.{i}"]);
            if (!b.IsList)
            {
                continue;
            }
            bool InPanel(int x) => b.PanelX <= x && b.PanelW + b.PanelX >= x;
            EveryPixel((x, y) => InPanel(x) && b.PanelY <= y && b.PanelH + b.PanelY >= y ? 0 : -1, slots[$"box.{i}.panel"]);
            EveryPixel((x, y) => InPanel(x) && b.PanelY + 9 <= y && b.PanelH + b.PanelY - 14 >= y ? 0 : -1, slots[$"box.{i}.lines"]);
            EveryPixel((x, y) => InPanel(x) && b.PanelY <= y && b.PanelY + 9 >= y ? 0 : -1, slots[$"box.{i}.up"]);
            EveryPixel((x, y) => InPanel(x) && b.PanelH + b.PanelY - 14 <= y && b.PanelH + b.PanelY >= y ? 0 : -1, slots[$"box.{i}.down"]);
            for (var y = -20; y < 220; y++)
            {
                Assert.Equal((y - b.PanelY - 9) / 8, slots[$"box.{i}.rows"].RowOf(y));
            }
        }
        // the age column: at box 1's x + 0x19 (the age; "(F)" / "(M)" 0x0f to its right)
        Assert.Equal(table[1].X + 0x19, slots["age.column"].X);
    }

    [Fact]
    public void AFrameDrawnFromTheRowsIsTheOriginalsFrame()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var sprites = new GameAssets(files).CommonSprites;
        foreach (var lit in new[] { false, true })
        {
            for (var row = 0; row < 5; row++)
            {
                var (a, b) = (new Screen(), new Screen());
                PartyPanel.DrawFrame(new Canvas(a), sprites, row, lit);
                PartyPanel.DrawFrame(new Canvas(b), sprites, MarchingOrderLayout.PartyRows.Layout, row, lit);
                Assert.Equal(a.Pixels, b.Pixels);
                Assert.Equal(a.Owners, b.Owners);
                Assert.Equal(a.Texels, b.Texels);
            }
        }
    }

    [Fact]
    public void APacksSlotOfTheSameKindIsUsedAndNoOther()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var ui = new GameUi(new BattleScreenTests.SilentHost(), new GameAssets(files));
        var declared = MarchingOrderLayout.PartyRows;
        Assert.Same(declared.Layout, ui.Slot(MarchingOrderLayout.Screen, declared));
        var folder = Path.Combine(Path.GetTempPath(), "dl-slots-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "text", "de"));
            Directory.CreateDirectory(Path.Combine(folder, "interface"));
            File.WriteAllText(Path.Combine(folder, "pack.json"), """{ "language": "de" }""");
            File.WriteAllText(Path.Combine(folder, "text", "de", "ui.ftl"), "id-rw-test = Test\n");
            File.WriteAllText(Path.Combine(folder, "interface", "layout.json"),
                """{ "screens": { "MarchingOrderScreen": { "party.rows": { "kind": "list", "x": 0, "y": 10, "width": 60, "rows": 5, "rowHeight": 38 } } } }""");
            Assert.True(ui.UseLanguagePack(folder, _ => { }));
            Assert.Equal(10, ui.Slot(MarchingOrderLayout.Screen, declared).Y);
            File.WriteAllText(Path.Combine(folder, "interface", "layout.json"),
                """{ "screens": { "MarchingOrderScreen": { "party.rows": { "kind": "area", "x": 0, "y": 10, "width": 60, "height": 20 } } } }""");
            Assert.True(ui.UseLanguagePack(folder, _ => { }));
            Assert.Same(declared.Layout, ui.Slot(MarchingOrderLayout.Screen, declared));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
