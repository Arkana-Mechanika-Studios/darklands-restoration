using Darklands.Cards;
using Darklands.Content.Formats.Battles;
using Darklands.Core;
using Darklands.Core.Battles;
using Darklands.Core.Battles.Generation;
using Darklands.Core.Battles.Tactics;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Platform;
using Darklands.Presentation.Graphics;

namespace Darklands.Presentation.Battles;

// The battle screen: the tester's traces and the oracle's injected timings (environment variables read by tools/oracle). The loop and the set-up are in BattleScreen.cs.
public sealed partial class BattleScreen
{

    /// <summary>Sets the tester's traces and the oracle's feeds above from the environment (the hosts call it through
    /// <see cref="TesterHooks.Apply"/> before the game starts); each is what its variable gave when the screen read it itself.</summary>
    internal static void FromEnvironment(Func<string, string?> environment)
    {
        PassTrace = environment("DARKLANDS_BATTLE_TRACE") is null ? null : Console.WriteLine;
        RecordTrace = environment("DARKLANDS_BATTLE_TRACE") == "records";
        OneTickPerPass = environment("DARKLANDS_BATTLE_TICK_PER_PASS") is not null;
        SetUpTicks = ParseSetUpTicks(environment("DARKLANDS_BATTLE_SETUP_TICKS"));
        LootTicks = ParseClockJumps(environment("DARKLANDS_BATTLE_LOOT_TICKS"));
        ClockJumps = ParseClockJumps(environment("DARKLANDS_BATTLE_CLOCK_JUMPS"));
        ClockRises = ParseClockRises(environment("DARKLANDS_BATTLE_CLOCK_JUMPS"));
        RegainRises = ParseClockRises(environment("DARKLANDS_BATTLE_REGAIN_RISES"));
        SheetCallbacks = ParseSheetCallbacks(environment("DARKLANDS_BATTLE_SHEET_CALLBACKS"));
        MenuCallbacks = ParseSheetCallbacks(environment("DARKLANDS_BATTLE_MENU_CALLBACKS"));
        BoxCallbacks = ParseSheetCallbacks(environment("DARKLANDS_BATTLE_BOX_CALLBACKS"));
        NoticeCallbacks = ParseSheetCallbacks(environment("DARKLANDS_BATTLE_NOTICE_CALLBACKS"));
        ResumeClocks = string.IsNullOrEmpty(environment("DARKLANDS_BATTLE_RESUME_CLOCKS")) ? null
            : new(environment("DARKLANDS_BATTLE_RESUME_CLOCKS")!.Split(';').Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)));
        PanelTrace = environment("DARKLANDS_PANEL_TRACE") is null ? null : Console.WriteLine;
    }
    /// <summary>A debugging aid: a line at the head of each pass, the combat clock and the random state (DARKLANDS_BATTLE_TRACE).</summary>
    public static Action<string>? PassTrace { get; set; }

    /// <summary>A debugging aid: called at the head of each pass with the combat clock (after the end test), and with -1 when the loop ends
    /// (the headless host's still: step, which gives the tester's key of a battle's start pause after as many passes at one clock as the
    /// original was given it).</summary>
    public static Action<int>? PassHook { get; set; }

    /// <summary>DARKLANDS_BATTLE_TRACE=records (the battle tester): each pass's line also holds the combatants' records and tables.</summary>
    public static bool RecordTrace { get; set; }

    /// <summary>" cbt &lt;13 records, hex&gt; q &lt;queued endurance, strength per combatant&gt; ar &lt;attack rates&gt;", as the tester reads them in
    /// the original's memory.</summary>
    private static string CombatantDump(Battle battle)
    {
        var records = new byte[Combatant.Count * Darklands.Persistence.CombatantRecords.Size];
        var queued = new byte[Combatant.Count * 2];
        var rates = new byte[Combatant.Count];
        for (var i = 0; i < Combatant.Count; i++)
        {
            var c = battle[i];
            Darklands.Persistence.CombatantRecords.Write(c, records.AsSpan(i * Darklands.Persistence.CombatantRecords.Size));
            queued[2 * i] = (byte)c.QueuedEndurance;
            queued[2 * i + 1] = (byte)c.QueuedStrength;
            rates[i] = c.AttackRate;
        }
        return $" cbt {Convert.ToHexString(records)} q {Convert.ToHexString(queued)} ar {Convert.ToHexString(rates)}";
    }

    /// <summary>A debugging aid: exactly one timer callback of the 60 Hz clock a pass of the loop, whatever the time (DARKLANDS_BATTLE_TICK_PER_PASS),
    /// as tools/oracle/lockstep.py runs the original's battle.</summary>
    public static bool OneTickPerPass { get; set; }

    /// <summary>A debugging aid: the clock ticks each battle's set-up takes (stages 1 to 4, see ITacticalView.SetUpTime), in place of
    /// the time that passed, battle after battle ("704,41,1,0;650,40,0"; the last entry goes on; the optional fourth number is the part of the
    /// third that comes before the activations check's clock is read, R014+0x0332), as tools/oracle/lockstep.py measures them
    /// in the original (DARKLANDS_BATTLE_SETUP_TICKS).</summary>
    public static Queue<(int Loads, int FirstFrame, int Late, int BeforeActivations)>? SetUpTicks { get; set; }

    private static Queue<(int, int, int, int)>? ParseSetUpTicks(string? text) => text is null ? null
        : new(text.Split(';').Select(p => p.Split(',')).Select(p => (int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
            p.Length > 2 ? int.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture) : 0,
            p.Length > 3 ? int.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture) : 0)));

    /// <summary>A debugging aid: the timer callbacks of the original's loot screens, by the combat clock it was opened at ("2731,7"), as
    /// tools/oracle/lockstep.py takes them from the original's clock jumps (DARKLANDS_BATTLE_LOOT_TICKS): the end test R014+0x08FE runs
    /// fight-recovery R014+0x0DEE before the loot screen R040, during which the timer root 047C:000A counts the clock on.</summary>
    public static Dictionary<int, int>? LootTicks { get; set; }

    /// <summary>A debugging aid: timer callbacks that came within a pass beyond its one, by the combat clock at the pass's head ("1382,69;1452,67":
    /// the clock was 1382 at the head, 69 more callbacks came during the pass), as tools/oracle/battle_oracle.py measures them in the original
    /// (DARKLANDS_BATTLE_CLOCK_JUMPS): the original's emulated time passes during a pass that computes for long (R014+0x4BFA..0x66FE
    /// in the wild fight), and the timer callbacks of that time count the clock on.</summary>
    public static Dictionary<int, int>? ClockJumps { get; set; }

    private static Dictionary<int, int>? ParseClockJumps(string? text) => string.IsNullOrEmpty(text) ? null
        : text.Split(';').Select(p => p.Split(',')).ToDictionary(p => int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            p => int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>A debugging aid: the same jumps' part that came between the pass's random draws ("1452,67,4:4,5:2": before the pass's 4th draw
    /// (counted from 0 at the pass's head) 4 callbacks came, before the 5th 2 more, the rest after the pass), as battle_oracle.py measures them:
    /// the draws that read the clock (a re-path time) see the clock the original had then (DARKLANDS_BATTLE_CLOCK_JUMPS). The keys -1 and -2 are the
    /// callbacks that came before the tick's scheduler (R014+0xF418's 1DB7:0D36) and melee exchanges (R014+0x2A4A), which test the clock against the
    /// re-path and engage times before any draw.</summary>
    public static Dictionary<int, Dictionary<int, int>>? ClockRises { get; set; }

    private static Dictionary<int, Dictionary<int, int>>? ParseClockRises(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        var all = new Dictionary<int, Dictionary<int, int>>();
        foreach (var entry in text.Split(';').Select(p => p.Split(',')).Where(p => p.Length > 2))
        {
            all[int.Parse(entry[0], System.Globalization.CultureInfo.InvariantCulture)] = entry.Skip(2).Select(r => r.Split(':'))
                .ToDictionary(r => int.Parse(r[0], System.Globalization.CultureInfo.InvariantCulture), r => int.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        return all;
    }

    /// <summary>The key of <see cref="RegainRises"/> for the callbacks after the regain's loop, before fight-input (lockstep.REGAIN_AFTER).</summary>
    private const int RegainAfter = 9;

    /// <summary>A debugging aid: the timer callbacks that came inside the endurance regain's loop (R014+0x45AE reads the clock anew for each
    /// member), by the clock at its first read ("2097,0,1:1": one callback before member 1's read;
    /// the second number is unused, as in the jumps' format; slot RegainAfter: after the loop), as tools/oracle/timer_marks.py finds them in
    /// the original (DARKLANDS_BATTLE_REGAIN_RISES); lockstep.py takes them off the ticks of the pass before.</summary>
    public static Dictionary<int, Dictionary<int, int>>? RegainRises { get; set; }

    /// <summary>A debugging aid: for each combat sheet of the run in turn, the timer callbacks that came while it was open (R022+0x0066 ..
    /// R022+0x03C6, DS:07D3 held) and those that came after it up to the next pass's head ("2310,9;40,0"), as tools/oracle/lockstep.py
    /// measures them in the original (DARKLANDS_BATTLE_SHEET_CALLBACKS); the C# host has no time of its own there in OneTickPerPass.</summary>
    public static Queue<(int During, int After)>? SheetCallbacks { get; set; }

    private static Queue<(int, int)>? ParseSheetCallbacks(string? text) => string.IsNullOrEmpty(text) ? null
        : new(text.Split(';').Select(p => p.Split(',')).Select(p => (int.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            p.Length > 1 ? int.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture) : 0)));

    /// <summary>A debugging aid: for each run of the menu bar in a battle in turn, the timer callbacks that came while it ran (root 0000:0000 with
    /// the bar up, the clock held) and after it up to the next pass, as tools/oracle/lockstep.py measures them in the original
    /// (DARKLANDS_BATTLE_MENU_CALLBACKS, "1420,54;..."); the C# host has no time of its own there in OneTickPerPass.</summary>
    public static Queue<(int During, int After)>? MenuCallbacks { get; set; }

    /// <summary>A debugging aid: the same for each message box of a hot key in a battle (Alt-D / F / M / C: notice-display's wait of 0x3C ticks of the
    /// timer's tick byte), as tools/oracle/lockstep.py measures them in the original (DARKLANDS_BATTLE_BOX_CALLBACKS).</summary>
    public static Queue<(int During, int After)>? BoxCallbacks { get; set; }

    /// <summary>A debugging aid: the other notice boxes of a battle pass (ShowMessage), as the tester measures them in the original
    /// (DARKLANDS_BATTLE_NOTICE_CALLBACKS): paused, the callbacks after the box up to the next pass, which the host has no time for.</summary>
    public static Queue<(int During, int After)>? NoticeCallbacks { get; set; }

    /// <summary>A debugging aid: for each battle taken up from a saved game in turn, the combat clock of the original's first pass after it
    /// ("5720;...", an entry for every battle after the first): the restore R003+0x0CE8 reads the clock from the battle file and the timer callback root 047C:000A counts it on while
    /// the levels are exploded and the set-up loads its files (emulated time), as tools/oracle/lockstep.py reads it from the original's trace
    /// (DARKLANDS_BATTLE_RESUME_CLOCKS). The clock moves there after the set-up.</summary>
    public static Queue<int>? ResumeClocks { get; set; }

    /// <summary>A tester's trace (DARKLANDS_PANEL_TRACE): each row drawn, with the combat clock and its note.</summary>
    public static Action<string>? PanelTrace { get; set; }
}
