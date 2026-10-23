using System.Reflection;
using Darklands.Core.Battles;
using Darklands.Platform;
using Darklands.Presentation;
using Darklands.Presentation.Battles;

namespace Darklands.Battles.Tests.BattleCalls;

/// <summary>A host whose pointer, Shift and Ctrl the test sets; it draws nothing and has no keys.</summary>
internal sealed class CallHost : IHost, IVideo, IInput, IClock, IAudio
{
    private long _now;

    public IVideo Video => this;
    public IInput Input => this;
    public IAudio Audio => this;
    public IClock Clock => this;
    public bool QuitRequested => false;
    public MouseState Mouse { get; set; } = new(160, 100, MouseButtons.None);
    public bool ShiftHeld { get; set; }
    public bool CtrlHeld { get; set; }
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

/// <summary>
/// The battle screen's own state of the mouse handler paired with the original's memory: the pointer and its button, the releases the driver counted
/// and the one waiting (DS:C62A), the keyboard's hover and click flags (DS:D728, DS:D73B), the hot spots of the last drawn frame (DS:E0A1 of them: who
/// DS:D848, left DS:D8F1, right DS:D8D7, top DS:D8BD, bottom DS:D8A3) and the view's origin (DS:C26C, DS:C293).
/// </summary>
internal static class ScreenOverlay
{
    public const int MouseX = 0x6EEC, MouseY = 0x6EEE, MouseButtonsAt = 0x6EF0, Releases = 0xC62A, KeyboardHover = 0xD728, KeyboardClick = 0xD73B;
    public const int HotSpotCount = 0xE0A1, HotWho = 0xD848, HotLeft = 0xD8F1, HotRight = 0xD8D7, HotTop = 0xD8BD, HotBottom = 0xD8A3, CameraX = 0xC26C, CameraY = 0xC293;

    private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static int Word(byte[] m, int at) => m[at] | m[at + 1] << 8;

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Any)!.GetValue(target)!;

    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, Any)!.SetValue(target, value);

    /// <summary>The view's origin only (the cell the screen tiles of the last redraw are counted from).</summary>
    public static void Camera(BattleScreen screen, MemoryPair image)
    {
        var ds = image.Ds;
        if (Word(ds, CameraX) % 32 != 0 || Word(ds, CameraY) % 16 != 0)
        {
            throw new BattleCallsTests.SkipJob("the view origin is not a cell");
        }
        var view = Field<BattlefieldView>(screen, "_view");
        view.CentreColumn = (short)Word(ds, CameraX) / 32;
        view.CentreRow = (short)Word(ds, CameraY) / 16;
    }

    /// <param name="driverReleases">The left-button releases the mouse driver has counted since the handler last asked (the original's INT 33h function 6).</param>
    public static void Apply(BattleScreen screen, CallHost host, MemoryPair image, int driverReleases, bool ctrl)
    {
        var ds = image.Ds;
        if (Word(ds, CameraX) % 32 != 0 || Word(ds, CameraY) % 16 != 0)
        {
            throw new BattleCallsTests.SkipJob("the view origin is not a cell");
        }
        var x = (short)Word(ds, MouseX);
        var y = (short)Word(ds, MouseY);
        host.Mouse = new MouseState(x, y, (ds[MouseButtonsAt] & 1) != 0 ? MouseButtons.Left : MouseButtons.None);
        host.CtrlHeld = ctrl;
        var view = Field<BattlefieldView>(screen, "_view");
        view.CentreColumn = (short)Word(ds, CameraX) / 32;
        view.CentreRow = (short)Word(ds, CameraY) / 16;
        SetField(screen, "_releases", (int)(short)Word(ds, Releases));
        SetField(screen, "_releaseX", x);
        SetField(screen, "_releaseY", y);
        SetField(screen, "_keyboardHover", ds[KeyboardHover] != 0);
        SetField(screen, "_keyboardClick", ds[KeyboardClick] != 0);
        if (ds[Releases] != 0 || ds[Releases + 1] != 0)
        {
            // releases already counted: the place is the one the driver's answer kept in the buffer DS:C4FE names (+4, +6)
            var buffer = Word(ds, 0xC4FE);
            SetField(screen, "_releaseX", (int)(short)Word(ds, buffer + 4));
            SetField(screen, "_releaseY", (int)(short)Word(ds, buffer + 6));
        }
        var ui = Field<GameUi>(screen, "_ui");
        SetField(ui, "_leftReleases", driverReleases);
        SetField(ui, "_lastReleaseX", x);
        SetField(ui, "_lastReleaseY", y);
        HotSpots(screen, image);
    }

    /// <summary>The hot spots of the last drawn frame only.</summary>
    public static void HotSpots(BattleScreen screen, MemoryPair image)
    {
        var ds = image.Ds;
        var spots = Field<List<(int Who, int Left, int Top, int Right, int Bottom, int MapY)>>(screen, "_hotSpots");
        spots.Clear();
        for (var k = 0; k < Math.Min((int)ds[HotSpotCount], 13); k++)
        {
            var who = (sbyte)ds[HotWho + k];
            if (who is < 0 or >= Combatant.Count)
            {
                throw new BattleCallsTests.SkipJob($"hot spot {k} names combatant {who}");
            }
            var record = ImageOverlay.Combatants + ImageOverlay.Record * who;
            // the original's x bounds are the view's (it tests mouse x - 0x3C, R014+0xB75E); the rewrite keeps screen pixels
            spots.Add((who, (short)Word(ds, HotLeft + 2 * k) + BattlefieldView.Left, (short)Word(ds, HotTop + 2 * k), (short)Word(ds, HotRight + 2 * k) + BattlefieldView.Left,
                (short)Word(ds, HotBottom + 2 * k), (short)Word(ds, record + 6)));
        }
    }

    /// <summary>The handler's results in the screen: the releases still waiting and the keyboard's flags.</summary>
    public static void Outputs(BattleScreen screen, BattleCallsTests.Context c)
    {
        void Put(int at, string name, int value, int width)
        {
            for (var b = 0; b < width; b++)
            {
                c.Outputs[at + b] = b == 0 ? name : name + " high";
                c.Actual[at + b] = (byte)(value >> (8 * b));
            }
        }
        Put(Releases, "releases waiting", Field<int>(screen, "_releases"), 2);
        Put(KeyboardHover, "keyboard hover", Field<bool>(screen, "_keyboardHover") ? 1 : 0, 1);
        Put(KeyboardClick, "keyboard click", Field<bool>(screen, "_keyboardClick") ? 1 : 0, 1);
    }
}
