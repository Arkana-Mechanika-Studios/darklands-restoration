using Darklands.Cards;
using Darklands.Core;
using Darklands.Core.Characters;
using Darklands.Core.Economy;
using Darklands.Presentation.Battles;

namespace Darklands.Presentation;

/// <summary>
/// The battle the game opens for a card (<see cref="IGameScreens.Battle"/>): builds the
/// battlefield for the request, runs the tactical screen until the battle ends, hands the loot
/// over and returns the result code (0 won, 2 fled, 3 lost, 4 surrendered, 5 all down …).
/// </summary>
// orig: battle_run R002+0x0000 (0E76:0000) draft
public sealed class BattleScreens
{
    private readonly GameUi _ui;
    private readonly Func<GameState> _state;
    private readonly Action<IList<ItemStack>, Money>? _loot;
    private BattleAssets? _assets;

    /// <param name="ui">The screen and the player.</param>
    /// <param name="state">The game in progress (party, random generator, place, date, difficulty).</param>
    /// <param name="loot">Called with the loot of a won battle (the game's loot screen, <see cref="IGameScreens.Loot"/>); null keeps it in <see cref="LastLoot"/>.</param>
    public BattleScreens(GameUi ui, Func<GameState> state, Action<IList<ItemStack>, Money>? loot = null)
    {
        _ui = ui;
        _state = state;
        _loot = loot;
    }

    /// <summary>The battle screen of the battle running (or of the last one); for tests and the headless host.</summary>
    public BattleScreen? Current { get; private set; }

    /// <summary>A battle is running.</summary>
    public bool Fighting { get; private set; }

    /// <summary>The terrain code under the party on the world map (DS:D843); by default the game state's PartyTerrain.</summary>
    public Func<int>? WorldTerrain { get; set; }

    /// <summary>What the last battle left as loot (items and money).</summary>
    public (IList<ItemStack> Items, Money Money)? LastLoot { get; private set; }

    /// <summary>The cleared-level memory persists between battles at one place (it is the game state's, saved with the game).</summary>
    public Core.Battles.Generation.BattlefieldMemory Memory => _state().Battlefield;

    /// <summary>
    /// Runs one battle. <see cref="BattleRequest.Battlefield"/> is the battlefield type (the
    /// card's <c>battle_enemy</c>, or <c>run_battle</c>'s first argument), <see cref="BattleRequest.Seed"/>
    /// the battle seed, the groups the three (enemy type, quality, number) specs.
    /// </summary>
    public int Battle(BattleRequest request)
    {
        // the battle tester (tools/oracle/battle_oracle.py): DARKLANDS_BATTLE_REQUEST="mode,battlefield,seed,a,b,c,a,b,c,a,b,c"
        // replaces the battle's arguments, as the tester rewrites the original's at R002+0x0000's entry
        if (Environment.GetEnvironmentVariable("DARKLANDS_BATTLE_REQUEST") is { Length: > 0 } given)
        {
            var v = given.Split(',').Select(t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            request = request with { Mode = v[0], Battlefield = v[1], Seed = v[2], Groups = [(v[3], v[4], v[5]), (v[6], v[7], v[8]), (v[9], v[10], v[11])] };
        }
        var screen = new BattleScreen(_ui, Assets, _state(), this);
        Current = screen;
        Memory.BattlesFought++;
        Fighting = true;
        int result;
        try
        {
            result = screen.Run(request);
        }
        finally
        {
            Fighting = false;
        }
        LastResultCard = screen.ResultCard;
        // orig: R002+0x0000 at its end: `combat_flags &= 0xFE10` (bits 1, 2, 4, 8, 0x20, 0x40 and 0x80 cleared after every battle); tests: RevisitTests
        _state().BattleOrders.Flags &= 0xFE10;
        return result;
    }

    /// <summary>The character sheet of a member (F1..F5 and the portraits during a battle); null: none.</summary>
    public Action<int>? CharacterSheet { get; set; }

    /// <summary>The party information screen (Party, Party Info / F6 during a battle); null: none.</summary>
    public Action? PartyInfo { get; set; }

    /// <summary>Save Game inside a battle (Alt-S and the Game menu while the battle offers it): the game's save of the battle running.</summary>
    public Action<Core.Battles.Tactics.TacticalBattle>? Save { get; set; }

    /// <summary>Load Saved Game inside a battle (Alt-L and the Game menu): the game's load; a loaded game leaves the battle.</summary>
    public Action? Load { get; set; }

    /// <summary>DS:0FD8 " finished" (DS:11F2): the Orders entry "&lt;name&gt; finished".</summary>
    public const int FinishedText = 0x0FD8;

    /// <summary>
    /// In a battle, for a party member who left the battlefield through an exit (cbt_floor DS:A6B4 = 0x9D, −99): R022+0x04E4's
    /// "You can't view &lt;name&gt; at this time." for 150 ticks, and true; false otherwise (no battle, or the member is still there).
    /// The character sheet (R031+0x0124) and the loot screen (R040+0x0C22) ask it before showing a member.
    /// </summary>
    // orig: R031+0x0124's `if (DS:907A == 1 && cbt_floor[m] == 0x9D) thunk 09C0:1D51(m)` (R031+0x0644..0x064B, 0x07A4..0x07AB,
    //   0x0837..0x083E: `cmp byte [di - 0x594C], 0x9D`, the byte table; portrait click, F1..F5, Enter on a member box);
    //   R022+0x04E4 matched: sprintf("%Fs%s%Fs", 286B:0212, name, 286B:0222), root 0000:24BC(buf, 0x96)
    public bool CannotViewMember(int member)
    {
        if (!Fighting || Current?.Tactics is not { } tb || member is < 0 or >= Core.Battles.Combatant.PartySlots
            || tb.Battle[member].Floor != Core.Battles.BattleEnd.LeftThroughExit)
        {
            return false;
        }
        TellCannotView(tb, member);
        return true;
    }

    /// <summary>
    /// The loot screen's D for party slot <paramref name="slot"/> (R040+0x0DB4..0x0DC8): true (after the "can't view" message) when the
    /// slot is to be skipped as gone from the battlefield.
    /// </summary>
    // orig: R040+0x0DB3..0x0DC8: the test (BattleEnd.LootSkips), then DS:8DC4 = slot, thunk 09C0:1D51(slot) and the next slot; no
    //   DS:907A test (only the battle opens the loot screen)
    public bool LootSkipsMember(int slot)
    {
        if (!Fighting || Current?.Tactics is not { } tb || !Core.Battles.BattleEnd.LootSkips(tb.Battle, slot))
        {
            return false;
        }
        TellCannotView(tb, slot);
        return true;
    }

    private void TellCannotView(Core.Battles.Tactics.TacticalBattle tb, int who) =>
        new MessageBox(_ui).Show(CannotView(tb.Battle[who].Member?.Name ?? ""), 0x96 * 1000 / 60);

    /// <summary>"You can't view &lt;name&gt; at this time." (R136+0x7E72 + name + R136+0x7E82, R022 04E4).</summary>
    internal string CannotView(string name) => FarText(0x0212) + name + FarText(0x0222);

    /// <summary>"Do you really want to surrender?" (a literal of R022 03E4, DS:23B2).</summary>
    internal string SurrenderQuestion => Assets.Executable.String(0x23B2);

    /// <summary>"Select first" … "Select fourth" (R022 051C: "Select %Fs" with the ordinals R136+0x7EC7[k]).</summary>
    internal string SelectOrdinal(int k)
    {
        var p = Assets.Executable.Far(0x286B, 0x0267 + 4 * k, 4);
        return "Select " + FarText(p[0] | p[1] << 8);
    }

    /// <summary>"BattleResults - victory" … (battle_run, the box after the battle; result 8 "leave").</summary>
    // orig: battle_run R002+0x0000 (0E76:0000) draft (the switch on DS:A893)
    internal static string ResultText(int result) => "BattleResults - " + result switch
    {
        0 => "victory",
        2 => "retreat",
        3 => "defeat",
        4 => "surrender",
        5 => "all dead",
        8 => "leave",
        _ => "",
    };

    internal string FarText(int offset)
    {
        var bytes = Assets.Executable.Far(0x286B, offset, 60);
        var end = bytes.IndexOf((byte)0);
        return System.Text.Encoding.Latin1.GetString(end < 0 ? bytes : bytes[..end]);
    }

    internal BattleAssets Assets => _assets ??= new BattleAssets(_ui.Assets.Files);

    /// <summary>The last battlefield and the place it belongs to (modes 1..3 go back onto it).</summary>
    public Core.Battles.Generation.GeneratedBattlefield? LastField { get; internal set; }

    /// <summary>The generator globals of the whole program (DS:E051 …), kept across battles and loads.</summary>
    public Core.Battles.Generation.GenerationProcessMemory GenerationMemory { get; } = new();

    public int LastFieldLocation { get; internal set; } = -1;

    /// <summary>DS:C866 / DS:C886: the battle view's centre cell, a global the next battle's first view is drawn around (unless its
    /// placement sets it).</summary>
    // C: (0, 0) when a game is begun or loaded from the opening screen at the program's start: DS:C866/C886 are in the BSS the start-up clears
    // (root 06A1:009D..00B7); a game loaded after a battle of an earlier one keeps the last centre (DarklandsGame._battleViewCentre).
    public (int Column, int Row) ViewCentre { get; internal set; }

    /// <summary>seenpos (20A5:79A9): where each combatant last saw each other one near its path, a program global no battle start clears
    /// (only a path found writes it, R014+0xF568): the last battle's (or the battle a load left) arrays, null before any battle.</summary>
    public Darklands.Core.Battles.MapPoint[][]? SeenPositions { get; internal set; }

    /// <summary>The cursor keys' state (DS:D728, DS:D73B, DS:D7FE, DS:D947, DS:D945), program globals no battle start clears: each battle
    /// works on this one (DarklandsGame keeps it across loads; a battle file restored puts its bytes back).</summary>
    public Darklands.Core.Battles.Tactics.CursorKeyState CursorKeys { get; internal set; } = new();

    /// <summary>The view centre now: the running battle's while one runs, else <see cref="ViewCentre"/>.</summary>
    public (int Column, int Row) CurrentViewCentre => Current?.ViewCentre ?? ViewCentre;

    /// <summary>The card the last battle left for (battle_args: the level's card for its result).</summary>
    public int LastResultCard { get; private set; }

    /// <summary>The loot of a pile or of the victory: to the loot screen, or kept in <see cref="LastLoot"/>.</summary>
    internal void TakeLoot(IList<ItemStack> items, Money money)
    {
        LastLoot = (items, money);
        _loot?.Invoke(items, money);
    }
}
