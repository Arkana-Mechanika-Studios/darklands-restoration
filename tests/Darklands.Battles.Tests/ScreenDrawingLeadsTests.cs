using Darklands.Content;
using Darklands.Core.Characters;
using Darklands.Platform;
using Darklands.Presentation;

namespace Darklands.Battles.Tests;

/// <summary>
/// Drawing details checked against the original's screens (local/batch/c5uncertain, vis10 pixel comparisons): the prayer page's member
/// list (R034+0x0E04), the card picture held until its first text (R043+0x0A9A, DS:EE55).
/// </summary>
public sealed class ScreenDrawingLeadsTests
{
    /// <summary>A host that keeps the last screen presented.</summary>
    private sealed class ShotHost : IHost, IVideo, IInput, IClock
    {
        private long _now;

        public IVideo Video => this;
        public IInput Input => this;
        public IAudio Audio { get; } = new SilentAudio();
        public IClock Clock => this;
        public bool QuitRequested => false;
        public MouseState Mouse => new(5, 5, MouseButtons.None);
        public bool ShiftHeld => false;
        public long Milliseconds => _now;
        public byte[] Shown { get; } = new byte[Screen.Width * Screen.Height];

        public void Present(Screen screen) => screen.Pixels.CopyTo(Shown, 0);

        public void Sleep(int milliseconds) => _now += Math.Max(1, milliseconds);

        public bool TryReadKey(out int key)
        {
            key = 0;
            return false;
        }

        public void Pump()
        {
        }
    }

    [Fact]
    public void ThePrayerMemberListHoldsTheShortNamesOfThePresentMembersInSlotOrder()
    {
        var party = new Party();
        party.Members.Add(new Character { Name = "Heidi Baldur", ShortName = "Heidi", Status = CharacterStatus.Able });
        party.Members.Add(new Character { Name = "Nobody", ShortName = "Nobody", Status = CharacterStatus.Empty });
        party.Members.Add(new Character { Name = "Wilhelm Kreisler", ShortName = "Wilhelm", Status = CharacterStatus.Unconscious });
        Assert.Equal("Heidi\nWilhelm", SaintPrayerScreen.MemberListText(party));
    }

    [Fact]
    public void AHeldScreenStaysOnShowUntilItIsReleasedOrInputIsAwaited()
    {
        if (GameFolder.Files is not { } files)
        {
            return;
        }
        var host = new ShotHost();
        var ui = new GameUi(host, new GameAssets(files));
        ui.Canvas.Fill(0, 0, 10, 10, 3);
        ui.Present();
        ui.HoldScreen();
        ui.Canvas.Fill(0, 0, 10, 10, 7);
        ui.Present();
        Assert.Equal(3, host.Shown[0]);
        ui.Poll();
        Assert.Equal(7, host.Shown[0]);
    }
}
