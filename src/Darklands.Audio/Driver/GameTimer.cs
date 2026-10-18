using Darklands.Platform;

namespace Darklands.Audio.Driver;

/// <summary>What the game's timer calls in the sound driver.</summary>
internal interface ITimerClient
{
    /// <summary>Once per game tick; returns 1 to want the fast timer, -1 to give it up, 0 for no change.</summary>
    int FrameTick();

    /// <summary>On each interrupt of the fast timer.</summary>
    void FastTick();
}

/// <summary>
/// The game's timer interrupt as it concerns the sound driver. The game runs the PC timer at
/// the video frame rate (70 Hz in mode 13h), or <see cref="FastMultiplier"/> times faster while
/// the driver asks for it; each frame delivers the ticks of its <see cref="FrameTicker"/> (one,
/// none on every seventh frame: a 60 Hz music clock; or a measured track), and each tick calls the
/// driver's frame tick; on every interrupt of the fast timer it calls the driver's fast tick.
/// </summary>
// orig: root 047C:0066 asm - installs int 8 at 047C:01EA
// orig: root 047C:02A2 asm - measures the frame period [7EAh] in timer counts;
//   multiplier [7E8h] = (period + period/16) / 0F89h, kept when 4..6, else 5 with the 60 Hz period 4DAEh. tests: GameTimerTests
internal sealed class GameTimer(FrameTicker ticker)
{
    public GameTimer()
        : this(new FrameTicker())
    {
    }

    /// <summary>The PC timer's input clock.</summary>
    public const double PitClock = FrameTicker.PitClock;

    /// <summary>VGA mode 13h refresh: the period the game measures.</summary>
    public const int FramePeriod = FrameTicker.FramePeriod;

    // C: ([7EAh] + [7EAh] >> 4) / 0F89h with unsigned 16-bit division: 4280h gives 4.
    public static readonly int FastMultiplier = (FramePeriod + (FramePeriod >> 4)) / 0x0F89;

    private int _multiplier = 1;      // [7E6h]: interrupts per frame
    private int _countdown = 1;       // [7EEh]
    private int _period = FramePeriod; // [7E4h] / [7E2h]: the timer's divisor in effect
    private int _pendingPeriod = FramePeriod;

    /// <summary>The frames and ticks (shared with the presentation through <see cref="IGameTimer"/>).</summary>
    public FrameTicker Ticker { get; } = ticker;

    /// <summary>Called as each tick begins, before the driver's work (its number).</summary>
    public Action<long>? TickStarting { get; set; }

    /// <summary>Called after each tick (the music requests waiting for it are carried out).</summary>
    public Action<long>? Ticked { get; set; }

    /// <summary>Seconds until the next timer interrupt.</summary>
    public double InterruptInterval => _period / PitClock;

    /// <summary>True while the driver has the timer running fast.</summary>
    public bool Fast => _multiplier != 1;

    /// <summary>One timer interrupt.</summary>
    // orig: (interrupt handler) root 047C:01EA asm - dec [7EEh]; at zero reload it from
    //   [7E6h], reprogram the timer if [7E4h] changed (047C:0257) and do the frame (047C:000A); then call the
    //   driver's fast tick when [7E6h] != 1.
    // DIVERGENCE: the original waits for the vertical retrace before reprogramming the timer and re-syncs
    //   to it every 70 frames (047C:026A..0284); here the timer is exact and never drifts.
    public void Interrupt(ITimerClient driver)
    {
        if (--_countdown == 0)
        {
            _countdown = _multiplier;
            _period = _pendingPeriod;
            Frame(driver);
        }
        if (_multiplier != 1)
        {
            driver.FastTick();
        }
    }

    // orig: (frame work) root 047C:000A asm
    private void Frame(ITimerClient driver)
    {
        // DORMANT: [7D8h] != 0 calls 047C:042A first (game work, not sound).
        // C: the skip of one frame in seven (sub byte [7DAh],1 / jg) is the ticker's.
        var ticks = Ticker.NextFrame();
        for (var i = 0; i < ticks; i++)
        {
            // (030B:0508, the PAN player's tick, and the frame counters 7D0h..7D9h are the presentation's: IGameTimer.Ticks.)
            var tick = Ticker.Ticks - ticks + i + 1;
            TickStarting?.Invoke(tick);
            var request = driver.FrameTick();
            if (request < 0)
            {
                // orig: root 047C:00D7 - back to one interrupt per frame
                _multiplier = 1;
                _pendingPeriod = FramePeriod;
            }
            else if (request > 0)
            {
                // orig: root 047C:00C4 - [7E6h] = [7E8h], divisor [7EAh] / [7E8h]
                _multiplier = FastMultiplier;
                _pendingPeriod = FramePeriod / FastMultiplier; // C: unsigned 16-bit division, remainder dropped
            }
            Ticked?.Invoke(tick);
        }
    }
}
